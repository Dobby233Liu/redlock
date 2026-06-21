using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using AsmResolver;
using AsmResolver.PE.File;

namespace redlock;

internal partial class Program
{
	private static readonly string RpVersionCheckStr = "RP_VersionCheck";

	// new version is largely vibe-coded
	private static int GetRequiredRPVersion(string tWinUiPath)
	{
		var file = PEFile.FromFile(tWinUiPath);
		var machineType = file.FileHeader.Machine;

		Console.Write($" -> TWinUI architecture: {machineType.ToString()}");
		if (machineType is not (MachineType.I386 or MachineType.Amd64))
		{
			Console.WriteLine(" (unsupported)");
			return int.MaxValue;
		}
		Console.WriteLine();

		var codeSection = file.GetSectionContainingRva(file.OptionalHeader.BaseOfCode);
		
		var verCheckVa = FindStringVa(RpVersionCheckStr, file, tWinUiPath, (long)codeSection.Offset);
		if (verCheckVa == ulong.MaxValue)
			return int.MaxValue;
		
		var codeSectionData = codeSection.ToArray();
		var codeSectionVa = file.OptionalHeader.ImageBase + codeSection.Rva;

		if (machineType is (MachineType.I386 or MachineType.Amd64))
		{
			foreach (var nextInsOffset in
			         (machineType == MachineType.I386
				         ? X86FindAddrLoad(codeSectionData, verCheckVa)
				         : Amd64FindAddrLoad(codeSectionData, codeSectionVa, verCheckVa)))
			{
				var result = X86FindCmp(codeSectionData, nextInsOffset, 20);
				if (result != int.MaxValue)
					return result;
			}
		}
		
		return int.MaxValue;
	}

	private static ulong FindStringVa(string strToFind, PEFile file, string filePath, long startOffset = 0)
	{
		var strBytes = Encoding.ASCII.GetBytes(strToFind);
		var addr = PatternFinder.FindPatternInFile(filePath, strBytes, true, startOffset);
		if (addr == PatternFinder.NoneFound)
		{
			Console.WriteLine($" -> Did not find {strToFind}");
			return ulong.MaxValue;
		}

		var rva = file.FileOffsetToRva((ulong)addr);
		var section = file.GetSectionContainingRva(rva);
		var va = file.OptionalHeader.ImageBase + rva;
		Console.WriteLine($" -> Found {strToFind} in {section.Name} at 0x{addr:x} (VA 0x{va:x})");
		return va;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Func<int, bool> BytesEnoughGen(int codeLength, int i)
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		bool BytesEnough(int count)
		{
			return i - 1 + count < codeLength;
		}

		return BytesEnough;
	}

	private static IEnumerator<int> X86FindAddrLoad(byte[] code, ulong targetVa)
	{
		// 5 + 1 = smallest amount of bytes we require
		for (var i = 0; i < code.LongLength - (5 + 1 - 1); i++)
		{
			var bytesEnough = BytesEnoughGen(code.Length, i);

			// push imm32
			if (bytesEnough(5 + 1)
			    && code[i] == 0x68)
			{
				if (BitConverter.ToUInt32(code, i + 1) != targetVa)
					continue;
				Console.WriteLine($" -> Found push 0x{targetVa:x4} at 0x{i:x}");

				yield return i + 4 + 1;
			}
		}
	}

	private static IEnumerator<int> Amd64FindAddrLoad(byte[] code, ulong baseVa, ulong targetVa)
	{
		for (var i = 0; i < code.Length - (7 + 1 - 1); i++)
		{
			var bytesEnough = BytesEnoughGen(code.Length, i);

			// lea rdx, [rip + disp32]
			if (bytesEnough(7 + 1)
			    && code[i] == 0x48 && code[i + 1] == 0x8D && code[i + 2] == 0x15)
			{
				// Calculate what RIP-relative address this would reference
				var displacement = BitConverter.ToUInt32(code, i + 3);
				var loadingAddr = baseVa + (uint)i + 7 + displacement;
				if (loadingAddr != targetVa)
					continue;
				Console.WriteLine($" -> Found lea rdx, 0x{loadingAddr:x4} at 0x{i:x}");

				yield return i + 6 + 1;
			}
		}
	}

	private static int X86FindCmp(byte[] code, int startOffset, int searchLength)
	{
		var endOffset = Math.Min(startOffset + searchLength, code.Length);
		for (var i = startOffset; i < endOffset - (3 - 1); i++)
		{
			var bytesEnough = BytesEnoughGen(code.Length, i);

			// cmp eax, imm8
			if (bytesEnough(3)
			    && code[i] == 0x83 && code[i + 1] == 0xF8)
			{
				var result = code[i + 2];
				Console.WriteLine($" -> Found cmp eax, 0x{result:x4} at 0x{i:x}");
				return result;
			}

			// cmp eax, imm32
			if (bytesEnough(5)
			    && code[i] == 0x3D)
			{
				// result < 0x100 || result >= 0x200
				if (!(code[i + 3] == 0x01 && code[i + 4] == 0x00))
					continue;
				var result = BitConverter.ToInt32(code, i + 1);
				Console.WriteLine($" -> Found cmp eax, 0x{result:x4} at 0x{i:x}");
				return result;
			}
		}

		return int.MaxValue;
	}
}