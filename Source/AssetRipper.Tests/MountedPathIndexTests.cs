using AssetRipper.Assets.Bundles;
using AssetRipper.Assets.Collections;
using AssetRipper.Assets.Generics;
using AssetRipper.Assets.IO.Writing;
using AssetRipper.GUI.Avalonia.Browser;
using AssetRipper.Import.GamePlugins;
using AssetRipper.Import.Structure.Assembly.Managers;
using AssetRipper.IO.Endian;
using AssetRipper.IO.Files.SerializedFiles;
using AssetRipper.IO.Files.SerializedFiles.Parser;
using AssetRipper.Primitives;
using AssetRipper.SourceGenerated.Classes.ClassID_142;
using AssetRipper.SourceGenerated.Extensions;
using AssetRipper.SourceGenerated.Subclasses.AssetInfo;

namespace AssetRipper.Tests;

public class MountedPathIndexTests
{
	[Test]
	public async Task MergesEveryArchiveIntoTheRootAndKeepsCollidingPathsDeferred()
	{
		MemoryArchive first = new("first.assets", CreateFile("assets/shared/item.txt"));
		MemoryArchive second = new("second.assets", CreateFile("assets/shared/item.txt"));
		using Session session = new([first, second]);
		List<PathIndexProgress> reports = [];
		List<BrowserItem> root = MountedPathIndex.Build(session, reports.Add, CancellationToken.None);
		List<BrowserItem> shared = BrowserFolderBuilder.BuildChildren(
			BrowserFolderBuilder.BuildChildren(root.Single(static item => item.Name == "assets")).Single()
		);
		await Assert.That(shared.Count).IsEqualTo(2);
		await Assert.That(shared.All(static item => item is { Kind: BrowserItemKind.MountedAsset, Target: MountedAsset, Name: "item.txt" })).IsTrue();
		await Assert.That(shared.Select(static item => ((MountedAsset)item.Target).Source.Archive).Distinct().Count()).IsEqualTo(2);
		await Assert.That(root.Any(static item => item.Kind == BrowserItemKind.MountedArchive)).IsFalse();
		await Assert.That(first.Releases + second.Releases).IsEqualTo(2);
		await Assert.That(reports[^1]).IsEqualTo(new PathIndexProgress(2, 2, 2, 0, ""));
	}

	[Test]
	public async Task ResolvesCrossArchivePathsAndContinuesAfterAnInvalidFile()
	{
		MemoryArchive failed = new("invalid.assets", [1, 2, 3]);
		MemoryArchive referring = new("first.assets", CreateFile("assets/alias.txt", "second.assets"));
		MemoryArchive target = new("second.assets", CreateFile("assets/original.txt"));
		using Session session = new([failed, referring, target]);
		List<PathIndexProgress> reports = [];
		List<BrowserItem> root = MountedPathIndex.Build(session, reports.Add, CancellationToken.None);
		List<BrowserItem> assets = BrowserFolderBuilder.BuildChildren(root.Single(static item => item.Name == "assets"));
		MountedAsset alias = (MountedAsset)assets.Single(static item => item.Name == "alias.txt").Target;
		await Assert.That(ReferenceEquals(alias.Source.Archive, target)).IsTrue();
		await Assert.That(alias.PathId).IsEqualTo(2);
		await Assert.That(reports[^1].Failed).IsEqualTo(1);
		await Assert.That(reports[^1].Completed).IsEqualTo(3);
	}

	[Test]
	public async Task CancellationStopsBeforeTheNextArchiveAndReleasesDecodedData()
	{
		MemoryArchive first = new("first.assets", CreateFile("assets/first.txt"));
		MemoryArchive second = new("second.assets", CreateFile("assets/second.txt"));
		using Session session = new([first, second]);
		using CancellationTokenSource cancellation = new();
		await Assert
			.That(() =>
				MountedPathIndex.Build(
					session,
					progress =>
					{
						if (progress.Completed == 1)
						{
							cancellation.Cancel();
						}
					},
					cancellation.Token,
					maxDegreeOfParallelism: 1
				)
			)
			.Throws<OperationCanceledException>();
		await Assert.That(first.Releases).IsEqualTo(1);
		await Assert.That(second.Opens).IsEqualTo(0);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ScansArchivesConcurrentlyWithinTheLimitAndReleasesAllWorkers(bool cancel)
	{
		using ManualResetEventSlim release = new();
		using CancellationTokenSource cancellation = new();
		TaskCompletionSource bothStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
		int active = 0;
		int maximum = 0;
		int started = 0;
		MemoryArchive[] archives = Enumerable
			.Range(0, 6)
			.Select(index => new MemoryArchive(
				$"{index}.assets",
				CreateFile($"assets/{index}.txt"),
				() =>
				{
					int running = Interlocked.Increment(ref active);
					Interlocked.Increment(ref started);
					int observed;
					do
					{
						observed = Volatile.Read(ref maximum);
					} while (running > observed && Interlocked.CompareExchange(ref maximum, running, observed) != observed);
					if (running == 2)
					{
						bothStarted.TrySetResult();
					}
					try
					{
						release.Wait();
					}
					finally
					{
						Interlocked.Decrement(ref active);
					}
				}
			))
			.ToArray();
		using Session session = new(archives);
		List<PathIndexProgress> reports = [];
		Task<List<BrowserItem>> scan = Task.Run(() => MountedPathIndex.Build(session, reports.Add, cancellation.Token, maxDegreeOfParallelism: 2));
		try
		{
			await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(15));
			await Assert.That(Volatile.Read(ref started)).IsEqualTo(2);
			if (cancel)
			{
				cancellation.Cancel();
			}
		}
		finally
		{
			release.Set();
			if (cancel)
			{
				await Assert.That(async () => await scan).Throws<OperationCanceledException>();
			}
			else
			{
				await scan;
			}
		}
		await Assert.That(maximum).IsEqualTo(2);
		await Assert.That(archives.Sum(static archive => archive.Opens)).IsEqualTo(cancel ? 2 : 6);
		await Assert.That(archives.All(static archive => archive.Opens == archive.Releases)).IsTrue();
		await Assert
			.That(reports.Zip(reports.Skip(1), static (a, b) => a.Completed <= b.Completed && a.Paths <= b.Paths).All(static value => value))
			.IsTrue();
		if (!cancel)
		{
			await Assert.That(reports[^1]).IsEqualTo(new PathIndexProgress(6, 6, 6, 0, ""));
		}
	}

	private static byte[] CreateFile(string path, string? dependency = null)
	{
		ProcessedAssetCollection collection = AssetCreator.CreateCollection(new(2022, 3, 62));
		IAssetBundle bundle = collection.CreateAssetBundle();
		AccessPairBase<Utf8String, IAssetInfo> pair = bundle.Container.AddNew();
		pair.Key = path;
		ProcessedAssetCollection targetCollection = dependency is null
			? collection
			: ((GameBundle)collection.Bundle).AddNewProcessedCollection(dependency, collection.Version);
		if (dependency is not null)
		{
			// Keep the target's local object ID identical to the serialized fixture.
			targetCollection.CreateTextAsset();
		}
		pair.Value.Asset.SetAsset(collection, targetCollection.CreateTextAsset());
		using MemoryStream payload = new();
		using (AssetWriter writer = new(payload, collection))
		{
			bundle.WriteRelease(writer);
		}
		SerializedFileBuilder builder = new()
		{
			Generation = FormatVersion.LargeFilesSupport,
			Version = collection.Version,
			EndianType = EndianType.LittleEndian,
		};
		SerializedType bundleType = new() { TypeID = 142, ScriptTypeIndex = -1 };
		SerializedType assetType = new() { TypeID = 49, ScriptTypeIndex = -1 };
		builder.Types.AddRange([bundleType, assetType]);
		builder.Objects.Add(
			new ObjectInfo(bundleType)
			{
				FileID = 1,
				SerializedTypeIndex = 0,
				ObjectData = payload.ToArray(),
			}
		);
		builder.Objects.Add(
			new ObjectInfo(assetType)
			{
				FileID = 2,
				SerializedTypeIndex = 1,
				ObjectData = [255],
			}
		);
		if (dependency is not null)
		{
			builder.Dependencies.Add(
				new FileIdentifier
				{
					AssetPath = Utf8String.Empty,
					PathName = dependency,
					PathNameOrigin = dependency,
				}
			);
		}
		using MemoryStream output = new();
		builder.Build().Write(output);
		return output.ToArray();
	}

	private sealed class MemoryArchive(string name, byte[] data, Action? onOpen = null) : GameArchive
	{
		public override string Name => name;
		public override string FilePath => name;
		public override IReadOnlyList<GameArchiveEntry> Entries { get; } = [new(0, name, data.Length, true)];
		public int Opens { get; private set; }
		public int Releases { get; private set; }

		public override Stream OpenEntry(GameArchiveEntry entry)
		{
			Opens++;
			onOpen?.Invoke();
			return new MemoryStream(data, false);
		}

		public override void ReleaseCachedData() => Releases++;

		public override void Dispose() { }
	}

	private sealed class Session(IReadOnlyList<GameArchive> archives) : GameImportSession
	{
		public override UnityVersion Version => new(2022, 3, 62);
		public override IReadOnlyList<GameArchive> Archives => archives;

		public override IAssemblyManager CreateAssemblyManager() => throw new InvalidOperationException("Indexing must not load script assemblies.");

		public override void Dispose() { }
	}
}
