global using BlobPatch = System.Collections.Generic.KeyValuePair<long, byte>;

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace redlock.Blobs;

internal class Blob
{
	public readonly int Size;

	public byte[] Data { get; }

	internal Blob(int size)
	{
		Size = size;
		Data = new byte[Size];
	}

	internal void Read(Stream stream)
	{
		Debug.Assert(Data.Length == Size);
		var readSize = stream.Read(Data, 0, Size);
		if (readSize != Size)
			throw new InvalidDataException($"Expected {Size} bytes, got {readSize} bytes");
	}

	public byte[] ApplyPatch(IEnumerable<BlobPatch> patches)
	{
		foreach (var patch in patches)
			Data[patch.Key] = patch.Value;
		return Data;
	}
}