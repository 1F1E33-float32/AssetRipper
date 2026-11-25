using AssetRipper.Il2CppInspector.Reflection;

namespace AssetRipper.Il2CppInspector.Next.NameTranslation;

public readonly struct NameTranslationApplierContext
{
	private readonly NameTranslationInfo _nameTranslationInfo;

	private NameTranslationApplierContext(NameTranslationInfo nameTranslationInfo)
	{
		_nameTranslationInfo = nameTranslationInfo;
	}

	public static void Process(Assembly assemblyDefinition, NameTranslationInfo nameTranslationInfo)
	{
		NameTranslationApplierContext ctx = new(nameTranslationInfo);
		ctx.Process(assemblyDefinition);
	}

	private string TranslateName(string name) => _nameTranslationInfo.NameTranslation.GetValueOrDefault(name, name);

	private bool TryTranslateName(string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? translatedName) =>
		_nameTranslationInfo.NameTranslation.TryGetValue(name, out translatedName);

	private string TranslateNamespace(string className, string currentNamespace) =>
		_nameTranslationInfo.ClassNamespaces.GetValueOrDefault(className, currentNamespace);

	private void Process(Assembly assemblyDefinition)
	{
		foreach (TypeInfo module in assemblyDefinition.DefinedTypes)
		{
			Process(module);
		}
	}

	private void Process(TypeInfo typeDefinition)
	{
		if (TryTranslateName(typeDefinition.Name, out string? translatedName))
		{
			typeDefinition.Namespace = TranslateNamespace(typeDefinition.Name, typeDefinition.Namespace);
			typeDefinition.Name = translatedName;
		}

		foreach (MethodInfo method in typeDefinition.DeclaredMethods)
		{
			Process(method);
		}

		foreach (FieldInfo field in typeDefinition.DeclaredFields)
		{
			Process(field);
		}

		foreach (PropertyInfo property in typeDefinition.DeclaredProperties)
		{
			Process(property);
		}

		foreach (EventInfo evt in typeDefinition.DeclaredEvents)
		{
			Process(evt);
		}

		foreach (TypeInfo nestedType in typeDefinition.DeclaredNestedTypes)
		{
			Process(nestedType);
		}
	}

	private void Process(MethodInfo methodDefinition)
	{
		methodDefinition.Name = TranslateName(methodDefinition.Name);
		foreach (ParameterInfo parameter in methodDefinition.DeclaredParameters)
		{
			Process(parameter);
		}
	}

	private void Process(FieldInfo fieldDefinition)
	{
		fieldDefinition.Name = TranslateName(fieldDefinition.Name);
	}

	private void Process(PropertyInfo propertyDefinition)
	{
		propertyDefinition.Name = TranslateName(propertyDefinition.Name);
	}

	private void Process(EventInfo eventDefinition)
	{
		eventDefinition.Name = TranslateName(eventDefinition.Name);
	}

	private void Process(ParameterInfo parameterDefinition)
	{
		parameterDefinition.Name = TranslateName(parameterDefinition.Name);
	}
}
