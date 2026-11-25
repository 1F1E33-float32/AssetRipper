using AssetRipper.Il2CppInspector.VersionedSerialization.Attributes;

namespace AssetRipper.Il2CppInspector.Next.BinaryMetadata;

[VersionedStruct]
public partial record struct Il2CppRgctxDefinition
{
	[NativeInteger]
	public Il2CppRgctxDataType Type;
	public PrimitivePointer<byte> Data; // void*
	public readonly Pointer<Il2CppRgctxDefinitionData> Definition => Data.PointerValue;
	public readonly Pointer<Il2CppRgctxConstrainedData> Constrained => Data.PointerValue;
}
