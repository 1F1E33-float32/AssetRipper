using AssetRipper.Il2CppInspector.VersionedSerialization.Attributes;

namespace AssetRipper.Il2CppInspector.Next.Metadata;

[VersionedStruct]
public partial record struct Il2CppMetadataUsagePair
{
	public uint DestinationIndex { get; private set; }
	public uint EncodedSourceIndex { get; private set; }
}
