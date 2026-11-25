using System.Diagnostics;
using AssetRipper.Il2CppInspector.Next;
using AssetRipper.Il2CppInspector.Next.BinaryMetadata;
using AssetRipper.Il2CppInspector.Next.Metadata;
using AssetRipper.Il2CppInspector.Reflection;
using AssetRipper.Il2CppInspector.Utils;

namespace AssetRipper.Il2CppInspector;

public class CustomAttributeDataReader
{
	private readonly Il2CppInspector _inspector;
	private readonly Assembly _assembly;
	private readonly BinaryObjectStreamReader _data;
	private readonly uint _end;
	private readonly long _ctorBufferStart;
	private readonly long _dataBufferStart;
	public uint Count { get; }

	public CustomAttributeDataReader(Il2CppInspector inspector, Assembly assembly, BinaryObjectStreamReader data, uint startOffset, uint endOffset)
	{
		_inspector = inspector;
		_assembly = assembly;
		_data = data;
		_end = endOffset;
		data.Position = startOffset;
		Count = data.ReadCompressedUInt32();
		_ctorBufferStart = data.Position;
		_dataBufferStart = _ctorBufferStart + Count * sizeof(int);
	}

	public IEnumerable<CustomAttributeCtor> Read()
	{
		_data.Position = _ctorBufferStart;
		CustomAttributeCtor[] ctors = new CustomAttributeCtor[Count];
		for (int i = 0; i < Count; i++)
		{
			ctors[i] = new CustomAttributeCtor();
			uint ctorIndex = _data.ReadUInt32();
			ctors[i].Ctor =
				_assembly.Model.Package.Version >= MetadataVersions.V1040
					? _assembly.Model.GetMetadataUsageMethod(MetadataUsage.FromEncodedIndex(_assembly.Model.Package, ctorIndex))
					: _assembly.Model.MethodsByDefinitionIndex[ctorIndex];
		}

		_data.Position = _dataBufferStart;
		for (int i = 0; i < Count; i++)
		{
			CustomAttributeCtor ctor = ctors[i];
			TypeInfo attrClass = ctor.Ctor.DeclaringType;
			uint argumentCount = _data.ReadCompressedUInt32();
			uint fieldCount = _data.ReadCompressedUInt32();
			uint propertyCount = _data.ReadCompressedUInt32();
			ctor.Arguments = new CustomAttributeArgument[argumentCount];
			for (int j = 0; j < argumentCount; j++)
			{
				ctor.Arguments[j] = new CustomAttributeArgument();
				ReadAttributeDataValue(ctor.Arguments[j]);
			}

			ctor.Fields = new CustomAttributeFieldArgument[fieldCount];
			for (int j = 0; j < fieldCount; j++)
			{
				ctor.Fields[j] = new CustomAttributeFieldArgument();
				ReadAttributeDataValue(ctor.Fields[j]);
				(TypeInfo? fieldClass, int fieldIndex) = ReadCustomAttributeNamedArgumentClassAndIndex(attrClass);
				ctor.Fields[j].Field = fieldClass.DeclaredFields[fieldIndex];
			}

			ctor.Properties = new CustomAttributePropertyArgument[propertyCount];
			for (int j = 0; j < propertyCount; j++)
			{
				ctor.Properties[j] = new CustomAttributePropertyArgument();
				ReadAttributeDataValue(ctor.Properties[j]);
				(TypeInfo? propertyClass, int propertyIndex) = ReadCustomAttributeNamedArgumentClassAndIndex(attrClass);
				ctor.Properties[j].Property = propertyClass.DeclaredProperties[propertyIndex];
			}

			yield return ctor;
		}

		if (_data.Position != _end)
		{
			throw new InvalidDataException($"IL2CPP custom attributes ended at 0x{_data.Position:X}, expected 0x{_end:X}.");
		}
	}

	private void ReadAttributeDataValue(CustomAttributeArgument arg)
	{
		Il2CppTypeEnum type = BlobReader.ReadEncodedTypeEnum(_inspector, _data, out Il2CppTypeDefinition typeDef);
		object? value = BlobReader.GetConstantValueFromBlob(_inspector, type, _data);
		value = ConvertAttributeValue(value);
		if (value is CustomAttributeArgument valueAttr)
		{
			arg.Type = valueAttr.Type;
			arg.Value = valueAttr.Value;
		}
		else
		{
			arg.Type = ConvertTypeDef(typeDef, type);
			arg.Value = value;
		}
	}

	private object? ConvertAttributeValue(object? value)
	{
		return value switch
		{
			Il2CppType type => _assembly.Model.TypesByReferenceIndex[_inspector.TypeReferences.IndexOf(type)],
			BlobReader.ConstantBlobArray blobArray => new CustomAttributeArgument
			{
				Type = ConvertTypeDef(blobArray.ArrayTypeDef, blobArray.ArrayTypeEnum),
				Value = blobArray
					.Elements.Select(blobElem => new CustomAttributeArgument
					{
						Type = ConvertTypeDef(blobElem.TypeDef, blobElem.TypeEnum),
						Value = ConvertAttributeValue(blobElem.Value),
					})
					.ToArray(),
			},
			_ => value,
		};
	}

	private TypeInfo ConvertTypeDef(Il2CppTypeDefinition typeDef, Il2CppTypeEnum type) =>
		typeDef.IsValid
			? _assembly.Model.TypesByDefinitionIndex[_inspector.TypeDefinitions.IndexOf(typeDef)]
			: _assembly.Model.GetTypeDefinitionFromTypeEnum(type);

	private (TypeInfo, int) ReadCustomAttributeNamedArgumentClassAndIndex(TypeInfo attrInfo)
	{
		int memberIndex = _data.ReadCompressedInt32();
		if (memberIndex >= 0) // Negative indices mean that it's a member of a base class
		{
			return (attrInfo, memberIndex);
		}

		memberIndex = -(memberIndex + 1);
		uint typeDefIndex = _data.ReadCompressedUInt32();
		TypeInfo typeInfo =
			_assembly.Model.Package.Version >= MetadataVersions.V1040
				? _assembly.Model.TypesByReferenceIndex[typeDefIndex]
				: _assembly.Model.TypesByDefinitionIndex[typeDefIndex];
		return (typeInfo, memberIndex);
	}
}

public class CustomAttributeCtor
{
	private MethodBase? _ctor;
	public MethodBase Ctor
	{
		get => _ctor ?? throw new InvalidDataException("IL2CPP custom attribute Ctor is missing.");
		set => _ctor = value;
	}
	public CustomAttributeArgument[] Arguments { get; set; } = [];
	public CustomAttributeFieldArgument[] Fields { get; set; } = [];
	public CustomAttributePropertyArgument[] Properties { get; set; } = [];
}

public class CustomAttributeArgument
{
	private TypeInfo? _type;
	public TypeInfo Type
	{
		get => _type ?? throw new InvalidDataException("IL2CPP custom attribute Type is missing.");
		set => _type = value;
	}
	public object? Value { get; set; }
}

public class CustomAttributeFieldArgument : CustomAttributeArgument
{
	private FieldInfo? _field;
	public FieldInfo Field
	{
		get => _field ?? throw new InvalidDataException("IL2CPP custom attribute Field is missing.");
		set => _field = value;
	}
}

public class CustomAttributePropertyArgument : CustomAttributeArgument
{
	private PropertyInfo? _property;
	public PropertyInfo Property
	{
		get => _property ?? throw new InvalidDataException("IL2CPP custom attribute Property is missing.");
		set => _property = value;
	}
}
