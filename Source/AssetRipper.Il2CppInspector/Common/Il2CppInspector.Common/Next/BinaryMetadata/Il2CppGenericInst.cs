using AssetRipper.Il2CppInspector.VersionedSerialization.Attributes;

namespace AssetRipper.Il2CppInspector.Next.BinaryMetadata;

[VersionedStruct]
public partial record struct Il2CppGenericInst
{
	public readonly bool Valid => TypeArgc > 0;

	[NativeInteger]
	public uint TypeArgc;
	public Pointer<Pointer<Il2CppType>> TypeArgv;
}
