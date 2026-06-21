using System;
using System.Runtime.CompilerServices;
using System.Text;
using AsmResolver;
using AsmResolver.PE;
using AsmResolver.PE.File;

namespace redlock;

internal partial class Program
{
	private static readonly string RpVersionCheckStr = "RP_VersionCheck";
	private static readonly byte[] RpVersionCheckBytes = Encoding.ASCII.GetBytes(RpVersionCheckStr);
	
	// new version is largely vibe-coded
	private static int GetRequiredRPVersion(string tWinUiPath)
	{
		var file = PEFile.FromFile(tWinUiPath);
		var image = PEImage.FromFile(file);
		
		Console.Write($" -> TWinUI architecture: {image.MachineType.ToString()}");
		if (image.MachineType is not (MachineType.I386 or MachineType.Amd64))
		{
			Console.WriteLine(" (unsupported)");
			return int.MaxValue;
		}
		Console.WriteLine();

		var codeSection = file.GetSectionContainingRva(file.OptionalHeader.BaseOfCode);
		
		var verCheckAddrS =
			PatternFinder.FindPatternInFile(tWinUiPath, RpVersionCheckBytes, true,
				(long)codeSection.Offset);
		if (verCheckAddrS == PatternFinder.NoneFound)
		{
			Console.WriteLine($" -> Did not find {RpVersionCheckStr}");
			return int.MaxValue;
		}
		var verCheckAddr = (ulong)verCheckAddrS;
		var verCheckRva = file.FileOffsetToRva(verCheckAddr);
		var verCheckSection = file.GetSectionContainingRva(verCheckRva); 
		var verCheckVa = image.ImageBase + verCheckRva;
		Console.WriteLine($" -> Found {RpVersionCheckStr} in {verCheckSection.Name} at 0x{verCheckAddr:x}"
							+ $" (VA 0x{verCheckVa:x})");

		var codeSectionData = codeSection.ToArray();
		var codeSectionRva = image.ImageBase + codeSection.Rva;

		return image.MachineType switch
		{
			MachineType.I386 => X86FindAddrLoad(codeSectionData, verCheckVa),
			MachineType.Amd64 => Amd64FindAddrLoad(codeSectionData, codeSectionRva, verCheckVa),
			_ => int.MaxValue
		};
	}
	
	// the plan was to use a disassembler, but it didn't really work in my favor

	private static int X86FindAddrLoad(byte[] code, ulong targetVa)
	{
		for (var i = 0; i < code.Length - (5 + 1 - 1); i++) // 5 + 1 = smallest amount of bytes we require
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			bool BytesEnough(int count) => i - 1 + count < code.Length;

			// push imm32 => 68 xx xx xx xx
			if (BytesEnough(5 + 1) && code[i] == 0x68)
			{
				if (BitConverter.ToUInt32(code, i + 1) != targetVa)
					continue;
				Console.WriteLine($" -> Found push 0x{targetVa:x4} at 0x{i:x}");
				return X86FindCmp(code, i + 4 + 1, 20);
			}
		}
		return int.MaxValue;
	}

	private static int Amd64FindAddrLoad(byte[] code, ulong baseVa, ulong targetVa)
	{
		for (var i = 0; i < code.Length - (7 + 1 - 1); i++)
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			bool BytesEnough(int count) => i - 1 + count < code.Length;
			
			// lea rdx, [rip + disp32] => 48 8D 15 xx xx xx xx
			if (BytesEnough(7 + 1) && code[i] == 0x48 && code[i + 1] == 0x8D && code[i + 2] == 0x15)
			{
				// Calculate what RIP-relative address this would reference
				var displacement = BitConverter.ToUInt32(code, i + 3);
				var loadingAddr = baseVa + (uint)i + 7 + displacement;
				if (loadingAddr != targetVa)
					continue;
				Console.WriteLine($" -> Found lea rdx, 0x{loadingAddr:x4} at 0x{i:x}");
				return X86FindCmp(code, i + 6 + 1, 20);
			}
		}
		return int.MaxValue;
	}

	private static int X86FindCmp(byte[] code, int startOffset, int searchLength)
	{
		var endOffset = Math.Min(startOffset + searchLength, code.Length);
		for (var i = startOffset; i < endOffset - (3 - 1); i++)
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			bool BytesEnough(int count) => i - 1 + count < code.Length;
			
			// cmp eax, imm8 => 83 F8 xx
			if (BytesEnough(3) && code[i] == 0x83 && code[i + 1] == 0xF8)
			{
				var result = code[i + 2];
				Console.WriteLine($" -> Found cmp eax, 0x{result:x4} at 0x{i:x}");
				return result;
			}

			// cmp eax, imm32 => 3D xx xx xx xx
			if (BytesEnough(5) && code[i] == 0x3D)
			{
				if (!(code[i + 3] == 0x01 && code[i + 4] == 0x00)) // result < 0x100 || result >= 0x200
					continue;
				var result = BitConverter.ToInt32(code, i + 1);
				Console.WriteLine($" -> Found cmp eax, 0x{result:x4} at 0x{i:x}");
				return result;
			}
		}
		return int.MaxValue;
	}
}