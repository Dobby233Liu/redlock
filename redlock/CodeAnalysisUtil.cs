using System;
using System.Collections.Generic;
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
	
	private static IEnumerable<int> X86FindAddrLoad(byte[] code, ulong targetVa)
	{
		var bytesEnough = BytesEnoughGen(code.Length);
		// 5 + 1 = smallest amount of bytes we require
		for (var i = 0; i < code.Length - (5 + 1 - 1); i++)
			// push imm32
			if (bytesEnough(i, 5 + 1)
			    && code[i] == 0x68)
			{
				if (BitConverter.ToUInt32(code, i + 1) != targetVa)
					continue;
				Console.WriteLine($" -> Found push 0x{targetVa:x8} at 0x{i:x}");
				yield return i + 4 + 1;
			}
	}

	private static IEnumerable<int> Amd64FindAddrLoad(byte[] code, ulong baseVa, ulong targetVa)
	{
		var bytesEnough = BytesEnoughGen(code.Length);
		for (var i = 0; i < code.Length - (7 + 1 - 1); i++)
			// lea rdx, [rip + disp32]
			if (bytesEnough(i, 7 + 1)
			    && code[i] == 0x48 && code[i + 1] == 0x8D && code[i + 2] == 0x15)
			{
				// Calculate what RIP-relative address this would reference
				var displacement = BitConverter.ToUInt32(code, i + 3);
				var loadingAddr = baseVa + (uint)i + 7 + displacement;
				if (loadingAddr != targetVa)
					continue;
				Console.WriteLine($" -> Found lea rdx, 0x{loadingAddr:x8} at 0x{i:x}");
				yield return i + 6 + 1;
			}
	}

	private static int X86FindCmp(byte[] code, int startOffset, int searchLength,
		Func<int, bool>? int16Constraint = null)
	{
		var endOffset = Math.Min(startOffset + searchLength, code.Length - (3 - 1));
		var bytesEnough = BytesEnoughGen(code.Length);
		for (var i = startOffset; i < endOffset; i++)
		{
			// cmp eax, imm8
			if (bytesEnough(i, 3)
			    && code[i] == 0x83 && code[i + 1] == 0xF8)
			{
				var result = code[i + 2];
				Console.WriteLine($" -> Found cmp eax, 0x{result:x4} at 0x{i:x}");
				return result;
			}

			// cmp eax, imm32
			if (bytesEnough(i, 5)
			    && code[i] == 0x3D)
			{
				var result = BitConverter.ToInt32(code, i + 1);
				if (!(int16Constraint is not null && int16Constraint(result)))
					continue;
				Console.WriteLine($" -> Found cmp eax, 0x{result:x4} at 0x{i:x}");
				return result;
			}
		}

		return int.MaxValue;
	}

	// TODO: this is not very sound
	private static IEnumerable<int> ArmFindLiteralPoolEntry(byte[] code, ulong targetVa)
	{
		var bytesEnough = BytesEnoughGen(code.Length);
		for (var i = 0; i < code.Length - (4 - 1); i += 4)
			if (bytesEnough(i, 4))
			{
				if (BitConverter.ToUInt32(code, i) != targetVa) continue;
				Console.WriteLine($" -> Found 0x{targetVa:x8} at 0x{i:x}");
				yield return i;
			}
	}
	
	private static int ArmFindAddrLoad(byte[] code, ulong baseVa, int targetRva)
	{
		var targetVa = baseVa + (ulong)targetRva;

		var bytesEnough = BytesEnoughGen(code.Length);
		// 4096 = max offset reachable by LDR.W
		for (var i = targetRva - (3 - 1); i >= Math.Max(0, targetRva - 4096); i -= 2)
		{
			var currentVa = baseVa + (ulong)i;
			var programCounter = currentVa + 4;
			var ins = BitConverter.ToUInt16(code, i);

			// LDR Rt, [PC, #imm8]
			if ((ins & 0xF800) == 0x4800)
			{
				var regId = ((uint)ins >> 8) & 0x7;
				var imm8 = (uint)ins & 0xFF;
				var loadAddr = (programCounter & 0xFFFFFFFC) + (imm8 << 2); // Word-aligned offset

				if (loadAddr != targetVa) continue;
				Console.WriteLine($" -> Found LDR R{regId}, =0x{targetVa:x8} at 0x{i:x}");
				return i;
			}

			// LDR.W Rt, [PC, #imm12]
			if (bytesEnough(i, 4)
			    && (ins & 0xFF7F) == 0xF85F)
			{
				var hw2 = BitConverter.ToUInt16(code, i + 2);
				var regId = ((uint)hw2 >> 12) & 0xF;
				var imm12 = (uint)hw2 & 0x0FFF;
				var loadAddr = (programCounter & 0xFFFFFFFC) + imm12; // Byte offset

				if (loadAddr != targetVa) continue;
				Console.WriteLine($" -> Found LDR.W R{regId}, =0x{targetVa:x8} at 0x{i:x}");
				return i;
			}
		}

		return int.MaxValue;
	}

	private static int ArmFindCmp(byte[] code, int startOffset, uint regId, int searchLength,
		Func<int, bool>? int16Constraint = null)
	{
		var endOffset = Math.Min(startOffset + searchLength, code.Length - (3 - 1));
		var bytesEnough = BytesEnoughGen(code.Length);
		for (var i = startOffset + 2; i < endOffset; i += 2)
		{
			var ins = BitConverter.ToUInt16(code, i);

			// CMP Rx, #0xXX
			if ((ins & 0xF800) == 0x2800 && (((uint)ins >> 8) & 0x7) == regId)
			{
				var result = ins & 0xFF;
				Console.WriteLine($" -> Found CMP R{regId}, #0x{result:x2} at 0x{i:x}");
				return result;
			}

			// CMP.W Rx, #0xXXXX
			if (bytesEnough(i, 4)
			    && (ins & 0xFFF0) == 0xF1B0 && (ins & 0xF) == regId)
			{
				var iBit = ((uint)ins >> 10) & 0x1;
				var hw2 = BitConverter.ToUInt16(code, i + 2);
				var imm3 = ((uint)hw2 >> 12) & 0x7;
				var imm8 = (uint)hw2 & 0xFF;
				var result = (int)((iBit << 11) | (imm3 << 8) | imm8);

				if (!(int16Constraint is not null && int16Constraint(result)))
					continue;
				Console.WriteLine($" -> Found CMP.W R{regId}, #0x{result:x4} at 0x{i:x}");
				return result;
			}
		}

		return int.MaxValue;
	}
}