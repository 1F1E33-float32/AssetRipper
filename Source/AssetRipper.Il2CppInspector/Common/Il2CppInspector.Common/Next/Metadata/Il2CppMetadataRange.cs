using AssetRipper.Il2CppInspector.VersionedSerialization.Attributes;

namespace AssetRipper.Il2CppInspector.Next.Metadata;

[VersionedStruct]
public partial record struct Il2CppMetadataRange
{
	public int Start { get; private set; }
	public int Length { get; private set; }
}
