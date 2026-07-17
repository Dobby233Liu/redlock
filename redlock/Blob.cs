global using BlobPatch = System.Collections.Generic.KeyValuePair<long, byte>;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace redlock;

internal class Blob
{
	public readonly int Size;

	internal Blob(int size)
	{
		Size = size;
		Data = new byte[Size];
	}

	public byte[] Data { get; }
	public bool HasBeenRead;

	internal void Read(Stream stream)
	{
		Debug.Assert(Data.Length == Size);
		var readSize = stream.Read(Data, 0, Size);
		if (readSize != Size)
			throw new InvalidDataException($"Expected {Size} bytes, got {readSize} bytes");
		HasBeenRead = true;
	}

	public byte[] ApplyPatch(IEnumerable<BlobPatch> patches)
	{
		foreach (var patch in patches)
			Data[patch.Key] = patch.Value;
		return Data;
	}
}