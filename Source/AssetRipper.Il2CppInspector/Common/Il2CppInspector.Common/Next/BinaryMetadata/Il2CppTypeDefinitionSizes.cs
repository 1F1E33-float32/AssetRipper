using AssetRipper.Il2CppInspector.VersionedSerialization.Attributes;

namespace AssetRipper.Il2CppInspector.Next.BinaryMetadata;

[VersionedStruct]
public partial record struct Il2CppTypeDefinitionSizes
{
	public uint InstanceSize;
	public int NativeSize;
	public uint StaticFieldsSize;
	public uint ThreadStaticFieldsSize;
}
