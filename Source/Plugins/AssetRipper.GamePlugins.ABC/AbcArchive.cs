using System.Buffers.Binary;
using System.Text;
using AssetRipper.Import.GamePlugins;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.SerializedFiles;
using AssetRipper.IO.Files.SerializedFiles.Parser;
using K4os.Compression.LZ4;
using OodleSharp;

namespace AssetRipper.GamePlugins.ABC;

public sealed class AbcArchive : GameArchive
{
	private readonly Lazy<Index> _index;
	private readonly object _gate = new();
	private byte[]? _cachedBlock;
	private int _cachedBlockIndex = -1;
	private bool _disposed;
	public override string FilePath { get; }
	public override string Name => Path.GetFileName(FilePath);
	public override IReadOnlyList<GameArchiveEntry> Entries => _index.Value.Entries;
	public long DecodedBlockCount { get; private set; }
	public bool IsIndexLoaded => _index.IsValueCreated;

	public AbcArchive(string path)
	{
		FilePath = path;
		_index = new Lazy<Index>(ReadIndex);
	}

	private Index ReadIndex()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		using Stream stream = File.OpenRead(FilePath);
		using BinaryReader reader = new(stream, Encoding.UTF8, true);
		if (ReadString(reader) != "UnityFS" || ReadInt(reader) != 10)
			throw new InvalidDataException($"{Name}: expected ABC UnityFS v10.");
		_ = ReadString(reader);
		if (ReadString(reader) != "2022.3.14f1")
			throw new NotSupportedException($"{Name}: unsupported ABC Unity revision.");
		long fileSize = ReadLong(reader);
		int compressed = ReadInt(reader);
		int uncompressed = ReadInt(reader);
		int flags = ReadInt(reader);
		if (
			fileSize != stream.Length
			|| compressed < 16
			|| uncompressed < 24
			|| uncompressed > 64 * 1024 * 1024
			|| compressed > stream.Length - stream.Position
			|| (flags & ~0xAFF) != 0
			|| (flags & 0x840) != 0x840
		)
			throw new InvalidDataException($"{Name}: invalid archive header or flags 0x{flags:X}.");
		long headerEnd = Align(stream.Position);
		long metadataOffset = (flags & 0x80) != 0 ? fileSize - compressed : headerEnd;
		stream.Position = metadataOffset;
		byte[] encoded = reader.ReadBytes(compressed);
		if (encoded.Length != compressed)
			throw new EndOfStreamException(Name);
		AbcCipher.Decrypt(encoded);
		byte[] metadata = new byte[uncompressed];
		int written = (flags & 63) switch
		{
			0 when compressed == uncompressed => Copy(encoded, metadata),
			2 or 3 => LZ4Codec.Decode(encoded, metadata),
			_ => throw new NotSupportedException($"{Name}: unsupported block table compression {flags & 63}."),
		};
		if (written != uncompressed)
			throw new InvalidDataException($"{Name}: invalid decoded block table length.");
		using BinaryReader table = new(new MemoryStream(metadata));
		table.BaseStream.Position = 16;
		int blockCount = ReadCount(table, 10);
		Block[] blocks = new Block[blockCount];
		long packed = (flags & 0x80) != 0 ? headerEnd : headerEnd + ((flags & 0x200) != 0 ? Align(compressed) : compressed);
		long dataEnd = (flags & 0x80) != 0 ? metadataOffset : fileSize;
		long unpacked = 0;
		for (int i = 0; i < blocks.Length; i++)
		{
			int rawSize = ReadInt(table);
			int packedSize = ReadInt(table);
			int blockFlags = BinaryPrimitives.ReverseEndianness(table.ReadUInt16());
			if (
				rawSize <= 0
				|| rawSize > 64 * 1024 * 1024
				|| packedSize <= 0
				|| packedSize > dataEnd - packed
				|| (blockFlags & ~63) != 0
				|| (blockFlags & 63) is not (0 or 2 or 3 or 5 or 6 or 7 or 8 or 9)
			)
				throw new InvalidDataException($"{Name}: invalid block {i} sizes or flags.");
			blocks[i] = new(packed, unpacked, packedSize, rawSize, blockFlags);
			packed += packedSize;
			unpacked += rawSize;
		}
		if (packed != dataEnd)
			throw new InvalidDataException($"{Name}: block extents do not match archive size.");
		// UnityPlayer 0x181CFA330: v10 nodes have no path strings; each record is BE u64,u64,u32.
		int nodeCount = ReadCount(table, 20);
		GameArchiveEntry[] entries = new GameArchiveEntry[nodeCount];
		long[] offsets = new long[nodeCount];
		for (int i = 0; i < entries.Length; i++)
		{
			long offset = ReadLong(table);
			long length = ReadLong(table);
			int nodeFlags = ReadInt(table);
			if (offset < 0 || length < 0 || offset > unpacked || length > unpacked - offset || (nodeFlags & ~7) != 0)
				throw new InvalidDataException($"{Name}: invalid node {i} extent or flags.");
			offsets[i] = offset;
			entries[i] = new(
				i,
				$"{Path.GetFileNameWithoutExtension(Name)}_node_{i}{((nodeFlags & 4) != 0 ? ".assets" : ".resS")}",
				length,
				(nodeFlags & 4) != 0
			);
		}
		int refs = ReadCount(table, 12);
		if (refs != nodeCount)
			throw new InvalidDataException($"{Name}: node relation count mismatch.");
		int[] relations = new int[refs * 3];
		for (int i = 0; i < relations.Length; i++)
		{
			uint node = unchecked((uint)ReadInt(table));
			if (node != uint.MaxValue && node >= nodeCount)
				throw new InvalidDataException($"{Name}: invalid related node index.");
			relations[i] = unchecked((int)node);
		}
		if (table.BaseStream.Position != table.BaseStream.Length)
			throw new InvalidDataException($"{Name}: trailing block table data.");
		return new(blocks, entries, offsets, relations);
	}

	public override IEnumerable<GameArchiveEntry> GetResources(GameArchiveEntry entry)
	{
		Index index = _index.Value;
		return index
			.Relations.AsSpan(entry.Index * 3, 3)
			.ToArray()
			.Where(n => n >= 0 && !index.Entries[n].IsSerialized)
			.Distinct()
			.Select(n => index.Entries[n]);
	}

	public override FileBase ReadEntry(GameArchiveEntry entry, Predicate<int>? readObjectData = null)
	{
		FileBase file = base.ReadEntry(entry, readObjectData);
		if (file is not SerializedFile serialized)
			return file;
		GameArchiveEntry? image = GetResources(entry).LastOrDefault();
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
		AbcTypeTrees typeTrees = new();
		foreach (ObjectInfo original in serialized.Objects)
		{
			ObjectInfo item = original;
			if (readObjectData is null || readObjectData(item.TypeID))
			{
				typeTrees.Apply(ref item);
			}
			// Resource paths are absent from the player tree; the archive relation supplies them.
			item.SourceResourcePath = image?.Name;
			item.SourceResourceSize = image?.Length;
			builder.Objects.Add(item);
		}
		SerializedFile result = builder.Build();
		result.Name = file.Name;
		result.FilePath = file.FilePath;
		return result;
	}

	public override Stream OpenEntry(GameArchiveEntry entry)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		Index index = _index.Value;
		if ((uint)entry.Index >= index.Entries.Length || index.Entries[entry.Index] != entry)
			throw new ArgumentException("Entry does not belong to this archive.", nameof(entry));
		return new EntryStream(this, index.Offsets[entry.Index], entry.Length);
	}

	public override void Dispose()
	{
		lock (_gate)
		{
			_disposed = true;
			_cachedBlock = null;
		}
	}

	public override void ReleaseCachedData()
	{
		lock (_gate)
		{
			_cachedBlock = null;
			_cachedBlockIndex = -1;
		}
	}

	private int Read(long position, Span<byte> destination)
	{
		lock (_gate)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			Block[] blocks = _index.Value.Blocks;
			int lo = 0,
				hi = blocks.Length - 1;
			while (lo < hi)
			{
				int mid = (lo + hi + 1) / 2;
				if (blocks[mid].UnpackedOffset <= position)
					lo = mid;
				else
					hi = mid - 1;
			}
			Block block = blocks[lo];
			if (_cachedBlockIndex != lo)
			{
				using Stream file = File.OpenRead(FilePath);
				file.Position = block.PackedOffset;
				byte[] packed = new byte[block.PackedSize];
				file.ReadExactly(packed);
				byte[] unpacked = new byte[block.RawSize];
				int written = block.Flags switch
				{
					_ when block.PackedSize == block.RawSize => Copy(packed, unpacked),
					0 => Copy(packed, unpacked),
					2 or 3 => LZ4Codec.Decode(packed, unpacked),
					>= 5 and <= 9 => checked((int)OodleDecompressor.Decompress(packed, unpacked)),
					_ => throw new NotSupportedException($"{Name}: unsupported block compression {block.Flags}."),
				};
				if (written != block.RawSize)
					throw new InvalidDataException($"{Name}: invalid decoded block {lo} length.");
				_cachedBlock = unpacked;
				_cachedBlockIndex = lo;
				DecodedBlockCount++;
			}
			int offset = checked((int)(position - block.UnpackedOffset));
			int count = Math.Min(destination.Length, block.RawSize - offset);
			_cachedBlock!.AsSpan(offset, count).CopyTo(destination);
			return count;
		}
	}

	private static int Copy(byte[] source, byte[] target)
	{
		source.CopyTo(target, 0);
		return source.Length;
	}

	private static long Align(long value) => (value + 15) & ~15L;

	private static int ReadInt(BinaryReader reader) => BinaryPrimitives.ReverseEndianness(reader.ReadInt32());

	private static long ReadLong(BinaryReader reader) => BinaryPrimitives.ReverseEndianness(reader.ReadInt64());

	private static int ReadCount(BinaryReader reader, int stride)
	{
		int count = ReadInt(reader);
		if (count < 0 || count > (reader.BaseStream.Length - reader.BaseStream.Position) / stride)
			throw new InvalidDataException("ABC table count exceeds metadata bounds.");
		return count;
	}

	private static string ReadString(BinaryReader reader)
	{
		List<byte> bytes = [];
		for (int i = 0; i < 64; i++)
		{
			byte b = reader.ReadByte();
			if (b == 0)
				return Encoding.UTF8.GetString(bytes.ToArray());
			bytes.Add(b);
		}
		throw new InvalidDataException("ABC header string exceeds 64 bytes.");
	}

	private sealed record Index(Block[] Blocks, GameArchiveEntry[] Entries, long[] Offsets, int[] Relations);

	private sealed record Block(long PackedOffset, long UnpackedOffset, int PackedSize, int RawSize, int Flags);

	private sealed class EntryStream(AbcArchive archive, long offset, long length) : Stream
	{
		private bool _closed;
		public override bool CanRead => !_closed;
		public override bool CanSeek => !_closed;
		public override bool CanWrite => false;
		public override long Length => length;
		public override long Position { get; set; }

		public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

		public override int Read(Span<byte> buffer)
		{
			ObjectDisposedException.ThrowIf(_closed, this);
			if (Position < 0 || Position > length)
				throw new IOException("ABC entry position is out of range.");
			int count = (int)Math.Min(buffer.Length, length - Position);
			if (count == 0)
				return 0;
			int read = archive.Read(offset + Position, buffer[..count]);
			Position += read;
			return read;
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			long next = origin switch
			{
				SeekOrigin.Begin => offset,
				SeekOrigin.Current => checked(Position + offset),
				SeekOrigin.End => checked(length + offset),
				_ => throw new ArgumentOutOfRangeException(nameof(origin)),
			};
			if (next < 0 || next > length)
				throw new IOException("ABC entry seek exceeds bounds.");
			return Position = next;
		}

		protected override void Dispose(bool disposing)
		{
			_closed = true;
			base.Dispose(disposing);
		}

		public override void Flush() { }

		public override void SetLength(long value) => throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}
}
