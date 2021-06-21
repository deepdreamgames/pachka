using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DeepDreamGames
{
	// Read stream as "<length> <key>=<value>\n" records
	// See EXTENDED TAR (PAX) HEADER FORMAT in https://www.systutorials.com/docs/linux/man/5-star/
	// https://pubs.opengroup.org/onlinepubs/9699919799/utilities/pax.html#tag_20_92_13_03
	public class ExtendedTarHeader
	{
		// If there is a hdrcharset extended header in effect for a file, the value field for any gname, linkpath, path, and uname 
		// extended header records shall be encoded using the character set specified by the hdrcharset extended header record; 
		// otherwise, the value field shall be encoded using UTF-8. 
		// The value field for all other keywords specified by POSIX.1-2017 shall be encoded using UTF-8.
		static public readonly HashSet<string> encodedKeys = new HashSet<string>() { "gname", "linkpath", "path", "uname" };

		private Stream stream;		// 
		private long streamPos;		// 
		private long streamLen;		// 
		
		private byte[] buffer;		// 
		private int bytePos;		// Current byte position in buffer (valid up to byteLen)
		private int byteLen;		// Number of valid characters written to byte buffer

		private Decoder decoder;
		private char[] chars;		// 
		private StringBuilder sb = new StringBuilder();

		// Ctor
		public ExtendedTarHeader()
		{
			Encoding utf8 = Encoding.UTF8;
			buffer = new byte[512];
			chars = new char[utf8.GetMaxCharCount(buffer.Length)];
			decoder = utf8.GetDecoder();
		}

		// 
		public bool TryRead(Stream stream, long size, Decoder valueDecoder, Dictionary<string, string> records)
		{
			Reset();

			this.stream = stream;
			streamLen = size;
			try
			{
				string key, value;
				while (ReadRecord(valueDecoder, out key, out value))
				{
					records[key] = value;
				}

				return true;
			}
			catch (Exception ex)
			{
				Application.Log(ex);
				return false;
			}
			finally
			{
				Reset();
			}
		}

		// 
		private void Reset()
		{
			stream = null;
			streamPos = 0;
			streamLen = 0;

			bytePos = 0;
			byteLen = 0;

			decoder.Reset();
			sb.Length = 0;
		}

		// 
		private void FillBuffer()
		{
			// Unused bytes still present in buffer
			if (bytePos < byteLen) { return; }

			bytePos = 0;
			byteLen = 0;

			// Read buffer.Length number of bytes but only up to specified stream size
			long remainder = streamLen - streamPos;
			if (remainder == 0) { return; }
			int toRead = buffer.Length;
			if (toRead > remainder)
			{
				toRead = (int)remainder;
			}
			
			byteLen = stream.Read(buffer, 0, toRead);
			streamPos += byteLen;
		}

		// 
		private bool EndOfStream()
		{
			FillBuffer();
			return byteLen == 0;
		}

		// 
		private bool ReadRecord(Decoder valueDecoder, out string key, out string value)
		{
			key = null;
			value = null;
			if (EndOfStream()) { return false; }

			// Length
			// Read bytes as ASCII numbers up until ' ' is encountered
			int recPos = 0;
			int recLen = 0;
			while (true)
			{
				byte b = buffer[bytePos];
				bytePos++;
				recPos++;
				if (b >= (byte)'0' && b <= (byte)'9')
				{
					recLen = recLen * 10 + (b - (byte)'0');
				}
				else if (b == (byte)' ')
				{
					//Application.Log(ConsoleColor.Cyan, recLen.ToString());
					break;
				}
				else
				{
					throw new Exception(string.Format("Bad value {0} is encountered while reading record length.", b));
				}

				if (EndOfStream())
				{
					throw new EndOfStreamException("End of stream reached while reading record length.");
				}
			}

			// Key
			// Read UTF8 string as key up until '=' is encountered
			sb.Length = 0;
			decoder.Reset();
			while (true)
			{
				if (EndOfStream())
				{
					throw new EndOfStreamException("End of stream reached while reading record key.");
				}

				// Find key end (in case it is present in the current buffer)
				int i = 0;
				int r = Math.Min(byteLen - bytePos, recLen - recPos);
				for (; i < r; i++)
				{
					if (buffer[bytePos + i] == (byte)'=')
					{
						break;
					}
				}

				// Flush remainder of the buffer to UTF8 decoder or up to '=' char if it was encountered in the loop above
				sb.Append(chars, 0, decoder.GetChars(buffer, bytePos, i, chars, 0));
				bytePos += i;
				recPos += i;

				if (recPos == recLen)
				{
					throw new Exception("Not found key-value separator character '=' while reading record.");
				}

				// Found key end in the current buffer (loop above was interrupted prior to i has reached r)
				if (i < r)
				{
					key = sb.ToString();
					//Application.Log(ConsoleColor.DarkYellow, key);
					// Skip '='
					bytePos++;
					recPos++;
					break;
				}
			}

			// Value
			// Read up to recLen bytes and decode them all as value
			sb.Length = 0;
			valueDecoder.Reset();
			while (true)
			{
				if (EndOfStream())
				{
					throw new EndOfStreamException("End of stream reached while reading record value.");
				}

				int r = Math.Min(byteLen - bytePos, recLen - 1 - recPos);

				// Flush remainder of the buffer to the value decoder
				sb.Append(chars, 0, (encodedKeys.Contains(key) ? valueDecoder : decoder).GetChars(buffer, bytePos, r, chars, 0));
				bytePos += r;
				recPos += r;

				// Reached end of value
				if (recPos == recLen - 1)
				{
					value = sb.ToString();
					//Application.Log(ConsoleColor.Blue, value);
					break;
				}
			}

			// End
			// Read single '\n' byte
			{
				if (EndOfStream())
				{
					throw new EndOfStreamException("End of stream reached while reading record end.");
				}

				byte b = buffer[bytePos];
				if (b == '\n')
				{
					bytePos++;
				}
				else
				{
					throw new Exception(string.Format("Bad value {0} is encountered while reading record end.", b));
				}
			}

			return true;
		}
	}
}
