using System;
using System.Text;

namespace DeepDreamGames
{
	public class TarHeader
	{
		#region Constants
		private const int lengthName = 100;
		private const int lengthMode = 8;
		private const int lengthUid = 8;
		private const int lengthGid = 8;
		private const int lengthSize = 12;
		private const int lengthMtime = 12;
		private const int lengthChksum = 8;
		private const int lengthTypeflag = 1;
		private const int lengthLinkname = 100;
		private const int lengthMagic = 6;
		private const int lengthVersion = 2;
		private const int lengthUname = 32;
		private const int lengthGname = 32;
		private const int lengthDevmajor = 8;
		private const int lengthDevminor = 8;
		private const int lengthPrefix = 155;
		#endregion

		public string name;
		public int mode;
		public int uid;
		public int gid;
		public long size;
		public DateTime mtime;
		public int chksum;
		public byte typeflag;
		public string linkname;
		public string magic;
		public string version;
		public string uname;
		public string gname;
		public int devmajor;
		public int devminor;

		// 
		static public bool TryRead(byte[] buffer, Encoding encoding, TarHeader header)
		{
			int pos = 0;

			// name
			header.name = ReadString(buffer, pos, lengthName, encoding);
			pos += lengthName;
			if (string.IsNullOrEmpty(header.name))
			{
				return false;
			}

			// mode
			header.mode = (int)ReadNumber(buffer, pos, lengthMode);
			pos += lengthMode;

			// uid
			header.uid = (int)ReadNumber(buffer, pos, lengthUid);
			pos += lengthUid;

			// gid
			header.gid = (int)ReadNumber(buffer, pos, lengthGid);
			pos += lengthGid;

			// size
			header.size = ReadNumber(buffer, pos, lengthSize);
			pos += lengthSize;

			// mtime
			// The mtime field represents the data modification time of the file at the time it was archived. 
			// It represents the integer number of seconds since January 1, 1970, 00:00 Coordinated Universal Time.
			long seconds = ReadNumber(buffer, pos, lengthMtime);
			pos += lengthMtime;
			header.mtime = new DateTime(Application.Epoch.Ticks + seconds * Application.TicksPerSecond);

			// chksum
			header.chksum = (int)ReadNumber(buffer, pos, lengthChksum);
			pos += lengthChksum;

			// typeflag
			header.typeflag = buffer[pos];
			pos += lengthTypeflag;

			// linkname
			header.linkname = ReadString(buffer, pos, lengthLinkname, encoding);
			pos += lengthLinkname;

			// magic
			header.magic = ReadString(buffer, pos, lengthMagic, encoding);
			pos += lengthMagic;

			if (header.magic == "ustar")
			{
				// version
				header.version = ReadString(buffer, pos, lengthVersion, encoding);
				pos += lengthVersion;

				// uname
				header.uname = ReadString(buffer, pos, lengthUname, encoding);
				pos += lengthUname;

				// gname
				header.gname = ReadString(buffer, pos, lengthGname, encoding);
				pos += lengthGname;

				// devmajor
				header.devmajor = (int)ReadNumber(buffer, pos, lengthDevmajor);
				pos += lengthDevmajor;

				// devminor
				header.devminor = (int)ReadNumber(buffer, pos, lengthDevminor);
				pos += lengthDevminor;

				// prefix
				string prefix = ReadString(buffer, pos, lengthPrefix, encoding);
				pos += lengthPrefix;
				if (!string.IsNullOrEmpty(prefix))
				{
					header.name = prefix + '/' + header.name;
				}
			}

			return true;
		}

		// Read null-terminated character string
		static private string ReadString(byte[] buffer, int position, int length, Encoding encoding)
		{
			if (encoding == null) { encoding = Encoding.ASCII; }

			// Find null-terminated character string end
			int l = 0;
			for (; l < length; l++)
			{
				byte b = buffer[position + l];
				if (b == 0)
				{
					break;
				}
			}
			return encoding.GetString(buffer, position, l);
		}

		// Read zero-filled octal number in ASCII
		static private long ReadNumber(byte[] buffer, int position, int length)
		{
			long value = 0L;

			bool padding = true;
			for (int i = position, end = position + length; i < end; i++)
			{
				byte b = buffer[i];
				if (b >= '0' && b <= '7') { padding = false; value = value * 8L + (b - '0'); }
				else if (padding && b == (byte)' ') {  }
				else { break; }
			}

			return value;
		}

		// 
		public override string ToString()
		{
			StringBuilder sb = new StringBuilder();
			Append(sb);
			return sb.ToString();
		}

		// 
		public void Append(StringBuilder sb, string indent = "\n")
		{
			sb.Append(indent).Append("name: ").AppendString(name);
			sb.Append(indent).Append("mode: ").Append(mode);
			sb.Append(indent).Append("uid: ").Append(uid);
			sb.Append(indent).Append("gid: ").Append(gid);
			sb.Append(indent).Append("size: ").Append(size);
			sb.Append(indent).Append("mtime: ").Append(mtime);
			sb.Append(indent).Append("chksum: ").Append(chksum);
			sb.Append(indent).Append("typeflag: ").Append(typeflag);
			sb.Append(indent).Append("linkname: ").AppendString(linkname);
			sb.Append(indent).Append("magic: ").AppendString(magic);
			sb.Append(indent).Append("version: ").AppendString(version);
			sb.Append(indent).Append("uname: ").AppendString(uname);
			sb.Append(indent).Append("gname: ").AppendString(gname);
			sb.Append(indent).Append("devmajor: ").Append(devmajor);
			sb.Append(indent).Append("devminor: ").Append(devminor);
		}
	}
}
