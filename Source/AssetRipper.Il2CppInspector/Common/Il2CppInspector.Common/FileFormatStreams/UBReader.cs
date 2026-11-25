/*
	Copyright 2017-2021 Katy Coe - http://www.djkaty.com - https://github.com/djkaty

	All rights reserved.
*/
using AssetRipper.Il2CppInspector.Bin2Object;

namespace AssetRipper.Il2CppInspector;

public class UBReader : FileFormatStream<UBReader>
{
	private FatHeader _header = new();
	public override string DefaultFilename => "app";

	protected override bool Init()
	{
		// Fat headers are always big-endian regardless of architectures
		Endianness = Endianness.Big;
		_header = ReadObject<FatHeader>();
		if ((UB)_header.Magic != UB.FAT_MAGIC)
		{
			return false;
		}

		NumImages = _header.NumArch;
		return true;
	}

	public override IFileFormatStream this[uint index]
	{
		get
		{
			Position = 0x8 + 0x14 * index; // sizeof(FatHeader), sizeof(FatArch)
			Endianness = Endianness.Big;
			FatArch arch = ReadObject<FatArch>();
			Position = arch.Offset;
			Endianness = Endianness.Little;
			using BinaryObjectStream s = new(ReadBytes((int)arch.Size));
			return MachOReader32.Load(s, _loadOptions, OnStatusUpdate) as IFileFormatStream
				?? MachOReader64.Load(s, _loadOptions, OnStatusUpdate)
				?? throw new InvalidDataException($"Mach-O image {index} is unsupported.");
		}
	}
}
