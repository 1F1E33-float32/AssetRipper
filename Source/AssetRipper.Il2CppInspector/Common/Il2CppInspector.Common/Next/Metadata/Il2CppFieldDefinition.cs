namespace AssetRipper.Il2CppInspector.Next.Metadata;

using AssetRipper.Il2CppInspector.VersionedSerialization.Attributes;
using StringIndex = int;

[VersionedStruct]
public partial record struct Il2CppFieldDefinition
{
	public Int32 NameIndex { get; internal set; }
	public TypeIndex TypeIndex { get; internal set; }

	[VersionCondition(LessThanOrEqual = "24.0")]
	public int CustomAttributeIndex { get; internal set; }

	[VersionCondition(GreaterThanOrEqual = "19.0")]
	public uint Token { get; internal set; }
	public readonly bool IsValid => NameIndex != 0;
}
