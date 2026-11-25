/*
	Copyright 2019-2021 Katy Coe - http://www.djkaty.com - https://github.com/djkaty

	All rights reserved.
*/
using AssetRipper.Il2CppInspector.Next;

namespace AssetRipper.Il2CppInspector;

internal class Il2CppBinaryX64 : Il2CppBinary
{
	public Il2CppBinaryX64(IFileFormatStream stream, EventHandler<string>? statusCallback = null)
		: base(stream, statusCallback) { }

	public Il2CppBinaryX64(IFileFormatStream stream, uint codeRegistration, uint metadataRegistration, EventHandler<string>? statusCallback = null)
		: base(stream, codeRegistration, metadataRegistration, statusCallback) { }

	// Format of 64-bit LEA:
	// 0x48/0x4C - REX prefix signifying 64-bit mode with 64-bit operand size (REX prefix bits: Volume 2A, page 2-9) (bit 2 is register bit 3)
	// 8x8D - LEA opcode (8D /r, LEA r64, m)
	// 0xX5 - bottom 3 bits = 101 to indicate subsequent operand is a 32-bit displacement; middle 3 bits = register number; top 2 bits = 00
	// Bytes 03-06 - 32-bit displacement
	// Register numbers: 00b = RAX, 01b = RCX, 10b = RDX, 11b = RBX
	// See: https://software.intel.com/sites/default/files/managed/39/c5/325462-sdm-vol-1-2abcd-3abcd.pdf
	// Chapter 2.1, 2.1.3, 2.1.5 table 2-2, page 3-537
	// NOTE: There is a chance of false positives because of x86's variable instruction length architecture
	private (int foundOffset, int reg, uint operand)? FindLea(byte[] buff, int offset, int searchDistance)
	{
		// Find first LEA but don't search too far (either 0x48 0x8D, or 0x4C 0x8D)
		int i,
			index;
		for (i = offset, index = 0; i < offset + searchDistance && i < buff.Length && index < 2; i++)
		{
			if (index == 1 && buff[i] != 0x8D)
			{
				index = 0;
			}
			else if (index != 0 || buff[i] == 0x48 || buff[i] == 0x4C)
			{
				++index;
			}
		}

		if (index < 2)
		{
			return null;
		}

		(int reg, uint operand)? lea = GetLea(buff, i - 2);
		return lea is { } instruction ? (i - 2, instruction.reg, instruction.operand) : null;
	}

	private (int reg, uint operand)? GetLea(byte[] buff, int offset)
	{
		if ((buff[offset] != 0x48 && buff[offset] != 0x4C) || buff[offset + 1] != 0x8D)
		{
			return null;
		}

		// Found LEA RnX, [RIP + disp32]
		int reg = ((buff[offset + 2] >> 3) & 7) + ((buff[offset] << 1) & 8);
		uint operand = BitConverter.ToUInt32(buff, offset + 3);
		return (reg, operand);
	}

	// REX.W + 0x89 - for a 5-byte mov r/m64,r64
	// Volume 2B, page 4-35
	private bool IsMovRM64R64(byte[] buff, int offset = 0) => buff[offset] == 0x48 && buff[offset + 1] == 0x89;

	// REX 0x40 to set 64-bit mode with 64-bit register size, register bit 3 in REX bit 0; bottom 3 bits of opcode are register bits 0-2
	// or the same thing without REX prefix
	// Volume 2B, page 4-511
	private bool IsPushR64(byte[] buff, int offset = 0) =>
		((buff[offset] == 0x40 || buff[offset] == 0x41) && buff[offset + 1] >= 0x50 && buff[offset + 1] <= 0x57)
		|| (buff[offset] >= 0x50 && buff[offset] <= 0x57);

	// push rbp is a one-byte instruction encoded as 0x55
	// mov rbp, rsp is a 3-byte instruction encoded as 0x48 0x89 0xE5
	private bool IsPrologue(byte[] buff, int offset = 0) =>
		IsPushR64(buff) && buff[offset + 1] == 0x48 && buff[offset + 2] == 0x89 && buff[offset + 3] == 0xE5;

	// 0b0100_0X0Y to set 64-bit mode, 0x31 or 0x33 for XOR, 0b11_XXX_YYY for register numbers
	// Volume 2C, page 5-612
	private (int reg_op1, int reg_op2)? GetXorR64R64(byte[] buff, int offset = 0)
	{
		if (
			(buff[offset] & 0b1111_1010) != 0b_0100_0000
			|| (buff[offset + 1] != 0x31 && buff[offset + 1] != 0x33)
			|| (buff[offset + 2] & 0b1100_0000) != 0b1100_0000
		)
		{
			return null;
		}

		return (
			((buff[offset] & 0b0000_0100) << 1) + ((buff[offset + 2] & 0b0011_1000) >> 3),
			((buff[offset] & 0b0000_0001) << 3) + (buff[offset + 2] & 0b0000_0111)
		);
	}

	// 0x31 or 0x33 for XOR, 0b11_XXX_YYY for register numbers
	// Volume 2C, page 5-612
	private (int reg_op1, int reg_op2)? GetXorR32R32(byte[] buff, int offset = 0)
	{
		if ((buff[offset] != 0x31 && buff[offset] != 0x33) || (buff[offset + 1] & 0b1100_0000) != 0b1100_0000)
		{
			return null;
		}

		return ((buff[offset + 1] & 0b0011_1000) >> 3, buff[offset + 1] & 0b0000_0111);
	}

	protected override (ulong, ulong) ConsiderCode(IFileFormatStream image, uint loc)
	{
		// Setup
		int buffSize = 0x76; // minimum number of bytes to process the longest expected function
		int leaSize = 7; // the length of an LEA instruction with a 64-bit register operand and a 32-bit memory operand
		int xor64Size = 3; // the length of a XOR instruction of two 64-bit registers
		int xor32Size = 2; // the length of a XOR instruction of two 32-bit registers
		int pushSize = 2; // the length of a PUSH instruction with a 64-bit register
		int offset = 0;
		int rax = 0,
			rbx = 3,
			rcx = 1,
			rdx = 2,
			rsi = 6,
			rdi = 7; // R8 = 8
		ulong pCgr = 0; // the point to the code registration function
		image.Position = loc;
		byte[] buff = image.ReadBytes(buffSize);
		// We have seen two versions of the initializer:
		// 1. Regular version
		// 2. Inlined version with il2cpp::utils::RegisterRuntimeInitializeAndCleanup(CallbackFunction, CallbackFunction, order)
		// Version 1 passes "this" in rcx and the arguments in rdx (our wanted pointer), r8d (always zero) and r9d (always zero)
		//           or "this" in rdi, and the arguments in rsi (our wanted pointer), edx (always zero) and ecx (always zero)
		// Version 2 has a standard prologue and loads the wanted pointer into rax or rbp (lea rax/rbp)
		// Check for regular version
		// Generalize it as follows:
		// - each instruction must be lea r64, imm32 or xor r32, r32
		// - xors must always have the same register for both operands
		// - lea that can't be mapped into the file is the pointer to 'this', otherwise it's the pointer to the init function
		// - the last instruction should always be jmp (not currently enforced)
		// - function length should not be longer than 5 instructions (two leas, two xors and one jmp)
		for (int instructions = 0; instructions < 4; instructions++)
		{
			image.CancellationToken.ThrowIfCancellationRequested();
			// All allowed instruction types
			(int reg_op1, int reg_op2)? xor32 = GetXorR32R32(buff, offset);
			(int reg_op1, int reg_op2)? xor64 = GetXorR64R64(buff, offset);
			(int reg, uint operand)? lea = GetLea(buff, offset);
			if (xor32 != null && xor32.Value.reg_op1 == xor32.Value.reg_op2)
			{
				offset += xor32Size;
			}
			else if (xor64 != null && xor64.Value.reg_op1 == xor64.Value.reg_op2)
			{
				offset += xor64Size;
			}
			else if (lea != null)
			{
				offset += leaSize;
				if (pCgr == 0)
				{
					try
					{
						// We may have found Il2CppCodegenRegistration(void)
						pCgr = image.GlobalOffset + loc + (ulong)offset + lea.Value.operand;
						image.MapVATR(pCgr);
					}
					catch (InvalidOperationException)
					{
						// this pointer
						pCgr = 0;
					}
				}
			}
			else
			{
				// not lea or xor
				pCgr = 0;
				break;
			}
		}

		// Check for inlined version
		if (pCgr == 0)
		{
			// Check for prologue
			// - A sequence of 0 or more mov [rsp+argX], rXX followed by 1 or more push rXX
			offset = 0;
			while (IsMovRM64R64(buff, offset))
			{
				image.CancellationToken.ThrowIfCancellationRequested();
				offset += 5;
			}

			if (IsPushR64(buff, offset))
			{
				// Linear sweep for LEA
				(int foundOffset, int reg, uint operand)? leaInlined = FindLea(buff, pushSize, buffSize - pushSize);
				if (leaInlined != null)
				{
					pCgr = image.GlobalOffset + loc + (uint)leaInlined.Value.foundOffset + (uint)leaSize + leaInlined.Value.operand;
				}
			}
		}

		// Assume we've found the pointer to Il2CppCodegenRegistration(void) and jump there
		if (pCgr != 0)
		{
			try
			{
				Image.Position = Image.MapVATR(pCgr);
			}
			// Couldn't map virtual address to data in file, so it's not this function
			catch (InvalidOperationException)
			{
				pCgr = 0;
			}
		}

		// Find the first 2 LEAs which we'll hope contain pointers to CodeRegistration and MetadataRegistration
		// There are two options here:
		// 1. il2cpp::vm::MetadataCache::Register is called directly with arguments in rcx, rdx, r8 or rdi, rsi, rdx (lea, lea, lea, jmp)
		// 2. The two functions being inlined. The arguments are loaded sequentially into rax after the prologue
		if (pCgr != 0)
		{
			int buff2Size = 0x50;
			byte[] buff2 = image.ReadBytes(buffSize);
			offset = 0;
			Dictionary<(int index, ulong address), int> leas = [];
			// Find the first three LEAs in the function
			while (offset + leaSize < buff2Size && leas.Count < 3)
			{
				image.CancellationToken.ThrowIfCancellationRequested();
				(int foundOffset, int reg, uint operand)? nextLea = FindLea(buff2, offset, buff2Size - (offset + leaSize));
				// Use the original pointer found, not the file location + GlobalOffset because the data may be in a different section
				if (nextLea != null)
				{
					leas.Add((leas.Count, pCgr + (uint)nextLea.Value.foundOffset + (uint)leaSize + nextLea.Value.operand), nextLea.Value.reg);
				}

				offset = nextLea?.foundOffset + leaSize ?? buff2Size;
			}

			if ((image.Version < MetadataVersions.V210 && leas.Count == 2) || (image.Version >= MetadataVersions.V210 && leas.Count == 3))
			{
				// Register-based argument passing?
				ulong leaRSI = leas.FirstOrDefault(l => l.Value == rsi).Key.address;
				ulong leaRDI = leas.FirstOrDefault(l => l.Value == rdi).Key.address;
				if (leaRSI != 0 && leaRDI != 0)
				{
					return (leaRDI, leaRSI);
				}

				ulong leaRCX = leas.FirstOrDefault(l => l.Value == rcx).Key.address;
				ulong leaRDX = leas.FirstOrDefault(l => l.Value == rdx).Key.address;
				if (leaRCX != 0 && leaRDX != 0)
				{
					return (leaRCX, leaRDX);
				}

				// RAX sequential loading? If so, take the first two arguments
				ulong[] leasRAX = [.. leas.Where(l => l.Value == rax).OrderBy(l => l.Key.index).Select(l => l.Key.address)];
				if (leasRAX.Length > 1)
				{
					return (leasRAX[0], leasRAX[1]);
				}
			}
		}

		// If no initializer is found, we may be looking at a DT_INIT function which calls its own function table manually
		// In the sample we have seen (PlayStation 4), this function runs through two function tables:
		// 1. Start address of table loaded into rbx, pointer past end of table in r12 (lea rbx; lea r12)
		// 2. Pointer to final address of 2nd table loaded into rbx (lea rbx), runs backwards (8 bytes per entry) until finding 0xFFFFFFFF_FFFFFFFF
		// The strategy: find these LEAs, acquire and merge the two function tables, then call ourselves in a loop to check each function address
		// Expect function prologue and at least 3 64-bit register pushes (there are probably more)
		if (!IsPrologue(buff) || !IsPushR64(buff, 4) || !IsPushR64(buff, 6) || !IsPushR64(buff, 8))
		{
			return (0, 0);
		}

		// Find the start and end addresses of the first function table
		(int foundOffset, int reg, uint operand)? leaOfStart = FindLea(buff, 10, buffSize - 10);
		if (leaOfStart == null || leaOfStart.Value.reg != rbx) // Most be lea rbx
		{
			return (0, 0);
		}

		(int foundOffset, int reg, uint operand)? leaOfEnd = FindLea(
			buff,
			leaOfStart.Value.foundOffset + leaSize,
			buffSize - (leaOfStart.Value.foundOffset + leaSize)
		);
		if (leaOfEnd == null || leaOfEnd.Value.reg == rbx) // Must be lea with any register besides rbx
		{
			return (0, 0);
		}

		long ptrStart1 = leaOfStart.Value.foundOffset + leaSize + leaOfStart.Value.operand;
		long ptrEnd1 = leaOfEnd.Value.foundOffset + leaSize + leaOfEnd.Value.operand;
		// Find the address of the last item in the second function table
		(int foundOffset, int reg, uint operand)? leaOfLastItem = FindLea(
			buff,
			leaOfEnd.Value.foundOffset + leaSize,
			buffSize - (leaOfEnd.Value.foundOffset + leaSize)
		);
		if (leaOfLastItem == null || leaOfLastItem.Value.reg != rbx) // Must be lea rbx
		{
			return (0, 0);
		}

		int entrySize = 8; // 64-bit array entries
		long ptrEnd2 = leaOfLastItem.Value.foundOffset + leaSize + leaOfLastItem.Value.operand + entrySize;
		// Work backwards to find the address of the first item in the second function table
		long ptrStart2 = ptrEnd2;
		while (image.ReadUInt64(image.MapVATR((ulong)ptrStart2)) != 0xFFFF_FFFF_FFFF_FFFF)
		{
			image.CancellationToken.ThrowIfCancellationRequested();
			ptrStart2 -= entrySize;
		}

		ptrStart2 += entrySize;
		// Acquire both function tables
		long[] funcs1 = image.ReadMappedWordArray((ulong)ptrStart1, (int)(ptrEnd1 - ptrStart1) / entrySize);
		long[] funcs2 = image.ReadMappedWordArray((ulong)ptrStart2, (int)(ptrEnd2 - ptrStart2) / entrySize);
		// Check every function
		IEnumerable<long> funcs = funcs1.Concat(funcs2);
		foreach (long pFunc in funcs)
		{
			image.CancellationToken.ThrowIfCancellationRequested();
			(ulong, ulong) result = ConsiderCode(image, image.MapVATR((ulong)pFunc));
			if (result != (0, 0))
			{
				return result;
			}
		}

		return (0, 0);
	}
}
