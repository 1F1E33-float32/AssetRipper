using System.Diagnostics;
using System.Text;
using AssetRipper.Il2CppInspector.Bin2Object;
using AssetRipper.Il2CppInspector.Next;
using AssetRipper.Il2CppInspector.Next.BinaryMetadata;
using AssetRipper.Il2CppInspector.Next.Metadata;

namespace AssetRipper.Il2CppInspector.Utils;

public static class BlobReader
{
	public static object? GetConstantValueFromBlob(Il2CppInspector? inspector, Il2CppTypeEnum type, BinaryObjectStreamReader blob)
	{
		const byte kArrayTypeWithDifferentElements = 1;
		object? value = null;
		switch (type)
		{
			case Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN:
				value = blob.ReadBoolean();
				break;
			case Il2CppTypeEnum.IL2CPP_TYPE_U1:
				value = blob.ReadByte();
				break;
			case Il2CppTypeEnum.IL2CPP_TYPE_I1:
				value = blob.ReadPrimitive<sbyte>();
				break;
			case Il2CppTypeEnum.IL2CPP_TYPE_CHAR:
				// UTF-8 character assumed
				value = (char)blob.ReadPrimitive<short>();
				break;
			case Il2CppTypeEnum.IL2CPP_TYPE_U2:
				value = blob.ReadUInt16();
				break;
			case Il2CppTypeEnum.IL2CPP_TYPE_I2:
				value = blob.ReadInt16();
				break;
			case Il2CppTypeEnum.IL2CPP_TYPE_U4:
				value = ReadUInt32();
				break;
			case Il2CppTypeEnum.IL2CPP_TYPE_I4:
				value = ReadInt32();
				break;
			case Il2CppTypeEnum.IL2CPP_TYPE_U8:
				value = blob.ReadUInt64();
				break;
			case Il2CppTypeEnum.IL2CPP_TYPE_I8:
				value = blob.ReadInt64();
				break;
			case Il2CppTypeEnum.IL2CPP_TYPE_R4:
				value = blob.ReadSingle();
				break;
			case Il2CppTypeEnum.IL2CPP_TYPE_R8:
				value = blob.ReadDouble();
				break;
			case Il2CppTypeEnum.IL2CPP_TYPE_STRING:
				int uiLen = ReadInt32();
				if (uiLen != -1)
				{
					value = Encoding.UTF8.GetString(blob.ReadBytes(uiLen));
				}

				break;
			case Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY:
				int length = ReadInt32();
				if (length == -1)
				{
					break;
				}

				// This is only used in custom arguments.
				// We actually want the reflection TypeInfo here, but as we do not have it yet
				// we store everything in a custom array type to be changed out later in the TypeModel.
				Il2CppTypeEnum arrayElementType = ReadEncodedTypeEnum(
					inspector ?? throw new InvalidDataException("IL2CPP array constant requires type metadata."),
					blob,
					out Il2CppTypeDefinition arrayElementDef
				);
				byte arrayElementsAreDifferent = blob.ReadByte();
				ConstantBlobArrayElement[] array = new ConstantBlobArrayElement[length];
				if (arrayElementsAreDifferent == kArrayTypeWithDifferentElements)
				{
					for (int i = 0; i < length; i++)
					{
						Il2CppTypeEnum elementType = ReadEncodedTypeEnum(
							inspector ?? throw new InvalidDataException("IL2CPP array constant requires type metadata."),
							blob,
							out Il2CppTypeDefinition elementTypeDef
						);
						array[i] = new ConstantBlobArrayElement(elementTypeDef, GetConstantValueFromBlob(inspector, elementType, blob), elementType);
					}
				}
				else
				{
					for (int i = 0; i < length; i++)
					{
						array[i] = new ConstantBlobArrayElement(
							arrayElementDef,
							GetConstantValueFromBlob(inspector, arrayElementType, blob),
							arrayElementType
						);
					}
				}

				value = new ConstantBlobArray(arrayElementDef, array, arrayElementType);
				break;
			case Il2CppTypeEnum.IL2CPP_TYPE_CLASS:
			case Il2CppTypeEnum.IL2CPP_TYPE_OBJECT:
			case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
				break;
			case Il2CppTypeEnum.IL2CPP_TYPE_IL2CPP_TYPE_INDEX:
				int index = blob.ReadCompressedInt32();
				if (index != -1)
				{
					value = (inspector ?? throw new InvalidDataException("IL2CPP type constant requires type metadata.")).TypeReferences[index];
				}

				break;
			case Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE:
				break;
			default:
				throw new NotSupportedException($"IL2CPP constant kind {type} is unsupported.");
		}

		return value;
		int ReadInt32()
		{
			if (blob.Version >= MetadataVersions.V290)
			{
				long address = blob.Position;
				try
				{
					return blob.ReadCompressedInt32();
				}
				catch (InvalidDataException)
				{
					return blob.ReadInt32(address);
				}
			}

			return blob.ReadInt32();
		}

		uint ReadUInt32()
		{
			if (blob.Version >= MetadataVersions.V290)
			{
				long address = blob.Position;
				try
				{
					return blob.ReadCompressedUInt32();
				}
				catch (InvalidDataException)
				{
					return blob.ReadUInt32(address);
				}
			}

			return blob.ReadUInt32();
		}
	}

	public static Il2CppTypeEnum ReadEncodedTypeEnum(Il2CppInspector inspector, BinaryObjectStream blob, out Il2CppTypeDefinition enumType)
	{
		enumType = default;
		Il2CppTypeEnum typeEnum = (Il2CppTypeEnum)blob.ReadByte();
		if (typeEnum == Il2CppTypeEnum.IL2CPP_TYPE_ENUM)
		{
			int typeIndex = blob.ReadCompressedInt32();
			int typeHandle = inspector.TypeReferences[typeIndex].Data.KlassIndex;
			enumType = inspector.TypeDefinitions[typeHandle];
			int elementTypeIndex = enumType.GetEnumElementTypeIndex(inspector.Version);
			int elementTypeHandle = inspector.TypeReferences[elementTypeIndex].Data.KlassIndex;
			Il2CppTypeDefinition elementType = inspector.TypeDefinitions[elementTypeHandle];
			typeEnum = inspector.TypeReferences[elementType.ByValTypeIndex].Type;
		}

		// This technically also handles SZARRAY (System.Array) and all others by just returning their system type
		return typeEnum;
	}

	public record ConstantBlobArray(Il2CppTypeDefinition ArrayTypeDef, ConstantBlobArrayElement[] Elements, Il2CppTypeEnum ArrayTypeEnum);

	public record ConstantBlobArrayElement(Il2CppTypeDefinition TypeDef, object? Value, Il2CppTypeEnum TypeEnum);
}
