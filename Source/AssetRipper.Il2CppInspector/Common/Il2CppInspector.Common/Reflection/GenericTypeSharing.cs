namespace AssetRipper.Il2CppInspector.Reflection;

// IL2CPP GetSharedType/GetSharedGenericInst: preserve identity; normalize only layout lookup keys.
internal sealed class GenericTypeSharing(TypeModel model, bool enableEnumSharing)
{
	private readonly TypeInfo _objectType = model.TypesByFullName["System.Object"];
	private readonly Dictionary<TypeInfo, TypeInfo> _sharedArguments = [];

	internal TypeInfo SharedArgument(TypeInfo type)
	{
		// Only reference kinds are replaced with Object. Native pointers and
		// byrefs are not managed reference types in the runtime's kind mask.
		if (!type.IsValueType && !type.IsPointer && !type.IsByRef && !type.IsGenericParameter)
		{
			return _objectType;
		}

		if (type.IsEnum && enableEnumSharing)
		{
			return model.TypesByFullName["System." + type.GetEnumUnderlyingType().Name + "Enum"];
		}

		if (!type.IsGenericType || type.IsGenericTypeDefinition)
		{
			return type;
		}

		if (_sharedArguments.TryGetValue(type, out TypeInfo? cached))
		{
			return cached;
		}

		TypeInfo[] args = type.GetGenericArguments();
		TypeInfo[] shared = [.. args.Select(SharedArgument)];
		TypeInfo result = args.SequenceEqual(shared) ? type : type.GetGenericTypeDefinition().MakeGenericType(shared);
		_sharedArguments.Add(type, result);
		return result;
	}
}
