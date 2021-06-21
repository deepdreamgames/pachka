using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DeepDreamGames
{
	// Build mutable data tree from incoming Json stream
	public class JsonReader
	{
		#region Constants
		static private readonly Type typeObject = typeof(Dictionary<string, object>);
		static private readonly Type typeArray = typeof(List<object>);
		private const int lineFeed = '\n';
		#endregion

		#region Accessors
		private bool waitingForValue { get { return property != null; } }
		#endregion

		#region Private Fields
		private StreamReader reader;                        // Input reader
		private object root;                                // Data tree root
		private Stack<object> stack = new Stack<object>();
		private object context;
		private string property;
		private StringBuilder sb = new StringBuilder();
		private object syncRoot = new object();
		private StringComparer comparer;
		private int line = 1;
		private int column;
		#endregion

		#region Public Methods
		// 
		public object ReadToEnd(StreamReader reader, StringComparer comparer = null)
		{
			lock (syncRoot)
			{
				this.reader = reader;
				this.comparer = comparer != null ? comparer : StringComparer.Ordinal;
				try
				{
					while (!reader.EndOfStream)
					{
						Read();
					}

					object result = root;
					Reset();
					return result;
				}
				catch (Exception ex)
				{
					throw new Exception(string.Format("An exception has occured while reading JSON at line: {0}, column: {1}", line, column), ex);
				}
				finally
				{
					Reset();
				}
			}
		}
		#endregion

		#region Private Methods
		// 
		private void Reset()
		{
			reader = null;
			root = null;
			stack.Clear();
			context = null;
			property = null;
			sb.Length = 0;
			comparer = null;
			line = 1;
			column = 0;
		}
		
		// 
		private int ReadChar()
		{
			// StreamReader internally using char[] buffer so that's not a problem to read characters one by one here
			int value = reader.Read();
			if (value >= 0)
			{
				if (value == lineFeed)
				{
					line++;
					column = 0;
				}
				else
				{
					column++;
				}
			}
			return value;
		}

		// 
		private void Read()
		{
			Type contextType = context != null ? context.GetType() : null;

			// Advances StreamReader to the next non-whitespace char and returns it
			char ch;
			do
			{
				int value = ReadChar();
				if (value < 0) { return; }  // EndOfStream
				ch = (char)value;
			}
			while (" \t\n\r".IndexOf(ch) >= 0);
			string keyword = null;
			object primitive = null;

			switch (ch)
			{
				default:
					{
						throw new Exception(string.Format("Unhandled char '{0}'", ch));
					}
				case '{':
					{
						var value = new Dictionary<string, object>(comparer);
						Add(value);
						stack.Push(value);
						context = value;
						break;
					}
				case '}':
					{
						if (context != null)
						{
							if (context.GetType() == typeObject)
							{
								if (property == null)
								{
									stack.Pop();
									context = stack.Count == 0 ? null : stack.Peek();
								}
								else
								{
									throw new Exception("Property is set but the value isn't assigned - object cannot be closed.");
								}
							}
							else
							{
								throw new Exception("Unexpected EndObject (no object to close).");
							}
						}
						else
						{
							throw new Exception("Context is null.");
						}
						break;
					}
				case '[':
					{
						var value = new List<object>();
						Add(value);
						stack.Push(value);
						context = value;
						break;
					}
				case ']':
					{
						if (context != null)
						{
							if (context.GetType() == typeArray)
							{
								stack.Pop();
								context = stack.Count == 0 ? null : stack.Peek();
							}
							else
							{
								throw new Exception("Unexpected EndArray (no array to close).");
							}
						}
						else
						{
							throw new Exception("Context is null.");
						}
						break;
					}
				case ',':
					{
						if (contextType == typeObject && waitingForValue)
						{
							throw new Exception("Unexpected comma.");
						}
						break;
					}
				case ':':
					{
						if (contextType != typeObject || !waitingForValue)
						{
							throw new Exception("Unexpected colon.");
						}
						break;
					}
				case '"':
					{
						sb.Length = 0;
						while (true)
						{
							if (reader.EndOfStream) { throw new Exception("EndOfStream reached while parsing string"); }
							ch = (char)ReadChar();

							// End quote
							if (ch == '"')
							{
								break;
							}
							// Quoted char
							else
							{
								if (ch == '\\')
								{
									if (!reader.EndOfStream)
									{
										ch = (char)ReadChar();

										switch (ch)
										{
											case 'a': sb.Append('\a'); break;
											case 'b': sb.Append('\b'); break;
											case 'f': sb.Append('\f'); break;
											case 'n': sb.Append('\n'); break;
											case 'r': sb.Append('\r'); break;
											case 't': sb.Append('\t'); break;
											case 'v': sb.Append('\v'); break;
											case '\'': sb.Append('\''); break;
											case '\"': sb.Append('\"'); break;
											case '\\': sb.Append('\\'); break;
											case '/': sb.Append('/'); break;
											case '?': sb.Append('?'); break;
											case 'u':
												// ASCII character in hexadecimal notation (\uH \uHH \uHHH or \uHHHH)
												{
													ushort value = 0;
													int i = 0;
													while (i < 4 && !reader.EndOfStream)
													{
														ushort v = value;
														char c = (char)reader.Peek();
														if (c >= '0' && c <= '9') { v = (ushort)(v * 16 + (c - '0')); }
														else if (c >= 'A' && c <= 'F') { v = (ushort)(v * 16 + (c - 'A') + 10); }
														else if (c >= 'a' && c <= 'f') { v = (ushort)(v * 16 + (c - 'a') + 10); }
														else { break; }

														if (v < value) { throw new Exception("ushort overflow has occured."); }
														value = v;
														ReadChar(); // Consume valid hex character
														i++;
													}
													sb.Append((char)value);
												}
												break;
											default:
												throw new Exception(string.Format("Unhandled escape sequence '\\{0}'.", ch));
										}
									}
									else
									{
										throw new Exception("EndOfStream reached in string after single backslash character");
									}
								}
								else
								{
									sb.Append(ch);
								}
							}
						}
						Add(sb.ToString());
					}
					break;
				case '0': case '1': case '2': case '3': case '4': case '5': case '6': case '7': case '8': case '9': case '-':
					{
						sb.Length = 0;
						sb.Append(ch);

						bool fractional = false;
						int n;
						while ((n = reader.Peek()) >= 0)
						{
							ch = (char)n;
							int i = "0123456789+-.eE".IndexOf(ch);
							if (i < 0) { break; }

							// Advance
							ReadChar();
							fractional |= i >= 12;
							sb.Append(ch);
						}

						if (fractional)
						{
							Add(double.Parse(sb.ToString()));
						}
						else
						{
							Add(long.Parse(sb.ToString()));
						}
						break;
					}
				case 'F':
				case 'f':
					{
						keyword = "false";
						primitive = false;
						goto keyword;
					}
				case 'T':
				case 't':
					{
						keyword = "true";
						primitive = true;
						goto keyword;
					}
				case 'N':
				case 'n':
					{
						keyword = "null";
						primitive = null;
						goto keyword;
					}
					keyword:
					{
						int index = 0;
						while (index < keyword.Length && keyword[index] == char.ToLower(ch))
						{
							if (reader.EndOfStream) { throw new Exception("EndOfStream reached while parsing keyword"); }
							ch = (char)ReadChar();
							index++;
						}
						if (index == keyword.Length)
						{
							Add(primitive);
						}
						else
						{
							throw new Exception("Unknown keyword");
						}
						break;
					}
			}
		}

		// Validates and adds value to the data tree
		private void Add(object value)
		{
			if (root != null)
			{
				if (context != null)
				{
					Type contextType = context.GetType();
					if (contextType == typeObject)
					{
						if (waitingForValue)
						{
							var o = context as Dictionary<string, object>;
							// RFC 4627: The names within an object SHOULD be unique
							if (o.ContainsKey(property))
							{
								throw new Exception(string.Format("Duplicate property name '{0}'. Property names within an object should be unique!", property));
							}
							o[property] = value;
							property = null;
						}
						else
						{
							if (property == null)
							{
								if (value != null)
								{
									Type type = value.GetType();
									if (type == typeof(string))
									{
										property = value as string;
									}
									else
									{
										throw new Exception("Value without property.");
									}
								}
								else
								{
									throw new Exception("Property cannot be null.");
								}
							}
							else
							{
								throw new Exception("Attempt to set property twice.");
							}
						}
					}
					else if (contextType == typeArray)
					{
						var array = context as List<object>;
						array.Add(value);
					}
				}
				// context is null but root is assigned => attempt to add multiple objects at root level
				else
				{
					throw new Exception("Can't have multiple objects or arrays at the root level.");
				}
			}
			// No root
			else
			{
				if (value != null)
				{
					Type type = value.GetType();
					if (type == typeObject || type == typeArray)
					{
						root = value;
					}
					else
					{
						throw new Exception("Only object or array is allowed to be in the root.");
					}
				}
				else
				{
					throw new Exception("Only object or array is allowed to be in the root.");
				}
			}
		}
		#endregion
	}
}
