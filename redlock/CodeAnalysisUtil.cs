using System;
using System.Runtime.CompilerServices;
using System.Text;
using AsmResolver;
using AsmResolver.PE.File;

namespace redlock;

internal static partial class CodeAnalysisUtil
{
	private static byte[] MyReadSegment(IReadableSegment segment)
	{
		var data = new byte[segment.GetPhysicalSize()];
		segment.CreateReader().ReadBytes(data, 0, data.Length);
		return data;
	}

	private static ulong FindStringVa(string strToFind, PEFile file, string filePath, long startOffset = 0)
	{
		var strBytes = Encoding.ASCII.GetBytes(strToFind);
		var addr = PatternFinder.FindPatternInFile(filePath, strBytes, true, startOffset);
		if (addr == PatternFinder.NoneFound)
			return ulong.MaxValue;

		var rva = file.FileOffsetToRva((ulong)addr);
		var section = file.GetSectionContainingRva(rva);
		var va = file.OptionalHeader.ImageBase + rva;
		Console.WriteLine($" -> Found {strToFind} in {section.Name} at 0x{addr:x} (VA 0x{va:x8})");
		return va;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Func<int, int, bool> BytesEnoughGen(int codeLength)
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		bool BytesEnough(int i, int count)
		{
			return i + count <= codeLength;
		}

		return BytesEnough;
	}
}