using System.Buffers.Binary;
using AssetRipper.Import.Structure.Assembly.TypeTrees;
using AssetRipper.IO.Files.SerializedFiles;
using AssetRipper.IO.Files.SerializedFiles.Parser;
using AssetRipper.IO.Files.SerializedFiles.Parser.TypeTrees;
using AssetRipper.Primitives;

namespace AssetRipper.GamePlugins.ABC;

internal sealed class AbcTypeTrees
{
	private static readonly Lazy<TypeTreeDump> Dump = new(static () =>
	{
		using Stream stream =
			typeof(AbcTypeTrees).Assembly.GetManifestResourceStream("AssetRipper.GamePlugins.ABC.release.ttbin")
			?? throw new InvalidDataException("ABC release type tree resource is missing.");
		TypeTreeDump dump = TypeTreeDump.Read(stream, TransferInstructionFlags.SerializeGameRelease);
		if (dump.Revision != new UnityVersion(2022, 3, 0) || dump.Variant != "Runtime")
		{
			throw new NotSupportedException($"Unsupported ABC type tree revision {dump.Revision}, variant {dump.Variant}.");
		}
		return dump;
	});

	private readonly Dictionary<(int ClassID, int Variant), TypeTree> variants = [];

	public void Apply(ref ObjectInfo item)
	{
		// Native MonoBehaviour RTTI contains only the base fields. Script layouts still come from metadata.
		if (item.TypeID == 114)
		{
			return;
		}
		if (item.Type?.OldType is { Nodes.Count: > 0 } embedded)
		{
			item.SourceTypeTree = embedded;
			return;
		}
		if (!Dump.Value.Trees.TryGetValue(item.TypeID, out TypeTree? tree))
		{
			return;
		}
		if (item.TypeID is 0x37FFFF02 or 0x37FFFF03)
		{
			int variant = ReadVariant(item.TypeID, item.ObjectData, tree);
			(int, int) key = (item.TypeID, variant);
			if (!variants.TryGetValue(key, out TypeTree? resolved))
			{
				resolved = ResolveVariant(tree, item.TypeID, variant);
				variants.Add(key, resolved);
			}
			tree = resolved;
		}
		item.SourceTypeTree = tree;
	}

	private static int ReadVariant(int classId, ReadOnlySpan<byte> data, TypeTree tree)
	{
		if (data.Length < 4)
		{
			throw new InvalidDataException("ABC Kodama name length is missing.");
		}
		int nameLength = BinaryPrimitives.ReadInt32LittleEndian(data);
		long position = (4L + nameLength + 3) & ~3L;
		if (nameLength < 0 || position >= data.Length)
		{
			throw new InvalidDataException("ABC Kodama name exceeds object bounds.");
		}
		if (classId == 0x37FFFF03)
		{
			return data[(int)position] switch
			{
				0 => 0,
				1 => 1,
				_ => throw new InvalidDataException("Invalid ABC Kodama APV flag."),
			};
		}
		if (position > data.Length - sizeof(int))
		{
			throw new InvalidDataException("ABC Kodama LOD count is missing.");
		}
		int count = BinaryPrimitives.ReadInt32LittleEndian(data[(int)position..]);
		TypeTreeNodeStruct.TryMakeFromTypeTree(tree, out TypeTreeNodeStruct root);
		int minimumSize = TypeTreeSerialization.GetMinimumSize(root["m_BakedSectorData"]["SectorData_0"]);
		if (count < 0 || count > (data.Length - position - sizeof(int)) / Math.Max(1, minimumSize))
		{
			throw new InvalidDataException($"ABC Kodama LOD count {count} exceeds object bounds.");
		}
		return count;
	}

	private static TypeTree ResolveVariant(TypeTree source, int classId, int variant)
	{
		// GenerateTypeTreeTransfer uses a default instance. StreamedBinaryRead varies these branches:
		// KodamaBuildData 0x181797350 repeats SectorData by lodSectorDataNum;
		// KodamaVolumeData 0x1817AC360 omits editor sectors and their index map when APV is enabled.
		TypeTree result = new();
		for (int i = 0; i < source.Nodes.Count; )
		{
			TypeTreeNode node = source.Nodes[i];
			if (classId == 0x37FFFF02 && node.Level == 2 && node.Name.StartsWith("SectorData_", StringComparison.Ordinal))
			{
				int end = SubtreeEnd(source, i);
				if (node.Name == "SectorData_0")
				{
					for (int lod = 0; lod < variant; lod++)
					{
						for (int n = i; n < end; n++)
						{
							Append(source.Nodes[n], n == i ? $"SectorData_{lod}" : source.Nodes[n].Name);
						}
					}
				}
				i = end;
			}
			else if (classId == 0x37FFFF03 && variant != 0 && node.Level == 1 && node.Name is "m_EditorSectorData" or "m_SectorGuidToSectorIndex")
			{
				i = SubtreeEnd(source, i);
			}
			else
			{
				Append(node, node.Name);
				i++;
			}
		}
		result.RebuildStringBuffer();
		return result;

		void Append(TypeTreeNode node, string name)
		{
			result.Nodes.Add(
				new(node.Type, name, node.Level, node.ByteSize, result.Nodes.Count, node.Version, node.TypeFlags, node.MetaFlag)
				{
					RefTypeHash = node.RefTypeHash,
				}
			);
		}
	}

	private static int SubtreeEnd(TypeTree tree, int start)
	{
		int end = start + 1;
		while (end < tree.Nodes.Count && tree.Nodes[end].Level > tree.Nodes[start].Level)
		{
			end++;
		}
		return end;
	}
}
