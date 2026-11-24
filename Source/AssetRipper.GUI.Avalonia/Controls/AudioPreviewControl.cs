using System.Diagnostics;
using System.Globalization;
using AssetRipper.GUI.Avalonia.Services;
using AssetRipper.GUI.Avalonia.Theme;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace AssetRipper.GUI.Avalonia.Controls;

/// <summary>
/// Mel spectrogram, real-time RMS/Peak dBFS level meter, and native audio transport,
/// faithfully ported from vnlens-gui/src/workspace/audio_view.rs &amp; level_meter.rs.
/// </summary>
internal sealed class AudioPreviewControl : Grid, IDisposable
{
	private const float RangeDb = 100f;
	private const double LeftMargin = 28.0;
	private const double RightMargin = 94.0;
	private const double TopMargin = 16.0;
	private const double BottomMargin = 28.0;
	private const double ChannelGap = 10.0;
	private const float MeterFloorDb = -60f;

	private readonly SpectrogramCanvas _spectrogramCanvas;
	private readonly LevelMeterCanvas _levelMeterCanvas;
	private readonly Border _playButton;
	private readonly TextBlock _positionText;
	private readonly TextBlock _durationText;
	private readonly TextBlock _metaText;
	private readonly DispatcherTimer _tickTimer;

	private DecodedAudioResult? _audio;
	private PcmAudioPlayer? _player;
	private bool _playing;
	private double _position;

	public AudioPreviewControl()
	{
		RowDefinitions = new RowDefinitions("*,46");
		Margin = new Thickness(8, 0);

		_spectrogramCanvas = new SpectrogramCanvas();
		_spectrogramCanvas.SeekRequested += SeekToFraction;

		_levelMeterCanvas = new LevelMeterCanvas();

		Grid scrollContent = new()
		{
			RowDefinitions = new RowDefinitions("*,Auto"),
		};
		Grid.SetRow(_spectrogramCanvas, 0);
		Grid.SetRow(_levelMeterCanvas, 1);
		scrollContent.Children.Add(_spectrogramCanvas);
		scrollContent.Children.Add(_levelMeterCanvas);

		ScrollViewer scrollViewer = new()
		{
			Content = scrollContent,
			HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
			VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
		};
		Grid.SetRow(scrollViewer, 0);
		Children.Add(scrollViewer);

		// Bottom 46px transport bar (matching audio_view.rs lines 286-351)
		Grid transport = new()
		{
			Height = 46,
			Margin = new Thickness(8, 0),
			ColumnDefinitions = new ColumnDefinitions("Auto,12,Auto,*,Auto"),
			VerticalAlignment = VerticalAlignment.Center,
		};
		Grid.SetRow(transport, 1);

		_playButton = new Border
		{
			Width = 32,
			Height = 32,
			CornerRadius = new CornerRadius(16),
			Background = Palette.Control,
			Cursor = new Cursor(StandardCursorType.Hand),
			Child = Icons.Glyph(IconKind.Play, 14, Palette.Disabled),
		};
		GlassSurface.SetTooltip(_playButton, "Play (Space)");
		_playButton.PointerEntered += (_, _) =>
		{
			if (_audio is { Duration: > 0 })
			{
				_playButton.Background = Palette.AccentHover;
			}
		};
		_playButton.PointerExited += (_, _) =>
		{
			_playButton.Background = _audio is { Duration: > 0 } ? Palette.Accent : Palette.Control;
		};
		_playButton.PointerPressed += (_, e) =>
		{
			if (e.GetCurrentPoint(_playButton).Properties.IsLeftButtonPressed)
			{
				e.Handled = true;
				TogglePlayback();
			}
		};
		Grid.SetColumn(_playButton, 0);
		transport.Children.Add(_playButton);

		_positionText = new TextBlock
		{
			Text = "0:00.0",
			FontFamily = Palette.MonoFont,
			FontSize = 11,
			Foreground = Palette.Text,
			VerticalAlignment = VerticalAlignment.Center,
		};
		_durationText = new TextBlock
		{
			Text = " / 0:00.0",
			FontFamily = Palette.MonoFont,
			FontSize = 11,
			Foreground = Palette.Secondary,
			VerticalAlignment = VerticalAlignment.Center,
		};
		StackPanel timeStack = new()
		{
			Orientation = Orientation.Horizontal,
			VerticalAlignment = VerticalAlignment.Center,
		};
		timeStack.Children.Add(_positionText);
		timeStack.Children.Add(_durationText);
		Grid.SetColumn(timeStack, 2);
		transport.Children.Add(timeStack);

		_metaText = new TextBlock
		{
			FontFamily = Palette.UiFont,
			FontSize = 11,
			Foreground = Palette.Secondary,
			VerticalAlignment = VerticalAlignment.Center,
		};
		Grid.SetColumn(_metaText, 4);
		transport.Children.Add(_metaText);

		Children.Add(transport);

		_tickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
		_tickTimer.Tick += OnTick;

		DetachedFromVisualTree += (_, _) => StopAudio();
	}

	public void SetAudio(DecodedAudioResult? audio)
	{
		StopAudio();
		_audio = audio;
		_position = 0;

		if (audio is null)
		{
			_spectrogramCanvas.Clear();
			_levelMeterCanvas.SetChannelCount(0);
			UpdateTransportUi();
			return;
		}

		int channels = Math.Max(1, audio.Channels);
		_levelMeterCanvas.SetChannelCount(channels);
		_spectrogramCanvas.MinHeight = channels * 150.0 + TopMargin + BottomMargin;

		// Build Mel spectrogram textures on background thread
		DecodedAudioResult captured = audio;
		Task.Run(() => BuildSpectrogramBitmaps(captured)).ContinueWith(t =>
		{
			if (t.IsCompletedSuccessfully && ReferenceEquals(_audio, captured))
			{
				Dispatcher.UIThread.Post(() =>
				{
					if (ReferenceEquals(_audio, captured))
					{
						_spectrogramCanvas.SetData(t.Result, captured.SampleRate, captured.Duration);
					}
				});
			}
		});

		UpdateTransportUi();
	}

	public void TogglePlayback()
	{
		if (_audio is null || _audio.Duration <= 0 || _audio.Samples.Length == 0)
		{
			return;
		}

		if (_playing && _player is not null)
		{
			_player.Pause();
			_playing = false;
			UpdateTransportUi();
			return;
		}

		if (_player is not null && _player.IsPaused && !_player.IsEnded)
		{
			_player.Resume();
			_playing = true;
			_tickTimer.Start();
			UpdateTransportUi();
			return;
		}

		if (_position >= _audio.Duration)
		{
			_position = 0;
		}

		StartPlaybackAt(_position);
	}

	private void StartPlaybackAt(double seconds)
	{
		if (_audio is null || _audio.Samples.Length == 0)
		{
			return;
		}

		DisposePlayer();

		try
		{
			_player = new PcmAudioPlayer(_audio.Samples, _audio.SampleRate, _audio.Channels);
			_player.PlayFrom(seconds);
			_playing = _player.IsPlaying;
			if (_playing)
			{
				_tickTimer.Start();
			}
			UpdateTransportUi();
		}
		catch (Exception ex)
		{
			ActivityLogService.Instance.ReportError($"Audio playback: {ex.Message}");
			_playing = false;
			UpdateTransportUi();
		}
	}

	private void SeekToFraction(double fraction)
	{
		if (_audio is null || _audio.Duration <= 0)
		{
			return;
		}
		_position = _audio.Duration * Math.Clamp(fraction, 0.0, 1.0);
		_levelMeterCanvas.Reset();
		if (_player is not null)
		{
			try
			{
				_player.Seek(_position);
			}
			catch (Exception ex)
			{
				ActivityLogService.Instance.ReportError($"Audio seek: {ex.Message}");
			}
		}
		_spectrogramCanvas.SetPosition(_position);
		UpdateTransportUi();
	}

	public void StopAudio()
	{
		_playing = false;
		_tickTimer.Stop();
		DisposePlayer();
		_levelMeterCanvas.Reset();
	}

	private void DisposePlayer()
	{
		if (_player is not null)
		{
			_player.Dispose();
			_player = null;
		}
	}

	public void Dispose()
	{
		StopAudio();
	}

	private void OnTick(object? sender, EventArgs e)
	{
		if (_audio is null)
		{
			_tickTimer.Stop();
			return;
		}

		float[]? rmsDb = null;
		float[]? peakDb = null;

		if (_playing && _player is not null)
		{
			_position = Math.Min(_audio.Duration, _player.CurrentSeconds);
			ComputeWindowLevels(_audio, _position, out rmsDb, out peakDb);
			if (_player.IsEnded)
			{
				_playing = false;
				_position = _audio.Duration;
				UpdateTransportUi();
			}
		}

		bool meterChanged = _levelMeterCanvas.Update(rmsDb, peakDb);
		_spectrogramCanvas.SetPosition(_position);
		_positionText.Text = FormatTime(_position);

		if (!_playing && !meterChanged)
		{
			_tickTimer.Stop();
			UpdateTransportUi();
		}
	}

	private void UpdateTransportUi()
	{
		double duration = _audio?.Duration ?? 0;
		bool canPlay = duration > 0 && _audio?.Samples.Length > 0;
		_playButton.Background = canPlay ? Palette.Accent : Palette.Control;
		_playButton.Cursor = canPlay ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
		_playButton.Child = Icons.Glyph(_playing ? IconKind.Pause : IconKind.Play, 14, canPlay ? Palette.Text : Palette.Disabled);
		GlassSurface.SetTooltip(_playButton, _playing ? "Pause (Space)" : "Play (Space)");

		_positionText.Text = FormatTime(_position);
		_durationText.Text = $" / {FormatTime(duration)}";

		if (_audio is not null)
		{
			string chLabel = _audio.Channels switch
			{
				1 => "Mono",
				2 => "Stereo",
				int n => $"{n} ch",
			};
			_metaText.Text = $"{_audio.SampleRate} Hz · {chLabel} · Mel";
		}
		else
		{
			_metaText.Text = "";
		}
	}

	private static string FormatTime(double seconds)
	{
		if (seconds < 0 || double.IsNaN(seconds))
		{
			seconds = 0;
		}
		int minutes = (int)Math.Floor(seconds / 60.0);
		double rem = seconds - minutes * 60.0;
		return $"{minutes}:{rem:00.0}";
	}

	private static void ComputeWindowLevels(DecodedAudioResult audio, double posSeconds, out float[] rmsDb, out float[] peakDb)
	{
		int ch = Math.Max(1, audio.Channels);
		rmsDb = new float[ch];
		peakDb = new float[ch];
		int totalFrames = audio.Samples.Length / ch;
		int endFrame = Math.Clamp((int)(posSeconds * audio.SampleRate), 0, totalFrames);
		int windowFrames = Math.Max(1, audio.SampleRate / 10); // 100ms window
		int startFrame = Math.Max(0, endFrame - windowFrames);
		int count = endFrame - startFrame;

		if (count <= 0)
		{
			Array.Fill(rmsDb, MeterFloorDb);
			Array.Fill(peakDb, MeterFloorDb);
			return;
		}

		for (int c = 0; c < ch; c++)
		{
			double sumSq = 0;
			float peak = 0;
			for (int f = startFrame; f < endFrame; f++)
			{
				float s = Math.Abs(audio.Samples[f * ch + c]);
				sumSq += s * s;
				if (s > peak)
				{
					peak = s;
				}
			}
			float rms = (float)Math.Sqrt(sumSq / count);
			rmsDb[c] = rms > 1e-6f ? MathF.Max(MeterFloorDb, 20f * MathF.Log10(rms)) : MeterFloorDb;
			peakDb[c] = peak > 1e-6f ? MathF.Max(MeterFloorDb, 20f * MathF.Log10(peak)) : MeterFloorDb;
		}
	}

	#region Mel Spectrogram DSP (Exact 6-stop palette from vnlens-gui/src/workspace/audio_view.rs)

	private static double HzToMel(double hz) => 2595.0 * Math.Log10(1.0 + hz / 700.0);
	private static double MelToHz(double mel) => 700.0 * (Math.Pow(10.0, mel / 2595.0) - 1.0);

	private static WriteableBitmap[] BuildSpectrogramBitmaps(DecodedAudioResult audio)
	{
		if (audio.Samples.Length == 0 || audio.Channels <= 0 || audio.SampleRate <= 0)
		{
			return [];
		}

		const int FftSize = 1024;
		const int MaxColumns = 768;
		const int Bands = 128;

		int channels = audio.Channels;
		int totalFrames = audio.Samples.Length / channels;
		if (totalFrames < 32)
		{
			return [];
		}

		int columns = Math.Min(MaxColumns, Math.Max(1, totalFrames / 64));
		double nyquist = audio.SampleRate / 2.0;
		double maxMel = HzToMel(nyquist);

		// Precompute Hann window
		float[] hann = new float[FftSize];
		for (int i = 0; i < FftSize; i++)
		{
			hann[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (FftSize - 1)));
		}

		// Precompute Mel filter bin bounds
		int halfFft = FftSize / 2;
		int[] melBinStart = new int[Bands];
		int[] melBinCenter = new int[Bands];
		int[] melBinEnd = new int[Bands];
		for (int b = 0; b < Bands; b++)
		{
			double m0 = maxMel * b / (Bands + 1);
			double m1 = maxMel * (b + 1) / (Bands + 1);
			double m2 = maxMel * (b + 2) / (Bands + 1);
			melBinStart[b] = Math.Clamp((int)Math.Floor(MelToHz(m0) / nyquist * halfFft), 0, halfFft - 1);
			melBinCenter[b] = Math.Clamp((int)Math.Round(MelToHz(m1) / nyquist * halfFft), 0, halfFft - 1);
			melBinEnd[b] = Math.Clamp((int)Math.Ceiling(MelToHz(m2) / nyquist * halfFft), melBinCenter[b] + 1, halfFft);
		}

		float[][] channelMelDb = new float[channels][];
		float globalMax = float.NegativeInfinity;

		float[] real = new float[FftSize];
		float[] imag = new float[FftSize];
		float[] mag = new float[halfFft + 1];

		for (int c = 0; c < channels; c++)
		{
			float[] grid = new float[columns * Bands];
			channelMelDb[c] = grid;

			for (int col = 0; col < columns; col++)
			{
				int centerFrame = (int)((long)col * totalFrames / columns);
				int startFrame = centerFrame - FftSize / 2;

				for (int i = 0; i < FftSize; i++)
				{
					int frame = startFrame + i;
					float sample = (frame >= 0 && frame < totalFrames) ? audio.Samples[frame * channels + c] : 0f;
					real[i] = sample * hann[i];
					imag[i] = 0f;
				}

				FftRadix2(real, imag);

				for (int k = 0; k <= halfFft; k++)
				{
					mag[k] = real[k] * real[k] + imag[k] * imag[k];
				}

				for (int b = 0; b < Bands; b++)
				{
					int s = melBinStart[b];
					int mid = melBinCenter[b];
					int e = melBinEnd[b];
					float energy = 0f;
					for (int k = s; k < e && k <= halfFft; k++)
					{
						float w = k <= mid
							? (mid > s ? (float)(k - s) / (mid - s) : 1f)
							: (e > mid ? (float)(e - k) / (e - mid) : 1f);
						energy += mag[k] * MathF.Max(0.05f, w);
					}
					float db = 10f * MathF.Log10(MathF.Max(energy, 1e-12f));
					grid[col * Bands + b] = db;
					if (db > globalMax)
					{
						globalMax = db;
					}
				}
			}
		}

		if (float.IsNegativeInfinity(globalMax))
		{
			globalMax = 0f;
		}

		WriteableBitmap[] results = new WriteableBitmap[channels];
		for (int c = 0; c < channels; c++)
		{
			WriteableBitmap wb = new(new PixelSize(columns, Bands), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
			using ILockedFramebuffer fb = wb.Lock();
			float[] grid = channelMelDb[c];
			unsafe
			{
				byte* dst = (byte*)fb.Address;
				for (int y = 0; y < Bands; y++)
				{
					int band = Bands - 1 - y;
					byte* row = dst + y * fb.RowBytes;
					for (int col = 0; col < columns; col++)
					{
						float relDb = grid[col * Bands + band] - globalMax;
						(byte r, byte g, byte b) = MapDbToColor(relDb);
						int idx = col * 4;
						row[idx] = b;
						row[idx + 1] = g;
						row[idx + 2] = r;
						row[idx + 3] = 255;
					}
				}
			}
			results[c] = wb;
		}
		return results;
	}

	private static void FftRadix2(float[] real, float[] imag)
	{
		int n = real.Length;
		for (int i = 1, j = 0; i < n; i++)
		{
			int bit = n >> 1;
			for (; (j & bit) != 0; bit >>= 1)
			{
				j ^= bit;
			}
			j ^= bit;
			if (i < j)
			{
				(real[i], real[j]) = (real[j], real[i]);
				(imag[i], imag[j]) = (imag[j], imag[i]);
			}
		}

		for (int len = 2; len <= n; len <<= 1)
		{
			float ang = -2f * MathF.PI / len;
			float wlenR = MathF.Cos(ang);
			float wlenI = MathF.Sin(ang);
			for (int i = 0; i < n; i += len)
			{
				float wr = 1f, wi = 0f;
				int half = len >> 1;
				for (int j = 0; j < half; j++)
				{
					int u = i + j;
					int v = u + half;
					float vr = real[v] * wr - imag[v] * wi;
					float vi = real[v] * wi + imag[v] * wr;
					real[v] = real[u] - vr;
					imag[v] = imag[u] - vi;
					real[u] += vr;
					imag[u] += vi;
					float nextWr = wr * wlenR - wi * wlenI;
					wi = wr * wlenI + wi * wlenR;
					wr = nextWr;
				}
			}
		}
	}

	private static (byte R, byte G, byte B) MapDbToColor(float db)
	{
		// Exact 6 stops from vnlens-gui/src/workspace/audio_view.rs
		ReadOnlySpan<float> sr = [0f, 16f, 119f, 235f, 255f, 255f];
		ReadOnlySpan<float> sg = [0f, 30f, 54f, 112f, 202f, 255f];
		ReadOnlySpan<float> sb = [0f, 49f, 24f, 22f, 65f, 210f];

		float pos = Math.Clamp((db + RangeDb) / RangeDb, 0f, 1f) * 5f;
		int idx = Math.Min((int)pos, 4);
		float frac = pos - idx;
		byte r = (byte)MathF.Round(sr[idx] + (sr[idx + 1] - sr[idx]) * frac);
		byte g = (byte)MathF.Round(sg[idx] + (sg[idx + 1] - sg[idx]) * frac);
		byte b = (byte)MathF.Round(sb[idx] + (sb[idx + 1] - sb[idx]) * frac);
		return (r, g, b);
	}

	private static string ChannelLabel(int index, int count) => count switch
	{
		1 => "M",
		2 => index == 0 ? "L" : "R",
		_ => (index + 1).ToString(CultureInfo.InvariantCulture),
	};

	#endregion

	private sealed class SpectrogramCanvas : Control
	{
		private WriteableBitmap[] _channels = [];
		private int _sampleRate;
		private double _duration;
		private double _position;

		public event Action<double>? SeekRequested;

		public SpectrogramCanvas()
		{
			Cursor = new Cursor(StandardCursorType.Cross);
		}

		public void Clear()
		{
			_channels = [];
			_duration = 0;
			_position = 0;
			InvalidateVisual();
		}

		public void SetData(WriteableBitmap[] channels, int sampleRate, double duration)
		{
			_channels = channels;
			_sampleRate = sampleRate;
			_duration = duration;
			InvalidateVisual();
		}

		public void SetPosition(double position)
		{
			_position = position;
			InvalidateVisual();
		}

		private Rect GetPlotBounds() => new(
			LeftMargin,
			TopMargin,
			Math.Max(0, Bounds.Width - LeftMargin - RightMargin),
			Math.Max(0, Bounds.Height - TopMargin - BottomMargin));

		protected override void OnPointerPressed(PointerPressedEventArgs e)
		{
			base.OnPointerPressed(e);
			if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || _duration <= 0)
			{
				return;
			}
			Rect plot = GetPlotBounds();
			Point p = e.GetPosition(this);
			if (plot.Contains(p) && plot.Width > 0)
			{
				SeekRequested?.Invoke((p.X - plot.Left) / plot.Width);
				e.Handled = true;
			}
		}

		public override void Render(DrawingContext context)
		{
			Rect plot = GetPlotBounds();
			if (_channels.Length == 0 || plot.Width <= 0 || plot.Height <= 0)
			{
				return;
			}

			double chHeight = (plot.Height - ChannelGap * (_channels.Length - 1)) / _channels.Length;
			double nyquist = _sampleRate / 2.0;
			double maxMel = HzToMel(nyquist);
			Typeface typeface = new(Palette.UiFont);

			for (int i = 0; i < _channels.Length; i++)
			{
				Rect rect = new(plot.Left, plot.Top + (chHeight + ChannelGap) * i, plot.Width, chHeight);
				WriteableBitmap bmp = _channels[i];
				context.DrawImage(bmp, new Rect(0, 0, bmp.PixelSize.Width, bmp.PixelSize.Height), rect);

				DrawLabel(context, ChannelLabel(i, _channels.Length), new Point(plot.Left - 20, rect.Top + chHeight / 2 - 7), typeface);

				double prevY = rect.Top - 20;
				double[] freqTicks = [nyquist, 20000, 10000, 5000, 2000, 1000, 500, 200, 0];
				foreach (double hz in freqTicks)
				{
					if (hz > nyquist)
					{
						continue;
					}
					double y = rect.Bottom - chHeight * (HzToMel(hz) / maxMel);
					if (y - prevY < 20)
					{
						continue;
					}
					prevY = y;
					context.DrawRectangle(Palette.Border, null, new Rect(rect.Right, y, 4, 1));
					string text = hz >= 1000 ? $"{hz / 1000.0:0.0}k" : $"{hz:0}";
					DrawLabel(context, text, new Point(rect.Right + 7, y - 7), typeface);
				}
			}

			DrawLabel(context, "Hz", new Point(plot.Right + 7, plot.Bottom + 7), typeface);

			int ticks = Math.Max(1, (int)Math.Floor(plot.Width / 90.0));
			for (int i = 0; i <= ticks; i++)
			{
				double frac = (double)i / ticks;
				double x = plot.Left + plot.Width * frac;
				context.DrawRectangle(Palette.Border, null, new Rect(x, plot.Bottom, 1, 4));
				string text = $"{_duration * frac:0.0}s";
				double offset = i == ticks ? text.Length * 5.5 : 0;
				DrawLabel(context, text, new Point(x - offset, plot.Bottom + 7), typeface);
			}

			// Playhead cursor (#FFD166)
			double px = plot.Left + plot.Width * (_duration > 0 ? Math.Clamp(_position / _duration, 0, 1) : 0);
			context.DrawRectangle(Palette.Playhead, null, new Rect(Math.Min(px, plot.Right - 1), plot.Top, 1, plot.Height));
			context.DrawRectangle(Palette.Playhead, null, new Rect(px - 3, plot.Top - 6, 7, 5), 2, 2);

			// dB legend bar
			double legendX = plot.Right + 50;
			const int Steps = 64;
			double stepH = plot.Height / Steps;
			for (int s = 0; s < Steps; s++)
			{
				float frac = (float)s / Steps;
				(byte r, byte g, byte b) = MapDbToColor(-frac * RangeDb);
				context.DrawRectangle(new SolidColorBrush(Color.FromRgb(r, g, b)), null, new Rect(legendX, plot.Top + plot.Height * frac, 5, stepH + 1));
			}
			DrawLabel(context, "dB rel.", new Point(legendX, plot.Top - 15), typeface);
			int[] dbTicks = [0, 20, 40, 60, 80, 100];
			foreach (int db in dbTicks)
			{
				double y = plot.Top + plot.Height * (db / RangeDb);
				DrawLabel(context, db == 0 ? "0" : $"−{db}", new Point(legendX + 9, y - 7), typeface);
			}
		}

		private static void DrawLabel(DrawingContext ctx, string text, Point origin, Typeface typeface)
		{
			FormattedText ft = new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 10, Palette.Secondary);
			ctx.DrawText(ft, origin);
		}
	}

	private sealed class LevelMeterCanvas : Control
	{
		private struct ChannelState
		{
			public float Rms;
			public float Peak;
			public float Held;
			public long HoldUntil;
		}

		private static readonly ISolidColorBrush GreenFull = new SolidColorBrush(Color.Parse("#34C759"));
		private static readonly ISolidColorBrush GreenPeak = new SolidColorBrush(Color.FromArgb(77, 0x34, 0xC7, 0x59));
		private static readonly ISolidColorBrush YellowFull = new SolidColorBrush(Color.Parse("#FFCC00"));
		private static readonly ISolidColorBrush YellowPeak = new SolidColorBrush(Color.FromArgb(77, 0xFF, 0xCC, 0x00));
		private static readonly ISolidColorBrush RedFull = new SolidColorBrush(Color.Parse("#FF453A"));
		private static readonly ISolidColorBrush RedPeak = new SolidColorBrush(Color.FromArgb(77, 0xFF, 0x45, 0x3A));

		private ChannelState[] _channels = [];
		private long _lastUpdate;

		public void SetChannelCount(int count)
		{
			_channels = new ChannelState[count];
			Reset();
			Height = count > 0 ? 24 + count * 24 : 0;
			InvalidateVisual();
		}

		public void Reset()
		{
			for (int i = 0; i < _channels.Length; i++)
			{
				_channels[i] = new ChannelState { Rms = MeterFloorDb, Peak = MeterFloorDb, Held = MeterFloorDb };
			}
			_lastUpdate = Stopwatch.GetTimestamp();
			InvalidateVisual();
		}

		public bool Update(float[]? rmsDb, float[]? peakDb)
		{
			long now = Stopwatch.GetTimestamp();
			float elapsed = _lastUpdate == 0 ? 0f : (float)Stopwatch.GetElapsedTime(_lastUpdate, now).TotalSeconds;
			_lastUpdate = now;
			bool changed = false;

			for (int i = 0; i < _channels.Length; i++)
			{
				ChannelState prev = _channels[i];
				float inRms = rmsDb is not null && i < rmsDb.Length ? rmsDb[i] : MeterFloorDb;
				float inPeak = peakDb is not null && i < peakDb.Length ? peakDb[i] : MeterFloorDb;

				ChannelState cur = prev;
				cur.Rms = MathF.Max(MathF.Max(inRms, MeterFloorDb), cur.Rms - 24f * elapsed);
				cur.Peak = MathF.Max(MathF.Max(inPeak, MeterFloorDb), cur.Peak - 24f * elapsed);

				if (inPeak >= cur.Held && inPeak > MeterFloorDb)
				{
					cur.Held = inPeak;
					cur.HoldUntil = now + Stopwatch.Frequency; // 1s hold
				}
				else if (now >= cur.HoldUntil)
				{
					cur.Held = MathF.Max(cur.Peak, cur.Held - 12f * elapsed);
				}

				_channels[i] = cur;
				if (MathF.Abs(cur.Rms - prev.Rms) > 0.05f || MathF.Abs(cur.Peak - prev.Peak) > 0.05f || MathF.Abs(cur.Held - prev.Held) > 0.05f)
				{
					changed = true;
				}
			}

			if (changed)
			{
				InvalidateVisual();
			}
			return changed;
		}

		private static double DbFraction(float db) => Math.Clamp((db - MeterFloorDb) / -MeterFloorDb, 0.0, 1.0);

		public override void Render(DrawingContext context)
		{
			if (_channels.Length == 0)
			{
				return;
			}
			double left = LeftMargin;
			double right = Bounds.Width - RightMargin;
			double width = right - left;
			if (width <= 0)
			{
				return;
			}

			Typeface typeface = new(Palette.UiFont);
			double prevX = left - 40;
			float[] dbTicks = [-60, -48, -36, -24, -12, 0];
			foreach (float db in dbTicks)
			{
				double x = left + width * DbFraction(db);
				if (x - prevX < 34)
				{
					continue;
				}
				prevX = x;
				DrawLabel(context, $"{db:0}", new Point(x - 5, 0), typeface);
			}
			DrawLabel(context, "RMS/Peak dBFS", new Point(right + 7, 0), typeface);

			(float Start, float End, ISolidColorBrush Full, ISolidColorBrush Peak)[] segments =
			[
				(MeterFloorDb, -12f, GreenFull, GreenPeak),
				(-12f, -3f, YellowFull, YellowPeak),
				(-3f, 0f, RedFull, RedPeak),
			];

			for (int i = 0; i < _channels.Length; i++)
			{
				ChannelState ch = _channels[i];
				double top = 20 + i * 24;
				Rect bar = new(left, top, width, 10);

				DrawLabel(context, ChannelLabel(i, _channels.Length), new Point(8, top - 2), typeface);
				context.DrawRectangle(Palette.Border, null, bar, 2, 2);

				foreach (var seg in segments)
				{
					float peakEnd = MathF.Min(seg.End, ch.Peak);
					if (peakEnd > seg.Start)
					{
						double x0 = left + width * DbFraction(seg.Start);
						double x1 = left + width * DbFraction(peakEnd);
						context.DrawRectangle(seg.Peak, null, new Rect(x0, top, x1 - x0, 10));
					}
					float rmsEnd = MathF.Min(seg.End, ch.Rms);
					if (rmsEnd > seg.Start)
					{
						double x0 = left + width * DbFraction(seg.Start);
						double x1 = left + width * DbFraction(rmsEnd);
						context.DrawRectangle(seg.Full, null, new Rect(x0, top, x1 - x0, 10));
					}
				}

				if (ch.Peak > MeterFloorDb)
				{
					double px = Math.Min(left + width * DbFraction(ch.Peak), right - 1);
					context.DrawRectangle(Palette.Text, null, new Rect(px, top, 1, 10));
				}

				if (ch.Held > MeterFloorDb)
				{
					double hx = Math.Min(left + width * DbFraction(ch.Held), right - 2);
					context.DrawRectangle(ch.Held >= 0 ? RedFull : Palette.Playhead, null, new Rect(hx, top - 2, 2, 14));
				}

				string rmsStr = ch.Rms <= MeterFloorDb ? "≤−60" : $"{ch.Rms:0.0}";
				string peakStr = ch.Peak <= MeterFloorDb ? "≤−60" : $"{ch.Peak:0.0}";
				DrawLabel(context, $"{rmsStr} / {peakStr}", new Point(right + 7, top - 2), typeface);
			}
		}

		private static void DrawLabel(DrawingContext ctx, string text, Point origin, Typeface typeface)
		{
			FormattedText ft = new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 10, Palette.Secondary);
			ctx.DrawText(ft, origin);
		}
	}
}
