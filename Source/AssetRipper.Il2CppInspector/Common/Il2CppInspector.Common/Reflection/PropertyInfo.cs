/*
	Copyright 2017-2021 Katy Coe - http://www.djkaty.com - https://github.com/djkaty

	All rights reserved.
*/
using System.Reflection;
using AssetRipper.Il2CppInspector.Next.Metadata;

namespace AssetRipper.Il2CppInspector.Reflection;

public class PropertyInfo : MemberInfo
{
	public override TypeInfo DeclaringType => base.DeclaringType ?? throw new InvalidOperationException("Member has no declaring type.");

	// IL2CPP-specific data
	public Il2CppPropertyDefinition Definition { get; }
	public int Index { get; }

	// Root definition: the property with Definition != null
	protected readonly PropertyInfo _rootDefinition;

	// Information/flags about the property
	public PropertyAttributes Attributes { get; }
	public bool CanRead => GetMethod != null;
	public bool CanWrite => SetMethod != null;

	// Custom attributes for this member
	public override IEnumerable<CustomAttributeData> CustomAttributes => CustomAttributeData.GetCustomAttributes(_rootDefinition);
	public MethodInfo? GetMethod { get; }
	public MethodInfo? SetMethod { get; }
	public bool IsAutoProperty => DeclaringType.DeclaredFields.Any(f => f.Name == $"<{Name}>k__BackingField");

	public override string CSharpName
	{
		get
		{
			// Explicit interface implementation
			if (
				DeclaringType.ImplementedInterfaces.FirstOrDefault(i => Name.IndexOf("." + i.CSharpName, StringComparison.Ordinal) != -1)
				is TypeInfo @interface
			)
			{
				return Name[(Name.IndexOf("." + @interface.CSharpName, StringComparison.Ordinal) + 1)..];
			}

			// Regular property
			return Name;
		}
	}

	public TypeInfo PropertyType =>
		GetMethod?.ReturnType
		?? (SetMethod ?? throw new InvalidDataException("IL2CPP property has no accessors.")).DeclaredParameters[^1].ParameterType;
	public override MemberTypes MemberType => MemberTypes.Property;

	public PropertyInfo(Il2CppInspector pkg, int propIndex, TypeInfo declaringType)
		: base(declaringType)
	{
		Index = propIndex;
		Definition = pkg.Properties[propIndex];
		MetadataToken = (int)Definition.Token;
		Name = pkg.Strings[Definition.NameIndex];
		_rootDefinition = this;
		// Copy attributes
		Attributes = (PropertyAttributes)Definition.Attrs;
		// prop.get and prop.set are method indices from the first method of the declaring type
		if (Definition.Get >= 0)
		{
			GetMethod = declaringType.DeclaredMethods.First(x => x.Index == declaringType.Definition.MethodIndex + Definition.Get);
		}

		if (Definition.Set >= 0)
		{
			SetMethod = declaringType.DeclaredMethods.First(x => x.Index == declaringType.Definition.MethodIndex + Definition.Set);
		}
	}

	// Create a property based on a get and set method
	public PropertyInfo(MethodInfo? getter, MethodInfo? setter, TypeInfo declaringType)
		: base(declaringType)
	{
		Index = -1;
		_rootDefinition = this;
		Name = (getter ?? setter ?? throw new InvalidDataException("IL2CPP property has no accessors."))
			.Name.Replace(".get_", ".")
			.Replace(".set_", ".");
		GetMethod = getter;
		SetMethod = setter;
	}

	public PropertyInfo(PropertyInfo propertyDef, TypeInfo declaringType)
		: base(declaringType)
	{
		_rootDefinition = propertyDef;
		Name = propertyDef.Name;
		if (propertyDef.GetMethod != null)
		{
			GetMethod = declaringType.GetMethodByDefinition(propertyDef.GetMethod);
		}

		if (propertyDef.SetMethod != null)
		{
			SetMethod = declaringType.GetMethodByDefinition(propertyDef.SetMethod);
		}
	}
}
