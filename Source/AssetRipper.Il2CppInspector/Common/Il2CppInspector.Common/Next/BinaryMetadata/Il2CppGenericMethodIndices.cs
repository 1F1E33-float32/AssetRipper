using AssetRipper.Il2CppInspector.VersionedSerialization.Attributes;

namespace AssetRipper.Il2CppInspector.Next.BinaryMetadata;

using MethodIndex = int;

[VersionedStruct]
public partial record struct Il2CppGenericMethodIndices
{
	public Int32 MethodIndex;
	public Int32 InvokerIndex;

	[VersionCondition(EqualTo = "24.5")]
	[VersionCondition(GreaterThanOrEqual = "27.1")]
	public Int32 AdjustorThunkIndex;
}
