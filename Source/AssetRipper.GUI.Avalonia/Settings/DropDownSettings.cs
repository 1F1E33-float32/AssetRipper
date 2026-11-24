using AssetRipper.Export.Configuration;
using AssetRipper.GUI.Localizations;
using AssetRipper.Import.Configuration;
using AssetRipper.Processing.Configuration;

namespace AssetRipper.GUI.Avalonia.Settings;

internal readonly record struct SettingChoice<T>(T Value, string DisplayName, string? Description)
{
	public override string ToString() => DisplayName;
}

internal static class SettingsCatalog
{
	public static IReadOnlyList<SettingChoice<ScriptContentLevel>> ScriptContentLevels() =>
	[
		new(ScriptContentLevel.Level0, Localization.ScriptContentLevel0, Localization.ScriptContentLevel0Description),
		new(ScriptContentLevel.Level1, Localization.ScriptContentLevel1, Localization.ScriptContentLevel1Description),
		new(ScriptContentLevel.Level2, Localization.ScriptContentLevel2, Localization.ScriptContentLevel2Description),
		new(ScriptContentLevel.Level3, Localization.ScriptContentLevel3, GameFileLoader.Premium
			? Localization.ScriptContentLevel3Description
			: Localization.NotAvailableInTheFreeEdition),
	];

	public static IReadOnlyList<SettingChoice<BundledAssetsExportMode>> BundledAssetsExportModes() =>
	[
		new(BundledAssetsExportMode.GroupByAssetType, Localization.BundledAssetsExportGroupByAssetType, Localization.BundledAssetsExportGroupByAssetTypeDescription),
		new(BundledAssetsExportMode.GroupByBundleName, Localization.BundledAssetsExportGroupByBundleName, Localization.BundledAssetsExportGroupByBundleNameDescription),
		new(BundledAssetsExportMode.DirectExport, Localization.BundledAssetsExportDirectExport, Localization.BundledAssetsExportDirectExportDescription),
	];

	public static IReadOnlyList<SettingChoice<AudioExportFormat>> AudioExportFormats() =>
	[
		new(AudioExportFormat.Yaml, Localization.AudioFormatYaml, Localization.AudioFormatYamlDescription),
		new(AudioExportFormat.Native, Localization.AudioFormatNative, Localization.AudioFormatNativeDescription),
		new(AudioExportFormat.PreferWav, Localization.AudioFormatForceWav, Localization.AudioFormatForceWavDescription),
		new(AudioExportFormat.Default, Localization.AudioFormatDefault, Localization.AudioFormatDefaultDescription),
	];

	public static IReadOnlyList<SettingChoice<ImageExportFormat>> ImageExportFormats() =>
		Enum.GetValues<ImageExportFormat>()
			.Select(v => new SettingChoice<ImageExportFormat>(v, v.ToString(), Localization.ImageFormatDescription))
			.ToArray();

	public static IReadOnlyList<SettingChoice<LightmapTextureExportFormat>> LightmapTextureExportFormats() =>
		Enum.GetValues<LightmapTextureExportFormat>()
			.Select(v => new SettingChoice<LightmapTextureExportFormat>(v, v.ToString(), Localization.LightmapTextureFormatDescription))
			.ToArray();

	public static IReadOnlyList<SettingChoice<SpriteExportMode>> SpriteExportModes() =>
	[
		new(SpriteExportMode.Yaml, Localization.SpriteFormatYaml, Localization.SpriteFormatYamlDescription),
		new(SpriteExportMode.Native, Localization.SpriteFormatNative, Localization.SpriteFormatNativeDescription),
		new(SpriteExportMode.Texture2D, Localization.SpriteFormatTexture, Localization.SpriteFormatTextureDescription),
	];

	public static IReadOnlyList<SettingChoice<ShaderExportMode>> ShaderExportModes() =>
	[
		new(ShaderExportMode.Dummy, Localization.ShaderAssetFormatDummy, Localization.ShaderAssetFormatDummyDescription),
		new(ShaderExportMode.Yaml, Localization.ShaderAssetFormatYaml, Localization.ShaderAssetFormatYamlDescription),
		new(ShaderExportMode.Decompile, Localization.ShaderAssetFormatDecompile, GameFileLoader.Premium
			? Localization.ShaderAssetFormatDecompileDescription
			: Localization.NotAvailableInTheFreeEdition),
	];

	public static IReadOnlyList<SettingChoice<TextExportMode>> TextExportModes() =>
	[
		new(TextExportMode.Bytes, Localization.TextAssetFormatBinary, Localization.TextAssetFormatBinaryDescription),
		new(TextExportMode.Txt, Localization.TextAssetFormatText, Localization.TextAssetFormatTextDescription),
		new(TextExportMode.Parse, Localization.TextAssetFormatParse, Localization.TextAssetFormatParseDescription),
	];

	public static IReadOnlyList<SettingChoice<ScriptExportMode>> ScriptExportModes() =>
	[
		new(ScriptExportMode.Decompiled, Localization.ScriptExportFormatDecompiled, Localization.ScriptExportFormatDecompiledDescription),
		new(ScriptExportMode.Hybrid, Localization.ScriptExportFormatHybrid, Localization.ScriptExportFormatHybridDescription),
		new(ScriptExportMode.DllExportWithRenaming, Localization.ScriptExportFormatDllWithRenaming, Localization.NotImplementedYet),
		new(ScriptExportMode.DllExportWithoutRenaming, Localization.ScriptExportFormatDllWithoutRenaming, Localization.ScriptExportFormatDllWithoutRenamingDescription),
	];

	public static IReadOnlyList<SettingChoice<ScriptLanguageVersion>> ScriptLanguageVersions() =>
		Enum.GetValues<ScriptLanguageVersion>()
			.Select(v => new SettingChoice<ScriptLanguageVersion>(v, GetLanguageVersionName(v), Localization.CSharpLanguageVersionConfigDescription))
			.ToArray();

	private static string GetLanguageVersionName(ScriptLanguageVersion value) => value switch
	{
		ScriptLanguageVersion.AutoExperimental => Localization.CSharpLangageVersionConfigAutoExperimental,
		ScriptLanguageVersion.AutoSafe => Localization.CSharpLangageVersionConfigAutoSafe,
		ScriptLanguageVersion.CSharp1 => Localization.CSharpLangageVersionConfig1,
		ScriptLanguageVersion.CSharp2 => Localization.CSharpLangageVersionConfig2,
		ScriptLanguageVersion.CSharp3 => Localization.CSharpLangageVersionConfig3,
		ScriptLanguageVersion.CSharp4 => Localization.CSharpLangageVersionConfig4,
		ScriptLanguageVersion.CSharp5 => Localization.CSharpLangageVersionConfig5,
		ScriptLanguageVersion.CSharp6 => Localization.CSharpLangageVersionConfig6,
		ScriptLanguageVersion.CSharp7 => Localization.CSharpLangageVersionConfig7,
		ScriptLanguageVersion.CSharp7_1 => Localization.CSharpLangageVersionConfig71,
		ScriptLanguageVersion.CSharp7_2 => Localization.CSharpLangageVersionConfig72,
		ScriptLanguageVersion.CSharp7_3 => Localization.CSharpLangageVersionConfig73,
		ScriptLanguageVersion.CSharp8_0 => Localization.CSharpLangageVersionConfig80,
		ScriptLanguageVersion.CSharp9_0 => Localization.CSharpLangageVersionConfig90,
		ScriptLanguageVersion.CSharp10_0 => Localization.CSharpLangageVersionConfig100,
		ScriptLanguageVersion.CSharp11_0 => Localization.CSharpLangageVersionConfig110,
		ScriptLanguageVersion.CSharp12_0 => Localization.CSharpLangageVersionConfig120,
		ScriptLanguageVersion.CSharp13_0 => Localization.CSharpLangageVersionConfig130,
		ScriptLanguageVersion.CSharp14_0 => Localization.CSharpLangageVersionConfig140,
		ScriptLanguageVersion.CSharp15_0 => Localization.CSharpLangageVersionConfig150,
		ScriptLanguageVersion.Latest => Localization.CSharpLangageVersionConfigLatest,
		_ => value.ToString(),
	};
}
