using AssetRipper.Il2CppInspector.VersionedSerialization.Generator.Utils;

namespace AssetRipper.Il2CppInspector.VersionedSerialization.Generator.Models;

public sealed record PropertySerializationInfo(
	string Name,
	string ReadMethod,
	string SizeExpression,
	PropertyType Type,
	ImmutableEquatableArray<VersionCondition> VersionConditions
);
