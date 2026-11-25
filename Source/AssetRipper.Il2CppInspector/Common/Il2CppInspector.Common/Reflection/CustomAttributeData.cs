/*
	Copyright 2017-2021 Katy Coe - http://www.djkaty.com - https://github.com/djkaty

	All rights reserved.
*/
using AssetRipper.Il2CppInspector.Next;

namespace AssetRipper.Il2CppInspector.Reflection;

// See: https://docs.microsoft.com/en-us/dotnet/api/system.reflection.customattributedata?view=netframework-4.8
public class CustomAttributeData
{
	// IL2CPP-specific data
	public TypeModel Model => AttributeType.Assembly.Model;
	public int Index { get; set; }

	// The type of the attribute
	public required TypeInfo AttributeType { get; set; }

	// v29 custom attribute info
	public CustomAttributeCtor? CtorInfo { get; set; }

	// Pre-v29 Properties used for stub Attributes
	public (ulong Start, ulong End) VirtualAddress =>
		CtorInfo != null
			? (0, 0)
			: // The last one will be wrong but there is no way to calculate it
			(Model.Package.CustomAttributeGenerators[Index], Model.Package.FunctionAddresses[Model.Package.CustomAttributeGenerators[Index]]);

	public override string ToString() => "[" + AttributeType.FullName + "]";

	// Get the machine code of the C++ function
	public byte[] GetMethodBody() =>
		Model.Package.BinaryImage.ReadMappedBytes(VirtualAddress.Start, (int)(VirtualAddress.End - VirtualAddress.Start));

	public IEnumerable<TypeInfo> GetAllTypeReferences()
	{
		yield return AttributeType;
		if (CtorInfo != null)
		{
			foreach (TypeInfo typeRef in GetTypeReferences(CtorInfo.Arguments))
			{
				yield return typeRef;
			}

			foreach (TypeInfo typeRef in GetTypeReferences(CtorInfo.Fields))
			{
				yield return typeRef;
			}

			foreach (TypeInfo typeRef in GetTypeReferences(CtorInfo.Properties))
			{
				yield return typeRef;
			}
		}

		static IEnumerable<TypeInfo> GetTypeReferences(IEnumerable<CustomAttributeArgument> arguments)
		{
			foreach (CustomAttributeArgument arg in arguments)
			{
				yield return arg.Type;
				switch (arg.Value)
				{
					case TypeInfo info:
						yield return info;
						break;
					case CustomAttributeArgument[] array:
						foreach (TypeInfo info in GetTypeReferences(array))
						{
							yield return info;
						}

						break;
				}
			}
		}
	}

	// Get all the custom attributes for a given assembly, type, member or parameter
	private static IEnumerable<CustomAttributeData> GetCustomAttributes(Assembly asm, int customAttributeIndex)
	{
		if (customAttributeIndex < 0)
		{
			yield break;
		}

		Il2CppInspector pkg = asm.Model.Package;
		// Attribute type ranges weren't included before v21 (customAttributeGenerators was though)
		if (pkg.Version < MetadataVersions.V210)
		{
			yield break;
		}

		if (pkg.Version < MetadataVersions.V290)
		{
			Next.Metadata.Il2CppCustomAttributeTypeRange range = pkg.AttributeTypeRanges[customAttributeIndex];
			for (int i = range.Start; i < range.Start + range.Count; i++)
			{
				int typeIndex = pkg.AttributeTypeIndices[i];
				if (asm.Model.AttributesByIndices.TryGetValue(i, out CustomAttributeData? attribute))
				{
					yield return attribute;
					continue;
				}

				attribute = new CustomAttributeData { Index = customAttributeIndex, AttributeType = asm.Model.TypesByReferenceIndex[typeIndex] };
				asm.Model.AttributesByIndices.TryAdd(i, attribute);
				yield return attribute;
			}
		}
		else
		{
			if (!asm.Model.AttributesByDataIndices.TryGetValue(customAttributeIndex, out List<CustomAttributeData>? attributes))
			{
				Next.Metadata.Il2CppCustomAttributeDataRange range = pkg.Metadata.AttributeDataRanges[customAttributeIndex];
				Next.Metadata.Il2CppCustomAttributeDataRange next = pkg.Metadata.AttributeDataRanges[customAttributeIndex + 1];
				uint startOffset = (uint)pkg.Metadata.AttributeDataOffset + range.StartOffset;
				uint endOffset = (uint)pkg.Metadata.AttributeDataOffset + next.StartOffset;
				CustomAttributeDataReader reader = new(pkg, asm, pkg.Metadata, startOffset, endOffset);
				if (reader.Count == 0)
				{
					yield break;
				}

				attributes =
				[
					.. reader
						.Read()
						.Select(
							(x, i) =>
								new CustomAttributeData
								{
									AttributeType = x.Ctor.DeclaringType,
									CtorInfo = x,
									Index = i,
								}
						),
				];
				asm.Model.AttributesByDataIndices[customAttributeIndex] = attributes;
			}

			foreach (CustomAttributeData attribute in attributes)
			{
				yield return attribute;
			}
		}
	}

	public static IList<CustomAttributeData> GetCustomAttributes(Assembly asm, int token, int customAttributeIndex)
	{
		int index = asm.Model.GetCustomAttributeIndex(asm, token, customAttributeIndex);
		return index < 0 ? Array.Empty<CustomAttributeData>() : GetCustomAttributes(asm, index).ToList();
	}

	public static IList<CustomAttributeData> GetCustomAttributes(Assembly asm) =>
		GetCustomAttributes(asm, asm.MetadataToken, asm.AssemblyDefinition.CustomAttributeIndex);

	public static IList<CustomAttributeData> GetCustomAttributes(EventInfo evt) =>
		GetCustomAttributes(evt.Assembly, evt.MetadataToken, evt.Definition.CustomAttributeIndex);

	public static IList<CustomAttributeData> GetCustomAttributes(FieldInfo field) =>
		GetCustomAttributes(field.Assembly, field.MetadataToken, field.Definition.CustomAttributeIndex);

	public static IList<CustomAttributeData> GetCustomAttributes(MethodBase method) =>
		GetCustomAttributes(method.Assembly, method.MetadataToken, method.Definition.CustomAttributeIndex);

	public static IList<CustomAttributeData> GetCustomAttributes(ParameterInfo param) =>
		GetCustomAttributes(param.DeclaringMethod.Assembly, param.MetadataToken, param.Definition.CustomAttributeIndex);

	public static IList<CustomAttributeData> GetCustomAttributes(PropertyInfo prop) =>
		prop.Definition.IsValid ? GetCustomAttributes(prop.Assembly, prop.MetadataToken, prop.Definition.CustomAttributeIndex) : [];

	public static IList<CustomAttributeData> GetCustomAttributes(TypeInfo type) =>
		type.Definition.IsValid ? GetCustomAttributes(type.Assembly, type.MetadataToken, type.Definition.CustomAttributeIndex) : [];
}
