using AssetRipper.Il2CppInspector.VersionedSerialization.Attributes;

namespace AssetRipper.Il2CppInspector.Next.BinaryMetadata;

using MethodIndex = int;
using TypeIndex = int;

[VersionedStruct]
public partial record struct Il2CppRgctxDefinitionData
{
	public int Value;
	public readonly Int32 MethodIndex => Value;
	public readonly Int32 TypeIndex => Value;
}
