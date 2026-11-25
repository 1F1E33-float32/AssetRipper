using System.Diagnostics;
using AssetRipper.GUI.Avalonia.Theme;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AssetRipper.GUI.Avalonia.Controls;

/// <summary>900ms continuous rotating spinner.</summary>
internal sealed class VnlensSpinner : Border
{
	private readonly RotateTransform _rotate = new(0);
	private readonly FrameLoop _frames;
	private long _start;

	public VnlensSpinner(double size, IBrush brush)
	{
		Width = size;
		Height = size;
		Control glyph = Icons.Glyph(IconKind.Spinner, size, brush);
		glyph.RenderTransformOrigin = RelativePoint.Center;
		glyph.RenderTransform = _rotate;
		Child = glyph;

		_frames = new FrameLoop(
			this,
			() =>
			{
				double elapsed = Stopwatch.GetElapsedTime(_start).TotalMilliseconds;
				_rotate.Angle = (elapsed % Palette.SpinnerDuration.TotalMilliseconds) / Palette.SpinnerDuration.TotalMilliseconds * 360.0;
				return true;
			}
		);

		AttachedToVisualTree += (_, _) =>
		{
			_start = Stopwatch.GetTimestamp();
			_frames.Start();
		};
		DetachedFromVisualTree += (_, _) => _frames.Stop();
	}
}

/// <summary>Circular progress ring.</summary>
internal sealed class ProgressRingControl : Control
{
	private double _progress;

	public double Progress
	{
		get => _progress;
		set
		{
			double clamped = Math.Clamp(value, 0.0, 1.0);
			if (Math.Abs(_progress - clamped) > 0.001)
			{
				_progress = clamped;
				InvalidateVisual();
			}
		}
	}

	public ProgressRingControl(double diameter = 15.0)
	{
		Width = diameter;
		Height = diameter;
	}

	public override void Render(DrawingContext context)
	{
		double diameter = Math.Min(Bounds.Width, Bounds.Height);
		if (diameter <= 2)
		{
			return;
		}
		Point center = new(Bounds.Width / 2.0, Bounds.Height / 2.0);
		double radius = diameter / 2.0 - 1.0;

		Pen bgPen = new(Palette.Border, 1.6, lineCap: PenLineCap.Round);
		context.DrawEllipse(null, bgPen, center, radius, radius);

		if (_progress <= 0.001)
		{
			return;
		}

		Pen fgPen = new(Palette.Accent, 1.6, lineCap: PenLineCap.Round);
		if (_progress >= 0.999)
		{
			context.DrawEllipse(null, fgPen, center, radius, radius);
			return;
		}

		double angle = _progress * Math.PI * 2.0;
		Point start = new(center.X, center.Y - radius);
		Point end = new(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));

		StreamGeometry geo = new();
		using (StreamGeometryContext ctx = geo.Open())
		{
			ctx.BeginFigure(start, false);
			ctx.ArcTo(end, new Size(radius, radius), 0, _progress > 0.5, SweepDirection.Clockwise);
			ctx.EndFigure(false);
		}
		context.DrawGeometry(null, fgPen, geo);
	}
}
