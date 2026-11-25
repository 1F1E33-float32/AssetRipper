/*
	Copyright 2017-2021 Katy Coe - http://www.djkaty.com - https://github.com/djkaty

	All rights reserved.
*/
using System.Text.RegularExpressions;

namespace AssetRipper.Il2CppInspector;

// References:
// PE Header file: https://github.com/dotnet/llilc/blob/master/include/clr/ntimage.h
// PE format specification: https://docs.microsoft.com/en-us/windows/win32/debug/pe-format?redirectedfrom=MSDN
public class PEReader : FileFormatStream<PEReader>
{
	private COFFHeader _coff = new();
	private IPEOptHeader _pe = new PEOptHeader64();
	private PESection[] _sections = [];
	private uint _pFuncTable;

	// Section types we need to rename in obfuscated binaries
	private readonly Dictionary<PE, string> _wantedSectionTypes = new()
	{
		[PE.IMAGE_SCN_MEM_READ | PE.IMAGE_SCN_MEM_EXECUTE | PE.IMAGE_SCN_CNT_CODE] = ".text",
		[PE.IMAGE_SCN_MEM_READ | PE.IMAGE_SCN_CNT_INITIALIZED_DATA] = ".rdata",
		[PE.IMAGE_SCN_MEM_READ | PE.IMAGE_SCN_MEM_WRITE | PE.IMAGE_SCN_CNT_INITIALIZED_DATA] = ".data",
	};
	public override string DefaultFilename => "GameAssembly.dll";
	public override string Format => _pe is PEOptHeader32 ? "PE32" : "PE32+";
	public override string Arch =>
		_coff.Machine switch
		{
			0x8664 => "x64", // IMAGE_FILE_MACHINE_AMD64
			0x1C0 => "ARM", // IMAGE_FILE_MACHINE_ARM
			0xAA64 => "ARM64", // IMAGE_FILE_MACHINE_ARM64
			0x1C4 => "ARM", // IMAGE_FILE_MACHINE_ARMINT (Thumb-2)
			0x14C => "x86", // IMAGE_FILE_MACHINE_I386
			0x1C2 => "ARM", // IMAGE_FILE_MACHINE_THUMB (Thumb)
			_ => "Unsupported",
		};

	// IMAGE_NT_OPTIONAL_HDR64_MAGIC = 0x20B
	// IMAGE_NT_OPTIONAL_HDR32_MAGIC = 0x10B
	// Could also use coff.Characteristics (IMAGE_FILE_32BIT_MACHINE) or coff.Machine
	public override int Bits => (PE)_pe.Magic == PE.IMAGE_NT_OPTIONAL_HDR64_MAGIC ? 64 : 32;
	public override ulong ImageBase => _pe.ImageBase;

	protected override bool Init()
	{
		// Check for MZ signature "MZ"
		if (ReadUInt16() != 0x5A4D)
		{
			return false;
		}

		// Get offset to PE header from DOS header
		Position = ReadUInt32(0x3C);
		// Check PE signature "PE\0\0"
		if (ReadUInt32() != 0x00004550)
		{
			return false;
		}

		// Read COFF Header
		_coff = ReadObject<COFFHeader>();
		// Ensure presence of PE Optional header
		// Size will always be 0x60 (32-bit) or 0x70 (64-bit) + (0x10 ' 0x8) for 16 RVA entries @ 8 bytes each
		if (
			(
				_coff.SizeOfOptionalHeader == 0xE0 ? 32
				: _coff.SizeOfOptionalHeader == 0xF0 ? (int?)64
				: null
			)
			is not int likelyWordSize
		)
		{
			return false;
		}

		// Read PE optional header
		_pe = likelyWordSize switch
		{
			32 => ReadObject<PEOptHeader32>(),
			64 => ReadObject<PEOptHeader64>(),
			_ => throw new InvalidDataException("PE optional header has an unsupported word size."),
		};
		// Confirm architecture magic number matches expected word size
		if ((PE)_pe.Magic != _pe.ExpectedMagic)
		{
			return false;
		}

		// Get IAT size
		uint iATSize = _pe.DataDirectory[12].Size;
		// Get sections table
		_sections = ReadArray<PESection>(_coff.NumberOfSections);
		// Unpacking must be done starting here, one byte after the end of the headers
		// Packed or previously packed with Themida? This is purely for information
		if (_sections.Any(x => x.Name == ".themida")) { }

		// Rename sections if needed (before potentially searching them or rewriting them to the stream)
		foreach (PESection section in _sections.Where(s => _wantedSectionTypes.ContainsKey(s.Characteristics)))
		// Replace section name if blank or all whitespace
		{
			if (Regex.IsMatch(section.Name, @"^\s*$"))
			{
				section.Name = _wantedSectionTypes[section.Characteristics];
			}
		}

		// Get base of code
		GlobalOffset =
			_pe.ImageBase + _pe.BaseOfCode - _sections.FirstOrDefault(x => x.Characteristics.HasFlag(PE.IMAGE_SCN_MEM_EXECUTE))?.PointerToRawData
			?? 0;
		// Function pointer table follows the IAT at the start of .rdata
		PESection? rData = _sections.FirstOrDefault(x => x.Name == ".rdata");
		if (rData != null)
		{
			// Calculate start of function pointer table
			_pFuncTable = rData.PointerToRawData + iATSize;
			// Skip over __guard_check_icall_fptr and __guard_dispatch_icall_fptr if present, then the following zero offset
			Position = _pFuncTable;
			if (_pe is PEOptHeader32)
			{
				while (ReadUInt32() != 0)
				{
					_pFuncTable += 4;
				}

				_pFuncTable += 4;
			}
			else
			{
				while (ReadUInt64() != 0)
				{
					_pFuncTable += 8;
				}

				_pFuncTable += 8;
			}
		}

		// In the fist go round, we signal that this is at least a valid PE file; we don't try to unpack yet
		return true;
	}

	public override uint[] GetFunctionTable()
	{
		if (_pFuncTable == 0)
		{
			return [];
		}

		Position = _pFuncTable;
		List<uint> addrs = [];
		ulong addr;
		// Use TryMapVATR to avoid crash if function table is stripped or corrupted
		// Can happen with packed or previously packed files
		while ((addr = _pe is PEOptHeader32 ? ReadUInt32() : ReadUInt64()) != 0 && TryMapVATR(addr, out uint fileOffset))
		{
			addrs.Add(fileOffset & 0xfffffffc);
		}

		return [.. addrs];
	}

	public override IEnumerable<Export> GetExports()
	{
		RvaEntry exportDirectory = _pe.DataDirectory[0];
		if (exportDirectory.Size == 0)
		{
			return [];
		}

		// Get exports table
		ulong exportTableStart = exportDirectory.VirtualAddress + _pe.ImageBase;
		// Get export RVAs
		PEExportDirectory exportDirectoryTable = ReadObject<PEExportDirectory>(MapVATR(exportTableStart));
		int exportCount = (int)exportDirectoryTable.NumberOfFunctions;
		uint[] exportAddresses = ReadArray<uint>(MapVATR(exportDirectoryTable.AddressOfFunctions + _pe.ImageBase), exportCount);
		// AddressOfFunctions contains RVAs, not file offsets. GlobalOffset also
		// includes the first code section's RVA/raw-file displacement.
		Dictionary<int, Export> exports = exportAddresses
			.Select((a, i) => new Export { Ordinal = (int)(exportDirectoryTable.Base + i), VirtualAddress = _pe.ImageBase + a })
			.ToDictionary(x => x.Ordinal, x => x);
		// Get export names
		int nameCount = (int)exportDirectoryTable.NumberOfNames;
		uint[] namePointers = ReadArray<uint>(MapVATR(exportDirectoryTable.AddressOfNames + _pe.ImageBase), nameCount);
		ushort[] ordinals = ReadArray<ushort>(MapVATR(exportDirectoryTable.AddressOfNameOrdinals + _pe.ImageBase), nameCount);
		for (int i = 0; i < nameCount; i++)
		{
			string name = ReadNullTerminatedString(MapVATR(namePointers[i] + _pe.ImageBase));
			int ordinal = (int)exportDirectoryTable.Base + ordinals[i];
			exports[ordinal].Name = name;
		}

		return exports.Values;
	}

	public override bool TryMapVATR(ulong uiAddr, out uint fileOffset)
	{
		if (uiAddr == 0)
		{
			fileOffset = 0;
			return true;
		}

		PESection? section = _sections.FirstOrDefault(x =>
			uiAddr - _pe.ImageBase >= x.VirtualAddress && uiAddr - _pe.ImageBase < x.VirtualAddress + x.SizeOfRawData
		);
		if (section == null)
		{
			fileOffset = 0;
			return false;
		}

		fileOffset = (uint)(uiAddr - section.VirtualAddress - _pe.ImageBase + section.PointerToRawData);
		return true;
	}

	public override uint MapVATR(ulong uiAddr)
	{
		if (uiAddr == 0)
		{
			return 0;
		}

		PESection section = _sections.First(x =>
			uiAddr - _pe.ImageBase >= x.VirtualAddress && uiAddr - _pe.ImageBase < x.VirtualAddress + x.SizeOfRawData
		);
		return (uint)(uiAddr - section.VirtualAddress - _pe.ImageBase + section.PointerToRawData);
	}

	public override ulong MapFileOffsetToVA(uint offset)
	{
		PESection section = _sections.First(x => offset >= x.PointerToRawData && offset < x.PointerToRawData + x.SizeOfRawData);
		return _pe.ImageBase + section.VirtualAddress + offset - section.PointerToRawData;
	}

	public override IEnumerable<Section> GetSections()
	{
		return _sections.Select(s => new Section
		{
			VirtualStart = _pe.ImageBase + s.VirtualAddress,
			VirtualEnd = _pe.ImageBase + s.VirtualAddress + s.VirtualSize - 1,
			ImageStart = s.PointerToRawData,
			ImageEnd = s.PointerToRawData + s.SizeOfRawData - 1,
			IsData = (s.Characteristics & PE.IMAGE_SCN_CNT_INITIALIZED_DATA) != 0,
			IsExec = (s.Characteristics & PE.IMAGE_SCN_CNT_CODE) != 0,
			IsBSS = (s.Characteristics & PE.IMAGE_SCN_CNT_UNINITIALIZED_DATA) != 0 || s.PointerToRawData == 0u,
			Name = s.Name,
		});
	}
}
