using System.Text;

namespace AssetRipper.IO.Files.SerializedFiles.Parser.TypeTrees;

/// <summary>Reads the version 1 TYPETREE binary dump format.</summary>
public sealed class TypeTreeDump
{
	private static readonly UTF8Encoding StringEncoding = new(false, true);
	public UnityVersion Revision { get; private init; }
	public string Variant { get; private init; } = "";
	public IReadOnlyDictionary<int, TypeTree> Trees { get; private init; } = new Dictionary<int, TypeTree>();

	public static TypeTreeDump Read(Stream stream, TransferInstructionFlags transferFlags)
	{
		using BinaryReader reader = new(stream, StringEncoding, true);
		try
		{
			if (reader.ReadUInt64() != 0x4545525445505954)
			{
				throw new InvalidDataException("Invalid TYPETREE signature.");
			}
			uint format = reader.ReadUInt32();
			if (format != 1)
			{
				throw new NotSupportedException($"Unsupported TYPETREE format {format}.");
			}
			UnityVersion revision = new(reader.ReadUInt16(), reader.ReadByte(), reader.ReadByte());
			string variant = ReadString(reader);
			int typeCount = ReadCount(reader, 48);
			Dictionary<int, TypeTree> trees = [];
			for (int i = 0; i < typeCount; i++)
			{
				_ = ReadString(reader); // RTTI class, namespace, and module names.
				_ = ReadString(reader);
				_ = ReadString(reader);
				int classId = reader.ReadInt32();
				int size = reader.ReadInt32();
				_ = reader.ReadUInt32(); // RTTI flags.
				_ = reader.ReadInt32(); // Base ClassID.
				uint derivedIndex = reader.ReadUInt32();
				uint descendants = reader.ReadUInt32();
				if (size < 0 || derivedIndex > typeCount || descendants > typeCount - derivedIndex)
				{
					throw new InvalidDataException($"Invalid RTTI extent for ClassID {classId}.");
				}
				ulong flags = reader.ReadUInt64();
				int nodeCount = ReadCount(reader, 35);
				TypeTree tree = new();
				for (int n = 0; n < nodeCount; n++)
				{
					TypeTreeNode node = new()
					{
						Type = ReadString(reader),
						Name = ReadString(reader),
						TypeFlags = checked((int)reader.ReadUInt32()),
						ByteSize = reader.ReadInt32(),
						Index = reader.ReadInt32(),
						Version = reader.ReadInt16(),
						Level = reader.ReadByte(),
						MetaFlag = (TransferMetaFlags)reader.ReadUInt32(),
						RefTypeHash = reader.ReadUInt64(),
					};
					if (
						node.ByteSize < -1
						|| node.Index < 0
						|| node.Version < 0
						|| node.TypeFlags > byte.MaxValue
						|| (n == 0 ? node.Level != 0 : node.Level == 0 || node.Level > tree.Nodes[^1].Level + 1)
					)
					{
						throw new InvalidDataException($"Invalid type tree node {n} for ClassID {classId}.");
					}
					tree.Nodes.Add(node);
				}
				if (nodeCount > 0 && flags == (ulong)transferFlags)
				{
					tree.RebuildStringBuffer();
					if (!trees.TryAdd(classId, tree))
					{
						throw new InvalidDataException($"Duplicate type tree for ClassID {classId}.");
					}
				}
			}
			if (stream.Position != stream.Length)
			{
				throw new InvalidDataException("Trailing data in TYPETREE dump.");
			}
			return new()
			{
				Revision = revision,
				Variant = variant,
				Trees = trees,
			};
		}
		catch (Exception ex) when (ex is EndOfStreamException or OverflowException or DecoderFallbackException)
		{
			throw new InvalidDataException("Invalid or truncated TYPETREE dump.", ex);
		}
	}

	private static int ReadCount(BinaryReader reader, int minimumSize)
	{
		uint count = reader.ReadUInt32();
		if (count > int.MaxValue || count > (reader.BaseStream.Length - reader.BaseStream.Position) / minimumSize)
		{
			throw new InvalidDataException("TYPETREE count exceeds file bounds.");
		}
		return (int)count;
	}

	private static string ReadString(BinaryReader reader)
	{
		int size = ReadCount(reader, 1);
		return StringEncoding.GetString(reader.ReadBytes(size));
	}
}
