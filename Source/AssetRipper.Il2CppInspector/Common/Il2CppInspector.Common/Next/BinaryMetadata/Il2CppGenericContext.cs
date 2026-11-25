using AssetRipper.Il2CppInspector.VersionedSerialization.Attributes;

namespace AssetRipper.Il2CppInspector.Next.BinaryMetadata;

[VersionedStruct]
public partial record struct Il2CppGenericContext
{
	public Pointer<Il2CppGenericInst> ClassInst;
	public Pointer<Il2CppGenericInst> MethodInst;
}
