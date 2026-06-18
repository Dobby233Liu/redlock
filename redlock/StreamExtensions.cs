using System.IO;

namespace redlock
{
	public static class StreamExtensions
	{
		public static void Align(this Stream stream, bool round1To2 = false, int alignment = 4)
		{
			if (round1To2)
				stream.Position += 1;
			var remainder = stream.Position % alignment;
			stream.Position += alignment - remainder;
		}
	}
}