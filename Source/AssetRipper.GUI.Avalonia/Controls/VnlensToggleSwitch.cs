using System.Diagnostics;
using AssetRipper.GUI.Avalonia.Theme;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace AssetRipper.GUI.Avalonia.Controls;

/// <summary>
/// 44x24 pill toggle switch with 180ms ease-in-out sliding white knob,
/// matching vnlens-gui/src/workspace/view.rs settings switches.
/// </summary>
internal sealed class VnlensToggleSwitch : Border
{
	private readonly Border _knob;
	private readonly DispatcherTimer _timer;
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

		Canvas track = new()
		{
			Width = 44,
			Height = 24,
		};

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

		_timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
		_timer.Tick += OnTick;

		ApplyVisual();

		PointerPressed += (_, e) =>
		{
			if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && _isEnabled)
			{
				e.Handled = true;
				SetChecked(!_isChecked, animate: true);
				Toggled?.Invoke(_isChecked);
			}
		};
	}

	public void SetChecked(bool value, bool animate)
	{
		if (_isChecked == value && !_started.HasValue)
		{
			return;
		}
		_isChecked = value;
		_from = _position;
		_target = value ? 1.0 : 0.0;
		if (animate)
		{
			_started = Stopwatch.GetTimestamp();
			_timer.Start();
		}
		else
		{
			_started = null;
			_position = _target;
			_timer.Stop();
			ApplyVisual();
		}
	}

	private void OnTick(object? sender, EventArgs e)
	{
		if (!_started.HasValue)
		{
			_timer.Stop();
			return;
		}
		double t = Stopwatch.GetElapsedTime(_started.Value).TotalSeconds / Palette.ToggleDuration.TotalSeconds;
		if (t >= 1.0)
		{
			_position = _target;
			_started = null;
			_timer.Stop();
		}
		else
		{
			double eased = LayoutMotion.EaseInOut(t);
			_position = _from + (_target - _from) * eased;
		}
		ApplyVisual();
	}

	private void ApplyVisual()
	{
		Canvas.SetLeft(_knob, 2.0 + 20.0 * _position);
		Color off = Palette.ControlHoverColor;
		Color on = Palette.AccentColor;
		byte r = (byte)Math.Round(off.R + (on.R - off.R) * _position);
		byte g = (byte)Math.Round(off.G + (on.G - off.G) * _position);
		byte b = (byte)Math.Round(off.B + (on.B - off.B) * _position);
		Background = new SolidColorBrush(Color.FromRgb(r, g, b));
	}
}
