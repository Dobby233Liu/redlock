using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace redlock.Blobs;

internal class BlobPack : IDisposable
{
	private readonly Stream Stream;

	protected virtual Blob[] Blobs { get; }

	private long[]? _offsets;

	private long[] BuildOffsets()
	{
		var offsets = new long[Blobs.Length];
		long curOffset = 0;
		for (var i = 0; i < Blobs.Length; i++)
		{
			offsets[i] = curOffset;
			curOffset += Blobs[i].Size;
		}
		return offsets;
	}
	
	private long[] Offsets => _offsets ??= BuildOffsets();
	
	protected BlobPack(Stream stream)
	{
		Stream = stream;
	}

	public Blob Read(Blob blob)
	{
		var index = Array.IndexOf(Blobs, blob);
		if (index == -1)
			throw new Exception($"Blob is not part of {GetType().Name}");
		
		var offset = Offsets[index];
		if (offset != Stream.Position)
		{
			Debug.WriteLine($"Non-sequential read of {blob} in {GetType().Name}");
			Stream.Position = offset;
		}

		blob.Read(Stream);
		return blob;
	}
	
	public virtual void Dispose()
	{
		Stream.Dispose();
	}
}

internal class CompBlobPack : BlobPack
{
	private readonly Stream? RawStream;
	
	protected CompBlobPack(byte[] data)
		: this(new MemoryStream(data, false))
	{
	}
	
	protected CompBlobPack(Stream rawStream)
		: base(new GZipStream(rawStream, CompressionMode.Decompress))
	{
		RawStream = rawStream;
	}

	public override void Dispose()
	{
		base.Dispose();
		RawStream?.Dispose();
	}
}