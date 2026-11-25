using System.Reflection;
using AssetRipper.Il2CppInspector.VersionedSerialization.Attributes;

namespace AssetRipper.Il2CppInspector.Next.Metadata;

using GenericParameterConstraintIndex = short;
using StringIndex = int;

[VersionedStruct]
public partial record struct Il2CppGenericParameter
{
	public GenericContainerIndex OwnerIndex { get; internal set; }
	public Int32 NameIndex { get; internal set; }
	public Int16 ConstraintsStart { get; internal set; }
	public short ConstraintsCount { get; internal set; }
	public ushort Num { get; internal set; }
	public ushort Flags { get; internal set; }
	public readonly GenericParameterAttributes Attributes => (GenericParameterAttributes)Flags;
}
