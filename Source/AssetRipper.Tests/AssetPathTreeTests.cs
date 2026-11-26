using AssetRipper.Assets.Collections;
using AssetRipper.Assets.Generics;
using AssetRipper.Export.Configuration;
using AssetRipper.GUI.Avalonia.Browser;
using AssetRipper.GUI.Localizations;
using AssetRipper.Import.Configuration;
using AssetRipper.Primitives;
using AssetRipper.Processing.Configuration;
using AssetRipper.SourceGenerated.Classes.ClassID_142;
using AssetRipper.SourceGenerated.Classes.ClassID_49;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.SourceGenerated.Subclasses.AssetInfo;

namespace AssetRipper.Tests;

public class AssetPathTreeTests
{
	[Test]
	public async Task UsesBundlePathsAndPreservesAliasesAndDuplicateNames()
	{
		ProcessedAssetCollection collection = AssetCreator.CreateCollection(new(2022, 3, 62));
		IAssetBundle bundle = collection.CreateAssetBundle();
		ITextAsset first = collection.CreateTextAsset();
		ITextAsset second = collection.CreateTextAsset();
		first.OriginalPath = "Assets/AssetBundles/regrouped/old.txt";
		AddPath("assets\\texts//same.txt", first);
		AddPath("assets/texts/same.txt", first);
		AddPath("assets/texts/same.txt", second);
		AddPath("assets/aliases/other.txt", first);

		List<BrowserItem> items = AssetPathTree.Build([collection]);
		List<BrowserItem> assets = BrowserFolderBuilder.BuildChildren(items.Single(static item => item.Name == "assets"));
		List<BrowserItem> texts = BrowserFolderBuilder.BuildChildren(assets.Single(static item => item.Name == "texts"));
		await Assert.That(texts.Select(static item => item.Target).SequenceEqual(new object[] { first, second })).IsTrue();
		await Assert.That(texts.All(static item => item.Name == "same.txt" && item.SecondaryPath == "assets/texts/same.txt")).IsTrue();
		List<BrowserItem> aliases = BrowserFolderBuilder.BuildChildren(assets.Single(static item => item.Name == "aliases"));
		await Assert.That(ReferenceEquals(aliases.Single().Target, first)).IsTrue();
		await Assert.That(first.OriginalPath).IsEqualTo("Assets/AssetBundles/regrouped/old.txt");

		void AddPath(string path, ITextAsset asset)
		{
			AccessPairBase<Utf8String, IAssetInfo> pair = bundle.Container.AddNew();
			pair.Key = path;
			pair.Value.Asset.SetAsset(collection, asset);
		}
	}

	[Test]
	public async Task KeepsUnmappedAssetsAndFindsSelectionsInNestedFolders()
	{
		ProcessedAssetCollection collection = AssetCreator.CreateCollection(new(2022, 3, 62));
		ITextAsset mapped = collection.CreateTextAsset();
		mapped.OriginalPath = "assets/texts/example.txt";
		ITextAsset unmapped = collection.CreateTextAsset();
		List<BrowserItem> items = AssetPathTree.Build([collection]);
		List<BrowserItem> path = [];
		await Assert.That(AssetPathTree.TryFindAsset(items, mapped, path)).IsTrue();
		await Assert.That(path.Select(static item => item.Name).SequenceEqual(["assets", "texts", "example.txt"])).IsTrue();
		path.Clear();
		await Assert.That(AssetPathTree.TryFindAsset(items, unmapped, path)).IsTrue();
		await Assert.That(path[0].Name).IsEqualTo(Localization.AssetsWithoutInternalPath);
		await Assert.That(ReferenceEquals(path[^1].Target, unmapped)).IsTrue();
	}

	[Test]
	public async Task SavesDisplayModeAndDefaultsOldSettingsToArchiveStructure()
	{
		string path = Path.GetTempFileName();
		try
		{
			SerializedSettings settings = new(
				new ImportSettings(),
				new ProcessingSettings(),
				new ExportSettings { AssetPathDisplayMode = AssetPathDisplayMode.InternalPaths }
			);
			settings.Save(path);
			await Assert.That(SerializedSettings.Load(path).Export.AssetPathDisplayMode).IsEqualTo(AssetPathDisplayMode.InternalPaths);
			File.WriteAllText(path, """{"Import":{},"Processing":{},"Export":{}}""");
			await Assert.That(SerializedSettings.Load(path).Export.AssetPathDisplayMode).IsEqualTo(AssetPathDisplayMode.ArchiveStructure);
		}
		finally
		{
			File.Delete(path);
		}
	}
}
