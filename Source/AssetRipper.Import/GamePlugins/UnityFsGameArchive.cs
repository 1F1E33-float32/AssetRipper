using System.Buffers.Binary;
using System.Text;
using AssetRipper.IO.Files.BundleFiles;
using K4os.Compression.LZ4;

namespace AssetRipper.Import.GamePlugins;

/// <summary>A seekable, lazy archive over one or more concatenated UnityFS bundles, optionally protected by UnityCN.</summary>
public class UnityFsGameArchive : GameArchive
{
	private const int MaximumBlockSize = 64 * 1024 * 1024;
	private readonly byte[] _key;
	private readonly byte[] _signature;
	private readonly Lazy<ArchiveIndex> _index;
	private readonly object _gate = new();
	private byte[]? _cachedBlock;
	private int _cachedBlockIndex = -1;
	private bool _disposed;

	public override string FilePath { get; }
	public override string Name => Path.GetFileName(FilePath);
	public override IReadOnlyList<GameArchiveEntry> Entries => _index.Value.Entries;

	public UnityFsGameArchive(string path, ReadOnlySpan<byte> key = default, ReadOnlySpan<byte> signature = default)
	{
		FilePath = path;
		_key = key.ToArray();
		_signature = signature.ToArray();
		_index = new(ReadIndex);
	}

	private ArchiveIndex ReadIndex()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		try
		{
			using Stream stream = File.OpenRead(FilePath);
			using BinaryReader reader = new(stream, Encoding.UTF8, true);
			List<Block> blocks = [];
			List<GameArchiveEntry> entries = [];
			List<long> offsets = [];
			List<int> bundles = [];
			long bundleStart = 0;
			long unpacked = 0;
			int bundleIndex = 0;
			while (bundleStart < stream.Length)
			{
				stream.Position = bundleStart;
				if (ReadString(reader, 16) != "UnityFS")
				{
					throw new InvalidDataException($"{Name}: expected UnityFS at 0x{bundleStart:X}.");
				}
				int version = ReadInt(reader);
				if (version is < 6 or > 8)
				{
					throw new NotSupportedException($"{Name}: unsupported UnityFS version {version}.");
				}
				_ = ReadString(reader, 64);
				_ = ReadString(reader, 64);
				long size = ReadLong(reader);
				int compressedSize = ReadInt(reader);
				int rawSize = ReadInt(reader);
				int flags = ReadInt(reader);
				if (
					size < stream.Position - bundleStart
					|| size > stream.Length - bundleStart
					|| compressedSize <= 0
					|| rawSize < 24
					|| rawSize > MaximumBlockSize
					|| compressedSize > rawSize
				)
				{
					throw new InvalidDataException($"{Name}: invalid UnityFS header sizes.");
				}
				if ((flags & ~0x6FF) != 0 || (flags & 0x40) == 0)
				{
					throw new NotSupportedException($"{Name}: unsupported UnityFS flags 0x{flags:X}.");
				}
				UnityCnDecryptor? cipher = null;
				if ((flags & 0x400) != 0)
				{
					if (ReadInt(reader) != 0)
					{
						throw new NotSupportedException($"{Name}: unsupported UnityCN encryption version.");
					}
					byte[] table = ReadEncryptionRecord(reader);
					byte[] signature = ReadEncryptionRecord(reader);
					cipher = new(_key, _signature, table, signature);
				}
				long headerEnd = stream.Position - bundleStart;
				if (version >= 7)
				{
					headerEnd = Align(headerEnd);
				}
				long bundleEnd = bundleStart + size;
				long metadataOffset = (flags & 0x80) != 0 ? bundleEnd - compressedSize : bundleStart + headerEnd;
				if (metadataOffset < bundleStart + headerEnd || compressedSize > bundleEnd - metadataOffset)
				{
					throw new InvalidDataException($"{Name}: block table exceeds its bundle.");
				}
				stream.Position = metadataOffset;
				byte[] packedTable = new byte[compressedSize];
				stream.ReadExactly(packedTable);
				byte[] metadata = Decode(packedTable, rawSize, flags & 63);
				using BinaryReader info = new(new MemoryStream(metadata));
				info.BaseStream.Position = 16;
				int blockCount = ReadCount(info, 10);
				long packed = (flags & 0x80) != 0 ? bundleStart + headerEnd : metadataOffset + compressedSize;
				if ((flags & 0x200) != 0)
				{
					packed = bundleStart + Align(packed - bundleStart);
				}
				long dataEnd = (flags & 0x80) != 0 ? metadataOffset : bundleEnd;
				long unpackedStart = unpacked;
				for (int i = 0; i < blockCount; i++)
				{
					int blockRaw = ReadInt(info);
					int blockPacked = ReadInt(info);
					int blockFlags = BinaryPrimitives.ReverseEndianness(info.ReadUInt16());
					if (
						blockRaw <= 0
						|| blockRaw > MaximumBlockSize
						|| blockPacked <= 0
						|| blockPacked > MaximumBlockSize
						|| blockPacked > dataEnd - packed
						|| (blockFlags & ~0x17F) != 0
					)
					{
						throw new InvalidDataException($"{Name}: invalid block {i} size, extent or flags.");
					}
					if ((blockFlags & 63) is not (0 or 2 or 3))
					{
						throw new NotSupportedException($"{Name}: unsupported block compression {blockFlags & 63}.");
					}
					if ((blockFlags & 0x100) != 0 && cipher is null)
					{
						throw new InvalidDataException($"{Name}: encrypted block has no UnityCN header.");
					}
					blocks.Add(new(packed, unpacked, blockPacked, blockRaw, blockFlags & 63, i, (blockFlags & 0x100) != 0 ? cipher : null));
					packed += blockPacked;
					unpacked = checked(unpacked + blockRaw);
				}
				if (packed != dataEnd)
				{
					throw new InvalidDataException($"{Name}: block extents do not match the bundle size.");
				}
				int nodeCount = ReadCount(info, 21);
				for (int i = 0; i < nodeCount; i++)
				{
					long offset = ReadLong(info);
					long length = ReadLong(info);
					int nodeFlags = ReadInt(info);
					string name = ReadString(info, 4096);
					if (
						offset < 0
						|| length < 0
						|| length > unpacked - unpackedStart
						|| offset > unpacked - unpackedStart - length
						|| (nodeFlags & ~7) != 0
						|| name.Length == 0
					)
					{
						throw new InvalidDataException($"{Name}: invalid node {i} extent, flags or name.");
					}
					offsets.Add(unpackedStart + offset);
					bundles.Add(bundleIndex);
					entries.Add(new(entries.Count, name, length, (nodeFlags & 4) != 0));
				}
				if (info.BaseStream.Position != info.BaseStream.Length)
				{
					throw new InvalidDataException($"{Name}: unexpected trailing block table data.");
				}
				bundleStart = bundleEnd;
				bundleIndex++;
			}
			if (bundleIndex == 0)
			{
				throw new InvalidDataException($"{Name}: empty UnityFS archive.");
			}
			return new([.. blocks], [.. entries], [.. offsets], [.. bundles]);
		}
		catch (EndOfStreamException error)
		{
			throw new InvalidDataException($"{Name}: truncated UnityFS archive.", error);
		}
	}

	public override IEnumerable<GameArchiveEntry> GetResources(GameArchiveEntry entry)
	{
		ArchiveIndex index = GetIndex(entry);
		return index.Entries.Where(candidate => !candidate.IsSerialized && index.Bundles[candidate.Index] == index.Bundles[entry.Index]);
	}

	public override Stream OpenEntry(GameArchiveEntry entry)
	{
		ArchiveIndex index = GetIndex(entry);
		return new EntryStream(this, index.Offsets[entry.Index], entry.Length);
	}

	private ArchiveIndex GetIndex(GameArchiveEntry entry)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		ArchiveIndex index = _index.Value;
		if (entry.Index < 0 || entry.Index >= index.Entries.Length || index.Entries[entry.Index] != entry)
		{
			throw new ArgumentException("Entry does not belong to this archive.", nameof(entry));
		}
		return index;
	}

	private int Read(long position, Span<byte> destination)
	{
		lock (_gate)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			Block[] blocks = _index.Value.Blocks;
			int lo = 0;
			int hi = blocks.Length - 1;
			while (lo < hi)
			{
				int mid = lo + (hi - lo + 1) / 2;
				if (blocks[mid].UnpackedOffset <= position)
				{
					lo = mid;
				}
				else
				{
					hi = mid - 1;
				}
			}
			Block block = blocks[lo];
			if (_cachedBlockIndex != lo)
			{
				using Stream stream = File.OpenRead(FilePath);
				stream.Position = block.PackedOffset;
				byte[] packed = new byte[block.PackedSize];
				try
				{
					stream.ReadExactly(packed);
				}
				catch (EndOfStreamException error)
				{
					throw new InvalidDataException($"{Name}: truncated block {lo}.", error);
				}
				if (block.Compression is 2 or 3)
				{
					block.Cipher?.DecryptBlock(packed, block.Index);
				}
				_cachedBlock = Decode(packed, block.RawSize, block.Compression);
				_cachedBlockIndex = lo;
			}
			byte[] decoded = _cachedBlock ?? throw new InvalidOperationException("Decoded bundle block is missing.");
			int offset = checked((int)(position - block.UnpackedOffset));
			int count = Math.Min(destination.Length, decoded.Length - offset);
			decoded.AsSpan(offset, count).CopyTo(destination);
			return count;
		}
	}

	private static byte[] Decode(byte[] packed, int rawSize, int compression)
	{
		if (compression == 0)
		{
			if (packed.Length != rawSize)
			{
				throw new InvalidDataException("UnityFS uncompressed block sizes do not match.");
			}
			return packed;
		}
		if (compression is not (2 or 3))
		{
			throw new NotSupportedException($"Unsupported UnityFS compression {compression}.");
		}
		byte[] decoded = new byte[rawSize];
		if (LZ4Codec.Decode(packed, decoded) != rawSize)
		{
			throw new InvalidDataException("UnityFS LZ4 block does not match its declared uncompressed size.");
		}
		return decoded;
	}

	private static byte[] ReadEncryptionRecord(BinaryReader reader)
	{
		byte[] record = reader.ReadBytes(32);
		if (record.Length != 32 || reader.ReadByte() != 0)
		{
			throw new InvalidDataException("UnityCN encryption record has an invalid length or terminator.");
		}
		return record;
	}

	private static long Align(long value) => checked(value + 15) & ~15L;

	private static int ReadInt(BinaryReader reader) => BinaryPrimitives.ReverseEndianness(reader.ReadInt32());

	private static long ReadLong(BinaryReader reader) => BinaryPrimitives.ReverseEndianness(reader.ReadInt64());

	private static int ReadCount(BinaryReader reader, int stride)
	{
		int count = ReadInt(reader);
		if (count < 0 || count > (reader.BaseStream.Length - reader.BaseStream.Position) / stride)
		{
			throw new InvalidDataException("UnityFS table count exceeds metadata bounds.");
		}
		return count;
	}

	private static string ReadString(BinaryReader reader, int limit)
	{
		List<byte> bytes = [];
		for (int i = 0; i < limit; i++)
		{
			byte value = reader.ReadByte();
			if (value == 0)
			{
				return Encoding.UTF8.GetString(bytes.ToArray());
			}
			bytes.Add(value);
		}
		throw new InvalidDataException("UnityFS string exceeds its permitted length.");
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

	private sealed record ArchiveIndex(Block[] Blocks, GameArchiveEntry[] Entries, long[] Offsets, int[] Bundles);

	private sealed record Block(
		long PackedOffset,
		long UnpackedOffset,
		int PackedSize,
		int RawSize,
		int Compression,
		int Index,
		UnityCnDecryptor? Cipher
	);

	private sealed class EntryStream(UnityFsGameArchive archive, long entryOffset, long length) : Stream
	{
		private bool _closed;
		private long _position;
		public override bool CanRead => !_closed;
		public override bool CanSeek => !_closed;
		public override bool CanWrite => false;
		public override long Length => length;
		public override long Position
		{
			get => _position;
			set
			{
				ObjectDisposedException.ThrowIf(_closed, this);
				if (value < 0 || value > length)
				{
					throw new IOException("UnityFS entry position exceeds its bounds.");
				}
				_position = value;
			}
		}

		public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

		public override int Read(Span<byte> buffer)
		{
			ObjectDisposedException.ThrowIf(_closed, this);
			int count = (int)Math.Min(buffer.Length, length - Position);
			if (count == 0)
			{
				return 0;
			}
			int read = archive.Read(entryOffset + Position, buffer[..count]);
			Position += read;
			return read;
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			Position = origin switch
			{
				SeekOrigin.Begin => offset,
				SeekOrigin.Current => checked(Position + offset),
				SeekOrigin.End => checked(length + offset),
				_ => throw new ArgumentOutOfRangeException(nameof(origin)),
			};
			return Position;
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
