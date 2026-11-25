/*
	Copyright 2017 Perfare - https://github.com/Perfare/Il2CppDumper
	Copyright 2017-2021 Katy Coe - http://www.djkaty.com - https://github.com/djkaty

	All rights reserved.
*/
using AssetRipper.Il2CppInspector.Bin2Object;

namespace AssetRipper.Il2CppInspector;

[Flags]
public enum Elf : uint
{
	// elf_header.m_dwFormat
	ELFMAG = 0x464c457f, // "\177ELF"

	// elf_header.e_machine
	EM_386 = 0x03,
	EM_ARM = 0x28,
	EM_X86_64 = 0x3E,
	EM_AARCH64 = 0xB7,

	// elf_header.m_arch
	ELFCLASS32 = 1,
	ELFCLASS64 = 2,

	// elf_header.e_Type
	ET_EXEC = 2,

	// PHTs
	PT_LOAD = 1,
	PT_DYNAMIC = 2,
	PF_X = 1,
	PF_W = 2,
	PF_R = 4,

	// SHTs
	SHT_PROGBITS = 1,
	SHT_SYMTAB = 2,
	SHT_STRTAB = 3,
	SHT_RELA = 4,
	SHT_NOBITS = 8,
	SHT_REL = 9,
	SHT_DYNSYM = 11,

	// SHNs
	SHN_UNDEF = 0,

	// STTs
	STT_NOTYPE = 0,
	STT_OBJECT = 1,
	STT_FUNC = 2,
	STT_SECTION = 3,
	STT_FILE = 4,
	STT_COMMON = 5,
	STT_LOOS = 10,
	STT_HIOS = 12,
	STT_LOPROC = 13,
	STT_SPARC_REGISTER = 13,
	STT_HIPROC = 15,

	// SHFs
	SHF_ALLOC = 2,
	SHF_EXECINSTR = 4,

	// dynamic sections
	DT_PLTGOT = 3,
	DT_HASH = 4,
	DT_STRTAB = 5,
	DT_SYMTAB = 6,
	DT_RELA = 7,
	DT_RELASZ = 8,
	DT_RELAENT = 9,
	DT_STRSZ = 10,
	DT_INIT = 12,
	DT_FINI = 13,
	DT_REL = 17,
	DT_RELSZ = 18,
	DT_RELENT = 19,
	DT_JMPREL = 23,
	DT_INIT_ARRAY = 25,
	DT_FINI_ARRAY = 26,
	DT_INIT_ARRAYSZ = 27,
	DT_PREINIT_ARRAY = 32,
	DT_MOVETAB = 0x6ffffefe,
	DT_VERDEF = 0x6ffffffc,
	DT_VERNEED = 0x6ffffffe,
	DT_SYMINFO = 0x6ffffeff,

	// relocation types
	R_ARM_ABS32 = 2,
	R_ARM_REL32 = 3,
	R_ARM_COPY = 20,
	R_AARCH64_ABS64 = 0x101,
	R_AARCH64_PREL64 = 0x104,
	R_AARCH64_GLOB_DAT = 0x401,
	R_AARCH64_JUMP_SLOT = 0x402,
	R_AARCH64_RELATIVE = 0x403,
	R_386_32 = 1,
	R_386_PC32 = 2,
	R_386_GLOB_DAT = 6,
	R_386_JMP_SLOT = 7,
	R_X86_64_RELATIVE = 8,
	R_X86_64_RELATIVE64 = 38,
	R_AMD64_64 = 1,
}

public class ElfHeader<TWord>
	where TWord : struct
{
	// 0x7f followed by ELF in ascii
	public uint MDwFormat;

	// 1 - 32 bit
	// 2 - 64 bit
	public byte MArch;

	// 1 - little endian
	// 2 - big endian
	public byte MEndian;

	// 1 is original elf format
	public byte MVersion;

	// set based on OS, refer to OSABI enum
	public byte MOsabi;

	// refer to elf documentation
	public byte MOsabiVer;

	// unused
	[ArrayLength(FixedSize = 7)]
	public byte[] EPad = []; //byte[7]

	// 1 - relocatable
	// 2 - executable
	// 3 - shared
	// 4 - core
	public ushort EType;

	// refer to isa enum
	public ushort EMachine;
	public uint EVersion;
	public TWord EEntry;
	public TWord EPhoff;
	public TWord EShoff;
	public uint EFlags;
	public ushort EEhsize;
	public ushort EPhentsize;
	public ushort EPhnum;
	public ushort EShentsize;
	public ushort EShnum;
	public ushort EShtrndx;
}

public interface IElfPhdr<TWord>
	where TWord : struct
{
	uint PType { get; }

	TWord POffset { get; set; }

	TWord PFilesz { get; set; }

	TWord PMemsz { get; }

	TWord PVaddr { get; set; }

	uint PFlags { get; }
}

public class Elf32Phdr : IElfPhdr<uint>
{
	public uint PType => FPType;
	public uint POffset
	{
		get => FPOffset;
		set => FPOffset = value;
	}
	public uint PFilesz
	{
		get => FPFilesz;
		set => FPFilesz = value;
	}
	public uint PVaddr
	{
		get => FPVaddr;
		set => FPVaddr = value;
	}
	public uint PFlags => FPFlags;
	public uint PMemsz => FPMemsz;

	public uint FPType;
	public uint FPOffset;
	public uint FPVaddr;
	public uint PPaddr;
	public uint FPFilesz;
	public uint FPMemsz;
	public uint FPFlags;
	public uint PAlign;
}

public class Elf64Phdr : IElfPhdr<ulong>
{
	public uint PType => FPType;
	public ulong POffset
	{
		get => FPOffset;
		set => FPOffset = value;
	}
	public ulong PFilesz
	{
		get => FPFilesz;
		set => FPFilesz = value;
	}
	public ulong PMemsz => FPMemsz;
	public ulong PVaddr
	{
		get => FPVaddr;
		set => FPVaddr = value;
	}
	public uint PFlags => FPFlags;

	public uint FPType;
	public uint FPFlags;
	public ulong FPOffset;
	public ulong FPVaddr;
	public ulong PPaddr;
	public ulong FPFilesz;
	public ulong FPMemsz;
	public ulong PAlign;
}

public class ElfShdr<TWord>
	where TWord : struct
{
	public uint ShName;
	public uint ShType;
	public TWord ShFlags;
	public TWord ShAddr;
	public TWord ShOffset;
	public TWord ShSize;
	public uint ShLink;
	public uint ShInfo;
	public TWord ShAddralign;
	public TWord ShEntsize;
}

public interface IElfSym<TWord>
	where TWord : struct
{
	uint StName { get; }

	TWord StValue { get; }

	ushort StShndx { get; }

	Elf StInfo { get; }

	Elf Type { get; }
}

public class Elf32Sym : IElfSym<uint>
{
	public uint StName => FStName;
	public uint StValue => FStValue;
	public ushort StShndx => FStShndx;
	public Elf StInfo => (Elf)FStInfo;
	public Elf Type => (Elf)(FStInfo & 0xf);

	public uint FStName;
	public uint FStValue;
	public uint StSize;
	public byte FStInfo;
	public byte StOther;
	public ushort FStShndx;
}

public class Elf64Sym : IElfSym<ulong>
{
	public uint StName => FStName;
	public ulong StValue => FStValue;
	public ushort StShndx => FStShndx;
	public Elf StInfo => (Elf)FStInfo;
	public Elf Type => (Elf)(FStInfo & 0xf);

	public uint FStName;
	public byte FStInfo;
	public byte StOther;
	public ushort FStShndx;
	public ulong FStValue;
	public ulong StSize;
}

public class ElfDynamic<TWord>
	where TWord : struct
{
	public TWord DTag;
	public TWord DUn;
}

public class ElfRel<TWord>
	where TWord : struct
{
	public TWord ROffset;
	public TWord RInfo;
}

public class ElfRela<TWord>
	where TWord : struct
{
	public TWord ROffset;
	public TWord RInfo;
	public TWord RAddend;
}
