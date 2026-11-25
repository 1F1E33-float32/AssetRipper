/*
	Copyright 2017-2021 Katy Coe - http://www.djkaty.com - https://github.com/djkaty
	Copyright 2020 Robert Xiao - https://robertxiao.ca

	All rights reserved.
*/
using System.Reflection;
using System.Text;
using AssetRipper.Il2CppInspector.Next.Metadata;

namespace AssetRipper.Il2CppInspector.Reflection;

public abstract class MethodBase : MemberInfo
{
	public override TypeInfo DeclaringType => base.DeclaringType ?? throw new InvalidOperationException("Member has no declaring type.");

	// IL2CPP-specific data
	private readonly int _definitionIndex = -1;
	public Il2CppMethodDefinition Definition => _definitionIndex < 0 ? default : Assembly.Model.Package.Methods[_definitionIndex];
	public int Index { get; }
	public (ulong Start, ulong End)? VirtualAddress { get; set; }

	// This dictionary will cache all instantiated generic methods.
	// Only valid for GenericMethodDefinition - not valid on instantiated types!
	private Dictionary<TypeInfo[], MethodBase>? _genericMethodInstances;

	// Root method definition: the method with Definition != null
	protected readonly MethodBase _rootDefinition;

	// Method.Invoke implementation
	public MethodInvoker? Invoker { get; set; }

	// Information/flags about the method
	public MethodAttributes Attributes { get; protected set; }
	public MethodImplAttributes MethodImplementationFlags { get; protected set; }

	// Custom attributes for this member
	public override IEnumerable<CustomAttributeData> CustomAttributes => CustomAttributeData.GetCustomAttributes(_rootDefinition);

	private List<ParameterInfo>? _declaredParameters;
	public List<ParameterInfo> DeclaredParameters
	{
		get
		{
			if (_declaredParameters != null)
			{
				return _declaredParameters;
			}

			if (Definition.IsValid)
			{
				List<ParameterInfo> parameters = new(Definition.ParameterCount);
				for (ParameterIndex p = Definition.ParameterStart; p < Definition.ParameterStart + Definition.ParameterCount; p++)
				{
					parameters.Add(new ParameterInfo(Assembly.Model.Package, p, this));
				}

				return _declaredParameters = parameters;
			}

			return _declaredParameters = [
				.. _rootDefinition.DeclaredParameters.Select(p =>
					p.SubstituteGenericArguments(this, DeclaringType.GetGenericArguments(), _genericArguments)
				),
			];
		}
	}

	internal virtual void ReleaseTransientParameters()
	{
		if (Assembly.Model.RetainMethodParameters)
		{
			return;
		}

		_declaredParameters = null;
		if (_rootDefinition != this)
		{
			_rootDefinition.ReleaseTransientParameters();
		}
	}

	public bool IsAbstract => (Attributes & MethodAttributes.Abstract) == MethodAttributes.Abstract;
	public bool IsAssembly => (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Assembly;
	public bool IsConstructor => MemberType == MemberTypes.Constructor;
	public bool IsFamily => (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Family;
	public bool IsFamilyAndAssembly => (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.FamANDAssem;
	public bool IsFamilyOrAssembly => (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.FamORAssem;
	public bool IsFinal => (Attributes & MethodAttributes.Final) == MethodAttributes.Final;
	public bool IsHideBySig => (Attributes & MethodAttributes.HideBySig) == MethodAttributes.HideBySig;
	public bool IsPrivate => (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Private;
	public bool IsPublic => (Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public;
	public bool IsSpecialName => (Attributes & MethodAttributes.SpecialName) == MethodAttributes.SpecialName;
	public bool IsStatic => (Attributes & MethodAttributes.Static) == MethodAttributes.Static;
	public bool IsVirtual => (Attributes & MethodAttributes.Virtual) == MethodAttributes.Virtual;
	public virtual bool RequiresUnsafeContext => DeclaredParameters.Any(p => p.ParameterType.RequiresUnsafeContext);

	// True if the method contains unresolved generic type parameters, or if it is a non-generic method in an open generic type
	// See: https://docs.microsoft.com/en-us/dotnet/api/system.reflection.methodbase.containsgenericparameters?view=netframework-4.8
	public bool ContainsGenericParameters => DeclaringType.ContainsGenericParameters || _genericArguments.Any(ga => ga.ContainsGenericParameters);

	// For a generic method definition: the list of generic type parameters
	// For an open generic method: a mix of generic type parameters and generic type arguments
	// For a closed generic method: the list of generic type arguments
	private readonly TypeInfo[] _genericArguments = [];

	// See: https://docs.microsoft.com/en-us/dotnet/api/system.reflection.methodbase.getgenericarguments?view=netframework-4.8
	public TypeInfo[] GetGenericArguments() => _genericArguments;

	// This was added in .NET Core 2.1 and isn't properly documented yet
	public bool IsConstructedGenericMethod => IsGenericMethod && !IsGenericMethodDefinition;

	// Generic method definition: either a method with Definition != null, or an open method of a generic type
	private readonly MethodBase? _genericMethodDefinition;

	public MethodBase GetGenericMethodDefinition()
	{
		if (_genericMethodDefinition != null)
		{
			return _genericMethodDefinition;
		}

		if (_genericArguments.Any())
		{
			return this;
		}

		throw new InvalidOperationException("This method can only be called on generic methods");
	}

	public MethodBase RootDefinition => _rootDefinition;

	// See: https://docs.microsoft.com/en-us/dotnet/api/system.reflection.methodbase.isgenericmethod?view=netframework-4.8
	public bool IsGenericMethod { get; }
	public bool IsGenericMethodDefinition => _genericMethodDefinition == null && _genericArguments.Any();

	// Get the machine code of the method body
	public byte[]? GetMethodBody()
	{
		if (!VirtualAddress.HasValue)
		{
			return null;
		}

		IFileFormatStream image = Assembly.Model.Package.BinaryImage;
		return image.ReadMappedBytes(VirtualAddress.Value.Start, (int)(VirtualAddress.Value.End - VirtualAddress.Value.Start));
	}

	public override string CSharpName => // Operator overload or user-defined conversion operator
		OperatorMethodNames.ContainsKey(Name) ? "operator " + OperatorMethodNames[Name] // Explicit interface implementation
		: (IsVirtual && IsFinal && (Attributes & MethodAttributes.VtableLayoutMask) == MethodAttributes.NewSlot && Name.IndexOf('.') != -1)
			? GetExplicitInterfaceImplementationName() // Regular method
		: Name.ToCIdentifier(allowScopeQualifiers: true);

	private string GetExplicitInterfaceImplementationName()
	{
		// This is some shenanigans because IL2CPP does not use a consistent naming scheme for explicit interface implementation method names
		TypeInfo? implementingInterface =
			DeclaringType.ImplementedInterfaces.FirstOrDefault(i => Name.StartsWith(i.Namespace + "." + i.CSharpName + "."))
			?? DeclaringType.ImplementedInterfaces.FirstOrDefault(i =>
				Name.StartsWith(i.Namespace + "." + i.GetCSharpTypeDeclarationName().Replace(" ", "") + ".")
			);
		// TODO: There are some combinations we haven't dealt with so use this test as a safety valve
		// Don't call ToCIdentifier() because this still does definitely implement an interface, we just don't know which
		if (implementingInterface == null)
		{
			return Name;
		}

		return implementingInterface.CSharpName + "." + Name[(Name.LastIndexOf('.') + 1)..].ToCIdentifier();
	}

	// Initialize a method from a method definition (MethodDef)
	protected MethodBase(Il2CppInspector pkg, int methodIndex, TypeInfo declaringType)
		: base(declaringType)
	{
		_definitionIndex = methodIndex;
		MetadataToken = (int)Definition.Token;
		Index = methodIndex;
		Name = pkg.Strings[Definition.NameIndex];
		// Find method pointer
		VirtualAddress = pkg.GetMethodPointer(Assembly.ModuleDefinition, Definition);
		// Add to global method definition list
		Assembly.Model.MethodsByDefinitionIndex[Index] = this;
		_rootDefinition = this;
		// Generic method definition?
		if (Definition.GenericContainerIndex >= 0)
		{
			IsGenericMethod = true;
			// Store the generic type parameters for later instantiation
			Il2CppGenericContainer container = pkg.GenericContainers[Definition.GenericContainerIndex];
			_genericArguments =
			[
				.. Enumerable
					.Range(container.GenericParameterStart, container.TypeArgc)
					.Select(index => Assembly.Model.GetGenericParameterType(index)),
			];
			_genericMethodInstances = new Dictionary<TypeInfo[], MethodBase>(new TypeInfo.TypeArgumentsComparer()) { [_genericArguments] = this };
		}

		// Copy attributes
		Attributes = (MethodAttributes)Definition.Flags;
		MethodImplementationFlags = (MethodImplAttributes)Definition.ImplFlags;
	}

	protected MethodBase(MethodBase methodDef, TypeInfo declaringType)
		: base(declaringType)
	{
		if (!methodDef.Definition.IsValid)
		{
			throw new ArgumentException("Argument must be a bare method definition");
		}

		_rootDefinition = methodDef;
		Name = methodDef.Name;
		Attributes = methodDef.Attributes;
		MethodImplementationFlags = methodDef.MethodImplementationFlags;
		VirtualAddress = methodDef.VirtualAddress;
		IsGenericMethod = methodDef.IsGenericMethod;
		_genericArguments = methodDef.GetGenericArguments();
	}

	protected MethodBase(MethodBase methodDef, TypeInfo[] typeArguments)
		: base(methodDef.DeclaringType)
	{
		if (!methodDef.IsGenericMethodDefinition)
		{
			throw new InvalidOperationException(methodDef.Name + " is not a generic method definition.");
		}

		_rootDefinition = methodDef._rootDefinition;
		_genericMethodDefinition = methodDef;
		Name = methodDef.Name;
		Attributes = methodDef.Attributes;
		MethodImplementationFlags = methodDef.MethodImplementationFlags;
		VirtualAddress = methodDef.VirtualAddress;
		IsGenericMethod = true;
		_genericArguments = typeArguments;
	}

	// Strictly speaking, this should live in MethodInfo; constructors cannot have generic arguments.
	// However, Il2Cpp unifies Constructor and Method to a much greater extent, so that's why this is
	// here instead.
	public MethodBase MakeGenericMethod(params TypeInfo[] typeArguments)
	{
		if (typeArguments.Length != _genericArguments.Length)
		{
			throw new ArgumentException("The number of generic arguments provided does not match the generic type definition.");
		}

		_genericMethodInstances ??= new Dictionary<TypeInfo[], MethodBase>(new TypeInfo.TypeArgumentsComparer()) { [_genericArguments] = this };
		if (_genericMethodInstances.TryGetValue(typeArguments, out MethodBase? result))
		{
			return result;
		}

		result = MakeGenericMethodImpl(typeArguments);
		_genericMethodInstances[typeArguments] = result;
		return result;
	}

	protected abstract MethodBase MakeGenericMethodImpl(TypeInfo[] typeArguments);

	public string GetAccessModifierString() =>
		this switch
		{
			// Static constructors can not have an access level modifier
			{ IsConstructor: true, IsStatic: true } => "",
			// Finalizers can not have an access level modifier
			{ Name: "Finalize", IsVirtual: true, IsFamily: true } => "",
			// Explicit interface implementations do not have an access level modifier
			{ IsVirtual: true, IsFinal: true, Attributes: MethodAttributes a }
				when (a & MethodAttributes.VtableLayoutMask) == MethodAttributes.NewSlot && Name.IndexOf('.') != -1 => "",
			{ IsPrivate: true } => "private ",
			{ IsPublic: true } => "public ",
			{ IsFamily: true } => "protected ",
			{ IsAssembly: true } => "internal ",
			{ IsFamilyOrAssembly: true } => "protected internal ",
			{ IsFamilyAndAssembly: true } => "private protected ",
			_ => "",
		};

	public string GetModifierString()
	{
		// Interface methods and properties have no visible modifiers (they are always declared 'public abstract')
		if (DeclaringType.IsInterface)
		{
			return string.Empty;
		}

		StringBuilder modifiers = new(GetAccessModifierString());
		if (IsAbstract)
		{
			modifiers.Append("abstract ");
		}

		// Methods that implement interfaces are IsVirtual && IsFinal with MethodAttributes.NewSlot (don't show 'virtual sealed' for these)
		if (IsFinal && (Attributes & MethodAttributes.VtableLayoutMask) == MethodAttributes.ReuseSlot)
		{
			modifiers.Append("sealed override ");
		}

		// All abstract, override and sealed methods are also virtual by nature
		if (IsVirtual && !IsAbstract && !IsFinal && Name != "Finalize")
		{
			modifiers.Append((Attributes & MethodAttributes.VtableLayoutMask) == MethodAttributes.NewSlot ? "virtual " : "override ");
		}

		if (IsStatic)
		{
			modifiers.Append("static ");
		}

		if (RequiresUnsafeContext)
		{
			modifiers.Append("unsafe ");
		}

		if ((Attributes & MethodAttributes.PinvokeImpl) != 0)
		{
			modifiers.Append("extern ");
		}

		// Method hiding
		if (
			(DeclaringType.BaseType?.GetAllMethods().Any(m => SignatureEquals(m) && m.IsHideBySig) ?? false)
			&& (
				((Attributes & MethodAttributes.VtableLayoutMask) == MethodAttributes.ReuseSlot && !IsVirtual)
				|| (Attributes & MethodAttributes.VtableLayoutMask) == MethodAttributes.NewSlot
			)
			&& !OperatorMethodNames.ContainsKey(Name)
		)
		{
			modifiers.Append("new ");
		}

		if (Name == "op_Implicit")
		{
			modifiers.Append("implicit ");
		}

		if (Name == "op_Explicit")
		{
			modifiers.Append("explicit ");
		}

		// Async depends on a compiler-generated attribute
		if (GetCustomAttributes("System.Runtime.CompilerServices.AsyncStateMachineAttribute").Any())
		{
			modifiers.Append("async ");
		}

		// Will include a trailing space
		return modifiers.ToString();
	}

	// Get C# syntax-friendly list of parameters
	public string GetParametersString(Scope usingScope, bool emitPointer = false, bool commentAttributes = false) =>
		string.Join(", ", DeclaredParameters.Select(p => p.GetParameterString(usingScope, emitPointer, commentAttributes)));

	public string GetTypeParametersString(Scope usingScope) =>
		!GetGenericArguments().Any() ? "" : "<" + string.Join(", ", GetGenericArguments().Select(p => p.GetScopedCSharpName(usingScope))) + ">";

	public string GetFullTypeParametersString() =>
		!GetGenericArguments().Any() ? "" : "[" + string.Join(",", GetGenericArguments().Select(p => p.Name)) + "]";

	public bool SignatureEquals(MethodBase other)
	{
		if (this == other)
		{
			return true;
		}

		if (Name != other.Name)
		{
			return false;
		}

		if (DeclaredParameters.Count != other.DeclaredParameters.Count)
		{
			return false;
		}

		if (_genericArguments.Length != other._genericArguments.Length)
		{
			return false;
		}

		if (DeclaredParameters.All(p => !p.ParameterType.ContainsGenericParameters))
		{
			return Enumerable.SequenceEqual(DeclaredParameters.Select(p => p.ParameterType), other.DeclaredParameters.Select(p => p.ParameterType));
		}

		// We have to do something more expensive: check to see if the signatures are the same if the method type parameters are equated.
		// We substitute generic arguments into every parameter type but use a common set of method type parameters.
		return Enumerable.SequenceEqual(
			_rootDefinition.DeclaredParameters.Select(p =>
				p.ParameterType.SubstituteGenericArguments(DeclaringType.GetGenericArguments(), _genericArguments)
			),
			other._rootDefinition.DeclaredParameters.Select(p =>
				p.ParameterType.SubstituteGenericArguments(other.DeclaringType.GetGenericArguments(), _genericArguments)
			)
		);
	}

	// List of operator overload metadata names
	// https://docs.microsoft.com/en-us/dotnet/standard/design-guidelines/operator-overloads
	public static Dictionary<string, string> OperatorMethodNames = new()
	{
		["op_Implicit"] = "",
		["op_Explicit"] = "",
		["op_Addition"] = "+",
		["op_Subtraction"] = "-",
		["op_Multiply"] = "*",
		["op_Division"] = "/",
		["op_Modulus"] = "%",
		["op_ExclusiveOr"] = "^",
		["op_BitwiseAnd"] = "&",
		["op_BitwiseOr"] = "|",
		["op_LogicalAnd"] = "&&",
		["op_LogicalOr"] = "||",
		["op_Assign"] = "=",
		["op_LeftShift"] = "<<",
		["op_RightShift"] = ">>",
		["op_SignedLeftShift"] = "", // Listed as N/A in the documentation
		["op_SignedRightShift"] = "", // Listed as N/A in the documentation
		["op_Equality"] = "==",
		["op_Inequality"] = "!=",
		["op_GreaterThan"] = ">",
		["op_LessThan"] = "<",
		["op_GreaterThanOrEqual"] = ">=",
		["op_LessThanOrEqual"] = "<=",
		["op_MultiplicationAssignment"] = "*=",
		["op_SubtractionAssignment"] = "-=",
		["op_ExclusiveOrAssignment"] = "^=",
		["op_LeftShiftAssignment"] = "<<=", // Doesn't seem to be any right shift assignment`in documentation
		["op_ModulusAssignment"] = "%=",
		["op_AdditionAssignment"] = "+=",
		["op_BitwiseAndAssignment"] = "&=",
		["op_BitwiseOrAssignment"] = "|=",
		["op_Comma"] = ",",
		["op_DivisionAssignment"] = "*/=",
		["op_Decrement"] = "--",
		["op_Increment"] = "++",
		["op_UnaryNegation"] = "-",
		["op_UnaryPlus"] = "+",
		["op_OnesComplement"] = "~",
	};
}
