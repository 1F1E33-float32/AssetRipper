using AssetRipper.Il2CppInspector.VersionedSerialization.Attributes;

namespace AssetRipper.Il2CppInspector.Next.BinaryMetadata;

[VersionedStruct]
public partial record struct Il2CppMethodPointer
{
	public static readonly Il2CppMethodPointer Null = new();

	[NativeInteger]
	public ulong Value { get; set; }

	public Il2CppMethodPointer(ulong address) => Value = address;

	public readonly bool IsNull => Value == 0;

	public override readonly string ToString() => $"0x{Value:X}";

	public static implicit operator ulong(Il2CppMethodPointer ptr) => ptr.Value;

	public static implicit operator Il2CppMethodPointer(ulong ptr) => new(ptr);
}
