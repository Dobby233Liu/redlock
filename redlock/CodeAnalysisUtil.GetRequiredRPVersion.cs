using System;
using System.IO;
using AsmResolver.PE.File;

namespace redlock;

internal static partial class CodeAnalysisUtil
{
	private static readonly string RpVersionCheckStr = "RP_VersionCheck";

	private static bool IsValid16BitRpVer(int version)
	{
		return version is >= 0x100 and < 0x200;
	}

	// new version is largely vibe-coded
	internal static int GetRequiredRPVersion(string sourcePath)
	{
		Console.WriteLine($" -> Searching for RP version in {Path.GetFileNameWithoutExtension(sourcePath)}");

		var file = PEFile.FromFile(sourcePath);

		var machineType = file.FileHeader.Machine;
		Console.Write($" -> Architecture: {machineType.ToString()}");
		if (machineType is not (MachineType.I386 or MachineType.Amd64 or MachineType.ArmNt))
		{
			Console.WriteLine(" (unsupported)");
			return int.MaxValue;
		}

		Console.WriteLine();

		var codeSection = GetCodeSection(file);
		// this is unfortunately slow, but I don't like doing the sliding window thing
		Console.WriteLine($" -> Reading {codeSection.Name}");
		var codeSectionData = MyReadSegment(codeSection);
		var codeSectionVa = GetVa(file, codeSection.Rva);

		var verCheckVa = FindStringVa(RpVersionCheckStr, file, sourcePath, (long)codeSection.Offset);
		if (verCheckVa == ulong.MaxValue)
		{
			Console.WriteLine($" -> File doesn't contain {RpVersionCheckStr}");
			return int.MaxValue;
		}

		switch (machineType)
		{
		case MachineType.I386 or MachineType.Amd64:
		{
			foreach (var loadOffset in machineType switch
			         {
				         MachineType.I386 => X86FindAddrLoad(codeSectionData, verCheckVa),
				         MachineType.Amd64 => Amd64FindAddrLoad(codeSectionData, codeSectionVa, verCheckVa),
				         _ => throw new ArgumentOutOfRangeException()
			         })
			{
				var result = X86FindCmp(codeSectionData, loadOffset, 20, IsValid16BitRpVer);
				if (result != int.MaxValue)
					return result;
			}

			break;
		}
		case MachineType.ArmNt:
			foreach (var literalOffset in ArmFindLiteralPoolEntry(codeSectionData, verCheckVa))
			{
				var ldrOffset = ArmFindAddrLoad(codeSectionData, codeSectionVa, literalOffset);
				if (ldrOffset == int.MaxValue)
					continue;

				// Due to the call convention, the return value of RP_VersionCheck is basically
				// guaranteed to be stored in R0
				var result = ArmFindCmp(codeSectionData, ldrOffset, 0, 32, IsValid16BitRpVer);
				if (result != int.MaxValue)
					return result;
			}

			break;
		}

		return int.MaxValue;
	}
}