using AssetRipper.IO.Endian;
using AssetRipper.IO.Files.SerializedFiles;
using AssetRipper.IO.Files.SerializedFiles.Parser;
using AssetRipper.IO.Files.SerializedFiles.Parser.TypeTrees;
using AssetRipper.IO.Files.Streams.Smart;

namespace AssetRipper.IO.Files.Tests;

public class SerializedFilePartialReadTests
{
	[Test]
	[Arguments(EndianType.LittleEndian, 1)]
	[Arguments(EndianType.LittleEndian, 7)]
	[Arguments(EndianType.BigEndian, 1)]
	[Arguments(EndianType.BigEndian, 7)]
	public async Task ReadsTypeTreeAndFollowingDataAcrossPartialReads(EndianType endianType, int maximumReadSize)
	{
		SerializedType type = new() { TypeID = 1, ScriptTypeIndex = -1 };
		type.OldType.Nodes.Add(new TypeTreeNode("GameObject", "Base", 0, false));
		type.OldType.Nodes.Add(new TypeTreeNode("int", "m_ComponentCount", 1, false));
		type.OldType.RebuildStringBuffer();
		SerializedFileBuilder builder = new()
		{
			Generation = FormatVersion.LargeFilesSupport,
			Version = new(2022, 3, 62),
			Platform = BuildTarget.StandaloneWin64Player,
			EndianType = endianType,
			HasTypeTree = true,
			UserInformation = "after-type-tree",
		};
		builder.Types.Add(type);
		using MemoryStream output = new();
		builder.Build().Write(output);
		using SmartStream input = SmartStream.Create(new PartialReadStream(output.ToArray(), maximumReadSize));
		SerializedFile parsed = SerializedFileScheme.Default.Read(input, "", "test.assets");
		await Assert.That(parsed.Types.ToArray().SequenceEqual([type])).IsTrue();
		await Assert.That(parsed.UserInformation).IsEqualTo(builder.UserInformation);
	}

	private sealed class PartialReadStream(byte[] data, int maximumReadSize) : MemoryStream(data, false)
	{
		public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, maximumReadSize));

		public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(buffer.Length, maximumReadSize)]);
	}
}
