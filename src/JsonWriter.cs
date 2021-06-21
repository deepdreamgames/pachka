using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace DeepDreamGames
{
	public class JsonWriter
	{
		private enum State
		{
			None,
			AfterProperty,
			AfterValue,
		}

		private class TypeInfo
		{
			public FieldInfo[] fields;
			public PropertyInfo[] properties;
		}

		#region Constants
		static private readonly NumberFormatInfo numberFormat = NumberFormatInfo.InvariantInfo;
		#endregion

		#region Public Fields
		public bool prettyPrint;
		#endregion

		#region Private Fields
		private StreamWriter writer;
		private int indent;
		private Stack<State> stack = new Stack<State>();
		private State state;
		static private Dictionary<Type, TypeInfo> types = new Dictionary<Type, TypeInfo>();
		private bool firstNewLine = false;
		#endregion

		#region Public Methods
		// Ctor
		public JsonWriter(StreamWriter writer)
		{
			this.writer = writer;
		}

		public void WriteObjectStart() { WriteStart('{'); }
		public void WriteObjectEnd() { WriteEnd('}'); }

		public void WriteArrayStart() { WriteStart('['); }
		public void WriteArrayEnd() { WriteEnd(']'); }

		// 
		public void WriteProperty(string name)
		{
			WriteComma(); WriteNewLine();

			writer.Write('"');
			writer.Write(name);
			writer.Write('"');
			if (prettyPrint) { writer.Write(" : "); }
			else { writer.Write(':'); }

			state = State.AfterProperty;
		}

		// Helper
		public void Write(string name, object value)
		{
			WriteProperty(name);
			WriteValue(value);
		}
		#endregion

		#region Private Methods
		// 
		private void ContextPush()
		{
			stack.Push(state);
			state = State.None;
		}

		// 
		private void ContextPop()
		{
			state = stack.Pop();
		}

		// 
		private void NewLine()
		{
			if (prettyPrint)
			{
				// New Line
				if (firstNewLine)
				{
					writer.Write(writer.NewLine);
				}
				// Don't write first new line. Not using writer.BaseStream.Length > 0 check here 
				// since stream Length will be 0 until first Flush(), and we doesn't want to force that to happen
				else
				{
					firstNewLine = true;
				}

				// Indent
				for (int i = 0; i < indent; i++)
				{
					writer.Write('\t');
				}
			}
		}

		// 
		private void Comma()
		{
			writer.Write(',');
			if (prettyPrint) { writer.Write(' '); }
		}

		// 
		private void WriteNewLine()
		{
			if (state != State.AfterProperty)
			{
				NewLine();
			}
		}

		// 
		private void WriteComma()
		{
			if (state == State.AfterValue)
			{
				Comma();
			}
		}

		// 
		private void WriteStart(char ch)
		{
			WriteComma();
			NewLine();

			writer.Write(ch);
			indent++;
			ContextPush();
		}

		// 
		private void WriteEnd(char ch)
		{
			ContextPop();
			indent--;
			NewLine();
			writer.Write(ch);

			state = State.AfterValue;
		}

		// 
		private void Append(char ch)
		{
			const string hex = "0123456789abcdef";
			switch (ch)
			{
				case '"': writer.Write("\\\""); break;     // \"
				case '\\': writer.Write("\\\\"); break;    // \\
				case '\b': writer.Write("\\b"); break;     // \b
				case '\f': writer.Write("\\f"); break;     // \f
				case '\n': writer.Write("\\n"); break;     // \n
				case '\r': writer.Write("\\r"); break;     // \r
				case '\t': writer.Write("\\t"); break;     // \t
				default:
					ushort n = ch;
					if (n >= 32 && n <= 126)
					{
						writer.Write(ch);
					}
					else
					{
						writer.Write("\\u");
						// Non-allocating n.ToString("X4", numberFormat):
						writer.Write(hex[n >> 12]);
						writer.Write(hex[(n >> 8) & 0xF]);
						writer.Write(hex[(n >> 4) & 0xF]);
						writer.Write(hex[n & 0xF]);
					}
					break;
			}
		}

		// 
		static private TypeInfo GetTypeInfo(Type type)
		{
			const BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy;

			TypeInfo info;
			if (!types.TryGetValue(type, out info))
			{
				info = new TypeInfo();

				// Fields
				List<FieldInfo> fields = new List<FieldInfo>();
				foreach (FieldInfo field in type.GetFields(bindingFlags))
				{
					// Ignore const fields
					if (field.IsLiteral) { continue; }

					// Skip obsolete properties
					if (field.GetCustomAttributes(typeof(ObsoleteAttribute), true).Length > 0) { continue; }

					fields.Add(field);
				}
				info.fields = fields.ToArray();

				// Properties
				List<PropertyInfo> properties = new List<PropertyInfo>();
				foreach (PropertyInfo property in type.GetProperties(bindingFlags))
				{
					// Skip obsolete properties
					if (property.GetCustomAttributes(typeof(ObsoleteAttribute), true).Length > 0) { continue; }

					properties.Add(property);
				}
				info.properties = properties.ToArray();

				types.Add(type, info);
			}
			return info;
		}

		// 
		public void WriteValue(object value)
		{
			if (value == null)
			{
				WriteComma(); WriteNewLine();
				writer.Write("null");
			}
			else
			{
				Type type = value.GetType();

				// enum
				if (type.IsEnum)
				{
					// Write enum as Underlying Type
					type = Enum.GetUnderlyingType(type);
					WriteValue(Convert.ChangeType(value, type));
				}
				// string
				else if (type == typeof(string))
				{
					WriteComma(); WriteNewLine();
					string s = (string)value;
					writer.Write('"');
					for (int i = 0, l = s.Length; i < l; i++)
					{
						Append(s[i]);
					}
					writer.Write('"');
				}
				// char
				else if (type == typeof(char))
				{
					WriteComma(); WriteNewLine();
					writer.Write('"');
					Append((char)value);
					writer.Write('"');
				}
				// sbyte byte short ushort int uint long ulong
				else if (type == typeof(sbyte) || type == typeof(byte) || type == typeof(short) || type == typeof(ushort) || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong))
				{
					WriteComma(); WriteNewLine();
					writer.Write(value.ToString());
				}
				// float
				else if (type == typeof(float))
				{
					WriteComma(); WriteNewLine();
					writer.Write(((float)value).ToString(numberFormat));
				}
				// double
				else if (type == typeof(double))
				{
					WriteComma(); WriteNewLine();
					writer.Write(((double)value).ToString(numberFormat));
				}
				// decimal
				else if (type == typeof(decimal))
				{
					WriteComma(); WriteNewLine();
					writer.Write(((decimal)value).ToString(numberFormat));
				}
				// bool
				else if (type == typeof(bool))
				{
					WriteComma(); WriteNewLine();
					writer.Write(((bool)value) ? "true" : "false");
				}
				// array
				else if (typeof(IList).IsAssignableFrom(type))
				{
					WriteArrayStart();

					IList list = value as IList;
					for (int i = 0; i < list.Count; i++)
					{
						WriteValue(list[i]);
					}

					WriteArrayEnd();
				}
				// Dictionary<string, T>
				else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
				{
					WriteObjectStart();

					// Only supporting Dictionary<string, T>
					Type keyType = type.GetGenericArguments()[0];
					if (keyType == typeof(string))
					{
						IDictionary dict = value as IDictionary;
						{
							IDictionaryEnumerator e = dict.GetEnumerator();
							using (IDisposable d = e as IDisposable)
							{
								while (e.MoveNext())
								{
									Write((string)e.Key, e.Value);
								}
							}
						}
					}

					WriteObjectEnd();
				}
				// object
				else
				{
					WriteObjectStart();

					TypeInfo info = GetTypeInfo(type);

					// Fields
					FieldInfo[] fields = info.fields;
					for (int i = 0; i < fields.Length; i++)
					{
						FieldInfo field = fields[i];
						Write(field.Name, field.GetValue(value));
					}

					// Properties
					PropertyInfo[] properties = info.properties;
					for (int i = 0; i < properties.Length; i++)
					{
						PropertyInfo property = properties[i];
						Write(property.Name, property.GetValue(value, null));
					}

					WriteObjectEnd();
				}
			}

			state = State.AfterValue;
		}
		#endregion
	}
}