using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace redlock
{
	public class ProductPolicy
	{
		public int Unknown { get; set; }
		public int Version { get; set; }
		
		public class Item
		{
			public enum DataType : short
			{
				String = 1,
				Binary = 3,
				DWord
			}
			
			public DataType Type { get; set; }
			public int Flags { get; set; }
			public int Unknown { get; set; }
			
			public object Data { get; set; }
		}
		public Dictionary<string, Item> Policies { get; set; }
		
		public byte[] EndMarker { get; set; }
		
		private const int SerializedHeaderSize = 20;

		public static ProductPolicy Deserialize(byte[] serializedData)
		{
			using var stream = new MemoryStream(serializedData, false);
			using var reader = new BinaryReader(stream);

			reader.ReadInt32(); // total size
			var bodySize = reader.ReadInt32();
			var endMarkerSize = reader.ReadInt32();
			var headerAndBodySize = SerializedHeaderSize + bodySize;
			
			var policy = new ProductPolicy
			{
				Unknown = reader.ReadInt32(),
				Version = reader.ReadInt32(),
				Policies = new()
			};
			
			while (reader.BaseStream.Position < headerAndBodySize)
			{
				reader.ReadInt16(); // entry size
				
				var keySize = reader.ReadInt16();
				
				var item = new Item
				{
					Type = (Item.DataType)reader.ReadInt16()
				};
				var itemSize = reader.ReadInt16();
					
				item.Flags = reader.ReadInt32();
				item.Unknown = reader.ReadInt32();
				
				var key = Encoding.Unicode.GetString(reader.ReadBytes(keySize));
				item.Data = item.Type switch
				{
					Item.DataType.String => Encoding.Unicode.GetString(reader.ReadBytes(itemSize)),
					Item.DataType.DWord => reader.ReadInt32(),
					_ => reader.ReadBytes(itemSize)
				};
				
				policy.Policies[key] = item;

				stream.Align(itemSize == 1);
			}

			policy.EndMarker = reader.ReadBytes(endMarkerSize);

			return policy;
		}

		public byte[] Serialize()
		{
			using var stream = new MemoryStream();
			using var writer = new BinaryWriter(stream);
			
			stream.Position = 8; // reserve for total size and body size
			writer.Write(EndMarker.Length);
			writer.Write(Unknown);
			writer.Write(Version);
			
			foreach (var policyEntry in Policies)
			{
				var start = stream.Position;
				stream.Position += 2; // reserve for entry size
				
				var buf = Encoding.Unicode.GetBytes(policyEntry.Key);
				writer.Write((ushort)buf.Length); // key size
				
				var item = policyEntry.Value;
				writer.Write((short)item.Type);
				stream.Position += 2; // reserve for item size
				
				writer.Write(item.Flags);
				writer.Write(item.Unknown);
				
				stream.Write(buf, 0, buf.Length); // write key
				
				// write item
				buf = item.Type switch
				{
					Item.DataType.String => Encoding.Unicode.GetBytes((string)item.Data),
					Item.DataType.DWord => BitConverter.GetBytes((int)item.Data),
					_ => (byte[])item.Data
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

			stream.Write(EndMarker, 0, EndMarker.Length);
			
			stream.Position = 0;
			writer.Write((int)stream.Length);
			writer.Write((int)(stream.Length - SerializedHeaderSize - EndMarker.Length));
			
			return stream.ToArray();
		}

		public void SetValue(string name, int value, bool overwrite = false)
		{
			if (Policies.ContainsKey(name) && overwrite)
			{
				Policies[name].Data = value;
				return;
			}

			Policies[name] = new Item
			{
				Type = Item.DataType.DWord,
				Data = value
			};
		}
	}
}