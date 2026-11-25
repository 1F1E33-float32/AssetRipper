/*
	Copyright 2017-2021 Katy Coe - http://www.djkaty.com - https://github.com/djkaty
	Copyright 2020 Robert Xiao - https://robertxiao.ca

	All rights reserved.
*/
using System.Collections;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using AssetRipper.Il2CppInspector.Next.BinaryMetadata;
using AssetRipper.Il2CppInspector.Next.Metadata;

namespace AssetRipper.Il2CppInspector.Reflection;

public class TypeInfo : MemberInfo
{
	// IL2CPP-specific data
	private readonly int _definitionIndex = -1;
	public Il2CppTypeDefinition Definition => _definitionIndex < 0 ? default : Assembly.Model.Package.TypeDefinitions[_definitionIndex];

	private Il2CppTypeDefinitionSizes _sizes;
	private int? _gameLayoutGroup;
	private bool _gameLayoutResolved;
	public Il2CppTypeDefinitionSizes Sizes
	{
		get
		{
			ResolveGameLayout();
			return _sizes;
		}
	}

	internal int? GameLayoutGroup
	{
		get
		{
			ResolveGameLayout();
			return _gameLayoutGroup;
		}
	}

	private void ResolveGameLayout()
	{
		if (_gameLayoutResolved || !IsGenericType || IsGenericTypeDefinition)
		{
			return;
		}

		if (Assembly.Model.GameLayouts is not { } layouts)
		{
			return;
		}

		_gameLayoutResolved = true;
		layouts.Bind(this);
	}

	internal void SetGameLayoutGroup(int group)
	{
		_gameLayoutGroup = group;
		_sizes = (
			Assembly.Model.Package.Metadata.GameAdapter ?? throw new InvalidDataException("IL2CPP type layout has no metadata adapter.")
		).LayoutSizes(group);
		_gameLayoutResolved = true;
	}

	internal void SetRuntimeSizes(Il2CppTypeDefinitionSizes value) => _sizes = value;

	internal void ReleaseGeneratedFields()
	{
		if (_genericTypeDefinition != null)
		{
			_declaredFields = null;
		}
	}

	public int Index { get; } = -1;

	// This dictionary will cache all instantiated generic types out of this definition.
	// Only valid for GenericTypeDefinition - not valid on instantiated types!
	private readonly Dictionary<TypeInfo[], TypeInfo> _genericTypeInstances = new(new TypeArgumentsComparer());

	public class TypeArgumentsComparer : EqualityComparer<TypeInfo[]>
	{
		public override bool Equals(TypeInfo[]? x, TypeInfo[]? y)
		{
			return x is null ? y is null : ((IStructuralEquatable)x).Equals(y, StructuralComparisons.StructuralEqualityComparer);
		}

		public override int GetHashCode(TypeInfo[] obj)
		{
			return ((IStructuralEquatable)obj).GetHashCode(StructuralComparisons.StructuralEqualityComparer);
		}
	}

	// Cached derived types
	private Dictionary<int, TypeInfo>? _generatedArrayTypes;
	private TypeInfo? _generatedByRefType;
	private TypeInfo? _generatedPointerType;

	// Information/flags about the type
	// Undefined if the Type represents a generic type parameter
	public TypeAttributes Attributes { get; }

	public TypeInfo? BaseType
	{
		get
		{
			if (IsPointer || IsByRef)
			{
				return null;
			}

			if (IsArray)
			{
				return Assembly.Model.TypesByFullName["System.Array"];
			}

			if (IsEnum)
			{
				return Assembly.Model.TypesByFullName["System.Enum"];
			}

			if (Definition.IsValid)
			{
				// L-NOTE: On metadata v35 and above, enums store their element type in the parent index field
				// This means that we either have to implement a version check here, or handle enums in general.
				// I chose to do the latter, which is why there is an if (IsEnum) check above.
				if (Definition.ParentIndex >= 0)
				{
					return Assembly.Model.TypesByReferenceIndex[Definition.ParentIndex];
				}
			}

			if (_genericTypeDefinition != null)
			{
				return _genericTypeDefinition.BaseType?.SubstituteGenericArguments(_genericArguments);
			}

			if (IsGenericParameter)
			{
				TypeInfo? res = GetGenericParameterConstraints().FirstOrDefault(t => !t.IsInterface);
				if (res != null)
				{
					return res;
				}
			}

			if (Namespace != "System" || BaseName != "Object")
			{
				return Assembly.Model.TypesByFullName["System.Object"];
			}

			return null;
		}
	}

	public override TypeInfo? DeclaringType
	{
		get
		{
			if (Definition.IsValid)
			{
				/* Type definition */
				if (Definition.DeclaringTypeIndex == -1)
				{
					return null;
				}

				TypeInfo? type = Assembly.Model.TypesByReferenceIndex[Definition.DeclaringTypeIndex];
				if (type == null)
				{
					/* This might happen while initially setting up the types */
					Il2CppType typeRef = Assembly.Model.Package.TypeReferences[Definition.DeclaringTypeIndex];
					type = Assembly.Model.TypesByDefinitionIndex[typeRef.Data.KlassIndex];
				}

				return type;
			}

			if (_genericTypeDefinition != null)
			{
				// Generic parameters are *not* substituted in the DeclaringType
				return _genericTypeDefinition.DeclaringType;
			}

			return base.DeclaringType;
		}
	}

	// True if the type contains unresolved generic type parameters
	public bool ContainsGenericParameters =>
		(HasElementType && ElementType.ContainsGenericParameters) || IsGenericParameter || _genericArguments.Any(ga => ga.ContainsGenericParameters);

	// Custom attributes for this member
	public override IEnumerable<CustomAttributeData> CustomAttributes => CustomAttributeData.GetCustomAttributes(_genericTypeDefinition ?? this);

	private List<ConstructorInfo>? _declaredConstructors;
	public ReadOnlyCollection<ConstructorInfo> DeclaredConstructors
	{
		get
		{
			if (_declaredConstructors != null)
			{
				return _declaredConstructors.AsReadOnly();
			}

			if (_genericTypeDefinition != null)
			{
				List<ConstructorInfo> result = [.. _genericTypeDefinition.DeclaredConstructors.Select(GetConstructorByDefinition)];
				_declaredConstructors = result;
				return result.AsReadOnly();
			}

			return new List<ConstructorInfo>().AsReadOnly();
		}
	}

	private List<EventInfo>? _declaredEvents;
	public ReadOnlyCollection<EventInfo> DeclaredEvents
	{
		get
		{
			if (_declaredEvents != null)
			{
				return _declaredEvents.AsReadOnly();
			}

			if (_genericTypeDefinition != null)
			{
				List<EventInfo> result = [.. _genericTypeDefinition.DeclaredEvents.Select(c => new EventInfo(c, this))];
				_declaredEvents = result;
				return result.AsReadOnly();
			}

			return new List<EventInfo>().AsReadOnly();
		}
	}

	private List<FieldInfo>? _declaredFields;
	public ReadOnlyCollection<FieldInfo> DeclaredFields
	{
		get
		{
			if (_declaredFields != null)
			{
				return _declaredFields.AsReadOnly();
			}

			if (_genericTypeDefinition != null)
			{
				List<FieldInfo> result = [.. _genericTypeDefinition.DeclaredFields.Select(c => new FieldInfo(c, this))];
				_declaredFields = result;
				return result.AsReadOnly();
			}

			return new List<FieldInfo>().AsReadOnly();
		}
	}

	public List<MemberInfo> DeclaredMembers =>
		[
			.. new IEnumerable<MemberInfo>[]
			{
				DeclaredConstructors,
				DeclaredEvents,
				DeclaredFields,
				DeclaredMethods,
				DeclaredNestedTypes,
				DeclaredProperties,
			}.SelectMany(m => m),
		];

	private List<MethodInfo>? _declaredMethods;
	public ReadOnlyCollection<MethodInfo> DeclaredMethods
	{
		get
		{
			if (_declaredMethods != null)
			{
				return _declaredMethods.AsReadOnly();
			}

			if (_genericTypeDefinition != null)
			{
				List<MethodInfo> result = [.. _genericTypeDefinition.DeclaredMethods.Select(GetMethodByDefinition)];
				_declaredMethods = result;
				return result.AsReadOnly();
			}

			return new List<MethodInfo>().AsReadOnly();
		}
	}

	private readonly TypeRef[] _declaredNestedTypes = [];
	public IEnumerable<TypeInfo> DeclaredNestedTypes
	{
		get
		{
			if (_declaredNestedTypes != null)
			{
				return _declaredNestedTypes.Select(x => x.Value);
			}

			/* Type parameters are not substituted into nested classes,
				 * as nested classes aren't required to use the parameters
				 * from the containing class.
				 * This also matches the behaviour of the C# reflection API.
				 */
			if (_genericTypeDefinition != null)
			{
				return _genericTypeDefinition.DeclaredNestedTypes;
			}

			return [];
		}
	}

	private List<PropertyInfo>? _declaredProperties;
	public ReadOnlyCollection<PropertyInfo> DeclaredProperties
	{
		get
		{
			if (_declaredProperties != null)
			{
				return _declaredProperties.AsReadOnly();
			}

			if (_genericTypeDefinition != null)
			{
				List<PropertyInfo> result = [.. _genericTypeDefinition.DeclaredProperties.Select(c => new PropertyInfo(c, this))];
				_declaredProperties = result;
				return result.AsReadOnly();
			}

			return new List<PropertyInfo>().AsReadOnly();
		}
	}

	// Get a field by its name
	public FieldInfo? GetField(string name) => DeclaredFields.FirstOrDefault(f => f.Name == name);

	private readonly TypeRef[] _genericParameterConstraints = [];

	// Get type constraints on a generic parameter
	public TypeInfo[] GetGenericParameterConstraints() => _genericParameterConstraints?.Select(t => t.Value).ToArray() ?? [];

	private readonly TypeInfo? _genericTypeDefinition;

	/* https://docs.microsoft.com/en-us/dotnet/api/system.type.getgenerictypedefinition?view=netframework-4.8 */
	public TypeInfo GetGenericTypeDefinition()
	{
		if (_genericTypeDefinition != null)
		{
			return _genericTypeDefinition;
		}

		if (IsGenericTypeDefinition)
		{
			return this;
		}

		throw new InvalidOperationException("This method can only be called on generic types");
	}

	private Dictionary<MethodBase, MethodBase>? _constructedMembers;

	public ConstructorInfo GetConstructorByDefinition(ConstructorInfo definition)
	{
		if (_genericTypeDefinition != null)
		{
			_constructedMembers ??= [];
			if (!_constructedMembers.TryGetValue(definition.RootDefinition, out MethodBase? member))
			{
				_constructedMembers.Add(definition.RootDefinition, member = new ConstructorInfo((ConstructorInfo)definition.RootDefinition, this));
			}

			return (ConstructorInfo)member;
		}

		return definition;
	}

	// Get a method or constructor by the base type definition of that method
	public MethodInfo GetMethodByDefinition(MethodInfo definition)
	{
		if (_genericTypeDefinition != null)
		{
			_constructedMembers ??= [];
			if (!_constructedMembers.TryGetValue(definition.RootDefinition, out MethodBase? member))
			{
				_constructedMembers.Add(definition.RootDefinition, member = new MethodInfo((MethodInfo)definition.RootDefinition, this));
			}

			if (definition.IsGenericMethod && !definition.IsGenericMethodDefinition)
			{
				return (MethodInfo)
					member.MakeGenericMethod([.. definition.GetGenericArguments().Select(t => t.SubstituteGenericArguments(_genericArguments))]);
			}

			return (MethodInfo)member;
		}

		return definition;
	}

	// Get a method by its name
	public MethodInfo? GetMethod(string name) => DeclaredMethods.FirstOrDefault(m => m.Name == name);

	// Get all methods with same name (overloads)
	public MethodInfo[] GetMethods(string name) => [.. DeclaredMethods.Where(m => m.Name == name)];

	// Get methods including inherited methods
	public MethodInfo[] GetAllMethods()
	{
		List<IEnumerable<MethodInfo>> methods = [];
		// Specifically return a list in order of most derived to least derived
		for (TypeInfo? type = this; type != null; type = type.BaseType)
		{
			methods.Add(type.DeclaredMethods);
		}

		return [.. methods.SelectMany(m => m)];
	}

	// Get a property by its name
	public PropertyInfo? GetProperty(string name) => DeclaredProperties.FirstOrDefault(p => p.Name == name);

	public MethodBase[] GetVTable()
	{
		if (Definition.IsValid)
		{
			MetadataUsage[] vt = Assembly.Model.Package.GetVTable(Definition);
			MethodBase[] res = new MethodBase[vt.Length];
			for (int i = 0; i < vt.Length; i++)
			{
				if (vt[i].IsValid)
				{
					res[i] = Assembly.Model.GetMetadataUsageMethod(vt[i]);
				}
			}

			return res;
		}
		else if (_genericTypeDefinition != null)
		{
			MethodBase[] baseVt = _genericTypeDefinition.GetVTable();
			MethodBase[] res = new MethodBase[baseVt.Length];
			for (int i = 0; i < baseVt.Length; i++)
			{
				if (baseVt[i] == null)
				{
					continue;
				}

				TypeInfo declaringType = baseVt[i].DeclaringType.SubstituteGenericArguments(_genericArguments);
				if (baseVt[i] is ConstructorInfo ci)
				{
					res[i] = declaringType.GetConstructorByDefinition(ci);
				}
				else
				{
					res[i] = declaringType.GetMethodByDefinition((MethodInfo)baseVt[i]);
				}
			}

			return res;
		}

		return [];
	}

	// Method that the type is declared in if this is a type parameter of a generic method
	// TODO: Make a unit test from this: https://docs.microsoft.com/en-us/dotnet/api/system.type.declaringmethod?view=netframework-4.8
	public MethodBase? DeclaringMethod { get; }

	// IsGenericTypeParameter and IsGenericMethodParameter from https://github.com/dotnet/corefx/issues/23883
	public bool IsGenericTypeParameter => IsGenericParameter && DeclaringMethod == null;
	public bool IsGenericMethodParameter => IsGenericParameter && DeclaringMethod != null;

	// Gets the type of the object encompassed or referred to by the current array, pointer or reference type
	private readonly TypeInfo? _elementType;
	public TypeInfo ElementType => _elementType ?? throw new InvalidOperationException("This IL2CPP type has no element type.");
	#region Names
	public string BaseName => base.Name;

	private static string UnmangleName(string name)
	{
		int index = name.IndexOf("`", StringComparison.Ordinal);
		if (index != -1)
		{
			name = name[..index];
		}

		return name;
	}

	// Get rid of generic backticks and invalid characters
	public string CSharpBaseName => UnmangleName(base.Name).ToCIdentifier();

	// C# colloquial name of the type (if available)
	public override string CSharpName
	{
		get
		{
			if (HasElementType)
			{
				string? n = ElementType.CSharpName;
				if (IsByRef)
				{
					n = "ref " + n;
				}

				if (IsArray)
				{
					n += "[" + new string(',', GetArrayRank() - 1) + "]";
				}

				if (IsPointer)
				{
					n += "*";
				}

				return n;
			}
			else
			{
				string s = Namespace + "." + base.Name;
				int i = Il2CppConstants.FullNameTypeString.IndexOf(s);
				string? n = (i != -1 ? Il2CppConstants.CSharpTypeString[i] : CSharpBaseName);
				TypeInfo[] ga = GetGenericArguments();
				if (ga.Any())
				{
					n += "<" + string.Join(", ", ga.Select(x => x.CSharpName)) + ">";
				}

				if (s == "System.Nullable`1" && ga.Any())
				{
					n = ga[0].CSharpName + "?";
				}

				return n;
			}
		}
	}

	// C# name as it would be written in a type declaration
	public string GetCSharpTypeDeclarationName(bool includeVariance = false)
	{
		if (HasElementType)
		{
			string? n = ElementType.GetCSharpTypeDeclarationName();
			if (IsByRef)
			{
				n = "ref " + n;
			}

			if (IsArray)
			{
				n += "[" + new string(',', GetArrayRank() - 1) + "]";
			}

			if (IsPointer)
			{
				n += "*";
			}

			return n;
		}
		else
		{
			string? n = CSharpBaseName;
			IEnumerable<TypeInfo> ga = IsNested
				? GetGenericArguments().Where(p => (DeclaringType?.GetGenericArguments() ?? []).All(dp => dp.Name != p.Name))
				: GetGenericArguments();
			if (ga.Any())
			{
				n +=
					"<"
					+ string.Join(
						", ",
						ga.Select(x => (!x.IsGenericTypeParameter ? x.Namespace + "." : "") + x.GetCSharpTypeDeclarationName(includeVariance: true))
					)
					+ ">";
			}

			if (includeVariance)
			{
				if ((GenericParameterAttributes & GenericParameterAttributes.Covariant) == GenericParameterAttributes.Covariant)
				{
					n = "out " + n;
				}

				if ((GenericParameterAttributes & GenericParameterAttributes.Contravariant) == GenericParameterAttributes.Contravariant)
				{
					n = "in " + n;
				}
			}

			return n;
		}
	}

	// Display name of object
	public override string Name
	{
		get
		{
			if (IsGenericParameter)
			{
				return base.Name;
			}

			if (HasElementType)
			{
				string? n = ElementType.Name;
				if (IsArray)
				{
					n += "[" + new string(',', GetArrayRank() - 1) + "]";
				}

				if (IsByRef)
				{
					n += "&";
				}

				if (IsPointer)
				{
					n += "*";
				}

				return n;
			}
			else
			{
				/* XXX This is not exactly accurate to C# Type.Name:
					 * Type.Name should be the bare name (with & * [] suffixes)
					 * but without nested types or generic arguments */
				string? n = base.Name;
				if (DeclaringType != null)
				{
					n = DeclaringType.Name + "+" + n;
				}

				TypeInfo[] ga = GetGenericArguments();
				if (ga.Any())
				{
					n += "[" + string.Join(",", ga.Select(x => x.Namespace != Namespace ? x.FullName ?? x.Name : x.Name)) + "]";
				}

				return n;
			}
		}
	}

	// Type name including namespace
	// Fully qualified generic type names from the C# compiler use backtick and arity rather than a list of generic arguments
	public string? FullName
	{
		get
		{
			if (IsGenericParameter)
			{
				return null;
			}

			if (HasElementType)
			{
				string? n = ElementType.FullName;
				if (n == null)
				{
					return null;
				}

				if (IsArray)
				{
					n += "[" + new string(',', GetArrayRank() - 1) + "]";
				}

				if (IsByRef)
				{
					n += "&";
				}

				if (IsPointer)
				{
					n += "*";
				}

				return n;
			}
			else
			{
				string? n = base.Name;
				if (DeclaringType != null)
				{
					n = DeclaringType.FullName + "+" + n;
				}
				else if (Namespace.Length > 0)
				{
					n = Namespace + "." + n;
				}

				return n;
			}
		}
	}

	// Returns the minimally qualified type name required to refer to this type within the specified scope
	private string GetScopedFullName(Scope scope)
	{
		// This is the type to be used (generic type parameters have a null FullName)
		string usedType = (FullName ?? Name).Replace('+', '.');
		// This is the scope in which this type is currently being used
		// If Scope.Current is null, our scope is at the assembly level
		string? usingScope = scope.Current?.FullName?.Replace('+', '.') ?? "";
		// This is the scope in which this type's definition is located
		string? declaringScope = DeclaringType?.FullName?.Replace('+', '.') ?? Namespace;
		// Are we in the same scope as the scope the type is defined in? Save ourselves a bunch of work if so
		if (usingScope == declaringScope)
		{
			return base.Name;
		}

		// We're also in the same scope the type is defined in if we're looking for a nested type
		// that is declared in a type we derive from
		for (TypeInfo? b = scope.Current?.BaseType; b != null; b = b.BaseType)
		{
			if (b.FullName?.Replace('+', '.') == declaringScope)
			{
				return base.Name;
			}
		}

		// Find first difference in the declaring scope from the using scope, moving one namespace/type name at a time
		int diff = 1;
		usingScope += ".";
		declaringScope += ".";
		while (
			usingScope.IndexOf('.', diff) == declaringScope.IndexOf('.', diff)
			&& usingScope.IndexOf('.', diff) != -1
			&& usingScope[..usingScope.IndexOf('.', diff)] == declaringScope[..declaringScope.IndexOf('.', diff)]
		)
		{
			diff = usingScope.IndexOf('.', diff) + 1;
		}

		usingScope = usingScope[..^1];
		declaringScope = declaringScope[..^1];
		// This is the mutual root namespace and optionally nested types that the two scopes share
		string mutualRootScope = usingScope[..(diff - 1)];
		// Determine if the using scope is a child of the declaring scope (always a child if declaring scope is empty)
		bool usingScopeIsChildOfDeclaringScope = string.IsNullOrEmpty(declaringScope) || (usingScope + ".").StartsWith(declaringScope + ".");
		// Determine using directive to use
		string? usingDirective = // If the scope of usage is inside the scope in which the type is declared, no additional scope is needed
			// but we still need to check for ancestor conflicts below
			usingScopeIsChildOfDeclaringScope
				? declaringScope // Check to see if there is a namespace in our using directives which brings this type into scope
				// Sort by descending order of length to search the deepest namespaces first
				: scope.Namespaces?.OrderByDescending(n => n.Length).FirstOrDefault(n => declaringScope == n || declaringScope.StartsWith(n + "."));
		// minimallyScopedName will eventually contain the least qualified name needed to access the type
		// Initially we set it as follows:
		// - The non-mutual part of the declaring scope if there is a mutual root scope
		// - The fully-qualified type name if there is no mutual root scope
		// - The leaf name if the declaring scope and mutual root scope are the same
		// The first two must be checked in this order to avoid a . at the start
		// when the mutual root scope and declaring scope are both empty
		string? minimallyScopedName =
			declaringScope == mutualRootScope ? base.Name
			: string.IsNullOrEmpty(mutualRootScope) ? declaringScope + '.' + base.Name
			: string.Concat(declaringScope.AsSpan(mutualRootScope.Length + 1), ".", base.Name);
		// Find the outermost type name if the wanted type is a nested type (if we need it below)
		string outerTypeName = "";
		if (!usingScopeIsChildOfDeclaringScope)
		{
			for (TypeInfo? d = this; d != null; d = d.DeclaringType)
			{
				outerTypeName = d.BaseName;
			}
		}

		// Are there any ancestor nested types or namespaces in the using scope with the same name as the wanted type's unqualified name?
		// If so, the ancestor name will hide the type we are trying to reference, so we need to provide a higher-level scope
		// If the using scope is a child of the declaring scope, we can try every parent scope until we find one that doesn't hide the type
		// Otherwise, we just try the unqualified outer (least nested) type name to make sure it's accessible
		// and revert to the fully qualified name if it's hidden
		string[] nsAndTypeHierarchy = usingScopeIsChildOfDeclaringScope
			? [.. (usingDirective?.Split('.') ?? []), minimallyScopedName]
			: [outerTypeName];
		bool hidden = true;
		bool foundTypeInAncestorScope = false;
		string testTypeName = "";
		for (int depth = nsAndTypeHierarchy.Length - 1; depth >= 0 && hidden; depth--)
		{
			testTypeName = nsAndTypeHierarchy[depth] + (testTypeName.Length > 0 ? "." : "") + testTypeName;
			hidden = false;
			for (TypeInfo? d = scope.Current; d != null && !hidden && !foundTypeInAncestorScope; d = d.DeclaringType)
			{
				// If neither condition is true, the wanted type is not hidden by the type we are testing
				foundTypeInAncestorScope = d.FullName == FullName;
				hidden = !foundTypeInAncestorScope && d.BaseName == testTypeName;
			}

			// We found the shortest non-hidden scope we can use
			// For a child scope, use the shortest found scope
			// Otherwise, we've confirmed the outer nested type name is not hidden so go ahead and use the nested type name without a namespace
			if (!hidden)
			{
				minimallyScopedName = usingScopeIsChildOfDeclaringScope ? testTypeName : Name.Replace('+', '.');
			}

			// If the wanted type is an unhidden ancestor, we don't need any additional scope at all
			if (foundTypeInAncestorScope)
			{
				minimallyScopedName = base.Name;
			}
		}

		// If there are multiple using directives that would allow the same minimally scoped name to be used,
		// then the minimally scoped name is ambiguous and we can't use it
		// Note that if the wanted type is an unhidden outer class relative to the using scope, this takes precedence and there can be no ambiguity
		if (!foundTypeInAncestorScope)
		{
			// Only test the outermost type name
			outerTypeName = minimallyScopedName.Split('.')[0];
			// Take matching type names from all namespaces in scope
			List<string> matchingNamespaces =
				scope.Namespaces?.Where(n => Assembly.Model.TypesByFullName.ContainsKey(n + "." + outerTypeName)).ToList() ?? [];
			// The global namespace is in scope so take every matching type from that too
			if (Assembly.Model.TypesByFullName.ContainsKey(outerTypeName))
			{
				matchingNamespaces.Add("");
			}

			// More than one possible matching namespace? If so, the type reference is ambiguous
			if (matchingNamespaces.Count > 1)
			{
				// TODO: This can be improved to cut off a new mutual root that doesn't cause ambiguity
				minimallyScopedName = usedType;
			}

			// No matching namespaces, not hidden, no mutual root scope in the file and no using directive?
			// If so, the type's namespace is completely out of scope so use the fully-qualified type name
			if (matchingNamespaces.Count == 0 && !hidden && string.IsNullOrEmpty(mutualRootScope) && usingDirective == null)
			{
				minimallyScopedName = usedType;
			}
		}

		// Finally, check if the selected name has ambiguity with any available namespaces in the current scope
		// If so, use the full name with the mutual root scope cut off from the start
		List<string> checkNamespaces =
			scope.Namespaces?.Select(ns => (!string.IsNullOrEmpty(ns) ? ns + "." : "") + minimallyScopedName).ToList() ?? [];
		if (Assembly.Model.Namespaces.Intersect(checkNamespaces).Any())
		{
			minimallyScopedName = mutualRootScope.Length > 0 ? usedType[(mutualRootScope.Length + 1)..] : usedType;
		}
		// Check current namespace and all ancestors too
		else
		{
			checkNamespaces.Clear();
			string? ancestorUsingScope = "." + usingScope;
			while (ancestorUsingScope.Contains('.'))
			{
				ancestorUsingScope = ancestorUsingScope[..ancestorUsingScope.LastIndexOf('.')];
				checkNamespaces.Add((ancestorUsingScope.Length > 0 ? ancestorUsingScope[1..] + "." : "") + minimallyScopedName);
			}

			if (Assembly.Model.Namespaces.Intersect(checkNamespaces).Any())
			{
				minimallyScopedName = mutualRootScope.Length > 0 ? usedType[(mutualRootScope.Length + 1)..] : "global::" + usedType;
			}
		}

		// If the final name starts with ".", it's a reference to the global namespace (ie. unnamed root namespace)
		if (minimallyScopedName.StartsWith('.'))
		{
			minimallyScopedName = "global::" + minimallyScopedName[1..];
		}

		return minimallyScopedName;
	}

	// C#-friendly type name as it should be used in the scope of a given type
	public string GetScopedCSharpName(Scope? usingScope = null, bool omitRef = false, bool isPartOfTypeDeclaration = false)
	{
		// Unscoped name if no using scope specified
		if (usingScope == null)
		{
			return CSharpName;
		}

		// Generic parameters (or generic arrays) don't have a scope
		if (HasNoScope)
		{
			return CSharpName;
		}

		string s = Namespace + "." + base.Name;
		// Built-in keyword type names do not require a scope
		int i = Il2CppConstants.FullNameTypeString.IndexOf(s);
		string? n = i != -1 ? Il2CppConstants.CSharpTypeString[i] : GetScopedFullName(usingScope);
		n = UnmangleName(n).ToCIdentifier(allowScopeQualifiers: true);
		// Generic type parameters and type arguments
		// Inheriting from a base class or implementing an interface use the type's declaring scope, not the type's scope itself
		// for generic type parameters
		Scope? outerScope = usingScope;
		if (isPartOfTypeDeclaration)
		{
			outerScope = new Scope { Current = usingScope.Current?.DeclaringType, Namespaces = usingScope.Namespaces };
		}

		string g = string.Join(", ", GetGenericTypeParameters(usingScope).Select(x => x.GetScopedCSharpName(outerScope)));
		if (!string.IsNullOrEmpty(g))
		{
			n += "<" + g + ">";
		}

		// Nullable types
		if (s == "System.Nullable`1" && GetGenericArguments().Any())
		{
			n = GetGenericArguments()[0].GetScopedCSharpName(usingScope) + "?";
		}

		// Arrays, pointers, references
		if (HasElementType)
		{
			n = ElementType.GetScopedCSharpName(usingScope);
		}

		return (IsByRef && !omitRef ? "ref " : "") + n + (IsArray ? "[" + new string(',', GetArrayRank() - 1) + "]" : "") + (IsPointer ? "*" : "");
	}

	#endregion
	// Get the generic type parameters for a specific usage of this type based on its scope
	private IEnumerable<TypeInfo> GetGenericTypeParameters(Scope scope)
	{
		TypeInfo[] ga = GetGenericArguments();
		// If empty scope specified, or no type parameters, stop here
		if (scope.Current == null || !ga.Any())
		{
			return ga;
		}

		// In order to elide generic type parameters, the using scope must be a parent of the declaring scope
		// Determine if the using scope is a parent of the declaring scope (always a child if using scope is empty)
		bool usingScopeIsParent = false;
		for (TypeInfo? s = DeclaringType; s != null && !usingScopeIsParent; s = s.DeclaringType)
		{
			if (s == scope.Current)
			{
				usingScopeIsParent = true;
			}
		}

		if (!usingScopeIsParent)
		{
			return ga;
		}

		// Get the generic type parameters available in the using scope
		// (no need to recurse because every nested type inherits all of the generic type parameters of all of its ancestors)
		TypeInfo[] gasInScope = scope.Current.GetGenericArguments();
		// Return all of the generic type parameters this type uses minus those already in scope
		return ga.Where(p => gasInScope.All(pp => pp.Name != p.Name));
	}

	public GenericParameterAttributes GenericParameterAttributes { get; }

	// Generic parameter position in list of non-concrete type parameters
	// See: https://docs.microsoft.com/en-us/dotnet/api/system.type.genericparameterposition?view=netframework-4.8
	private readonly int _genericParameterPosition;
	public int GenericParameterPosition =>
		IsGenericParameter ? _genericParameterPosition : throw new InvalidOperationException("The current type does not represent a type parameter");

	// For a generic type definition: the list of generic type parameters
	// For an open generic type: a mix of generic type parameters and generic type arguments
	// For a closed generic type: the list of generic type arguments
	private readonly TypeInfo[] _genericArguments = [];
	public TypeInfo[] GenericTypeParameters => IsGenericTypeDefinition ? _genericArguments : [];
	public TypeInfo[] GenericTypeArguments => !IsGenericTypeDefinition ? _genericArguments : [];

	// See: https://docs.microsoft.com/en-us/dotnet/api/system.type.getgenericarguments?view=netframework-4.8
	public TypeInfo[] GetGenericArguments() => _genericArguments;

	// True if an array, pointer or reference, otherwise false
	// See: https://docs.microsoft.com/en-us/dotnet/api/system.type.haselementtype?view=netframework-4.8
	public bool HasElementType => _elementType != null;

	private readonly TypeRef[] _implementedInterfaceReferences = [];
	public IEnumerable<TypeInfo> ImplementedInterfaces
	{
		get
		{
			if (Definition.IsValid)
			{
				return _implementedInterfaceReferences.Select(x => x.Value);
			}

			if (_genericTypeDefinition != null)
			{
				return _genericTypeDefinition.ImplementedInterfaces.Select(t => t.SubstituteGenericArguments(_genericArguments));
			}

			if (IsGenericParameter)
			{
				return GetGenericParameterConstraints().Where(t => t.IsInterface);
			}

			return [];
		}
	}

	// Get only interfaces not inherited from base interfaces
	public IEnumerable<TypeInfo> NonInheritedInterfaces =>
		ImplementedInterfaces.Except(ImplementedInterfaces.SelectMany(t => t.ImplementedInterfaces));
	public bool IsAbstract => (Attributes & TypeAttributes.Abstract) == TypeAttributes.Abstract;
	public bool IsArray { get; }
	public bool IsByRef { get; }
	public bool IsClass => (Attributes & TypeAttributes.ClassSemanticsMask) == TypeAttributes.Class;
	public bool IsEnum { get; }
	public bool IsGenericParameter { get; }
	public bool IsGenericType { get; }
	public bool IsGenericTypeDefinition => Definition.IsValid && _genericArguments.Any();
	public bool IsImport => (Attributes & TypeAttributes.Import) == TypeAttributes.Import;
	public bool IsInterface => (Attributes & TypeAttributes.ClassSemanticsMask) == TypeAttributes.Interface;
	public bool IsNested => (MemberType & MemberTypes.NestedType) == MemberTypes.NestedType;
	public bool IsNestedAssembly => (Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.NestedAssembly;
	public bool IsNestedFamANDAssem => (Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.NestedFamANDAssem;
	public bool IsNestedFamily => (Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.NestedFamily;
	public bool IsNestedFamORAssem => (Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.NestedFamORAssem;
	public bool IsNestedPrivate => (Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.NestedPrivate;
	public bool IsNestedPublic => (Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.NestedPublic;
	public bool IsNotPublic => (Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.NotPublic;
	public bool IsPointer { get; }

	// Primitive types table: https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/built-in-types-table (we exclude Object and String)
	public bool IsPrimitive =>
		Namespace == "System"
		&& new[]
		{
			"Boolean",
			"Byte",
			"SByte",
			"Int16",
			"UInt16",
			"Int32",
			"UInt32",
			"Int64",
			"UInt64",
			"IntPtr",
			"UIntPtr",
			"Char",
			"Decimal",
			"Double",
			"Single",
		}.Contains(Name);
	public bool IsPublic => (Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.Public;
	public bool IsSealed => (Attributes & TypeAttributes.Sealed) == TypeAttributes.Sealed;
	public bool IsSerializable => (Attributes & (TypeAttributes)0x2000) == (TypeAttributes)0x2000;
	public bool IsSpecialName => (Attributes & TypeAttributes.SpecialName) == TypeAttributes.SpecialName;

	// L-NOTE: This must not check BaseType, as this is not what dictates whether
	// a type is considered a "ValueType". System.Enum inherits from ValueType
	// but is considered (and treated as) a class everywhere, which would not be caught
	// by the aforementioned check
	public bool IsValueType => _genericTypeDefinition?.IsValueType ?? Definition.Bitfield.ValueType;

	// Helper function for determining if using this type as a field, parameter etc. requires that field or method to be declared as unsafe
	public bool RequiresUnsafeContext => IsPointer || (HasElementType && ElementType.RequiresUnsafeContext);
	public bool HasNoScope => IsGenericParameter || (HasElementType && ElementType.HasNoScope);

	// May get overridden by Il2CppType-based constructor below
	public override MemberTypes MemberType { get; } = MemberTypes.TypeInfo;

	private string _namespace = "";
	public string Namespace
	{
		get => !string.IsNullOrEmpty(_namespace) ? _namespace : DeclaringType?.Namespace ?? "";
		set => _namespace = value;
	}
	public bool HasInlineArray => Definition.Bitfield.HasInlineArray;
	public int InlineArrayLength => Assembly.Model.Package.TypeInlineArrays[Index];

	// Number of dimensions of an array
	private readonly int _arrayRank;

	public int GetArrayRank() => _arrayRank;

	public string[] GetEnumNames() =>
		IsEnum
			? [.. DeclaredFields.Where(x => x.Name != "value__").Select(x => x.Name)]
			: throw new InvalidOperationException("Type is not an enumeration");

	// The underlying type of an enumeration (int by default)
	private readonly TypeRef? _enumUnderlyingTypeReference;

	public TypeInfo GetEnumUnderlyingType()
	{
		if (!IsEnum)
		{
			throw new InvalidOperationException("This IL2CPP type is not an enum.");
		}

		return (_enumUnderlyingTypeReference ?? throw new InvalidDataException("IL2CPP enum has no underlying type.")).Value;
	}

	public Array GetEnumValues() =>
		IsEnum
			? DeclaredFields.Where(x => x.Name != "value__").Select(x => x.DefaultValue).ToArray()
			: throw new InvalidOperationException("Type is not an enumeration");

	// Initialize type from TypeDef using specified index in metadata
	public TypeInfo(int typeIndex, Assembly owner)
		: base(owner)
	{
		Il2CppInspector pkg = Assembly.Model.Package;
		_definitionIndex = typeIndex;
		_sizes = pkg.TypeDefinitionSizes[typeIndex];
		MetadataToken = (int)Definition.Token;
		Index = typeIndex;
		Namespace = Regex.Replace(pkg.Strings[Definition.NamespaceIndex], @"[^A-Za-z0-9_\-\.<>{}]", "");
		Name = pkg.Strings[Definition.NameIndex];
		// Nested type?
		if (Definition.DeclaringTypeIndex >= 0)
		{
			MemberType |= MemberTypes.NestedType;
		}

		// Add to global type definition list
		Assembly.Model.TypesByDefinitionIndex[Index] = this;
		// Generic type definition?
		if (Definition.GenericContainerIndex >= 0)
		{
			IsGenericType = true;
			// Store the generic type parameters for later instantiation
			Il2CppGenericContainer container = pkg.GenericContainers[Definition.GenericContainerIndex];
			_genericArguments =
			[
				.. Enumerable
					.Range((int)container.GenericParameterStart, container.TypeArgc)
					.Select(index => Assembly.Model.GetGenericParameterType(index)),
			];
			_genericTypeInstances = new Dictionary<TypeInfo[], TypeInfo>(new TypeArgumentsComparer()) { [_genericArguments] = this };
		}

		// TODO: Move this to after we've populated TypesByReferenceIndex, since FullName might touch that
		Assembly.Model.TypesByFullName[FullName ?? throw new InvalidDataException("IL2CPP definition has no full name.")] = this;
		// Copy attributes
		Attributes = (TypeAttributes)Definition.Flags;
		// Enumerations - bit 1 of bitfield indicates this (also the baseTypeReference will be System.Enum)
		if (Definition.Bitfield.EnumType)
		{
			IsEnum = true;
			int enumUnderlyingTypeIndex = Definition.GetEnumElementTypeIndex(Assembly.Model.Package.Version);
			_enumUnderlyingTypeReference = TypeRef.FromReferenceIndex(Assembly.Model, enumUnderlyingTypeIndex);
		}

		// Add all implemented interfaces
		_implementedInterfaceReferences = new TypeRef[Definition.InterfacesCount];
		for (int i = 0; i < Definition.InterfacesCount; i++)
		{
			_implementedInterfaceReferences[i] = TypeRef.FromReferenceIndex(
				Assembly.Model,
				pkg.InterfaceUsageIndices[Definition.InterfacesIndex + i]
			);
		}

		// Add all nested types
		_declaredNestedTypes = new TypeRef[Definition.NestedTypeCount];
		for (int n = 0; n < Definition.NestedTypeCount; n++)
		{
			_declaredNestedTypes[n] = TypeRef.FromDefinitionIndex(Assembly.Model, pkg.NestedTypeIndices[Definition.NestedTypeIndex + n]);
		}

		// Add all fields
		_declaredFields = [];
		for (FieldIndex f = Definition.FieldIndex; f < Definition.FieldIndex + Definition.FieldCount; f++)
		{
			_declaredFields.Add(new FieldInfo(pkg, f, this));
		}

		// Add all methods
		_declaredConstructors = [];
		_declaredMethods = [];
		for (MethodIndex m = Definition.MethodIndex; m < Definition.MethodIndex + Definition.MethodCount; m++)
		{
			MethodInfo method = new(pkg, m, this);
			if (method.Name == ConstructorInfo.ConstructorName || method.Name == ConstructorInfo.TypeConstructorName)
			{
				_declaredConstructors.Add(new ConstructorInfo(pkg, m, this));
			}
			else
			{
				_declaredMethods.Add(method);
			}
		}

		// Add all properties
		_declaredProperties = [];
		for (PropertyIndex p = Definition.PropertyIndex; p < Definition.PropertyIndex + Definition.PropertyCount; p++)
		{
			_declaredProperties.Add(new PropertyInfo(pkg, p, this));
		}

		// There are rare cases when explicitly implemented interface properties
		// are only given as methods in the metadata. Find these and add them as properties
		IEnumerable<MethodInfo> eip = DeclaredMethods
			.Where(m => m.Name.Contains(".get_") || m.Name.Contains(".set_"))
			.Except(DeclaredProperties.Select(p => p.GetMethod).OfType<MethodInfo>())
			.Except(DeclaredProperties.Select(p => p.SetMethod).OfType<MethodInfo>());
		// Build a paired list of getters and setters
		List<(MethodInfo? get, MethodInfo? set)> pairedEip = [];
		foreach (MethodInfo p in eip)
		{
			if (p.Name.Contains(".get_"))
			{
				(MethodInfo? get, MethodInfo? set) method = pairedEip.FirstOrDefault(pe =>
					pe.get is null && pe.set?.Name == p.Name.Replace(".get_", ".set_")
				);
				if (method.set is not null)
				{
					pairedEip.Remove(method);
				}

				pairedEip.Add((p, method.set));
			}

			if (p.Name.Contains(".set_"))
			{
				(MethodInfo? get, MethodInfo? set) method = pairedEip.FirstOrDefault(pe =>
					pe.set is null && pe.get?.Name == p.Name.Replace(".set_", ".get_")
				);
				if (method.get is not null)
				{
					pairedEip.Remove(method);
				}

				pairedEip.Add((method.get, p));
			}
		}

		foreach ((MethodInfo? get, MethodInfo? set) prop in pairedEip)
		{
			_declaredProperties.Add(new PropertyInfo(prop.get, prop.set, this));
		}

		// Add all events
		_declaredEvents = [];
		for (EventIndex e = Definition.EventIndex; e < Definition.EventIndex + Definition.EventCount; e++)
		{
			_declaredEvents.Add(new EventInfo(pkg, e, this));
		}
		// TODO: Events have the same edge case issue as properties above, eg. PoGo 0.35.0
	}

	// Initialize a type from a concrete generic instance
	private TypeInfo(TypeInfo genericTypeDef, TypeInfo[] genericArgs)
		: base(genericTypeDef.Assembly)
	{
		if (!genericTypeDef.IsGenericTypeDefinition)
		{
			throw new InvalidOperationException(genericTypeDef.Name + " is not a generic type definition.");
		}

		_genericTypeDefinition = genericTypeDef;
		// Same visibility attributes as generic type definition
		Attributes = _genericTypeDefinition.Attributes;
		// Even though this isn't a TypeDef, we have to set this so that DeclaringType works in later references
		Index = _genericTypeDefinition.Index;
		// Same name as generic type definition
		Namespace = _genericTypeDefinition.Namespace;
		Name = _genericTypeDefinition.BaseName; // use BaseName to exclude the type parameters so we can supply our own
		MemberType = _genericTypeDefinition.MemberType;
		IsGenericType = true;
		_genericArguments = genericArgs;
		// Nested enums in a generic declaring type inherit its generic
		// context, but retain their scalar enum identity and storage type.
		IsEnum = _genericTypeDefinition.IsEnum;
		_enumUnderlyingTypeReference = _genericTypeDefinition._enumUnderlyingTypeReference;
		if (IsEnum)
		{
			_sizes = _genericTypeDefinition.Sizes;
		}
	}

	// Substitutes the elements of an array of types for the type parameters of the current generic type definition
	// and returns a TypeInfo object representing the resulting constructed type.
	// See: https://docs.microsoft.com/en-us/dotnet/api/system.type.makegenerictype?view=netframework-4.8
	public TypeInfo MakeGenericType(params TypeInfo[] typeArguments)
	{
		if (typeArguments.Length != _genericArguments.Length)
		{
			throw new ArgumentException("The number of generic arguments provided does not match the generic type definition.");
		}

		if (_genericTypeInstances.TryGetValue(typeArguments, out TypeInfo? result))
		{
			return result;
		}

		result = new TypeInfo(this, typeArguments);
		_genericTypeInstances[typeArguments] = result;
		return result;
	}

	public TypeInfo SubstituteGenericArguments(TypeInfo[] typeArguments, TypeInfo[]? methodArguments = null)
	{
		if (!ContainsGenericParameters)
		{
			return this;
		}

		if (IsGenericTypeParameter)
		{
			return typeArguments[GenericParameterPosition];
		}
		else if (IsGenericMethodParameter)
		{
			return (methodArguments ?? throw new ArgumentException("Method type arguments are required.", nameof(methodArguments)))[
				GenericParameterPosition
			];
		}
		else if (IsGenericTypeDefinition)
		{
			return MakeGenericType(typeArguments);
		}
		else if (HasElementType)
		{
			TypeInfo elementType = ElementType.SubstituteGenericArguments(typeArguments, methodArguments);
			if (IsArray)
			{
				return elementType.MakeArrayType(GetArrayRank());
			}
			else if (IsByRef)
			{
				return elementType.MakeByRefType();
			}
			else if (IsPointer)
			{
				return elementType.MakePointerType();
			}

			throw new InvalidOperationException("TypeInfo element type state is invalid!");
		}
		else
		{
			IEnumerable<TypeInfo> newGenericArguments = _genericArguments.Select(x => x.SubstituteGenericArguments(typeArguments, methodArguments));
			return GetGenericTypeDefinition().MakeGenericType([.. newGenericArguments]);
		}
	}

	// Initialize a type that is a generic parameter of a generic type
	// See: https://docs.microsoft.com/en-us/dotnet/api/system.type.isgenerictype?view=netframework-4.8
	public TypeInfo(TypeInfo declaringType, Il2CppGenericParameter param)
		: base(declaringType)
	{
		// Same visibility attributes as declaring type
		Attributes = declaringType.Attributes;
		// Same namespace as declaring type
		Namespace = declaringType.Namespace;
		// Special constraints
		GenericParameterAttributes = (GenericParameterAttributes)param.Flags;
		// Type constraints
		_genericParameterConstraints = new TypeRef[param.ConstraintsCount];
		for (int c = 0; c < param.ConstraintsCount; c++)
		{
			_genericParameterConstraints[c] = TypeRef.FromReferenceIndex(
				Assembly.Model,
				Assembly.Model.Package.GenericConstraintIndices[param.ConstraintsStart + c]
			);
		}

		// Name of parameter
		Name = Assembly.Model.Package.Strings[param.NameIndex];
		// Position
		_genericParameterPosition = param.Num;
		IsGenericParameter = true;
	}

	// Initialize a type that is a generic parameter of a generic method
	public TypeInfo(MethodBase declaringMethod, Il2CppGenericParameter param)
		: this(declaringMethod.DeclaringType, param) => DeclaringMethod = declaringMethod;

	// Initialize a type that is an array of the specified type
	private TypeInfo(TypeInfo elementType, int rank)
		: base(elementType.Assembly)
	{
		_elementType = elementType;
		IsArray = true;
		Namespace = ElementType.Namespace;
		Name = ElementType.Name;
		_arrayRank = rank;
	}

	// Initialize a type that is a reference or pointer to the specified type
	private TypeInfo(TypeInfo underlyingType, bool isPointer)
		: base(underlyingType.Assembly)
	{
		_elementType = underlyingType;
		if (isPointer)
		{
			IsPointer = true;
		}
		else
		{
			IsByRef = true;
		}

		Namespace = ElementType.Namespace;
		Name = ElementType.Name;
	}

	public TypeInfo MakeArrayType(int rank = 1)
	{
		_generatedArrayTypes ??= [];
		if (_generatedArrayTypes.TryGetValue(rank, out TypeInfo? type))
		{
			return type;
		}

		type = new TypeInfo(this, rank);
		_generatedArrayTypes[rank] = type;
		return type;
	}

	public TypeInfo MakeByRefType() => _generatedByRefType ??= new TypeInfo(this, isPointer: false);

	public TypeInfo MakePointerType() => _generatedPointerType ??= new TypeInfo(this, isPointer: true);

	// Get all the other types directly referenced by this type (single level depth; no recursion)
	public List<TypeInfo> GetAllTypeReferences()
	{
		HashSet<TypeInfo>? refs = [];
		// Fixed attributes
		if (IsImport)
		{
			refs.Add(Assembly.Model.TypesByFullName["System.Runtime.InteropServices.ComVisibleAttribute"]);
		}

		if (IsSerializable)
		{
			refs.Add(Assembly.Model.TypesByFullName["System.SerializableAttribute"]);
		}

		// Constructor, event, field, method, nested type, property attributes
		IEnumerable<CustomAttributeData> attrs = DeclaredMembers.SelectMany(m => m.CustomAttributes);
		refs.UnionWith(attrs.SelectMany(a => a.GetAllTypeReferences()));
		// Events
		refs.UnionWith(DeclaredEvents.Select(e => e.EventHandlerType));
		// Fields
		refs.UnionWith(DeclaredFields.Select(f => f.FieldType));
		// Properties (return type of getters or argument type of setters)
		refs.UnionWith(DeclaredProperties.Select(p => p.PropertyType));
		// Nested types
		refs.UnionWith(DeclaredNestedTypes);
		refs.UnionWith(DeclaredNestedTypes.SelectMany(n => n.GetAllTypeReferences()));
		// Constructors
		refs.UnionWith(DeclaredConstructors.SelectMany(m => m.DeclaredParameters).Select(p => p.ParameterType));
		// Methods (includes event add/remove/raise, property get/set methods and extension methods)
		refs.UnionWith(DeclaredMethods.Select(m => m.ReturnParameter.ParameterType));
		refs.UnionWith(DeclaredMethods.SelectMany(m => m.DeclaredParameters).Select(p => p.ParameterType));
		// Method generic type parameters and constraints
		refs.UnionWith(DeclaredMethods.SelectMany(m => m.GetGenericArguments()));
		refs.UnionWith(DeclaredMethods.SelectMany(m => m.GetGenericArguments()).SelectMany(p => p.GetGenericParameterConstraints()));
		// Type declaration attributes
		refs.UnionWith(CustomAttributes.SelectMany(a => a.GetAllTypeReferences()));
		// Parent type
		if (BaseType != null)
		{
			refs.Add(BaseType);
		}

		// Declaring type
		if (DeclaringType != null)
		{
			refs.Add(DeclaringType);
		}

		// Element type
		if (HasElementType)
		{
			refs.Add(ElementType);
		}

		// Enum type
		if (IsEnum)
		{
			refs.Add(GetEnumUnderlyingType());
		}

		// Generic type definition
		if (_genericTypeDefinition != null)
		{
			refs.Add(_genericTypeDefinition);
		}

		// Generic type parameters and constraints
		refs.UnionWith(GetGenericArguments());
		refs.UnionWith(GetGenericParameterConstraints());
		// Generic type constraints of type parameters in generic type definition
		refs.UnionWith(GenericTypeParameters.SelectMany(p => p.GetGenericParameterConstraints()));
		// Implemented interfaces
		refs.UnionWith(ImplementedInterfaces);
		// Repeatedly replace arrays, pointers and references with their element types
		while (refs.Any(r => r.HasElementType))
		{
			refs = [.. refs.Select(r => r.HasElementType ? r.ElementType : r)];
		}

		// Type arguments in generic types that may have been a field, method parameter etc.
		IEnumerable<TypeInfo> genericArguments = [.. refs];
		do
		{
			genericArguments = genericArguments.SelectMany(r => r.GetGenericArguments());
			refs.UnionWith(genericArguments);
		} while (genericArguments.Any());
		// Remove anonymous types
		refs.RemoveWhere(r => string.IsNullOrEmpty(r.FullName));
		IEnumerable<TypeInfo> refList = refs;
		// Eliminated named duplicates (the HashSet removes instance duplicates)
		refList = refList.GroupBy(r => r.FullName).Select(p => p.First());
		// Remove System.Object
		refList = refList.Where(r => r.FullName != "System.Object");
		return [.. refList];
	}

	public string GetAccessModifierString() =>
		this switch
		{
			{ IsPublic: true } => "public ",
			{ IsNotPublic: true } => "internal ",
			{ IsNestedPublic: true } => "public ",
			{ IsNestedPrivate: true } => "private ",
			{ IsNestedFamily: true } => "protected ",
			{ IsNestedAssembly: true } => "internal ",
			{ IsNestedFamORAssem: true } => "protected internal ",
			{ IsNestedFamANDAssem: true } => "private protected ",
			_ => throw new InvalidOperationException("Unknown type access modifier"),
		};

	public string GetModifierString()
	{
		StringBuilder modifiers = new(GetAccessModifierString());
		switch (this)
		{
			// An abstract sealed class is a static class
			case { IsAbstract: true, IsSealed: true }:
				modifiers.Append("static ");
				break;
			case { IsAbstract: true, IsInterface: false }:
				modifiers.Append("abstract ");
				break;
			case { IsSealed: true, IsValueType: false, IsEnum: false }:
				modifiers.Append("sealed ");
				break;
		}

		switch (this)
		{
			// IsEnum needs to be checked before IsValueType,
			// as enums are both enums and ValueTypes
			case { IsEnum: true }:
				modifiers.Append("enum ");
				break;
			case { IsValueType: true }:
				modifiers.Append("struct ");
				break;
			case { IsInterface: true }:
				modifiers.Append("interface ");
				break;
			default:
				modifiers.Append("class ");
				break;
		}

		return modifiers.ToString();
	}

	public string GetTypeConstraintsString(Scope scope)
	{
		if (!IsGenericParameter)
		{
			return string.Empty;
		}

		TypeInfo[] typeConstraints = GetGenericParameterConstraints();
		if (GenericParameterAttributes == GenericParameterAttributes.None && typeConstraints.Length == 0)
		{
			return string.Empty;
		}

		// Check if we are in a nested type, and if so, exclude ourselves if we are a generic type parameter from the outer type
		// All constraints are inherited automatically by all nested types so we only have to look at the immediate outer type
		if (
			DeclaringMethod == null
			&& DeclaringType is { IsNested: true, DeclaringType: { } parentType }
			&& parentType.GetGenericArguments().Any(p => p.Name == Name)
		)
		{
			return string.Empty;
		}

		// Check if we are in an overriding method, and if so, exclude ourselves if we are a generic type parameter from the base method
		// All constraints are inherited automatically by all overriding methods so we only have to look at the immediate base method
		if (
			DeclaringMethod != null
			&& DeclaringMethod.IsVirtual
			&& !DeclaringMethod.IsAbstract
			&& !DeclaringMethod.IsFinal
			&& (DeclaringMethod.Attributes & MethodAttributes.VtableLayoutMask) == MethodAttributes.ReuseSlot
		)
		{
			// Find nearest ancestor base method which has us as a generic type parameter
			MethodInfo? method = (DeclaringMethod.DeclaringType.BaseType?.GetAllMethods() ?? []).FirstOrDefault(m =>
				m.IsHideBySig && m.IsVirtual && DeclaringMethod.SignatureEquals(m) && m.GetGenericArguments().Any(p => p.Name == Name)
			);
			// Stop if we are inherited from a base method
			if (method != null)
			{
				return string.Empty;
			}
		}

		List<string> constraintList = [.. typeConstraints.Where(c => c.FullName != "System.ValueType").Select(c => c.GetScopedCSharpName(scope))];
		// struct or class must be the first constraint specified
		if (
			(GenericParameterAttributes & GenericParameterAttributes.NotNullableValueTypeConstraint)
			== GenericParameterAttributes.NotNullableValueTypeConstraint
		)
		{
			constraintList.Insert(0, "struct");
		}

		if ((GenericParameterAttributes & GenericParameterAttributes.ReferenceTypeConstraint) == GenericParameterAttributes.ReferenceTypeConstraint)
		{
			constraintList.Insert(0, "class");
		}

		if (
			(GenericParameterAttributes & GenericParameterAttributes.DefaultConstructorConstraint)
				== GenericParameterAttributes.DefaultConstructorConstraint
			&& !constraintList.Contains("struct")
		)
		// new() must be the last constraint specified
		{
			constraintList.Add("new()");
		}

		// Covariance/contravariance constraints can lead to an empty constraint list
		if (!constraintList.Any())
		{
			return string.Empty;
		}

		return "where " + Name + " : " + string.Join(", ", constraintList);
	}

	public override string ToString() => Name;
}
