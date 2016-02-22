﻿using System;
using System.IO;

namespace NFirmwareEditor.Firmware
{
	public static class FirmwareEncoder
	{
		private const int MagicNumber = 0x63B38;

		public static byte[] Encode(byte[] bytes)
		{
			if (bytes == null) throw new ArgumentNullException("bytes");

			var result = new byte[bytes.Length];
			for (var i = 0; i < bytes.Length; i++)
			{
				result[i] = (byte)((bytes[i] ^ (i + bytes.Length + MagicNumber - bytes.Length / MagicNumber)) & 0xFF);
			}
			return result;
		}

		public static byte[] Decode(byte[] bytes)
		{
			if (bytes == null) throw new ArgumentNullException("bytes");
			return Encode(bytes);
		}

		public static byte[] ReadFile(string filePath, bool decode = true)
		{
			if (string.IsNullOrEmpty(filePath)) throw new ArgumentNullException("filePath");

			var rawBytes = File.ReadAllBytes(filePath);
			return decode ? Decode(rawBytes) : rawBytes;
		}

		public static void WriteFile(string filePath, byte[] data, bool encode = true)
		{
			if (string.IsNullOrEmpty(filePath)) throw new ArgumentNullException("filePath");
			if (data == null) throw new ArgumentNullException("data");

			File.WriteAllBytes(filePath, encode ? Encode(data) : data);
		}
	}
}