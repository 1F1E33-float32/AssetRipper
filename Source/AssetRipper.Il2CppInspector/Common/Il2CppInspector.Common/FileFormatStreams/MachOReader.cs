/*
	Copyright 2017-2021 Katy Coe - http://www.djkaty.com - https://github.com/djkaty

	All rights reserved.
*/
using AssetRipper.Il2CppInspector.Bin2Object;
using AssetRipper.Il2CppInspector.VersionedSerialization;

namespace AssetRipper.Il2CppInspector;

public class MachOReader32 : MachOReader<uint, MachOReader32, Convert32>
{
	public override int Bits => 32;

	protected override bool CheckMagicLE(MachO magic) => magic == MachO.MH_MAGIC;

	protected override bool CheckMagicBE(MachO magic) => magic == MachO.MH_CIGAM;

	protected override MachO LcSegment => MachO.LC_SEGMENT;

	public override uint MapVATR(ulong uiAddr)
	{
		MachOSection<uint> section = _machoSections.First(x =>
			uiAddr >= x.Address && uiAddr <= x.Address + x.Size && x.Name != "__bss" && x.Name != "__common"
		);
		return (uint)uiAddr - (section.Address - section.ImageOffset);
	}

	public override ulong MapFileOffsetToVA(uint offset)
	{
		MachOSection<uint> section = _machoSections.First(x => offset >= x.ImageOffset && offset < x.ImageOffset + x.Size);
		return section.Address + offset - section.ImageOffset;
	}
}

public class MachOReader64 : MachOReader<ulong, MachOReader64, Convert64>
{
	public override int Bits => 64;

	protected override bool CheckMagicLE(MachO magic) => magic == MachO.MH_MAGIC_64;

	protected override bool CheckMagicBE(MachO magic) => magic == MachO.MH_CIGAM_64;

	protected override MachO LcSegment => MachO.LC_SEGMENT_64;

	public override uint MapVATR(ulong uiAddr)
	{
		MachOSection<ulong> section = _machoSections.First(x =>
			uiAddr >= x.Address && uiAddr <= x.Address + x.Size && x.Name != "__bss" && x.Name != "__common"
		);
		return (uint)(uiAddr - (section.Address - section.ImageOffset));
	}

	public override ulong MapFileOffsetToVA(uint offset)
	{
		MachOSection<ulong> section = _machoSections.First(x => offset >= x.ImageOffset && offset < x.ImageOffset + x.Size);
		return section.Address + offset - section.ImageOffset;
	}
}

// We need this convoluted generic TReader declaration so that "static T FileFormatReader.Load(Stream)"
// is inherited to MachOReader32/64 with a correct definition of T
public abstract class MachOReader<TWord, TReader, TConvert> : FileFormatStream<TReader>
	where TWord : struct
	where TReader : FileFormatStream<TReader>
	where TConvert : IWordConverter<TWord>, new()
{
	private readonly TConvert _conv = new();
	private MachOHeader<TWord> _header = new();
	protected readonly List<MachOSection<TWord>> _machoSections = [];
	private readonly List<Section> _sections = [];
	private Dictionary<string, Symbol> _symbolTable = [];
	private MachOSection<TWord> _funcTab = new();
	private MachOSymtabCommand _symTab = new();
	private readonly List<Export> _exports = [];
	public override string DefaultFilename => "app";
	public override string Format => "Mach-O " + (Bits == 32 ? "32-bit" : "64-bit");
	public override string Arch =>
		(MachO)_header.CPUType switch
		{
			MachO.CPU_TYPE_ARM => "ARM",
			MachO.CPU_TYPE_ARM64 => "ARM64",
			MachO.CPU_TYPE_X86 => "x86",
			MachO.CPU_TYPE_X86_64 => "x64",
			_ => "Unsupported",
		};

	protected abstract bool CheckMagicLE(MachO magic);
	protected abstract bool CheckMagicBE(MachO magic);
	protected abstract MachO LcSegment { get; }

	protected override bool Init()
	{
		// Detect endianness - default is little-endianness
		MachO magic = (MachO)ReadUInt32();
		if (CheckMagicBE(magic))
		{
			Endianness = Endianness.Big;
		}

		if (!CheckMagicBE(magic) && !CheckMagicLE(magic))
		{
			return false;
		}

		_header = ReadObject<MachOHeader<TWord>>(0);
		// Must be of a known file type
		if ((MachO)_header.FileType < MachO.MH_MIN_FILETYPE || (MachO)_header.FileType > MachO.MH_MAX_FILETYPE)
		{
			return false;
		}

		// Process load commands
		for (int c = 0; c < _header.NumCommands; c++)
		{
			long startPos = Position;
			MachOLoadCommand loadCommand = ReadObject<MachOLoadCommand>();
			switch ((MachO)loadCommand.Command)
			{
				// Segments
				case MachO cmd when cmd == LcSegment:
					MachOSegmentCommand<TWord> segment = ReadObject<MachOSegmentCommand<TWord>>();
					// Sections in each segment
					for (int s = 0; s < segment.NumSections; s++)
					{
						MachOSection<TWord> section = ReadObject<MachOSection<TWord>>();
						_machoSections.Add(section);
						// Create universal section
						_sections.Add(
							new Section
							{
								VirtualStart = _conv.ULong(section.Address),
								VirtualEnd = _conv.ULong(section.Address) + (uint)Math.Max(_conv.Int(section.Size) - 1, 0),
								ImageStart = section.ImageOffset,
								ImageEnd = section.ImageOffset + (uint)Math.Max(_conv.Int(section.Size) - 1, 0),
								IsData = segment.Name == "__TEXT" || segment.Name == "__DATA",
								IsExec = segment.Name == "__TEXT",
								IsBSS = segment.Name == "__DATA" && (section.Name == "__bss" || section.Name == "__common"),
								Name = section.Name,
							}
						);
						// First code section
						if (section.Name == "__text")
						{
							GlobalOffset = _conv.ULong(section.Address) - section.ImageOffset;
						}

						// Initialization (pre-main) functions
						if (section.Name == "__mod_init_func")
						{
							_funcTab = section;
						}
					}

					break;
				// Location of static symbol table
				case MachO.LC_SYMTAB:
					_symTab = ReadObject<MachOSymtabCommand>();
					break;
				case MachO.LC_DYSYMTAB:
					// TODO: Implement Mach-O dynamic symbol table
					break;
				// Compressed dyld information
				case MachO.LC_DYLD_INFO:
				case MachO.LC_DYLD_INFO_ONLY:
					MachODyldInfoCommand dyld = ReadObject<MachODyldInfoCommand>();
					LoadExportTrie(dyld.ExportOffset);
					break;
				// Encryption check
				// If cryptid == 1, this binary is encrypted with FairPlay DRM
				case MachO.LC_ENCRYPTION_INFO:
				case MachO.LC_ENCRYPTION_INFO_64:
					MachOEncryptionInfo encryptionInfo = ReadObject<MachOEncryptionInfo>();
					if (encryptionInfo.CryptID != 0)
					{
						throw new NotImplementedException(
							"This Mach-O executable is encrypted with FairPlay DRM and cannot be processed. Please provide a decrypted version of the executable."
						);
					}

					break;
				case MachO.LC_DYLD_CHAINED_FIXUPS:
					MachOLinkEditDataCommand chainedFixupsInfo = ReadObject<MachOLinkEditDataCommand>();
					ApplyChainedFixups(chainedFixupsInfo);
					break;
			}

			// There might be other data after the load command so always use the specified total size to step forwards
			Position = startPos + loadCommand.Size;
		}

		// Note: Some binaries do not have __mod_init_func, but instead just __init_offset with offsets to the init functions,
		// so a missing funcTab is not treated as an error
		// Process relocations
		foreach (MachOSection<TWord> section in _machoSections)
		{
			MachO_relocation_info[] rels = ReadArray<MachO_relocation_info>(section.ImageRelocOffset, section.NumRelocEntries);
			// TODO: Implement Mach-O relocations
			if (rels.Length > 0)
			{
				break;
			}
		}

		// Build symbol table
		ProcessSymbols();
		return true;
	}

	// Handle export trie
	private void LoadExportTrie(uint trieOffset, uint nodeOffset = 0, string partialSymbol = "")
	{
		Position = trieOffset + nodeOffset;
		ulong size = ULEB128.Decode(this);
		// Terminal information
		if (size != 0)
		{
			byte flags = ReadByte();
			int symbolType = (flags >> 2) & 0x03;
			// 0 = regular, 1 = weak, 2 = re-export, 3 = stub
			// Re-export and stub payloads are consumed but not recorded
			switch (symbolType)
			{
				case 0:
				case 1:
					ulong address = ULEB128.Decode(this);
					_exports.Add(new Export { Name = partialSymbol, VirtualAddress = GlobalOffset + address });
					break;
				case 2:
					_ = ULEB128.Decode(this); // ordinal
					_ = ReadNullTerminatedString(); // name
					break;
				case 3:
					_ = ULEB128.Decode(this); // stub offset
					_ = ULEB128.Decode(this); // resolver offset
					break;
			}
		}

		byte branchCount = ReadByte();
		for (int branch = 0; branch < branchCount; branch++)
		{
			string prefix = ReadNullTerminatedString();
			uint childNodeOffset = (uint)ULEB128.Decode(this);
			long currentPosition = Position;
			LoadExportTrie(trieOffset, childNodeOffset, partialSymbol + prefix);
			Position = currentPosition;
		}
	}

	private void ProcessSymbols()
	{
		// https://opensource.apple.com/source/cctools/cctools-795/include/mach-o/nlist.h
		// n_sect: https://opensource.apple.com/source/cctools/cctools-795/include/mach-o/stab.h
		StatusUpdate("Processing symbols");
		_symbolTable = [];
		MachO_nlist<TWord>[] symbolList = ReadArray<MachO_nlist<TWord>>(_symTab.SymOffset, (int)_symTab.NumSyms);
		// This is a really naive implementation that ignores the values of n_type and n_sect
		// which may affect the interpretation of n_value
		foreach (MachO_nlist<TWord> symbol in symbolList)
		{
			Position = _symTab.StrOffset + symbol.NStrx;
			string name = (symbol.NStrx != 0) ? ReadNullTerminatedString() : "";
			ulong value = _conv.ULong(symbol.NValue);
			// Ignore symbols with no address or name
			if (value == 0 || name.Length == 0)
			{
				continue;
			}

			// Mask out the N_EXT and N_PEXT bits because we don't care about it
			MachO_NType ntype = (MachO_NType)((byte)symbol.NType & ~(byte)MachO_NType.N_EXT & ~(byte)MachO_NType.N_PEXT);
			// For non-debugging symbols (no bits of N_STAB set), just leave the N_TYPE
			// Otherwise leave the whole n_type field (with N_EXT and N_PEXT removed)
			bool dbg = (symbol.NType & MachO_NType.N_STAB) != 0;
			if (dbg)
			{
				if (ntype == MachO_NType.N_BNSYM || ntype == MachO_NType.N_ENSYM || ntype == MachO_NType.N_SO || ntype == MachO_NType.N_OSO)
				{
					continue;
				}
			}

			SymbolType type = ntype switch
			{
				MachO_NType.N_FUN or MachO_NType.N_MAIN => SymbolType.Function,
				MachO_NType.N_STSYM or MachO_NType.N_GSYM or MachO_NType.N_SECT or MachO_NType.N_LCSYM or MachO_NType.N_ROSYM => SymbolType.Name,
				_ => SymbolType.Unknown,
			};
			if (type == SymbolType.Unknown) { }

			// Ignore duplicates
			_symbolTable.TryAdd(
				name,
				new Symbol
				{
					Name = name,
					VirtualAddress = value,
					Type = type,
				}
			);
		}
	}

	private void ApplyChainedFixups(in MachOLinkEditDataCommand info)
	{
		MachODyldChainedFixupsHeader chainedFixupsHeader = ReadVersionedObject<MachODyldChainedFixupsHeader>(info.Offset);
		if (chainedFixupsHeader.FixupsVersion != 0)
		{
			return;
		}

		MachODyldChainedImportFormat importsFormat = (MachODyldChainedImportFormat)chainedFixupsHeader.ImportsFormat;
		if (
			importsFormat != MachODyldChainedImportFormat.DYLD_CHAINED_IMPORT
			&& importsFormat != MachODyldChainedImportFormat.DYLD_CHAINED_IMPORT_ADDEND
			&& importsFormat != MachODyldChainedImportFormat.DYLD_CHAINED_IMPORT_ADDEND64
		)
		{
			// Rebase chains can still be processed even if bind import entries are in an unknown format.
		}

		uint startsBase = info.Offset + chainedFixupsHeader.StartsOffset;
		uint segmentCount = ReadPrimitive<uint>(startsBase);
		System.Collections.Immutable.ImmutableArray<uint> segmentStartOffsets = ReadPrimitiveArray<uint>(startsBase + 4, segmentCount);
		foreach (uint startOffset in segmentStartOffsets)
		{
			if (startOffset == 0)
			{
				continue;
			}

			MachODyldChainedStartsInSegment startsInfo = ReadVersionedObject<MachODyldChainedStartsInSegment>(startsBase + startOffset);
			if (startsInfo.SegmentOffset == 0)
			{
				continue;
			}

			MachODyldChainedPtr pointerFormat = (MachODyldChainedPtr)startsInfo.PointerFormat;
			System.Collections.Immutable.ImmutableArray<ushort> pages = ReadPrimitiveArray<ushort>(
				startsBase + startOffset + MachODyldChainedStartsInSegment.StructSize(),
				startsInfo.PageCount
			);
			for (int i = 0; i < pages.Length; i++)
			{
				ushort page = pages[i];
				if (page == MachODyldChainedStartsInSegment.DYLD_CHAINED_PTR_START_NONE)
				{
					continue;
				}

				ulong chainOffset = startsInfo.SegmentOffset + (ulong)(i * startsInfo.PageSize) + page;
				while (true)
				{
					MachODyldChainedPtr64Rebase currentEntry = ReadVersionedObject<MachODyldChainedPtr64Rebase>((long)chainOffset);
					if (!currentEntry.Bind) // todo: bind
					{
						ulong fixedValue = pointerFormat switch
						{
							MachODyldChainedPtr.DYLD_CHAINED_PTR_64 or MachODyldChainedPtr.DYLD_CHAINED_PTR_64_OFFSET => currentEntry.High8 << 56
								| currentEntry.Target,
							_ => 0ul,
						};
						Write((long)chainOffset, fixedValue);
					}

					if (currentEntry.Next == 0)
					{
						break;
					}

					chainOffset += currentEntry.Next * 4;
				}
			}
		}
	}

	public override uint[] GetFunctionTable() =>
		_funcTab == null
			? []
			: [.. ReadArray<TWord>(_funcTab.ImageOffset, _conv.Int(_funcTab.Size) / (Bits / 8)).Select(x => MapVATR(_conv.ULong(x)) & 0xffff_fffe)];

	public override Dictionary<string, Symbol> GetSymbolTable() => _symbolTable;

	public override IEnumerable<Export> GetExports() => _exports;

	public override IEnumerable<Section> GetSections() => _sections;
}
