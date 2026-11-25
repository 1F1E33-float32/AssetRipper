using AssetRipper.Assets;
using AssetRipper.Assets.Bundles;
using AssetRipper.Assets.Collections;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.ResourceFiles;
using AssetRipper.SourceGenerated.Classes.ClassID_117;
using AssetRipper.SourceGenerated.Classes.ClassID_187;
using AssetRipper.SourceGenerated.Classes.ClassID_188;
using AssetRipper.SourceGenerated.Classes.ClassID_28;
using AssetRipper.SourceGenerated.Classes.ClassID_329;
using AssetRipper.SourceGenerated.Classes.ClassID_43;
using AssetRipper.SourceGenerated.Classes.ClassID_83;

namespace AssetRipper.GUI.Avalonia.Browser;

/// <summary>Groups loaded resource companions without reading their payloads or changing export paths.</summary>
internal static class ResourceBindings
{
	public static Dictionary<AssetCollection, ResourceFile[]> FindCompanions(Bundle bundle)
	{
		Dictionary<AssetCollection, ResourceFile[]> result = new();
		if (bundle.Resources.Count == 0)
		{
			return result;
		}
		// Names are scoped to this bundle. Ambiguous duplicate names stay separate in the browser.
		Dictionary<string, ResourceFile[]> resources = bundle
			.Resources.GroupBy(resource => resource.NameFixed, StringComparer.Ordinal)
			.ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
		foreach (SerializedAssetCollection collection in bundle.Collections.OfType<SerializedAssetCollection>())
		{
			HashSet<string> names = new(StringComparer.Ordinal)
			{
				// Unity's conventional sidecars retain the complete serialized file name.
				SpecialFileNames.FixFileIdentifier(collection.Name + ResourceFile.StreamingFileExtension),
				SpecialFileNames.FixFileIdentifier(collection.Name + ResourceFile.ResourceFileExtension),
			};
			foreach (IUnityObjectBase asset in collection)
			{
				string? path = asset switch
				{
					ITexture2D texture => texture.StreamData_C28?.Path.String,
					ITexture3D texture => texture.StreamData?.Path.String,
					ITexture2DArray texture => texture.StreamData?.Path.String,
					ICubemapArray texture => texture.StreamData?.Path.String,
					IMesh mesh => mesh.StreamData?.Path.String,
					IAudioClip audio => audio.Resource?.Source.String,
					IVideoClip video => video.ExternalResources.Source.String,
					_ => null,
				};
				if (!string.IsNullOrEmpty(path))
				{
					names.Add(SpecialFileNames.FixFileIdentifier(path));
				}
			}
			ResourceFile[] companions = names
				.Select(name => resources.GetValueOrDefault(name))
				.Where(matches => matches is { Length: 1 })
				.Select(matches => matches![0])
				.OrderBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
				.ToArray();
			if (companions.Length > 0)
			{
				result.Add(collection, companions);
			}
		}
		return result;
	}
}
