using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AssetRipper.Il2CppInspector.VersionedSerialization.Attributes;

namespace AssetRipper.Il2CppInspector.Next.Metadata;

using StringIndex = int;

[InlineArray(PublicKeyLength)]
public struct PublicKeyToken
{
	private const int PublicKeyLength = 8;
	private byte _value;
}

[VersionedStruct]
[StructLayout(LayoutKind.Explicit)]
public partial record struct Il2CppAssemblyNameDefinition
{
	[FieldOffset(0)]
	public Int32 NameIndex;

	[FieldOffset(4)]
	public Int32 CultureIndex;

	[FieldOffset(8)]
	[VersionCondition(LessThanOrEqual = "24.3")]
	public int HashValueIndex;

	[FieldOffset(12)]
	public Int32 PublicKeyIndex;

	[FieldOffset(44)]
	[VersionCondition(LessThanOrEqual = "15.0")]
	private PublicKeyToken _legacyPublicKeyToken;

	[FieldOffset(16)]
	public AssemblyHashAlgorithm HashAlg;

	[FieldOffset(20)]
	public int HashLen;

	[FieldOffset(24)]
	public AssemblyNameFlags Flags;

	[FieldOffset(28)]
	public int Major;

	[FieldOffset(32)]
	public int Minor;

	[FieldOffset(36)]
	public int Build;

	[FieldOffset(40)]
	public int Revision;

	[FieldOffset(44)]
	public PublicKeyToken PublicKeyToken;
}
