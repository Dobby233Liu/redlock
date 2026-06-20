using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AsmResolver;
using AsmResolver.PE;
using AsmResolver.PE.File;

namespace redlock;

[Obsolete]
internal class PESectionInfo
{
	public string SectionName { get; set; }

	public int VirtSize { get; set; }

	public int VirtAddr { get; set; }

	public int PhysSize { get; set; }

	public int PhysAddr { get; set; }

	public int VirtOffset { get; set; }
}

internal partial class Program
{
	// this was largely vibe-coded

	private static int GetRequiredRPVersionTest(string tWinUiPath)
	{
		var peFile = PEFile.FromFile(tWinUiPath);
		var image = PEImage.FromFile(peFile);
		
		Console.Write($" -> TWinUI architecture: {image.MachineType.ToString()}");
		if (image.MachineType is not (MachineType.I386 or MachineType.Amd64))
		{
			Console.WriteLine(" (unsupported)");
			return int.MaxValue;
		}
		Console.WriteLine();

		var codeSection = peFile.GetSectionContainingRva(peFile.OptionalHeader.BaseOfCode);
		var codeSectionData = codeSection.ToArray();
		var codeSectionRva = image.ImageBase + codeSection.Rva;

		var verCheckStr = Encoding.ASCII.GetBytes("RP_VersionCheck");
		long verCheckAddr;
		using (var stream = new MemoryStream(codeSectionData, false))
		{
			using var reader = new BinaryReader(stream);
			verCheckAddr = PatternFinder.FindPattern(reader, verCheckStr);
			if (verCheckAddr == PatternFinder.NoneFound)
			{
				Console.WriteLine(" -> Didn't find RP_VersionCheck");
				return int.MaxValue;
			}
			verCheckAddr = (long)codeSection.Offset + verCheckAddr;
		}
		var verCheckRva = peFile.FileOffsetToRva((ulong)verCheckAddr);
		var verCheckSection = peFile.GetSectionContainingRva(verCheckRva); 
		Console.WriteLine($" -> Found RP_VersionCheck at 0x{verCheckAddr:x} (virtual address 0x{verCheckRva:x} in {verCheckSection.Name})");
		var verCheckVa = image.ImageBase + verCheckRva;

		return image.MachineType switch
		{
			MachineType.I386 => FindVersionX86(codeSectionData, verCheckVa),
			MachineType.Amd64 => FindVersionAmd64(codeSectionData, codeSectionRva, verCheckVa),
			_ => int.MaxValue
		};
	}
	
	private static int FindVersionX86(byte[] code, ulong targetVa)
	{
		for (var i = 0; i < code.Length - 5; i++)
		{
			// push imm32
			if (code[i] == 0x68 && BitConverter.ToUInt32(code, i + 1) == (uint)targetVa)
			{
				Console.WriteLine(" -> Found matching push offset at 0x{0:x}", i);
				// Found candidate push - look for subsequent cmp eax, imm
				return FindCmpAfter(code, i + 5, 20); // search next 20 bytes
			}
		}
		return int.MaxValue;
	}

	private static int FindVersionAmd64(byte[] code, ulong baseVa, ulong targetVa)
	{
		for (var i = 0; i < code.Length - 7; i++)
		{
			// lea rdx, [rip + disp32]
			if (code[i] == 0x48 && code[i + 1] == 0x8D && code[i + 2] == 0x15)
			{
				// Calculate what RIP-relative address this would reference
				var insVa = (long)baseVa + i;
				var disp = BitConverter.ToInt32(code, i + 3);
				var computedTarget = insVa + 7 + disp; // 7 = length of lea rdx, [rip+disp32]

				if ((ulong)computedTarget == targetVa)
				{
					Console.WriteLine(" -> Found matching lea rdx");
					return FindCmpAfter(code, i + 7, 20);
				}
			}
		}
		return int.MaxValue;
	}

	private static int FindCmpAfter(byte[] code, int startOffset, int searchLength)
	{
		var end = Math.Min(startOffset + searchLength, code.Length);
		for (var i = startOffset; i < end - 2; i++)
		{
			// cmp eax, imm8 => 83 F8 xx
			if (code[i] == 0x83 && code[i + 1] == 0xF8)
			{
				var result = code[i + 2];
				Console.WriteLine(" -> Found cmp eax, 0x{0:x} at 0x{1:x}", result, i);
				return result;
			}

			// cmp eax, imm32 => 3D xx xx xx xx with high bytes being 0x01 0x00
			if (code[i] == 0x3D && i + 5 < code.Length && code[i + 4] == 0x01 && code[i + 5] == 0x00)
			{
				var result = BitConverter.ToInt32(code, i + 1);
				Console.WriteLine(" -> Found cmp eax, 0x{0:x} at 0x{1:x}", result, i);
				return result;
			}
		}
		return int.MaxValue;
	}
	
	[Obsolete]
	private static int GetRequiredRPVersionOld(string filePath)
	{
		var result = int.MaxValue;
		
		using var stream = new FileStream(filePath, FileMode.Open);
		using var reader = new BinaryReader(stream);
		
		reader.BaseStream.Seek(0x3CL, SeekOrigin.Begin);
		reader.BaseStream.Seek(reader.ReadInt32() + 4, SeekOrigin.Begin);
		
		var archId = reader.ReadUInt16();
		const int archX86 = 0x14C, archAmd64 = 0x8664;
		var isAmd64 = false;
		Console.Write(" -> Architecture: ");
		switch (archId)
		{
		case archX86:
			Console.WriteLine("x86");
			break;
		case archAmd64:
			Console.WriteLine("x64");
			isAmd64 = true;
			break;
		default:
			Console.WriteLine("Unknown");
			return result;
		}

		var sectionCount = reader.ReadUInt16();
		reader.BaseStream.Seek(isAmd64 ? 0x28L : 0x2CL, SeekOrigin.Current);
		var imageBaseMaybe = isAmd64 ? reader.ReadInt64() : reader.ReadInt32();
		reader.BaseStream.Seek(isAmd64 ? 0xD0L : 0xC0L, SeekOrigin.Current);
		
		var sections = new List<PESectionInfo>();
		var rsrcAddr = 0;
		for (var i = 0; i < sectionCount; i++)
		{
			var section = new PESectionInfo
			{
				SectionName = Encoding.ASCII.GetString(reader.ReadBytes(8)).TrimEnd(new char[1]),
				VirtSize = reader.ReadInt32(),
				VirtAddr = reader.ReadInt32(),
				PhysSize = reader.ReadInt32(),
				PhysAddr = reader.ReadInt32()
			};
			section.VirtOffset = section.VirtAddr - section.PhysAddr;
			
			sections.Add(section);
			if (rsrcAddr < 1 && section.SectionName == ".rsrc") rsrcAddr = section.PhysAddr;
			reader.BaseStream.Seek(16L, SeekOrigin.Current);
		}

		var verCheckDefStart = PatternFinder.FindPattern(reader, Encoding.ASCII.GetBytes("RP_VersionCheck"),
			true, sections[0].PhysAddr, rsrcAddr);
		if (verCheckDefStart <= -1L)
		{
			Console.WriteLine(" -> TWinUI doesn't contain RP_VersionCheck");
			return result;
		}
		
		var verCheckSection = sections.First(x =>
			verCheckDefStart > x.PhysAddr && verCheckDefStart < x.PhysAddr + x.PhysSize);
		var verCheckVirtAddr = verCheckDefStart + verCheckSection.VirtOffset;
		Console.WriteLine(" -> Found RP_VersionCheck at 0x{0:x} (virtual address 0x{1:x} in {2})",
			verCheckDefStart, verCheckVirtAddr, verCheckSection.SectionName);
		reader.BaseStream.Seek(sections[0].PhysAddr, SeekOrigin.Begin);

		var buf = new byte[15];
		if (!isAmd64)
		{
			verCheckVirtAddr += imageBaseMaybe;

			var findFail = false;
			while (buf[10] != 0x68 || BitConverter.ToInt32(buf, 11) != verCheckVirtAddr)
			{
				Array.Copy(buf, 1, buf, 0, 14);
				try
				{
					buf[buf.Length - 1] = reader.ReadByte();
				}
				catch
				{
					findFail = true;
					break;
				}
			}

			if (!findFail)
				Console.WriteLine(" -> Found matching push offset at 0x{0:x}",
					reader.BaseStream.Position - 5L);
		}
		else
		{
			var findFail = false;
			while (true)
			{
				if (buf[8] != 0x48 || buf[9] != 0x8D || buf[10] != 0x15)
				{
					Array.Copy(buf, 1, buf, 0, 14);
					try
					{
						buf[buf.Length - 1] = reader.ReadByte();
					}
					catch
					{
						findFail = true;
						goto skipContinue;
					}

					continue;
				}

				skipContinue:
				if (findFail) goto findCmp;
				var num7 = (ulong)(verCheckVirtAddr - (reader.BaseStream.Position + sections[0].VirtOffset));
				num7 &= 0xFFFFFFFFFFFFFFFF;
				var num8 = (uint)BitConverter.ToInt32(buf, 11);
				if (num7 == num8) break;
				buf[8] = 0;
			}

			Console.WriteLine(" -> Found matching lea rdx");
		}

		findCmp:
		while ((buf[10] != 0x83 || buf[11] != 0xF8) &&
		       (buf[10] != 0x3D || buf[13] != 0x1 || buf[14] != 0x0))
		{
			Array.Copy(buf, 1, buf, 0, 14);
			try
			{
				buf[buf.Length - 1] = reader.ReadByte();
			}
			catch
			{
				return result;
			}
		}
		if (buf[14] == 0)
			result = BitConverter.ToInt32(buf, 11);
		else
			result = buf[12];
		Console.WriteLine(" -> Found cmp eax, {0:x} at 0x{1:x}", result,
			(int)reader.BaseStream.Position - 5);

		return result;
	}
}