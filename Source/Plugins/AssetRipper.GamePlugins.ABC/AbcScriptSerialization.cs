using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;

namespace AssetRipper.GamePlugins.ABC;

internal static class AbcScriptSerialization
{
	public static bool ShouldSerializeField(FieldDefinition field, TypeSignature fieldType)
	{
		// UnityPlayer 0x181B1C050 excludes EditorOnlySerializable on the field, its type, or its declaring type.
		const string attributeNamespace = "UnityEngine";
		const string attributeName = "EditorOnlySerializable";
		return !field.HasCustomAttribute(attributeNamespace, attributeName)
			&& field.DeclaringType?.HasCustomAttribute(attributeNamespace, attributeName) != true
			&& (
				!fieldType.TryResolve(field.DeclaringModule?.RuntimeContext, out TypeDefinition? type)
				|| !type.HasCustomAttribute(attributeNamespace, attributeName)
			);
	}
}
