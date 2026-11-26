using System.Diagnostics;
using AssetRipper.GUI.Avalonia.Browser;
using AssetRipper.GUI.Avalonia.Controls;
using AssetRipper.GUI.Avalonia.Theme;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace AssetRipper.GUI.Avalonia.Views;

/// <summary>
/// Finder-style Miller column with fade-in animation, resizable edge handle,
/// virtualized rows, and optional class filter for collections.
/// </summary>
internal sealed class ColumnView : Grid
{
	private readonly List<BrowserItem> _allItems;
	private readonly Dictionary<BrowserItem, BrowserItem> _loadedItems = new();
	private List<BrowserItem> _filteredItems;
	private readonly ListBox _listBox;
	private readonly Border _filterBar;
	private readonly ComboBox _classFilterCombo;
	private readonly TextBlock _emptyLabel;
	private readonly Border _resizeHandle;
	private readonly FrameLoop _enterFrames;
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
			? ResolveItem(_filteredItems[_selectedIndex.Value])
			: null;
	public int? SelectedIndex => _selectedIndex;
	public int ItemCount => _filteredItems.Count;
	public List<BrowserItem> Items => _allItems;

	public event Action<ColumnView, int, BrowserItem, bool>? ItemActivated;
	public event Action<ColumnView>? ColumnClicked;
	public event Action<ColumnView, double>? WidthChangedByUser;
	public event Action<ColumnView>? SelectionCleared;

	public BrowserItem ResolveItem(BrowserItem item) => _loadedItems.GetValueOrDefault(item, item);

	public void SetLoadedItem(BrowserItem original, BrowserItem loaded) => _loadedItems[original] = loaded;

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

		Grid innerGrid = new() { RowDefinitions = new RowDefinitions("Auto,*") };

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
			AutoScrollToSelectedItem = false,
			ItemsSource = _filteredItems,
		};
		ScrollViewer.SetHorizontalScrollBarVisibility(_listBox, ScrollBarVisibility.Disabled);
		ScrollViewer.SetVerticalScrollBarVisibility(_listBox, ScrollBarVisibility.Auto);
		ScrollViewer.SetIsScrollChainingEnabled(_listBox, false);

		_listBox.SelectionChanged += OnListSelectionChanged;

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

		// Right-edge column resize handle
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
		_resizeHandle.PointerCaptureLost += (_, _) =>
		{
			_resizing = false;
			_resizeHandle.Background = Palette.Transparent;
		};
		Children.Add(_resizeHandle);

		PointerPressed += (_, e) =>
		{
			if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
			{
				ColumnClicked?.Invoke(this);
			}
		};

		// 180ms ease-in-out column-enter fade animation
		_enterFrames = new FrameLoop(
			this,
			() =>
			{
				double t = Stopwatch.GetElapsedTime(_enterStarted).TotalSeconds / Palette.ColumnEnterDuration.TotalSeconds;
				Opacity = LayoutMotion.EaseInOut(t);
				return t < 1.0;
			}
		);

		bool enterPending = animateEnter;
		Opacity = animateEnter ? 0.0 : 1.0;
		AttachedToVisualTree += (_, _) =>
		{
			if (enterPending)
			{
				enterPending = false;
				_enterStarted = Stopwatch.GetTimestamp();
				_enterFrames.Start();
			}
		};
	}

	private Control BuildRowVisual(BrowserItem? item, INameScope _)
	{
		if (item is null)
		{
			return new Border { Height = Palette.RowHeight };
		}

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
		if (item.CompanionNames.Count > 0)
		{
			pill.BorderThickness = new Thickness(1);
			pill.BorderBrush = Palette.GlassBorder;
			GlassSurface.SetTooltip(pill, string.Join('\n', new[] { item.Name }.Concat(item.CompanionNames)));
		}

		Grid row = new()
		{
			ColumnDefinitions = item.IsContainer ? new ColumnDefinitions("Auto,7,*,6,Auto,5,Auto") : new ColumnDefinitions("Auto,7,*,6,Auto"),
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
			bool isSelected = ReferenceEquals(SelectedItem, ResolveItem(item));
			bool highlighted = isSelected && _isActiveColumn;

			if (!isSelected)
			{
				pill.Background =
					hovered ? Palette.RowHover
					: item.CompanionNames.Count > 0 ? Palette.RowGroupBg
					: Palette.Transparent;
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

		int clickCount = 0;
		pill.PointerPressed += (_, e) => clickCount = e.ClickCount;
		PointerClick.Bind(
			pill,
			_ =>
			{
				int index = _filteredItems.IndexOf(item);
				SelectIndex(index, raiseEvent: false);
				ItemActivated?.Invoke(this, index, ResolveItem(item), clickCount == 2);
			}
		);

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
			ItemActivated?.Invoke(this, idx, ResolveItem(_filteredItems[idx]), false);
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
		_suppressSelectionEvent = true;
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
		if (raiseEvent && SelectedItem is { } selected && index.HasValue)
		{
			ItemActivated?.Invoke(this, index.Value, selected, false);
		}
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
		int next = _selectedIndex.HasValue ? Math.Clamp(_selectedIndex.Value + delta, 0, _filteredItems.Count - 1) : 0;
		SelectIndex(next, raiseEvent: false);
		if (_listBox.Scroll is { } scroll)
		{
			double center = (next + 0.5) * Palette.RowHeight + _listBox.Padding.Top;
			scroll.Offset = new Vector(0, Math.Max(0, center - scroll.Viewport.Height / 2));
		}
		ItemActivated?.Invoke(this, next, ResolveItem(_filteredItems[next]), false);
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
		_emptyLabel.IsVisible = _depth > 0 && _filteredItems.Count == 0;
		SelectionCleared?.Invoke(this);
	}

	private void RefreshVisibleRowVisuals()
	{
		foreach (Control container in _listBox.GetRealizedContainers())
		{
			if (container is ListBoxItem lbi && lbi.Presenter?.Child is Border outer && outer.Tag is Action<bool> updater)
			{
				updater(outer.IsPointerOver);
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
