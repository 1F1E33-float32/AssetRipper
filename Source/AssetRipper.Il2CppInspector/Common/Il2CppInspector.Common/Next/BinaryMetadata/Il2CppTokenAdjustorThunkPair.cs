using AssetRipper.Il2CppInspector.VersionedSerialization.Attributes;

namespace AssetRipper.Il2CppInspector.Next.BinaryMetadata;

[VersionedStruct]
public partial record struct Il2CppTokenAdjustorThunkPair
{
	[NativeInteger]
	public uint Token;
	public Il2CppMethodPointer AdjustorThunk;
}
