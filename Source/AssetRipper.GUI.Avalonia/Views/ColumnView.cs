using System.Diagnostics;
using AssetRipper.GUI.Avalonia.Browser;
using AssetRipper.GUI.Avalonia.Theme;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace AssetRipper.GUI.Avalonia.Views;

/// <summary>
/// Single Finder-style Miller column matching vnlens-gui/src/workspace/browser_view.rs::column.
/// Supports 180ms opacity enter animation, resizable right-edge handle (double-click to reset to 220px),
/// virtualized 26px rows, active/inactive selection styling, and optional class filter for collections.
/// </summary>
internal sealed class ColumnView : Grid
{
	private readonly List<BrowserItem> _allItems;
	private List<BrowserItem> _filteredItems;
	private readonly ListBox _listBox;
	private readonly Border _filterBar;
	private readonly ComboBox _classFilterCombo;
	private readonly TextBlock _emptyLabel;
	private readonly Border _resizeHandle;
	private readonly DispatcherTimer _enterTimer;
	private long _enterStarted;

	private int _depth;
	private bool _isActiveColumn;
	private int? _selectedIndex;
	private bool _suppressSelectionEvent;
	private bool _resizing;
	private double _resizeStartScreenX;
	private double _resizeStartWidth;

	public int Depth => _depth;
	public double ColumnWidth { get; private set; } = Palette.ColumnWidth;
	public BrowserItem? SelectedItem =>
		_selectedIndex.HasValue && _selectedIndex.Value >= 0 && _selectedIndex.Value < _filteredItems.Count
			? _filteredItems[_selectedIndex.Value]
			: null;
	public int? SelectedIndex => _selectedIndex;
	public int ItemCount => _filteredItems.Count;

	public event Action<ColumnView, int, BrowserItem, bool>? ItemActivated;
	public event Action<ColumnView>? ColumnClicked;
	public event Action<ColumnView, double>? WidthChangedByUser;

	public ColumnView(int depth, List<BrowserItem> items, bool animateEnter)
	{
		_depth = depth;
		_allItems = items;
		_filteredItems = items;

		Width = ColumnWidth;
		HorizontalAlignment = HorizontalAlignment.Left;
		VerticalAlignment = VerticalAlignment.Stretch;
		Background = depth == 0 ? Palette.RootColumnBackground : Palette.Transparent;

		Border rightBorder = new()
		{
			BorderBrush = Palette.ColumnBorder,
			BorderThickness = new Thickness(0, 0, 1, 0),
			IsHitTestVisible = false,
		};
		Children.Add(rightBorder);

		Grid innerGrid = new()
		{
			RowDefinitions = new RowDefinitions("Auto,*"),
		};

		// Optional compact Unity class filter when column contains multiple asset classes
		string[] distinctClasses = items
			.Where(i => i.ClassName is not null)
			.Select(i => i.ClassName!)
			.Distinct(StringComparer.Ordinal)
			.OrderBy(s => s, StringComparer.Ordinal)
			.ToArray();

		_classFilterCombo = new ComboBox
		{
			HorizontalAlignment = HorizontalAlignment.Stretch,
			Height = 26,
			MinHeight = 26,
			FontFamily = Palette.UiFont,
			FontSize = 11,
			Padding = new Thickness(8, 2),
		};
		_filterBar = new Border
		{
			Padding = new Thickness(6, 6, 6, 2),
			IsVisible = distinctClasses.Length > 1,
			Child = _classFilterCombo,
		};

		if (distinctClasses.Length > 1)
		{
			List<string> filterOptions = new(distinctClasses.Length + 1) { $"All ({items.Count})" };
			filterOptions.AddRange(distinctClasses);
			_classFilterCombo.ItemsSource = filterOptions;
			_classFilterCombo.SelectedIndex = 0;
			_classFilterCombo.SelectionChanged += (_, _) => ApplyClassFilter();
		}

		Grid.SetRow(_filterBar, 0);
		innerGrid.Children.Add(_filterBar);

		_listBox = new ListBox
		{
			Background = Palette.Transparent,
			BorderThickness = new Thickness(0),
			Padding = new Thickness(0, 4, 0, 50),
			ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
			ItemTemplate = new FuncDataTemplate<BrowserItem>(BuildRowVisual),
			ItemsSource = _filteredItems,
		};
		ScrollViewer.SetHorizontalScrollBarVisibility(_listBox, ScrollBarVisibility.Disabled);
		ScrollViewer.SetVerticalScrollBarVisibility(_listBox, ScrollBarVisibility.Auto);

		_listBox.SelectionChanged += OnListSelectionChanged;
		_listBox.DoubleTapped += (_, _) =>
		{
			if (SelectedItem is { } item && _selectedIndex.HasValue)
			{
				ItemActivated?.Invoke(this, _selectedIndex.Value, item, true);
			}
		};

		Grid.SetRow(_listBox, 1);
		innerGrid.Children.Add(_listBox);

		_emptyLabel = new TextBlock
		{
			Text = "No items",
			FontFamily = Palette.UiFont,
			FontSize = 12,
			Foreground = Palette.Secondary,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(0, 0, 0, 50),
			IsVisible = depth > 0 && items.Count == 0,
			IsHitTestVisible = false,
		};
		Grid.SetRow(_emptyLabel, 1);
		innerGrid.Children.Add(_emptyLabel);

		Children.Add(innerGrid);

		// 5px right-edge column resize handle (matching browser_view.rs lines 456-481)
		_resizeHandle = new Border
		{
			Width = 5,
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Stretch,
			Background = Palette.Transparent,
			Cursor = new Cursor(StandardCursorType.SizeWestEast),
		};
		_resizeHandle.PointerEntered += (_, _) => _resizeHandle.Background = Palette.ResizeHover;
		_resizeHandle.PointerExited += (_, _) =>
		{
			if (!_resizing)
			{
				_resizeHandle.Background = Palette.Transparent;
			}
		};
		_resizeHandle.PointerPressed += OnResizePressed;
		_resizeHandle.PointerMoved += OnResizeMoved;
		_resizeHandle.PointerReleased += OnResizeReleased;
		Children.Add(_resizeHandle);

		PointerPressed += (_, e) =>
		{
			if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
			{
				ColumnClicked?.Invoke(this);
			}
		};

		// 180ms ease-in-out column-enter fade animation
		_enterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
		_enterTimer.Tick += (_, _) =>
		{
			double t = Stopwatch.GetElapsedTime(_enterStarted).TotalSeconds / Palette.ColumnEnterDuration.TotalSeconds;
			if (t >= 1.0)
			{
				Opacity = 1.0;
				_enterTimer.Stop();
			}
			else
			{
				Opacity = LayoutMotion.EaseInOut(t);
			}
		};

		if (animateEnter)
		{
			Opacity = 0.0;
			AttachedToVisualTree += (_, _) =>
			{
				_enterStarted = Stopwatch.GetTimestamp();
				_enterTimer.Start();
			};
		}
	}

	private Control BuildRowVisual(BrowserItem? item, INameScope _)
	{
		if (item is null)
		{
			return new Border { Height = Palette.RowHeight };
		}

		// Outer wrapper matches h(26), px(6)
		Border outer = new()
		{
			Height = Palette.RowHeight,
			Padding = new Thickness(6, 0),
			Background = Palette.Transparent,
		};

		Border pill = new()
		{
			CornerRadius = new CornerRadius(6),
			Padding = new Thickness(7, 0),
			Background = Palette.Transparent,
			Cursor = new Cursor(StandardCursorType.Hand),
			Tag = item,
		};

		Grid row = new()
		{
			ColumnDefinitions = item.IsContainer
				? new ColumnDefinitions("Auto,7,*,6,Auto,5,Auto")
				: new ColumnDefinitions("Auto,7,*,6,Auto"),
			VerticalAlignment = VerticalAlignment.Center,
		};

		Border iconHost = new()
		{
			Width = 17,
			Height = 17,
			VerticalAlignment = VerticalAlignment.Center,
			Child = Icons.FileIcon(item.Icon, false),
		};
		Grid.SetColumn(iconHost, 0);
		row.Children.Add(iconHost);

		TextBlock nameBlock = new()
		{
			Text = item.Name,
			FontFamily = Palette.UiFont,
			FontSize = 13,
			Foreground = Palette.Text,
			TextTrimming = TextTrimming.CharacterEllipsis,
			VerticalAlignment = VerticalAlignment.Center,
		};
		Grid.SetColumn(nameBlock, 2);
		row.Children.Add(nameBlock);

		TextBlock metaBlock = new()
		{
			Text = item.Meta,
			FontFamily = Palette.UiFont,
			FontSize = 11,
			Foreground = Palette.MutedDefault,
			VerticalAlignment = VerticalAlignment.Center,
		};
		Grid.SetColumn(metaBlock, 4);
		row.Children.Add(metaBlock);

		Border? chevronHost = null;
		if (item.IsContainer)
		{
			chevronHost = new Border
			{
				Width = 11,
				Height = 11,
				VerticalAlignment = VerticalAlignment.Center,
				Child = Icons.Glyph(IconKind.ChevronRight, 11, Palette.MutedDefault),
			};
			Grid.SetColumn(chevronHost, 6);
			row.Children.Add(chevronHost);
		}

		pill.Child = row;
		outer.Child = pill;

		void UpdateRowStyle(bool hovered)
		{
			bool isSelected = ReferenceEquals(SelectedItem, item);
			bool highlighted = isSelected && _isActiveColumn;

			if (!isSelected)
			{
				pill.Background = hovered ? Palette.RowHover : Palette.Transparent;
			}
			else if (_isActiveColumn)
			{
				pill.Background = Palette.Accent;
			}
			else
			{
				pill.Background = Palette.RowSelectedInactive;
			}

			IBrush muted = highlighted ? Palette.MutedOnAccent : Palette.MutedDefault;
			metaBlock.Foreground = muted;
			iconHost.Child = Icons.FileIcon(item.Icon, highlighted);
			if (chevronHost is not null)
			{
				chevronHost.Child = Icons.Glyph(IconKind.ChevronRight, 11, muted);
			}
		}

		outer.Tag = (Action<bool>)UpdateRowStyle;
		UpdateRowStyle(false);

		pill.PointerEntered += (_, _) => UpdateRowStyle(true);
		pill.PointerExited += (_, _) => UpdateRowStyle(false);

		return outer;
	}

	private void OnListSelectionChanged(object? sender, SelectionChangedEventArgs e)
	{
		if (_suppressSelectionEvent)
		{
			return;
		}
		int idx = _listBox.SelectedIndex;
		if (idx >= 0 && idx < _filteredItems.Count)
		{
			_selectedIndex = idx;
			RefreshVisibleRowVisuals();
			ItemActivated?.Invoke(this, idx, _filteredItems[idx], false);
		}
	}

	public void SetActive(bool active)
	{
		if (_isActiveColumn != active)
		{
			_isActiveColumn = active;
			RefreshVisibleRowVisuals();
		}
	}

	public void SelectIndex(int? index, bool raiseEvent = true)
	{
		_selectedIndex = index;
		_suppressSelectionEvent = !raiseEvent;
		try
		{
			_listBox.SelectedIndex = index ?? -1;
			if (index.HasValue && index.Value >= 0 && index.Value < _filteredItems.Count)
			{
				_listBox.ScrollIntoView(_filteredItems[index.Value]);
			}
		}
		finally
		{
			_suppressSelectionEvent = false;
		}
		RefreshVisibleRowVisuals();
	}

	public bool SelectTarget(object target, bool raiseEvent = true)
	{
		for (int i = 0; i < _filteredItems.Count; i++)
		{
			if (ReferenceEquals(_filteredItems[i].Target, target))
			{
				SelectIndex(i, raiseEvent);
				return true;
			}
		}
		// If filtered out, clear filter and retry
		if (_filterBar.IsVisible && _classFilterCombo.SelectedIndex > 0)
		{
			_classFilterCombo.SelectedIndex = 0;
			for (int i = 0; i < _filteredItems.Count; i++)
			{
				if (ReferenceEquals(_filteredItems[i].Target, target))
				{
					SelectIndex(i, raiseEvent);
					return true;
				}
			}
		}
		return false;
	}

	public void MoveSelection(int delta)
	{
		if (_filteredItems.Count == 0)
		{
			return;
		}
		int next = _selectedIndex.HasValue
			? Math.Clamp(_selectedIndex.Value + delta, 0, _filteredItems.Count - 1)
			: (delta > 0 ? 0 : _filteredItems.Count - 1);
		SelectIndex(next, raiseEvent: true);
	}

	private void ApplyClassFilter()
	{
		int sel = _classFilterCombo.SelectedIndex;
		if (sel <= 0 || _classFilterCombo.SelectedItem is not string chosen)
		{
			_filteredItems = _allItems;
		}
		else
		{
			_filteredItems = _allItems.Where(i => string.Equals(i.ClassName, chosen, StringComparison.Ordinal)).ToList();
		}

		_suppressSelectionEvent = true;
		_selectedIndex = null;
		_listBox.SelectedIndex = -1;
		_listBox.ItemsSource = _filteredItems;
		_suppressSelectionEvent = false;
	}

	private void RefreshVisibleRowVisuals()
	{
		foreach (Control container in _listBox.GetRealizedContainers())
		{
			if (container is ListBoxItem lbi && lbi.Presenter?.Child is Border outer && outer.Tag is Action<bool> updater)
			{
				updater(false);
			}
		}
	}

	private void OnResizePressed(object? sender, PointerPressedEventArgs e)
	{
		if (!e.GetCurrentPoint(_resizeHandle).Properties.IsLeftButtonPressed)
		{
			return;
		}
		if (e.ClickCount == 2)
		{
			SetColumnWidth(Palette.ColumnWidth);
			e.Handled = true;
			return;
		}
		_resizing = true;
		_resizeStartScreenX = e.GetPosition(VisualRoot as Visual).X;
		_resizeStartWidth = ColumnWidth;
		e.Pointer.Capture(_resizeHandle);
		e.Handled = true;
	}

	private void OnResizeMoved(object? sender, PointerEventArgs e)
	{
		if (!_resizing)
		{
			return;
		}
		double currentX = e.GetPosition(VisualRoot as Visual).X;
		double newWidth = Math.Clamp(_resizeStartWidth + (currentX - _resizeStartScreenX), Palette.ColumnMinWidth, Palette.ColumnMaxWidth);
		SetColumnWidth(newWidth);
		e.Handled = true;
	}

	private void OnResizeReleased(object? sender, PointerReleasedEventArgs e)
	{
		if (_resizing)
		{
			_resizing = false;
			_resizeHandle.Background = Palette.Transparent;
			e.Pointer.Capture(null);
			e.Handled = true;
		}
	}

	private void SetColumnWidth(double width)
	{
		ColumnWidth = width;
		Width = width;
		WidthChangedByUser?.Invoke(this, width);
	}
}
