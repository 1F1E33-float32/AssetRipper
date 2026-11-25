using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AssetRipper.Assets;
using AssetRipper.Assets.Bundles;
using AssetRipper.Assets.Collections;
using AssetRipper.Export.PrimaryContent;
using AssetRipper.Import.AssetCreation;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.ResourceFiles;
using AssetRipper.IO.Files.SerializedFiles.Parser.TypeTrees;

namespace AssetRipper.Export;

/// <summary>Preserves complete player structures alongside assets converted for standard Unity.</summary>
public static class SourceAssetExporter
{
	public static void Export(GameBundle bundle, string destination, FileSystem fileSystem)
	{
		string root = fileSystem.Path.Join(destination, "SourceAssets");
		Dictionary<TypeTree, string> layouts = new(ReferenceEqualityComparer.Instance);
		Dictionary<ResourceFile, string> resources = new(ReferenceEqualityComparer.Instance);
		int collectionIndex = 0;
		foreach (AssetCollection collection in bundle.FetchAssetCollections())
		{
			string directory = fileSystem.Path.Join(root, $"{collectionIndex++:D6}_{FileSystem.FixInvalidFileNameCharacters(collection.Name)}");
			StreamWriter? manifest = null;
			try
			{
				foreach (IUnityObjectBase asset in collection)
				{
					if ((asset.SourceAsset ?? asset) is not TypeTreeObject { OriginalData: { } bytes, OriginalTypeTree: { } tree } source)
					{
						continue;
					}
					if (manifest is null)
					{
						fileSystem.Directory.Create(directory);
						manifest = new StreamWriter(fileSystem.File.Create(fileSystem.Path.Join(directory, "manifest.jsonl")));
						manifest.WriteLine(
							JsonSerializer.Serialize(
								new SourceCollectionManifest(
									collection.Name,
									collection.FilePath,
									collection.OriginalVersion.ToString(),
									collection.EndianType.ToString(),
									collection is SerializedAssetCollection serialized
										? serialized.OriginalDependencies.ToArray()
										: collection.Dependencies.Skip(1).Select(static c => c?.Name).ToArray()
								),
								SourceManifestJsonContext.Default.SourceCollectionManifest
							)
						);
					}
					if (!layouts.TryGetValue(tree, out string? layoutPath))
					{
						fileSystem.Directory.Create(fileSystem.Path.Join(root, "TypeTrees"));
						layoutPath = $"TypeTrees/{layouts.Count:D4}_{source.ClassID}.txt";
						using StreamWriter layout = new(fileSystem.File.Create(fileSystem.Path.Join(root, layoutPath)));
						layout.Write(tree.Dump);
						layouts.Add(tree, layoutPath);
					}
					string name = source.PathID.ToString(CultureInfo.InvariantCulture);
					string? resourcePath = null;
					if (source.OriginalResourcePath is { } originalResource && collection.Bundle.ResolveResource(originalResource) is { } resource)
					{
						if (!resources.TryGetValue(resource, out resourcePath))
						{
							fileSystem.Directory.Create(fileSystem.Path.Join(root, "Resources"));
							resourcePath = $"Resources/{resources.Count:D6}_{FileSystem.FixInvalidFileNameCharacters(resource.Name)}";
							using Stream stream = fileSystem.File.Create(fileSystem.Path.Join(root, resourcePath));
							long originalPosition = resource.Stream.Position;
							try
							{
								resource.Stream.Position = 0;
								resource.Stream.CopyTo(stream);
							}
							finally
							{
								resource.Stream.Position = originalPosition;
							}
							resources.Add(resource, resourcePath);
						}
					}
					using (Stream stream = fileSystem.File.Create(fileSystem.Path.Join(directory, name + ".bin")))
					{
						stream.Write(bytes);
					}
					using (StreamWriter json = new(fileSystem.File.Create(fileSystem.Path.Join(directory, name + ".json"))))
					{
						source.WalkStandard(new DefaultJsonWalker(json));
					}
					manifest.WriteLine(
						JsonSerializer.Serialize(
							new SourceObjectManifest(
								source.PathID,
								source.ClassID,
								source.ClassName,
								source.SerializedVersion,
								layoutPath,
								name + ".bin",
								name + ".json",
								source.OriginalResourcePath,
								resourcePath
							),
							SourceManifestJsonContext.Default.SourceObjectManifest
						)
					);
				}
			}
			finally
			{
				manifest?.Dispose();
			}
		}
	}
}

internal sealed record SourceCollectionManifest(string Collection, string Archive, string Version, string Endian, string?[] Dependencies);

internal sealed record SourceObjectManifest(
	long PathId,
	int ClassId,
	string ClassName,
	int SerializedVersion,
	string TypeTree,
	string Data,
	string Fields,
	string? OriginalResource,
	string? Resource
);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SourceCollectionManifest))]
[JsonSerializable(typeof(SourceObjectManifest))]
internal partial class SourceManifestJsonContext : JsonSerializerContext;
