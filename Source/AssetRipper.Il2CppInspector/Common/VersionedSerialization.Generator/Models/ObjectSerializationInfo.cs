using AssetRipper.Il2CppInspector.VersionedSerialization.Generator.Utils;
using Microsoft.CodeAnalysis.CSharp;

namespace AssetRipper.Il2CppInspector.VersionedSerialization.Generator.Models;

public sealed record ObjectSerializationInfo(
	string Namespace,
	string Name,
	bool HasBaseType,
	bool IsPublic,
	SyntaxKind DefinitionType,
	bool CanGenerateSizeMethod,
	ImmutableEquatableArray<PropertySerializationInfo> Properties
);
