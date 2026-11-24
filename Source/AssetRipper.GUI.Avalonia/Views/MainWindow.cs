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
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.ResourceFiles;
using AssetRipper.SourceGenerated.Classes.ClassID_43;
using AssetRipper.SourceGenerated.Classes.ClassID_83;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace AssetRipper.GUI.Avalonia.Views;

/// <summary>
/// Main desktop window faithfully reproducing vnlens-gui's frameless titlebar,
/// Finder-style Miller column browser, coordinated 220ms inspector &amp; scroll motion,
/// floating glass status dock, breadcrumb path bar, and welcome drop target.
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
	private readonly StackPanel _statusPopoverList;
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
	private string? _lastExportOutputPath;
	private string _contextPathToCopy = "";

	private readonly LayoutMotion _layoutMotion = new();
	private readonly DispatcherTimer _layoutTimer;
	private double? _manualInspectorWidth;
	private double _currentInset;
	private bool _resizingInspector;
	private double _resizeInspectorStartX;
	private double _resizeInspectorStartWidth;
	private int? _revealColumnDepth;
	private double _lastViewportWidth;

	// Menu fade animation
	private readonly DispatcherTimer _fadeTimer;
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

		// Custom frameless titlebar (matching vnlens-gui/src/titlebar.rs)
		ExtendClientAreaToDecorationsHint = true;
		ExtendClientAreaTitleBarHeightHint = -1;
		WindowDecorations = WindowDecorations.BorderOnly;

		DragDrop.SetAllowDrop(this, true);
		AddHandler(DragDrop.DragOverEvent, OnDragOver);
		AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
		AddHandler(DragDrop.DropEvent, OnDrop);

		_rootGrid = new Grid
		{
			Background = Palette.Background,
		};

		// ================================================================
		// 1. WORKSPACE LAYER
		// ================================================================
		_workspaceLayer = new Grid
		{
			RowDefinitions = new RowDefinitions("40,*"),
		};

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
			else if (e.Key == Key.Escape)
			{
				e.Handled = true;
				_searchBox.Text = "";
				Focus();
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
		GlassSurface.SetTooltip(_exportBtn, $"Export ({Shortcut("E")})");
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
				new Border { Width = 1, Height = 16, Margin = new Thickness(4, 0), Background = Palette.Border, VerticalAlignment = VerticalAlignment.Center },
				openFolderBtn,
				openFilesBtn,
			],
			centerControl: _searchContainer,
			rightControls:
			[
				_exportBtn,
				_inspectorBtn,
				_settingsBtn,
			],
			out _maxIconHost);
		Grid.SetRow(mainTitlebar, 0);
		_workspaceLayer.Children.Add(mainTitlebar);

		// Main Split Content Area (Column Browser + Animated Inspector)
		Grid bodyGrid = new()
		{
			ColumnDefinitions = new ColumnDefinitions("*,Auto"),
		};
		Grid.SetRow(bodyGrid, 1);

		// Column Strip
		_columnsPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			VerticalAlignment = VerticalAlignment.Stretch,
		};
		// 24px trailing spacer matching browser_view.rs line 275
		_columnsPanel.Children.Add(new Border { Width = 24 });

		_stripScroll = new ScrollViewer
		{
			HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
			VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
			Content = _columnsPanel,
		};

		// Welcome Drop Overlay (matching browser_view.rs::welcome)
		(_welcomeOverlay, _welcomeCard) = BuildWelcomeOverlay();

		Grid browserHost = new()
		{
			ClipToBounds = true,
		};
		browserHost.Children.Add(_stripScroll);
		browserHost.Children.Add(_welcomeOverlay);
		Grid.SetColumn(browserHost, 0);
		bodyGrid.Children.Add(browserHost);

		// Right-hand Inspector Container (animated width via LayoutMotion)
		_inspectorView = new InspectorView();
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
		// 2. SETTINGS LAYER (matching view.rs::settings)
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
			out _settingsMaxIconHost);
		Grid.SetRow(settingsTitlebar, 0);
		_settingsLayer.Children.Add(settingsTitlebar);

		_settingsView = new SettingsView();
		_settingsView.ResetRequested += ResetLoadedFiles;
		_settingsView.LanguageChanged += () => settingsTitle.Text = Localization.Settings;
		Grid.SetRow(_settingsView, 1);
		_settingsLayer.Children.Add(_settingsView);

		_rootGrid.Children.Add(_settingsLayer);

		// ================================================================
		// 3. FLOATING STATUS DOCK & POPOVERS (matching status_view.rs & browser_view.rs)
		// ================================================================
		(_statusTile, _statusIconHost, _statusBadgeDot, _statusPopover, _statusPopoverList,
		 _pathBarGlass, _pathSegmentsPanel, _exportPreviewBtn) = BuildStatusDock();

		// Export dropdown menu (matching browser_view.rs::export_menu)
		(_exportMenuOverlay, _exportMenuCard, _menuExportUnity, _menuExportPrimary,
		 _menuExportSelected, _menuTimestampSubfolder, _menuReset) = BuildExportMenu();
		_rootGrid.Children.Add(_exportMenuOverlay);

		// Path right-click context menu (matching browser_view.rs::path_context_menu)
		(_pathContextOverlay, _pathContextCard, _copyPathMenuItem) = BuildPathContextMenu();
		_rootGrid.Children.Add(_pathContextOverlay);

		// Non-empty directory deletion confirmation modal
		(_confirmOverlay, _confirmMessage) = BuildConfirmOverlay();
		_rootGrid.Children.Add(_confirmOverlay);

		Content = _rootGrid;

		// ================================================================
		// 4. TIMERS & EVENTS
		// ================================================================
		_layoutTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
		_layoutTimer.Tick += (_, _) => StepLayoutAnimation();

		_fadeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
		_fadeTimer.Tick += (_, _) => StepMenuFade();

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

		ActivityLogService.Instance.Changed += () =>
		{
			Dispatcher.UIThread.Post(RefreshStatusDock);
		};

		KeyDown += OnWorkspaceKeyDown;
		Closed += (_, _) => _inspectorView.Dispose();

		RefreshToolbarAndDock();
	}

	#region Titlebar Construction (matching vnlens-gui/src/titlebar.rs)

	private Control BuildTitlebar(
		IReadOnlyList<Control> leftControls,
		Control? centerControl,
		IReadOnlyList<Control> rightControls,
		out Border maximizeIconHost)
	{
		Border bar = new()
		{
			Height = Palette.TitlebarHeight,
			Background = Palette.Background,
			BorderBrush = Palette.Border,
			BorderThickness = new Thickness(0, 0, 0, 1),
			Padding = new Thickness(12, 0, 0, 0),
		};

		Grid layout = new()
		{
			ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
			VerticalAlignment = VerticalAlignment.Stretch,
		};

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
		StackPanel windowControls = new()
		{
			Orientation = Orientation.Horizontal,
			VerticalAlignment = VerticalAlignment.Stretch,
		};

		windowControls.Children.Add(CreateCaptionButton(
			Icons.Glyph(IconKind.WindowMinimize, 12, Palette.Text),
			isClose: false,
			onClick: () => WindowState = WindowState.Minimized));

		maximizeIconHost = new Border
		{
			Width = 12,
			Height = 12,
			Child = Icons.Glyph(IconKind.WindowMaximize, 12, Palette.Text),
		};
		windowControls.Children.Add(CreateCaptionButton(
			maximizeIconHost,
			isClose: false,
			onClick: ToggleMaximize));

		windowControls.Children.Add(CreateCaptionButton(
			Icons.Glyph(IconKind.WindowClose, 12, Palette.Text),
			isClose: true,
			onClick: Close));

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
		btn.PointerPressed += (_, e) =>
		{
			if (e.GetCurrentPoint(btn).Properties.IsLeftButtonPressed)
			{
				e.Handled = true;
				onClick();
			}
		};
		return btn;
	}

	private void OnTitlebarDragPressed(object? sender, PointerPressedEventArgs e)
	{
		if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
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
		WindowState = WindowState == WindowState.Maximized
			? WindowState.Normal
			: WindowState.Maximized;
	}

	private void UpdateMaximizeIcons()
	{
		IconKind kind = WindowState == WindowState.Maximized
			? IconKind.WindowRestore
			: IconKind.WindowMaximize;
		_maxIconHost.Child = Icons.Glyph(kind, 12, Palette.Text);
		_settingsMaxIconHost.Child = Icons.Glyph(kind, 12, Palette.Text);
	}

	#endregion

	#region Welcome Overlay (matching browser_view.rs::welcome)

	private (Grid Overlay, Border Card) BuildWelcomeOverlay()
	{
		Grid overlay = new()
		{
			Margin = new Thickness(24, 24, 24, 66),
		};

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
		hints.Children.Add(new TextBlock
		{
			Text = $"{Shortcut("Shift+O")} folder",
			FontFamily = Palette.UiFont,
			FontSize = 11,
			Foreground = Palette.Tertiary,
		});
		hints.Children.Add(new TextBlock
		{
			Text = $"{Shortcut("O")} files",
			FontFamily = Palette.UiFont,
			FontSize = 11,
			Foreground = Palette.Tertiary,
		});

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

		// Dashed border card using Rectangle StrokeDashArray for authentic vnlens-gui look
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
		cardGrid.Children.Add(new Border
		{
			Padding = new Thickness(28, 36),
			Child = stack,
		});
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
		StackPanel StatusPopoverList,
		Border PathBarGlass,
		StackPanel PathSegmentsPanel,
		VnlensButton ExportPreviewBtn) BuildStatusDock()
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
		StackPanel popoverList = new()
		{
			Orientation = Orientation.Vertical,
			Spacing = 6,
		};

		ScrollViewer popoverScroll = new()
		{
			MaxHeight = 340,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			Content = popoverList,
		};

		Border statusPopover = GlassSurface.Create(10);
		statusPopover.MaxWidth = 650;
		statusPopover.MinWidth = 320;
		statusPopover.Padding = new Thickness(8);
		statusPopover.HorizontalAlignment = HorizontalAlignment.Left;
		statusPopover.VerticalAlignment = VerticalAlignment.Bottom;
		statusPopover.Margin = new Thickness(12, 0, 12, 50);
		statusPopover.IsVisible = false;
		statusPopover.Child = popoverScroll;
		_rootGrid.Children.Add(statusPopover);

		// 3. Breadcrumb Path Bar (glass pill next to status tile)
		StackPanel pathSegments = new()
		{
			Orientation = Orientation.Horizontal,
			VerticalAlignment = VerticalAlignment.Center,
		};

		ScrollViewer pathScroll = new()
		{
			HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
			VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
			Content = pathSegments,
		};

		Border pathBarGlass = GlassSurface.Create(10);
		pathBarGlass.Padding = new Thickness(5, 3);
		pathBarGlass.VerticalAlignment = VerticalAlignment.Bottom;
		pathBarGlass.IsVisible = false;
		pathBarGlass.Child = pathScroll;

		// 4. Right-aligned primary Export button when inspecting a leaf item
		VnlensButton exportPreviewBtn = new(label: "Export…", active: true, enabled: false, ghost: false);
		exportPreviewBtn.IsVisible = false;
		exportPreviewBtn.VerticalAlignment = VerticalAlignment.Bottom;
		exportPreviewBtn.Clicked += () => _ = ExportSelectedAssetAsync();

		// Dock container
		Grid dock = new()
		{
			ColumnDefinitions = new ColumnDefinitions("Auto,8,Auto,*,Auto"),
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

		return (statusTile, iconHost, badgeDot, statusPopover, popoverList, pathBarGlass, pathSegments, exportPreviewBtn);
	}

	private (
		Border Overlay,
		Border Card,
		VnlensMenuItem ExportUnity,
		VnlensMenuItem ExportPrimary,
		VnlensMenuItem ExportSelected,
		VnlensMenuItem TimestampSubfolder,
		VnlensMenuItem ResetItem) BuildExportMenu()
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

		StackPanel list = new()
		{
			Orientation = Orientation.Vertical,
			Spacing = 2,
		};
		list.Children.Add(exportUnity);
		list.Children.Add(exportPrimary);
		list.Children.Add(exportSelected);
		list.Children.Add(new Border { Height = 1, Margin = new Thickness(6, 3), Background = Palette.Border });
		list.Children.Add(timestampSubfolder);
		list.Children.Add(new Border { Height = 1, Margin = new Thickness(6, 3), Background = Palette.Border });
		list.Children.Add(resetItem);

		Border card = GlassSurface.Create(10);
		card.Width = 240;
		card.Padding = new Thickness(4);
		card.HorizontalAlignment = HorizontalAlignment.Right;
		card.VerticalAlignment = VerticalAlignment.Top;
		card.Margin = new Thickness(0, Palette.TitlebarHeight - 4, 12, 0);
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

		Canvas overlay = new()
		{
			Background = Palette.Transparent,
			IsVisible = false,
		};
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
		stack.Children.Add(new TextBlock
		{
			Text = "Directory is not empty",
			FontFamily = Palette.UiFont,
			FontSize = 15,
			FontWeight = FontWeight.SemiBold,
			Foreground = Palette.Warning,
		});
		stack.Children.Add(msg);
		stack.Children.Add(buttons);

		Border card = GlassSurface.Create(12);
		card.MaxWidth = 440;
		card.Padding = new Thickness(20);
		card.HorizontalAlignment = HorizontalAlignment.Center;
		card.VerticalAlignment = VerticalAlignment.Center;
		card.Child = stack;

		Grid overlay = new()
		{
			Background = new SolidColorBrush(Color.FromArgb(0x99, 0x00, 0x00, 0x00)),
			IsVisible = false,
		};
		overlay.Children.Add(card);

		return (overlay, msg);
	}

	private Task<bool> ConfirmDeleteDirectoryAsync(string path)
	{
		_confirmTcs = new TaskCompletionSource<bool>();
		_confirmMessage.Text = $"{Localization.ExportDirectoryDeleteUserConfirmation}\n\n{path}";
		_confirmOverlay.IsVisible = true;
		return _confirmTcs.Task;
	}

	private void ResolveConfirmation(bool result)
	{
		_confirmOverlay.IsVisible = false;
		_confirmTcs?.TrySetResult(result);
		_confirmTcs = null;
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
		List<string> paths = items
			.Select(i => i.TryGetLocalPath())
			.Where(p => !string.IsNullOrEmpty(p))
			.Cast<string>()
			.ToList();
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
		IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
		{
			Title = Localization.MenuFileOpenFolder,
			AllowMultiple = true,
		});
		List<string> paths = folders
			.Select(f => f.TryGetLocalPath())
			.Where(p => !string.IsNullOrEmpty(p))
			.Cast<string>()
			.ToList();
		if (paths.Count > 0)
		{
			await LoadPathsAsync(paths);
		}
	}

	private async Task OpenFilesDialogAsync()
	{
		IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = Localization.MenuFileOpenFile,
			AllowMultiple = true,
		});
		List<string> paths = files
			.Select(f => f.TryGetLocalPath())
			.Where(p => !string.IsNullOrEmpty(p))
			.Cast<string>()
			.ToList();
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
		RefreshToolbarAndDock();

		using IDisposable task = ActivityLogService.Instance.BeginTask($"Loading {paths.Count} item(s)…");
		try
		{
			await Task.Run(() => GameFileLoader.LoadAndProcess(paths));
			BuildRootBrowser();
		}
		catch (Exception ex)
		{
			ActivityLogService.Instance.ReportError($"Load failed: {ex.Message}");
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
		if (!GameFileLoader.IsLoaded || _exporting)
		{
			return;
		}
		IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
		{
			Title = primaryContent ? Localization.ExportPrimaryContent : Localization.MenuExportAll,
			AllowMultiple = false,
		});
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

		using IDisposable task = ActivityLogService.Instance.BeginTask(
			primaryContent ? "Exporting primary content…" : "Exporting Unity project…");
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
			ActivityLogService.Instance.ReportError($"Export failed: {ex.Message}");
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
			if (AssetContentService.HasImageData(asset)) defaultExt = "png";
			else if (AssetContentService.HasAudioData(asset)) defaultExt = _inspectorView.CurrentDecodedAudio?.EncodedExtension ?? "wav";
			else if (AssetContentService.HasModelData(asset)) defaultExt = "glb";
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
			if (string.IsNullOrEmpty(defaultExt)) defaultExt = "bin";
		}
		else if (item.Target is FailedFile)
		{
			defaultExt = "txt";
		}

		IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			Title = "Export selected asset",
			SuggestedFileName = baseName.EndsWith("." + defaultExt, StringComparison.OrdinalIgnoreCase)
				? baseName
				: $"{baseName}.{defaultExt}",
			DefaultExtension = defaultExt,
		});
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
			ActivityLogService.Instance.ReportError($"Export failed: {ex.Message}");
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
		ClearAllColumns();
		_selectedLeaf = null;
		_selectedLeafDepth = null;
		_inspectorView.Inspect(null);

		if (!GameFileLoader.IsLoaded)
		{
			_welcomeOverlay.IsVisible = true;
			TriggerLayoutUpdate(animate: true);
			RefreshToolbarAndDock();
			return;
		}

		_welcomeOverlay.IsVisible = false;
		List<BrowserItem> rootItems = BrowserFolderBuilder.BuildRoot(GameFileLoader.GameBundle);
		ColumnView rootCol = CreateColumn(0, rootItems, animate: true);
		_columns.Add(rootCol);
		_columnsPanel.Children.Insert(0, rootCol);
		SetActiveColumn(0);
		TriggerLayoutUpdate(animate: true);
		RefreshToolbarAndDock();
	}

	private ColumnView CreateColumn(int depth, List<BrowserItem> items, bool animate)
	{
		ColumnView col = new(depth, items, animate);
		col.ColumnClicked += c =>
		{
			SetActiveColumn(c.Depth);
			RefreshToolbarAndDock();
		};
		col.ItemActivated += OnColumnItemActivated;
		col.WidthChangedByUser += (_, _) => TriggerLayoutUpdate(animate: false);
		return col;
	}

	private void OnColumnItemActivated(ColumnView column, int rowIndex, BrowserItem item, bool doubleClicked)
	{
		int depth = column.Depth;
		SetActiveColumn(depth);
		TrimColumnsAfter(depth);

		if (item.IsContainer)
		{
			_selectedLeaf = null;
			_selectedLeafDepth = null;
			_inspectorView.Inspect(null);

			List<BrowserItem> children = BrowserFolderBuilder.BuildChildren(item);
			ColumnView childCol = CreateColumn(depth + 1, children, animate: true);
			_columns.Add(childCol);
			_columnsPanel.Children.Insert(_columns.Count - 1, childCol);

			if (doubleClicked && children.Count > 0)
			{
				SetActiveColumn(depth + 1);
				childCol.SelectIndex(0, raiseEvent: true);
				return;
			}

			_revealColumnDepth = depth + 1;
		}
		else
		{
			_selectedLeaf = item;
			_selectedLeafDepth = depth;
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
				_columns[depth].SelectTarget(assetToSelect, raiseEvent: true);
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

	private void GoHome()
	{
		if (!GameFileLoader.IsLoaded)
		{
			return;
		}
		_searchBox.Text = "";
		BuildRootBrowser();
	}

	private void GoBack()
	{
		if (_columns.Count == 0)
		{
			return;
		}
		if (_selectedLeaf is not null)
		{
			_selectedLeaf = null;
			_selectedLeafDepth = null;
			_inspectorView.Inspect(null);
			_columns[_activeColumn].SelectIndex(null, raiseEvent: false);
			TriggerLayoutUpdate(animate: true);
			RefreshToolbarAndDock();
			return;
		}
		if (_activeColumn > 0)
		{
			int target = _activeColumn - 1;
			TrimColumnsAfter(target);
			SetActiveColumn(target);
			_revealColumnDepth = target;
			TriggerLayoutUpdate(animate: true);
			RefreshToolbarAndDock();
		}
		else if (_columns.Count > 1)
		{
			TrimColumnsAfter(0);
			SetActiveColumn(0);
			TriggerLayoutUpdate(animate: true);
			RefreshToolbarAndDock();
		}
	}

	private void CollapseToPathDepth(int depth)
	{
		if (depth < 0 || depth >= _columns.Count)
		{
			return;
		}
		ColumnView col = _columns[depth];
		if (col.SelectedItem is { IsContainer: true })
		{
			TrimColumnsAfter(depth + 1);
			_selectedLeaf = null;
			_selectedLeafDepth = null;
			_inspectorView.Inspect(null);
			SetActiveColumn(depth);
			_revealColumnDepth = depth + 1;
		}
		else
		{
			TrimColumnsAfter(depth);
			SetActiveColumn(depth);
			_revealColumnDepth = depth;
		}
		TriggerLayoutUpdate(animate: true);
		RefreshToolbarAndDock();
	}

	private void TriggerLayoutUpdate(bool animate)
	{
		double viewport = Bounds.Width;
		if (viewport <= 0 || _columns.Count == 0)
		{
			_currentInset = 0;
			_inspectorContainer.Width = 0;
			_inspectorContainer.IsVisible = false;
			return;
		}

		bool preview = _showInspector && _selectedLeaf is not null;
		int depth;
		if (_selectedLeafDepth.HasValue)
		{
			depth = _selectedLeafDepth.Value;
		}
		else if (_revealColumnDepth.HasValue)
		{
			depth = _revealColumnDepth.Value;
			_revealColumnDepth = null;
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
			_manualInspectorWidth);

		LayoutSnapshot from = new(_currentInset, _stripScroll.Offset.X);
		_layoutMotion.Retarget(from, target, animate && !_resizingInspector);
		StepLayoutAnimation();
		if (_layoutMotion.IsAnimating)
		{
			_layoutTimer.Start();
		}
	}

	private void StepLayoutAnimation()
	{
		(LayoutSnapshot current, bool animating) = _layoutMotion.Sample();
		_currentInset = current.Inset;
		_inspectorContainer.Width = Math.Max(0, current.Inset);
		_inspectorContainer.IsVisible = current.Inset > 0.5;
		_stripScroll.Offset = new Vector(current.Scroll, _stripScroll.Offset.Y);

		_exportPreviewBtn.IsVisible = !_showSettings && current.Inset > 0.5 && _selectedLeaf is not null;

		if (!animating)
		{
			_layoutTimer.Stop();
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
		TriggerLayoutUpdate(animate: true);
		RefreshToolbarAndDock();
	}

	private void ToggleSettings()
	{
		ClosePopups();
		_showSettings = !_showSettings;
		_settingsLayer.IsVisible = _showSettings;
		_workspaceLayer.IsVisible = !_showSettings;
		if (_showSettings)
		{
			_settingsView.Refresh();
		}
		RefreshToolbarAndDock();
	}

	private void ToggleExportMenu()
	{
		if (_exporting)
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
		}
	}

	private void ClosePopups()
	{
		_showExportMenu = false;
		_exportMenuOverlay.IsVisible = false;
		_exportBtn.IsActive = false;
		_pathContextOverlay.IsVisible = false;
	}

	private void StartMenuFade(Control target)
	{
		_fadingControl = target;
		_fadingControl.Opacity = 0;
		_fadeStarted = Stopwatch.GetTimestamp();
		_fadeTimer.Start();
	}

	private void StepMenuFade()
	{
		if (_fadingControl is null)
		{
			_fadeTimer.Stop();
			return;
		}
		double t = Stopwatch.GetElapsedTime(_fadeStarted).TotalSeconds / Palette.MenuEnterDuration.TotalSeconds;
		if (t >= 1.0)
		{
			_fadingControl.Opacity = 1.0;
			_fadingControl = null;
			_fadeTimer.Stop();
		}
		else
		{
			_fadingControl.Opacity = LayoutMotion.EaseInOut(t);
		}
	}

	private void RefreshToolbarAndDock()
	{
		bool loaded = GameFileLoader.IsLoaded;

		_homeBtn.IsActionEnabled = loaded;
		_backBtn.IsActionEnabled = _activeColumn > 0 || _columns.Count > 1 || _selectedLeaf is not null;
		_searchContainer.IsVisible = loaded;
		_exportBtn.IsActionEnabled = !_exporting;
		_inspectorBtn.IsActionEnabled = _selectedLeaf is not null;
		_inspectorBtn.IsActive = _showInspector && _selectedLeaf is not null;

		_menuExportUnity.IsActionEnabled = loaded && !_exporting;
		_menuExportPrimary.IsActionEnabled = loaded && !_exporting;
		_menuExportSelected.IsActionEnabled = _selectedLeaf is not null && !_exporting;
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

		// Root "Loaded" chip
		Border rootChip = CreatePathChip(
			Icons.Glyph(IconKind.Home, 12, Palette.Secondary),
			"Loaded",
			isLast: _columns[0].SelectedItem is null,
			fullPath: "Loaded",
			onClick: GoHome);
		_pathSegmentsPanel.Children.Add(rootChip);

		string runningPath = "";
		List<(int Depth, BrowserItem Item, string FullPath)> segments = new();
		for (int d = 0; d < _columns.Count; d++)
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

			_pathSegmentsPanel.Children.Add(new Border
			{
				Margin = new Thickness(1, 0),
				VerticalAlignment = VerticalAlignment.Center,
				Child = Icons.Glyph(IconKind.ChevronRight, 9, Palette.Tertiary),
			});

			int capturedDepth = depth;
			Border chip = CreatePathChip(
				Icons.FileIcon(item.Icon, false, size: 13),
				item.Name,
				isLast,
				fullPath,
				onClick: () => CollapseToPathDepth(capturedDepth));
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
		content.Children.Add(new TextBlock
		{
			Text = text,
			FontFamily = Palette.UiFont,
			FontSize = 10,
			Foreground = isLast ? Palette.Text : Palette.Secondary,
			VerticalAlignment = VerticalAlignment.Center,
		});

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
		StartMenuFade(_pathContextCard);
	}

	private void RefreshStatusDock()
	{
		ActivityLogService log = ActivityLogService.Instance;
		bool busy = log.IsBusy || _loading || _exporting;
		bool failed = log.HasErrors;
		bool hasWarn = !string.IsNullOrEmpty(log.LastWarning);

		if (busy)
		{
			if (_statusIconHost.Child is not VnlensSpinner)
			{
				_statusIconHost.Child = new VnlensSpinner(15, Palette.Accent);
			}
			_statusBadgeDot.IsVisible = failed || hasWarn;
			_statusBadgeDot.Background = failed ? Palette.Danger : Palette.Warning;
		}
		else if (failed)
		{
			_statusIconHost.Child = Icons.Glyph(IconKind.Alert, 16, Palette.Danger);
			_statusBadgeDot.IsVisible = false;
		}
		else if (hasWarn)
		{
			_statusIconHost.Child = Icons.Glyph(IconKind.Alert, 16, Palette.Warning);
			_statusBadgeDot.IsVisible = false;
		}
		else
		{
			_statusIconHost.Child = Icons.Glyph(IconKind.Check, 16, Palette.Success);
			_statusBadgeDot.IsVisible = false;
		}

		if (!_showStatus)
		{
			return;
		}

		_statusPopoverList.Children.Clear();

		if (busy && log.ActiveJobLabel is { } jobLabel)
		{
			Grid busyRow = new()
			{
				ColumnDefinitions = new ColumnDefinitions("*,8,Auto"),
				Margin = new Thickness(6, 4),
			};
			busyRow.Children.Add(new TextBlock
			{
				Text = jobLabel,
				FontFamily = Palette.UiFont,
				FontSize = 11,
				Foreground = Palette.Accent,
				TextWrapping = TextWrapping.Wrap,
				VerticalAlignment = VerticalAlignment.Center,
			});
			VnlensSpinner sp = new(14, Palette.Accent);
			Grid.SetColumn(sp, 2);
			busyRow.Children.Add(sp);
			_statusPopoverList.Children.Add(busyRow);
		}

		if (!string.IsNullOrEmpty(log.Status))
		{
			_statusPopoverList.Children.Add(new TextBlock
			{
				Text = log.Status,
				Margin = new Thickness(6, 2),
				FontFamily = Palette.UiFont,
				FontSize = 11,
				Foreground = Palette.Secondary,
				TextWrapping = TextWrapping.Wrap,
			});
		}

		if (log.LastWarning is { } warn)
		{
			_statusPopoverList.Children.Add(new TextBlock
			{
				Text = warn,
				Margin = new Thickness(6, 2),
				FontFamily = Palette.UiFont,
				FontSize = 11,
				Foreground = Palette.Warning,
				TextWrapping = TextWrapping.Wrap,
			});
		}

		IReadOnlyList<ActivityError> errors = log.GetErrors();
		foreach (ActivityError err in errors.TakeLast(10))
		{
			ActivityError captured = err;
			Border errRow = new()
			{
				Padding = new Thickness(8, 4, 4, 4),
				CornerRadius = new CornerRadius(6),
			};
			errRow.PointerEntered += (_, _) => errRow.Background = Palette.RowHover;
			errRow.PointerExited += (_, _) => errRow.Background = Palette.Transparent;

			Grid grid = new()
			{
				ColumnDefinitions = new ColumnDefinitions("*,4,Auto,4,Auto"),
				VerticalAlignment = VerticalAlignment.Center,
			};
			grid.Children.Add(new TextBlock
			{
				Text = captured.Message,
				FontFamily = Palette.UiFont,
				FontSize = 11,
				Foreground = Palette.Danger,
				TextWrapping = TextWrapping.Wrap,
				VerticalAlignment = VerticalAlignment.Center,
			});

			VnlensButton copyBtn = new(IconKind.Copy, ghost: true, height: 22, iconSize: 12);
			GlassSurface.SetTooltip(copyBtn, "Copy");
			copyBtn.Clicked += async () =>
			{
				if (Clipboard is not null)
				{
					await Clipboard.SetTextAsync(captured.Message);
				}
			};
			Grid.SetColumn(copyBtn, 2);
			grid.Children.Add(copyBtn);

			VnlensButton dismissBtn = new(IconKind.WindowClose, ghost: true, height: 22, iconSize: 12);
			GlassSurface.SetTooltip(dismissBtn, "Dismiss");
			dismissBtn.Clicked += () => ActivityLogService.Instance.DismissError(captured.Id);
			Grid.SetColumn(dismissBtn, 4);
			grid.Children.Add(dismissBtn);

			errRow.Child = grid;
			_statusPopoverList.Children.Add(errRow);
		}

		if (!string.IsNullOrEmpty(_lastExportOutputPath))
		{
			string outputPath = _lastExportOutputPath;
			VnlensButton showOutputBtn = new(label: "Show output", active: false, enabled: true, ghost: false, height: 26);
			showOutputBtn.HorizontalAlignment = HorizontalAlignment.Left;
			showOutputBtn.Margin = new Thickness(6, 4, 0, 2);
			showOutputBtn.Clicked += () => OpenInSystemExplorer(outputPath);
			_statusPopoverList.Children.Add(showOutputBtn);
		}

		// Recent log lines
		IReadOnlyList<ActivityLogEntry> recent = log.GetRecentLogs();
		if (recent.Count > 0)
		{
			_statusPopoverList.Children.Add(new Border
			{
				Height = 1,
				Margin = new Thickness(4, 4),
				Background = Palette.Border,
			});

			foreach (ActivityLogEntry entry in recent.TakeLast(15))
			{
				IBrush color = entry.Type switch
				{
					Import.Logging.LogType.Error => Palette.Danger,
					Import.Logging.LogType.Warning => Palette.Warning,
					_ => Palette.Tertiary,
				};
				_statusPopoverList.Children.Add(new TextBlock
				{
					Text = entry.Category == Import.Logging.LogCategory.None
						? entry.Message
						: $"[{entry.Category}] {entry.Message}",
					Margin = new Thickness(6, 1),
					FontFamily = Palette.MonoFont,
					FontSize = 10,
					Foreground = color,
					TextWrapping = TextWrapping.Wrap,
				});
			}
		}
	}

	#endregion

	#region Keyboard Shortcuts (matching vnlens-gui/src/workspace.rs::bind_keys)

	private void OnWorkspaceKeyDown(object? sender, KeyEventArgs e)
	{
		bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
		bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
		bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);

		if (e.Key == Key.Escape)
		{
			if (_confirmOverlay.IsVisible)
			{
				ResolveConfirmation(false);
				e.Handled = true;
			}
			else if (_showExportMenu || _pathContextOverlay.IsVisible)
			{
				ClosePopups();
				e.Handled = true;
			}
			else if (_showStatus)
			{
				_showStatus = false;
				_statusPopover.IsVisible = false;
				e.Handled = true;
			}
			else if (_showSettings)
			{
				ToggleSettings();
				e.Handled = true;
			}
			return;
		}

		if (ctrl && e.Key == Key.OemComma)
		{
			ToggleSettings();
			e.Handled = true;
			return;
		}

		if (_showSettings)
		{
			return;
		}

		if (ctrl && shift && e.Key == Key.O)
		{
			_ = OpenFolderDialogAsync();
			e.Handled = true;
			return;
		}
		if (ctrl && e.Key == Key.O)
		{
			_ = OpenFilesDialogAsync();
			e.Handled = true;
			return;
		}
		if (ctrl && e.Key == Key.E)
		{
			ToggleExportMenu();
			e.Handled = true;
			return;
		}
		if (ctrl && e.Key == Key.I)
		{
			ToggleInspector();
			e.Handled = true;
			return;
		}
		if (ctrl && e.Key == Key.F && GameFileLoader.IsLoaded)
		{
			_searchBox.Focus();
			_searchBox.SelectAll();
			e.Handled = true;
			return;
		}
		if (alt && e.Key == Key.Home)
		{
			GoHome();
			e.Handled = true;
			return;
		}
		if (alt && e.Key == Key.Left)
		{
			GoBack();
			e.Handled = true;
			return;
		}

		// Don't hijack navigation keys while typing inside a TextBox
		if (FocusManager?.GetFocusedElement() is TextBox)
		{
			return;
		}

		if (e.Key == Key.Space && _showInspector && _selectedLeaf is not null)
		{
			_inspectorView.ToggleAudioPlayback();
			e.Handled = true;
			return;
		}

		if (_columns.Count == 0)
		{
			return;
		}

		switch (e.Key)
		{
			case Key.Up:
				_columns[_activeColumn].MoveSelection(-1);
				e.Handled = true;
				break;
			case Key.Down:
				_columns[_activeColumn].MoveSelection(1);
				e.Handled = true;
				break;
			case Key.Left:
				GoBack();
				e.Handled = true;
				break;
			case Key.Right:
			case Key.Enter:
				if (_activeColumn + 1 < _columns.Count && _columns[_activeColumn + 1].ItemCount > 0)
				{
					SetActiveColumn(_activeColumn + 1);
					_columns[_activeColumn].SelectIndex(0, raiseEvent: true);
					e.Handled = true;
				}
				break;
		}
	}

	#endregion

	private static string Shortcut(string key) =>
		OperatingSystem.IsMacOS() ? $"⌘{key}" : $"Ctrl+{key}";

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
				case "png": format = ImageExportFormat.Png; return true;
				case "bmp": format = ImageExportFormat.Bmp; return true;
				case "jpg":
				case "jpeg": format = ImageExportFormat.Jpeg; return true;
				case "tga": format = ImageExportFormat.Tga; return true;
				case "exr": format = ImageExportFormat.Exr; return true;
				case "hdr": format = ImageExportFormat.Hdr; return true;
				default: format = default; return false;
			}
		}
	}
}
