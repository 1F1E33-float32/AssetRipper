namespace AssetRipper.Il2CppInspector.Next.Metadata;

using AssetRipper.Il2CppInspector.VersionedSerialization.Attributes;
using StringIndex = int;

[VersionedStruct]
public partial record struct Il2CppWindowsRuntimeTypeNamePair
{
	public Int32 NameIndex { get; private set; }
	public TypeIndex TypeIndex { get; private set; }
}
