using AssetRipper.Il2CppInspector.VersionedSerialization.Attributes;

namespace AssetRipper.Il2CppInspector.Next.BinaryMetadata;

using GenericInstIndex = int;
using MethodIndex = int;

[VersionedStruct]
public partial record struct Il2CppMethodSpec
{
	public Int32 MethodDefinitionIndex;
	public Int32 ClassIndexIndex;
	public Int32 MethodIndexIndex;
}
