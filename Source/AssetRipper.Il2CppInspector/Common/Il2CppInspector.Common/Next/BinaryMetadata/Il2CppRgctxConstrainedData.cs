using AssetRipper.Il2CppInspector.Next.Metadata;
using AssetRipper.Il2CppInspector.VersionedSerialization.Attributes;

namespace AssetRipper.Il2CppInspector.Next.BinaryMetadata;

using TypeIndex = int;

[VersionedStruct]
public partial record struct Il2CppRgctxConstrainedData
{
	public Int32 TypeIndex;
	public Il2CppMetadataUsage EncodedMethodIndex;
}
