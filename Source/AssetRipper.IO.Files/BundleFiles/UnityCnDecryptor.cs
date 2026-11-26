using System.Security.Cryptography;

namespace AssetRipper.IO.Files.BundleFiles;

/// <summary>Decodes UnityCN's AES-protected substitution tables and encrypted LZ4 control bytes.</summary>
public sealed class UnityCnDecryptor
{
	private readonly byte[] _inverse = new byte[16];
	private readonly byte[] _offsets = new byte[16];

	public UnityCnDecryptor(
		ReadOnlySpan<byte> key,
		ReadOnlySpan<byte> signature,
		ReadOnlySpan<byte> encryptedTable,
		ReadOnlySpan<byte> encryptedSignature
	)
	{
		if (key.Length != 16 || signature.Length != 16)
		{
			throw new ArgumentException("UnityCN requires a 16-byte key and signature.");
		}
		if (encryptedTable.Length != 32 || encryptedSignature.Length != 32)
		{
			throw new InvalidDataException("UnityCN encryption records must contain a 16-byte ciphertext and IV.");
		}
		using Aes aes = Aes.Create();
		aes.Key = key.ToArray();
		Span<byte> decoded = stackalloc byte[16];
		DecodeRecord(aes, encryptedSignature, decoded);
		if (!decoded.SequenceEqual(signature))
		{
			throw new InvalidDataException("UnityCN archive signature does not match the supplied key and identifier.");
		}
		DecodeRecord(aes, encryptedTable, decoded);
		int seen = 0;
		for (int i = 0; i < 16; i++)
		{
			int value = (i & 1) == 0 ? decoded[i / 2] >> 4 : decoded[i / 2] & 15;
			if ((seen & (1 << value)) != 0)
			{
				throw new InvalidDataException("UnityCN substitution table is not a permutation.");
			}
			seen |= 1 << value;
			_inverse[value] = (byte)i;
			_offsets[(i % 4) * 4 + i / 4] = (byte)((i & 1) == 0 ? decoded[8 + i / 2] >> 4 : decoded[8 + i / 2] & 15);
		}
	}

	private static void DecodeRecord(Aes aes, ReadOnlySpan<byte> record, Span<byte> output)
	{
		// UnityCN encrypts the IV with AES-128, then XORs that keystream with the record.
		_ = aes.EncryptEcb(record[16..], output, PaddingMode.None);
		for (int i = 0; i < output.Length; i++)
		{
			output[i] ^= record[i];
		}
	}

	/// <summary>Restores an LZ4 block in place. The sequence counter starts at the bundle-local block index.</summary>
	public void DecryptBlock(Span<byte> data, int blockIndex)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(blockIndex);
		int position = 0;
		uint sequence = (uint)blockIndex;
		while (position < data.Length)
		{
			uint index = sequence++;
			byte token = DecodeByte(data, ref position, index++);
			int literalLength = token >> 4;
			if (literalLength == 15)
			{
				byte extension;
				do
				{
					extension = DecodeByte(data, ref position, index++);
					if (literalLength > data.Length - extension)
					{
						throw new InvalidDataException("UnityCN LZ4 literal length exceeds its block.");
					}
					literalLength += extension;
				} while (extension == 255);
			}
			if (literalLength > data.Length - position)
			{
				throw new InvalidDataException("UnityCN LZ4 literals exceed their block.");
			}
			position += literalLength;
			if (position == data.Length)
			{
				break;
			}
			_ = DecodeByte(data, ref position, index++);
			_ = DecodeByte(data, ref position, index++);
			if ((token & 15) == 15)
			{
				while (DecodeByte(data, ref position, index++) == 255) { }
			}
		}
	}

	private byte DecodeByte(Span<byte> data, ref int position, uint index)
	{
		if (position >= data.Length)
		{
			throw new InvalidDataException("Truncated UnityCN LZ4 control sequence.");
		}
		int shift = _offsets[index & 3] + _offsets[4 + ((index >> 2) & 3)] + _offsets[8 + ((index >> 4) & 3)] + _offsets[12 + ((index >> 6) & 3)];
		byte value = data[position];
		byte decoded = (byte)(((_inverse[value >> 4] - shift) & 15) << 4 | ((_inverse[value & 15] - shift) & 15));
		data[position++] = decoded;
		return decoded;
	}
}
