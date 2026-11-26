using System.Buffers.Binary;
using System.Text;
using AssetRipper.Import.GamePlugins;

namespace AssetRipper.Tests;

public class UnityFsGameArchiveTests
{
	[Test]
	public async Task ReadsConcatenatedBundlesAcrossBlocksAndSeeks()
	{
		string path = Path.GetTempFileName();
		try
		{
			using (Stream file = File.Create(path))
			{
				WriteBundle(file, false);
				WriteBundle(file, true);
			}
			using UnityFsGameArchive archive = new(path);
			await Assert.That(archive.Entries.Count).IsEqualTo(4);
			foreach (GameArchiveEntry entry in archive.Entries.Where(static entry => entry.IsSerialized))
			{
				using Stream stream = archive.OpenEntry(entry);
				byte[] content = new byte[8];
				stream.ReadExactly(content);
				await Assert.That(Encoding.ASCII.GetString(content)).IsEqualTo("cdefghij");
				_ = stream.Seek(-3, SeekOrigin.End);
				stream.ReadExactly(content.AsSpan(0, 3));
				await Assert.That(Encoding.ASCII.GetString(content, 0, 3)).IsEqualTo("hij");
				GameArchiveEntry resource = archive.GetResources(entry).Single();
				await Assert.That(resource.Index).IsEqualTo(entry.Index + 1);
			}
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Test]
	public async Task ReadsDataOnlyWhenAnEntryIsOpened()
	{
		string path = Path.GetTempFileName();
		try
		{
			using (Stream file = File.Create(path))
			{
				WriteBundle(file, false, invalidBlockSize: true);
			}
			using UnityFsGameArchive archive = new(path);
			using Stream stream = archive.OpenEntry(archive.Entries[0]);
			await Assert.That(() => stream.ReadByte()).Throws<InvalidDataException>();
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task RejectsTruncatedBundlesAndInvalidNodeExtents(bool invalidNode)
	{
		string path = Path.GetTempFileName();
		try
		{
			using (Stream file = File.Create(path))
			{
				WriteBundle(file, false, invalidNode: invalidNode);
				if (!invalidNode)
				{
					file.SetLength(file.Length - 1);
				}
			}
			using UnityFsGameArchive archive = new(path);
			await Assert.That(() => archive.Entries).Throws<InvalidDataException>();
		}
		finally
		{
			File.Delete(path);
		}
	}

	private static void WriteBundle(Stream output, bool tableAtEnd, bool invalidBlockSize = false, bool invalidNode = false)
	{
		using MemoryStream table = new();
		using BinaryWriter info = new(table, Encoding.UTF8, true);
		info.Write(new byte[16]);
		WriteInt(info, 3);
		for (int i = 0; i < 3; i++)
		{
			WriteInt(info, i == 0 && invalidBlockSize ? 5 : 4);
			WriteInt(info, 4);
			info.Write((short)0);
		}
		WriteInt(info, 2);
		WriteLong(info, invalidNode ? 100 : 2);
		WriteLong(info, 8);
		WriteInt(info, 4);
		info.Write("test.assets\0"u8);
		WriteLong(info, 10);
		WriteLong(info, 2);
		WriteInt(info, 0);
		info.Write("test.resS\0"u8);

		using MemoryStream bundle = new();
		using BinaryWriter writer = new(bundle, Encoding.UTF8, true);
		writer.Write("UnityFS\0"u8);
		WriteInt(writer, 8);
		writer.Write("5.x.x\0"u8);
		writer.Write("2022.3.62f3\0"u8);
		long sizePosition = bundle.Position;
		WriteLong(writer, 0);
		WriteInt(writer, (int)table.Length);
		WriteInt(writer, (int)table.Length);
		WriteInt(writer, tableAtEnd ? 0x2C0 : 0x240);
		while (bundle.Position % 16 != 0)
		{
			writer.Write((byte)0);
		}
		if (!tableAtEnd)
		{
			writer.Write(table.ToArray());
			while (bundle.Position % 16 != 0)
			{
				writer.Write((byte)0);
			}
		}
		writer.Write("abcdefghijkl"u8);
		if (tableAtEnd)
		{
			writer.Write(table.ToArray());
		}
		bundle.Position = sizePosition;
		WriteLong(writer, bundle.Length);
		output.Write(bundle.ToArray());
	}

	private static void WriteInt(BinaryWriter writer, int value) => writer.Write(BinaryPrimitives.ReverseEndianness(value));

	private static void WriteLong(BinaryWriter writer, long value) => writer.Write(BinaryPrimitives.ReverseEndianness(value));
}
