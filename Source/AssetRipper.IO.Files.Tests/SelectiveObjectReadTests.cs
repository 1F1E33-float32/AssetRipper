using System.Buffers.Binary;
using AssetRipper.IO.Endian;
using AssetRipper.IO.Files.SerializedFiles;
using AssetRipper.IO.Files.SerializedFiles.Parser;
using AssetRipper.IO.Files.Streams.Smart;

namespace AssetRipper.IO.Files.Tests;

public class SelectiveObjectReadTests
{
	[Test]
	public async Task ReadsSelectedPayloadsWithoutTouchingOtherObjectData()
	{
		byte[] data = BuildFile();
		long dataOffset = BinaryPrimitives.ReadInt64BigEndian(data.AsSpan(32));
		using SmartStream stream = SmartStream.Create(new GuardedStream(data, dataOffset + 8));
		SerializedFile file = new();
		file.Read(stream, static classID => classID == 142);
		ObjectInfo[] objects = file.Objects.ToArray();
		await Assert.That(objects.Select(static item => item.TypeID).SequenceEqual([142, 28])).IsTrue();
		await Assert.That(objects[0].ObjectData.AsSpan().SequenceEqual(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })).IsTrue();
		await Assert.That(objects[1].ObjectData.Length).IsEqualTo(0);
		await Assert.That(objects[1].FileID).IsEqualTo(2);
	}

	[Test]
	public async Task ValidatesSkippedPayloadBounds()
	{
		byte[] data = BuildFile();
		using SmartStream stream = SmartStream.CreateMemory(data[..^1]);
		SerializedFile file = new();
		await Assert.That(() => file.Read(stream, static _ => false)).Throws<InvalidDataException>();
	}

	private static byte[] BuildFile()
	{
		SerializedFileBuilder builder = new()
		{
			Generation = FormatVersion.LargeFilesSupport,
			Version = new(2022, 3, 62),
			EndianType = EndianType.LittleEndian,
		};
		foreach (int classID in new[] { 142, 28 })
		{
			SerializedType type = new() { TypeID = classID, ScriptTypeIndex = -1 };
			builder.Objects.Add(
				new ObjectInfo(type)
				{
					FileID = builder.Types.Count + 1,
					SerializedTypeIndex = builder.Types.Count,
					ObjectData = [1, 2, 3, 4, 5, 6, 7, 8],
				}
			);
			builder.Types.Add(type);
		}
		using MemoryStream stream = new();
		builder.Build().Write(stream);
		return stream.ToArray();
	}

	private sealed class GuardedStream(byte[] data, long forbiddenOffset) : MemoryStream(data, false)
	{
		public override int Read(byte[] buffer, int offset, int count)
		{
			if (Position + count > forbiddenOffset)
			{
				throw new InvalidOperationException("Unselected object payload was read.");
			}
			return base.Read(buffer, offset, count);
		}
	}
}
