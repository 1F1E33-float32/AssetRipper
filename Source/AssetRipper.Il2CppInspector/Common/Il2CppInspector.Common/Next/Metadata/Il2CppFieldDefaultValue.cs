using AssetRipper.Il2CppInspector.VersionedSerialization.Attributes;

namespace AssetRipper.Il2CppInspector.Next.Metadata;

[VersionedStruct]
public partial record struct Il2CppFieldDefaultValue
{
	public FieldIndex FieldIndex { get; internal set; }
	public TypeIndex TypeIndex { get; internal set; }
	public DefaultValueDataIndex DataIndex { get; internal set; }
}
