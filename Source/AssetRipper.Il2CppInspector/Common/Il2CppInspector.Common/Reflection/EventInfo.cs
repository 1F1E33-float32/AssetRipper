/*
	Copyright 2017-2021 Katy Coe - http://www.djkaty.com - https://github.com/djkaty

	All rights reserved.
*/
using System.Reflection;
using AssetRipper.Il2CppInspector.Next.Metadata;

namespace AssetRipper.Il2CppInspector.Reflection;

public class EventInfo : MemberInfo
{
	public override TypeInfo DeclaringType => base.DeclaringType ?? throw new InvalidOperationException("Member has no declaring type.");

	// IL2CPP-specific data
	public Il2CppEventDefinition Definition { get; }
	public int Index { get; }

	// Root definition: the event with Definition != null
	protected readonly EventInfo _rootDefinition;

	// Information/flags about the event
	public EventAttributes Attributes { get; }

	// Custom attributes for this member
	public override IEnumerable<CustomAttributeData> CustomAttributes => CustomAttributeData.GetCustomAttributes(_rootDefinition);

	// Methods for the event
	public MethodInfo? AddMethod { get; }
	public MethodInfo? RemoveMethod { get; }
	public MethodInfo? RaiseMethod { get; }

	// Event handler delegate type
	private readonly TypeRef _eventTypeReference;
	public TypeInfo EventHandlerType => _eventTypeReference.Value;

	// True if the event has a special name
	public bool IsSpecialName => (Attributes & EventAttributes.SpecialName) == EventAttributes.SpecialName;
	public override MemberTypes MemberType => MemberTypes.Event;

	public EventInfo(Il2CppInspector pkg, int eventIndex, TypeInfo declaringType)
		: base(declaringType)
	{
		Definition = pkg.Events[eventIndex];
		MetadataToken = (int)Definition.Token;
		Index = eventIndex;
		Name = pkg.Strings[Definition.NameIndex];
		_rootDefinition = this;
		_eventTypeReference = TypeRef.FromReferenceIndex(Assembly.Model, Definition.TypeIndex);
		Next.BinaryMetadata.Il2CppType eventType = pkg.TypeReferences[Definition.TypeIndex];
		// Copy attributes
		Attributes = (EventAttributes)eventType.Attrs;
		// NOTE: This relies on methods being added to TypeInfo.DeclaredMethods in the same order they are defined in the Il2Cpp metadata
		// add, remove and raise are method indices from the first method of the declaring type
		if (Definition.Add >= 0)
		{
			AddMethod = declaringType.DeclaredMethods.First(x => x.Index == declaringType.Definition.MethodIndex + Definition.Add);
		}

		if (Definition.Remove >= 0)
		{
			RemoveMethod = declaringType.DeclaredMethods.First(x => x.Index == declaringType.Definition.MethodIndex + Definition.Remove);
		}

		if (Definition.Raise >= 0)
		{
			RaiseMethod = declaringType.DeclaredMethods.First(x => x.Index == declaringType.Definition.MethodIndex + Definition.Raise);
		}
	}

	public EventInfo(EventInfo eventDef, TypeInfo declaringType)
		: base(declaringType)
	{
		_rootDefinition = eventDef;
		Name = eventDef.Name;
		Attributes = eventDef.Attributes;
		_eventTypeReference = TypeRef.FromTypeInfo(eventDef.EventHandlerType.SubstituteGenericArguments(declaringType.GetGenericArguments()));
		AddMethod = eventDef.AddMethod is { } addMethod ? declaringType.GetMethodByDefinition(addMethod) : null;
		RemoveMethod = eventDef.RemoveMethod is { } removeMethod ? declaringType.GetMethodByDefinition(removeMethod) : null;
		RaiseMethod = eventDef.RaiseMethod is { } raiseMethod ? declaringType.GetMethodByDefinition(raiseMethod) : null;
	}
}
