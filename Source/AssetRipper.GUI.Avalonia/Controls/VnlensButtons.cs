using AssetRipper.GUI.Avalonia.Theme;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace AssetRipper.GUI.Avalonia.Controls;

/// <summary>Borderless ghost toolbar buttons, solid accent/control buttons, glass surfaces, and menu items.</summary>
internal sealed class VnlensButton : Border
{
	private readonly bool _ghost;
	private readonly StackPanel _content;
	private readonly IconKind? _icon;
	private readonly double _iconSize;
	private readonly TextBlock? _labelText;
	private IBrush? _iconBrush;
	private readonly Border _iconHost = new();
	private bool _active;
	private bool _enabled = true;
	private bool _hovered;

	public event Action? Clicked;

	public bool IsActive
	{
		get => _active;
		set
		{
			if (_active != value)
			{
				_active = value;
				UpdateVisual();
			}
		}
	}

	public bool IsActionEnabled
	{
		get => _enabled;
		set
		{
			if (_enabled != value)
			{
				_enabled = value;
				UpdateVisual();
			}
		}
	}

	public bool IsPointerHovered => _hovered;

	public VnlensButton(
		IconKind? icon = null,
		string? label = null,
		bool active = false,
		bool enabled = true,
		bool ghost = true,
		double height = 30.0,
		double iconSize = 16.0)
	{
		_ghost = ghost;
		_icon = icon;
		_iconSize = iconSize;
		_active = active;
		_enabled = enabled;

		Height = height;
		MinWidth = ghost ? height : 0;
		CornerRadius = new CornerRadius(7);
		Padding = ghost
			? (label is not null ? new Thickness(7, 0, 10, 0) : new Thickness(7, 0, 7, 0))
			: new Thickness(12, 0, 12, 0);
		VerticalAlignment = VerticalAlignment.Center;

		_content = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 6,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
		};

		if (label is not null)
		{
			_labelText = new TextBlock
			{
				Text = label,
				FontFamily = Palette.UiFont,
				FontSize = 13,
				VerticalAlignment = VerticalAlignment.Center,
			};
		}

		if (_icon.HasValue)
		{
			_content.Children.Add(_iconHost);
		}
		if (_labelText is not null)
		{
			_content.Children.Add(_labelText);
		}
		Child = _content;
		UpdateVisual();

		PointerEntered += (_, _) =>
		{
			_hovered = true;
			UpdateVisual();
		};
		PointerExited += (_, _) =>
		{
			_hovered = false;
			UpdateVisual();
		};
		PointerClick.Bind(this, _ =>
		{
			if (_enabled)
			{
				Clicked?.Invoke();
			}
		});
	}

	public void SetLabel(string label)
	{
		if (_labelText is not null)
		{
			_labelText.Text = label;
		}
	}

	private void UpdateVisual()
	{
		IBrush bg;
		if (!_enabled)
		{
			bg = _ghost ? Palette.Transparent : Palette.Control;
		}
		else if (_ghost)
		{
			if (_active)
			{
				bg = _hovered ? Palette.GhostActiveHover : Palette.GhostActive;
			}
			else
			{
				bg = _hovered ? Palette.GhostHover : Palette.Transparent;
			}
		}
		else
		{
			if (_active)
			{
				bg = _hovered ? Palette.AccentHover : Palette.Accent;
			}
			else
			{
				bg = _hovered ? Palette.ControlHover : Palette.Control;
			}
		}

		IBrush fg = !_enabled
			? Palette.Disabled
			: (_ghost && _active ? Palette.AccentText : Palette.Text);

		Background = bg;
		Cursor = _enabled ? new Cursor(StandardCursorType.Hand) : Cursor.Default;

		if (_icon.HasValue && !ReferenceEquals(_iconBrush, fg))
		{
			_iconBrush = fg;
			_iconHost.Child = Icons.Glyph(_icon.Value, _iconSize, fg);
		}
		if (_labelText is not null)
		{
			_labelText.Foreground = fg;
		}
	}
}

internal sealed class VnlensMenuItem : Border
{
	public void SetLabel(string label) => _label.Text = label;

	private readonly Grid _grid;
	private readonly TextBlock _label;
	private readonly Border _iconSlot;
	private IconKind? _icon;
	private bool _enabled;
	private bool _hovered;

	public event Action? Clicked;

	public bool IsActionEnabled
	{
		get => _enabled;
		set
		{
			if (_enabled != value)
			{
				_enabled = value;
				UpdateVisual();
			}
		}
	}

	public IconKind? Icon
	{
		get => _icon;
		set
		{
			if (_icon != value)
			{
				_icon = value;
				UpdateVisual();
			}
		}
	}

	public VnlensMenuItem(IconKind? icon, string label, bool enabled = true)
	{
		_icon = icon;
		_enabled = enabled;
		Height = 28;
		Padding = new Thickness(8, 0);
		CornerRadius = new CornerRadius(6);

		_grid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("15,8,*"),
			VerticalAlignment = VerticalAlignment.Center,
		};

		_iconSlot = new Border
		{
			Width = 15,
			Height = 15,
			VerticalAlignment = VerticalAlignment.Center,
		};
		Grid.SetColumn(_iconSlot, 0);

		_label = new TextBlock
		{
			Text = label,
			FontFamily = Palette.UiFont,
			FontSize = 13,
			VerticalAlignment = VerticalAlignment.Center,
		};
		Grid.SetColumn(_label, 2);

		_grid.Children.Add(_iconSlot);
		_grid.Children.Add(_label);
		Child = _grid;
		UpdateVisual();

		PointerEntered += (_, _) =>
		{
			_hovered = true;
			UpdateVisual();
		};
		PointerExited += (_, _) =>
		{
			_hovered = false;
			UpdateVisual();
		};
		PointerClick.Bind(this, _ =>
		{
			if (_enabled)
			{
				Clicked?.Invoke();
			}
		});
	}

	private void UpdateVisual()
	{
		IBrush fg = _enabled ? Palette.Text : Palette.Disabled;
		Background = (_enabled && _hovered) ? Palette.MenuHover : Palette.Transparent;
		Cursor = _enabled ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
		_label.Foreground = fg;
		_iconSlot.Child = _icon.HasValue ? Icons.Glyph(_icon.Value, 15, fg) : null;
	}
}

internal static class GlassSurface
{
	public static Border Create(double cornerRadius = 10.0)
	{
		return new Border
		{
			Background = Palette.GlassBackground,
			BorderBrush = Palette.GlassBorder,
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(cornerRadius),
			BoxShadow = Palette.GlassShadow,
		};
	}

	public static void SetTooltip(Control target, string text)
	{
		ToolTip.SetTip(target, new Border
		{
			Background = Palette.GlassBackground,
			BorderBrush = Palette.GlassBorder,
			BorderThickness = new Thickness(1),
			CornerRadius = new CornerRadius(6),
			Padding = new Thickness(8, 4),
			BoxShadow = Palette.GlassShadow,
			Child = new TextBlock
			{
				Text = text,
				FontFamily = Palette.UiFont,
				FontSize = 11,
				Foreground = Palette.Text,
			},
		});
		ToolTip.SetShowDelay(target, 350);
	}
}
