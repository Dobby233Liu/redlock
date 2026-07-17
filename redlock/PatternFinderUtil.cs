using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace redlock;

internal static class PatternFinderUtil
{
	internal const int NoneFound = -1;

	// the original version of this was evil and had to be rewritten, though it may still be evil
	// reminder: try porting https://github.com/rvhuang/kmp-algorithm if this is totally broken
	internal static long[] Find(BinaryReader binReader, IReadOnlyList<byte[]> bytePatterns,
		long minOffset = 0, long maxOffset = -1)
	{
		var stream = binReader.BaseStream;
		if (!(stream.CanRead && stream.CanSeek))
			throw new NotSupportedException("Stream is not readable and seekable");

		if (minOffset >= stream.Length)
			throw new ArgumentOutOfRangeException(nameof(minOffset), "minOffset must be less than stream length");
		if (maxOffset < 0)
			maxOffset = stream.Length;
		if (maxOffset < minOffset)
			throw new ArgumentOutOfRangeException(nameof(maxOffset),
				$"{nameof(maxOffset)} must be greater than {nameof(minOffset)}");

		var result = InitResult(bytePatterns.Count);
		if (result.Length == 0 || stream.Length == 0)
			return result;

		var maxPatternSize = bytePatterns.Max(p => p.Length);
		if (maxPatternSize == 0)
			return result;

		var patternFailureTables = new int[bytePatterns.Count][];
		for (var i = 0; i < patternFailureTables.Length; i++)
			patternFailureTables[i] = KmpBuildFailureTable(bytePatterns[i]);

		var origOffset = stream.Position;
		try
		{
			stream.Seek(minOffset, SeekOrigin.Begin);

			var buf = new byte[Math.Max(4096, maxPatternSize * 2)];
			var validDataSize = 0;
			var overlapSize = maxPatternSize - 1;
			while (stream.Position < maxOffset || validDataSize > overlapSize)
			{
				if (validDataSize > 0)
				{
					var keepingSize = Math.Min(validDataSize, overlapSize);
					// LANDMINE: will fail if buf is somehow no longer a byte[]
					Buffer.BlockCopy(buf, validDataSize - keepingSize, buf, 0, keepingSize);
					validDataSize = keepingSize;
				}

				var readingSize = Math.Min(buf.Length - validDataSize, (int)(maxOffset - stream.Position));
				var readSize = readingSize > 0 ? binReader.Read(buf, validDataSize, readingSize) : 0;
				if (readSize == 0 && validDataSize <= overlapSize)
					break;
				validDataSize += readSize;
				var bufLogicalStart = stream.Position - validDataSize;

				var allFound = true;
				for (var i = 0; i < bytePatterns.Count; i++)
				{
					if (result[i] != NoneFound)
						continue;
					if (bytePatterns[i].Length == 0)
					{
						allFound = false;
						continue;
					}

					var offset = KmpIndexOf(buf, bytePatterns[i], patternFailureTables[i],
						bodySize: validDataSize);
					if (offset >= 0)
						result[i] = bufLogicalStart + offset;
					else
						allFound = false;
				}

				if (allFound) break;
			}
		}
		finally
		{
			stream.Seek(origOffset, SeekOrigin.Begin);
		}

		return result;
	}
	
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static long[] InitResult(int count)
	{
		var result = new long[count];
		for (var i = 0; i < result.Length; i++)
			result[i] = NoneFound;
		return result;
	}

	/// <see
	///     href="https://en.wikipedia.org/wiki/Special:Permalink/1362385861#Description_of_pseudocode_for_the_table-building_algorithm">
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
	///     href="https://en.wikipedia.org/wiki/Special:Permalink/1362385861#Description_of_pseudocode_for_the_search_algorithm">
	/// </see>
	[Pure]
	private static int KmpIndexOf(byte[] body, byte[] pattern, int[] failureTable, int? bodySize = null)
	{
		bodySize ??= body.Length;
		var bodyPos = 0;
		var patternPos = 0;
		while (bodyPos < bodySize)
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
	
	internal static long[] FindInFile(string filePath, IReadOnlyList<byte[]> bytePatterns,
		long minOffset = 0, long maxOffset = -1)
	{
		if (!File.Exists(filePath))
			return InitResult(bytePatterns.Count);

		using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		using var reader = new BinaryReader(stream);
		return Find(reader, bytePatterns, minOffset, maxOffset);
	}

	internal static long Find(BinaryReader binReader, byte[] bytePattern,
		long minOffset = 0, long maxOffset = -1)
	{
		return Find(binReader, [bytePattern], minOffset, maxOffset)[0];
	}

	internal static long FindInFile(string filePath, byte[] bytePattern,
		long minOffset = 0, long maxOffset = -1)
	{
		if (!File.Exists(filePath))
			return NoneFound;

		using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		using var reader = new BinaryReader(stream);
		return Find(reader, bytePattern, minOffset, maxOffset);
	}
}