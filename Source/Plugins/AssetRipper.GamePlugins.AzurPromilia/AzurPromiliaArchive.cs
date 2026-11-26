using AssetRipper.Import.GamePlugins;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.SerializedFiles;
using AssetRipper.IO.Files.SerializedFiles.Parser;
using AssetRipper.IO.Files.SerializedFiles.Parser.TypeTrees;
using AssetRipper.Primitives;

namespace AssetRipper.GamePlugins.AzurPromilia;

internal sealed class AzurPromiliaArchive : UnityFsGameArchive
{
	private static readonly Lazy<TypeTreeDump> Dump = new(static () =>
	{
		using Stream stream =
			typeof(AzurPromiliaArchive).Assembly.GetManifestResourceStream("AssetRipper.GamePlugins.AzurPromilia.release.ttbin")
			?? throw new InvalidDataException("Azur Promilia release type tree resource is missing.");
		TypeTreeDump dump = TypeTreeDump.Read(stream, TransferInstructionFlags.SerializeGameRelease);
		if (dump.Revision != new UnityVersion(2022, 3, 0) || !string.Equals(dump.Variant, "Runtime", StringComparison.Ordinal))
		{
			throw new NotSupportedException($"Unsupported Azur Promilia type tree revision {dump.Revision}, variant {dump.Variant}.");
		}
		return dump;
	});

	// UnityPlayer RVA 0x6D8FE0 compares the first 16 bytes of the replacement identifier.
	public AzurPromiliaArchive(string path)
		: base(path, "z4l23bh5#35h&321"u8, "#$manjuuunity*!@ArchiveRedirect"u8[..16]) { }

	public override FileBase ReadEntry(GameArchiveEntry entry, Predicate<int>? readObjectData = null)
	{
		FileBase file = base.ReadEntry(entry, readObjectData);
		if (file is not SerializedFile serialized)
		{
			return file;
		}
		SerializedFileBuilder builder = new()
		{
			Generation = serialized.Generation,
			Version = serialized.Version,
			Platform = serialized.Platform,
			EndianType = serialized.EndianType,
			HasTypeTree = serialized.HasTypeTree,
			UserInformation = serialized.UserInformation,
		};
		builder.Dependencies.AddRange(serialized.Dependencies);
		builder.ScriptTypes.AddRange(serialized.ScriptTypes);
		builder.Types.AddRange(serialized.Types);
		builder.RefTypes.AddRange(serialized.RefTypes);
		foreach (ObjectInfo original in serialized.Objects)
		{
			ObjectInfo item = original;
			// Native MonoBehaviour RTTI describes only its base; script fields still require metadata.
			if (item.TypeID != 114 && (readObjectData is null || readObjectData(item.TypeID)))
			{
				if (item.Type?.OldType is { Nodes.Count: > 0 } embedded)
				{
					item.SourceTypeTree = embedded;
				}
				else if (Dump.Value.Trees.TryGetValue(item.TypeID, out TypeTree? tree))
				{
					// AnimationClip adds m_Acl* fields before m_ClipBindingConstant; keep the full native layout.
					item.SourceTypeTree = tree;
				}
			}
			builder.Objects.Add(item);
		}
		SerializedFile result = builder.Build();
		result.Name = file.Name;
		result.FilePath = file.FilePath;
		return result;
	}
}
