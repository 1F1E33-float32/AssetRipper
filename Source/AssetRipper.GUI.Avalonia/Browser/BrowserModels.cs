using AssetRipper.Assets;
using AssetRipper.Assets.Bundles;
using AssetRipper.Assets.Collections;
using AssetRipper.GUI.Avalonia.Services;
using AssetRipper.GUI.Avalonia.Theme;
using AssetRipper.GUI.Localizations;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.ResourceFiles;
using AssetRipper.Processing.Textures;
using AssetRipper.SourceGenerated.Classes.ClassID_1;
using AssetRipper.SourceGenerated.Classes.ClassID_114;
using AssetRipper.SourceGenerated.Classes.ClassID_115;
using AssetRipper.SourceGenerated.Classes.ClassID_128;
using AssetRipper.SourceGenerated.Classes.ClassID_156;
using AssetRipper.SourceGenerated.Classes.ClassID_189;
using AssetRipper.SourceGenerated.Classes.ClassID_21;
using AssetRipper.SourceGenerated.Classes.ClassID_213;
using AssetRipper.SourceGenerated.Classes.ClassID_28;
using AssetRipper.SourceGenerated.Classes.ClassID_329;
using AssetRipper.SourceGenerated.Classes.ClassID_4;
using AssetRipper.SourceGenerated.Classes.ClassID_43;
using AssetRipper.SourceGenerated.Classes.ClassID_48;
using AssetRipper.SourceGenerated.Classes.ClassID_49;
using AssetRipper.SourceGenerated.Classes.ClassID_83;
using AssetRipper.Import.GamePlugins;

namespace AssetRipper.GUI.Avalonia.Browser;

internal enum BrowserItemKind
{
	MountedArchive,
	MountedEntry,
	Bundle,
	Collection,
	Scene,
	Asset,
	ResourceFile,
	FailedFile,
	ScenesGroup,
	SearchGroup,
}

internal sealed class BrowserItem
{
	public required string Name { get; init; }
	public required BrowserItemKind Kind { get; init; }
	public required IconKind Icon { get; init; }
	public required bool IsContainer { get; init; }
	public required object Target { get; init; }
	public string Meta { get; init; } = "";
	public string? ClassName { get; init; }
	public string? SecondaryPath { get; init; }
	public IReadOnlyList<string> CompanionNames { get; init; } = [];

	public string DisplayPathSegment => Name;

	public static IconKind ClassifyAssetIcon(IUnityObjectBase asset)
	{
		if (asset is IImageTexture or SpriteInformationObject or ISprite or ITerrainData)
		{
			return IconKind.Image;
		}
		if (asset is IMesh)
		{
			return IconKind.Model;
		}
		if (asset is IAudioClip)
		{
			return IconKind.Audio;
		}
		if (asset is IVideoClip)
		{
			return IconKind.Video;
		}
		if (asset is IMonoScript or IMonoBehaviour)
		{
			return IconKind.Script;
		}
		if (asset is IShader)
		{
			return IconKind.Code;
		}
		if (asset is ITextAsset)
		{
			return IconKind.Text;
		}
		if (asset is IFont)
		{
			return IconKind.Font;
		}
		if (asset is IMaterial)
		{
			return IconKind.Config;
		}
		if (asset is IGameObject or ITransform)
		{
			return IconKind.Application;
		}
		return IconKind.File;
	}

	public static string GetKindLabel(BrowserItem item) => item.Kind switch
	{
		BrowserItemKind.MountedArchive => Localization.Bundle,
		BrowserItemKind.MountedEntry => Localization.Collection,
		BrowserItemKind.Bundle => Localization.Bundle,
		BrowserItemKind.Collection => Localization.Collection,
		BrowserItemKind.Scene => Localization.Scene,
		BrowserItemKind.ResourceFile => Localization.Resources,
		BrowserItemKind.FailedFile => Localization.FailedFiles,
		BrowserItemKind.Asset => item.ClassName ?? "Asset",
		_ => "Folder",
	};
}

internal static class BrowserFolderBuilder
{
	public static List<BrowserItem> BuildRoot(GameBundle gameBundle)
	{
		List<BrowserItem> items = BuildBundleItems(gameBundle);
		if (GameFileLoader.MountedGame is { } mounted)
		{
			foreach (GameArchive archive in mounted.Archives)
				items.Add(new BrowserItem { Name = archive.Name, Kind = BrowserItemKind.MountedArchive, Icon = IconKind.Archive, IsContainer = true, Target = archive, Meta = "Mounted" });
		}

		// Add a virtual Scenes folder if the loaded project contains scenes
		List<SceneDefinition> scenes = gameBundle.Scenes.ToList();
		if (scenes.Count > 0)
		{
			scenes.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
			items.Insert(0, new BrowserItem
			{
				Name = "Scenes",
				Kind = BrowserItemKind.ScenesGroup,
				Icon = IconKind.Folder,
				IsContainer = true,
				Target = scenes,
				Meta = scenes.Count.ToString(),
			});
		}

		return items;
	}

	public static List<BrowserItem> BuildChildren(BrowserItem container)
	{
		return container.Kind switch
		{
			BrowserItemKind.MountedArchive when container.Target is GameArchive archive => BuildArchiveItems(archive),
			BrowserItemKind.MountedEntry when container.Target is AssetCollection collection => BuildCollectionItems(collection),
			BrowserItemKind.Bundle when container.Target is Bundle bundle => BuildBundleItems(bundle),
			BrowserItemKind.Collection when container.Target is AssetCollection col => BuildCollectionItems(col),
			BrowserItemKind.Scene when container.Target is SceneDefinition scene => BuildSceneItems(scene),
			BrowserItemKind.ScenesGroup when container.Target is List<SceneDefinition> scenes => BuildScenesGroupItems(scenes),
			BrowserItemKind.SearchGroup when container.Target is List<SearchHit> hits => BuildSearchHitItems(hits),
			_ => [],
		};
	}

	private static List<BrowserItem> BuildArchiveItems(GameArchive archive)
	{
		// Plugin relationships come from the archive index; listing must not decode any entries.
		Dictionary<int, GameArchiveEntry[]> companions = archive.Entries
			.Where(entry => entry.IsSerialized)
			.ToDictionary(entry => entry.Index, entry => archive.GetResources(entry)
				.Where(resource => !resource.IsSerialized).DistinctBy(resource => resource.Index).ToArray());
		HashSet<int> boundResources = companions.Values.SelectMany(resources => resources)
			.Select(resource => resource.Index).ToHashSet();
		List<BrowserItem> items = new();
		foreach (GameArchiveEntry entry in archive.Entries)
		{
			if (boundResources.Contains(entry.Index))
			{
				continue;
			}
			GameArchiveEntry[] resources = companions.GetValueOrDefault(entry.Index) ?? [];
			items.Add(new BrowserItem
			{
				Name = entry.Name,
				Kind = BrowserItemKind.MountedEntry,
				Icon = entry.IsSerialized ? IconKind.Library : IconKind.Data,
				IsContainer = entry.IsSerialized,
				Target = (archive, entry),
				Meta = resources.Length > 0 ? $"{resources.Length + 1} files" : AssetContentService.FormatBytes(entry.Length),
				CompanionNames = resources.Select(resource => resource.Name).ToArray(),
			});
		}
		return items;
	}

	public static List<BrowserItem> BuildBundleItems(Bundle bundle)
	{
		List<BrowserItem> items = new();
		Dictionary<AssetCollection, ResourceFile[]> companions = ResourceBindings.FindCompanions(bundle);
		HashSet<ResourceFile> boundResources = companions.Values.SelectMany(resources => resources).ToHashSet();

		if (bundle.Bundles.Count > 0)
		{
			foreach (Bundle child in bundle.Bundles.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase))
			{
				int count = child.Bundles.Count + child.Collections.Count + child.Resources.Count + child.FailedFiles.Count;
				items.Add(new BrowserItem
				{
					Name = string.IsNullOrEmpty(child.Name) ? "(Unnamed Bundle)" : child.Name,
					Kind = BrowserItemKind.Bundle,
					Icon = IconKind.Archive,
					IsContainer = true,
					Target = child,
					Meta = count.ToString(),
				});
			}
		}

		if (bundle.Collections.Count > 0)
		{
			foreach (AssetCollection col in bundle.Collections.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
			{
				if (col.Count == 0 && col is not SerializedAssetCollection)
				{
					continue;
				}
				items.Add(new BrowserItem
				{
					Name = string.IsNullOrEmpty(col.Name) ? "(Unnamed Collection)" : col.Name,
					Kind = BrowserItemKind.Collection,
					Icon = IconKind.Library,
					IsContainer = true,
					Target = col,
					Meta = companions.TryGetValue(col, out ResourceFile[]? resources)
						? $"{resources.Length + 1} files"
						: col.Count.ToString(),
					CompanionNames = resources?.Select(resource => resource.Name).ToArray() ?? [],
				});
			}
		}

		if (bundle.Resources.Count > 0)
		{
			foreach (ResourceFile res in bundle.Resources.OrderBy(r => r.NameFixed, StringComparer.OrdinalIgnoreCase))
			{
				if (boundResources.Contains(res))
				{
					continue;
				}
				long size = res.Stream.CanSeek ? res.Stream.Length : 0;
				items.Add(new BrowserItem
				{
					Name = res.NameFixed,
					Kind = BrowserItemKind.ResourceFile,
					Icon = IconKind.Data,
					IsContainer = false,
					Target = res,
					Meta = size > 0 ? AssetContentService.FormatBytes(size) : "",
				});
			}
		}

		if (bundle.FailedFiles.Count > 0)
		{
			foreach (FailedFile failed in bundle.FailedFiles.OrderBy(f => f.NameFixed, StringComparer.OrdinalIgnoreCase))
			{
				items.Add(new BrowserItem
				{
					Name = failed.NameFixed,
					Kind = BrowserItemKind.FailedFile,
					Icon = IconKind.Alert,
					IsContainer = false,
					Target = failed,
					Meta = "Failed",
				});
			}
		}

		return items;
	}

	public static List<BrowserItem> BuildCollectionItems(AssetCollection collection)
	{
		List<BrowserItem> items = new(collection.Count);
		foreach (IUnityObjectBase asset in collection)
		{
			items.Add(CreateAssetItem(asset));
		}
		items.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
		return items;
	}

	public static List<BrowserItem> BuildSceneItems(SceneDefinition scene)
	{
		List<BrowserItem> items = new();
		foreach (AssetCollection col in scene.Collections)
		{
			items.Add(new BrowserItem
			{
				Name = col.Name,
				Kind = BrowserItemKind.Collection,
				Icon = IconKind.Library,
				IsContainer = true,
				Target = col,
				Meta = col.Count.ToString(),
			});
		}
		return items;
	}

	public static List<BrowserItem> BuildScenesGroupItems(List<SceneDefinition> scenes)
	{
		List<BrowserItem> items = new(scenes.Count);
		foreach (SceneDefinition scene in scenes)
		{
			items.Add(new BrowserItem
			{
				Name = scene.Name,
				Kind = BrowserItemKind.Scene,
				Icon = IconKind.Document,
				IsContainer = true,
				Target = scene,
				Meta = scene.Collections.Count.ToString(),
				SecondaryPath = scene.Path,
			});
		}
		return items;
	}

	public static List<BrowserItem> BuildSearchHitItems(List<SearchHit> hits)
	{
		List<BrowserItem> items = new(hits.Count);
		foreach (SearchHit hit in hits)
		{
			items.Add(CreateAssetItem(hit.Asset, hit.Collection.Name));
		}
		return items;
	}

	public static BrowserItem CreateAssetItem(IUnityObjectBase asset, string? collectionLabel = null)
	{
		string name = asset.GetBestName();
		if (string.IsNullOrWhiteSpace(name))
		{
			name = $"{asset.ClassName} #{asset.PathID}";
		}
		return new BrowserItem
		{
			Name = name,
			Kind = BrowserItemKind.Asset,
			Icon = BrowserItem.ClassifyAssetIcon(asset),
			IsContainer = false,
			Target = asset,
			ClassName = asset.ClassName,
			Meta = collectionLabel is not null ? $"{asset.ClassName}" : asset.ClassName,
			SecondaryPath = collectionLabel,
		};
	}
}
