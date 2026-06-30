using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace redlock;

internal class ProductPolicy
{
	private const int SerializedHeaderSize = 20;
	public int Unknown { get; set; }
	public int Version { get; set; }

	public Dictionary<string, Item> Policies { get; private set; }

	public byte[] EndMarker { get; set; }

	public ProductPolicy Deserialize(BinaryReader reader)
	{
		reader.ReadInt32(); // total size
		var bodySize = reader.ReadInt32();
		var endMarkerSize = reader.ReadInt32();

		Unknown = reader.ReadInt32();
		Version = reader.ReadInt32();
		Policies = new Dictionary<string, Item>();

		while (reader.BaseStream.Position < SerializedHeaderSize + bodySize)
		{
			var item = Item.Deserialize(reader);
			Policies[item.Key] = item.Value;
		}

		EndMarker = reader.ReadBytes(endMarkerSize);

		return this;
	}

	public ProductPolicy Deserialize(byte[] data)
	{
		using var stream = new MemoryStream(data, false);
		using var reader = new BinaryReader(stream);
		return Deserialize(reader);
	}

	public void Serialize(BinaryWriter writer)
	{
		var stream = writer.BaseStream;

		stream.Position = 8; // reserve for total size and body size
		writer.Write(EndMarker.Length);
		writer.Write(Unknown);
		writer.Write(Version);

		foreach (var policyEntry in Policies)
			policyEntry.Value.Serialize(writer, policyEntry.Key);

		stream.Write(EndMarker, 0, EndMarker.Length);

		stream.Position = 0;
		writer.Write((int)stream.Length);
		writer.Write((int)(stream.Length - SerializedHeaderSize - EndMarker.Length));
	}

	public byte[] Serialize()
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter(stream);
		Serialize(writer);
		return stream.ToArray();
	}

	public void SetValue(string name, int value, bool overwrite = false)
	{
		if (overwrite && Policies.TryGetValue(name, out var policy))
		{
			if (policy.Type != Item.DataType.DWord)
				throw new ArgumentException("Attempting to set policy of type " + policy.Type + " to " +
				                            Item.DataType.DWord);

			Policies[name].Data = value;
			return;
		}

		Policies[name] = new Item
		{
			Type = Item.DataType.DWord,
			Data = value
		};
	}

	public class Item
	{
		public enum DataType : short
		{
			String = 1,
			DWord = 2,
			Binary = 3
		}

		public DataType Type { get; set; }
		public int Flags { get; set; }
		public int Unknown { get; set; }

		public object Data { get; set; }

		public static KeyValuePair<string, Item> Deserialize(BinaryReader reader)
		{
			reader.ReadInt16(); // entry size

			var keySize = reader.ReadInt16();

			var item = new Item
			{
				Type = (DataType)reader.ReadInt16()
			};
			var dataSize = reader.ReadInt16();

			item.Flags = reader.ReadInt32();
			item.Unknown = reader.ReadInt32();

			var key = Encoding.Unicode.GetString(reader.ReadBytes(keySize));
			item.Data = item.Type switch
			{
				DataType.String => Encoding.Unicode.GetString(reader.ReadBytes(dataSize)),
				DataType.DWord => reader.ReadInt32(),
				_ => reader.ReadBytes(dataSize)
			};

			reader.BaseStream.Align(dataSize == 1);

			return new KeyValuePair<string, Item>(key, item);
		}

		public void Serialize(BinaryWriter writer, string key)
		{
			var stream = writer.BaseStream;

			var start = stream.Position;
			stream.Position += 2; // reserve for entry size

			var buf = Encoding.Unicode.GetBytes(key);
			writer.Write((ushort)buf.Length); // key size

			writer.Write((short)Type);
			stream.Position += 2; // reserve for data size

			writer.Write(Flags);
			writer.Write(Unknown);

			stream.Write(buf, 0, buf.Length); // write key

			// write data
			buf = Type switch
			{
				DataType.String => Encoding.Unicode.GetBytes((string)Data),
				DataType.DWord => BitConverter.GetBytes((int)Data),
				_ => (byte[])Data
			};
			stream.Write(buf, 0, buf.Length);

			stream.Align(buf.Length == 1);

			var entrySize = stream.Position - start;
			stream.Position = start;
			writer.Write((ushort)entrySize);

			stream.Position += 2 + 2;
			writer.Write((ushort)buf.Length); // item size

			stream.Position += entrySize - 8; // go to end of entry
		}
	}
}