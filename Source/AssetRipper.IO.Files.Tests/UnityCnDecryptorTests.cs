using AssetRipper.IO.Files.BundleFiles;

namespace AssetRipper.IO.Files.Tests;

public class UnityCnDecryptorTests
{
	private static readonly byte[] Key = Convert.FromHexString("000102030405060708090a0b0c0d0e0f");
	private static readonly byte[] Table = Convert.FromHexString("f92255ec9781317e912dab76073f5f7c101112131415161718191a1b1c1d1e1f");
	private static readonly byte[] Signature = Convert.FromHexString("5290860098964d4ee46b9d65aee7fbf4101112131415161718191a1b1c1d1e1f");

	[Test]
	[Arguments(0, "696162635211053132333435")]
	[Arguments(255, "696162639655493132333435")]
	public async Task RestoresControlsAndPreservesLiterals(int blockIndex, string encrypted)
	{
		// The table reverses nibble values; the four offset columns contain 0..15.
		UnityCnDecryptor cipher = new(Key, "UnityCN test sig"u8, Table, Signature);
		byte[] block = Convert.FromHexString(encrypted);
		cipher.DecryptBlock(block, blockIndex);
		await Assert.That(block.SequenceEqual(Convert.FromHexString("306162630300503132333435"))).IsTrue();
	}

	[Test]
	public async Task RestoresExtendedLiteralAndMatchLengths()
	{
		UnityCnDecryptor cipher = new(Key, "UnityCN test sig"u8, Table, Signature);
		byte[] literals = [0xA9, 0x50, .. "abcdefghijklmnopqrst"u8];
		cipher.DecryptBlock(literals, 0);
		await Assert.That(literals.SequenceEqual((byte[])[0xF0, 5, .. "abcdefghijklmnopqrst"u8])).IsTrue();
		byte[] match = Convert.FromHexString("6a6162635211dc053132333435");
		cipher.DecryptBlock(match, 0);
		await Assert.That(match.SequenceEqual(Convert.FromHexString("3f616263030001503132333435"))).IsTrue();
	}

	[Test]
	[Arguments("a9")]
	[Arguments("b9")]
	[Arguments("9999")]
	public async Task RejectsTruncatedControlsAndLiteralRanges(string encrypted)
	{
		UnityCnDecryptor cipher = new(Key, "UnityCN test sig"u8, Table, Signature);
		byte[] block = Convert.FromHexString(encrypted);
		await Assert.That(() => cipher.DecryptBlock(block, 0)).Throws<InvalidDataException>();
	}

	[Test]
	public async Task RejectsWrongKeyAndIdentifier()
	{
		await Assert.That(() => new UnityCnDecryptor(new byte[16], "UnityCN test sig"u8, Table, Signature)).Throws<InvalidDataException>();
		await Assert.That(() => new UnityCnDecryptor(Key, "different marker"u8, Table, Signature)).Throws<InvalidDataException>();
	}
}
