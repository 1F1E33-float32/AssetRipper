namespace AssetRipper.GamePlugins.ABC;

// UnityPlayer 0x181F2B130, 0x181F2B320..0x181F2B960, 0x181F2A680 and 0x181F2ADC0.
// The format uses a modified AES substitution, a modified RC4 payload, then three header rounds.
internal static partial class AbcCipher
{
	public static void Decrypt(Span<byte> data)
	{
		if (data.Length < 16)
		{
			throw new InvalidDataException("ABC encrypted buffer is shorter than its 16-byte seed.");
		}
		unchecked
		{
			for (int i = 0; i < Key.Length; i++)
				data[i & 15] ^= Key[i];
			Span<byte> substitution = stackalloc byte[256];
			for (int i = 0; i < 256; i++)
				substitution[i] = (byte)(Runtime[i] ^ Aesxor[i]);
			Span<byte> keys = stackalloc byte[176];
			Span<byte> state = stackalloc byte[16];
			for (int row = 0; row < 4; row++)
			for (int col = 0; col < 4; col++)
			{
				keys[4 * row + col] = Key[4 * col + row];
				state[4 * row + col] = data[4 * col + row];
			}
			for (int round = 1; round <= 10; round++)
			{
				int previous = (round - 1) * 16;
				for (int row = 0; row < 4; row++)
				{
					byte value = keys[previous + 4 * ((row + 1) & 3) + 3];
					value ^= substitution[value];
					if (row == 0)
						value ^= Rcon[round - 1];
					for (int col = 0; col < 4; col++)
					{
						value ^= keys[previous + 4 * row + col];
						keys[round * 16 + 4 * row + col] = value;
					}
				}
			}
			AddKey(state, keys[..16]);
			Span<byte> copy = stackalloc byte[16];
			for (int round = 1; round <= 10; round++)
			{
				for (int i = 0; i < 16; i++)
					state[i] ^= substitution[state[i]];
				state.CopyTo(copy);
				for (int row = 0; row < 4; row++)
				for (int col = 0; col < 4; col++)
					state[4 * row + col] = copy[4 * row + ((col + row) & 3)];
				if (round != 10)
				{
					state.CopyTo(copy);
					for (int col = 0; col < 4; col++)
					for (int row = 0; row < 4; row++)
						state[4 * row + col] = (byte)(
							Multiply(copy[4 * row + col], 2)
							^ Multiply(copy[4 * ((row + 1) & 3) + col], 3)
							^ copy[4 * ((row + 2) & 3) + col]
							^ copy[4 * ((row + 3) & 3) + col]
						);
				}
				AddKey(state, keys.Slice(round * 16, 16));
			}
			for (int row = 0; row < 4; row++)
			for (int col = 0; col < 4; col++)
				data[4 * col + row] = state[4 * row + col];
			Span<byte> box = stackalloc byte[256];
			for (int i = 0; i < 256; i++)
				box[i] = (byte)(Runtime[i] ^ Rc4Xor[i]);
			int j = 0;
			for (int i = 0; i < 256; i++)
			{
				j = (j + box[i] + data[i & 7]) & 255;
				(box[i], box[j]) = (box[j], box[i]);
			}
			int x = 0;
			j = 0;
			for (int i = 16; i < data.Length; i++)
			{
				x = (x + 1) & 255;
				j = (j + box[x]) & 255;
				(box[x], box[j]) = (box[j], box[x]);
				byte value = box[(box[x] + box[j]) & 255];
				data[i] = (data[8 + (x & 7)] % 3) switch
				{
					0 => (byte)(data[i] ^ value),
					1 => (byte)(data[i] - value),
					_ => (byte)(data[i] + value),
				};
			}
			for (int round = 0; round < 3; round++)
			{
				for (int i = 0; i < 16; i++)
					copy[i] = data[Perm[(2 - round) * 16 + i]];
				for (int i = 0; i < 16; i++)
				{
					int index = copy[i] == 0 ? 0 : Exp[(Log[copy[i]] + Log[Multkey[i & 7]]) % 255];
					data[i] = (byte)(Mix[(i & 3) * 256 + index] ^ Postxor[i & 7]);
				}
			}
		}
	}

	private static void AddKey(Span<byte> state, ReadOnlySpan<byte> key)
	{
		for (int i = 0; i < 16; i++)
			state[i] ^= key[i];
	}

	private static int Multiply(int value, int factor)
	{
		int doubled = ((value << 1) ^ ((value & 128) == 0 ? 0 : 0x11B)) & 255;
		return factor == 2 ? doubled : doubled ^ value;
	}
}
