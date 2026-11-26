using AssetRipper.Assets;
using AssetRipper.Assets.Collections;
using AssetRipper.Assets.Generics;
using AssetRipper.GUI.Avalonia.Theme;
using AssetRipper.GUI.Localizations;
using AssetRipper.Primitives;
using AssetRipper.SourceGenerated.Classes.ClassID_142;
using AssetRipper.SourceGenerated.Classes.ClassID_147;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.SourceGenerated.Subclasses.AssetInfo;
using AssetRipper.SourceGenerated.Subclasses.PPtr_Object;

namespace AssetRipper.GUI.Avalonia.Browser;

internal sealed class AssetPathTree
{
	private readonly Folder _root = new();
	private readonly List<BrowserItem> _unmapped = [];

	public void Add(string? path, BrowserItem item)
	{
		string[] segments = SplitSegments(path);
		if (segments.Length == 0)
		{
			_unmapped.Add(item);
			return;
		}
		Folder folder = _root;
		foreach (string segment in segments.AsSpan(0, segments.Length - 1))
		{
			if (!folder.Folders.TryGetValue(segment, out Folder? child))
			{
				child = new();
				folder.Folders.Add(segment, child);
			}
			folder = child;
		}
		folder.Assets.Add(item);
	}

	public List<BrowserItem> BuildItems()
	{
		List<BrowserItem> items = _root.BuildItems();
		if (_unmapped.Count > 0)
		{
			_unmapped.Sort(CompareItems);
			items.Add(CreateFolder(Localization.AssetsWithoutInternalPath, _unmapped));
		}
		return items;
	}

	public static List<BrowserItem> Build(IReadOnlyList<AssetCollection> collections, CancellationToken cancellationToken = default)
	{
		Dictionary<IUnityObjectBase, HashSet<string>> paths = GetPaths(collections, cancellationToken);
		AssetPathTree tree = new();
		foreach (IUnityObjectBase asset in collections.SelectMany(static collection => collection))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!paths.TryGetValue(asset, out HashSet<string>? assetPaths))
			{
				tree.Add(null, BrowserFolderBuilder.CreateAssetItem(asset, asset.Collection.Name));
				continue;
			}
			foreach (string path in assetPaths)
			{
				tree.Add(path, BrowserFolderBuilder.CreateAssetItem(asset, path, path[(path.LastIndexOf('/') + 1)..]));
			}
		}
		return tree.BuildItems();
	}

	private static Dictionary<IUnityObjectBase, HashSet<string>> GetPaths(
		IReadOnlyList<AssetCollection> collections,
		CancellationToken cancellationToken
	)
	{
		Dictionary<IUnityObjectBase, HashSet<string>> paths = [];
		HashSet<AssetCollection> includedCollections = collections.ToHashSet();
		// Read the recorded container paths directly: export processing may have regrouped OriginalPath.
		foreach (IUnityObjectBase owner in collections.SelectMany(static collection => collection))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (owner is IAssetBundle bundle)
			{
				foreach (AccessPairBase<Utf8String, IAssetInfo> pair in bundle.Container)
				{
					AddPath(pair.Value.Asset.TryGetAsset(bundle.Collection), pair.Key.String);
				}
			}
			else if (owner is IResourceManager manager)
			{
				foreach (AccessPairBase<Utf8String, IPPtr_Object> pair in manager.Container)
				{
					AddPath(pair.Value.TryGetAsset(manager.Collection), pair.Key.String);
				}
			}
		}
		foreach (IUnityObjectBase asset in collections.SelectMany(static collection => collection))
		{
			if (!paths.ContainsKey(asset))
			{
				AddPath(asset, asset.OriginalPath);
			}
		}
		return paths;

		void AddPath(IUnityObjectBase? asset, string? path)
		{
			if (asset is null || !includedCollections.Contains(asset.Collection) || string.IsNullOrWhiteSpace(path))
			{
				return;
			}
			string normalized = NormalizePath(path);
			if (normalized.Length == 0)
			{
				return;
			}
			if (!paths.TryGetValue(asset, out HashSet<string>? assetPaths))
			{
				assetPaths = new(StringComparer.Ordinal);
				paths.Add(asset, assetPaths);
			}
			assetPaths.Add(normalized);
		}
	}

	public static bool TryFindAsset(List<BrowserItem> items, IUnityObjectBase asset, List<BrowserItem> path)
	{
		foreach (BrowserItem item in items)
		{
			path.Add(item);
			if (
				ReferenceEquals(item.Target, asset)
				|| item.Target is MountedAsset mounted
					&& mounted.PathId == asset.PathID
					&& ReferenceEquals(GameFileLoader.GetLoadedEntry(mounted.Source.Archive, mounted.Source.Entry), asset.Collection)
				|| item is { Kind: BrowserItemKind.PathFolder, Target: List<BrowserItem> children } && TryFindAsset(children, asset, path)
			)
			{
				return true;
			}
			path.RemoveAt(path.Count - 1);
		}
		return false;
	}

	internal static string NormalizePath(string? path) => string.Join('/', SplitSegments(path));

	private static string[] SplitSegments(string? path) => (path ?? "").Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

	private static BrowserItem CreateFolder(string name, List<BrowserItem> children) =>
		new()
		{
			Name = name,
			Kind = BrowserItemKind.PathFolder,
			Icon = IconKind.Folder,
			IsContainer = true,
			Target = children,
			Meta = children.Count.ToString(),
		};

	private static int CompareItems(BrowserItem left, BrowserItem right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);

	private sealed class Folder
	{
		public Dictionary<string, Folder> Folders { get; } = new(StringComparer.Ordinal);
		public List<BrowserItem> Assets { get; } = [];

		public List<BrowserItem> BuildItems()
		{
			List<BrowserItem> children = Folders.Select(static pair => CreateFolder(pair.Key, pair.Value.BuildItems())).ToList();
			children.Sort(CompareItems);
			Assets.Sort(CompareItems);
			children.AddRange(Assets);
			return children;
		}
	}
}
