/*
	Copyright 2017-2021 Katy Coe - http://www.djkaty.com - https://github.com/djkaty
	Copyright 2020 Robert Xiao - https://robertxiao.ca

	All rights reserved.
*/
namespace AssetRipper.Il2CppInspector.Reflection;

/// <summary>
/// A class which lazily refers to a TypeInfo instance
/// </summary>
internal class TypeRef
{
	private TypeModel? _model;
	private int _referenceIndex = -1;
	private int _definitionIndex = -1;
	private TypeInfo? _typeInfo;

	private TypeRef() { }

	public TypeInfo Value
	{
		get
		{
			if (_model is not null && _referenceIndex != -1)
			{
				return _model.TypesByReferenceIndex[_referenceIndex];
			}

			if (_model is not null && _definitionIndex != -1)
			{
				return _model.TypesByDefinitionIndex[_definitionIndex];
			}

			return _typeInfo ?? throw new InvalidDataException("IL2CPP type reference has no target.");
		}
	}

	public static TypeRef FromReferenceIndex(TypeModel model, int index) => new() { _model = model, _referenceIndex = index };

	public static TypeRef FromDefinitionIndex(TypeModel model, int index) => new() { _model = model, _definitionIndex = index };

	public static TypeRef FromTypeInfo(TypeInfo type) => new() { _typeInfo = type };
}
