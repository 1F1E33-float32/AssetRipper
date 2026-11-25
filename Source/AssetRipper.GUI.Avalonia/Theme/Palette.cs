using Avalonia;
using Avalonia.Media;

namespace AssetRipper.GUI.Avalonia.Theme;

internal static class Palette
{
	public static readonly Color BackgroundColor = Color.Parse("#1C1C1E");
	public static readonly Color PanelColor = Color.Parse("#242426");
	public static readonly Color PanelAltColor = Color.Parse("#28282B");
	public static readonly Color BorderColor = Color.Parse("#37373B");
	public static readonly Color TextColor = Color.Parse("#F5F5F7");
	public static readonly Color SecondaryColor = Color.Parse("#98989F");
	public static readonly Color TertiaryColor = Color.Parse("#6B6B73");
	public static readonly Color DisabledColor = Color.Parse("#77777C");
	public static readonly Color AccentColor = Color.Parse("#0A84FF");
	public static readonly Color AccentHoverColor = Color.Parse("#409CFF");
	public static readonly Color AccentTextColor = Color.Parse("#9DCAFF");
	public static readonly Color ControlColor = Color.Parse("#363639");
	public static readonly Color ControlHoverColor = Color.Parse("#46464B");
	public static readonly Color DangerColor = Color.Parse("#FF8A82");
	public static readonly Color WarningColor = Color.Parse("#FFD60A");
	public static readonly Color SuccessColor = Color.Parse("#30D158");

	public static readonly Color GlassBackgroundColor = Color.FromArgb(0xF2, 0x2A, 0x2A, 0x2D);
	public static readonly Color GlassBorderColor = Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF);
	public static readonly Color ColumnBorderColor = Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF);
	public static readonly Color RootColumnBackgroundColor = Color.FromArgb(0x05, 0xFF, 0xFF, 0xFF);
	public static readonly Color RowHoverColor = Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF);
	public static readonly Color RowSelectedInactiveColor = Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF);
	public static readonly Color RowGroupBgColor = Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF);
	public static readonly Color GhostHoverColor = Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF);
	public static readonly Color GhostActiveColor = Color.FromArgb(0x2E, 0x0A, 0x84, 0xFF);
	public static readonly Color GhostActiveHoverColor = Color.FromArgb(0x42, 0x0A, 0x84, 0xFF);
	public static readonly Color MenuHoverColor = Color.FromArgb(0xCC, 0x0A, 0x84, 0xFF);
	public static readonly Color CloseHoverColor = Color.Parse("#C42B1C");
	public static readonly Color WindowControlHoverColor = Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF);
	public static readonly Color ResizeHoverColor = Color.FromArgb(0x80, 0x0A, 0x84, 0xFF);
	public static readonly Color DropZoneAccentBgColor = Color.FromArgb(0x14, 0x0A, 0x84, 0xFF);
	public static readonly Color WelcomeIconBgColor = Color.FromArgb(0x1F, 0x0A, 0x84, 0xFF);
	public static readonly Color PillBgColor = Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF);
	public static readonly Color PathHoverColor = Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF);
	public static readonly Color MutedOnAccentColor = Color.FromArgb(0xC7, 0xFF, 0xFF, 0xFF);
	public static readonly Color MutedDefaultColor = Color.FromArgb(0x6B, 0xFF, 0xFF, 0xFF);
	public static readonly Color CheckerLightColor = Color.Parse("#38383B");
	public static readonly Color CheckerDarkColor = Color.Parse("#2C2C2E");
	public static readonly Color PlayheadColor = Color.Parse("#FFD166");
	public static readonly Color TableSelectedColor = Color.Parse("#24496B");

	// Brushes
	public static readonly ISolidColorBrush Background = new SolidColorBrush(BackgroundColor);
	public static readonly ISolidColorBrush Panel = new SolidColorBrush(PanelColor);
	public static readonly ISolidColorBrush PanelAlt = new SolidColorBrush(PanelAltColor);
	public static readonly ISolidColorBrush Border = new SolidColorBrush(BorderColor);
	public static readonly ISolidColorBrush Text = new SolidColorBrush(TextColor);
	public static readonly ISolidColorBrush Secondary = new SolidColorBrush(SecondaryColor);
	public static readonly ISolidColorBrush Tertiary = new SolidColorBrush(TertiaryColor);
	public static readonly ISolidColorBrush Disabled = new SolidColorBrush(DisabledColor);
	public static readonly ISolidColorBrush Accent = new SolidColorBrush(AccentColor);
	public static readonly ISolidColorBrush AccentHover = new SolidColorBrush(AccentHoverColor);
	public static readonly ISolidColorBrush AccentText = new SolidColorBrush(AccentTextColor);
	public static readonly ISolidColorBrush Control = new SolidColorBrush(ControlColor);
	public static readonly ISolidColorBrush ControlHover = new SolidColorBrush(ControlHoverColor);
	public static readonly ISolidColorBrush Danger = new SolidColorBrush(DangerColor);
	public static readonly ISolidColorBrush Warning = new SolidColorBrush(WarningColor);
	public static readonly ISolidColorBrush Success = new SolidColorBrush(SuccessColor);
	public static readonly ISolidColorBrush White = Brushes.White;
	public static readonly ISolidColorBrush Transparent = Brushes.Transparent;

	public static readonly ISolidColorBrush GlassBackground = new SolidColorBrush(GlassBackgroundColor);
	public static readonly ISolidColorBrush GlassBorder = new SolidColorBrush(GlassBorderColor);
	public static readonly ISolidColorBrush ColumnBorder = new SolidColorBrush(ColumnBorderColor);
	public static readonly ISolidColorBrush RootColumnBackground = new SolidColorBrush(RootColumnBackgroundColor);
	public static readonly ISolidColorBrush RowHover = new SolidColorBrush(RowHoverColor);
	public static readonly ISolidColorBrush RowSelectedInactive = new SolidColorBrush(RowSelectedInactiveColor);
	public static readonly ISolidColorBrush RowGroupBg = new SolidColorBrush(RowGroupBgColor);
	public static readonly ISolidColorBrush GhostHover = new SolidColorBrush(GhostHoverColor);
	public static readonly ISolidColorBrush GhostActive = new SolidColorBrush(GhostActiveColor);
	public static readonly ISolidColorBrush GhostActiveHover = new SolidColorBrush(GhostActiveHoverColor);
	public static readonly ISolidColorBrush MenuHover = new SolidColorBrush(MenuHoverColor);
	public static readonly ISolidColorBrush CloseHover = new SolidColorBrush(CloseHoverColor);
	public static readonly ISolidColorBrush WindowControlHover = new SolidColorBrush(WindowControlHoverColor);
	public static readonly ISolidColorBrush ResizeHover = new SolidColorBrush(ResizeHoverColor);
	public static readonly ISolidColorBrush DropZoneAccentBg = new SolidColorBrush(DropZoneAccentBgColor);
	public static readonly ISolidColorBrush WelcomeIconBg = new SolidColorBrush(WelcomeIconBgColor);
	public static readonly ISolidColorBrush PillBg = new SolidColorBrush(PillBgColor);
	public static readonly ISolidColorBrush PathHover = new SolidColorBrush(PathHoverColor);
	public static readonly ISolidColorBrush MutedOnAccent = new SolidColorBrush(MutedOnAccentColor);
	public static readonly ISolidColorBrush MutedDefault = new SolidColorBrush(MutedDefaultColor);
	public static readonly ISolidColorBrush CheckerLight = new SolidColorBrush(CheckerLightColor);
	public static readonly ISolidColorBrush CheckerDark = new SolidColorBrush(CheckerDarkColor);
	public static readonly ISolidColorBrush Playhead = new SolidColorBrush(PlayheadColor);
	public static readonly ISolidColorBrush TableSelected = new SolidColorBrush(TableSelectedColor);

	// Fonts
	public static readonly FontFamily UiFont = new("Segoe UI, Inter, -apple-system, BlinkMacSystemFont, sans-serif");
	public static readonly FontFamily MonoFont = new("Cascadia Mono, Consolas, JetBrains Mono, monospace");

	// Layout and animation constants
	public const double TitlebarHeight = 40.0;
	public const double ColumnWidth = 330.0;
	public const double ColumnMinWidth = 180.0;
	public const double ColumnMaxWidth = 900.0;
	public const double InspectorMinWidth = 180.0;
	public const double RowHeight = 26.0;
	public const double InspectorHeaderHeight = 38.0;

	public static readonly TimeSpan LayoutDuration = TimeSpan.FromMilliseconds(220);
	public static readonly TimeSpan ColumnEnterDuration = TimeSpan.FromMilliseconds(180);
	public static readonly TimeSpan ZoomDuration = TimeSpan.FromMilliseconds(180);
	public static readonly TimeSpan ToggleDuration = TimeSpan.FromMilliseconds(180);
	public static readonly TimeSpan MenuEnterDuration = TimeSpan.FromMilliseconds(140);
	public static readonly TimeSpan SpinnerDuration = TimeSpan.FromMilliseconds(900);

	public static readonly BoxShadows GlassShadow = new(new BoxShadow
	{
		OffsetX = 0,
		OffsetY = 8,
		Blur = 24,
		Spread = 0,
		Color = Color.FromArgb(0x88, 0x00, 0x00, 0x00),
	});

	public static readonly BoxShadows KnobShadow = new(new BoxShadow
	{
		OffsetX = 0,
		OffsetY = 1,
		Blur = 3,
		Spread = 0,
		Color = Color.FromArgb(0x66, 0x00, 0x00, 0x00),
	});
}
