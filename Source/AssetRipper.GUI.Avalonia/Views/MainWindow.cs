using System.Diagnostics;
using AssetRipper.Assets;
using AssetRipper.Assets.Bundles;
using AssetRipper.Assets.Collections;
using AssetRipper.Export.Configuration;
using AssetRipper.GUI.Avalonia.Browser;
using AssetRipper.GUI.Avalonia.Controls;
using AssetRipper.GUI.Avalonia.Services;
using AssetRipper.GUI.Avalonia.Theme;
using AssetRipper.GUI.Localizations;
using AssetRipper.Import.GamePlugins;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.ResourceFiles;
using AssetRipper.SourceGenerated.Classes.ClassID_43;
using AssetRipper.SourceGenerated.Classes.ClassID_83;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AssetRipper.GUI.Avalonia.Views;

/// <summary>
/// Main desktop window with frameless titlebar, Miller column browser, animated inspector
/// and scroll motion, floating status dock, breadcrumb path bar, and welcome drop target.
/// </summary>
internal sealed class MainWindow : Window
{
	// Root layers
	private readonly Grid _rootGrid;
	private readonly Grid _workspaceLayer;
	private readonly Grid _settingsLayer;

	// Main titlebar & toolbar controls
	private readonly VnlensButton _homeBtn;
	private readonly VnlensButton _backBtn;
	private readonly Border _searchContainer;
	private readonly TextBox _searchBox;
	private readonly VnlensButton _exportBtn;
	private readonly VnlensButton _inspectorBtn;
	private readonly VnlensButton _settingsBtn;
	private readonly Border _maxIconHost;
	private readonly Border _settingsMaxIconHost;

	// Browser strip & welcome overlay
	private readonly ScrollViewer _stripScroll;
	private readonly StackPanel _columnsPanel;
	private readonly Grid _welcomeOverlay;
	private readonly Border _welcomeCard;

	// Inspector & resize handle
	private readonly Border _inspectorContainer;
	private readonly InspectorView _inspectorView;
	private readonly Border _inspectorResizeHandle;

	// Floating status dock & popovers
	private readonly Border _statusTile;
	private readonly Border _statusIconHost;
	private readonly Border _statusBadgeDot;
	private readonly Border _statusPopover;
	private readonly ActivityLogView _activityLogView;
	private int _statusRefreshPending;
	private bool _closed;
	private readonly Border _pathBarGlass;
	private readonly StackPanel _pathSegmentsPanel;
	private readonly VnlensButton _exportPreviewBtn;
	private readonly Border _exportMenuOverlay;
	private readonly Border _exportMenuCard;
	private readonly VnlensMenuItem _menuExportUnity;
	private readonly VnlensMenuItem _menuExportPrimary;
	private readonly VnlensMenuItem _menuExportSelected;
	private readonly VnlensMenuItem _menuTimestampSubfolder;
	private readonly VnlensMenuItem _menuReset;
	private readonly Canvas _pathContextOverlay;
	private readonly Border _pathContextCard;
	private readonly VnlensMenuItem _copyPathMenuItem;

	// Confirmation overlay
	private readonly Grid _confirmOverlay;
	private readonly TextBlock _confirmMessage;
	private TaskCompletionSource<bool>? _confirmTcs;

	// Settings view
	private readonly SettingsView _settingsView;

	// Browser & layout animation state
	private readonly List<ColumnView> _columns = new();
	private int _activeColumn;
	private BrowserItem? _selectedLeaf;
	private int? _selectedLeafDepth;
	private bool _showInspector = true;
	private bool _showSettings;
	private bool _showStatus;
	private bool _showExportMenu;
	private bool _createTimestampSubfolder;
	private bool _exporting;
	private bool _loading;
	private bool _showingSearch;
	private AssetPathDisplayMode _browserPathMode = GameFileLoader.Settings.ExportSettings.AssetPathDisplayMode;
	private List<BrowserItem>? _internalPathItems;
	private CancellationTokenSource? _pathIndexCancellation;
	private string? _lastExportOutputPath;
	private string _contextPathToCopy = "";

	private readonly LayoutMotion _layoutMotion = new();
	private readonly FrameLoop _layoutFrames;
	private double? _pendingLayoutScroll;
	private bool _revealPath;
	private double? _manualInspectorWidth;
	private double _currentInset;
	private bool _resizingInspector;
	private double _resizeInspectorStartX;
	private double _resizeInspectorStartWidth;
	private int? _revealColumnDepth;
	private double _lastViewportWidth;

	// Menu fade animation
	private readonly FrameLoop _fadeFrames;
	private Control? _fadingControl;
	private long _fadeStarted;

	public MainWindow()
	{
		Title = "AssetRipper";
		Width = 1280;
		Height = 800;
		MinWidth = 960;
		MinHeight = 600;
		WindowStartupLocation = WindowStartupLocation.CenterScreen;
		Background = Palette.Background;
		Foreground = Palette.Text;
		FontFamily = Palette.UiFont;
		FontSize = 13;

		// Custom frameless titlebar
		ExtendClientAreaToDecorationsHint = true;
		ExtendClientAreaTitleBarHeightHint = -1;
		WindowDecorations = WindowDecorations.BorderOnly;

		DragDrop.SetAllowDrop(this, true);
		AddHandler(DragDrop.DragOverEvent, OnDragOver);
		AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
		AddHandler(DragDrop.DropEvent, OnDrop);

		_rootGrid = new Grid { Focusable = true, Background = Palette.Background };

		// ================================================================
		// 1. WORKSPACE LAYER
		// ================================================================
		_workspaceLayer = new Grid { RowDefinitions = new RowDefinitions("40,*") };

		// Main Titlebar (40px)
		_homeBtn = new VnlensButton(IconKind.Home, enabled: false, ghost: true);
		GlassSurface.SetTooltip(_homeBtn, "Root bundle (Alt+Home)");
		_homeBtn.Clicked += GoHome;

		_backBtn = new VnlensButton(IconKind.ChevronLeft, enabled: false, ghost: true);
		GlassSurface.SetTooltip(_backBtn, "Back (Alt+←)");
		_backBtn.Clicked += GoBack;

		VnlensButton openFolderBtn = new(IconKind.FolderOpen, "Open folder…", ghost: true);
		GlassSurface.SetTooltip(openFolderBtn, Shortcut("Shift+O"));
		openFolderBtn.Clicked += () => _ = OpenFolderDialogAsync();

		VnlensButton openFilesBtn = new(IconKind.FilePlus, "Files…", ghost: true);
		GlassSurface.SetTooltip(openFilesBtn, Shortcut("O"));
		openFilesBtn.Clicked += () => _ = OpenFilesDialogAsync();

		// Sleek compact search pill (visible when loaded)
		_searchBox = new TextBox
		{
			PlaceholderText = "Search assets… (Ctrl+F)",
			Width = 210,
			Height = 26,
			MinHeight = 26,
			FontFamily = Palette.UiFont,
			FontSize = 12,
			Padding = new Thickness(8, 3),
			Background = Palette.Transparent,
			BorderThickness = new Thickness(0),
			Foreground = Palette.Text,
			VerticalContentAlignment = VerticalAlignment.Center,
		};
		_searchBox.KeyDown += (_, e) =>
		{
			if (e.Key == Key.Enter)
			{
				e.Handled = true;
				PerformSearch(_searchBox.Text ?? "");
			}
		};

		StackPanel searchInner = new()
		{
			Orientation = Orientation.Horizontal,
			Spacing = 4,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(8, 0, 4, 0),
		};
		searchInner.Children.Add(Icons.Glyph(IconKind.Search, 13, Palette.Secondary));
		searchInner.Children.Add(_searchBox);

		_searchContainer = new Border
		{
			Height = 28,
			CornerRadius = new CornerRadius(7),
			Background = Palette.PillBg,
			BorderBrush = Palette.Border,
			BorderThickness = new Thickness(1),
			VerticalAlignment = VerticalAlignment.Center,
			IsVisible = false,
			Child = searchInner,
		};

		_exportBtn = new VnlensButton(IconKind.Export, "Export…", active: false, enabled: true, ghost: true);
		GlassSurface.SetTooltip(_exportBtn, $"Export Unity project ({Shortcut("E")})");
		_exportBtn.Clicked += ToggleExportMenu;

		_inspectorBtn = new VnlensButton(IconKind.Sidebar, active: false, enabled: false, ghost: true);
		GlassSurface.SetTooltip(_inspectorBtn, $"Preview ({Shortcut("I")})");
		_inspectorBtn.Clicked += ToggleInspector;

		_settingsBtn = new VnlensButton(IconKind.Settings, active: false, enabled: true, ghost: true);
		GlassSurface.SetTooltip(_settingsBtn, $"Settings ({Shortcut(",")})");
		_settingsBtn.Clicked += ToggleSettings;

		Control mainTitlebar = BuildTitlebar(
			leftControls:
			[
				_homeBtn,
				_backBtn,
				new Border
				{
					Width = 1,
					Height = 16,
					Margin = new Thickness(4, 0),
					Background = Palette.Border,
					VerticalAlignment = VerticalAlignment.Center,
				},
				openFolderBtn,
				openFilesBtn,
			],
			centerControl: _searchContainer,
			rightControls: [_exportBtn, _inspectorBtn, _settingsBtn],
			out _maxIconHost
		);
		Grid.SetRow(mainTitlebar, 0);
		_workspaceLayer.Children.Add(mainTitlebar);

		// Main Split Content Area (Column Browser + Animated Inspector)
		Grid bodyGrid = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
		Grid.SetRow(bodyGrid, 1);

		// Column Strip
		_columnsPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Stretch };
		_columnsPanel.Children.Add(new Border { Width = 24 });

		_stripScroll = new ScrollViewer
		{
			Focusable = true,
			BringIntoViewOnFocusChange = false,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
			VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
			Content = _columnsPanel,
		};
		// Inner lists reveal rows vertically; only LayoutMotion moves the strip horizontally.
		_stripScroll.AddHandler(RequestBringIntoViewEvent, (_, e) => e.Handled = true);
		_stripScroll.LayoutUpdated += (_, _) => ApplyPendingLayoutScroll();
		_stripScroll.AddHandler(PointerWheelChangedEvent, OnBrowserWheel, RoutingStrategies.Tunnel);

		// Welcome drop overlay
		(_welcomeOverlay, _welcomeCard) = BuildWelcomeOverlay();

		Grid browserHost = new() { ClipToBounds = true };
		browserHost.Children.Add(_stripScroll);
		browserHost.Children.Add(_welcomeOverlay);
		Grid.SetColumn(browserHost, 0);
		bodyGrid.Children.Add(browserHost);

		// Right-hand Inspector Container (animated width via LayoutMotion)
		_inspectorView = new InspectorView { Focusable = true };
		_inspectorView.AddHandler(PointerPressedEvent, (_, _) => _inspectorView.Focus(), RoutingStrategies.Tunnel);
		_inspectorView.ReferenceClicked += NavigateToReference;

		_inspectorResizeHandle = new Border
		{
			Width = 5,
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Stretch,
			Background = Palette.Transparent,
			Cursor = new Cursor(StandardCursorType.SizeWestEast),
		};
		_inspectorResizeHandle.PointerEntered += (_, _) => _inspectorResizeHandle.Background = Palette.ResizeHover;
		_inspectorResizeHandle.PointerExited += (_, _) =>
		{
			if (!_resizingInspector)
			{
				_inspectorResizeHandle.Background = Palette.Transparent;
			}
		};
		_inspectorResizeHandle.PointerPressed += OnInspectorResizePressed;
		_inspectorResizeHandle.PointerMoved += OnInspectorResizeMoved;
		_inspectorResizeHandle.PointerReleased += OnInspectorResizeReleased;
		_inspectorResizeHandle.PointerCaptureLost += (_, _) =>
		{
			_resizingInspector = false;
			_inspectorResizeHandle.Background = Palette.Transparent;
		};

		Grid inspectorStack = new();
		inspectorStack.Children.Add(_inspectorView);
		inspectorStack.Children.Add(_inspectorResizeHandle);

		_inspectorContainer = new Border
		{
			Width = 0,
			IsVisible = false,
			Background = Palette.Panel,
			BorderBrush = Palette.Border,
			BorderThickness = new Thickness(1, 0, 0, 0),
			ClipToBounds = true,
			Child = inspectorStack,
		};
		Grid.SetColumn(_inspectorContainer, 1);
		bodyGrid.Children.Add(_inspectorContainer);

		_workspaceLayer.Children.Add(bodyGrid);
		_rootGrid.Children.Add(_workspaceLayer);

		// ================================================================
		// 2. SETTINGS LAYER
		// ================================================================
		_settingsLayer = new Grid
		{
			RowDefinitions = new RowDefinitions("40,*"),
			IsVisible = false,
			Background = Palette.Background,
		};

		VnlensButton closeSettingsBtn = new(IconKind.ChevronLeft, enabled: true, ghost: true);
		GlassSurface.SetTooltip(closeSettingsBtn, "Back to browser (Esc)");
		closeSettingsBtn.Clicked += ToggleSettings;

		TextBlock settingsTitle = new()
		{
			Text = Localization.Settings,
			FontFamily = Palette.UiFont,
			FontSize = 13,
			FontWeight = FontWeight.SemiBold,
			Foreground = Palette.Text,
			VerticalAlignment = VerticalAlignment.Center,
		};

		Control settingsTitlebar = BuildTitlebar(
			leftControls: [closeSettingsBtn, settingsTitle],
			centerControl: null,
			rightControls: [],
			out _settingsMaxIconHost
		);
		Grid.SetRow(settingsTitlebar, 0);
		_settingsLayer.Children.Add(settingsTitlebar);

		_settingsView = new SettingsView();
		_settingsView.ResetRequested += ResetLoadedFiles;
		_settingsView.LanguageChanged += () => settingsTitle.Text = Localization.Settings;
		Grid.SetRow(_settingsView, 1);
		_settingsLayer.Children.Add(_settingsView);

		_rootGrid.Children.Add(_settingsLayer);

		// ================================================================
		// 3. FLOATING STATUS DOCK & POPOVERS
		// ================================================================
		(_statusTile, _statusIconHost, _statusBadgeDot, _statusPopover, _activityLogView, _pathBarGlass, _pathSegmentsPanel, _exportPreviewBtn) =
			BuildStatusDock();

		// Export dropdown menu
		(_exportMenuOverlay, _exportMenuCard, _menuExportUnity, _menuExportPrimary, _menuExportSelected, _menuTimestampSubfolder, _menuReset) =
			BuildExportMenu();
		_rootGrid.Children.Add(_exportMenuOverlay);

		// Path right-click context menu
		(_pathContextOverlay, _pathContextCard, _copyPathMenuItem) = BuildPathContextMenu();
		_rootGrid.Children.Add(_pathContextOverlay);

		// Non-empty directory deletion confirmation modal
		(_confirmOverlay, _confirmMessage) = BuildConfirmOverlay();
		_rootGrid.Children.Add(_confirmOverlay);

		Content = _rootGrid;

		// ================================================================
		// 4. TIMERS & EVENTS
		// ================================================================
		_layoutFrames = new FrameLoop(this, StepLayoutAnimation);
		_fadeFrames = new FrameLoop(this, StepMenuFade);

		PropertyChanged += (_, e) =>
		{
			if (e.Property == WindowStateProperty)
			{
				UpdateMaximizeIcons();
			}
		};

		SizeChanged += (_, _) =>
		{
			if (Math.Abs(Bounds.Width - _lastViewportWidth) > 0.5)
			{
				_lastViewportWidth = Bounds.Width;
				TriggerLayoutUpdate(animate: false);
			}
		};

		ActivityLogService.Instance.Changed += QueueStatusRefresh;
		_activityLogView.CancelProgressRequested += () => _pathIndexCancellation?.Cancel();

		AddHandler(KeyDownEvent, OnWorkspaceKeyDown, RoutingStrategies.Tunnel);
		Opened += (_, _) => _stripScroll.Focus();
		Closed += (_, _) =>
		{
			_closed = true;
			_pathIndexCancellation?.Cancel();
			ActivityLogService.Instance.Changed -= QueueStatusRefresh;
			_layoutFrames.Stop();
			_fadeFrames.Stop();
			_inspectorView.Dispose();
		};

		RefreshToolbarAndDock();
	}

	#region Titlebar Construction

	private Control BuildTitlebar(
		IReadOnlyList<Control> leftControls,
		Control? centerControl,
		IReadOnlyList<Control> rightControls,
		out Border maximizeIconHost
	)
	{
		Border bar = new()
		{
			Height = Palette.TitlebarHeight,
			Background = Palette.Background,
			BorderBrush = Palette.Border,
			BorderThickness = new Thickness(0, 0, 0, 1),
			Padding = new Thickness(12, 0, 0, 0),
		};

		Grid layout = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), VerticalAlignment = VerticalAlignment.Stretch };

		StackPanel leftStack = new()
		{
			Orientation = Orientation.Horizontal,
			Spacing = 6,
			VerticalAlignment = VerticalAlignment.Center,
		};
		foreach (Control ctrl in leftControls)
		{
			leftStack.Children.Add(ctrl);
		}
		Grid.SetColumn(leftStack, 0);
		layout.Children.Add(leftStack);

		// Draggable titlebar space + optional centered control
		Grid dragRegion = new()
		{
			Background = Palette.Transparent,
			MinWidth = 20,
			VerticalAlignment = VerticalAlignment.Stretch,
			HorizontalAlignment = HorizontalAlignment.Stretch,
		};
		if (centerControl is not null)
		{
			centerControl.HorizontalAlignment = HorizontalAlignment.Center;
			dragRegion.Children.Add(centerControl);
		}
		dragRegion.PointerPressed += OnTitlebarDragPressed;
		Grid.SetColumn(dragRegion, 1);
		layout.Children.Add(dragRegion);

		StackPanel rightStack = new()
		{
			Orientation = Orientation.Horizontal,
			Spacing = 6,
			Margin = new Thickness(0, 0, 8, 0),
			VerticalAlignment = VerticalAlignment.Center,
		};
		foreach (Control ctrl in rightControls)
		{
			rightStack.Children.Add(ctrl);
		}
		Grid.SetColumn(rightStack, 2);
		layout.Children.Add(rightStack);

		// Native-style Windows caption controls (46px wide, 40px tall)
		StackPanel windowControls = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Stretch };

		windowControls.Children.Add(
			CreateCaptionButton(
				Icons.Glyph(IconKind.WindowMinimize, 12, Palette.Text),
				isClose: false,
				onClick: () => WindowState = WindowState.Minimized
			)
		);

		maximizeIconHost = new Border
		{
			Width = 12,
			Height = 12,
			Child = Icons.Glyph(IconKind.WindowMaximize, 12, Palette.Text),
		};
		windowControls.Children.Add(CreateCaptionButton(maximizeIconHost, isClose: false, onClick: ToggleMaximize));

		windowControls.Children.Add(CreateCaptionButton(Icons.Glyph(IconKind.WindowClose, 12, Palette.Text), isClose: true, onClick: Close));

		Grid.SetColumn(windowControls, 3);
		layout.Children.Add(windowControls);

		bar.Child = layout;
		return bar;
	}

	private static Border CreateCaptionButton(Control icon, bool isClose, Action onClick)
	{
		Border btn = new()
		{
			Width = 46,
			Height = Palette.TitlebarHeight,
			Background = Palette.Transparent,
			Child = icon,
		};
		btn.PointerEntered += (_, _) => btn.Background = isClose ? Palette.CloseHover : Palette.WindowControlHover;
		btn.PointerExited += (_, _) => btn.Background = Palette.Transparent;
		PointerClick.Bind(btn, _ => onClick());
		return btn;
	}

	private void OnTitlebarDragPressed(object? sender, PointerPressedEventArgs e)
	{
		if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || IsEditingControl(e.Source))
		{
			return;
		}
		if (e.ClickCount == 2)
		{
			ToggleMaximize();
			e.Handled = true;
			return;
		}
		BeginMoveDrag(e);
	}

	private void ToggleMaximize()
	{
		WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
	}

	private void UpdateMaximizeIcons()
	{
		IconKind kind = WindowState == WindowState.Maximized ? IconKind.WindowRestore : IconKind.WindowMaximize;
		_maxIconHost.Child = Icons.Glyph(kind, 12, Palette.Text);
		_settingsMaxIconHost.Child = Icons.Glyph(kind, 12, Palette.Text);
	}

	#endregion

	#region Welcome Overlay

	private (Grid Overlay, Border Card) BuildWelcomeOverlay()
	{
		Grid overlay = new() { Margin = new Thickness(24, 24, 24, 66) };

		Border iconBadge = new()
		{
			Width = 56,
			Height = 56,
			CornerRadius = new CornerRadius(14),
			Background = Palette.WelcomeIconBg,
			HorizontalAlignment = HorizontalAlignment.Center,
			Child = Icons.Glyph(IconKind.Import, 28, Palette.AccentText),
		};

		TextBlock heading = new()
		{
			Text = "Open Unity game files",
			FontFamily = Palette.UiFont,
			FontSize = 16,
			FontWeight = FontWeight.SemiBold,
			Foreground = Palette.Text,
			HorizontalAlignment = HorizontalAlignment.Center,
			Margin = new Thickness(0, 4, 0, 0),
		};

		TextBlock subtitle = new()
		{
			Text = "Drop game folders, bundles or files here, or choose them.",
			FontFamily = Palette.UiFont,
			FontSize = 13,
			Foreground = Palette.Secondary,
			TextAlignment = TextAlignment.Center,
			TextWrapping = TextWrapping.Wrap,
			HorizontalAlignment = HorizontalAlignment.Center,
		};

		VnlensButton folderBtn = new(label: "Open folder…", active: true, enabled: true, ghost: false);
		folderBtn.Clicked += () => _ = OpenFolderDialogAsync();

		VnlensButton filesBtn = new(label: "Open files…", active: false, enabled: true, ghost: false);
		filesBtn.Clicked += () => _ = OpenFilesDialogAsync();

		StackPanel buttons = new()
		{
			Orientation = Orientation.Horizontal,
			Spacing = 8,
			HorizontalAlignment = HorizontalAlignment.Center,
			Margin = new Thickness(0, 8, 0, 0),
		};
		buttons.Children.Add(folderBtn);
		buttons.Children.Add(filesBtn);

		StackPanel hints = new()
		{
			Orientation = Orientation.Horizontal,
			Spacing = 16,
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		hints.Children.Add(
			new TextBlock
			{
				Text = $"{Shortcut("Shift+O")} folder",
				FontFamily = Palette.UiFont,
				FontSize = 11,
				Foreground = Palette.Tertiary,
			}
		);
		hints.Children.Add(
			new TextBlock
			{
				Text = $"{Shortcut("O")} files",
				FontFamily = Palette.UiFont,
				FontSize = 11,
				Foreground = Palette.Tertiary,
			}
		);

		StackPanel stack = new()
		{
			Orientation = Orientation.Vertical,
			Spacing = 12,
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		stack.Children.Add(iconBadge);
		stack.Children.Add(heading);
		stack.Children.Add(subtitle);
		stack.Children.Add(buttons);
		stack.Children.Add(hints);

		global::Avalonia.Controls.Shapes.Rectangle dashedOutline = new()
		{
			Stroke = Palette.Border,
			StrokeThickness = 1,
			StrokeDashArray = [5, 4],
			RadiusX = 14,
			RadiusY = 14,
			IsHitTestVisible = false,
		};

		Border card = new()
		{
			MaxWidth = 460,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			CornerRadius = new CornerRadius(14),
			Background = Palette.RootColumnBackground,
			Tag = dashedOutline,
		};

		Grid cardGrid = new();
		cardGrid.Children.Add(dashedOutline);
		cardGrid.Children.Add(new Border { Padding = new Thickness(28, 36), Child = stack });
		card.Child = cardGrid;

		overlay.Children.Add(card);
		return (overlay, card);
	}

	#endregion

	#region Floating Bottom Status Dock, Breadcrumb Path Bar & Popovers

	private (
		Border StatusTile,
		Border StatusIconHost,
		Border StatusBadgeDot,
		Border StatusPopover,
		ActivityLogView ActivityLog,
		Border PathBarGlass,
		StackPanel PathSegmentsPanel,
		VnlensButton ExportPreviewBtn
	) BuildStatusDock()
	{
		// 1. Status Tile (30x30 glass square, bottom-left)
		Border iconHost = new()
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Child = Icons.Glyph(IconKind.Check, 16, Palette.Success),
		};

		Border badgeDot = new()
		{
			Width = 5,
			Height = 5,
			CornerRadius = new CornerRadius(2.5),
			Background = Palette.Danger,
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Top,
			Margin = new Thickness(0, 4, 4, 0),
			IsVisible = false,
		};

		Grid tileContent = new();
		tileContent.Children.Add(iconHost);
		tileContent.Children.Add(badgeDot);

		Border statusTile = GlassSurface.Create(10);
		statusTile.Width = 30;
		statusTile.Height = 30;
		statusTile.Cursor = new Cursor(StandardCursorType.Hand);
		statusTile.Child = tileContent;
		GlassSurface.SetTooltip(statusTile, "Activity");
		statusTile.PointerEntered += (_, _) => statusTile.Background = Palette.Control;
		statusTile.PointerExited += (_, _) => statusTile.Background = Palette.GlassBackground;
		statusTile.PointerPressed += (_, e) =>
		{
			if (e.GetCurrentPoint(statusTile).Properties.IsLeftButtonPressed)
			{
				e.Handled = true;
				_showStatus = !_showStatus;
				_statusPopover.IsVisible = _showStatus;
				if (_showStatus)
				{
					RefreshStatusDock();
					StartMenuFade(_statusPopover);
				}
			}
		};

		// 2. Activity Details Popover (bottom: 50px, left: 12px)
		ActivityLogView activityLog = new();
		activityLog.ShowOutputRequested += OpenInSystemExplorer;

		Border statusPopover = GlassSurface.Create(10);
		statusPopover.Width = 650;
		statusPopover.MinWidth = 320;
		statusPopover.Padding = new Thickness(8);
		statusPopover.HorizontalAlignment = HorizontalAlignment.Left;
		statusPopover.VerticalAlignment = VerticalAlignment.Bottom;
		statusPopover.Margin = new Thickness(12, 0, 12, 50);
		statusPopover.IsVisible = false;
		statusPopover.Child = activityLog;
		_rootGrid.Children.Add(statusPopover);

		// 3. Breadcrumb Path Bar (glass pill next to status tile)
		StackPanel pathSegments = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

		ScrollViewer pathScroll = new()
		{
			HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
			VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
			Content = pathSegments,
		};

		Border pathBarGlass = GlassSurface.Create(10);
		pathBarGlass.Padding = new Thickness(5, 3);
		pathBarGlass.VerticalAlignment = VerticalAlignment.Bottom;
		pathBarGlass.HorizontalAlignment = HorizontalAlignment.Left;
		pathBarGlass.IsVisible = false;
		pathBarGlass.Child = pathScroll;
		pathScroll.LayoutUpdated += (_, _) =>
		{
			if (_revealPath)
			{
				_revealPath = false;
				pathScroll.Offset = new Vector(Math.Max(0, pathScroll.Extent.Width - pathScroll.Viewport.Width), 0);
			}
		};

		// 4. Right-aligned primary Export button when inspecting a leaf item
		VnlensButton exportPreviewBtn = new(label: "Export…", active: true, enabled: false, ghost: false);
		exportPreviewBtn.IsVisible = false;
		exportPreviewBtn.VerticalAlignment = VerticalAlignment.Bottom;
		exportPreviewBtn.Clicked += () => _ = ExportSelectedAssetAsync();

		// Dock container
		Grid dock = new()
		{
			ColumnDefinitions = new ColumnDefinitions("Auto,8,*,8,Auto"),
			Margin = new Thickness(12, 0, 12, 12),
			VerticalAlignment = VerticalAlignment.Bottom,
			HorizontalAlignment = HorizontalAlignment.Stretch,
		};
		Grid.SetColumn(statusTile, 0);
		Grid.SetColumn(pathBarGlass, 2);
		Grid.SetColumn(exportPreviewBtn, 4);

		dock.Children.Add(statusTile);
		dock.Children.Add(pathBarGlass);
		dock.Children.Add(exportPreviewBtn);

		_rootGrid.Children.Add(dock);

		return (statusTile, iconHost, badgeDot, statusPopover, activityLog, pathBarGlass, pathSegments, exportPreviewBtn);
	}

	private (
		Border Overlay,
		Border Card,
		VnlensMenuItem ExportUnity,
		VnlensMenuItem ExportPrimary,
		VnlensMenuItem ExportSelected,
		VnlensMenuItem TimestampSubfolder,
		VnlensMenuItem ResetItem
	) BuildExportMenu()
	{
		VnlensMenuItem exportUnity = new(IconKind.Folder, "Export Unity Project…", enabled: false);
		exportUnity.Clicked += () =>
		{
			ClosePopups();
			_ = ExportProjectDialogAsync(primaryContent: false);
		};

		VnlensMenuItem exportPrimary = new(IconKind.Archive, "Export Primary Content…", enabled: false);
		exportPrimary.Clicked += () =>
		{
			ClosePopups();
			_ = ExportProjectDialogAsync(primaryContent: true);
		};

		VnlensMenuItem exportSelected = new(IconKind.File, "Export Selected Asset…", enabled: false);
		exportSelected.Clicked += () =>
		{
			ClosePopups();
			_ = ExportSelectedAssetAsync();
		};

		VnlensMenuItem timestampSubfolder = new(null, "Timestamped subfolder", enabled: true);
		timestampSubfolder.Clicked += () =>
		{
			_createTimestampSubfolder = !_createTimestampSubfolder;
			timestampSubfolder.Icon = _createTimestampSubfolder ? IconKind.Check : null;
		};

		VnlensMenuItem resetItem = new(IconKind.WindowClose, Localization.MenuFileReset, enabled: false);
		resetItem.Clicked += () =>
		{
			ClosePopups();
			ResetLoadedFiles();
		};

		StackPanel list = new() { Orientation = Orientation.Vertical, Spacing = 2 };
		list.Children.Add(exportUnity);
		list.Children.Add(exportPrimary);
		list.Children.Add(exportSelected);
		list.Children.Add(
			new Border
			{
				Height = 1,
				Margin = new Thickness(6, 3),
				Background = Palette.Border,
			}
		);
		list.Children.Add(timestampSubfolder);
		list.Children.Add(
			new Border
			{
				Height = 1,
				Margin = new Thickness(6, 3),
				Background = Palette.Border,
			}
		);
		list.Children.Add(resetItem);

		Border card = GlassSurface.Create(10);
		card.Width = 230;
		card.Padding = new Thickness(4);
		card.HorizontalAlignment = HorizontalAlignment.Right;
		card.VerticalAlignment = VerticalAlignment.Top;
		card.Margin = new Thickness(0, Palette.TitlebarHeight - 6, 12, 0);
		card.Child = list;

		Border overlay = new()
		{
			Background = Palette.Transparent,
			IsVisible = false,
			Child = card,
		};
		overlay.PointerPressed += (_, e) =>
		{
			if (!card.IsPointerOver && !_exportBtn.IsPointerHovered)
			{
				ClosePopups();
				e.Handled = true;
			}
		};

		return (overlay, card, exportUnity, exportPrimary, exportSelected, timestampSubfolder, resetItem);
	}

	private (Canvas Overlay, Border Card, VnlensMenuItem CopyItem) BuildPathContextMenu()
	{
		VnlensMenuItem copyItem = new(IconKind.Copy, "Copy path", enabled: true);
		copyItem.Clicked += async () =>
		{
			if (Clipboard is not null)
			{
				await Clipboard.SetTextAsync(_contextPathToCopy);
			}
			ClosePopups();
		};

		Border card = GlassSurface.Create(10);
		card.Width = 160;
		card.Padding = new Thickness(4);
		card.Child = copyItem;

		Canvas overlay = new() { Background = Palette.Transparent, IsVisible = false };
		overlay.Children.Add(card);
		overlay.PointerPressed += (_, e) =>
		{
			if (!card.IsPointerOver)
			{
				ClosePopups();
				e.Handled = true;
			}
		};

		return (overlay, card, copyItem);
	}

	private (Grid Overlay, TextBlock Message) BuildConfirmOverlay()
	{
		TextBlock msg = new()
		{
			FontFamily = Palette.UiFont,
			FontSize = 13,
			Foreground = Palette.Text,
			TextWrapping = TextWrapping.Wrap,
		};

		VnlensButton cancelBtn = new(label: "Cancel", active: false, enabled: true, ghost: false);
		cancelBtn.Clicked += () => ResolveConfirmation(false);

		VnlensButton deleteBtn = new(label: "Delete & Export", active: true, enabled: true, ghost: false);
		deleteBtn.Clicked += () => ResolveConfirmation(true);

		StackPanel buttons = new()
		{
			Orientation = Orientation.Horizontal,
			Spacing = 8,
			HorizontalAlignment = HorizontalAlignment.Right,
			Margin = new Thickness(0, 12, 0, 0),
		};
		buttons.Children.Add(cancelBtn);
		buttons.Children.Add(deleteBtn);

		StackPanel stack = new() { Spacing = 8 };
		stack.Children.Add(
			new TextBlock
			{
				Text = "Directory is not empty",
				FontFamily = Palette.UiFont,
				FontSize = 15,
				FontWeight = FontWeight.SemiBold,
				Foreground = Palette.Warning,
			}
		);
		stack.Children.Add(msg);
		stack.Children.Add(buttons);

		Border card = GlassSurface.Create(12);
		card.MaxWidth = 440;
		card.Padding = new Thickness(20);
		card.HorizontalAlignment = HorizontalAlignment.Center;
		card.VerticalAlignment = VerticalAlignment.Center;
		card.Child = stack;

		Grid overlay = new() { Background = new SolidColorBrush(Color.FromArgb(0x99, 0x00, 0x00, 0x00)), IsVisible = false };
		overlay.Children.Add(card);

		return (overlay, msg);
	}

	private Task<bool> ConfirmDeleteDirectoryAsync(string path)
	{
		_confirmTcs = new TaskCompletionSource<bool>();
		_confirmMessage.Text = $"{Localization.ExportDirectoryDeleteUserConfirmation}\n\n{path}";
		_confirmOverlay.IsVisible = true;
		_rootGrid.Focus();
		return _confirmTcs.Task;
	}

	private void ResolveConfirmation(bool result)
	{
		_confirmOverlay.IsVisible = false;
		_confirmTcs?.TrySetResult(result);
		_confirmTcs = null;
		_stripScroll.Focus();
	}

	#endregion

	#region File Loading, Drag & Drop, Search, Export

	private void OnDragOver(object? sender, DragEventArgs e)
	{
		if (e.DataTransfer.Contains(DataFormat.File))
		{
			e.DragEffects = DragDropEffects.Copy;
			SetWelcomeDragHighlight(true);
		}
		else
		{
			e.DragEffects = DragDropEffects.None;
		}
	}

	private void OnDragLeave(object? sender, DragEventArgs e)
	{
		SetWelcomeDragHighlight(false);
	}

	private void OnDrop(object? sender, DragEventArgs e)
	{
		SetWelcomeDragHighlight(false);
		IStorageItem[]? items = e.DataTransfer.TryGetFiles();
		if (items is null)
		{
			return;
		}
		List<string> paths = items.Select(i => i.TryGetLocalPath()).Where(p => !string.IsNullOrEmpty(p)).Cast<string>().ToList();
		if (paths.Count > 0)
		{
			_ = LoadPathsAsync(paths);
		}
	}

	private void SetWelcomeDragHighlight(bool highlighted)
	{
		_welcomeCard.Background = highlighted ? Palette.DropZoneAccentBg : Palette.RootColumnBackground;
		if (_welcomeCard.Tag is global::Avalonia.Controls.Shapes.Rectangle rect)
		{
			rect.Stroke = highlighted ? Palette.Accent : Palette.Border;
		}
	}

	private async Task OpenFolderDialogAsync()
	{
		IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(
			new FolderPickerOpenOptions { Title = Localization.MenuFileOpenFolder, AllowMultiple = true }
		);
		List<string> paths = folders.Select(f => f.TryGetLocalPath()).Where(p => !string.IsNullOrEmpty(p)).Cast<string>().ToList();
		if (paths.Count > 0)
		{
			await LoadPathsAsync(paths);
		}
	}

	private async Task OpenFilesDialogAsync()
	{
		IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(
			new FilePickerOpenOptions { Title = Localization.MenuFileOpenFile, AllowMultiple = true }
		);
		List<string> paths = files.Select(f => f.TryGetLocalPath()).Where(p => !string.IsNullOrEmpty(p)).Cast<string>().ToList();
		if (paths.Count > 0)
		{
			await LoadPathsAsync(paths);
		}
	}

	public async Task LoadPathsAsync(IReadOnlyList<string> paths)
	{
		if (_loading || paths.Count == 0)
		{
			return;
		}
		ClosePopups();
		_showSettings = false;
		_settingsLayer.IsVisible = false;
		_workspaceLayer.IsVisible = true;

		_loading = true;
		_inspectorView.Inspect(null);
		RefreshToolbarAndDock();

		using IDisposable task = ActivityLogService.Instance.BeginTask($"Loading {paths.Count} item(s)…");
		try
		{
			await Task.Run(() => GameFileLoader.LoadAndProcess(paths));
			_internalPathItems = null;
			if (GameFileLoader.Settings.ExportSettings.AssetPathDisplayMode == AssetPathDisplayMode.InternalPaths)
			{
				await PrepareInternalPathsAsync();
			}
			BuildRootBrowser();
		}
		catch (Exception ex)
		{
			ActivityLogService.Instance.ReportError("Load failed.", ex);
		}
		finally
		{
			_loading = false;
			_settingsView.Refresh();
			RefreshToolbarAndDock();
		}
	}

	private void ResetLoadedFiles()
	{
		if (_loading || _exporting)
		{
			return;
		}
		_inspectorView.Inspect(null);
		GameFileLoader.Reset();
		_internalPathItems = null;
		ClearAllColumns();
		_selectedLeaf = null;
		_selectedLeafDepth = null;
		_searchBox.Text = "";
		_lastExportOutputPath = null;
		_welcomeOverlay.IsVisible = true;
		_settingsView.Refresh();
		TriggerLayoutUpdate(animate: true);
		RefreshToolbarAndDock();
	}

	private async Task ExportProjectDialogAsync(bool primaryContent)
	{
		if (!GameFileLoader.IsLoaded || _exporting || _loading)
		{
			return;
		}
		IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(
			new FolderPickerOpenOptions
			{
				Title = primaryContent ? Localization.ExportPrimaryContent : Localization.MenuExportAll,
				AllowMultiple = false,
			}
		);
		string? path = folders.FirstOrDefault()?.TryGetLocalPath();
		if (string.IsNullOrEmpty(path))
		{
			return;
		}

		if (_createTimestampSubfolder)
		{
			string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
			path = Path.Combine(path, $"AssetRipper_export_{timestamp}");
		}

		_exporting = true;
		RefreshToolbarAndDock();

		using IDisposable task = ActivityLogService.Instance.BeginTask(primaryContent ? "Exporting primary content…" : "Exporting Unity project…");
		try
		{
			bool ok = primaryContent
				? await GameFileLoader.ExportPrimaryContentAsync(path, ConfirmDeleteDirectoryAsync)
				: await GameFileLoader.ExportUnityProjectAsync(path, ConfirmDeleteDirectoryAsync);

			if (ok)
			{
				_lastExportOutputPath = path;
				ActivityLogService.Instance.SetStatus($"Exported to {path}");
			}
		}
		catch (Exception ex)
		{
			ActivityLogService.Instance.ReportError("Export failed.", ex);
		}
		finally
		{
			_exporting = false;
			RefreshToolbarAndDock();
		}
	}

	private async Task ExportSelectedAssetAsync()
	{
		BrowserItem? item = _selectedLeaf;
		if (item is null || _exporting)
		{
			return;
		}

		string baseName = FileSystem.FixInvalidFileNameCharacters(item.Name);
		string defaultExt = "bin";
		if (item.Target is IUnityObjectBase asset)
		{
			if (AssetContentService.HasImageData(asset))
				defaultExt = "png";
			else if (AssetContentService.HasAudioData(asset))
				defaultExt = _inspectorView.CurrentDecodedAudio?.EncodedExtension ?? "wav";
			else if (AssetContentService.HasModelData(asset))
				defaultExt = "glb";
			else if (AssetContentService.HasTextData(asset))
			{
				string fn = AssetContentService.GetTextFileName(asset);
				baseName = Path.GetFileNameWithoutExtension(fn);
				defaultExt = Path.GetExtension(fn).TrimStart('.');
			}
			else if (AssetContentService.HasBinaryData(asset))
			{
				(_, defaultExt) = AssetContentService.GetBinaryData(asset);
			}
			else
			{
				defaultExt = "asset";
			}
		}
		else if (item.Target is ResourceFile)
		{
			defaultExt = Path.GetExtension(baseName).TrimStart('.');
			if (string.IsNullOrEmpty(defaultExt))
				defaultExt = "bin";
		}
		else if (item.Target is FailedFile)
		{
			defaultExt = "txt";
		}

		IStorageFile? file = await StorageProvider.SaveFilePickerAsync(
			new FilePickerSaveOptions
			{
				Title = "Export selected asset",
				SuggestedFileName = baseName.EndsWith("." + defaultExt, StringComparison.OrdinalIgnoreCase) ? baseName : $"{baseName}.{defaultExt}",
				DefaultExtension = defaultExt,
			}
		);
		string? outPath = file?.TryGetLocalPath();
		if (string.IsNullOrEmpty(outPath))
		{
			return;
		}

		_exporting = true;
		RefreshToolbarAndDock();
		using IDisposable task = ActivityLogService.Instance.BeginTask($"Exporting {item.Name}…");
		try
		{
			await Task.Run(() => SaveSingleItem(item, outPath));
			_lastExportOutputPath = Path.GetDirectoryName(outPath);
			ActivityLogService.Instance.SetStatus($"Exported {Path.GetFileName(outPath)}");
		}
		catch (Exception ex)
		{
			ActivityLogService.Instance.ReportError("Export failed.", ex);
		}
		finally
		{
			_exporting = false;
			RefreshToolbarAndDock();
		}
	}

	private void SaveSingleItem(BrowserItem item, string outPath)
	{
		if (item.Target is IUnityObjectBase asset)
		{
			string ext = Path.GetExtension(outPath).TrimStart('.').ToLowerInvariant();
			if (AssetContentService.HasImageData(asset) && ImageExportFormatExtensions.TryParse(ext, out ImageExportFormat imgFmt))
			{
				DecodedImageResult? decoded = _inspectorView.CurrentDecodedImage ?? AssetContentService.DecodeImage(asset);
				if (decoded is not null)
				{
					using FileStream fs = File.Create(outPath);
					decoded.DirectBitmap.Save(fs, imgFmt);
					return;
				}
			}
			if (asset is IAudioClip clip)
			{
				DecodedAudioResult? audio = _inspectorView.CurrentDecodedAudio ?? AssetContentService.DecodeAudio(clip, out _);
				if (audio is not null)
				{
					byte[] payload = (ext == "wav" && audio.WavData is not null) ? audio.WavData : audio.EncodedData;
					File.WriteAllBytes(outPath, payload);
					return;
				}
			}
			if (asset is IMesh mesh && ext == "glb")
			{
				using FileStream fs = File.Create(outPath);
				if (!AssetContentService.TryExportGlb(mesh, fs, out string? err))
				{
					throw new InvalidOperationException(err ?? "GLB export failed.");
				}
				return;
			}
			if (ext == "json")
			{
				File.WriteAllText(outPath, AssetContentService.GetJsonContent(asset));
				return;
			}
			if (ext is "asset" or "yaml" or "yml")
			{
				File.WriteAllText(outPath, AssetContentService.GetYamlContent(asset));
				return;
			}
			if (AssetContentService.HasTextData(asset))
			{
				File.WriteAllText(outPath, AssetContentService.GetTextContent(asset));
				return;
			}
			if (AssetContentService.HasBinaryData(asset))
			{
				(byte[] data, _) = AssetContentService.GetBinaryData(asset);
				File.WriteAllBytes(outPath, data);
				return;
			}
			File.WriteAllText(outPath, AssetContentService.GetYamlContent(asset));
		}
		else if (item.Target is ResourceFile resource)
		{
			File.WriteAllBytes(outPath, AssetContentService.ReadResourceBytes(resource));
		}
		else if (item.Target is FailedFile failed)
		{
			File.WriteAllText(outPath, failed.StackTrace);
		}
	}

	private void PerformSearch(string query)
	{
		if (!GameFileLoader.IsLoaded)
		{
			return;
		}
		if (string.IsNullOrWhiteSpace(query))
		{
			BuildRootBrowser();
			return;
		}

		List<SearchHit> hits = AssetContentService.SearchAssets(query.Trim());
		_showingSearch = true;
		List<BrowserItem> hitItems = BrowserFolderBuilder.BuildSearchHitItems(hits);

		ClearAllColumns();
		_selectedLeaf = null;
		_selectedLeafDepth = null;
		_inspectorView.Inspect(null);

		ColumnView searchCol = CreateColumn(0, hitItems, animate: true);
		_columns.Add(searchCol);
		_columnsPanel.Children.Insert(0, searchCol);
		SetActiveColumn(0);

		ActivityLogService.Instance.SetStatus($"Search \"{query.Trim()}\": {hits.Count} result(s)");
		TriggerLayoutUpdate(animate: true);
		RefreshToolbarAndDock();
	}

	#endregion

	#region Miller Column Browser Navigation & Coordinated 220ms Layout Motion

	private void BuildRootBrowser()
	{
		if (_closed || _pathIndexCancellation is not null)
		{
			return;
		}
		_browserPathMode = GameFileLoader.Settings.ExportSettings.AssetPathDisplayMode;
		_showingSearch = false;
		ClearAllColumns();
		_selectedLeaf = null;
		_selectedLeafDepth = null;
		_revealColumnDepth = 0;
		_inspectorView.Inspect(null);

		if (!GameFileLoader.IsLoaded)
		{
			_welcomeOverlay.IsVisible = true;
			TriggerLayoutUpdate(animate: true);
			RefreshToolbarAndDock();
			return;
		}

		_welcomeOverlay.IsVisible = false;
		List<BrowserItem> rootItems =
			_browserPathMode == AssetPathDisplayMode.InternalPaths && _internalPathItems is not null
				? _internalPathItems
				: BrowserFolderBuilder.BuildRoot(GameFileLoader.GameBundle);
		ColumnView rootCol = CreateColumn(0, rootItems, animate: true);
		_columns.Add(rootCol);
		_columnsPanel.Children.Insert(0, rootCol);
		SetActiveColumn(0);
		TriggerLayoutUpdate(animate: true);
		RefreshToolbarAndDock();
		_stripScroll.Focus();
	}

	private ColumnView CreateColumn(int depth, List<BrowserItem> items, bool animate)
	{
		ColumnView col = new(depth, items, animate);
		col.ColumnClicked += c =>
		{
			if (_activeColumn != c.Depth)
			{
				ActivateColumn(c.Depth);
			}
			_stripScroll.Focus();
		};
		col.ItemActivated += OnColumnItemActivated;
		col.WidthChangedByUser += (_, _) => TriggerLayoutUpdate(animate: false);
		col.SelectionCleared += c =>
		{
			TrimColumnsAfter(c.Depth);
			ActivateColumn(c.Depth, focusBrowser: false);
		};
		return col;
	}

	private async void OnColumnItemActivated(ColumnView column, int rowIndex, BrowserItem item, bool doubleClicked)
	{
		if (_loading || _exporting)
			return;
		item = column.ResolveItem(item);
		if (item.Target is MountedAsset or IndexedEntry)
		{
			_loading = true;
			RefreshToolbarAndDock();
			using IDisposable task = ActivityLogService.Instance.BeginTask(string.Format(Localization.LoadingInternalAsset, item.Name));
			try
			{
				MountedAsset? mountedAsset = item.Target as MountedAsset;
				IndexedEntry source = mountedAsset?.Source ?? (IndexedEntry)item.Target;
				AssetCollection collection = (AssetCollection)await Task.Run(() => GameFileLoader.LoadMountedEntry(source.Archive, source.Entry));
				BrowserItem original = item;
				if (mountedAsset is not null)
				{
					IUnityObjectBase asset =
						collection.TryGetAsset(mountedAsset.PathId)
						?? throw new InvalidDataException($"Missing PathID {mountedAsset.PathId} in {source.Entry.Name}.");
					item = BrowserFolderBuilder.CreateAssetItem(asset, item.SecondaryPath, item.Name);
				}
				else
				{
					List<BrowserItem> children = BrowserFolderBuilder
						.BuildCollectionItems(collection)
						.Where(child => child.Target is IUnityObjectBase asset && !source.MappedPathIds.Contains(asset.PathID))
						.ToList();
					item = new BrowserItem
					{
						Name = item.Name,
						Kind = BrowserItemKind.PathFolder,
						Icon = item.Icon,
						IsContainer = true,
						Target = children,
						Meta = children.Count.ToString(),
					};
				}
				column.SetLoadedItem(original, item);
			}
			catch (Exception error)
			{
				ActivityLogService.Instance.ReportError(Localization.InternalAssetLoadFailed, error);
				return;
			}
			finally
			{
				_loading = false;
				RefreshToolbarAndDock();
			}
		}
		if (item.Kind == BrowserItemKind.MountedEntry && item.Target is ValueTuple<GameArchive, GameArchiveEntry> mounted)
		{
			_loading = true;
			RefreshToolbarAndDock();
			using IDisposable task = ActivityLogService.Instance.BeginTask($"Loading {mounted.Item2.Name}…");
			try
			{
				object target = await Task.Run(() => GameFileLoader.LoadMountedEntry(mounted.Item1, mounted.Item2));
				BrowserItem original = item;
				item = new BrowserItem
				{
					Name = item.Name,
					Kind = target is AssetCollection ? BrowserItemKind.Collection : BrowserItemKind.ResourceFile,
					Icon = item.Icon,
					IsContainer = target is AssetCollection,
					Target = target,
					Meta = item.Meta,
					CompanionNames = item.CompanionNames,
				};
				column.SetLoadedItem(original, item);
			}
			catch (Exception ex)
			{
				ActivityLogService.Instance.ReportError("Entry load failed.", ex);
				return;
			}
			finally
			{
				_loading = false;
				RefreshToolbarAndDock();
			}
		}
		if (item.Kind == BrowserItemKind.MountedArchive)
		{
			_loading = true;
			RefreshToolbarAndDock();
			try
			{
				await Task.Run(() => ((GameArchive)item.Target).Entries.Count);
			}
			catch (Exception ex)
			{
				ActivityLogService.Instance.ReportError("Archive mount failed.", ex);
				return;
			}
			finally
			{
				_loading = false;
				RefreshToolbarAndDock();
			}
		}
		int depth = column.Depth;
		if (depth >= _columns.Count || !ReferenceEquals(_columns[depth], column) || column.SelectedIndex != rowIndex)
		{
			return;
		}
		_stripScroll.Focus();
		if (item.IsContainer && depth + 1 < _columns.Count && Equals(_columns[depth + 1].Tag, item.Target))
		{
			ActivateColumn(depth);
			_revealColumnDepth = depth + 1;
			TriggerLayoutUpdate(animate: true);
			if (doubleClicked)
			{
				EnterColumn();
			}
			return;
		}
		SetActiveColumn(depth);
		TrimColumnsAfter(depth);

		if (item.IsContainer)
		{
			_selectedLeaf = null;
			_selectedLeafDepth = null;
			_inspectorView.Inspect(null);

			List<BrowserItem> children = BrowserFolderBuilder.BuildChildren(item);
			ColumnView childCol = CreateColumn(depth + 1, children, animate: true);
			childCol.Tag = item.Target;
			_columns.Add(childCol);
			_columnsPanel.Children.Insert(_columns.Count - 1, childCol);

			if (doubleClicked)
			{
				EnterColumn();
				return;
			}

			_revealColumnDepth = depth + 1;
		}
		else
		{
			_selectedLeaf = item;
			_selectedLeafDepth = depth;
			_showInspector = true;
			_inspectorView.Inspect(item);
			_revealColumnDepth = depth;
		}

		TriggerLayoutUpdate(animate: true);
		RefreshToolbarAndDock();
	}

	private void NavigateToReference(object target)
	{
		if (!GameFileLoader.IsLoaded)
		{
			return;
		}

		if (target is IUnityObjectBase asset)
		{
			NavigateToCollection(asset.Collection, asset);
		}
		else if (target is AssetCollection collection)
		{
			NavigateToCollection(collection, null);
		}
		else if (target is SceneDefinition scene)
		{
			BuildRootBrowser();
			if (_columns.Count > 0 && _columns[0].ItemCount > 0)
			{
				// Select "Scenes" group at root if present
				for (int i = 0; i < _columns[0].ItemCount; i++)
				{
					_columns[0].SelectIndex(i, raiseEvent: false);
					if (_columns[0].SelectedItem?.Kind == BrowserItemKind.ScenesGroup)
					{
						_columns[0].SelectIndex(i, raiseEvent: true);
						if (_columns.Count > 1)
						{
							_columns[1].SelectTarget(scene, raiseEvent: true);
						}
						break;
					}
				}
			}
		}
	}

	private void NavigateToCollection(AssetCollection collection, IUnityObjectBase? assetToSelect)
	{
		if (_browserPathMode == AssetPathDisplayMode.InternalPaths)
		{
			BuildRootBrowser();
			if ((assetToSelect ?? collection.FirstOrDefault()) is { } asset && _columns.Count > 0)
			{
				SelectAssetInColumn(0, asset);
			}
			return;
		}
		// Build bundle ancestor chain from root GameBundle down to collection.Bundle
		List<Bundle> chain = new();
		Bundle? cur = collection.Bundle;
		while (cur is not null && cur.Parent is not null)
		{
			chain.Add(cur);
			cur = cur.Parent;
		}
		chain.Reverse();

		BuildRootBrowser();
		int depth = 0;
		foreach (Bundle b in chain)
		{
			if (depth >= _columns.Count || !_columns[depth].SelectTarget(b, raiseEvent: true))
			{
				break;
			}
			depth++;
		}

		if (depth < _columns.Count && _columns[depth].SelectTarget(collection, raiseEvent: true))
		{
			depth++;
			if (assetToSelect is not null && depth < _columns.Count)
			{
				SelectAssetInColumn(depth, assetToSelect);
			}
		}
	}

	private void SelectAssetInColumn(int depth, IUnityObjectBase asset)
	{
		List<BrowserItem> path = [];
		if (AssetPathTree.TryFindAsset(_columns[depth].Items, asset, path))
		{
			foreach (BrowserItem item in path)
			{
				if (depth >= _columns.Count || !_columns[depth].SelectTarget(item.Target))
				{
					break;
				}
				depth++;
			}
		}
	}

	private async Task RefreshAssetPathDisplayAsync()
	{
		IUnityObjectBase? selected = _selectedLeaf?.Target as IUnityObjectBase;
		if (GameFileLoader.IsLoaded && GameFileLoader.Settings.ExportSettings.AssetPathDisplayMode == AssetPathDisplayMode.InternalPaths)
		{
			await PrepareInternalPathsAsync();
		}
		BuildRootBrowser();
		if (selected is not null && !_closed)
		{
			NavigateToCollection(selected.Collection, selected);
		}
	}

	private async Task PrepareInternalPathsAsync()
	{
		if (_internalPathItems is not null)
		{
			return;
		}
		bool wasLoading = _loading;
		_loading = true;
		ClearAllColumns();
		_selectedLeaf = null;
		_inspectorView.Inspect(null);
		_welcomeOverlay.IsVisible = false;
		using CancellationTokenSource cancellation = new();
		_pathIndexCancellation = cancellation;
		ActivityLogService activity = ActivityLogService.Instance;
		using IDisposable task = activity.BeginTask(Localization.IndexingInternalPaths);
		activity.SetProgress(new(Localization.IndexingInternalPaths, 0));
		RefreshToolbarAndDock();
		try
		{
			_internalPathItems = await Task.Run(() =>
				GameFileLoader.MountedGame is { } mounted
					? MountedPathIndex.Build(
						mounted,
						progress =>
							activity.SetProgress(
								new(
									string.Format(
										Localization.InternalPathIndexProgress,
										progress.Completed,
										progress.Total,
										progress.Paths,
										progress.Failed,
										progress.CurrentArchive
									),
									progress.Total == 0 ? 0 : (double)progress.Completed / progress.Total
								)
							),
						cancellation.Token
					)
					: AssetPathTree.Build(GameFileLoader.GameBundle.FetchAssetCollections().ToList(), cancellation.Token)
			);
			cancellation.Token.ThrowIfCancellationRequested();
			activity.SetStatus(Localization.InternalPathsReady);
		}
		catch (OperationCanceledException)
		{
			_internalPathItems = null;
			GameFileLoader.Settings.ExportSettings.AssetPathDisplayMode = AssetPathDisplayMode.ArchiveStructure;
			activity.SetStatus(Localization.InternalPathIndexCancelled);
		}
		catch (Exception error)
		{
			GameFileLoader.Settings.ExportSettings.AssetPathDisplayMode = AssetPathDisplayMode.ArchiveStructure;
			activity.ReportError(Localization.InternalPathIndexFailed, error);
		}
		finally
		{
			_pathIndexCancellation = null;
			_loading = wasLoading;
			activity.SetProgress(null);
			if (!_closed)
			{
				RefreshToolbarAndDock();
			}
		}
	}

	private void SetActiveColumn(int depth)
	{
		if (_columns.Count == 0)
		{
			_activeColumn = 0;
			return;
		}
		_activeColumn = Math.Clamp(depth, 0, _columns.Count - 1);
		for (int i = 0; i < _columns.Count; i++)
		{
			_columns[i].SetActive(i == _activeColumn);
		}
	}

	private void TrimColumnsAfter(int depth)
	{
		while (_columns.Count > depth + 1)
		{
			int last = _columns.Count - 1;
			ColumnView col = _columns[last];
			_columns.RemoveAt(last);
			_columnsPanel.Children.Remove(col);
		}
	}

	private void ClearAllColumns()
	{
		foreach (ColumnView col in _columns)
		{
			_columnsPanel.Children.Remove(col);
		}
		_columns.Clear();
		_activeColumn = 0;
	}

	private void ActivateColumn(int depth, bool focusBrowser = true)
	{
		SetActiveColumn(depth);
		BrowserItem? item = _columns[_activeColumn].SelectedItem;
		BrowserItem? leaf = item is { IsContainer: false } ? item : null;
		if (!ReferenceEquals(_selectedLeaf, leaf))
		{
			_selectedLeaf = leaf;
			_inspectorView.Inspect(_showInspector ? leaf : null);
		}
		_selectedLeafDepth = leaf is null ? null : _activeColumn;
		_revealColumnDepth = _activeColumn;
		TriggerLayoutUpdate(animate: true);
		RefreshToolbarAndDock();
		if (focusBrowser)
		{
			_stripScroll.Focus();
		}
	}

	private void EnterColumn()
	{
		if (_columns.Count == 0 || _loading || _exporting)
		{
			return;
		}
		if (_activeColumn + 1 < _columns.Count)
		{
			ActivateColumn(_activeColumn + 1);
			if (_columns[_activeColumn].ItemCount > 0)
			{
				ColumnView child = _columns[_activeColumn];
				child.SelectIndex(0, raiseEvent: false);
				OnColumnItemActivated(child, 0, child.SelectedItem!, false);
			}
		}
		else if (_columns[_activeColumn] is { SelectedItem: { } item, SelectedIndex: { } row } column)
		{
			OnColumnItemActivated(column, row, item, false);
		}
		_stripScroll.Focus();
	}

	private void GoHome()
	{
		if (_showingSearch)
		{
			_searchBox.Text = "";
			BuildRootBrowser();
			_stripScroll.Focus();
			return;
		}
		if (_columns.Count == 0)
		{
			return;
		}
		_searchBox.Text = "";
		TrimColumnsAfter(0);
		_columns[0].SelectIndex(null, raiseEvent: false);
		ActivateColumn(0);
	}

	private void GoBack()
	{
		if (_activeColumn > 0)
		{
			ActivateColumn(_activeColumn - 1);
		}
		else if (_columns.Count > 1)
		{
			TrimColumnsAfter(0);
			_columns[0].SelectIndex(null, raiseEvent: false);
			ActivateColumn(0);
		}
		_stripScroll.Focus();
	}

	private void CollapseToPathDepth(int depth)
	{
		if (depth < 0 || depth >= _columns.Count)
		{
			return;
		}
		int target = depth;
		if (_columns[depth].SelectedItem is { IsContainer: true } && depth + 1 < _columns.Count)
		{
			target++;
			_columns[target].SelectIndex(null, raiseEvent: false);
		}
		ClosePopups();
		TrimColumnsAfter(target);
		ActivateColumn(target);
	}

	private void TriggerLayoutUpdate(bool animate)
	{
		double viewport = Bounds.Width;
		if (viewport <= 0 || _columns.Count == 0)
		{
			_layoutFrames.Stop();
			_layoutMotion.Retarget(default, default, animate: false);
			_revealColumnDepth = null;
			StepLayoutAnimation();
			return;
		}

		bool preview = _showInspector && _selectedLeaf is not null;
		int? reveal = _revealColumnDepth;
		_revealColumnDepth = null;
		int depth;
		if (_selectedLeafDepth.HasValue)
		{
			depth = _selectedLeafDepth.Value;
		}
		else if (reveal.HasValue)
		{
			depth = reveal.Value;
		}
		else
		{
			depth = _activeColumn + 1 < _columns.Count ? _activeColumn + 1 : _activeColumn;
		}
		depth = Math.Clamp(depth, 0, _columns.Count - 1);

		double left = 0;
		for (int i = 0; i < depth; i++)
		{
			left += _columns[i].ColumnWidth;
		}
		double contentWidth = 24.0;
		for (int i = 0; i < _columns.Count; i++)
		{
			contentWidth += _columns[i].ColumnWidth;
		}

		LayoutSnapshot target = LayoutMotion.ComputeTarget(
			viewport,
			left,
			_columns[depth].ColumnWidth,
			contentWidth,
			_stripScroll.Offset.X,
			preview,
			_manualInspectorWidth
		);

		if (animate && !_resizingInspector && _layoutMotion.IsAnimating && target == _layoutMotion.Target)
		{
			return;
		}
		LayoutSnapshot from = new(_currentInset, _stripScroll.Offset.X);
		_layoutMotion.Retarget(from, target, animate && !_resizingInspector);
		if (StepLayoutAnimation())
		{
			_layoutFrames.Start();
		}
		else
		{
			_layoutFrames.Stop();
		}
	}

	private bool StepLayoutAnimation()
	{
		(LayoutSnapshot current, bool animating) = _layoutMotion.Sample();
		_currentInset = current.Inset;
		_inspectorContainer.Width = Math.Max(0, current.Inset);
		_inspectorContainer.IsVisible = current.Inset > 0;
		_pendingLayoutScroll = current.Scroll;
		// Apply scrolling after the new inspector width has updated the viewport and extent.
		_stripScroll.InvalidateArrange();

		_exportPreviewBtn.IsVisible = !_showSettings && current.Inset > 0.5 && _selectedLeaf is not null;

		return animating;
	}

	private void ApplyPendingLayoutScroll()
	{
		if (_pendingLayoutScroll is not { } scroll)
		{
			return;
		}
		_pendingLayoutScroll = null;
		double maximum = Math.Max(0, _stripScroll.Extent.Width - _stripScroll.Viewport.Width);
		_stripScroll.Offset = new Vector(Math.Clamp(scroll, 0, maximum), 0);
	}

	private void OnBrowserWheel(object? sender, PointerWheelEventArgs e)
	{
		if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
		{
			double delta = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) ? e.Delta.X : e.Delta.Y;
			double maximum = Math.Max(0, _stripScroll.Extent.Width - _stripScroll.Viewport.Width);
			_stripScroll.Offset = new Vector(Math.Clamp(_stripScroll.Offset.X - delta * Palette.RowHeight, 0, maximum), 0);
			e.Handled = true;
		}
	}

	private void OnInspectorResizePressed(object? sender, PointerPressedEventArgs e)
	{
		if (!e.GetCurrentPoint(_inspectorResizeHandle).Properties.IsLeftButtonPressed)
		{
			return;
		}
		if (e.ClickCount == 2)
		{
			_manualInspectorWidth = null;
			TriggerLayoutUpdate(animate: true);
			e.Handled = true;
			return;
		}
		_resizingInspector = true;
		_resizeInspectorStartX = e.GetPosition(this).X;
		_resizeInspectorStartWidth = _currentInset;
		_manualInspectorWidth = _currentInset;
		e.Pointer.Capture(_inspectorResizeHandle);
		e.Handled = true;
	}

	private void OnInspectorResizeMoved(object? sender, PointerEventArgs e)
	{
		if (!_resizingInspector)
		{
			return;
		}
		double maxW = Math.Max(Palette.InspectorMinWidth, Bounds.Width - Palette.InspectorMinWidth);
		double x = e.GetPosition(this).X;
		_manualInspectorWidth = Math.Clamp(_resizeInspectorStartWidth + (_resizeInspectorStartX - x), Palette.InspectorMinWidth, maxW);
		TriggerLayoutUpdate(animate: false);
		e.Handled = true;
	}

	private void OnInspectorResizeReleased(object? sender, PointerReleasedEventArgs e)
	{
		if (_resizingInspector)
		{
			_resizingInspector = false;
			_inspectorResizeHandle.Background = Palette.Transparent;
			e.Pointer.Capture(null);
			e.Handled = true;
		}
	}

	#endregion

	#region Toolbar, Status Dock & Popover Updates

	private void ToggleInspector()
	{
		if (_selectedLeaf is null)
		{
			return;
		}
		_showInspector = !_showInspector;
		_manualInspectorWidth = null;
		_revealColumnDepth = _activeColumn;
		_inspectorView.Inspect(_showInspector ? _selectedLeaf : null);
		if (!_showInspector)
		{
			_stripScroll.Focus();
		}
		TriggerLayoutUpdate(animate: true);
		RefreshToolbarAndDock();
	}

	private async void ToggleSettings()
	{
		if (_loading || _exporting)
		{
			return;
		}
		ClosePopups();
		_showSettings = !_showSettings;
		_settingsLayer.IsVisible = _showSettings;
		_workspaceLayer.IsVisible = !_showSettings;
		if (_showSettings)
		{
			_settingsView.Refresh();
			_layoutFrames.Stop();
			_rootGrid.Focus();
		}
		else
		{
			if (_browserPathMode != GameFileLoader.Settings.ExportSettings.AssetPathDisplayMode)
			{
				await RefreshAssetPathDisplayAsync();
			}
			TriggerLayoutUpdate(animate: false);
			_stripScroll.Focus();
		}
		RefreshToolbarAndDock();
	}

	private void ToggleExportMenu()
	{
		if (_exporting || _loading)
		{
			return;
		}
		_pathContextOverlay.IsVisible = false;
		_showExportMenu = !_showExportMenu;
		_exportMenuOverlay.IsVisible = _showExportMenu;
		_exportBtn.IsActive = _showExportMenu;
		if (_showExportMenu)
		{
			StartMenuFade(_exportMenuCard);
			_rootGrid.Focus();
		}
		else
		{
			_stripScroll.Focus();
		}
	}

	private void ClosePopups()
	{
		_showExportMenu = false;
		_exportMenuOverlay.IsVisible = false;
		_exportBtn.IsActive = false;
		_pathContextOverlay.IsVisible = false;
		_fadeFrames.Stop();
		if (_fadingControl is not null)
		{
			_fadingControl.Opacity = 1;
			_fadingControl = null;
		}
		if (!_showSettings)
		{
			_stripScroll.Focus();
		}
	}

	private void StartMenuFade(Control target)
	{
		if (_fadingControl is not null)
		{
			_fadingControl.Opacity = 1;
		}
		_fadingControl = target;
		_fadingControl.Opacity = 0;
		_fadeStarted = Stopwatch.GetTimestamp();
		_fadeFrames.Start();
	}

	private bool StepMenuFade()
	{
		if (_fadingControl is null)
		{
			return false;
		}
		double t = Stopwatch.GetElapsedTime(_fadeStarted).TotalSeconds / Palette.MenuEnterDuration.TotalSeconds;
		_fadingControl.Opacity = LayoutMotion.EaseInOut(t);
		if (t >= 1.0)
		{
			_fadingControl = null;
			return false;
		}
		return true;
	}

	private void RefreshToolbarAndDock()
	{
		bool loaded = GameFileLoader.IsLoaded;
		bool mounted = GameFileLoader.MountedGame is not null;
		bool hasExportData =
			loaded && (!mounted || GameFileLoader.GameBundle.HasAnyAssetCollections() || GameFileLoader.GameBundle.Resources.Count > 0);

		_homeBtn.IsActionEnabled = loaded;
		_backBtn.IsActionEnabled = _activeColumn > 0 || _columns.Count > 1;
		_searchContainer.IsVisible = loaded;
		_exportBtn.IsActionEnabled = !_exporting && !_loading;
		_inspectorBtn.IsActionEnabled = _selectedLeaf is not null;
		_inspectorBtn.IsActive = _showInspector && _selectedLeaf is not null;

		_menuExportUnity.SetLabel(mounted ? "Export opened entries as Unity project…" : "Export Unity Project…");
		_menuExportPrimary.SetLabel(mounted ? "Export opened entries as primary content…" : "Export Primary Content…");
		_menuExportUnity.IsActionEnabled = hasExportData && !_exporting && !_loading;
		_menuExportPrimary.IsActionEnabled = hasExportData && !_exporting && !_loading;
		_menuExportSelected.IsActionEnabled = _selectedLeaf is not null && !_exporting && !_loading;
		_menuReset.IsActionEnabled = loaded && !_exporting && !_loading;

		_exportPreviewBtn.IsVisible = !_showSettings && _showInspector && _selectedLeaf is not null && _currentInset > 0.5;
		_exportPreviewBtn.IsActionEnabled = !_exporting && _selectedLeaf is not null;

		RebuildPathBar();
		RefreshStatusDock();
	}

	private void RebuildPathBar()
	{
		_pathSegmentsPanel.Children.Clear();
		if (_showSettings || !GameFileLoader.IsLoaded || _columns.Count == 0)
		{
			_pathBarGlass.IsVisible = false;
			return;
		}

		_pathBarGlass.IsVisible = true;
		_revealPath = true;

		// Root "Loaded" chip
		Border rootChip = CreatePathChip(
			Icons.Glyph(IconKind.Home, 12, Palette.Secondary),
			"Loaded",
			isLast: _columns[0].SelectedItem is null,
			fullPath: "Loaded",
			onClick: GoHome
		);
		_pathSegmentsPanel.Children.Add(rootChip);

		string runningPath = "";
		List<(int Depth, BrowserItem Item, string FullPath)> segments = new();
		for (int d = 0; d <= _activeColumn && d < _columns.Count; d++)
		{
			BrowserItem? sel = _columns[d].SelectedItem;
			if (sel is null)
			{
				break;
			}
			runningPath = string.IsNullOrEmpty(runningPath) ? sel.Name : $"{runningPath}/{sel.Name}";
			segments.Add((d, sel, runningPath));
		}

		for (int i = 0; i < segments.Count; i++)
		{
			(int depth, BrowserItem item, string fullPath) = segments[i];
			bool isLast = i == segments.Count - 1;

			_pathSegmentsPanel.Children.Add(
				new Border
				{
					Margin = new Thickness(1, 0),
					VerticalAlignment = VerticalAlignment.Center,
					Child = Icons.Glyph(IconKind.ChevronRight, 9, Palette.Tertiary),
				}
			);

			int capturedDepth = depth;
			Border chip = CreatePathChip(
				Icons.FileIcon(item.Icon, false, size: 13),
				item.Name,
				isLast,
				fullPath,
				onClick: () => CollapseToPathDepth(capturedDepth)
			);
			_pathSegmentsPanel.Children.Add(chip);
		}
	}

	private Border CreatePathChip(Control icon, string text, bool isLast, string fullPath, Action onClick)
	{
		StackPanel content = new()
		{
			Orientation = Orientation.Horizontal,
			Spacing = 5,
			VerticalAlignment = VerticalAlignment.Center,
		};
		content.Children.Add(icon);
		content.Children.Add(
			new TextBlock
			{
				Text = text,
				FontFamily = Palette.UiFont,
				FontSize = 10,
				Foreground = isLast ? Palette.Text : Palette.Secondary,
				VerticalAlignment = VerticalAlignment.Center,
			}
		);

		Border chip = new()
		{
			Height = 22,
			Padding = new Thickness(6, 0),
			CornerRadius = new CornerRadius(6),
			Background = Palette.Transparent,
			Cursor = new Cursor(StandardCursorType.Hand),
			Child = content,
		};
		chip.PointerEntered += (_, _) => chip.Background = Palette.PathHover;
		chip.PointerExited += (_, _) => chip.Background = Palette.Transparent;
		chip.PointerPressed += (_, e) =>
		{
			PointerPoint pt = e.GetCurrentPoint(this);
			if (pt.Properties.IsLeftButtonPressed)
			{
				e.Handled = true;
				onClick();
			}
			else if (pt.Properties.IsRightButtonPressed)
			{
				e.Handled = true;
				OpenPathContextMenu(fullPath, pt.Position);
			}
		};
		return chip;
	}

	private void OpenPathContextMenu(string path, Point position)
	{
		ClosePopups();
		_contextPathToCopy = path;
		double left = Math.Clamp(position.X, 8, Math.Max(8, Bounds.Width - 168));
		double top = position.Y + 46 <= Bounds.Height ? position.Y : Math.Max(8, position.Y - 38);
		Canvas.SetLeft(_pathContextCard, left);
		Canvas.SetTop(_pathContextCard, top);
		_pathContextOverlay.IsVisible = true;
		_rootGrid.Focus();
		StartMenuFade(_pathContextCard);
	}

	private void QueueStatusRefresh()
	{
		// Import can produce many log lines between frames. Keep only one pending UI refresh.
		if (Interlocked.Exchange(ref _statusRefreshPending, 1) != 0)
		{
			return;
		}
		Dispatcher.UIThread.Post(
			() =>
			{
				Interlocked.Exchange(ref _statusRefreshPending, 0);
				if (!_closed)
				{
					RefreshStatusDock();
				}
			},
			DispatcherPriority.Background
		);
	}

	private void RefreshStatusDock()
	{
		ActivityLogSnapshot snapshot = ActivityLogService.Instance.GetSnapshot(includeEntries: _showStatus);
		bool busy = snapshot.IsBusy || _loading || _exporting;
		bool failed = snapshot.ErrorCount > 0;
		bool hasWarning = snapshot.WarningCount > 0;
		GlassSurface.SetTooltip(_statusTile, snapshot.Progress?.Label ?? snapshot.ActiveJobLabel ?? "Activity");

		if (busy)
		{
			if (snapshot.Progress is { } progress)
			{
				if (_statusIconHost.Child is not ProgressRingControl)
				{
					_statusIconHost.Child = new ProgressRingControl(15);
				}
				((ProgressRingControl)_statusIconHost.Child).Progress = progress.Fraction;
			}
			else if (_statusIconHost.Child is not VnlensSpinner)
			{
				_statusIconHost.Child = new VnlensSpinner(15, Palette.Accent);
			}
			_statusBadgeDot.IsVisible = failed || hasWarning;
			_statusBadgeDot.Background = failed ? Palette.Danger : Palette.Warning;
		}
		else
		{
			_statusIconHost.Child =
				failed ? Icons.Glyph(IconKind.Alert, 16, Palette.Danger)
				: hasWarning ? Icons.Glyph(IconKind.Alert, 16, Palette.Warning)
				: Icons.Glyph(IconKind.Check, 16, Palette.Success);
			_statusBadgeDot.IsVisible = false;
		}

		if (_showStatus)
		{
			_activityLogView.Refresh(snapshot, _lastExportOutputPath);
		}
	}

	#endregion

	#region Keyboard Shortcuts

	private static bool IsEditingControl(object? source) =>
		source is Visual visual && visual.GetSelfAndVisualAncestors().Any(v => v is TextBox or ComboBox or Slider or SelectableTextBlock);

	private void OnWorkspaceKeyDown(object? sender, KeyEventArgs e)
	{
		KeyModifiers modifiers = e.KeyModifiers;
		bool primary = modifiers is KeyModifiers.Control or KeyModifiers.Meta;
		bool primaryShift = modifiers == (KeyModifiers.Control | KeyModifiers.Shift) || modifiers == (KeyModifiers.Meta | KeyModifiers.Shift);
		bool plain = modifiers == KeyModifiers.None;

		if (_confirmOverlay.IsVisible)
		{
			if (plain && e.Key == Key.Escape)
			{
				ResolveConfirmation(false);
				e.Handled = true;
			}
			return;
		}
		if (plain && e.Key == Key.Escape)
		{
			if (IsEditingControl(e.Source) && !_searchBox.IsKeyboardFocusWithin)
			{
				return;
			}
			if (_showSettings)
			{
				ToggleSettings();
			}
			else if (_showExportMenu || _pathContextOverlay.IsVisible)
			{
				ClosePopups();
			}
			else if (_showStatus)
			{
				_showStatus = false;
				_statusPopover.IsVisible = false;
			}
			else if (_searchBox.IsKeyboardFocusWithin)
			{
				_searchBox.Text = "";
				_stripScroll.Focus();
			}
			else if (_showInspector && _selectedLeaf is not null)
			{
				ToggleInspector();
			}
			e.Handled = true;
			return;
		}

		if (primary && e.Key == Key.OemComma)
		{
			ToggleSettings();
			e.Handled = true;
			return;
		}
		if (_showSettings)
		{
			return;
		}
		if (primaryShift && e.Key == Key.O)
		{
			_ = OpenFolderDialogAsync();
			e.Handled = true;
			return;
		}
		if (primary)
		{
			switch (e.Key)
			{
				case Key.O:
					_ = OpenFilesDialogAsync();
					break;
				case Key.E:
					ClosePopups();
					_ = ExportProjectDialogAsync(primaryContent: false);
					break;
				case Key.I:
					ToggleInspector();
					break;
				case Key.F when GameFileLoader.IsLoaded:
					_searchBox.Focus();
					_searchBox.SelectAll();
					break;
				case Key.C when _inspectorView.IsKeyboardFocusWithin && !IsEditingControl(e.Source):
					_ = _inspectorView.CopySelectionAsync();
					break;
				default:
					return;
			}
			e.Handled = true;
			return;
		}
		if (modifiers == KeyModifiers.Alt && e.Key == Key.Home)
		{
			GoHome();
			e.Handled = true;
			return;
		}
		if (modifiers == KeyModifiers.Alt && e.Key == Key.Left)
		{
			GoBack();
			e.Handled = true;
			return;
		}
		if (!plain || IsEditingControl(e.Source) || _showExportMenu || _pathContextOverlay.IsVisible)
		{
			return;
		}
		if (e.Key == Key.Space && _showInspector && _selectedLeaf is not null)
		{
			_inspectorView.ToggleAudioPlayback();
			e.Handled = true;
			return;
		}
		if (!_stripScroll.IsKeyboardFocusWithin || _columns.Count == 0 || _loading || _exporting)
		{
			return;
		}
		switch (e.Key)
		{
			case Key.Up:
				_columns[_activeColumn].MoveSelection(-1);
				break;
			case Key.Down:
				_columns[_activeColumn].MoveSelection(1);
				break;
			case Key.Left:
				GoBack();
				break;
			case Key.Right:
			case Key.Enter:
				EnterColumn();
				break;
			default:
				return;
		}
		e.Handled = true;
	}

	#endregion

	private static string Shortcut(string key) => OperatingSystem.IsMacOS() ? $"⌘{key}" : $"Ctrl+{key}";

	private static void OpenInSystemExplorer(string path)
	{
		try
		{
			if (OperatingSystem.IsWindows())
			{
				Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
			}
			else if (OperatingSystem.IsMacOS())
			{
				Process.Start("open", path);
			}
			else if (OperatingSystem.IsLinux())
			{
				Process.Start("xdg-open", path);
			}
		}
		catch
		{
			// Ignore shell launch errors
		}
	}

	private static class ImageExportFormatExtensions
	{
		public static bool TryParse(string ext, out ImageExportFormat format)
		{
			switch (ext)
			{
				case "png":
					format = ImageExportFormat.Png;
					return true;
				case "bmp":
					format = ImageExportFormat.Bmp;
					return true;
				case "jpg":
				case "jpeg":
					format = ImageExportFormat.Jpeg;
					return true;
				case "tga":
					format = ImageExportFormat.Tga;
					return true;
				case "exr":
					format = ImageExportFormat.Exr;
					return true;
				case "hdr":
					format = ImageExportFormat.Hdr;
					return true;
				default:
					format = default;
					return false;
			}
		}
	}
}
