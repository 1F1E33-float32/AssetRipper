using System.Diagnostics;
using AssetRipper.Il2CppInspector.VersionedSerialization;

namespace AssetRipper.Il2CppInspector.Next.Metadata;

using AssetRipper.Il2CppInspector.VersionedSerialization.Attributes;
using EncodedMethodIndex = uint;

[VersionedStruct]
public partial record struct Il2CppMetadataUsage
{
	private const uint TypeMask = 0b111u << 29;
	private const uint InflatedMask = 0b1;
	private const uint IndexMask = ~(TypeMask | InflatedMask);
	public readonly Il2CppMetadataUsageType Type => (Il2CppMetadataUsageType)((EncodedValue & TypeMask) >> 29);
	public readonly uint Index => (EncodedValue & IndexMask) >> 1;
	public readonly bool Inflated => (EncodedValue & InflatedMask) == 1;

	public UInt32 EncodedValue;

	public static Il2CppMetadataUsage FromValue(in StructVersion version, uint encodedValue)
	{
		if (version >= MetadataVersions.V270)
		{
			return new Il2CppMetadataUsage { EncodedValue = encodedValue };
		}

		if (version >= MetadataVersions.V190)
		{
			// Below v27 we need to fake the 'inflated' flag, so shift the value by one
			UInt32 type = (encodedValue & TypeMask) >> 29;
			UInt32 value = encodedValue & (IndexMask | 1);
			Debug.Assert((value & 0x10000000) == 0);
			return new Il2CppMetadataUsage { EncodedValue = (type << 29) | (value << 1) };
		}

		/* These encoded indices appear only in vtables, and are decoded by IsGenericMethodIndex/GetDecodedMethodIndex */
		Il2CppMetadataUsageType methodType = (encodedValue >> 31) != 0 ? Il2CppMetadataUsageType.MethodRef : Il2CppMetadataUsageType.MethodDef;
		UInt32 index = encodedValue & 0x7FFFFFFF;
		Debug.Assert((index & 0x60000000) == 0);
		return new Il2CppMetadataUsage { EncodedValue = ((uint)methodType << 29) | (index << 1) };
	}

	public override readonly string ToString()
	{
		return $"{Type} @ 0x{Index:X}";
	}
}
