using AssetRipper.Export.Configuration;
using AssetRipper.GUI.Avalonia.Controls;
using AssetRipper.GUI.Avalonia.Settings;
using AssetRipper.GUI.Avalonia.Theme;
using AssetRipper.GUI.Licensing;
using AssetRipper.GUI.Localizations;
using AssetRipper.Primitives;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AssetRipper.GUI.Avalonia.Views;

/// <summary>Full-window Settings panel bound directly to <see cref="GameFileLoader.Settings"/>.</summary>
internal sealed class SettingsView : Grid
{
	private readonly StackPanel _sectionsContainer;
	private readonly Border _loadedWarningBanner;

	public event Action? ResetRequested;
	public event Action? LanguageChanged;

	public SettingsView()
	{
		Background = Palette.Background;

		_sectionsContainer = new StackPanel
		{
			Orientation = Orientation.Vertical,
			Spacing = 8,
			MaxWidth = 640,
			HorizontalAlignment = HorizontalAlignment.Stretch,
			Margin = new Thickness(16, 28, 16, 68),
		};

		_loadedWarningBanner = new Border
		{
			Padding = new Thickness(16, 12),
			CornerRadius = new CornerRadius(10),
			Background = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xD6, 0x0A)),
			BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xD6, 0x0A)),
			BorderThickness = new Thickness(1),
			Margin = new Thickness(0, 0, 0, 8),
			IsVisible = false,
		};

		ScrollViewer scroll = new()
		{
			HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
			VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
			Content = _sectionsContainer,
		};

		Children.Add(scroll);
		Rebuild();
	}

	public void Refresh()
	{
		Rebuild();
	}

	private void Rebuild()
	{
		_sectionsContainer.Children.Clear();
		FullConfiguration cfg = GameFileLoader.Settings;
		bool locked = GameFileLoader.IsLoaded;

		// Locked notice banner when files are loaded
		if (locked)
		{
			Grid bannerGrid = new() { ColumnDefinitions = new ColumnDefinitions("*,12,Auto"), VerticalAlignment = VerticalAlignment.Center };
			bannerGrid.Children.Add(
				new TextBlock
				{
					Text = Localization.SettingsImportExportLocked,
					FontFamily = Palette.UiFont,
					FontSize = 12,
					Foreground = Palette.Warning,
					TextWrapping = TextWrapping.Wrap,
					VerticalAlignment = VerticalAlignment.Center,
				}
			);

			VnlensButton resetBtn = new(label: Localization.MenuFileReset, active: false, enabled: true, ghost: false, height: 28);
			resetBtn.Clicked += () =>
			{
				ResetRequested?.Invoke();
				Rebuild();
			};
			Grid.SetColumn(resetBtn, 2);
			bannerGrid.Children.Add(resetBtn);

			_loadedWarningBanner.Child = bannerGrid;
			_loadedWarningBanner.IsVisible = true;
			_sectionsContainer.Children.Add(_loadedWarningBanner);
		}

		// IMPORT SECTION
		AddSectionHeader(Localization.MenuImport.ToUpperInvariant());
		AddGameCard(cfg, !locked);
		AddToggleCard(
			Localization.SkipStreamingAssets,
			"Skip loading files inside StreamingAssets folders.",
			cfg.ImportSettings.IgnoreStreamingAssets,
			!locked,
			v =>
			{
				cfg.ImportSettings.IgnoreStreamingAssets = v;
				Persist(cfg);
			}
		);

		AddToggleCard(
			Localization.RemoveNullableAttributes,
			"Remove compiler-generated nullable reference attributes from scripts.",
			cfg.ProcessingSettings.RemoveNullableAttributes,
			!locked,
			v =>
			{
				cfg.ProcessingSettings.RemoveNullableAttributes = v;
				Persist(cfg);
			}
		);

		AddToggleCard(
			Localization.PublicizeAssemblies,
			"Change assembly member visibility to public during processing.",
			cfg.ProcessingSettings.PublicizeAssemblies,
			!locked,
			v =>
			{
				cfg.ProcessingSettings.PublicizeAssemblies = v;
				Persist(cfg);
			}
		);

		AddToggleCard(
			Localization.EnableStaticMeshSeparation + (GameFileLoader.Premium ? "" : $" ({Localization.PremiumFeatureNotice})"),
			"Separate combined static meshes back into individual meshes.",
			GameFileLoader.Premium && cfg.ProcessingSettings.EnableStaticMeshSeparation,
			!locked && GameFileLoader.Premium,
			v =>
			{
				cfg.ProcessingSettings.EnableStaticMeshSeparation = v;
				Persist(cfg);
			}
		);

		AddTextCard(
			Localization.DefaultVersion,
			"Fallback Unity version used when serialized files lack a version string.",
			cfg.ImportSettings.DefaultVersion.ToString(),
			!locked,
			s =>
			{
				cfg.ImportSettings.DefaultVersion = TryParseUnityVersion(s);
				Persist(cfg);
			}
		);

		AddDropDownCard(
			Localization.BundledAssetsExportTitle,
			SettingsCatalog.BundledAssetsExportModes(),
			cfg.ProcessingSettings.BundledAssetsExportMode,
			!locked,
			v =>
			{
				cfg.ProcessingSettings.BundledAssetsExportMode = v;
				Persist(cfg);
			}
		);

		AddDropDownCard(
			Localization.ScriptContentLevelTitle,
			SettingsCatalog.ScriptContentLevels(),
			cfg.ImportSettings.ScriptContentLevel,
			!locked,
			v =>
			{
				cfg.ImportSettings.ScriptContentLevel = v;
				Persist(cfg);
			}
		);

		// EXPERIMENTAL SECTION
		AddSectionHeader(Localization.Experimental.ToUpperInvariant(), topMargin: 16);
		AddToggleCard(
			Localization.EnablePrefabOutlining,
			"Attempt to outline nested prefabs during scene/asset processing.",
			cfg.ProcessingSettings.EnablePrefabOutlining,
			!locked,
			v =>
			{
				cfg.ProcessingSettings.EnablePrefabOutlining = v;
				Persist(cfg);
			}
		);

		AddToggleCard(
			Localization.EnableAssetDeduplication + (GameFileLoader.Premium ? "" : $" ({Localization.PremiumFeatureNotice})"),
			"Deduplicate identical assets across bundles.",
			GameFileLoader.Premium && cfg.ProcessingSettings.EnableAssetDeduplication,
			!locked && GameFileLoader.Premium,
			v =>
			{
				cfg.ProcessingSettings.EnableAssetDeduplication = v;
				Persist(cfg);
			}
		);

		AddTextCard(
			Localization.TargetVersionForVersionChanging,
			"Target Unity version for experimental version conversion.",
			cfg.ImportSettings.TargetVersion.ToString(),
			!locked,
			s =>
			{
				cfg.ImportSettings.TargetVersion = TryParseUnityVersion(s);
				Persist(cfg);
			}
		);

		// EXPORT SECTION
		AddSectionHeader(Localization.MenuExport.ToUpperInvariant(), topMargin: 16);
		AddDropDownCard(
			Localization.AudioExportTitle,
			SettingsCatalog.AudioExportFormats(),
			cfg.ExportSettings.AudioExportFormat,
			!locked,
			v =>
			{
				cfg.ExportSettings.AudioExportFormat = v;
				Persist(cfg);
			}
		);

		AddDropDownCard(
			Localization.ImageExportTitle,
			SettingsCatalog.ImageExportFormats(),
			cfg.ExportSettings.ImageExportFormat,
			!locked,
			v =>
			{
				cfg.ExportSettings.ImageExportFormat = v;
				Persist(cfg);
			}
		);

		AddDropDownCard(
			Localization.LightmapTextureExportTitle,
			SettingsCatalog.LightmapTextureExportFormats(),
			cfg.ExportSettings.LightmapTextureExportFormat,
			!locked,
			v =>
			{
				cfg.ExportSettings.LightmapTextureExportFormat = v;
				Persist(cfg);
			}
		);

		AddDropDownCard(
			Localization.SpriteExportTitle,
			SettingsCatalog.SpriteExportModes(),
			cfg.ExportSettings.SpriteExportMode,
			!locked,
			v =>
			{
				cfg.ExportSettings.SpriteExportMode = v;
				Persist(cfg);
			}
		);

		AddDropDownCard(
			Localization.ShaderAssetExportTitle,
			SettingsCatalog.ShaderExportModes(),
			cfg.ExportSettings.ShaderExportMode,
			!locked,
			v =>
			{
				cfg.ExportSettings.ShaderExportMode = v;
				Persist(cfg);
			}
		);

		AddDropDownCard(
			Localization.TextAssetExportTitle,
			SettingsCatalog.TextExportModes(),
			cfg.ExportSettings.TextExportMode,
			!locked,
			v =>
			{
				cfg.ExportSettings.TextExportMode = v;
				Persist(cfg);
			}
		);

		AddDropDownCard(
			Localization.ScriptExportTitle,
			SettingsCatalog.ScriptExportModes(),
			cfg.ExportSettings.ScriptExportMode,
			!locked,
			v =>
			{
				cfg.ExportSettings.ScriptExportMode = v;
				Persist(cfg);
			}
		);

		AddDropDownCard(
			Localization.ScriptLanguageVersionTitle,
			SettingsCatalog.ScriptLanguageVersions(),
			cfg.ExportSettings.ScriptLanguageVersion,
			!locked,
			v =>
			{
				cfg.ExportSettings.ScriptLanguageVersion = v;
				Persist(cfg);
			}
		);

		AddToggleCard(
			Localization.ScriptsUseFullyQualifiedTypeNames,
			"Emit fully qualified namespace prefixes in decompiled C# scripts.",
			cfg.ExportSettings.ScriptTypesFullyQualified,
			!locked,
			v =>
			{
				cfg.ExportSettings.ScriptTypesFullyQualified = v;
				Persist(cfg);
			}
		);

		AddToggleCard(
			Localization.PreferOriginalTextureExtension,
			"Keep original texture file extension when exporting compatible formats.",
			cfg.ExportSettings.PreferOriginalTextureExtension,
			!locked,
			v =>
			{
				cfg.ExportSettings.PreferOriginalTextureExtension = v;
				Persist(cfg);
			}
		);

		AddToggleCard(
			Localization.ExportUnreadableAssets,
			"Export assets even when their serialized data could not be fully read.",
			cfg.ExportSettings.ExportUnreadableAssets,
			!locked,
			v =>
			{
				cfg.ExportSettings.ExportUnreadableAssets = v;
				Persist(cfg);
			}
		);

		AddToggleCard(
			Localization.SaveSettingsToDisk,
			"Persist configuration changes to AssetRipper's default settings file.",
			cfg.ExportSettings.SaveSettingsToDisk,
			!locked,
			v =>
			{
				cfg.ExportSettings.SaveSettingsToDisk = v;
				Persist(cfg);
			}
		);

		// INTERFACE & LICENSES SECTION
		AddSectionHeader(Localization.InterfaceAndLicenses.ToUpperInvariant(), topMargin: 16);
		AddDropDownCard(
			Localization.AssetPathDisplayTitle,
			SettingsCatalog.AssetPathDisplayModes(),
			cfg.ExportSettings.AssetPathDisplayMode,
			true,
			value =>
			{
				cfg.ExportSettings.AssetPathDisplayMode = value;
				Persist(cfg);
			}
		);
		AddLanguageCard(cfg);
		AddLicensesCard();
	}

	private void AddSectionHeader(string title, double topMargin = 0)
	{
		_sectionsContainer.Children.Add(
			new TextBlock
			{
				Text = title,
				Margin = new Thickness(4, topMargin, 4, 0),
				FontFamily = Palette.UiFont,
				FontSize = 11,
				FontWeight = FontWeight.SemiBold,
				Foreground = Palette.Secondary,
			}
		);
	}

	private void AddToggleCard(string title, string description, bool initialValue, bool enabled, Action<bool> onChanged)
	{
		TextBlock stateLabel = new()
		{
			Text = initialValue ? "On" : "Off",
			FontFamily = Palette.UiFont,
			FontSize = 12,
			Foreground = Palette.Secondary,
			VerticalAlignment = VerticalAlignment.Center,
		};

		VnlensToggleSwitch toggle = new(initialValue) { IsSwitchEnabled = enabled, VerticalAlignment = VerticalAlignment.Center };
		toggle.Toggled += val =>
		{
			stateLabel.Text = val ? "On" : "Off";
			onChanged(val);
		};

		Grid row = new() { ColumnDefinitions = new ColumnDefinitions("*,16,Auto,12,Auto"), VerticalAlignment = VerticalAlignment.Center };

		StackPanel textStack = new() { Spacing = 3 };
		textStack.Children.Add(
			new TextBlock
			{
				Text = title,
				FontFamily = Palette.UiFont,
				FontSize = 13,
				Foreground = enabled ? Palette.Text : Palette.Disabled,
				TextWrapping = TextWrapping.Wrap,
			}
		);
		textStack.Children.Add(
			new TextBlock
			{
				Text = description,
				FontFamily = Palette.UiFont,
				FontSize = 12,
				Foreground = Palette.Secondary,
				TextWrapping = TextWrapping.Wrap,
			}
		);
		Grid.SetColumn(textStack, 0);
		Grid.SetColumn(stateLabel, 2);
		Grid.SetColumn(toggle, 4);

		row.Children.Add(textStack);
		row.Children.Add(stateLabel);
		row.Children.Add(toggle);

		_sectionsContainer.Children.Add(WrapCard(row));
	}

	private void AddDropDownCard<T>(string title, IReadOnlyList<SettingChoice<T>> choices, T currentValue, bool enabled, Action<T> onChanged)
		where T : struct, Enum
	{
		TextBlock descBlock = new()
		{
			FontFamily = Palette.UiFont,
			FontSize = 12,
			Foreground = Palette.Secondary,
			TextWrapping = TextWrapping.Wrap,
		};

		ComboBox combo = new()
		{
			ItemsSource = choices,
			MinWidth = 180,
			IsEnabled = enabled,
			FontFamily = Palette.UiFont,
			FontSize = 12,
			VerticalAlignment = VerticalAlignment.Center,
		};

		int selectedIndex = 0;
		for (int i = 0; i < choices.Count; i++)
		{
			if (EqualityComparer<T>.Default.Equals(choices[i].Value, currentValue))
			{
				selectedIndex = i;
				break;
			}
		}
		combo.SelectedIndex = selectedIndex;
		descBlock.Text = choices[selectedIndex].Description ?? "";
		descBlock.IsVisible = !string.IsNullOrEmpty(descBlock.Text);

		combo.SelectionChanged += (_, _) =>
		{
			if (combo.SelectedItem is SettingChoice<T> choice)
			{
				descBlock.Text = choice.Description ?? "";
				descBlock.IsVisible = !string.IsNullOrEmpty(descBlock.Text);
				onChanged(choice.Value);
			}
		};

		Grid row = new() { ColumnDefinitions = new ColumnDefinitions("*,16,Auto"), VerticalAlignment = VerticalAlignment.Center };

		StackPanel textStack = new() { Spacing = 3 };
		textStack.Children.Add(
			new TextBlock
			{
				Text = title,
				FontFamily = Palette.UiFont,
				FontSize = 13,
				Foreground = enabled ? Palette.Text : Palette.Disabled,
			}
		);
		textStack.Children.Add(descBlock);

		Grid.SetColumn(textStack, 0);
		Grid.SetColumn(combo, 2);
		row.Children.Add(textStack);
		row.Children.Add(combo);

		_sectionsContainer.Children.Add(WrapCard(row));
	}

	private void AddTextCard(string title, string description, string currentValue, bool enabled, Action<string> onChanged)
	{
		TextBox box = new()
		{
			Text = currentValue,
			Width = 180,
			IsEnabled = enabled,
			FontFamily = Palette.MonoFont,
			FontSize = 12,
			VerticalAlignment = VerticalAlignment.Center,
		};
		box.LostFocus += (_, _) => onChanged(box.Text ?? "");

		Grid row = new() { ColumnDefinitions = new ColumnDefinitions("*,16,Auto"), VerticalAlignment = VerticalAlignment.Center };

		StackPanel textStack = new() { Spacing = 3 };
		textStack.Children.Add(
			new TextBlock
			{
				Text = title,
				FontFamily = Palette.UiFont,
				FontSize = 13,
				Foreground = enabled ? Palette.Text : Palette.Disabled,
			}
		);
		textStack.Children.Add(
			new TextBlock
			{
				Text = description,
				FontFamily = Palette.UiFont,
				FontSize = 12,
				Foreground = Palette.Secondary,
				TextWrapping = TextWrapping.Wrap,
			}
		);

		Grid.SetColumn(textStack, 0);
		Grid.SetColumn(box, 2);
		row.Children.Add(textStack);
		row.Children.Add(box);

		_sectionsContainer.Children.Add(WrapCard(row));
	}

	private void AddLanguageCard(FullConfiguration cfg)
	{
		(string Code, string Name)[] langs = LanguageCodes
			.LanguageNameDictionary.OrderBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
			.Select(kv => (kv.Key, kv.Value))
			.ToArray();

		ComboBox combo = new()
		{
			ItemsSource = langs.Select(l => l.Name).ToArray(),
			MinWidth = 180,
			FontFamily = Palette.UiFont,
			FontSize = 12,
			VerticalAlignment = VerticalAlignment.Center,
		};

		int currentIdx = Array.FindIndex(langs, l => string.Equals(l.Code, Localization.CurrentLanguageCode, StringComparison.OrdinalIgnoreCase));
		combo.SelectedIndex = Math.Max(0, currentIdx);
		combo.SelectionChanged += (_, _) =>
		{
			int idx = combo.SelectedIndex;
			if (idx >= 0 && idx < langs.Length)
			{
				Localization.LoadLanguage(langs[idx].Code);
				cfg.LanguageCode = Localization.CurrentLanguageCode;
				cfg.MaybeSaveToDefaultPath();
				LanguageChanged?.Invoke();
				Rebuild();
			}
		};

		Grid row = new() { ColumnDefinitions = new ColumnDefinitions("*,16,Auto"), VerticalAlignment = VerticalAlignment.Center };
		StackPanel textStack = new() { Spacing = 3 };
		textStack.Children.Add(
			new TextBlock
			{
				Text = Localization.MenuLanguage,
				FontFamily = Palette.UiFont,
				FontSize = 13,
				Foreground = Palette.Text,
			}
		);
		textStack.Children.Add(
			new TextBlock
			{
				Text = "Display language for AssetRipper labels and settings.",
				FontFamily = Palette.UiFont,
				FontSize = 12,
				Foreground = Palette.Secondary,
			}
		);
		Grid.SetColumn(textStack, 0);
		Grid.SetColumn(combo, 2);
		row.Children.Add(textStack);
		row.Children.Add(combo);

		_sectionsContainer.Children.Add(WrapCard(row));
	}

	private void AddGameCard(FullConfiguration cfg, bool enabled)
	{
		string[] games = ["Standard Unity", .. GameFileLoader.Plugins.Select(p => p.Id)];
		ComboBox picker = new()
		{
			ItemsSource = games,
			SelectedIndex = Math.Max(0, Array.IndexOf(games, cfg.ImportSettings.Game)),
			MinWidth = 220,
			FontSize = 12,
			IsEnabled = enabled,
		};
		picker.SelectionChanged += (_, _) =>
		{
			cfg.ImportSettings.Game = picker.SelectedIndex <= 0 ? "" : games[picker.SelectedIndex];
			Persist(cfg);
			Rebuild();
		};
		Grid row = new() { ColumnDefinitions = new ColumnDefinitions("*,16,Auto") };
		StackPanel labels = new() { Spacing = 3 };
		labels.Children.Add(
			new TextBlock
			{
				Text = "Game",
				FontSize = 13,
				Foreground = Palette.Text,
			}
		);
		labels.Children.Add(
			new TextBlock
			{
				Text = "Choose a game before opening its folder or archives.",
				FontSize = 12,
				Foreground = Palette.Secondary,
			}
		);
		Grid.SetColumn(picker, 2);
		row.Children.Add(labels);
		row.Children.Add(picker);
		_sectionsContainer.Children.Add(WrapCard(row));
	}

	private void AddLicensesCard()
	{
		string[] names = Licenses.Names.ToArray();
		if (names.Length == 0)
		{
			return;
		}

		TextBlock licenseBody = new()
		{
			Text = Licenses.Load(names[0]),
			FontFamily = Palette.MonoFont,
			FontSize = 11,
			Foreground = Palette.Secondary,
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0, 8, 0, 0),
		};

		ComboBox picker = new()
		{
			ItemsSource = names,
			SelectedIndex = 0,
			MinWidth = 180,
			FontFamily = Palette.UiFont,
			FontSize = 12,
		};
		picker.SelectionChanged += (_, _) =>
		{
			if (picker.SelectedItem is string name)
			{
				licenseBody.Text = Licenses.Load(name);
			}
		};

		Grid header = new() { ColumnDefinitions = new ColumnDefinitions("*,16,Auto"), VerticalAlignment = VerticalAlignment.Center };
		header.Children.Add(
			new TextBlock
			{
				Text = Localization.Licenses,
				FontFamily = Palette.UiFont,
				FontSize = 13,
				Foreground = Palette.Text,
				VerticalAlignment = VerticalAlignment.Center,
			}
		);
		Grid.SetColumn(picker, 2);
		header.Children.Add(picker);

		ScrollViewer licenseScroll = new() { MaxHeight = 200, Content = licenseBody };

		StackPanel stack = new() { Spacing = 8 };
		stack.Children.Add(header);
		stack.Children.Add(licenseScroll);

		_sectionsContainer.Children.Add(WrapCard(stack));
	}

	private static Border WrapCard(Control content)
	{
		return new Border
		{
			Padding = new Thickness(16, 12),
			CornerRadius = new CornerRadius(10),
			Background = Palette.Panel,
			BorderBrush = Palette.Border,
			BorderThickness = new Thickness(1),
			Child = content,
		};
	}

	private static void Persist(FullConfiguration cfg)
	{
		if (cfg.SaveSettingsToDisk)
		{
			cfg.SaveToDefaultPath();
		}
		else
		{
			SerializedSettings.DeleteDefaultPath();
		}
	}

	private static UnityVersion TryParseUnityVersion(string? version)
	{
		if (string.IsNullOrWhiteSpace(version))
		{
			return default;
		}
		try
		{
			return UnityVersion.Parse(version.Trim());
		}
		catch
		{
			return default;
		}
	}
}
