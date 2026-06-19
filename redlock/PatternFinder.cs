using System;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;

namespace redlock;

internal static class PatternFinder
{
	internal const int NoneFound = -1;
	internal const int Found = 1;

	internal static long FindPatternInFile(string filePath, byte[] bytePattern, bool returnOffsets = true,
		long minOffset = 0, long maxOffset = 0)
	{
		if (!File.Exists(filePath))
			return NoneFound;

		using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		using var reader = new BinaryReader(stream);
		return FindPatterns(reader, [bytePattern], returnOffsets, minOffset, maxOffset)[0];
	}

	internal static long FindPattern(BinaryReader binReader, byte[] bytePattern, bool returnOffsets = true,
		long minOffset = 0, long maxOffset = 0)
	{
		return FindPatterns(binReader, [bytePattern], returnOffsets, minOffset, maxOffset)[0];
	}

	internal static long[] FindPatternsInFile(string filePath, byte[][] bytePatterns, bool returnOffsets = true,
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
		return FindPatterns(reader, bytePatterns, returnOffsets, minOffset, maxOffset);
	}

	/// <see
	///     href="https://en.wikipedia.org/wiki/Knuth%E2%80%93Morris%E2%80%93Pratt_algorithm#Description_of_pseudocode_for_the_table-building_algorithm">
	/// </see>
	[Pure]
	private static int[] KmpBuildFailureTable(byte[] pattern)
	{
		var table = new int[pattern.Length];
		if (table.Length == 0)
			return table;
		table[0] = -1;

		var pos = 1;
		var candidateIndex = 0;
		while (pos < pattern.Length)
		{
			if (pattern[pos] == pattern[candidateIndex])
			{
				table[pos] = table[candidateIndex];
			}
			else
			{
				table[pos] = candidateIndex;
				while (candidateIndex >= 0 && pattern[pos] != pattern[candidateIndex])
					candidateIndex = table[candidateIndex];
			}

			pos++;
			candidateIndex++;
		}

		return table;
	}

	/// <see
	///     href="https://en.wikipedia.org/wiki/Knuth%E2%80%93Morris%E2%80%93Pratt_algorithm#Description_of_pseudocode_for_the_search_algorithm">
	/// </see>
	[Pure]
	private static int KmpIndexOf(byte[] body, byte[] pattern, int[] failureTable)
	{
		var bodyPos = 0;
		var patternPos = 0;
		while (bodyPos < body.Length)
			if (pattern[patternPos] == body[bodyPos])
			{
				bodyPos++;
				patternPos++;
				if (patternPos == pattern.Length)
					return bodyPos - patternPos;
			}
			else
			{
				patternPos = failureTable[patternPos];
				if (patternPos >= 0)
					continue;
				bodyPos++;
				patternPos++;
			}

		return -1;
	}

	// Couldn't find where the original version was copied from, but it was (and probably still is) evil
	// reminder: try porting https://github.com/rvhuang/kmp-algorithm if this is totally broken
	internal static long[] FindPatterns(BinaryReader binReader, byte[][] bytePatterns, bool returnOffsets = true,
		long minOffset = 0, long maxOffset = 0)
	{
		var result = new long[bytePatterns.Length];
		if (result.Length == 0)
			return result;

		var stream = binReader.BaseStream;
		if (!stream.CanSeek)
			throw new NotSupportedException("Stream is not seekable");
		if (stream.Length == 0)
			return result;

		if (maxOffset <= 0)
			maxOffset = stream.Length;
		if (maxOffset < minOffset)
			throw new ArgumentOutOfRangeException(nameof(maxOffset),
				$"{nameof(maxOffset)} must be greater than {nameof(minOffset)}");

		var origOffset = stream.Position;
		try
		{
			stream.Seek(minOffset, SeekOrigin.Begin);

			var maxPatternSize = bytePatterns.Max(p => p.Length);
			if (maxPatternSize == 0)
				return result;
			for (var j = 0; j < result.Length; j++)
				result[j] = NoneFound;

			var patternFailureTables = new int[bytePatterns.Length][];
			for (var i = 0; i < bytePatterns.Length; i++)
				patternFailureTables[i] = KmpBuildFailureTable(bytePatterns[i]);

			var prevWindowSize = 0;
			var buf = new byte[maxPatternSize * 2];
			while (stream.Position < maxOffset)
			{
				if (prevWindowSize > 0)
				{
					var overlapSize = Math.Min(prevWindowSize, maxPatternSize - 1);
					Buffer.BlockCopy(buf, buf.Length - overlapSize, buf, 0, overlapSize);
					prevWindowSize = overlapSize;
				}

				var curData = binReader.ReadBytes(Math.Min(maxPatternSize, (int)(maxOffset - stream.Position)));
				if (curData.Length == 0)
					break;
				Buffer.BlockCopy(curData, 0, buf, prevWindowSize, curData.Length);
				var windowStart = stream.Position - prevWindowSize - curData.Length;
				prevWindowSize = curData.Length;

				var allFound = true;
				for (var i = 0; i < bytePatterns.Length; i++)
				{
					if (result[i] != NoneFound)
						continue;
					if (bytePatterns[i].Length == 0)
						continue;

					var offset = KmpIndexOf(buf, bytePatterns[i], patternFailureTables[i]);
					if (offset >= 0)
						result[i] = returnOffsets ? windowStart + offset : Found;
					else
						allFound = false;
				}

				if (allFound) break;
			}

			return result;
		}
		finally
		{
			stream.Seek(origOffset, SeekOrigin.Begin);
		}
	}
}