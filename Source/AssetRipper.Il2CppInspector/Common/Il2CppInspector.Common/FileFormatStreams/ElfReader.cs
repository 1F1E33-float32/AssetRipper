/*
	Copyright 2017 Perfare - https://github.com/Perfare/Il2CppDumper
	Copyright 2017-2021 Katy Coe - http://www.djkaty.com - https://github.com/djkaty

	All rights reserved.
*/
using System.Reflection;

namespace AssetRipper.Il2CppInspector;

public interface IElfReader : IFileFormatStream
{
	// Get PLT address
	uint GetPLTAddress();
}

public class ElfReader32 : ElfReader<uint, Elf32Phdr, Elf32Sym, ElfReader32, Convert32>
{
	public ElfReader32()
	{
		ElfReloc.GetRelocType = info => (Elf)(info & 0xff);
		ElfReloc.GetSymbolIndex = info => info >> 8;
	}

	public override int Bits => 32;
	protected override Elf ArchClass => Elf.ELFCLASS32;

	protected override void WriteWord(uint value) => Write(value);
}

public class ElfReader64 : ElfReader<ulong, Elf64Phdr, Elf64Sym, ElfReader64, Convert64>
{
	public ElfReader64()
	{
		ElfReloc.GetRelocType = info => (Elf)(info & 0xffff_ffff);
		ElfReloc.GetSymbolIndex = info => info >> 32;
	}

	public override int Bits => 64;
	protected override Elf ArchClass => Elf.ELFCLASS64;

	protected override void WriteWord(ulong value) => Write(value);
}

public abstract class ElfReader<TWord, TPHdr, TSym, TReader, TConvert> : FileFormatStream<TReader>, IElfReader
	where TWord : struct
	where TPHdr : IElfPhdr<TWord>, new()
	where TSym : IElfSym<TWord>, new()
	where TConvert : IWordConverter<TWord>, new()
	where TReader : FileFormatStream<TReader>
{
	public override string DefaultFilename => "libil2cpp.so";
	public override string Format => Bits == 32 ? "ELF" : "ELF64";
	public override string Arch =>
		(Elf)ElfHeader.EMachine switch
		{
			Elf.EM_386 => "x86",
			Elf.EM_ARM => "ARM",
			Elf.EM_X86_64 => "x64",
			Elf.EM_AARCH64 => "ARM64",
			_ => "Unsupported",
		};
	public override int Bits => (ElfHeader.MArch == (uint)Elf.ELFCLASS64) ? 64 : 32;
	public TPHdr[] PHT { get; private set; } = [];
	public ElfShdr<TWord>[] SHT { get; private set; } = [];
	public ElfDynamic<TWord>[] DynamicTable { get; private set; } = [];
	public ElfHeader<TWord> ElfHeader { get; private set; } = new();

	public Dictionary<string, ElfShdr<TWord>> SectionByName = [];

	public ElfShdr<TWord>? GetSection(Elf sectionIndex) => SHT.FirstOrDefault(x => x.ShType == (uint)sectionIndex);

	public IEnumerable<ElfShdr<TWord>> GetSections(Elf sectionIndex) => SHT.Where(x => x.ShType == (uint)sectionIndex);

	public TPHdr? GetProgramHeader(Elf programIndex) => PHT.FirstOrDefault(x => x.PType == (uint)programIndex);

	public ElfDynamic<TWord>? GetDynamicEntry(Elf dynamicIndex) => DynamicTable?.FirstOrDefault(x => (Elf)_conv.ULong(x.DTag) == dynamicIndex);

	private ElfDynamic<TWord> RequireDynamicEntry(Elf tag) =>
		GetDynamicEntry(tag) ?? throw new InvalidDataException($"ELF dynamic table is missing {tag}.");

	private readonly Dictionary<string, Symbol> _symbolTable = [];
	private List<Export> _exports = [];
	private readonly List<(uint Start, uint End)> _reverseMapExclusions = [];
	private bool _preferPHT;
	private bool _isMemoryImage;
	protected abstract Elf ArchClass { get; }

	protected abstract void WriteWord(TWord value);
	private readonly TConvert _conv = new();

	// Internal relocation entry helper
	protected class ElfReloc
	{
		public Elf Type;
		public TWord Offset;
		public TWord? Addend;
		public TWord SymbolTable;
		public TWord SymbolIndex;

		// Equality based on target address
		public override bool Equals(object? obj) => obj is ElfReloc reloc && Equals(reloc);

		public bool Equals(ElfReloc other)
		{
			return Offset.Equals(other.Offset);
		}

		public override int GetHashCode() => Offset.GetHashCode();

		public ElfReloc(ElfRel<TWord> rel, TWord symbolTable)
		{
			Offset = rel.ROffset;
			Type = GetRelocType(rel.RInfo);
			SymbolIndex = GetSymbolIndex(rel.RInfo);
			SymbolTable = symbolTable;
		}

		public ElfReloc(ElfRela<TWord> rela, TWord symbolTable)
			: this(new ElfRel<TWord> { RInfo = rela.RInfo, ROffset = rela.ROffset }, symbolTable) => Addend = rela.RAddend;

		public static Func<TWord, Elf> GetRelocType = static _ =>
			throw new InvalidOperationException("ELF relocation architecture is uninitialized.");
		public static Func<TWord, TWord> GetSymbolIndex = static _ =>
			throw new InvalidOperationException("ELF relocation architecture is uninitialized.");
	}

	// See also: https://docs.microsoft.com/en-us/dotnet/csharp/language-reference/operators/sizeof
	private int Sizeof(Type type)
	{
		int size = 0;
		foreach (FieldInfo i in type.GetTypeInfo().GetFields())
		{
			if (i.FieldType == typeof(byte) || i.FieldType == typeof(sbyte))
			{
				size += sizeof(byte);
			}

			if (i.FieldType == typeof(long) || i.FieldType == typeof(ulong))
			{
				size += sizeof(ulong);
			}

			if (i.FieldType == typeof(int) || i.FieldType == typeof(uint))
			{
				size += sizeof(uint);
			}

			if (i.FieldType == typeof(short) || i.FieldType == typeof(ushort))
			{
				size += sizeof(ushort);
			}
		}

		return size;
	}

	protected override bool Init()
	{
		ElfHeader = ReadObject<ElfHeader<TWord>>();
		// Check for magic bytes
		if ((Elf)ElfHeader.MDwFormat != Elf.ELFMAG)
		{
			return false;
		}

		// Ensure supported architecture
		if ((Elf)ElfHeader.MArch != ArchClass)
		{
			return false;
		}

		// Get PHT and SHT
		PHT = ReadArray<TPHdr>(_conv.Long(ElfHeader.EPhoff), ElfHeader.EPhnum);
		try
		{
			SHT = ReadArray<ElfShdr<TWord>>(_conv.Long(ElfHeader.EShoff), ElfHeader.EShnum);
		}
		catch (Exception exception)
			when (exception is EndOfStreamException or InvalidDataException or ArgumentOutOfRangeException or OverflowException)
		{
			_preferPHT = true;
			SHT = [];
		}

		// Determine if SHT is valid
		// These can happen as a result of conversions from other formats to ELF,
		// or if the SHT has been deliberately stripped
		if (!SHT.Any())
		{
			_preferPHT = true;
		}
		else if (SHT.All(s => _conv.ULong(s.ShAddr) == 0ul))
		{
			_preferPHT = true;
		}
		// Check for overlaps in sections that are memory-allocated on load
		else
		{
			IEnumerable<ulong> shtShouldBeOrdered = SHT.Where(s => ((Elf)_conv.Int(s.ShFlags) & Elf.SHF_ALLOC) == Elf.SHF_ALLOC)
				.OrderBy(s => s.ShAddr)
				.Select(s => new[] { _conv.ULong(s.ShAddr), _conv.ULong(s.ShAddr) + _conv.ULong(s.ShSize) })
				.SelectMany(s => s);
			// No sections that map into memory - this is probably a dumped image
			if (!shtShouldBeOrdered.Any())
			{
				// If the first file offset of the first PHT is zero, assume a dumped image
				if (PHT.Any(t => _conv.ULong(t.PVaddr) == 0ul))
				{
					_isMemoryImage = true;
				}

				_preferPHT = true;
			}
			// Sections overlap - this can happen if the ELF has been improperly generated or processed by another tool
			else
			{
				bool shtOverlap = shtShouldBeOrdered.Aggregate((x, y) => x <= y ? y : ulong.MaxValue) == ulong.MaxValue;
				if (shtOverlap)
				{
					_preferPHT = true;
				}
			}
		}

		// Dumped images must be rebased
		if (_isMemoryImage)
		{
			if (_loadOptions == null || _loadOptions.ImageBase == 0)
			{
				throw new InvalidOperationException("To load a dumped ELF image, you must specify the image base virtual address");
			}
		}

		// Rebase if requested (whether dumped or not) and treat it as a memory image,
		// disabling processing of relocations, symbols and decryption
		if (_loadOptions != null && _loadOptions.ImageBase != 0)
		{
			_isMemoryImage = true;
			Rebase(_conv.FromULong(_loadOptions.ImageBase));
		}

		// Get dynamic table if it exists (must be done after rebasing)
		if (GetProgramHeader(Elf.PT_DYNAMIC) is TPHdr pt_dynamic)
		{
			// Important: do not use p_offset here!
			// Only load sections should be loaded, which should also include the memory region that contains the dynamic section.
			// This just provides the virtual address of the section.
			// Some binaries may use the offset here to point to a fake version of the dynamic section,
			// making relocation resolution and subsequent analysis fail.
			// Reference for Android:
			// phdr_table_get_dynamic_section, https://cs.android.com/android/platform/superproject/main/+/main:bionic/linker/linker_phdr.cpp
			ulong dynamicAddr = _conv.ULong(pt_dynamic.PVaddr);
			int dynamicSize = (int)(_conv.Long(pt_dynamic.PFilesz) / Sizeof(typeof(ElfDynamic<TWord>)));
			DynamicTable = ReadMappedArray<ElfDynamic<TWord>>(dynamicAddr, dynamicSize);
		}

		// Get offset of code section
		TPHdr codeSegment = PHT.First(x => ((Elf)x.PFlags & Elf.PF_X) == Elf.PF_X);
		GlobalOffset = _conv.ULong(_conv.Sub(codeSegment.PVaddr, codeSegment.POffset));
		// Nothing more to do if the image is a memory dump (no section names, relocations or decryption)
		if (_isMemoryImage)
		{
			return true;
		}

		// Get section name mappings if there are any
		// This is currently only used to defeat the XOR obfuscation handled below
		// Note: There can be more than one section with the same name, or unnamed; we take the first section with a given name
		if (ElfHeader.EShtrndx < SHT.Length)
		{
			TWord pStrtab = SHT[ElfHeader.EShtrndx].ShOffset;
			foreach (ElfShdr<TWord> section in SHT)
			{
				try
				{
					string name = ReadNullTerminatedString(_conv.Long(pStrtab) + section.ShName);
					SectionByName.TryAdd(name, section);
				}
				catch (ArgumentOutOfRangeException)
				{
					// Names have been stripped, maybe previously dumped image
					break;
				}
			}
		}

		// Find all relocations; target address => (rela header (rels are converted to rela), symbol table base address, is rela?)
		HashSet<ElfReloc> rels = [];
		StatusUpdate("Finding relocations");
		// Relocations in dynamic section
		if (GetDynamicEntry(Elf.DT_REL) is ElfDynamic<TWord> dt_rel)
		{
			int dt_rel_count = _conv.Int(_conv.Div(RequireDynamicEntry(Elf.DT_RELSZ).DUn, RequireDynamicEntry(Elf.DT_RELENT).DUn));
			int dt_item_size = Sizeof(typeof(ElfRel<TWord>));
			uint dt_start = MapVATR(_conv.ULong(dt_rel.DUn));
			ElfRel<TWord>[] dt_rel_list = ReadArray<ElfRel<TWord>>(dt_start, dt_rel_count);
			TWord dt_symtab = RequireDynamicEntry(Elf.DT_SYMTAB).DUn;
			_reverseMapExclusions.Add((dt_start, (uint)(dt_start + dt_rel_count * dt_item_size - 1)));
			rels.UnionWith(from rel in dt_rel_list select new ElfReloc(rel, dt_symtab));
		}

		if (GetDynamicEntry(Elf.DT_RELA) is ElfDynamic<TWord> dt_rela)
		{
			int dt_rela_count = _conv.Int(_conv.Div(RequireDynamicEntry(Elf.DT_RELASZ).DUn, RequireDynamicEntry(Elf.DT_RELAENT).DUn));
			int dt_item_size = Sizeof(typeof(ElfRela<TWord>));
			uint dt_start = MapVATR(_conv.ULong(dt_rela.DUn));
			ElfRela<TWord>[] dt_rela_list = ReadArray<ElfRela<TWord>>(dt_start, dt_rela_count);
			TWord dt_symtab = RequireDynamicEntry(Elf.DT_SYMTAB).DUn;
			_reverseMapExclusions.Add((dt_start, (uint)(dt_start + dt_rela_count * dt_item_size - 1)));
			rels.UnionWith(from rela in dt_rela_list select new ElfReloc(rela, dt_symtab));
		}

		// Process relocations
		uint relsz = (uint)Sizeof(typeof(TSym));
		int currentRel = 0;
		int totalRel = rels.Count;
		ReportRelocations(0);
		foreach (ElfReloc rel in rels)
		{
			if (currentRel != 0 && currentRel % 1000 == 0)
			{
				ReportRelocations(currentRel);
			}

			currentRel++;
			TWord symValue;
			try
			{
				// man this really needs a full overhaul
				symValue = ReadMappedObject<TSym>(_conv.ULong(rel.SymbolTable) + _conv.ULong(rel.SymbolIndex) * relsz).StValue; // S
			}
			catch (InvalidOperationException)
			{
				try
				{
					symValue = ReadObject<TSym>(_conv.Long(rel.SymbolTable) + _conv.Long(rel.SymbolIndex) * relsz).StValue; // S
				}
				catch (InvalidOperationException)
				{
					continue;
				}
			}

			// Ignore relocations into memory addresses not mapped from the image
			try
			{
				Position = MapVATR(_conv.ULong(rel.Offset));
			}
			catch (InvalidOperationException)
			{
				continue;
			}

			// The addend is specified in the struct for rela, and comes from the target location for rel
			TWord addend = rel.Addend ?? ReadObject<TWord>(); // A
			// Only handle relocation types we understand, skip the rest
			// Relocation types from https://docs.oracle.com/cd/E23824_01/html/819-0690/chapter6-54839.html#scrolltoc
			// and https://studfiles.net/preview/429210/page:18/
			// and http://infocenter.arm.com/help/topic/com.arm.doc.ihi0056b/IHI0056B_aaelf64.pdf (AArch64)
			(TWord newValue, bool recognized) result = (rel.Type, (Elf)ElfHeader.EMachine) switch
			{
				(Elf.R_ARM_ABS32, Elf.EM_ARM) => (_conv.Add(symValue, addend), true), // S + A
				(Elf.R_ARM_REL32, Elf.EM_ARM) => (_conv.Add(_conv.Sub(symValue, rel.Offset), addend), true), // S - P + A
				(Elf.R_ARM_COPY, Elf.EM_ARM) => (symValue, true), // S
				(Elf.R_AARCH64_ABS64, Elf.EM_AARCH64) => (_conv.Add(symValue, addend), true), // S + A
				(Elf.R_AARCH64_PREL64, Elf.EM_AARCH64) => (_conv.Sub(_conv.Add(symValue, addend), rel.Offset), true), // S + A - P
				(Elf.R_AARCH64_GLOB_DAT, Elf.EM_AARCH64) => (_conv.Add(symValue, addend), true), // S + A
				(Elf.R_AARCH64_JUMP_SLOT, Elf.EM_AARCH64) => (_conv.Add(symValue, addend), true), // S + A
				(Elf.R_AARCH64_RELATIVE, Elf.EM_AARCH64) => (_conv.Add(symValue, addend), true), // Delta(S) + A
				(Elf.R_386_32, Elf.EM_386) => (_conv.Add(symValue, addend), true), // S + A
				(Elf.R_386_PC32, Elf.EM_386) => (_conv.Sub(_conv.Add(symValue, addend), rel.Offset), true), // S + A - P
				(Elf.R_386_GLOB_DAT, Elf.EM_386) => (symValue, true), // S
				(Elf.R_386_JMP_SLOT, Elf.EM_386) => (symValue, true), // S
				(Elf.R_AMD64_64, Elf.EM_AARCH64) => (_conv.Add(symValue, addend), true), // S + A
				(Elf.R_AMD64_64, Elf.EM_X86_64) => (_conv.Add(symValue, addend), true), // S + A
				(Elf.R_X86_64_RELATIVE, Elf.EM_X86_64) => (addend, true), // S + A
				(Elf.R_X86_64_RELATIVE64, Elf.EM_X86_64) => (addend, true), // S + A
				_ => (default, false),
			};
			if (result.recognized)
			{
				Position = MapVATR(_conv.ULong(rel.Offset));
				WriteWord(result.newValue);
			}
		}

		ReportRelocations(totalRel);
		StatusUpdate($"Processed {totalRel} relocations");
		void ReportRelocations(int completed)
		{
			if (_loadOptions?.ProgressCallback is { } progress)
			{
				progress(new OperationProgress("Processing relocations", completed, totalRel));
			}
			else
			{
				StatusUpdate($"Processing relocations ({(totalRel > 0 ? completed * 100L / totalRel : 100)}%)");
			}
		}

		// Build symbol and export tables
		ProcessSymbols();
		return true;
	}

	// Rebase the image to a new virtual address
	private void Rebase(TWord imageBase)
	{
		// Rebase PHT
		foreach (TPHdr segment in PHT)
		{
			segment.POffset = segment.PVaddr;
			segment.PVaddr = _conv.Add(segment.PVaddr, imageBase);
			segment.PFilesz = segment.PMemsz;
		}

		// Rewrite to stream
		WriteArray(_conv.Long(ElfHeader.EPhoff), PHT);
		IsModified = true;
		// Rebase dynamic table if it exists
		// Note we have to rebase the PHT first to get the correct location to read this
		if (GetProgramHeader(Elf.PT_DYNAMIC) is not TPHdr pt_dynamic)
		{
			return;
		}

		ElfDynamic<TWord>[] dt = ReadArray<ElfDynamic<TWord>>(
			_conv.Long(pt_dynamic.POffset),
			(int)(_conv.Long(pt_dynamic.PFilesz) / Sizeof(typeof(ElfDynamic<TWord>)))
		);
		// Every table containing virtual address pointers
		// https://docs.oracle.com/cd/E19683-01/817-3677/chapter6-42444/index.html
		Elf[] tablesToRebase =
		[
			Elf.DT_PLTGOT,
			Elf.DT_HASH,
			Elf.DT_STRTAB,
			Elf.DT_SYMTAB,
			Elf.DT_RELA,
			Elf.DT_INIT,
			Elf.DT_FINI,
			Elf.DT_REL,
			Elf.DT_JMPREL,
			Elf.DT_INIT_ARRAY,
			Elf.DT_FINI_ARRAY,
			Elf.DT_PREINIT_ARRAY,
			Elf.DT_MOVETAB,
			Elf.DT_VERDEF,
			Elf.DT_VERNEED,
			Elf.DT_SYMINFO,
		];
		// Rebase dynamic tables
		foreach (ElfDynamic<TWord> section in dt.Where(x => tablesToRebase.Contains((Elf)_conv.ULong(x.DTag))))
		{
			section.DUn = _conv.Add(section.DUn, imageBase);
		}

		// Rewrite to stream
		WriteArray(_conv.Long(pt_dynamic.POffset), dt);
	}

	private void ProcessSymbols()
	{
		StatusUpdate("Processing symbols");
		// Three possible symbol tables in ELF files
		List<(TWord offset, TWord count, TWord strings, TWord stringsCount)> pTables = [];
		// String table (a sequence of null-terminated strings, total length in sh_size
		ElfShdr<TWord>? sht_strtab = GetSection(Elf.SHT_STRTAB);
		if (sht_strtab != null)
		{
			// Section header shared object symbol table (.symtab)
			if (GetSection(Elf.SHT_SYMTAB) is ElfShdr<TWord> sht_symtab)
			{
				pTables.Add((sht_symtab.ShOffset, _conv.Div(sht_symtab.ShSize, sht_symtab.ShEntsize), sht_strtab.ShOffset, sht_strtab.ShSize));
			}

			// Section header executable symbol table (.dynsym)
			if (GetSection(Elf.SHT_DYNSYM) is ElfShdr<TWord> sht_dynsym)
			{
				pTables.Add((sht_dynsym.ShOffset, _conv.Div(sht_dynsym.ShSize, sht_dynsym.ShEntsize), sht_strtab.ShOffset, sht_strtab.ShSize));
			}
		}

		// Symbol table in dynamic section (DT_SYMTAB)
		// Normally the same as .dynsym except that .dynsym may be removed in stripped binaries
		// Dynamic string table
		if (GetDynamicEntry(Elf.DT_STRTAB) is ElfDynamic<TWord> dt_strtab)
		{
			if (GetDynamicEntry(Elf.DT_SYMTAB) is ElfDynamic<TWord> dt_symtab)
			{
				if (GetDynamicEntry(Elf.DT_STRSZ) is ElfDynamic<TWord> dt_strsz)
				{
					// Find the next pointer in the dynamic table to calculate the length of the symbol table
					List<ElfDynamic<TWord>> orderedTable = [.. (from x in DynamicTable where _conv.Gt(x.DUn, dt_symtab.DUn) orderby x.DUn select x)];
					if (orderedTable.Count != 0)
					{
						TWord end = orderedTable.First().DUn;
						// Dynamic symbol table
						pTables.Add(
							(
								_conv.FromUInt(MapVATR(_conv.ULong(dt_symtab.DUn))),
								_conv.Div(_conv.Sub(end, dt_symtab.DUn), Sizeof(typeof(TSym))),
								dt_strtab.DUn,
								dt_strsz.DUn
							)
						);
					}
				}
			}
		}

		// Now iterate through all of the symbol and string tables we found to build a full list
		_symbolTable.Clear();
		Dictionary<string, Export> exportTable = [];
		List<(TWord offset, TWord count)> alreadyProcessed = [];
		foreach ((TWord offset, TWord count, TWord strings, TWord stringsCount) pTab in pTables)
		{
			if (alreadyProcessed.Any(x => _conv.ULong(x.offset) == _conv.ULong(pTab.offset)))
			{
				continue;
			}

			alreadyProcessed.Add((pTab.offset, pTab.count));
			TSym[] symbol_table = ReadArray<TSym>(_conv.Long(pTab.offset), _conv.Int(pTab.count));
			foreach (TSym symbol in symbol_table)
			{
				if (_conv.Gt(_conv.FromUInt(symbol.StName), pTab.stringsCount))
				{
					break; // Skip invalid symbol table indices, since we might read past it on obfuscated binaries
				}

				string name;
				try
				{
					name = ReadNullTerminatedString(_conv.Long(pTab.strings) + symbol.StName);
				}
				catch (ArgumentOutOfRangeException)
				{
					// Name has been stripped, maybe previously dumped image
					continue;
				}

				SymbolType type =
					symbol.Type == Elf.STT_FUNC ? SymbolType.Function
					: symbol.Type == Elf.STT_OBJECT || symbol.Type == Elf.STT_COMMON ? SymbolType.Name
					: SymbolType.Unknown;
				if (symbol.StShndx == (ushort)Elf.SHN_UNDEF)
				{
					type = SymbolType.Import;
				}

				// Avoid duplicates
				Symbol symbolItem = new()
				{
					Name = name,
					Type = type,
					VirtualAddress = _conv.ULong(symbol.StValue),
				};
				_symbolTable.TryAdd(name, symbolItem);
				if (symbol.StShndx != (ushort)Elf.SHN_UNDEF)
				{
					exportTable.TryAdd(name, new Export { Name = symbolItem.Name, VirtualAddress = _conv.ULong(symbol.StValue) });
				}
			}
		}

		_exports = [.. exportTable.Values];
	}

	public override Dictionary<string, Symbol> GetSymbolTable() => _symbolTable;

	public override IEnumerable<Export> GetExports() => _exports;

	public override uint[] GetFunctionTable()
	{
		// INIT_ARRAY contains a list of pointers to initialization functions (not all functions in the binary)
		// INIT_ARRAYSZ contains the size of INIT_ARRAY
		// INIT_ARRAY is probably broken in dumped images and resaved dumped images
		if (GetDynamicEntry(Elf.DT_INIT_ARRAY) == null || GetDynamicEntry(Elf.DT_INIT_ARRAYSZ) == null || _isMemoryImage)
		{
			return [];
		}

		uint init = MapVATR(_conv.ULong(RequireDynamicEntry(Elf.DT_INIT_ARRAY).DUn));
		TWord size = RequireDynamicEntry(Elf.DT_INIT_ARRAYSZ).DUn;
		uint[]? init_array = _conv.UIntArray(ReadArray<TWord>(init, _conv.Int(size) / (Bits / 8)));
		// Additionally, check if there is an old-style DT_INIT function and include it in the list if so
		if (GetDynamicEntry(Elf.DT_INIT) != null)
		{
			init_array = [.. init_array, .. _conv.UIntArray([RequireDynamicEntry(Elf.DT_INIT).DUn])];
		}

		return [.. init_array.Select(x => MapVATR(x))];
	}

	public override IEnumerable<Section> GetSections()
	{
		// If the sections have been stripped, use the segment list from the PHT instead
		if (_preferPHT)
		{
			return PHT.Select(p => new Section
			{
				VirtualStart = _conv.ULong(p.PVaddr),
				VirtualEnd = _conv.ULong(p.PVaddr) + _conv.ULong(p.PMemsz) - 1,
				ImageStart = (uint)_conv.Int(p.POffset),
				ImageEnd = (uint)_conv.Int(p.POffset) + (uint)_conv.Int(p.PFilesz) - 1, // Not correct but probably the best we can do
				IsData = ((Elf)p.PFlags & Elf.PF_R) != 0,
				IsExec = ((Elf)p.PFlags & Elf.PF_X) != 0,
				IsBSS = _conv.Int(p.PFilesz) == 0,
				Name = string.Empty,
			});
		}

		// Return sections list
		return SHT.Select(s => new Section
		{
			VirtualStart = _conv.ULong(s.ShAddr),
			VirtualEnd = _conv.ULong(s.ShAddr) + _conv.ULong(s.ShSize) - 1,
			ImageStart = (uint)_conv.Int(s.ShOffset),
			ImageEnd = (uint)_conv.Int(s.ShOffset) + (uint)_conv.Int(s.ShSize) - 1,
			IsData =
				((Elf)_conv.Int(s.ShFlags) & Elf.SHF_ALLOC) == Elf.SHF_ALLOC
				&& ((Elf)_conv.Int(s.ShFlags) & Elf.SHF_EXECINSTR) == 0
				&& (Elf)s.ShType == Elf.SHT_PROGBITS,
			IsExec = ((Elf)_conv.Int(s.ShFlags) & Elf.SHF_EXECINSTR) == Elf.SHF_EXECINSTR && (Elf)s.ShType == Elf.SHT_PROGBITS,
			IsBSS = (Elf)s.ShType == Elf.SHT_NOBITS,
			Name = SectionByName.FirstOrDefault(sbn => _conv.Int(sbn.Value.ShOffset) == _conv.Int(s.ShOffset)).Key ?? string.Empty,
		});
	}

	// Map a virtual address to an offset into the image file. Throws an exception if the virtual address is not mapped into the file.
	// Note if uiAddr is a valid segment but filesz < memsz and the adjusted uiAddr falls between the range of filesz and memsz,
	// an exception will be thrown. This area of memory is assumed to contain all zeroes.
	public override uint MapVATR(ulong uiAddr)
	{
		// Additions in the argument to MapVATR may cause an overflow which should be discarded for 32-bit files
		if (!TryMapVATR(uiAddr, out uint offset))
		{
			throw new InvalidOperationException("Failed to map virtual address");
		}

		return offset;
	}

	public override bool TryMapVATR(ulong uiAddr, out uint fileOffset)
	{
		// Additions in the argument to MapVATR may cause an overflow which should be discarded for 32-bit files
		if (Bits == 32)
		{
			uiAddr &= 0xffff_ffff;
		}

		TPHdr? phtEntry = PHT.FirstOrDefault(x => uiAddr >= _conv.ULong(x.PVaddr) && uiAddr <= _conv.ULong(_conv.Add(x.PVaddr, x.PFilesz)));
		if (phtEntry == null)
		{
			fileOffset = 0;
			return false;
		}

		fileOffset = (uint)(uiAddr - _conv.ULong(_conv.Sub(phtEntry.PVaddr, phtEntry.POffset)));
		return true;
	}

	public override ulong MapFileOffsetToVA(uint offset)
	{
		// Exclude relocation areas
		if (_reverseMapExclusions.Any(r => offset >= r.Start && offset <= r.End))
		{
			return ulong.MaxValue;
		}

		TPHdr section = PHT.First(x => offset >= _conv.Int(x.POffset) && offset < _conv.Int(x.POffset) + _conv.Int(x.PFilesz));
		return _conv.ULong(section.PVaddr) + offset - _conv.ULong(section.POffset);
	}

	// Get the address of the procedure linkage table (.got.plt) which is needed for some disassemblies
	public uint GetPLTAddress() => (uint)_conv.ULong(RequireDynamicEntry(Elf.DT_PLTGOT).DUn);
}
