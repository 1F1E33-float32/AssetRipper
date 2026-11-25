using AssetRipper.Il2CppInspector.VersionedSerialization.Attributes;

namespace AssetRipper.Il2CppInspector.Next.BinaryMetadata;

using GenericMethodIndex = int;

[VersionedStruct]
public partial record struct Il2CppGenericMethodFunctionsDefinitions
{
	public Int32 GenericMethodIndex;
	public Il2CppGenericMethodIndices Indices;
}
