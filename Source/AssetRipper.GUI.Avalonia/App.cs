using AssetRipper.GUI.Avalonia.Theme;
using AssetRipper.GUI.Avalonia.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace AssetRipper.GUI.Avalonia;

internal sealed class App : Application
{
	public static string[] StartupPaths { get; set; } = [];

	public override void Initialize()
	{
		RequestedThemeVariant = ThemeVariant.Dark;
		Styles.Add(new FluentTheme());

		// Override default ListBoxItem chrome so vnlens-gui row pills render cleanly without system highlight boxes
		Style listBoxItemStyle = new(x => x.OfType<ListBoxItem>());
		listBoxItemStyle.Setters.Add(new Setter(Layoutable.MarginProperty, new Thickness(0)));
		listBoxItemStyle.Setters.Add(new Setter(TemplatedControl.PaddingProperty, new Thickness(0)));
		listBoxItemStyle.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, Palette.Transparent));
		listBoxItemStyle.Setters.Add(new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)));
		Styles.Add(listBoxItemStyle);

		Style listBoxItemSelected = new(x => x.OfType<ListBoxItem>().Class(":selected").Template().OfType<ContentPresenter>());
		listBoxItemSelected.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, Palette.Transparent));
		Styles.Add(listBoxItemSelected);

		Style listBoxItemPointerOver = new(x => x.OfType<ListBoxItem>().Class(":pointerover").Template().OfType<ContentPresenter>());
		listBoxItemPointerOver.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, Palette.Transparent));
		Styles.Add(listBoxItemPointerOver);

		// Clean glass ToolTip styling matching vnlens-gui
		Style toolTipStyle = new(x => x.OfType<ToolTip>());
		toolTipStyle.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, Palette.Transparent));
		toolTipStyle.Setters.Add(new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)));
		toolTipStyle.Setters.Add(new Setter(TemplatedControl.PaddingProperty, new Thickness(0)));
		Styles.Add(toolTipStyle);

		// Subtle dark ComboBox & TextBox polish
		Style comboStyle = new(x => x.OfType<ComboBox>());
		comboStyle.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, Palette.Control));
		comboStyle.Setters.Add(new Setter(TemplatedControl.BorderBrushProperty, Palette.Border));
		comboStyle.Setters.Add(new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(7)));
		comboStyle.Setters.Add(new Setter(TemplatedControl.ForegroundProperty, Palette.Text));
		Styles.Add(comboStyle);

		Style textBoxStyle = new(x => x.OfType<TextBox>());
		textBoxStyle.Setters.Add(new Setter(TemplatedControl.BackgroundProperty, Palette.Control));
		textBoxStyle.Setters.Add(new Setter(TemplatedControl.BorderBrushProperty, Palette.Border));
		textBoxStyle.Setters.Add(new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(7)));
		textBoxStyle.Setters.Add(new Setter(TemplatedControl.ForegroundProperty, Palette.Text));
		Styles.Add(textBoxStyle);
	}

	public override void OnFrameworkInitializationCompleted()
	{
		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			MainWindow window = new();
			desktop.MainWindow = window;
			if (StartupPaths.Length > 0)
			{
				window.Opened += (_, _) => _ = window.LoadPathsAsync(StartupPaths);
			}
		}

		base.OnFrameworkInitializationCompleted();
	}
}
