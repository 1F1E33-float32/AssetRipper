using AssetRipper.Il2CppInspector.VersionedSerialization.Attributes;

namespace AssetRipper.Il2CppInspector.Next.Metadata;

[VersionedStruct]
public partial record struct Il2CppSectionMetadata
{
	public int Offset { get; private set; }
	public int SectionSize { get; private set; }
	public int Count { get; private set; }
}
