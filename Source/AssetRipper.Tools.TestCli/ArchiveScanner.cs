using System.Text.Json;
using AssetRipper.Assets;
using AssetRipper.Assets.Bundles;
using AssetRipper.Assets.Generics;
using AssetRipper.Assets.IO;
using AssetRipper.Assets.Metadata;
using AssetRipper.Import.AssetCreation;
using AssetRipper.Import.GamePlugins;
using AssetRipper.Import.Logging;
using AssetRipper.Import.Structure.Assembly.Managers;
using AssetRipper.Import.Structure.Assembly.Serializable;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.ResourceFiles;
using AssetRipper.IO.Files.SerializedFiles;
using AssetRipper.IO.Files.SerializedFiles.Parser;
using AssetRipper.Primitives;
using AssetRipper.SourceGenerated.Classes.ClassID_114;

namespace AssetRipper.Tools.TestCli;

internal sealed class ArchiveScanner(
	IAssemblyManager assemblies,
	object assemblyGate,
	ScanLog.ErrorScope scanLog,
	TextWriter results,
	ScanSummary summary,
	ScanProgress progress,
	bool skipScripts,
	CancellationToken cancellation
)
{
	public void Scan(GameArchive archive, UnityVersion version)
	{
		summary.Archives++;
		try
		{
			// Collections are retained for one archive so local MonoScript references resolve,
			// then released before the next archive. Missing external scripts remain explicit failures.
			using GameBundle bundle = new();
			List<PendingAsset> pending = [];
			GameAssetFactory factory = new(assemblies);
			foreach (GameArchiveEntry entry in archive.Entries)
			{
				cancellation.ThrowIfCancellationRequested();
				summary.Entries++;
				Logger.Info(LogCategory.Import, $"Opening {archive.Name}/{entry.Name}");
				try
				{
					FileBase file = archive.ReadEntry(entry);
					if (file is ResourceFile resource)
					{
						// Merely opening the stream does not decompress every archive block.
						using (resource)
						{
							resource.Stream.Position = 0;
							byte[] buffer = new byte[64 * 1024];
							long total = 0;
							int read;
							while ((read = resource.Stream.Read(buffer)) != 0)
							{
								cancellation.ThrowIfCancellationRequested();
								total += read;
							}
							if (total != entry.Length)
								throw new InvalidDataException($"Read {total} resource bytes; expected {entry.Length}.");
						}
						summary.Resources++;
						WriteResult("resource", "passed", archive.FilePath, entry.Name);
					}
					else
					{
						using (file)
						{
							if (file is not SerializedFile serialized)
								throw new NotSupportedException($"Unsupported file type {file.GetType().Name}.");
							long before = scanLog.Errors;
							bundle.AddCollectionFromSerializedFile(
								serialized,
								new RecordingFactory(factory, scanLog, pending, entry.Name, cancellation),
								version
							);
							if (scanLog.Errors != before)
							{
								summary.FailedEntries++;
								WriteResult(
									"entry",
									"failed",
									archive.FilePath,
									entry.Name,
									message: "Asset parsing reported errors; see asset results and log."
								);
							}
							else
							{
								WriteResult("entry", "loaded", archive.FilePath, entry.Name);
							}
						}
					}
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					summary.FailedEntries++;
					Logger.Error(LogCategory.Import, $"Failed {archive.FilePath}/{entry.Name}", ex);
					WriteResult("entry", "failed", archive.FilePath, entry.Name, message: ex.Message);
				}
				finally
				{
					if (!cancellation.IsCancellationRequested)
					{
						progress.FinishEntry();
					}
				}
			}
			bundle.InitializeAllDependencyLists();
			foreach (PendingAsset item in pending)
			{
				cancellation.ThrowIfCancellationRequested();
				CheckAsset(archive.FilePath, item);
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			summary.FailedArchives++;
			Logger.Error(LogCategory.Import, $"Failed archive {archive.FilePath}", ex);
			WriteResult("archive", "failed", archive.FilePath, "", message: ex.Message);
		}
		finally
		{
			// The session owns the archives, but releasing each cache now keeps a long scan bounded.
			archive.Dispose();
		}
	}

	private void CheckAsset(string archivePath, PendingAsset item)
	{
		IUnityObjectBase asset = item.Asset;
		summary.Assets++;
		string status;
		string? message = null;
		try
		{
			if (asset is UnknownObject)
			{
				status = "unknown";
				summary.UnknownAssets++;
			}
			else if (item.HadErrors || asset is UnreadableObject)
			{
				status = "failed";
				summary.FailedAssets++;
			}
			else if (asset is IMonoBehaviour behaviour && behaviour.Structure is UnloadedStructure unloaded)
			{
				if (skipScripts)
				{
					status = "skipped-script";
					summary.SkippedScripts++;
				}
				else if (behaviour.ScriptP is null)
				{
					status = "unresolved-script";
					message = "MonoScript reference is unavailable in this archive; its structure was not validated.";
					summary.UnresolvedScripts++;
				}
				else
				{
					long before = scanLog.Errors;
					bool parsed;
					// The shared AsmResolver model and assembly manager lazily populate type caches.
					// Native object reads and archive decompression can proceed independently.
					lock (assemblyGate)
					{
						cancellation.ThrowIfCancellationRequested();
						parsed = unloaded.LoadStructure() is not null;
					}
					if (!parsed || scanLog.Errors != before)
					{
						status = "failed";
						message = "MonoBehaviour structure could not be parsed.";
						summary.FailedAssets++;
					}
					else
					{
						status = "passed";
						summary.PassedAssets++;
					}
				}
			}
			else if (asset is IMonoBehaviour { Structure: null })
			{
				status = "failed";
				message = "MonoBehaviour has no parsed structure.";
				summary.FailedAssets++;
			}
			else
			{
				status = "passed";
				summary.PassedAssets++;
			}
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			status = "failed";
			message = ex.Message;
			summary.FailedAssets++;
			Logger.Error(LogCategory.Import, $"Failed {archivePath}/{item.Entry}, PathID {asset.PathID}, {asset.ClassName}", ex);
		}
		if (status is "unknown" or "unresolved-script")
		{
			Logger.Warning(LogCategory.Import, $"{status}: {archivePath}/{item.Entry}, PathID {asset.PathID}, {asset.ClassName}. {message}");
		}
		WriteResult("asset", status, archivePath, item.Entry, asset.PathID, asset.ClassID, asset.ClassName, message);
		progress.FinishAsset(status is "failed" or "unknown" or "unresolved-script");
	}

	private void WriteResult(
		string scope,
		string status,
		string archive,
		string entry,
		long? pathId = null,
		int? classId = null,
		string? className = null,
		string? message = null
	)
	{
		results.WriteLine(
			JsonSerializer.Serialize(
				new
				{
					scope,
					status,
					archive,
					entry,
					pathId,
					classId,
					className,
					message,
				}
			)
		);
	}

	private sealed record PendingAsset(IUnityObjectBase Asset, string Entry, bool HadErrors);

	private sealed class RecordingFactory(
		GameAssetFactory factory,
		ScanLog.ErrorScope log,
		List<PendingAsset> pending,
		string entry,
		CancellationToken cancellation
	) : AssetFactoryBase
	{
		public override IUnityObjectBase ReadAsset(AssetInfo assetInfo, ObjectInfo objectInfo, IReadOnlyList<SerializedTypeReference> referenceTypes)
		{
			cancellation.ThrowIfCancellationRequested();
			long before = log.Errors;
			IUnityObjectBase asset;
			try
			{
				asset =
					factory.ReadAsset(assetInfo, objectInfo, referenceTypes)
					?? throw new InvalidDataException("The asset factory returned no object.");
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				Logger.Error(LogCategory.Import, $"Failed {entry}, PathID {assetInfo.PathID}, ClassID {assetInfo.ClassID}", ex);
				asset = new UnreadableObject(assetInfo, objectInfo.ObjectData);
			}
			pending.Add(new(asset, entry, log.Errors != before));
			return asset;
		}

		public override IUnityObjectBase? ReadAsset(
			AssetInfo assetInfo,
			ReadOnlyArraySegment<byte> assetData,
			SerializedType? assetType,
			IReadOnlyList<SerializedTypeReference> referenceTypes
		) => ReadAsset(assetInfo, new ObjectInfo { ObjectData = assetData.ToArray(), Type = assetType }, referenceTypes);
	}
}
