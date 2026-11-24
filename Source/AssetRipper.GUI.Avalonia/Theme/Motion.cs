using System.Diagnostics;

namespace AssetRipper.GUI.Avalonia.Theme;

/// <summary>
/// Coordinated preview resizing and column strip scrolling, ported directly from vnlens-gui/src/workspace/layout.rs.
/// </summary>
internal readonly record struct LayoutSnapshot(double Inset, double Scroll);

internal sealed class LayoutMotion
{
	private static readonly double DurationSeconds = Palette.LayoutDuration.TotalSeconds;

	private LayoutSnapshot _from;
	private LayoutSnapshot _target;
	private long? _startedTimestamp;

	public LayoutSnapshot Target => _target;
	public bool IsAnimating => _startedTimestamp.HasValue;

	public static double EaseInOut(double t)
	{
		t = Math.Clamp(t, 0.0, 1.0);
		return t < 0.5
			? 2.0 * t * t
			: 1.0 - Math.Pow(-2.0 * t + 2.0, 2.0) / 2.0;
	}

	public void Retarget(LayoutSnapshot from, LayoutSnapshot target, bool animate)
	{
		_from = from;
		_target = target;
		if (animate && (Math.Abs(from.Inset - target.Inset) > 0.25 || Math.Abs(from.Scroll - target.Scroll) > 0.25))
		{
			_startedTimestamp = Stopwatch.GetTimestamp();
		}
		else
		{
			_startedTimestamp = null;
		}
	}

	public (LayoutSnapshot Current, bool Animating) Sample()
	{
		if (!_startedTimestamp.HasValue)
		{
			return (_target, false);
		}

		double elapsed = Stopwatch.GetElapsedTime(_startedTimestamp.Value).TotalSeconds;
		if (elapsed >= DurationSeconds)
		{
			_startedTimestamp = null;
			return (_target, false);
		}

		double progress = EaseInOut(elapsed / DurationSeconds);
		LayoutSnapshot current = new(
			_from.Inset + (_target.Inset - _from.Inset) * progress,
			_from.Scroll + (_target.Scroll - _from.Scroll) * progress);
		return (current, true);
	}

	public static LayoutSnapshot ComputeTarget(
		double viewport,
		double left,
		double columnWidth,
		double contentWidth,
		double currentScroll,
		bool preview,
		double? manualWidth)
	{
		double maxInset = Math.Max(Palette.InspectorMinWidth, viewport - Palette.InspectorMinWidth);
		double inset = preview
			? Math.Clamp(manualWidth ?? (viewport - columnWidth), Palette.InspectorMinWidth, maxInset)
			: 0.0;

		double browserWidth = Math.Max(0.0, viewport - inset);
		double right = left + columnWidth;

		// Note: Avalonia ScrollViewer Offset.X is positive (0..maxScroll), opposite sign to GPUI's negative offset.
		double scroll;
		if ((preview && manualWidth is null) || left < currentScroll || columnWidth > browserWidth)
		{
			scroll = left;
		}
		else if (right - currentScroll > browserWidth)
		{
			scroll = right - browserWidth;
		}
		else
		{
			scroll = currentScroll;
		}

		double maxScroll = Math.Max(0.0, contentWidth - browserWidth);
		return new LayoutSnapshot(inset, Math.Clamp(scroll, 0.0, maxScroll));
	}
}
