/*
	Copyright 2017 Perfare - https://github.com/Perfare/Il2CppDumper
	Copyright 2017-2021 Katy Coe - http://www.djkaty.com - https://github.com/djkaty

	All rights reserved.
*/
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using AssetRipper.Il2CppInspector.Next;
using AssetRipper.Il2CppInspector.Next.Metadata;
using AssetRipper.Il2CppInspector.VersionedSerialization;

namespace AssetRipper.Il2CppInspector;

public partial class Metadata : BinaryObjectStreamReader
{
	public Il2CppGlobalMetadataHeader Header { get; set; }
	public ImmutableArray<Il2CppAssemblyDefinition> Assemblies { get; set; }
	public ImmutableArray<Il2CppImageDefinition> Images { get; set; }
	public ImmutableArray<Il2CppTypeDefinition> Types { get; set; }
	public ImmutableArray<Il2CppMethodDefinition> Methods { get; set; }
	public ImmutableArray<Il2CppParameterDefinition> Params { get; set; }
	public ImmutableArray<Il2CppFieldDefinition> Fields { get; set; }
	public ImmutableArray<Il2CppFieldDefaultValue> FieldDefaultValues { get; set; }
	public ImmutableArray<Il2CppParameterDefaultValue> ParameterDefaultValues { get; set; }
	public ImmutableArray<Il2CppPropertyDefinition> Properties { get; set; }
	public ImmutableArray<Il2CppEventDefinition> Events { get; set; }
	public ImmutableArray<Il2CppGenericContainer> GenericContainers { get; set; }
	public ImmutableArray<Il2CppGenericParameter> GenericParameters { get; set; }
	public ImmutableArray<Il2CppCustomAttributeTypeRange> AttributeTypeRanges { get; set; }
	public ImmutableArray<Il2CppCustomAttributeDataRange> AttributeDataRanges { get; set; }
	public ImmutableArray<Il2CppInterfaceOffsetPair> InterfaceOffsets { get; set; }
	public ImmutableArray<Il2CppMetadataUsageList> MetadataUsageLists { get; set; }
	public ImmutableArray<Il2CppMetadataUsagePair> MetadataUsagePairs { get; set; }
	public ImmutableArray<Il2CppFieldRef> FieldRefs { get; set; }
	public ImmutableArray<TypeIndex> InterfaceUsageIndices { get; set; }
	public ImmutableArray<int> NestedTypeIndices { get; set; }
	public ImmutableArray<int> AttributeTypeIndices { get; set; }
	public ImmutableArray<TypeIndex> GenericConstraintIndices { get; set; }
	public ImmutableArray<uint> VTableMethodIndices { get; set; }
	public string[] StringLiterals { get; set; } = [];
	public ImmutableArray<Il2CppInlineArrayLength> TypeInlineArrays { get; set; }
	public int FieldAndParameterDefaultValueDataOffset =>
		Version >= MetadataVersions.V380 ? Header.FieldAndParameterDefaultValueData.Offset : Header.FieldAndParameterDefaultValueDataOffset;
	public int AttributeDataOffset =>
		GameAdapter is { AttributeDataOffset: >= 0 } adapter ? adapter.AttributeDataOffset
		: Version >= MetadataVersions.V380 ? Header.AttributeData.Offset
		: Header.AttributeDataOffset;
	public Dictionary<int, string> Strings { get; private set; } = [];
	public Dictionary<int, byte[]> AssemblyPublicKeys { get; private set; } = [];

	// Set if something in the metadata has been modified / decrypted
	public bool IsModified { get; private set; }

	// Status update callback
	private EventHandler<string>? OnStatusUpdate { get; set; }

	private void StatusUpdate(string status) => OnStatusUpdate?.Invoke(this, status);

	// Initialize metadata object from a stream
	public static Metadata FromStream(MemoryStream stream, EventHandler<string>? statusCallback = null, CancellationToken cancellationToken = default)
	{
		Metadata metadata = new(statusCallback) { CancellationToken = cancellationToken };
		stream.Position = 0;
		stream.CopyTo(metadata);
		metadata.Position = 0;
		metadata.Initialize();
		return metadata;
	}

	private Metadata(EventHandler<string>? statusCallback = null) => OnStatusUpdate = statusCallback;

	private void Initialize()
	{
		StatusUpdate("Processing metadata");
		// Read metadata header
		Header = ReadVersionedObject<Il2CppGlobalMetadataHeader>(0);
		// Check for correct magic bytes
		if (!Header.SanityValid)
		{
			throw new InvalidDataException("IL2CPP metadata has an invalid signature.");
		}

		// Set object versioning for AssetRipper.Il2CppInspector.Bin2Object from metadata version
		Version = new StructVersion(Header.Version);
		if (Version < MetadataVersions.V160 || Version > MetadataVersions.V1060)
		{
			throw new NotSupportedException($"IL2CPP metadata version {Header.Version} is unsupported.");
		}

		// Rewind and read metadata header with the correct version settings
		Header = ReadVersionedObject<Il2CppGlobalMetadataHeader>(0);
		// Setup the proper index sizes for metadata v38+
		if (Version >= MetadataVersions.V380)
		{
			static int GetIndexSize(int elementCount)
			{
				return elementCount switch
				{
					<= byte.MaxValue => sizeof(byte),
					<= ushort.MaxValue => sizeof(ushort),
					_ => sizeof(int),
				};
			}

			int typeDefinitionIndexSize = GetIndexSize(Header.TypeDefinitions.Count);
			int genericContainerIndexSize = GetIndexSize(Header.GenericContainers.Count);
			string tag = $"{TypeDefinitionIndex.TagPrefix}{typeDefinitionIndexSize}_{GenericContainerIndex.TagPrefix}{genericContainerIndexSize}";
			StructVersion tempVersion = new(Version.Major, Version.Minor, tag);
			// now we need to derive the size for TypeIndex.
			// this is normally done through s_Il2CppMetadataRegistration->typesCount, but we don't want to use the binary for this
			// as we do not have it available at this point.
			// thankfully, we can just guess the size based off the three available options and the known total size of
			// a type entry that uses TypeIndex.
			int actualSize = Header.InterfaceOffsets.SectionSize / Header.InterfaceOffsets.Count;
			int maxSize = Il2CppInterfaceOffsetPair.StructSize(tempVersion);
			int typeIndexSize;
			if (actualSize == maxSize)
			{
				typeIndexSize = sizeof(int);
			}
			else if (actualSize == maxSize - 2)
			{
				typeIndexSize = sizeof(ushort);
			}
			else if (actualSize == maxSize - 3)
			{
				typeIndexSize = sizeof(byte);
			}
			else
			{
				throw new InvalidOperationException("Could not determine TypeIndex size based on the metadata header");
			}

			string? fullTag = $"{tag}_{TypeIndex.TagPrefix}{typeIndexSize}";
			if (Version >= MetadataVersions.V390)
			{
				int parameterIndexSize = GetIndexSize(Header.Parameters.Count);
				fullTag += $"_{ParameterIndex.TagPrefix}{parameterIndexSize}";
			}

			if (Version >= MetadataVersions.V1040)
			{
				int eventIndexSize = GetIndexSize(Header.Events.Count);
				int interfacesIndexSize = GetIndexSize(Header.InterfaceOffsets.Count);
				int nestedTypeIndexSize = GetIndexSize(Header.NestedTypes.Count);
				int propertyIndexSize = GetIndexSize(Header.Properties.Count);
				fullTag +=
					$"_{EventIndex.TagPrefix}{eventIndexSize}"
					+ $"_{InterfacesIndex.TagPrefix}{interfacesIndexSize}"
					+ $"_{NestedTypeIndex.TagPrefix}{nestedTypeIndexSize}"
					+ $"_{PropertyIndex.TagPrefix}{propertyIndexSize}";
			}

			if (Version >= MetadataVersions.V1050)
			{
				int methodIndexSize = GetIndexSize(Header.Methods.Count);
				fullTag += $"_{MethodIndex.TagPrefix}{methodIndexSize}";
			}

			if (Version >= MetadataVersions.V1060)
			{
				int genericParameterIndexSize = GetIndexSize(Header.GenericParameters.Count);
				int fieldIndexSize = GetIndexSize(Header.Fields.Count);
				int defaultValueDataIndex = GetIndexSize(Header.FieldAndParameterDefaultValueData.Count);
				fullTag +=
					$"_{GenericParameterIndex.TagPrefix}{genericParameterIndexSize}"
					+ $"_{FieldIndex.TagPrefix}{fieldIndexSize}"
					+ $"_{DefaultValueDataIndex.TagPrefix}{defaultValueDataIndex}";
			}

			Version = new StructVersion(Version.Major, Version.Minor, fullTag);
		}

		// Sanity checking
		// Unity.IL2CPP.MetadataCacheWriter.WriteLibIl2CppMetadata always writes the metadata information in the same order it appears in the header,
		// with each block always coming directly after the previous block, 4-byte aligned. We can use this to check the integrity of the data and
		// detect sub-versions.
		// For metadata v24.0, the header can either be either 0x110 (24.0, 24.1) or 0x108 (24.2) bytes long. Since 'stringLiteralOffset' is the first thing
		// in the header after the sanity and version fields, and since it will always point directly to the first byte after the end of the header,
		// we can use this value to determine the actual header length and therefore narrow down the metadata version to 24.0/24.1 or 24.2.
		int realHeaderLength = Version >= MetadataVersions.V380 ? Header.StringLiterals.Offset : Header.StringLiteralOffset;
		if (realHeaderLength != Sizeof<Il2CppGlobalMetadataHeader>())
		{
			if (Version == MetadataVersions.V240)
			{
				Version = MetadataVersions.V242;
				Header = ReadVersionedObject<Il2CppGlobalMetadataHeader>(0);
			}
		}

		if (realHeaderLength != Sizeof<Il2CppGlobalMetadataHeader>())
		{
			throw new InvalidOperationException(
				"Could not verify the integrity of the metadata file or accurately identify the metadata sub-version"
			);
		}

		// Load all the relevant metadata using offsets provided in the header
		Images = ReadMetadataArray<Il2CppImageDefinition>(Header.ImagesOffset, Header.ImagesSize, Header.Images);
		// As an additional sanity check, all images in the metadata should have Mono.Cecil.MetadataToken == 1
		// In metadata v24.1, two extra fields were added which will cause the below test to fail.
		// In that case, we can then adjust the version number and reload
		// Tokens were introduced in v19 - we don't bother testing earlier versions
		if (Version >= MetadataVersions.V190 && Images.Any(x => x.Token != 1))
		{
			if (Version == MetadataVersions.V240)
			{
				Version = MetadataVersions.V241;
				// No need to re-read the header, it's the same for both sub-versions
				Images = ReadMetadataArray<Il2CppImageDefinition>(Header.ImagesOffset, Header.ImagesSize, Header.Images);
				if (Images.Any(x => x.Token != 1))
				{
					throw new InvalidOperationException("Could not verify the integrity of the metadata file image list");
				}
			}
		}

		Types = ReadMetadataArray<Il2CppTypeDefinition>(Header.TypeDefinitionsOffset, Header.TypeDefinitionsSize, Header.TypeDefinitions);
		Methods = ReadMetadataArray<Il2CppMethodDefinition>(Header.MethodsOffset, Header.MethodsSize, Header.Methods);
		Params = ReadMetadataArray<Il2CppParameterDefinition>(Header.ParametersOffset, Header.ParametersSize, Header.Parameters);
		Fields = ReadMetadataArray<Il2CppFieldDefinition>(Header.FieldsOffset, Header.FieldsSize, Header.Fields);
		FieldDefaultValues = ReadMetadataArray<Il2CppFieldDefaultValue>(
			Header.FieldDefaultValuesOffset,
			Header.FieldDefaultValuesSize,
			Header.FieldDefaultValues
		);
		Properties = ReadMetadataArray<Il2CppPropertyDefinition>(Header.PropertiesOffset, Header.PropertiesSize, Header.Properties);
		Events = ReadMetadataArray<Il2CppEventDefinition>(Header.EventsOffset, Header.EventsSize, Header.Events);
		InterfaceUsageIndices = ReadMetadataArray<TypeIndex>(Header.InterfacesOffset, Header.InterfacesSize, Header.Interfaces);
		NestedTypeIndices = ReadMetadataPrimitiveArray<int>(Header.NestedTypesOffset, Header.NestedTypesSize, Header.NestedTypes);
		GenericContainers = ReadMetadataArray<Il2CppGenericContainer>(
			Header.GenericContainersOffset,
			Header.GenericContainersSize,
			Header.GenericContainers
		);
		GenericParameters = ReadMetadataArray<Il2CppGenericParameter>(
			Header.GenericParametersOffset,
			Header.GenericParametersSize,
			Header.GenericParameters
		);
		GenericConstraintIndices = ReadMetadataArray<TypeIndex>(
			Header.GenericParameterConstraintsOffset,
			Header.GenericParameterConstraintsSize,
			Header.GenericParameterConstraints
		);
		InterfaceOffsets = ReadMetadataArray<Il2CppInterfaceOffsetPair>(
			Header.InterfaceOffsetsOffset,
			Header.InterfaceOffsetsSize,
			Header.InterfaceOffsets
		);
		VTableMethodIndices = ReadMetadataPrimitiveArray<uint>(Header.VTableMethodsOffset, Header.VTableMethodsSize, Header.VtableMethods);
		bool useCompressedAssemblyPublicKeys = Version == MetadataVersions.V242 || Version >= MetadataVersions.V244;
		{
			// In v24.4 hashValueIndex was removed from Il2CppAssemblyNameDefinition, which is a field in Il2CppAssemblyDefinition
			// The number of images and assemblies should be the same. If they are not, we deduce that we are using v24.4
			// Note the version comparison matches both 24.2 and 24.3 here since 24.3 is tested for during binary loading
			int assemblyCount = Header.AssembliesSize / Sizeof<Il2CppAssemblyDefinition>();
			bool changedAssemblyDefStruct = false;
			if (
				(Version == MetadataVersions.V241 || Version == MetadataVersions.V242 || Version == MetadataVersions.V243)
				&& assemblyCount < Images.Length
			)
			{
				if (Version == MetadataVersions.V241)
				{
					changedAssemblyDefStruct = true;
				}

				Version = MetadataVersions.V244;
				useCompressedAssemblyPublicKeys = true;
			}

			Assemblies = ReadMetadataArray<Il2CppAssemblyDefinition>(Header.AssembliesOffset, Header.AssembliesSize, Header.Assemblies);
			if (changedAssemblyDefStruct)
			{
				Version = MetadataVersions.V241;
			}

			ParameterDefaultValues = ReadMetadataArray<Il2CppParameterDefaultValue>(
				Header.ParameterDefaultValuesOffset,
				Header.ParameterDefaultValuesSize,
				Header.ParameterDefaultValues
			);
		}

		if (Version >= MetadataVersions.V190 && Version < MetadataVersions.V270)
		{
			MetadataUsageLists = ReadMetadataArray<Il2CppMetadataUsageList>(Header.MetadataUsageListsOffset, Header.MetadataUsageListsCount, default);
			MetadataUsagePairs = ReadMetadataArray<Il2CppMetadataUsagePair>(Header.MetadataUsagePairsOffset, Header.MetadataUsagePairsCount, default);
		}

		if (Version >= MetadataVersions.V190)
		{
			FieldRefs = ReadMetadataArray<Il2CppFieldRef>(Header.FieldRefsOffset, Header.FieldRefsSize, Header.FieldRefs);
		}

		if (Version >= MetadataVersions.V210 && Version < MetadataVersions.V290)
		{
			AttributeTypeIndices = ReadMetadataPrimitiveArray<int>(Header.AttributesTypesOffset, Header.AttributesTypesCount, default);
			AttributeTypeRanges = ReadMetadataArray<Il2CppCustomAttributeTypeRange>(Header.AttributesInfoOffset, Header.AttributesInfoCount, default);
		}

		if (Version >= MetadataVersions.V290)
		{
			AttributeDataRanges = ReadMetadataArray<Il2CppCustomAttributeDataRange>(
				Header.AttributeDataRangeOffset,
				Header.AttributeDataRangeSize,
				Header.AttributeDataRanges
			);
		}

		if (Version >= MetadataVersions.V1040)
		{
			TypeInlineArrays = ReadMetadataArray<Il2CppInlineArrayLength>(0, 0, Header.TypeInlineArrays);
		}

		{
			int stringOffset = Version >= MetadataVersions.V380 ? Header.Strings.Offset : Header.StringOffset;
			int stringLength = Version >= MetadataVersions.V380 ? Header.Strings.SectionSize : Header.StringSize;
			Position = stringOffset;
			while (Position < stringOffset + stringLength)
			{
				CancellationToken.ThrowIfCancellationRequested();
				Strings.Add((int)Position - stringOffset, ReadNullTerminatedString());
			}
		}

		{
			ImmutableArray<Il2CppStringLiteral> stringLiteralList = ReadMetadataArray<Il2CppStringLiteral>(
				Header.StringLiteralOffset,
				Header.StringLiteralSize,
				Header.StringLiterals
			);
			int dataOffset = Version >= MetadataVersions.V380 ? Header.StringLiteralData.Offset : Header.StringLiteralDataOffset;
			if (Version >= MetadataVersions.V350)
			{
				string[] literals = new string[stringLiteralList.Length - 1];
				for (int i = 0; i < literals.Length; i++)
				{
					CancellationToken.ThrowIfCancellationRequested();
					int currentStringDataIndex = stringLiteralList[i].DataIndex;
					int nextStringDataIndex = stringLiteralList[i + 1].DataIndex;
					int stringLength = nextStringDataIndex - currentStringDataIndex;
					literals[i] = ReadFixedLengthString(dataOffset + currentStringDataIndex, stringLength);
				}

				StringLiterals = literals;
			}
			else
			{
				StringLiterals = new string[stringLiteralList.Length];
				for (int i = 0; i < stringLiteralList.Length; i++)
				{
					CancellationToken.ThrowIfCancellationRequested();
					StringLiterals[i] = ReadFixedLengthString(dataOffset + stringLiteralList[i].DataIndex, (int)stringLiteralList[i].Length);
				}
			}
		}

		// These are stored in a special way, so we read them in advance.
		if (useCompressedAssemblyPublicKeys)
		{
			int stringOffset = Version >= MetadataVersions.V380 ? Header.Strings.Offset : Header.StringOffset;
			foreach (Il2CppAssemblyDefinition assembly in Assemblies.Where(x => x.Aname.Flags.HasFlag(AssemblyNameFlags.PublicKey)))
			{
				CancellationToken.ThrowIfCancellationRequested();
				Position = stringOffset + assembly.Aname.PublicKeyIndex;
				uint length = ReadCompressedUInt32();
				AssemblyPublicKeys[assembly.Aname.PublicKeyIndex] = ReadBytes((int)length);
			}
		}
		else
		{
			foreach (Il2CppAssemblyDefinition assembly in Assemblies.Where(x => x.Aname.Flags.HasFlag(AssemblyNameFlags.PublicKey)))
			{
				string escapedPublicKey = Strings[assembly.Aname.PublicKeyIndex];
				Debug.Assert(escapedPublicKey[0] == '"' && escapedPublicKey[^1] == '"');
				ReadOnlySpan<char> publicKeyHex = escapedPublicKey.AsSpan(1, escapedPublicKey.Length - 2);
				// Upper bound for the public key length, this could also be decreased
				byte[] publicKey = new byte[publicKeyHex.Length];
				int publicKeyCurrentIndex = 0;
				byte publicKeyCurrentByte = byte.MinValue;
				foreach (char val in publicKeyHex)
				{
					CancellationToken.ThrowIfCancellationRequested();
					switch (val)
					{
						case '\\':
							publicKey[publicKeyCurrentIndex++] = publicKeyCurrentByte;
							publicKeyCurrentByte = 0;
							continue;
						case 'x':
							continue;
						default:
							byte currentCharValue = byte.Parse(new ReadOnlySpan<char>(in val), NumberStyles.HexNumber);
							publicKeyCurrentByte = (byte)((publicKeyCurrentByte << 4) | currentCharValue);
							break;
					}
				}

				publicKey[publicKeyCurrentIndex++] = publicKeyCurrentByte;
				AssemblyPublicKeys[assembly.Aname.PublicKeyIndex] = publicKey[..(publicKeyCurrentIndex + 1)];
			}
		}
	}

	public ImmutableArray<T> ReadMetadataPrimitiveArray<T>(int oldOffset, int oldSize, Il2CppSectionMetadata newMetadata)
		where T : unmanaged
	{
		if (Version >= MetadataVersions.V380)
		{
			Debug.Assert(newMetadata.Count * Unsafe.SizeOf<T>() == newMetadata.SectionSize);
			return ReadPrimitiveArray<T>(newMetadata.Offset, newMetadata.Count);
		}

		return ReadPrimitiveArray<T>(oldOffset, oldSize / Unsafe.SizeOf<T>());
	}

	public ImmutableArray<T> ReadMetadataArray<T>(int oldOffset, int oldSize, Il2CppSectionMetadata newMetadata)
		where T : IReadable, new()
	{
		if (Version >= MetadataVersions.V380)
		{
			Debug.Assert(newMetadata.Count * Sizeof<T>() == newMetadata.SectionSize);
			return ReadVersionedObjectArray<T>(newMetadata.Offset, newMetadata.Count);
		}

		return ReadVersionedObjectArray<T>(oldOffset, oldSize / Sizeof<T>());
	}

	public int Sizeof<T>()
		where T : IReadable => T.Size(Version, new ReaderConfig(Is32Bit));
}
