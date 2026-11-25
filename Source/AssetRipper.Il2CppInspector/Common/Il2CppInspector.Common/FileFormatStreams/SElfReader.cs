/*
	Copyright 2020-2021 Katy Coe - http://www.djkaty.com - https://github.com/djkaty

	All rights reserved.
*/
using AssetRipper.Il2CppInspector.Bin2Object;

namespace AssetRipper.Il2CppInspector;

// Sony PlayStation 4 fake signed ELF reader
// Not compatible with PlayStation 3, PSP or Vita
// References:
// http://hitmen.c02.at/files/yapspd/psp_doc/chap26.html
// https://www.psdevwiki.com/ps3/SELF_-_SPRX#File_Format
// https://www.psdevwiki.com/ps4/SELF_File_Format
// https://www.psxhax.com/threads/ps4-self-spkg-file-format-documentation-detailed-for-scene-devs.6636/
// https://wiki.henkaku.xyz/vita/images/a/a2/Vita_SDK_specifications.pdf
// https://www.psxhax.com/threads/make-fself-gui-for-flat_zs-make_fself-py-script-by-cfwprophet.3494/
public class SElfReader : FileFormatStream<SElfReader>
{
	public override string DefaultFilename => "libil2cpp.so";
	public override string Format => _sceData.ProductType == (ulong)SElfExInfoTypes.PTYPE_FAKE ? "FSELF" : "SELF";
	public override string Arch => "x64";
	public override int Bits => 64;

	private SElfHeader _selfHeader = new();
	private SElfEntry[] _entries = [];
	private SElfSCEData _sceData = new();
	private ElfHeader<ulong> _elfHeader = new();
	private Elf64Phdr[] _pht = [];

	protected override bool Init()
	{
		_selfHeader = ReadObject<SElfHeader>();
		// Check for magic bytes
		if ((SElfConsts)_selfHeader.Magic != SElfConsts.Magic)
		{
			return false;
		}

		if (_selfHeader.Endian != 0x1)
		{
			Endianness = Endianness.Big;
		}

		// Read entries
		_entries = ReadArray<SElfEntry>(_selfHeader.NumberOfEntries);
		// We can't deal with encrypted or compressed segments right now
		if (_entries.Any(e => e.HasBlocks && e.IsEncrypted))
		{
			throw new NotImplementedException("This file contains encrypted segments not currently supported by AssetRipper.Il2CppInspector.");
		}

		if (_entries.Any(e => e.HasBlocks && e.IsDeflated))
		{
			throw new NotImplementedException("This file contains compressed segments not currently supported by AssetRipper.Il2CppInspector.");
		}

		// Read ELF header
		// PS4 files as follows:
		// m_arch = 0x2 (64-bit)
		// m_endian = 0x1 (little endian)
		// m_version = 0x1 (ELF version 1)
		// m_osabi = 0x9 (FreeBSD)
		// e_type = special type, see psdevwiki documentation; probably 0xFE10 or 0xFE18
		// e_machine = 0x3E (x86-64)
		long startOfElf = Position;
		_elfHeader = ReadObject<ElfHeader<ulong>>();
		// Must be one of these supported binary types
		if (
			_elfHeader.EType != (ushort)Elf.ET_EXEC
			&& _elfHeader.EType != (ushort)SElfETypes.ET_SCE_EXEC
			&& _elfHeader.EType != (ushort)SElfETypes.ET_SCE_DYNEXEC
			&& _elfHeader.EType != (ushort)SElfETypes.ET_SCE_DYNAMIC
		)
		{
			return false;
		}

		// There are no sections, but read all the program headers
		// Each segment of type PT_LOAD, PT_SCE_RELRO, PT_SCE_DYNLIBDATA and PT_SCE_COMMENT
		// generates two SELF entries above - one pointing to the ELF segment and one pointing to a digest.
		// Only p_vaddr is used for memory mapping; all other fields are ignored.
		// offset, memsz and filesz are taken from the SELF entries.
		// The digests are all-zero in FSELF files.
		// All other ELF segments are ignored completely.
		_pht = ReadArray<Elf64Phdr>(startOfElf + (long)_elfHeader.EPhoff, _elfHeader.EPhnum);
		// Read extended info
		_sceData = ReadObject<SElfSCEData>(startOfElf + (long)_elfHeader.EPhoff + _elfHeader.EPhentsize * _elfHeader.EPhnum);
		// Get SELF entries which point to segments defined in phdrs
		List<SElfEntry> dataEntries = [.. _entries.Where(e => e.HasBlocks)];
		// Fixup the used phdr entries
		foreach (SElfEntry entry in dataEntries)
		{
			_pht[entry.SegmentIndex].FPFilesz = entry.EncryptedCompressedSize;
			_pht[entry.SegmentIndex].FPOffset = entry.FileOffset;
			_pht[entry.SegmentIndex].FPMemsz = entry.MemorySize;
		}

		// Filter out unused phdr entries
		List<int> phdrIndices = [.. dataEntries.Select(e => (int)e.SegmentIndex)];
		_pht = [.. _pht.Where((e, i) => phdrIndices.Contains(i))];
		// Get offset of code section
		Elf64Phdr codeSegment = _pht.First(x => ((Elf)x.PFlags & Elf.PF_X) == Elf.PF_X);
		GlobalOffset = codeSegment.PVaddr - codeSegment.POffset;
		return true;
	}

	// Only the DT_INIT function equivalent
	public override uint[] GetFunctionTable() => [MapVATR(_elfHeader.EEntry)];

	public override uint MapVATR(ulong uiAddr)
	{
		Elf64Phdr program_header_table = _pht.First(x => uiAddr >= x.PVaddr && uiAddr <= x.PVaddr + x.PFilesz);
		return (uint)(uiAddr - (program_header_table.PVaddr - program_header_table.POffset));
	}

	public override ulong MapFileOffsetToVA(uint offset)
	{
		Elf64Phdr segment = _pht.First(x => offset >= x.POffset && offset < x.POffset + x.PFilesz);
		return segment.PVaddr + offset - segment.POffset;
	}
	// TODO: SElfReader.GetSections()
}
