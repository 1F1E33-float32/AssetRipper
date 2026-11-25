using AsmResolver;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.DotNet.Metadata.Tables;
using AssetRipper.Il2CppInspector;
using AttributeArgument = AssetRipper.Il2CppInspector.CustomAttributeArgument;
using CustomAttributeArgument = AsmResolver.DotNet.Signatures.CustomAttributeArgument;
using Model = AssetRipper.Il2CppInspector.Reflection;

namespace AssetRipper.Import.Structure.Assembly.Managers;

/// <summary>
/// Adapts decoded IL2CPP metadata directly to the objects consumed by FieldSerializer and script export.
/// No managed binary is written or parsed during import.
/// </summary>
internal sealed class Il2CppTypeModelConverter(Model.TypeModel model, Action<OperationProgress>? progress = null)
{
	private readonly Dictionary<Model.Assembly, AssemblyDefinition> assemblies = [];
	private readonly Dictionary<Model.TypeInfo, TypeDefinition> types = [];
	private readonly Dictionary<Model.MethodBase, MethodDefinition> methods = [];
	private readonly Dictionary<(ModuleDefinition, Model.TypeInfo), TypeSignature> signatures = [];

	public IReadOnlyList<AssemblyDefinition> Convert()
	{
		foreach (Model.Assembly source in model.Assemblies)
		{
			var name = source.AssemblyDefinition.Aname;
			Version version = new(Math.Max(0, name.Major), Math.Max(0, name.Minor), Math.Max(0, name.Build), Math.Max(0, name.Revision));
			assemblies.Add(
				source,
				new AssemblyDefinition(model.Package.Strings[name.NameIndex], version) { Culture = model.Package.Strings[name.CultureIndex] }
			);
		}
		AssemblyDefinition core = assemblies[model.TypesByFullName["System.Object"].Assembly];
		foreach ((Model.Assembly source, AssemblyDefinition target) in assemblies)
		{
			ModuleDefinition module = new(source.ShortName, new AssemblyReference(core));
			target.Modules.Add(module);
			module.GetOrCreateModuleType();
		}
		RuntimeContext context = new(DotNetRuntimeInfo.NetCoreApp(10, 0), (bool?)null, core);
		foreach (AssemblyDefinition assembly in assemblies.Values)
			context.AddAssembly(assembly);

		// Register identities before resolving bases, fields, generic constraints or attribute constructors.
		foreach (Model.TypeInfo source in model.TypesByDefinitionIndex.Where(t => t.Name != "<Module>"))
			types.Add(source, new TypeDefinition(source.IsNested ? null : source.Namespace, source.BaseName, (TypeAttributes)source.Attributes));
		foreach ((Model.TypeInfo source, TypeDefinition target) in types)
		{
			if (source.DeclaringType is { } parent)
				types[parent].NestedTypes.Add(target);
			else
				assemblies[source.Assembly].ManifestModule!.TopLevelTypes.Add(target);
			foreach (Model.TypeInfo parameter in source.GenericTypeParameters)
				target.GenericParameters.Add(new GenericParameter(parameter.Name, (GenericParameterAttributes)parameter.GenericParameterAttributes));
		}

		int completed = 0;
		foreach ((Model.TypeInfo source, TypeDefinition target) in types)
		{
			model.Package.BinaryImage.CancellationToken.ThrowIfCancellationRequested();
			PopulateType(source, target);
			if (++completed % 1000 == 0)
				progress?.Invoke(new("Building managed type model", completed, types.Count, source.FullName));
		}

		foreach ((Model.Assembly source, AssemblyDefinition target) in assemblies)
			AddAttributes(target.ManifestModule!, target, source.CustomAttributes);
		foreach ((Model.TypeInfo source, TypeDefinition target) in types)
		{
			ModuleDefinition module = target.DeclaringModule!;
			AddAttributes(module, target, source.CustomAttributes);
			for (int i = 0; i < source.DeclaredFields.Count; i++)
				AddAttributes(module, target.Fields[i], source.DeclaredFields[i].CustomAttributes);
			AddPropertiesAndEvents(module, source, target);
			AddOverrides(module, source, target);
		}
		foreach ((Model.MethodBase source, MethodDefinition target) in methods)
		{
			ModuleDefinition module = target.DeclaringModule!;
			AddAttributes(module, target, source.CustomAttributes);
			for (int i = 0; i < source.DeclaredParameters.Count; i++)
				AddAttributes(module, target.ParameterDefinitions[i], source.DeclaredParameters[i].CustomAttributes);
			if (source is Model.MethodInfo method && source.Definition.ReturnParameterToken != 0)
			{
				ParameterDefinition returnParameter = new(0, null, 0);
				target.ParameterDefinitions.Add(returnParameter);
				AddAttributes(module, returnParameter, method.ReturnParameter.CustomAttributes);
			}
		}
		progress?.Invoke(new("Building managed type model", types.Count, types.Count));
		return assemblies.Values.ToArray();
	}

	private void PopulateType(Model.TypeInfo source, TypeDefinition target)
	{
		ModuleDefinition module = target.DeclaringModule!;
		if (source.BaseType is { } baseType)
			target.BaseType = Signature(module, baseType).ToTypeDefOrRef();
		foreach (Model.TypeInfo contract in source.ImplementedInterfaces)
			target.Interfaces.Add(new InterfaceImplementation(Signature(module, contract).ToTypeDefOrRef()));
		AddConstraints(module, target.GenericParameters, source.GenericTypeParameters);
		if (target.IsExplicitLayout || target.IsSequentialLayout)
		{
			int packing = (int)source.Definition.Bitfield.PackingSize;
			int size = source.Sizes.NativeSize;
			if (model.Package.Metadata.HasGameAdapter)
				size = checked((int)source.Sizes.InstanceSize - model.Package.BinaryImage.Bits / 8 * 2);
			target.ClassLayout = new ClassLayout(
				(ushort)(source.Definition.Bitfield.DefaultPackingSize || packing == 0 ? 0 : 1 << (packing - 1)),
				(uint)Math.Max(0, size)
			);
		}
		foreach (Model.FieldInfo field in source.DeclaredFields)
		{
			FieldDefinition result = new(field.Name, (FieldAttributes)field.Attributes, new FieldSignature(Signature(module, field.FieldType)));
			if (field.HasDefaultValue)
				result.Constant = MakeConstant(field.DefaultValue);
			if (field.HasFieldRVA)
			{
				int size =
					model.Package.Metadata.GameAdapter?.FieldRvaSize(
						model.Package.TypeReferences[model.Package.Fields[field.Index].TypeIndex],
						model.Package.Binary
					) ?? field.FieldType.Sizes.NativeSize;
				if (size < 0)
					throw new InvalidDataException($"Cannot determine RVA field size: {source.FullName}.{field.Name}");
				result.FieldRva = new DataSegment(model.Package.Metadata.ReadBytes(checked((long)field.DefaultValueMetadataAddress), size));
			}
			if (target.IsExplicitLayout && !field.IsStatic)
				result.FieldOffset = checked((int)field.Offset);
			target.Fields.Add(result);
		}
		foreach (Model.MethodBase method in source.DeclaredConstructors.Cast<Model.MethodBase>().Concat(source.DeclaredMethods))
		{
			MethodDefinition result = new(method.Name, (MethodAttributes)method.Attributes, MethodSignature(module, method));
			target.Methods.Add(result);
			methods.Add(method, result);
			foreach (Model.TypeInfo parameter in method.GetGenericArguments())
				result.GenericParameters.Add(new GenericParameter(parameter.Name, (GenericParameterAttributes)parameter.GenericParameterAttributes));
			AddConstraints(module, result.GenericParameters, method.GetGenericArguments());
			foreach (Model.ParameterInfo parameter in method.DeclaredParameters)
			{
				ParameterDefinition definition = new(
					checked((ushort)(parameter.Position + 1)),
					parameter.Name,
					(ParameterAttributes)parameter.Attributes
				);
				if (parameter.HasDefaultValue)
					definition.Constant = MakeConstant(parameter.DefaultValue);
				result.ParameterDefinitions.Add(definition);
			}
			// Native code cannot become CIL; valid stub bodies support the existing optional script export path.
			if (!method.IsAbstract && source.BaseType?.FullName != "System.MulticastDelegate")
			{
				result.IsPInvokeImpl = false;
				result.CilMethodBody = new CilMethodBody();
				result.CilMethodBody.Instructions.Add(CilOpCodes.Ldnull);
				result.CilMethodBody.Instructions.Add(CilOpCodes.Throw);
			}
			else if (!method.IsAbstract)
				result.ImplAttributes = MethodImplAttributes.Runtime;
		}
	}

	private void AddPropertiesAndEvents(ModuleDefinition module, Model.TypeInfo source, TypeDefinition target)
	{
		foreach (Model.PropertyInfo property in source.DeclaredProperties)
		{
			Model.MethodInfo? accessor = property.GetMethod ?? property.SetMethod;
			if (accessor is null)
				continue;
			TypeSignature[] parameters = property.GetMethod is { } getter
				? getter.DeclaredParameters.Select(p => Signature(module, p.ParameterType)).ToArray()
				: property.SetMethod!.DeclaredParameters.SkipLast(1).Select(p => Signature(module, p.ParameterType)).ToArray();
			PropertySignature signature = accessor.IsStatic
				? PropertySignature.CreateStatic(Signature(module, property.PropertyType), parameters)
				: PropertySignature.CreateInstance(Signature(module, property.PropertyType), parameters);
			PropertyDefinition result = new(property.Name, (PropertyAttributes)property.Attributes, signature);
			target.Properties.Add(result);
			result.SetSemanticMethods(Method(property.GetMethod), Method(property.SetMethod));
			AddAttributes(module, result, property.CustomAttributes);
		}
		foreach (Model.EventInfo evt in source.DeclaredEvents)
		{
			EventDefinition result = new(evt.Name, (EventAttributes)evt.Attributes, Signature(module, evt.EventHandlerType).ToTypeDefOrRef());
			target.Events.Add(result);
			result.SetSemanticMethods(Method(evt.AddMethod), Method(evt.RemoveMethod), Method(evt.RaiseMethod));
			AddAttributes(module, result, evt.CustomAttributes);
		}
		MethodDefinition? Method(Model.MethodBase? method) => method is null ? null : methods[method.RootDefinition];
	}

	private void AddConstraints(ModuleDefinition module, IList<GenericParameter> target, Model.TypeInfo[] source)
	{
		for (int i = 0; i < source.Length; i++)
			foreach (Model.TypeInfo constraint in source[i].GetGenericParameterConstraints())
				target[i].Constraints.Add(new GenericParameterConstraint(Signature(module, constraint).ToTypeDefOrRef()));
	}

	private MethodSignature MethodSignature(ModuleDefinition module, Model.MethodBase method)
	{
		TypeSignature returnType = method is Model.MethodInfo info ? Signature(module, info.ReturnType) : module.CorLibTypeFactory.Void;
		IEnumerable<TypeSignature> parameters = method.DeclaredParameters.Select(p => Signature(module, p.ParameterType));
		int arity = method.GetGenericArguments().Length;
		return method.IsStatic
			? AsmResolver.DotNet.Signatures.MethodSignature.CreateStatic(returnType, arity, parameters)
			: AsmResolver.DotNet.Signatures.MethodSignature.CreateInstance(returnType, arity, parameters);
	}

	private TypeSignature Signature(ModuleDefinition module, Model.TypeInfo source)
	{
		if (signatures.TryGetValue((module, source), out TypeSignature? result))
			return result;
		result = MakeSignature();
		signatures.Add((module, source), result);
		return result;

		TypeSignature MakeSignature()
		{
			if (source.IsGenericParameter)
				return new GenericParameterSignature(
					module,
					source.IsGenericMethodParameter ? GenericParameterType.Method : GenericParameterType.Type,
					source.GenericParameterPosition
				);
			if (source.IsArray)
				return source.GetArrayRank() == 1
					? new SzArrayTypeSignature(Signature(module, source.ElementType!))
					: new ArrayTypeSignature(Signature(module, source.ElementType!), source.GetArrayRank());
			if (source.IsByRef)
				return new ByReferenceTypeSignature(Signature(module, source.ElementType!));
			if (source.IsPointer)
				return new PointerTypeSignature(Signature(module, source.ElementType!));
			if (
				(source.IsPrimitive || source.FullName is "System.String" or "System.Object")
				&& module.CorLibTypeFactory.FromName(source.Namespace, source.Name) is { } primitive
			)
				return primitive;
			Model.TypeInfo definition = source.IsGenericType ? source.GetGenericTypeDefinition() : source;
			ITypeDefOrRef type = module.DefaultImporter.ImportType(types[definition]);
			return source.IsGenericType
				? new GenericInstanceTypeSignature(type, source.IsValueType, source.GetGenericArguments().Select(t => Signature(module, t)))
				: new TypeDefOrRefSignature(type, source.IsValueType);
		}
	}

	private void AddAttributes(ModuleDefinition module, IHasCustomAttribute target, IEnumerable<Model.CustomAttributeData> attributes)
	{
		foreach (Model.CustomAttributeData attribute in attributes)
		{
			CustomAttributeCtor? data = attribute.CtorInfo;
			Model.MethodBase? constructor =
				data?.Ctor ?? attribute.AttributeType.DeclaredConstructors.FirstOrDefault(c => !c.IsStatic && c.DeclaredParameters.Count == 0);
			// Before metadata v29, constructor arguments are native code and cannot be inferred.
			if (constructor is null)
				continue;
			CustomAttributeSignature signature = new();
			if (data is not null)
			{
				for (int i = 0; i < data.Arguments.Length; i++)
					signature.FixedArguments.Add(Argument(data.Arguments[i], constructor.DeclaredParameters[i].ParameterType));
				foreach (CustomAttributeFieldArgument field in data.Fields)
					signature.NamedArguments.Add(
						new CustomAttributeNamedArgument(
							CustomAttributeArgumentMemberType.Field,
							field.Field.Name,
							Signature(module, field.Field.FieldType),
							Argument(field, field.Field.FieldType)
						)
					);
				foreach (CustomAttributePropertyArgument property in data.Properties)
					signature.NamedArguments.Add(
						new CustomAttributeNamedArgument(
							CustomAttributeArgumentMemberType.Property,
							property.Property.Name,
							Signature(module, property.Property.PropertyType),
							Argument(property, property.Property.PropertyType)
						)
					);
			}
			target.CustomAttributes.Add(new CustomAttribute(module.DefaultImporter.ImportMethod(methods[constructor.RootDefinition]), signature));
		}

		CustomAttributeArgument Argument(AttributeArgument argument, Model.TypeInfo expected)
		{
			TypeSignature type = Signature(module, expected);
			if (argument.Value is AttributeArgument[] elements)
				return new CustomAttributeArgument(type, elements.Select(Value));
			object? value = Value(argument);
			if (expected.FullName == "System.Object")
				value = new BoxedArgument(Signature(module, argument.Type), value);
			return new CustomAttributeArgument(type, value);
		}
		object? Value(AttributeArgument argument) =>
			argument.Value switch
			{
				Model.TypeInfo { IsGenericTypeDefinition: true } type => new TypeDefOrRefSignature(
					module.DefaultImporter.ImportType(types[type]),
					type.IsValueType
				),
				Model.TypeInfo type => Signature(module, type),
				AttributeArgument nested => new BoxedArgument(Signature(module, nested.Type), Value(nested)),
				_ => argument.Value,
			};
	}

	private void AddOverrides(ModuleDefinition module, Model.TypeInfo source, TypeDefinition target)
	{
		if (source.IsInterface)
			return;
		Model.MethodBase[] vtable = source.GetVTable();
		HashSet<(MethodDefinition, Model.TypeInfo, Model.MethodBase)> seen = [];
		if (source.BaseType is { } baseType)
		{
			Model.MethodBase[] inherited = baseType.GetVTable();
			for (int i = 0; i < Math.Min(vtable.Length, inherited.Length); i++)
				Add(vtable[i], inherited[i]);
		}
		for (int i = 0; i < source.Definition.InterfaceOffsetsCount; i++)
		{
			var offset = model.Package.InterfaceOffsets[source.Definition.InterfaceOffsetsStart + i];
			Model.TypeInfo? contract = model.TypesByReferenceIndex[offset.InterfaceTypeIndex];
			if (contract is null || offset.Offset < 0)
				continue;
			foreach (Model.MethodInfo method in contract.DeclaredMethods)
			{
				int slot = offset.Offset + method.RootDefinition.Definition.Slot;
				if ((uint)slot < vtable.Length)
					Add(vtable[slot], method);
			}
		}

		void Add(Model.MethodBase? implementation, Model.MethodBase? declaration)
		{
			if (
				implementation is null
				|| declaration is null
				|| implementation.RootDefinition == declaration.RootDefinition
				|| implementation.RootDefinition.DeclaringType != source
				|| !methods.TryGetValue(implementation.RootDefinition, out MethodDefinition? body)
				|| !seen.Add((body, declaration.DeclaringType, declaration.RootDefinition))
			)
				return;
			MemberReference reference = new(
				Signature(module, declaration.DeclaringType).ToTypeDefOrRef(),
				declaration.Name,
				MethodSignature(module, declaration.RootDefinition)
			);
			target.MethodImplementations.Add(new MethodImplementation(reference, body));
		}
	}

	private static Constant MakeConstant(object? value) =>
		value switch
		{
			null => new Constant(ElementType.Class, new DataBlobSignature(new byte[4])),
			bool v => Constant.FromValue(v),
			char v => Constant.FromValue(v),
			sbyte v => Constant.FromValue(v),
			byte v => Constant.FromValue(v),
			short v => Constant.FromValue(v),
			ushort v => Constant.FromValue(v),
			int v => Constant.FromValue(v),
			uint v => Constant.FromValue(v),
			long v => Constant.FromValue(v),
			ulong v => Constant.FromValue(v),
			float v => Constant.FromValue(v),
			double v => Constant.FromValue(v),
			string v => Constant.FromValue(v),
			_ => throw new InvalidDataException($"Unsupported IL2CPP constant: {value.GetType()}"),
		};
}
