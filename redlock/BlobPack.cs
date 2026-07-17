using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace redlock;

internal abstract class BlobPack<T>(Stream stream) : IDisposable
	where T : Blob
{
	protected abstract T[] Blobs { get; }
	private long[] Offsets => field ??= BuildOffsets();

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

	public byte[] GetBlobData(T blob)
	{
		return !blob.HasBeenRead ? Read(blob).Data : blob.Data;
	}

	public T Read(T blob)
	{
		var index = Array.IndexOf(Blobs, blob);
		if (index == -1)
			throw new ArgumentException("Blob is not part of pack", nameof(blob));

		var offset = Offsets[index];
		if (stream.Position != offset)
		{
			if (!stream.CanSeek)
				throw new NotSupportedException(
					$"Attempted non-sequential read of blob #{index}, but the stream is not seekable");
			Debug.WriteLine($"Non-sequential read of blob #{index} in {GetType().Name}");
			stream.Position = offset;
		}

		blob.Read(stream);
		return blob;
	}

	public void ReadAll()
	{
		foreach (var blob in Blobs)
			Read(blob);
	}

	public virtual void Dispose()
	{
		stream.Dispose();
	}
}

internal abstract class CompBlobPack<T>(Stream rawStream)
	: BlobPack<T>(new GZipStreamWithFakePosition(rawStream, CompressionMode.Decompress))
	where T : Blob
{
	private readonly Stream? _rawStream = rawStream;

	protected class GZipStreamWithFakePosition(Stream stream, CompressionMode mode) : GZipStream(stream, mode)
	{
		private long _position;

		public override long Position
		{
			get => _position;
			set => throw new NotSupportedException();
		}

		public override int Read(byte[] array, int offset, int count)
		{
			var readBytes = base.Read(array, offset, count);
			_position += readBytes;
			return readBytes;
		}
	}

	protected CompBlobPack(byte[] data)
		: this(new MemoryStream(data, false))
	{
	}

	public override void Dispose()
	{
		base.Dispose();
		_rawStream?.Dispose();
	}
}