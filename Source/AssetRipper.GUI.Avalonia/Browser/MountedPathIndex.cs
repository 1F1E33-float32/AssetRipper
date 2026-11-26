using AssetRipper.Assets;
using AssetRipper.Assets.Bundles;
using AssetRipper.Assets.Collections;
using AssetRipper.Assets.Generics;
using AssetRipper.GUI.Avalonia.Theme;
using AssetRipper.Import.AssetCreation;
using AssetRipper.Import.GamePlugins;
using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure.Assembly.Managers;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.SerializedFiles;
using AssetRipper.IO.Files.SerializedFiles.Parser;
using AssetRipper.Primitives;
using AssetRipper.SourceGenerated;
using AssetRipper.SourceGenerated.Classes.ClassID_142;
using AssetRipper.SourceGenerated.Classes.ClassID_147;
using AssetRipper.SourceGenerated.Subclasses.AssetInfo;
using AssetRipper.SourceGenerated.Subclasses.PPtr_Object;

namespace AssetRipper.GUI.Avalonia.Browser;

internal sealed record PathIndexProgress(int Completed, int Total, int Paths, int Failed, string CurrentArchive);

internal sealed record IndexedEntry(GameArchive Archive, GameArchiveEntry Entry)
{
	public HashSet<long> MappedPathIds { get; } = [];
	public int ObjectCount { get; set; }
}

internal sealed record MountedAsset(IndexedEntry Source, long PathId);

internal static class MountedPathIndex
{
	public static List<BrowserItem> Build(
		GameImportSession session,
		Action<PathIndexProgress> report,
		CancellationToken cancellationToken,
		int? maxDegreeOfParallelism = null
	)
	{
		AssetPathTree tree = new();
		Dictionary<string, List<IndexedEntry>> entriesByName = new(StringComparer.OrdinalIgnoreCase);
		List<IndexedEntry> entries = [];
		List<(IndexedEntry Source, string Dependency, long PathId, string Path)> externalPaths = [];
		int concurrency = maxDegreeOfParallelism ?? Math.Max(1, Environment.ProcessorCount / 2);
		ArgumentOutOfRangeException.ThrowIfLessThan(concurrency, 1);
		ParallelOptions options = new() { MaxDegreeOfParallelism = concurrency, CancellationToken = cancellationToken };
		object gate = new();
		int completed = 0;
		int paths = 0;
		int failed = 0;
		Parallel.ForEach(
			session.Archives,
			options,
			archive =>
			{
				cancellationToken.ThrowIfCancellationRequested();
				lock (gate)
				{
					report(new(completed, session.Archives.Count, paths, failed, archive.Name));
				}
				try
				{
					using BaseManager manager = new(static _ => { });
					GameAssetFactory factory = new(manager);
					foreach (GameArchiveEntry entry in archive.Entries.Where(static entry => entry.IsSerialized))
					{
						cancellationToken.ThrowIfCancellationRequested();
						IndexedEntry source = new(archive, entry);
						lock (gate)
						{
							entries.Add(source);
							string name = SpecialFileNames.FixFileIdentifier(entry.Name);
							if (!entriesByName.TryGetValue(name, out List<IndexedEntry>? namedEntries))
							{
								namedEntries = [];
								entriesByName.Add(name, namedEntries);
							}
							namedEntries.Add(source);
						}
						try
						{
							using FileBase file = archive.ReadEntry(entry, static classID => classID is 142 or 147);
							if (file is not SerializedFile serialized)
							{
								throw new InvalidDataException($"Expected a serialized file: {entry.Name}");
							}
							source.ObjectCount = serialized.Objects.Length;
							Dictionary<long, int> classes = [];
							HashSet<(int FileId, long PathId, string Path)> seenPaths = [];
							SerializedFileBuilder builder = new()
							{
								Generation = serialized.Generation,
								Version = serialized.Version,
								Platform = serialized.Platform,
								EndianType = serialized.EndianType,
								HasTypeTree = serialized.HasTypeTree,
							};
							builder.Types.AddRange(serialized.Types);
							builder.RefTypes.AddRange(serialized.RefTypes);
							foreach (ObjectInfo item in serialized.Objects)
							{
								classes.Add(item.FileID, item.TypeID);
								if (item.TypeID is 142 or 147)
								{
									builder.Objects.Add(item);
								}
							}
							using GameBundle temporaryBundle = new();
							SerializedAssetCollection collection = temporaryBundle.AddCollectionFromSerializedFile(
								builder.Build(),
								factory,
								session.Version
							);
							foreach (IUnityObjectBase asset in collection)
							{
								if (asset is IAssetBundle bundle)
								{
									foreach (AccessPairBase<Utf8String, IAssetInfo> pair in bundle.Container)
									{
										AddPath(pair.Key.String, pair.Value.Asset);
									}
								}
								else if (asset is IResourceManager resourceManager)
								{
									foreach (AccessPairBase<Utf8String, IPPtr_Object> pair in resourceManager.Container)
									{
										AddPath(pair.Key.String, pair.Value);
									}
								}
								else
								{
									throw new InvalidDataException($"Could not read resource paths from {entry.Name}, PathID {asset.PathID}.");
								}
							}
							void AddPath(string path, IPPtr_Object pointer)
							{
								string normalized = AssetPathTree.NormalizePath(path);
								if (normalized.Length == 0 || pointer.PathID == 0 || !seenPaths.Add((pointer.FileID, pointer.PathID, normalized)))
								{
									return;
								}
								if (pointer.FileID == 0)
								{
									if (!classes.TryGetValue(pointer.PathID, out int classID))
									{
										throw new InvalidDataException($"Resource path points to missing PathID {pointer.PathID}.");
									}
									AddAsset(source, pointer.PathID, classID, normalized);
								}
								else
								{
									if (pointer.FileID < 1 || pointer.FileID > serialized.Dependencies.Length)
									{
										throw new InvalidDataException("Resource path dependency exceeds the serialized file dependency table.");
									}
									lock (gate)
									{
										externalPaths.Add(
											(source, serialized.Dependencies[pointer.FileID - 1].GetFilePath(), pointer.PathID, normalized)
										);
									}
								}
							}
						}
						catch (Exception error) when (error is not OperationCanceledException)
						{
							lock (gate)
							{
								failed++;
							}
							Logger.Error(LogCategory.Import, $"Failed to index resource paths: {archive.FilePath}/{entry.Name}", error);
						}
					}
				}
				catch (Exception error) when (error is not OperationCanceledException)
				{
					lock (gate)
					{
						failed++;
					}
					Logger.Error(LogCategory.Import, $"Failed to index archive: {archive.FilePath}", error);
				}
				finally
				{
					archive.ReleaseCachedData();
				}
				lock (gate)
				{
					completed++;
					report(new(completed, session.Archives.Count, paths, failed, archive.Name));
				}
			}
		);
		foreach ((IndexedEntry source, string dependency, long pathId, string path) in externalPaths)
		{
			cancellationToken.ThrowIfCancellationRequested();
			IndexedEntry? target = null;
			if (entriesByName.TryGetValue(dependency, out List<IndexedEntry>? candidates))
			{
				List<IndexedEntry> local = candidates.Where(candidate => ReferenceEquals(candidate.Archive, source.Archive)).ToList();
				target =
					local.Count == 1 ? local[0]
					: candidates.Count == 1 ? candidates[0]
					: null;
			}
			if (target is null)
			{
				failed++;
				Logger.Warning(LogCategory.Import, $"Could not resolve resource path {path}: {dependency}, PathID {pathId}.");
				continue;
			}
			AddAsset(target, pathId, 0, path);
		}
		foreach (IndexedEntry source in entries)
		{
			if (source.ObjectCount > source.MappedPathIds.Count)
			{
				tree.Add(
					null,
					new BrowserItem
					{
						Name = $"{source.Archive.Name} / {source.Entry.Name}",
						Kind = BrowserItemKind.MountedEntry,
						Icon = IconKind.Library,
						IsContainer = true,
						Target = source,
						Meta = (source.ObjectCount - source.MappedPathIds.Count).ToString(),
					}
				);
			}
		}
		cancellationToken.ThrowIfCancellationRequested();
		List<BrowserItem> result = tree.BuildItems();
		report(new(completed, session.Archives.Count, paths, failed, ""));
		return result;

		void AddAsset(IndexedEntry source, long pathId, int classID, string path)
		{
			// Merge paths and report counters atomically; decoding stays outside this lock.
			lock (gate)
			{
				source.MappedPathIds.Add(pathId);
				tree.Add(
					path,
					new BrowserItem
					{
						Name = path[(path.LastIndexOf('/') + 1)..],
						Kind = BrowserItemKind.MountedAsset,
						Icon = BrowserItem.ClassifyAssetIcon(classID),
						IsContainer = false,
						Target = new MountedAsset(source, pathId),
						ClassName = classID == 0 ? null : ((ClassIDType)classID).ToString(),
						Meta = source.Archive.Name,
						SecondaryPath = path,
					}
				);
				paths++;
			}
		}
	}
}
