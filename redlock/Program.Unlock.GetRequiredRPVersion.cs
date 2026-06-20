using System;
using System.IO;
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
	private static int GetRequiredRPVersion(PEFile file)
	{
		var image = PEImage.FromFile(file);
		
		Console.Write($" -> TWinUI architecture: {image.MachineType.ToString()}");
		if (image.MachineType is not (MachineType.I386 or MachineType.Amd64))
		{
			Console.WriteLine(" (unsupported)");
			return int.MaxValue;
		}
		Console.WriteLine();

		var codeSection = file.GetSectionContainingRva(file.OptionalHeader.BaseOfCode);
		var codeSectionData = codeSection.ToArray();
		var codeSectionRva = image.ImageBase + codeSection.Rva;

		ulong verCheckAddr;
		using (var stream = new MemoryStream(codeSectionData, false))
		{
			using var reader = new BinaryReader(stream);
			var verCheckAddrS = PatternFinder.FindPattern(reader, RpVersionCheckBytes);
			if (verCheckAddrS == PatternFinder.NoneFound)
			{
				Console.WriteLine($" -> Did not find {RpVersionCheckStr}");
				return int.MaxValue;
			}
			verCheckAddr = codeSection.Offset + (ulong)verCheckAddrS;
		}
		var verCheckRva = file.FileOffsetToRva(verCheckAddr);
		var verCheckSection = file.GetSectionContainingRva(verCheckRva); 
		Console.WriteLine($" -> Found {RpVersionCheckStr} at 0x{verCheckAddr:x}"
							+ $" (VA 0x{verCheckRva:x} in {verCheckSection.Name})");
		var verCheckVa = image.ImageBase + verCheckRva;

		return image.MachineType switch
		{
			MachineType.I386 => FindAddrLoadX86(codeSectionData, verCheckVa),
			MachineType.Amd64 => FindAddrLoadAmd64(codeSectionData, codeSectionRva, verCheckVa),
			_ => int.MaxValue
		};
	}

	private static int GetRequiredRPVersion(string tWinUiPath)
	{
		var file = PEFile.FromFile(tWinUiPath);
		return GetRequiredRPVersion(file);
	}
	
	// the plan was to use a disassembler, but it didn't really work in my favor
	
	private static int FindAddrLoadX86(byte[] code, ulong targetVa)
	{
		for (var i = 0; i < code.Length - 5; i++) // 5 = length of shortest pattern we're detecting - 1
		{
			bool BytesEnough(int count) => i + count < code.Length;

			// push imm32
			if (code[i] == 0x68 && BytesEnough(5)
			                    && BitConverter.ToUInt32(code, i + 1) == targetVa)
			{
				Console.WriteLine($" -> Found push 0x{targetVa:x} at 0x{i:x}");
				return FindCmpX86(code, i + 5, 20);
			}
		}
		return int.MaxValue;
	}

	private static int FindAddrLoadAmd64(byte[] code, ulong baseVa, ulong targetVa)
	{
		for (var i = 0; i < code.Length - 7; i++)
		{
			bool BytesEnough(int count) => i + count < code.Length;
			
			// lea rdx, [rip + disp32]
			if (code[i] == 0x48 && BytesEnough(7) && code[i + 1] == 0x8D && code[i + 2] == 0x15)
			{
				// Calculate what RIP-relative address this would reference
				var insVa = baseVa + (uint)i;
				var displacement = BitConverter.ToUInt32(code, i + 3);
				var loadingAddr = insVa + 7 + displacement;
				if (loadingAddr != targetVa) continue;
				Console.WriteLine($" -> Found lea rdx, 0x{loadingAddr:x} at 0x{i:x}");
				return FindCmpX86(code, i + 7, 20);
			}
		}
		return int.MaxValue;
	}

	private static int FindCmpX86(byte[] code, int startOffset, int searchLength)
	{
		for (var i = startOffset; i < Math.Min(startOffset + searchLength, code.Length) - 2; i++)
		{
			bool BytesEnough(int count) => i + count < code.Length;
			
			// cmp eax, imm8 => 83 F8 xx
			if (code[i] == 0x83 && BytesEnough(2) && code[i + 1] == 0xF8)
			{
				var result = code[i + 2];
				Console.WriteLine($" -> Found cmp eax, 0x{result:x} at 0x{i:x}");
				return result;
			}

			// cmp eax, imm32 => 3D xx xx xx xx xx, with high bytes being 0x01 0x00
			if (code[i] == 0x3D && BytesEnough(5) && code[i + 4] == 0x01 && code[i + 5] == 0x00)
			{
				var result = BitConverter.ToInt32(code, i + 1);
				Console.WriteLine($" -> Found cmp eax, 0x{result:x} at 0x{i:x}");
				return result;
			}
		}
		return int.MaxValue;
	}
}