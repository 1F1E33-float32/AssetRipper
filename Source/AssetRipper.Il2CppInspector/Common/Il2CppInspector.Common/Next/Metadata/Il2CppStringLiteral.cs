namespace AssetRipper.Il2CppInspector.Next.Metadata;

using AssetRipper.Il2CppInspector.VersionedSerialization.Attributes;
using StringLiteralIndex = int;

[VersionedStruct]
public partial record struct Il2CppStringLiteral
{
	[VersionCondition(LessThanOrEqual = "31.0")]
	public uint Length { get; private set; }
	public Int32 DataIndex { get; private set; }
}
