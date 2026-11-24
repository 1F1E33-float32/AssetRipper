using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AssetRipper.GUI.Avalonia.Services;

/// <summary>
/// Lightweight, zero-dependency PCM audio player using Windows winmm waveOut APIs.
/// Gracefully reports unsupported on non-Windows platforms without breaking builds.
/// </summary>
internal sealed partial class PcmAudioPlayer : IDisposable
{
	private const uint WaveMapper = 0xFFFFFFFF;
	private const ushort WaveFormatPcm = 1;
	private const uint WhdrDone = 0x00000001;

	[StructLayout(LayoutKind.Sequential, Pack = 2)]
	private struct WaveFormatEx
	{
		public ushort FormatTag;
		public ushort Channels;
		public uint SamplesPerSec;
		public uint AvgBytesPerSec;
		public ushort BlockAlign;
		public ushort BitsPerSample;
		public ushort ExtraSize;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct WaveHdr
	{
		public nint Data;
		public uint BufferLength;
		public uint BytesRecorded;
		public nint User;
		public uint Flags;
		public uint Loops;
		public nint Next;
		public nint Reserved;
	}

	[LibraryImport("winmm.dll")]
	private static partial uint waveOutOpen(out nint hWaveOut, uint deviceId, ref WaveFormatEx format, nint callback, nint instance, uint flags);

	[LibraryImport("winmm.dll")]
	private static partial uint waveOutPrepareHeader(nint hWaveOut, ref WaveHdr header, uint size);

	[LibraryImport("winmm.dll")]
	private static partial uint waveOutUnprepareHeader(nint hWaveOut, ref WaveHdr header, uint size);

	[LibraryImport("winmm.dll")]
	private static partial uint waveOutWrite(nint hWaveOut, ref WaveHdr header, uint size);

	[LibraryImport("winmm.dll")]
	private static partial uint waveOutPause(nint hWaveOut);

	[LibraryImport("winmm.dll")]
	private static partial uint waveOutRestart(nint hWaveOut);

	[LibraryImport("winmm.dll")]
	private static partial uint waveOutReset(nint hWaveOut);

	[LibraryImport("winmm.dll")]
	private static partial uint waveOutClose(nint hWaveOut);

	private readonly float[] _samples;
	private readonly int _sampleRate;
	private readonly int _channels;
	private readonly double _duration;

	private nint _hWaveOut;
	private GCHandle _pcmHandle;
	private WaveHdr _header;
	private bool _headerPrepared;

	private bool _playing;
	private bool _paused;
	private double _baseSeconds;
	private long _playStartedTimestamp;
	private bool _disposed;

	public bool IsPlaying => _playing && !_paused && !IsEnded;
	public bool IsPaused => _paused;
	public bool IsEnded => (_headerPrepared && (_header.Flags & WhdrDone) != 0) || CurrentSeconds >= _duration;

	public double CurrentSeconds
	{
		get
		{
			if (_playing && !_paused)
			{
				double elapsed = Stopwatch.GetElapsedTime(_playStartedTimestamp).TotalSeconds;
				return Math.Clamp(_baseSeconds + elapsed, 0.0, _duration);
			}
			return Math.Clamp(_baseSeconds, 0.0, _duration);
		}
	}

	public PcmAudioPlayer(float[] samples, int sampleRate, int channels)
	{
		_samples = samples;
		_sampleRate = Math.Max(1, sampleRate);
		_channels = Math.Max(1, channels);
		_duration = (double)samples.Length / _channels / _sampleRate;
	}

	public void PlayFrom(double seconds)
	{
		if (!OperatingSystem.IsWindows())
		{
			throw new PlatformNotSupportedException("Audio playback is currently supported on Windows.");
		}

		CloseDevice();

		int totalFrames = _samples.Length / _channels;
		int startFrame = Math.Clamp((int)(seconds * _sampleRate), 0, totalFrames);
		int framesToPlay = totalFrames - startFrame;
		if (framesToPlay <= 0)
		{
			_baseSeconds = _duration;
			_playing = false;
			_paused = false;
			return;
		}

		int startSample = startFrame * _channels;
		int sampleCount = framesToPlay * _channels;
		short[] pcm16 = new short[sampleCount];
		for (int i = 0; i < sampleCount; i++)
		{
			float clamped = Math.Clamp(_samples[startSample + i], -1f, 1f);
			pcm16[i] = (short)MathF.Round(clamped * short.MaxValue);
		}

		WaveFormatEx fmt = new()
		{
			FormatTag = WaveFormatPcm,
			Channels = (ushort)_channels,
			SamplesPerSec = (uint)_sampleRate,
			BitsPerSample = 16,
			BlockAlign = (ushort)(_channels * 2),
			AvgBytesPerSec = (uint)(_sampleRate * _channels * 2),
			ExtraSize = 0,
		};

		uint res = waveOutOpen(out _hWaveOut, WaveMapper, ref fmt, 0, 0, 0);
		if (res != 0)
		{
			_hWaveOut = 0;
			throw new InvalidOperationException($"waveOutOpen failed (error {res}).");
		}

		_pcmHandle = GCHandle.Alloc(pcm16, GCHandleType.Pinned);
		_header = new WaveHdr
		{
			Data = _pcmHandle.AddrOfPinnedObject(),
			BufferLength = (uint)(pcm16.Length * sizeof(short)),
		};

		uint hdrSize = (uint)Marshal.SizeOf<WaveHdr>();
		res = waveOutPrepareHeader(_hWaveOut, ref _header, hdrSize);
		if (res != 0)
		{
			CloseDevice();
			throw new InvalidOperationException($"waveOutPrepareHeader failed (error {res}).");
		}
		_headerPrepared = true;

		res = waveOutWrite(_hWaveOut, ref _header, hdrSize);
		if (res != 0)
		{
			CloseDevice();
			throw new InvalidOperationException($"waveOutWrite failed (error {res}).");
		}

		_baseSeconds = (double)startFrame / _sampleRate;
		_playStartedTimestamp = Stopwatch.GetTimestamp();
		_playing = true;
		_paused = false;
	}

	public void Pause()
	{
		if (!_playing || _paused || _hWaveOut == 0)
		{
			return;
		}
		_baseSeconds = CurrentSeconds;
		_paused = true;
		if (OperatingSystem.IsWindows())
		{
			_ = waveOutPause(_hWaveOut);
		}
	}

	public void Resume()
	{
		if (!_playing || !_paused || _hWaveOut == 0)
		{
			return;
		}
		_playStartedTimestamp = Stopwatch.GetTimestamp();
		_paused = false;
		if (OperatingSystem.IsWindows())
		{
			_ = waveOutRestart(_hWaveOut);
		}
	}

	public void Seek(double seconds)
	{
		bool wasPlaying = IsPlaying;
		double clamped = Math.Clamp(seconds, 0.0, _duration);
		if (wasPlaying)
		{
			PlayFrom(clamped);
		}
		else
		{
			CloseDevice();
			_baseSeconds = clamped;
			_playing = false;
			_paused = false;
		}
	}

	private void CloseDevice()
	{
		_playing = false;
		_paused = false;
		if (_hWaveOut != 0 && OperatingSystem.IsWindows())
		{
			_ = waveOutReset(_hWaveOut);
			if (_headerPrepared)
			{
				_ = waveOutUnprepareHeader(_hWaveOut, ref _header, (uint)Marshal.SizeOf<WaveHdr>());
				_headerPrepared = false;
			}
			_ = waveOutClose(_hWaveOut);
			_hWaveOut = 0;
		}
		if (_pcmHandle.IsAllocated)
		{
			_pcmHandle.Free();
		}
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			CloseDevice();
		}
	}
}
