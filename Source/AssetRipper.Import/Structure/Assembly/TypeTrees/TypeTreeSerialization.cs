using System.Text;
using AssetRipper.Assets;
using AssetRipper.Assets.IO.Writing;
using AssetRipper.Assets.Metadata;
using AssetRipper.Import.Structure.Assembly.Serializable;
using AssetRipper.IO.Endian;
using AssetRipper.IO.Files.SerializedFiles;
using AssetRipper.SerializationLogic;

namespace AssetRipper.Import.Structure.Assembly.TypeTrees;

/// <summary>Reads native fields and projects matching fields using the exact alignment of each tree node.</summary>
public static class TypeTreeSerialization
{
	internal static void Read(
		SerializableStructure structure,
		TypeTreeNodeStruct root,
		ref EndianSpanReader reader,
		UnityVersion version,
		TransferInstructionFlags flags,
		ITypeResolver resolver
	)
	{
		for (int i = 0; i < root.Count; i++)
		{
			structure.Fields[i] = ReadValue(root[i], structure.Type.Fields[i], structure.Depth, ref reader, version, flags, resolver);
		}
		Align(root, ref reader);
	}

	private static SerializableValue ReadValue(
		TypeTreeNodeStruct node,
		SerializableType.Field field,
		int depth,
		ref EndianSpanReader reader,
		UnityVersion version,
		TransferInstructionFlags flags,
		ITypeResolver resolver
	)
	{
		SerializableValue value = default;
		if (node.IsString)
		{
			int count = ReadCount(node[0][1], ref reader);
			// Unity can store binary TextAsset content in a string node. Keep its bytes intact.
			value.CValue = new Utf8String(reader.ReadBytesExact(count));
			Align(node[0], ref reader);
		}
		else if (node.IsVector || node.IsNamedVector || node.IsMap)
		{
			value = ReadValue(node[0], field, depth, ref reader, version, flags, resolver);
		}
		else if (node.IsArray)
		{
			int count = ReadCount(node[1], ref reader);
			SerializableType.Field element = field with { ArrayDepth = field.ArrayDepth - 1, Align = false };
			value.CValue = field.Type.Type switch
			{
				PrimitiveType.Bool => CreateArray<bool>(count, field.ArrayDepth),
				PrimitiveType.Char => CreateArray<char>(count, field.ArrayDepth),
				PrimitiveType.SByte => CreateArray<sbyte>(count, field.ArrayDepth),
				PrimitiveType.Byte => CreateArray<byte>(count, field.ArrayDepth),
				PrimitiveType.Short => CreateArray<short>(count, field.ArrayDepth),
				PrimitiveType.UShort => CreateArray<ushort>(count, field.ArrayDepth),
				PrimitiveType.Int => CreateArray<int>(count, field.ArrayDepth),
				PrimitiveType.UInt => CreateArray<uint>(count, field.ArrayDepth),
				PrimitiveType.Long => CreateArray<long>(count, field.ArrayDepth),
				PrimitiveType.ULong => CreateArray<ulong>(count, field.ArrayDepth),
				PrimitiveType.Single => CreateArray<float>(count, field.ArrayDepth),
				PrimitiveType.Double => CreateArray<double>(count, field.ArrayDepth),
				PrimitiveType.String => CreateArray<Utf8String>(count, field.ArrayDepth),
				PrimitiveType.Complex => CreateArray<IUnityAssetBase>(count, field.ArrayDepth),
				PrimitiveType.Pair or PrimitiveType.MapPair => CreateArray<SerializablePair>(count, field.ArrayDepth),
				_ => throw new NotSupportedException($"Unsupported type tree array {node.Name}."),
			};
			// Primitive arrays use typed reads without implicit version-dependent padding.
			if (field.ArrayDepth == 1 && field.Type.IsPrimitive() && field.Type.Type != PrimitiveType.String)
			{
				ReadPrimitives(value.CValue, node[1], ref reader);
			}
			else
			{
				for (int i = 0; i < count; i++)
				{
					SerializableValue item = ReadValue(node[1], element, depth, ref reader, version, flags, resolver);
					switch (value.CValue)
					{
						case Utf8String[] strings:
							strings[i] = (Utf8String)item.CValue;
							break;
						case IUnityAssetBase[] assets:
							assets[i] = item.AsAsset;
							break;
						case SerializablePair[] pairs:
							pairs[i] = item.AsPair;
							break;
						case Array[] arrays:
							arrays[i] = (Array)item.CValue;
							break;
					}
				}
			}
		}
		else if (field.Type.Type is PrimitiveType.Pair or PrimitiveType.MapPair)
		{
			SerializablePair pair = new(field.Type, depth + 1);
			pair.First = ReadValue(node[0], field.Type.Fields[0], depth + 1, ref reader, version, flags, resolver);
			pair.Second = ReadValue(node[1], field.Type.Fields[1], depth + 1, ref reader, version, flags, resolver);
			value.AsPair = pair;
		}
		else if (node.IsManagedReferencesRegistry)
		{
			value.AsAsset = ManagedReferenceRegistryReader.Read(ref reader, version, flags, depth + 1, resolver);
		}
		else if (field.Type.Type == PrimitiveType.Complex && !node.IsPPtr)
		{
			SerializableStructure nested = new(field.Type, depth + 1, version);
			Read(nested, node, ref reader, version, flags, resolver);
			value.AsAsset = nested;
		}
		else
		{
			value.Read(ref reader, version, flags, depth, field with { Align = false }, resolver);
		}
		Align(node, ref reader);
		return value;

		static object CreateArray<T>(int count, int depth) =>
			depth switch
			{
				1 => count == 0 ? Array.Empty<T>() : new T[count],
				2 => count == 0 ? Array.Empty<T[]>() : new T[count][],
				_ => throw new NotSupportedException($"Unsupported type tree array depth {depth}."),
			};
	}

	private static void ReadPrimitives(object array, TypeTreeNodeStruct element, ref EndianSpanReader reader)
	{
		switch (array)
		{
			case byte[] bytes when !element.AlignBytes:
				reader.ReadBytesExact(bytes.Length).CopyTo(bytes);
				break;
			case bool[] values:
				Read(values, element, ref reader);
				break;
			case char[] values:
				Read(values, element, ref reader);
				break;
			case sbyte[] values:
				Read(values, element, ref reader);
				break;
			case byte[] values:
				Read(values, element, ref reader);
				break;
			case short[] values:
				Read(values, element, ref reader);
				break;
			case ushort[] values:
				Read(values, element, ref reader);
				break;
			case int[] values:
				Read(values, element, ref reader);
				break;
			case uint[] values:
				Read(values, element, ref reader);
				break;
			case long[] values:
				Read(values, element, ref reader);
				break;
			case ulong[] values:
				Read(values, element, ref reader);
				break;
			case float[] values:
				Read(values, element, ref reader);
				break;
			case double[] values:
				Read(values, element, ref reader);
				break;
		}

		static void Read<T>(T[] values, TypeTreeNodeStruct node, ref EndianSpanReader reader)
			where T : unmanaged
		{
			for (int i = 0; i < values.Length; i++)
			{
				values[i] = reader.ReadPrimitive<T>();
				Align(node, ref reader);
			}
		}
	}

	private static int ReadCount(TypeTreeNodeStruct element, ref EndianSpanReader reader)
	{
		int count = reader.ReadInt32();
		int minimumSize = Math.Max(1, GetMinimumSize(element));
		if (count < 0 || count > (reader.Length - reader.Position) / minimumSize)
		{
			throw new InvalidDataException($"Array {element.Name} count {count} exceeds object bounds at {reader.Position - 4}.");
		}
		return count;
	}

	public static int GetMinimumSize(TypeTreeNodeStruct node)
	{
		if (node.IsArray || node.IsString)
		{
			return sizeof(int);
		}
		if (node.Count > 0)
		{
			return node.Sum(static child => GetMinimumSize(child));
		}
		return node.TypeName switch
		{
			"bool" or "char" or "UInt8" or "SInt8" => 1,
			"short" or "ushort" or "SInt16" or "UInt16" or "unsigned short" => 2,
			"int" or "uint" or "SInt32" or "UInt32" or "unsigned int" or "float" or "Type*" => 4,
			"SInt64" or "UInt64" or "long long" or "unsigned long long" or "double" or "FileSize" => 8,
			_ => 0,
		};
	}

	private static void Align(TypeTreeNodeStruct node, ref EndianSpanReader reader)
	{
		if (node.AlignBytes)
		{
			int padding = (4 - (reader.Position & 3)) & 3;
			if (padding > reader.Length - reader.Position)
			{
				throw new InvalidDataException($"Alignment of {node.Name} exceeds object bounds.");
			}
			reader.Position += padding;
		}
	}

	internal static void Write(
		AssetWriter writer,
		TypeTreeNodeStruct root,
		SerializableType type,
		SerializableStructure? source,
		string? resourcePath = null
	)
	{
		for (int i = 0; i < root.Count; i++)
		{
			SerializableType.Field target = type.Fields[i];
			SerializableType.Field? sourceField = null;
			SerializableValue value = default;
			if (source is not null && source.TryGetIndex(target.Name, out int index))
			{
				sourceField = source.Type.Fields[index];
				value = source.Fields[index];
			}
			// Archive formats can supply an external resource binding when StreamingInfo omits its path.
			if (type.Name == "StreamingInfo" && target.Name == "path" && sourceField is null && resourcePath is not null)
			{
				sourceField = target;
				value.AsString = resourcePath;
			}
			WriteValue(writer, root[i], target, sourceField, value, resourcePath);
		}
		if (root.AlignBytes)
		{
			writer.AlignStream();
		}
	}

	private static void WriteValue(
		AssetWriter writer,
		TypeTreeNodeStruct node,
		SerializableType.Field target,
		SerializableType.Field? sourceField,
		SerializableValue value,
		string? resourcePath
	)
	{
		if (sourceField is { } source && (source.ArrayDepth != target.ArrayDepth || source.Type.Type != target.Type.Type))
		{
			throw new InvalidDataException($"Cannot project field {target.Name}: {source.Type} to {target.Type}.");
		}
		if (node.IsString)
		{
			ReadOnlySpan<byte> bytes = value.CValue is Utf8String text ? text.Data : Encoding.UTF8.GetBytes(value.AsString);
			writer.Write(bytes.Length);
			writer.Write(bytes);
			if (node[0].AlignBytes)
			{
				writer.AlignStream();
			}
		}
		else if (node.IsVector || node.IsNamedVector || node.IsMap)
		{
			WriteValue(writer, node[0], target, sourceField, value, resourcePath);
		}
		else if (node.IsArray)
		{
			Array? array = value.CValue as Array;
			writer.Write(array?.Length ?? 0);
			if (array is byte[] bytes && !node[1].AlignBytes)
			{
				writer.Write(bytes);
			}
			else if (array is not null)
			{
				SerializableType.Field element = target with { ArrayDepth = target.ArrayDepth - 1, Align = false };
				SerializableType.Field? sourceElement = sourceField is { } arrayField
					? arrayField with
					{
						ArrayDepth = arrayField.ArrayDepth - 1,
						Align = false,
					}
					: null;
				foreach (object item in array)
				{
					WriteValue(writer, node[1], element, sourceElement, FromElement(item), resourcePath);
				}
			}
		}
		else if (target.Type.Type is PrimitiveType.Pair or PrimitiveType.MapPair)
		{
			SerializablePair? pair = value.CValue as SerializablePair;
			WriteValue(writer, node[0], target.Type.Fields[0], pair?.Type.Fields[0], pair is null ? default : pair.First, resourcePath);
			WriteValue(writer, node[1], target.Type.Fields[1], pair?.Type.Fields[1], pair is null ? default : pair.Second, resourcePath);
		}
		else if (node.IsPPtr)
		{
			IPPtr pointer = value.AsPPtr;
			writer.Write(pointer.FileID);
			if (GetMinimumSize(node[1]) == sizeof(long))
			{
				writer.Write(pointer.PathID);
			}
			else
			{
				writer.Write(checked((int)pointer.PathID));
			}
		}
		else if (target.Type.Type == PrimitiveType.Complex)
		{
			Write(writer, node, target.Type, value.CValue as SerializableStructure, resourcePath);
		}
		else
		{
			value.Write(writer, target with { Align = false });
		}
		if (node.AlignBytes)
		{
			writer.AlignStream();
		}
	}

	private static SerializableValue FromElement(object item) =>
		item switch
		{
			bool value => SerializableValue.FromPrimitive(value),
			char value => SerializableValue.FromPrimitive(value),
			sbyte value => SerializableValue.FromPrimitive(value),
			byte value => SerializableValue.FromPrimitive(value),
			short value => SerializableValue.FromPrimitive(value),
			ushort value => SerializableValue.FromPrimitive(value),
			int value => SerializableValue.FromPrimitive(value),
			uint value => SerializableValue.FromPrimitive(value),
			long value => SerializableValue.FromPrimitive(value),
			ulong value => SerializableValue.FromPrimitive(value),
			float value => SerializableValue.FromPrimitive(value),
			double value => SerializableValue.FromPrimitive(value),
			_ => new(0, item),
		};
}
