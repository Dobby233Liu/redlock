using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace redlock.Blobs;

internal abstract class BlobPack : IDisposable
{
	private readonly Stream _stream;

	protected abstract Blob[] Blobs { get; }

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
		_stream = stream;
	}

	public Blob Read(Blob blob)
	{
		var index = Array.IndexOf(Blobs, blob);
		if (index == -1)
			throw new Exception($"Blob is not part of {GetType().Name}");
		
		var offset = Offsets[index];
		if (offset != _stream.Position)
		{
			if (!_stream.CanSeek)
				throw new NotSupportedException(
					$"Attempted non-sequential read at offset {offset}, but the stream is not seekable");
			Debug.WriteLine($"Non-sequential read of {blob} in {GetType().Name}");
			_stream.Position = offset;
		}

		blob.Read(_stream);
		return blob;
	}
	
	public virtual void Dispose()
	{
		_stream.Dispose();
	}
}

internal abstract class CompBlobPack : BlobPack
{
	protected class GZipStreamWithFakePosition : GZipStream
	{
		private long _position;

		public override long Position
		{
			get => _position;
			set => throw new NotSupportedException();
		}

		public GZipStreamWithFakePosition(Stream stream, CompressionMode mode)
			: base(stream, mode)
		{
		}

		public override int Read(byte[] array, int offset, int count)
		{
			var readBytes = base.Read(array, offset, count);
			_position += readBytes;
			return readBytes;
		}
	}
	
	private readonly Stream? _rawStream;
	
	protected CompBlobPack(byte[] data)
		: this(new MemoryStream(data, false))
	{
	}
	
	protected CompBlobPack(Stream rawStream)
		: base(new GZipStreamWithFakePosition(rawStream, CompressionMode.Decompress))
	{
		_rawStream = rawStream;
	}

	public override void Dispose()
	{
		base.Dispose();
		_rawStream?.Dispose();
	}
}