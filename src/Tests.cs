using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace DeepDreamGames
{
	public class Tests
	{
		[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
		public class UnitTestAttribute : Attribute
		{
			
		}

		public class TestException : Exception
		{
			// Ctor
			public TestException(string message) : base(message)
			{
				
			}
			
			public TestException(string format, params object[] args) : base(string.Format(format, args))
			{
				
			}
		}

		// 
		static public void Run()
		{
			int numTotal = 0;
			int numPassed = 0;

			var type = typeof(Tests);

			object instance = null;
			MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
			for (int i = 0; i < methods.Length; i++)
			{
				MethodInfo method = methods[i];
				object[] attributes = method.GetCustomAttributes(typeof(UnitTestAttribute), false);
				if (attributes.Length > 0)
				{
					numTotal++;
					if (instance == null && !method.IsStatic)
					{
						instance = Activator.CreateInstance(type);
					}
					
					try
					{
						method.Invoke(instance, null);
						numPassed++;
						Application.Log(ConsoleColor.Green, "{0} - PASS", method.Name);
					}
					catch (TestException)
					{
						Application.Log(ConsoleColor.Red, "{0} - FAIL", method.Name);
					}
				}
			}

			int numFailed = numTotal - numPassed;
			if (numFailed > 0)
			{
				Application.Log(ConsoleColor.Red, "Unit tests FAILED. Total: {0}, Passed: {1}, Failed: {2}", numTotal, numPassed, numFailed);
			}
			// Success!
			else
			{
				Application.Log(ConsoleColor.Green, "Unit tests PASSED. Total: {0}", numTotal);
			}
		}

		// 
		[UnitTest]
		static private void TestJsonWriter()
		{
			var input = new Dictionary<string, object>()
			{
				{ "string", "http://localhost" },
				{ "int", 8085 },
				{ "array", new List<int> { -1, 2, -3, 4, -5 } },
				{ "null", null },
				{ "float", 1.5f },
				{ "double", -3.5 },
				{ "true", true },
				{ "false", false },
				{ "unicode", "Пр2ивет" },
			};
			string check = @"{""string"":""http://localhost"",""int"":8085,""array"":[-1,2,-3,4,-5],""null"":null,""float"":1.5,""double"":-3.5,""true"":true,""false"":false,""unicode"":""\u041f\u04402\u0438\u0432\u0435\u0442""}";

			using (var stream = new MemoryStream())
			{
				StreamWriter streamWriter = new StreamWriter(stream);
				JsonWriter writer = new JsonWriter(streamWriter);
				writer.WriteValue(input);

				streamWriter.Close();
				string output = Encoding.UTF8.GetString(stream.ToArray());
				IsEqual(output, check);
			}
		}

		// 
		[UnitTest]
		static private void TestJsonReader()
		{
			string input = @"{""string"":""http://localhost"",""number"":8085,""array"":[-1,2,-3,4,-5],""null"":null,""fractional"":1.5,""negative"":-3.5,""true"":true,""false"":false,""unicode"":""\u041f\u04402\u0438\u0432\u0435\u0442"",""object"":{""integer"":9999,""string"":""test"",""double"":-3.5}}";

			using (var inputStream = new MemoryStream(Encoding.UTF8.GetBytes(input)))
			using (StreamReader streamReader = new StreamReader(inputStream))
			{
				JsonReader reader = new JsonReader();
				var root = reader.ReadToEnd(streamReader, StringComparer.OrdinalIgnoreCase) as Dictionary<string, object>;

				IsEqual((string)root["string"], "http://localhost");
				IsEqual((long)root["number"], 8085);
				IsEqual((List<object>)root["array"], new List<object> { -1L, 2L, -3L, 4L, -5L });
				IsEqual(root["null"], null);
				IsEqual((double)root["fractional"], 1.5);
				IsEqual((double)root["negative"], -3.5);
				IsEqual((bool)root["true"], true);
				IsEqual((bool)root["false"], false);
				IsEqual((string)root["unicode"], "Пр2ивет");
				// Object
				var obj = root["object"] as Dictionary<string, object>;
				IsEqual((long)obj["integer"], 9999L);
				IsEqual((string)obj["string"], "test");
				IsEqual((double)obj["double"], -3.5);

				// Write data tree back using JsonWriter and compare result with initial string
				using (var outputStream = new MemoryStream())
				{
					StreamWriter streamWriter = new StreamWriter(outputStream);
					JsonWriter writer = new JsonWriter(streamWriter);
					writer.WriteValue(root);

					streamWriter.Close();
					string output = Encoding.UTF8.GetString(outputStream.ToArray());
					IsEqual(input, output);
				}
			}
		}

		#region TryParse
		// 
		[UnitTest]
		static private void TestSemVerValid()
		{
			string[] valid = new string[]
			{
				"0.0.0",
				
				// Valid Semantic Versions from https://regex101.com/r/vkijKf/1/
				"0.0.4",
				"1.2.3",
				"10.20.30",
				"1.1.2-prerelease+meta",
				"1.1.2+meta",
				"1.1.2+meta-valid",
				"1.0.0-alpha",
				"1.0.0-beta",
				"1.0.0-alpha.beta",
				"1.0.0-alpha.beta.1",
				"1.0.0-alpha.1",
				"1.0.0-alpha0.valid",
				"1.0.0-alpha.0valid",
				"1.0.0-alpha-a.b-c-somethinglong+build.1-aef.1-its-okay",
				"1.0.0-rc.1+build.1",
				"2.0.0-rc.1+build.123",
				"1.2.3-beta",
				"10.2.3-DEV-SNAPSHOT",
				"1.2.3-SNAPSHOT-123",
				"1.0.0",
				"2.0.0",
				"1.1.7",
				"2.0.0+build.1848",
				"2.0.1-alpha.1227",
				"1.0.0-alpha+beta",
				"1.2.3----RC-SNAPSHOT.12.9.1--.12+788",
				"1.2.3----R-S.12.9.1--.12+meta",
				"1.2.3----RC-SNAPSHOT.12.9.1--.12",
				"1.0.0+0.build.1-rc.10000aaa-kk-0.1",
				"99999999999999999999999.999999999999999999.99999999999999999",
				"1.0.0-0A.is.legal",
			};

			foreach (var item in valid)
			{
				SemVer value;
				bool isValid = SemVer.TryParse(item, out value);
				if (!isValid)
				{
					throw new TestException("Failed to parse '{0}' as valid SemVer", item);
				}
				else
				{
					// Additional check (turn SemVer back to string and compare with original)
					string valueString = value.ToString();
					if (!string.Equals(valueString, item, StringComparison.Ordinal))
					{
						throw new TestException("SemVer.ToString() returned '{0}' but '{1}' was expected.", valueString, item);
					}
				}
			}
		}

		// 
		[UnitTest]
		static private void TestSemVerInvalid()
		{
			string[] invalid = new string[]
			{
				"00.1.2",
				"01.2.3",
				"1.02.3",
				"1.2.03",
				"1.2.3-",
				"1.2.3-.",
				"1.2.3-..",
				"1.2.3-a.",
				"1.2.3+",
				"0.0.0k",
				"0.0z.0",
				"0.z0.0",
				"1.2.3 ",
				" 1.2.3",
				"1.2.3-привет",

				// Invalid Semantic Versions from https://regex101.com/r/vkijKf/1/
				"1",
				"1.2",
				"1.2.3-0123",
				"1.2.3-0123.0123",
				"1.1.2+.123",
				"+invalid",
				"-invalid",
				"-invalid+invalid",
				"-invalid.01",
				"alpha",
				"alpha.beta",
				"alpha.beta.1",
				"alpha.1",
				"alpha+beta",
				"alpha_beta",
				"alpha.",
				"alpha..",
				"beta",
				"1.0.0-alpha_beta",
				"-alpha.",
				"1.0.0-alpha..",
				"1.0.0-alpha..1",
				"1.0.0-alpha...1",
				"1.0.0-alpha....1",
				"1.0.0-alpha.....1",
				"1.0.0-alpha......1",
				"1.0.0-alpha.......1",
				"01.1.1",
				"1.01.1",
				"1.1.01",
				"1.2",
				"1.2.3.DEV",
				"1.2-SNAPSHOT",
				"1.2.31.2.3----RC-SNAPSHOT.12.09.1--..12+788",
				"1.2-RC-SNAPSHOT",
				"-1.0.3-gamma+b7718",
				"+justmeta",
				"9.8.7+meta+meta",
				"9.8.7-whatever+meta+meta",
				"99999999999999999999999.999999999999999999.99999999999999999----RC-SNAPSHOT.12.09.1--------------------------------..12",
			};

			foreach (var item in invalid)
			{
				SemVer value;

				bool isValid = SemVer.TryParse(item, out value);
				if (isValid)
				{
					throw new TestException("{0} is expected to be invalid, but was parsed as valid SemVer: {1}", item, value.ToString());
				}
			}
		}
		#endregion

		#region Compare
		private enum Condition
		{
			None,
			Equal,
			Less,
			Greater,
		}

		[UnitTest]
		private void TestNumbersSort()
		{
			List<string> list = new List<string>() { "123456", "89", "9999", "10", "333333", "80", "0", "345" };
			var method = typeof(SemVer).GetMethod("CompareNumber", BindingFlags.NonPublic | BindingFlags.Static);
			list.Sort(delegate (string x, string y)
			{
				return (int)method.Invoke(null, new object[] { x, 0, x.Length, y, 0, y.Length });
			});
			//Shell.Write(string.Join("\n", list.ToArray()));

			string[] check = new string[] { "0", "10", "80", "89", "345", "9999", "123456", "333333" };
			for (int i = 0; i < check.Length; i++)
			{
				if (!string.Equals(list[i], check[i], StringComparison.Ordinal))
				{
					throw new TestException("Sort order wrong! '{0}' != '{1}'", list[i], check[i]);
				}
			}
		}

		[UnitTest]
		private void TestWordSort()
		{
			List<string> list = new List<string>() { "abcdef", "abcd", "ef", "ab", "", "bcd" };
			var method = typeof(SemVer).GetMethod("CompareWord", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
			list.Sort(delegate (string x, string y)
			{
				return (int)method.Invoke(null, new object[] { x, 0, x.Length, y, 0, y.Length });
			});
			//Shell.Write(string.Join("\n", list.ToArray()));

			string[] check = new string[] { "", "ab", "abcd", "abcdef", "bcd", "ef" };
			for (int i = 0; i < check.Length; i++)
			{
				if (!string.Equals(list[i], check[i], StringComparison.Ordinal))
				{
					throw new TestException("Sort order wrong! '{0}' != '{1}'", list[i], check[i]);
				}
			}
		}

		[UnitTest]
		private void TestCompare()
		{
			// 1.0.0-alpha == 1.0.0-alpha+build
			Compare("1.0.0-alpha", Condition.Equal, "1.0.0-alpha+build");

			// 1.0.0 < 2.0.0 < 2.1.0 < 2.1.1
			Compare("1.0.0", "2.0.0", "2.1.0", "2.1.1");

			// 1.0.0-alpha < 1.0.0
			Compare("1.0.0-alpha", Condition.Less, "1.0.0");

			// 1.0.0-alpha < 1.0.0-alpha.1 < 1.0.0-alpha.beta < 1.0.0-beta < 1.0.0-beta.2 < 1.0.0-beta.11 < 1.0.0-rc.1 < 1.0.0
			Compare("1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0");
		}

		// Compare array of SemVer numbers in ascending order
		private void Compare(params string[] array)
		{
			// Forward
			// . . a < b . .
			for (int i = 0; i < array.Length - 1; i++)
			{
				string a = array[i];
				for (int j = i + 1; j < array.Length; j++)
				{
					string b = array[j];
					Compare(a, Condition.Less, b);
				}
			}

			// Reverse
			// . . b < a . .
			for (int i = array.Length - 1; i > 0; i--)
			{
				string a = array[i];
				for (int j = i - 1; j >= 0; j--)
				{
					string b = array[j];
					Compare(a, Condition.Greater, b);
				}
			}
		}

		// 
		private Condition FromComparison(int comparison)
		{
			if (comparison < 0) { return Condition.Less; }
			if (comparison > 0) { return Condition.Greater; }
			return Condition.Equal;
		}

		// 
		private void Compare(string a, Condition check, string b)
		{
			Condition result = FromComparison(SemVer.Compare(a, b));
			if (result != check)
			{
				throw new TestException("'{0}' was expected to be {1} than '{2}', but was {3}", a, check, b, result);
			}
		}
		#endregion

		// 
		static private void IsEqual<T>(T a, T b)
		{
			if (!Equals(a, b))
			{
				throw new TestException("'{0}' != '{1}'", a, b);
			}
		}

		// 
		static private bool Equals<T>(T a, T b)
		{
			Type type = typeof(T);
			// Compare collection contents - not their references
			if (typeof(ICollection).IsAssignableFrom(type))
			{
				var collA = a as ICollection;
				var collB = b as ICollection;
				// One is null, other is not
				if ((collA == null) != (collB == null))
				{
					return false;
				}
				// Both null
				if (collA == null)
				{
					return true;
				}
				// Length differs
				if (collA.Count != collB.Count)
				{
					return false;
				}
				// Lengths equal - compare each item
				IEnumerator eA = collA.GetEnumerator();
				IEnumerator eB = collB.GetEnumerator();
				for (int i = 0; i < collA.Count; i++)
				{
					eA.MoveNext();
					eB.MoveNext();
					if (!Equals(eA.Current, eB.Current))
					{
						return false;
					}
				}
				return true;
			}
			else
			{
				return EqualityComparer<T>.Default.Equals(a, b);
			}
		}
	}
}
