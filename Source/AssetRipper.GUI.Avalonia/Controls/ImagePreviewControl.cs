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
using Avalonia.Threading;

namespace AssetRipper.GUI.Avalonia.Controls;

/// <summary>
/// Zoomable, pannable image inspector with anchored checkerboard transparency grid,
/// 180ms ease-in-out zoom animation, custom scrollbars, and multi-layer frame controls,
/// ported directly from vnlens-gui/src/workspace/image_view.rs.
/// </summary>
internal sealed class ImagePreviewControl : Grid
{
	private readonly ImageCanvas _canvas;
	private readonly Border _frameNavContainer;
	private readonly VnlensButton _prevFrameBtn;
	private readonly VnlensButton _nextFrameBtn;
	private readonly TextBlock _frameLabel;
	private readonly TextBlock _zoomLabel;
	private readonly Border _zoomActualBtn;

	private DecodedImageResult? _image;
	private int _currentLayer;

	public ImagePreviewControl()
	{
		RowDefinitions = new RowDefinitions("*,40");

		_zoomLabel = new TextBlock
		{
			Text = "100%",
			FontFamily = Palette.MonoFont,
			FontSize = 11,
			Foreground = Palette.Text,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
		};

		_canvas = new ImageCanvas();
		_canvas.ScaleChanged += scale =>
		{
			_zoomLabel.Text = $"{Math.Round(scale * 100.0).ToString("0", CultureInfo.InvariantCulture)}%";
		};

		Border canvasHost = new()
		{
			Margin = new Thickness(12),
			Child = _canvas,
			ClipToBounds = true,
		};
		Grid.SetRow(canvasHost, 0);
		Children.Add(canvasHost);

		// Bottom 40px control bar (matching image_view.rs lines 386-487)
		Grid bar = new()
		{
			Height = 40,
			Margin = new Thickness(12, 0),
			ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
			VerticalAlignment = VerticalAlignment.Center,
		};
		Grid.SetRow(bar, 1);

		// Optional frame/layer controls
		_prevFrameBtn = new VnlensButton(IconKind.ChevronLeft, enabled: false, ghost: true, height: 28, iconSize: 15);
		GlassSurface.SetTooltip(_prevFrameBtn, "Previous layer");
		_prevFrameBtn.Clicked += () => SetLayer(_currentLayer - 1);

		_frameLabel = new TextBlock
		{
			MinWidth = 44,
			TextAlignment = TextAlignment.Center,
			FontFamily = Palette.MonoFont,
			FontSize = 11,
			Foreground = Palette.Secondary,
			VerticalAlignment = VerticalAlignment.Center,
		};

		_nextFrameBtn = new VnlensButton(IconKind.ChevronRight, enabled: false, ghost: true, height: 28, iconSize: 15);
		GlassSurface.SetTooltip(_nextFrameBtn, "Next layer");
		_nextFrameBtn.Clicked += () => SetLayer(_currentLayer + 1);

		StackPanel framePanel = new()
		{
			Orientation = Orientation.Horizontal,
			Spacing = 6,
			VerticalAlignment = VerticalAlignment.Center,
		};
		framePanel.Children.Add(_prevFrameBtn);
		framePanel.Children.Add(_frameLabel);
		framePanel.Children.Add(_nextFrameBtn);

		_frameNavContainer = new Border
		{
			IsVisible = false,
			Child = framePanel,
		};
		Grid.SetColumn(_frameNavContainer, 0);
		bar.Children.Add(_frameNavContainer);

		// Zoom pill (matching image_view.rs lines 432-486)
		VnlensButton zoomOutBtn = new(IconKind.ZoomOut, ghost: true, height: 26, iconSize: 15);
		GlassSurface.SetTooltip(zoomOutBtn, "Zoom out");
		zoomOutBtn.Clicked += () => _canvas.ChangeZoom(1.0 / 1.25);

		_zoomActualBtn = new Border
		{
			Height = 26,
			MinWidth = 52,
			Padding = new Thickness(8, 0),
			CornerRadius = new CornerRadius(6),
			Background = Palette.Transparent,
			Cursor = new Cursor(StandardCursorType.Hand),
			Child = _zoomLabel,
		};
		GlassSurface.SetTooltip(_zoomActualBtn, "Actual size (100%)");
		_zoomActualBtn.PointerEntered += (_, _) => _zoomActualBtn.Background = Palette.GhostHover;
		_zoomActualBtn.PointerExited += (_, _) => _zoomActualBtn.Background = Palette.Transparent;
		_zoomActualBtn.PointerPressed += (_, e) =>
		{
			if (e.GetCurrentPoint(_zoomActualBtn).Properties.IsLeftButtonPressed)
			{
				e.Handled = true;
				_canvas.ZoomToActualSize();
			}
		};

		VnlensButton zoomInBtn = new(IconKind.ZoomIn, ghost: true, height: 26, iconSize: 15);
		GlassSurface.SetTooltip(zoomInBtn, "Zoom in");
		zoomInBtn.Clicked += () => _canvas.ChangeZoom(1.25);

		StackPanel zoomStack = new()
		{
			Orientation = Orientation.Horizontal,
			VerticalAlignment = VerticalAlignment.Center,
		};
		zoomStack.Children.Add(zoomOutBtn);
		zoomStack.Children.Add(_zoomActualBtn);
		zoomStack.Children.Add(zoomInBtn);

		Border zoomPill = new()
		{
			Padding = new Thickness(2),
			CornerRadius = new CornerRadius(9),
			Background = Palette.PillBg,
			VerticalAlignment = VerticalAlignment.Center,
			Child = zoomStack,
		};
		Grid.SetColumn(zoomPill, 2);
		bar.Children.Add(zoomPill);

		Children.Add(bar);
	}

	public void SetImage(DecodedImageResult? image)
	{
		_image = image;
		_currentLayer = 0;
		if (image is not null && image.Layers.Length > 0)
		{
			_frameNavContainer.IsVisible = image.Layers.Length > 1;
			UpdateLayerUI();
			_canvas.SetBitmap(image.Layers[0], resetCamera: true);
		}
		else
		{
			_frameNavContainer.IsVisible = false;
			_canvas.SetBitmap(null, resetCamera: true);
		}
	}

	private void SetLayer(int index)
	{
		if (_image is null || index < 0 || index >= _image.Layers.Length || index == _currentLayer)
		{
			return;
		}
		_currentLayer = index;
		UpdateLayerUI();
		_canvas.SetBitmap(_image.Layers[_currentLayer], resetCamera: false);
	}

	private void UpdateLayerUI()
	{
		if (_image is null)
		{
			return;
		}
		int count = _image.Layers.Length;
		_frameLabel.Text = $"{_currentLayer + 1} / {count}";
		_prevFrameBtn.IsActionEnabled = _currentLayer > 0;
		_nextFrameBtn.IsActionEnabled = _currentLayer + 1 < count;
	}

	private sealed class ImageCanvas : Control
	{
		private struct ZoomMotion
		{
			public double FromScale;
			public double ToScale;
			public Point FromOrigin;
			public Point ToOrigin;
			public long Started;
		}

		private enum DragKind { None, Pan, HBar, VBar }

		private WriteableBitmap? _bitmap;
		private double? _zoom;
		private Point _pan;
		private ZoomMotion? _motion;
		private readonly DispatcherTimer _animTimer;

		private DragKind _dragKind;
		private Point _dragStart;
		private Point _dragInitialPan;
		private double _dragBarOrigin;
		private double _dragBarTravel;

		public event Action<double>? ScaleChanged;

		public ImageCanvas()
		{
			ClipToBounds = true;
			_animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
			_animTimer.Tick += (_, _) =>
			{
				if (!SampleMotion())
				{
					_animTimer.Stop();
				}
				InvalidateVisual();
			};
		}

		public void SetBitmap(WriteableBitmap? bitmap, bool resetCamera)
		{
			_bitmap = bitmap;
			if (resetCamera)
			{
				_zoom = null;
				_pan = default;
				_motion = null;
				_animTimer.Stop();
			}
			InvalidateVisual();
			ScaleChanged?.Invoke(CurrentScale());
		}

		private Size ViewportSize => new(Math.Max(0, Bounds.Width - 16), Math.Max(0, Bounds.Height - 16));

		private Size ImageDipSize
		{
			get
			{
				if (_bitmap is null)
				{
					return default;
				}
				double scaleFactor = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
				return new Size(_bitmap.PixelSize.Width / scaleFactor, _bitmap.PixelSize.Height / scaleFactor);
			}
		}

		private double CurrentScale()
		{
			if (_zoom.HasValue)
			{
				return _zoom.Value;
			}
			Size img = ImageDipSize;
			Size vp = ViewportSize;
			if (img.Width <= 0 || img.Height <= 0 || vp.Width <= 0 || vp.Height <= 0)
			{
				return 1.0;
			}
			return Math.Clamp(Math.Min(vp.Width / img.Width, vp.Height / img.Height), 1e-4, 1.0);
		}

		private double TargetScale() => _motion?.ToScale ?? CurrentScale();

		private Size MaximumPan(double scale)
		{
			Size img = ImageDipSize;
			Size vp = ViewportSize;
			return new Size(
				Math.Max(0, img.Width * scale - vp.Width),
				Math.Max(0, img.Height * scale - vp.Height));
		}

		private void ClampPan()
		{
			Size max = MaximumPan(CurrentScale());
			_pan = new Point(
				Math.Clamp(_pan.X, -max.Width, 0),
				Math.Clamp(_pan.Y, -max.Height, 0));
		}

		private Point OriginAt(double scale, Point pan)
		{
			Size img = ImageDipSize;
			Size vp = ViewportSize;
			return new Point(
				Math.Max(0, vp.Width - img.Width * scale) / 2.0 + pan.X,
				Math.Max(0, vp.Height - img.Height * scale) / 2.0 + pan.Y);
		}

		private (double Scale, Point Pan) ComputeZoomAt(double newScale, Point anchor)
		{
			double oldScale = CurrentScale();
			Point oldOrigin = OriginAt(oldScale, _pan);
			double clampedScale = Math.Clamp(newScale, 1.0 / 64.0, 64.0);
			Size img = ImageDipSize;
			Size vp = ViewportSize;

			double panX = anchor.X - (anchor.X - oldOrigin.X) * (clampedScale / oldScale)
				- Math.Max(0, vp.Width - img.Width * clampedScale) / 2.0;
			double panY = anchor.Y - (anchor.Y - oldOrigin.Y) * (clampedScale / oldScale)
				- Math.Max(0, vp.Height - img.Height * clampedScale) / 2.0;

			Size max = MaximumPan(clampedScale);
			return (clampedScale, new Point(Math.Clamp(panX, -max.Width, 0), Math.Clamp(panY, -max.Height, 0)));
		}

		private void AnimateZoom(double targetScale, Point anchor)
		{
			SampleMotion();
			double fromScale = CurrentScale();
			Point fromOrigin = OriginAt(fromScale, _pan);
			(double toScale, Point toPan) = ComputeZoomAt(targetScale, anchor);
			Point toOrigin = OriginAt(toScale, toPan);

			_motion = new ZoomMotion
			{
				FromScale = fromScale,
				ToScale = toScale,
				FromOrigin = fromOrigin,
				ToOrigin = toOrigin,
				Started = Stopwatch.GetTimestamp(),
			};
			_animTimer.Start();
			InvalidateVisual();
		}

		private bool SampleMotion()
		{
			if (!_motion.HasValue)
			{
				return false;
			}
			ZoomMotion m = _motion.Value;
			double elapsed = Stopwatch.GetElapsedTime(m.Started).TotalSeconds;
			double duration = Palette.ZoomDuration.TotalSeconds;
			if (elapsed >= duration)
			{
				_zoom = m.ToScale;
				_pan = default;
				Point baseOrigin = OriginAt(m.ToScale, default);
				_pan = new Point(m.ToOrigin.X - baseOrigin.X, m.ToOrigin.Y - baseOrigin.Y);
				_motion = null;
				ClampPan();
				ScaleChanged?.Invoke(CurrentScale());
				return false;
			}

			double t = LayoutMotion.EaseInOut(elapsed / duration);
			double s = m.FromScale + (m.ToScale - m.FromScale) * t;
			_zoom = s;
			Point baseOrg = OriginAt(s, default);
			double targetX = m.FromOrigin.X + (m.ToOrigin.X - m.FromOrigin.X) * t;
			double targetY = m.FromOrigin.Y + (m.ToOrigin.Y - m.FromOrigin.Y) * t;
			_pan = new Point(targetX - baseOrg.X, targetY - baseOrg.Y);
			ClampPan();
			ScaleChanged?.Invoke(s);
			return true;
		}

		public void ChangeZoom(double factor)
		{
			Size vp = ViewportSize;
			AnimateZoom(TargetScale() * factor, new Point(vp.Width / 2.0, vp.Height / 2.0));
		}

		public void ZoomToActualSize()
		{
			Size vp = ViewportSize;
			AnimateZoom(1.0, new Point(vp.Width / 2.0, vp.Height / 2.0));
		}

		protected override void OnSizeChanged(SizeChangedEventArgs e)
		{
			base.OnSizeChanged(e);
			ClampPan();
			ScaleChanged?.Invoke(CurrentScale());
			InvalidateVisual();
		}

		protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
		{
			base.OnPointerWheelChanged(e);
			if (_bitmap is null)
			{
				return;
			}
			Point pos = e.GetPosition(this);
			if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
			{
				double scale = TargetScale() * Math.Exp(e.Delta.Y * 0.22);
				AnimateZoom(scale, pos);
			}
			else
			{
				SampleMotion();
				_motion = null;
				_pan = new Point(_pan.X + e.Delta.X * 40.0, _pan.Y + e.Delta.Y * 40.0);
				ClampPan();
				InvalidateVisual();
			}
			e.Handled = true;
		}

		protected override void OnPointerPressed(PointerPressedEventArgs e)
		{
			base.OnPointerPressed(e);
			if (_bitmap is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
			{
				return;
			}
			SampleMotion();
			_motion = null;
			Point pos = e.GetPosition(this);
			_dragStart = pos;
			_dragInitialPan = _pan;
			_dragKind = DragKind.Pan;

			// Hit-test horizontal & vertical scrollbars
			if (TryGetBarMetrics(horizontal: true, out Rect hTrack, out Rect hThumb, out double hTravel) && hTrack.Contains(pos))
			{
				double grab = hThumb.Contains(pos) ? pos.X - hThumb.X : hThumb.Width / 2.0;
				_dragKind = DragKind.HBar;
				_dragBarOrigin = hTrack.X + grab;
				_dragBarTravel = hTravel;
			}
			else if (TryGetBarMetrics(horizontal: false, out Rect vTrack, out Rect vThumb, out double vTravel) && vTrack.Contains(pos))
			{
				double grab = vThumb.Contains(pos) ? pos.Y - vThumb.Y : vThumb.Height / 2.0;
				_dragKind = DragKind.VBar;
				_dragBarOrigin = vTrack.Y + grab;
				_dragBarTravel = vTravel;
			}

			e.Pointer.Capture(this);
			e.Handled = true;
		}

		protected override void OnPointerMoved(PointerEventArgs e)
		{
			base.OnPointerMoved(e);
			if (_dragKind == DragKind.None)
			{
				return;
			}
			Point pos = e.GetPosition(this);
			Size max = MaximumPan(CurrentScale());
			switch (_dragKind)
			{
				case DragKind.Pan:
					_pan = new Point(_dragInitialPan.X + (pos.X - _dragStart.X), _dragInitialPan.Y + (pos.Y - _dragStart.Y));
					break;
				case DragKind.HBar when _dragBarTravel > 0:
					_pan = new Point(-max.Width * ((pos.X - _dragBarOrigin) / _dragBarTravel), _pan.Y);
					break;
				case DragKind.VBar when _dragBarTravel > 0:
					_pan = new Point(_pan.X, -max.Height * ((pos.Y - _dragBarOrigin) / _dragBarTravel));
					break;
			}
			ClampPan();
			InvalidateVisual();
			e.Handled = true;
		}

		protected override void OnPointerReleased(PointerReleasedEventArgs e)
		{
			base.OnPointerReleased(e);
			if (_dragKind != DragKind.None)
			{
				_dragKind = DragKind.None;
				e.Pointer.Capture(null);
				e.Handled = true;
			}
		}

		private bool TryGetBarMetrics(bool horizontal, out Rect track, out Rect thumb, out double travel)
		{
			Size vp = ViewportSize;
			Size max = MaximumPan(CurrentScale());
			double maxAxis = horizontal ? max.Width : max.Height;
			double trackLen = horizontal ? vp.Width : vp.Height;
			if (maxAxis <= 0.5 || trackLen <= 0)
			{
				track = default;
				thumb = default;
				travel = 0;
				return false;
			}
			double thumbLen = Math.Clamp(trackLen * (trackLen / (trackLen + maxAxis)), 24.0, trackLen);
			travel = trackLen - thumbLen;
			double panAxis = horizontal ? _pan.X : _pan.Y;
			double start = travel > 0 ? (-panAxis / maxAxis) * travel : 0;

			if (horizontal)
			{
				track = new Rect(0, vp.Height + 4, trackLen, 8);
				thumb = new Rect(start, vp.Height + 4, thumbLen, 8);
			}
			else
			{
				track = new Rect(vp.Width + 4, 0, 8, trackLen);
				thumb = new Rect(vp.Width + 4, start, 8, thumbLen);
			}
			return true;
		}

		public override void Render(DrawingContext context)
		{
			if (_bitmap is null)
			{
				return;
			}
			Size vp = ViewportSize;
			if (vp.Width <= 0 || vp.Height <= 0)
			{
				return;
			}

			double scale = CurrentScale();
			Size imgSize = ImageDipSize;
			Point origin = OriginAt(scale, _pan);
			Rect viewportRect = new(0, 0, vp.Width, vp.Height);
			Rect drawRect = new(origin.X, origin.Y, imgSize.Width * scale, imgSize.Height * scale);

			using (context.PushClip(viewportRect))
			{
				Rect visible = drawRect.Intersect(viewportRect);
				if (visible.Width > 0 && visible.Height > 0)
				{
					PaintCheckerboard(context, visible, origin);
				}
				context.DrawImage(_bitmap, new Rect(0, 0, _bitmap.PixelSize.Width, _bitmap.PixelSize.Height), drawRect);
			}

			// Custom subtle dark scrollbars matching image_view.rs lines 254-286
			if (TryGetBarMetrics(true, out Rect hTrack, out Rect hThumb, out _))
			{
				context.DrawRectangle(Palette.PillBg, null, hTrack, 4, 4);
				context.DrawRectangle(Palette.Tertiary, null, hThumb, 4, 4);
			}
			if (TryGetBarMetrics(false, out Rect vTrack, out Rect vThumb, out _))
			{
				context.DrawRectangle(Palette.PillBg, null, vTrack, 4, 4);
				context.DrawRectangle(Palette.Tertiary, null, vThumb, 4, 4);
			}
		}

		private static void PaintCheckerboard(DrawingContext context, Rect area, Point anchor)
		{
			const double Tile = 8.0;
			context.DrawRectangle(Palette.CheckerDark, null, area);

			int firstCol = (int)Math.Floor((area.Left - anchor.X) / Tile);
			int firstRow = (int)Math.Floor((area.Top - anchor.Y) / Tile);
			int lastCol = (int)Math.Ceiling((area.Right - anchor.X) / Tile);
			int lastRow = (int)Math.Ceiling((area.Bottom - anchor.Y) / Tile);

			for (int row = firstRow; row < lastRow; row++)
			{
				for (int col = firstCol; col < lastCol; col++)
				{
					if (((row + col) & 1) == 0)
					{
						continue;
					}
					Rect tile = new Rect(anchor.X + col * Tile, anchor.Y + row * Tile, Tile, Tile).Intersect(area);
					if (tile.Width > 0 && tile.Height > 0)
					{
						context.DrawRectangle(Palette.CheckerLight, null, tile);
					}
				}
			}
		}
	}
}
