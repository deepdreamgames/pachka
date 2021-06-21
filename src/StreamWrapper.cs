using System;
using System.IO;

namespace DeepDreamGames
{
	// Limits underlying stream reading to specified size range. 
	public class StreamWrapper : Stream
	{
		private Stream stream;
		private long size;
		private long position;

		// 
		public void Initialize(Stream stream, long size)
		{
			this.stream = stream;
			this.size = size;
			position = 0L;
		}
		
		// 
		public void Deinitialize()
		{
			stream = null;
			size = 0L;
			position = 0L;
		}

		#region Stream implementation
		public override bool CanRead { get { return position < size && stream.CanRead; } }
		public override bool CanSeek { get { return false; } }
		public override bool CanWrite { get { return false; } }
		public override long Length { get { return size; } }
		public override long Position { get { return position; } set { throw new NotSupportedException(); } }

		// 
		public override int Read(byte[] buffer, int offset, int count)
		{
			long remainder = size - position;
			if (remainder < count)
			{
				count = (int)remainder;
			}
			
			int read = stream.Read(buffer, offset, count);
			position += read;
			
			return read;
		}

		// 
		protected override void Dispose(bool disposing)
		{
			Deinitialize();
			base.Dispose(disposing);
		}

		public override void Close() { stream.Close(); }
		public override void Flush() { stream.Flush(); }
		public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
		public override void SetLength(long value) { throw new NotSupportedException(); }
		public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
		#endregion
	}
}
