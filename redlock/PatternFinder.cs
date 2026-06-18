using System;
using System.IO;

namespace redlock;

internal static class PatternFinder
{
	internal const int NoneFound = -1;
	internal const int Found = 1;
	
	internal static long FindPatternInFile(string filePath, byte[] bytePattern, bool getOffset = true,
		long minOffset = 0, long maxOffset = 0)
	{
		if (!File.Exists(filePath))
			return NoneFound;

		using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		using var reader = new BinaryReader(stream);
		return FindPatternsInFile(reader, [bytePattern], getOffset, minOffset, maxOffset)[0];
	}

	internal static long FindPatternInFile(BinaryReader binReader, byte[] bytePattern, bool getOffset = true,
		long minOffset = 0, long maxOffset = 0)
	{
		return FindPatternsInFile(binReader, [bytePattern], getOffset, minOffset, maxOffset)[0];
	}

	internal static long[] FindPatternsInFile(string filePath, byte[][] bytePatterns, bool getOffset = true,
		long minOffset = 0, long maxOffset = 0)
	{
		if (!File.Exists(filePath))
		{
			var result = new long[bytePatterns.Length];
			for (var i = 0; i < result.Length; i++)
				result[i] = NoneFound;
			return result;
		}

		using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		using var reader = new BinaryReader(stream);
		return FindPatternsInFile(reader, bytePatterns, getOffset, minOffset, maxOffset);
	}
	
	internal static long[] FindPatternsInFile(BinaryReader binReader, byte[][] bytePatterns, bool getOffset = true,
		long minOffset = 0, long maxOffset = 0)
	{
		var stream = binReader.BaseStream;
		
		var origOffset = stream.Position;
		if (minOffset > 0)
			stream.Seek(minOffset, SeekOrigin.Begin);
		if (maxOffset <= 0)
			maxOffset = stream.Length;
		
		var result = new long[bytePatterns.Length];
		for (var j = 0; j < result.Length; j++)
			result[j] = NoneFound;
		
		var patternsHex = new string[bytePatterns.Length];
		for (var i = 0; i < bytePatterns.Length; i++)
			patternsHex[i] = BitConverter.ToString(bytePatterns[i]);

		const int prevWindowSize = 2048, curWindowSizeDef = 2048;
		var buf = new byte[prevWindowSize + curWindowSizeDef];
		while (stream.Position < maxOffset)
		{
			if (stream.Position > minOffset)
				Array.Copy(
					buf, prevWindowSize,
					buf, 0, prevWindowSize);

			var curWindowSize = curWindowSizeDef;
			var remainder = maxOffset - stream.Position;
			if (curWindowSize > remainder)
			{
				curWindowSize = (int)remainder;
				Array.Resize(ref buf, prevWindowSize + curWindowSize);
			}
			Array.Copy(
				binReader.ReadBytes(curWindowSize), 0,
				buf, prevWindowSize, curWindowSize);
			
			var bufHex = BitConverter.ToString(buf); // FIXME: why
			var allFound = true;
			if (getOffset)
			{
				for (var i = 0; i < bytePatterns.Length; i++)
				{
					if (result[i] > NoneFound)
						continue;

					var offset = bufHex.IndexOf(patternsHex[i], StringComparison.OrdinalIgnoreCase);
					if (offset > -1)
					{
						var offsetPhysical = (offset + 1) / 3;
						result[i] = stream.Position - buf.Length + offsetPhysical;
					}
					else
						allFound = false;
				}
			}
			else
			{
				for (var i = 0; i < bytePatterns.Length; i++)
				{
					if (result[i] > NoneFound)
						continue;

					if (bufHex.Contains(patternsHex[i]))
						result[i] = Found;
					else
						allFound = false;
				}
			}
			if (allFound) break;
		}

		stream.Seek(origOffset, SeekOrigin.Begin);
		return result;
	}
}