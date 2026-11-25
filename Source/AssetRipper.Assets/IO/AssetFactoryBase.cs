using AssetRipper.Assets.Generics;
using AssetRipper.Assets.Metadata;
using AssetRipper.IO.Files.SerializedFiles.Parser;

namespace AssetRipper.Assets.IO;

public abstract class AssetFactoryBase
{
	public virtual IUnityObjectBase? ReadAsset(AssetInfo assetInfo, ObjectInfo objectInfo, IReadOnlyList<SerializedTypeReference> referenceTypes) =>
		ReadAsset(assetInfo, objectInfo.ObjectData, objectInfo.Type, referenceTypes);

	public abstract IUnityObjectBase? ReadAsset(
		AssetInfo assetInfo,
		ReadOnlyArraySegment<byte> assetData,
		SerializedType? assetType,
		IReadOnlyList<SerializedTypeReference> referenceTypes
	);
}
