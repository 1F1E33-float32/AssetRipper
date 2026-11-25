using System.Diagnostics;
using AssetRipper.GUI.Avalonia.Theme;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace AssetRipper.GUI.Avalonia.Controls;

/// <summary>44x24 pill toggle switch with 180ms ease-in-out sliding knob.</summary>
internal sealed class VnlensToggleSwitch : Border
{
	private readonly Border _knob;
	private readonly FrameLoop _frames;
	private bool _isChecked;
	private bool _isEnabled = true;
	private double _position; // 0.0 (off) to 1.0 (on)
	private double _from;
	private double _target;
	private long? _started;

	public event Action<bool>? Toggled;

	public bool IsChecked
	{
		get => _isChecked;
		set => SetChecked(value, animate: false);
	}

	public bool IsSwitchEnabled
	{
		get => _isEnabled;
		set
		{
			_isEnabled = value;
			Opacity = _isEnabled ? 1.0 : 0.45;
			Cursor = _isEnabled ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
		}
	}

	public VnlensToggleSwitch(bool initial = false)
	{
		_isChecked = initial;
		_position = initial ? 1.0 : 0.0;
		_from = _position;
		_target = _position;

		Width = 44;
		Height = 24;
		CornerRadius = new CornerRadius(12);
		Cursor = new Cursor(StandardCursorType.Hand);

		Canvas track = new() { Width = 44, Height = 24 };

		_knob = new Border
		{
			Width = 20,
			Height = 20,
			CornerRadius = new CornerRadius(10),
			Background = Palette.White,
			BoxShadow = Palette.KnobShadow,
		};

		Canvas.SetTop(_knob, 2);
		track.Children.Add(_knob);
		Child = track;

		_frames = new FrameLoop(this, OnFrame);

		ApplyVisual();

		PointerClick.Bind(
			this,
			_ =>
			{
				if (_isEnabled)
				{
					SetChecked(!_isChecked, animate: true);
					Toggled?.Invoke(_isChecked);
				}
			}
		);
	}

	public void SetChecked(bool value, bool animate)
	{
		if (_isChecked == value && !_started.HasValue)
		{
			return;
		}
		if (_started.HasValue)
		{
			OnFrame();
		}
		_isChecked = value;
		_from = _position;
		_target = value ? 1.0 : 0.0;
		if (animate)
		{
			_started = Stopwatch.GetTimestamp();
			_frames.Start();
		}
		else
		{
			_started = null;
			_position = _target;
			_frames.Stop();
			ApplyVisual();
		}
	}

	private bool OnFrame()
	{
		if (!_started.HasValue)
		{
			return false;
		}
		double t = Stopwatch.GetElapsedTime(_started.Value).TotalSeconds / Palette.ToggleDuration.TotalSeconds;
		if (t >= 1.0)
		{
			_position = _target;
			_started = null;
			_frames.Stop();
		}
		else
		{
			double eased = LayoutMotion.EaseInOut(t);
			_position = _from + (_target - _from) * eased;
		}
		ApplyVisual();
		return _started.HasValue;
	}

	private void ApplyVisual()
	{
		Canvas.SetLeft(_knob, 2.0 + 20.0 * _position);
		Background = _isChecked ? Palette.Accent : Palette.ControlHover;
	}
}
