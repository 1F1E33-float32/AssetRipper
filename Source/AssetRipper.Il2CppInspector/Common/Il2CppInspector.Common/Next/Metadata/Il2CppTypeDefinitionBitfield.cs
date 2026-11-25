using AssetRipper.Il2CppInspector.VersionedSerialization.Attributes;

namespace AssetRipper.Il2CppInspector.Next.Metadata;

[VersionedStruct]
public partial record struct Il2CppTypeDefinitionBitfield
{
	private uint _value;

	internal static Il2CppTypeDefinitionBitfield FromRawValue(uint value) => new() { _value = value };

	public readonly bool ValueType => ((_value >> 0) & 1) == 1;
	public readonly bool EnumType => ((_value >> 1) & 1) == 1;
	public readonly bool HasFinalize => ((_value >> 2) & 1) == 1;
	public readonly bool HasCctor => ((_value >> 3) & 1) == 1;
	public readonly bool IsBlittable => ((_value >> 4) & 1) == 1;
	public readonly bool IsImportOrWindowsRuntime => ((_value >> 5) & 1) == 1;
	public readonly PackingSize PackingSize => (PackingSize)((_value >> 6) & 0b1111);
	public readonly bool DefaultPackingSize => ((_value >> 10) & 1) == 1;
	public readonly bool DefaultClassSize => ((_value >> 11) & 1) == 1;
	public readonly PackingSize ClassSize => (PackingSize)((_value >> 12) & 0b1111);
	public readonly bool IsByRefLike => ((_value >> 16) & 1) == 1;
	public readonly bool HasInlineArray => ((_value >> 17) & 1) == 1;
}
