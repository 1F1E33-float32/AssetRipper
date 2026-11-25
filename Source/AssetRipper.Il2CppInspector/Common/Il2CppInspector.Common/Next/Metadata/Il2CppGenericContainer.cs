using System.Runtime.InteropServices;

namespace AssetRipper.Il2CppInspector.Next.Metadata;

using AssetRipper.Il2CppInspector.VersionedSerialization.Attributes;

[VersionedStruct]
[StructLayout(LayoutKind.Explicit)]
public partial record struct Il2CppGenericContainer
{
	[field: FieldOffset(0)]
	public int OwnerIndex { get; internal set; }

	[VersionCondition(LessThan = "106.0")]
	[field: FieldOffset(4)]
	public int TypeArgc { get; internal set; }

	[VersionCondition(GreaterThanOrEqual = "106.0")]
	[field: FieldOffset(4)]
	private ushort NewTypeArgc { get; set; }

	[VersionCondition(LessThan = "106.0")]
	[field: FieldOffset(8)]
	public int IsMethod { get; internal set; }

	[VersionCondition(GreaterThanOrEqual = "106.0")]
	[field: FieldOffset(8)]
	public byte NewIsMethod;

	[field: FieldOffset(12)]
	public GenericParameterIndex GenericParameterStart { get; internal set; }
}
