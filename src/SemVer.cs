using System;
using System.Text;

namespace DeepDreamGames
{
	// Non-allocating Semantic Version parsing and comparison https://semver.org/
	public struct SemVer
	{
		public struct Range
		{
			public int start;
			public int end;
	
			public int Length { get { return end - start; } }
			
			//
			public void Append(string input, StringBuilder dst)
			{
				dst.Append(input, start, end - start);
			}
			
			// 
			public string ToString(string input)
			{
				return input.Substring(start, end);
			}
	
			// Ctor
			public Range(int start, int end)
			{
				this.start = start;
				this.end = end;
			}
		}
	
		// MAJOR.MINOR.PATCH-label+build
		#region Public Fields
		public Range Major { get; private set; }
		public Range Minor { get; private set; }
		public Range Patch { get; private set; }
	
		public Range Label { get; private set; }
		public Range Build { get; private set; }
	
		private string input;
		#endregion
	
		#region Public Methods
		// 
		static public bool TryParse(string input, out SemVer value)
		{
			return TryParse(input, 0, input.Length, out value);
		}
	
		// 
		static public bool TryParse(string input, int position, int to, out SemVer value)
		{
			value = new SemVer();
			value.input = input;
			if (string.IsNullOrEmpty(input)) { return false; }
	
			to = Clamp(to, 0, input.Length);
	
			int start, end;
			int l = input.Length;
	
			// Major
			if (!TryParseNumber(input, position, to, out end)) { return false; }
			if (end == l || input[end] != '.') { return false; }
			value.Major = new Range(position, end);
			position = end + 1; // Skip separator
	
			// Minor
			if (!TryParseNumber(input, position, to, out end)) { return false; }
			if (end == l || input[end] != '.') { return false; }
			value.Minor = new Range(position, end);
			position = end + 1; // Skip separator
	
			// Patch
			if (!TryParseNumber(input, position, to, out end)) { return false; }
			value.Patch = new Range(position, end);
			position = end;
	
			// Label (optional)
			if (end < l && input[end] == '-')
			{
				position++; // Skip separator
				if (position == l) { return false; }	// Nothing after '-'
	
				start = position;
				
				// Label. Parse to the end or until '+' is encountered
				// Series of dot-separated identifiers. 
				while (position < to)
				{
					// Identifiers must be valid
					bool isNumber;
					if (!TryParseLabelIdentifier(input, position, to, out end, out isNumber)) { return false; }
					position = end;
	
					if (end < l)
					{
						char ch = input[end];
						if (ch == '+')
						{
							break;
						}
						else if (ch == '.')
						{
							position++;	// Skip separator
						}
						else
						{
							// Invalid char
							return false;
						}
					}
				}
	
				value.Label = new Range(start, end);
			}
	
			// Build (optional)
			if (end < l && input[end] == '+')
			{
				position++;	// Skip separator
				if (position == l) { return false; }	// Nothing after '+'
	
				start = position;
	
				// Build. Parse to the end. 
				// Series of dot-separated identifiers.
				while (position < to)
				{
					// Identifiers must be valid
					if (!TryParseBuildIdentifier(input, position, to, out end)) { return false; }
					position = end;
					
					if (end < l)
					{
						char ch = input[end];
						if (ch == '.')
						{
							position++;	// Skip separator
						}
						else
						{
							// Invalid char
							return false;
						}
					}
				}
	
				value.Build = new Range(start, end);
			}
	
			return end == l;
		}
	
		// Compare x and y as SemVer values
		static public int Compare(string x, string y)
		{
			return Compare(x, 0, x.Length, y, 0, y.Length);
		}
	
		// 
		static public int Compare(string x, int startX, int endX, string y, int startY, int endY)
		{
			SemVer verX, verY;
			bool validX = TryParse(x, startX, endX, out verX);
			bool validY = TryParse(y, startY, endY, out verY);
	
			// One invalid
			if (validX != validY)
			{
				// Valid version goes after invalid. That matches the default behavior of sorting List<string> with null items. 
				return validX ? 1 : -1;
			}
	
			// Both invalid
			if (validX == false)
			{
				return 0;
			}
	
			return Compare(verX, verY);
		}
	
		// Compare substrings of x and y as SemVer values
		static public int Compare(SemVer verX, SemVer verY)
		{
			string x = verX.input;
			string y = verY.input;
			
			int result = CompareNumber(x, verX.Major.start, verX.Major.end, y, verY.Major.start, verY.Major.end);
			if (result != 0) { return result; }
	
			result = CompareNumber(x, verX.Minor.start, verX.Minor.end, y, verY.Minor.start, verY.Minor.end);
			if (result != 0) { return result; }
	
			result = CompareNumber(x, verX.Patch.start, verX.Patch.end, y, verY.Patch.start, verY.Patch.end);
			if (result != 0) { return result; }
	
			result = CompareLabel(x, verX.Label.start, verX.Label.end, y, verY.Label.start, verY.Label.end);
			if (result != 0) { return result; }
	
			// No Build comparison here because SemVer numbers with different Build values considered equal
	
			return 0;
		}
		
		// 
		public void Append(string input, StringBuilder dst)
		{
			Major.Append(input, dst);
			dst.Append('.');
			Minor.Append(input, dst);
			dst.Append('.');
			Patch.Append(input, dst);
			if (Label.Length > 0)
			{
				dst.Append('-');
				Label.Append(input, dst);
			}
			if (Build.Length > 0)
			{
				dst.Append('+');
				Build.Append(input, dst);
			}
		}
	
		// 
		public override string ToString()
		{
			StringBuilder sb = new StringBuilder();
			Append(input, sb);
			return sb.ToString();
		}
		#endregion
	
		#region Private Methods
		// Leading zeroes are invalid.
		static private bool TryParseNumber(string input, int start, int to, out int end)
		{
			end = -1;
			
			bool leadingZero = false;
			int i = start;
			for (; i < to; i++)
			{
				char ch = input[i];
	
				if (ch >= '0' && ch <= '9')
				{
					// First char
					if (i == start)
					{
						// Leading zero
						if (ch == '0')
						{
							leadingZero = true;
						}
					}
					// Any digit after leading zero - invalid
					else if (leadingZero)
					{
						return false;
					}
				}
				// Not a digit
				else
				{
					end = i;
					break;
				}
			}
	
			if (end < 0) { end = to; }
			if (i == start) { return false; }
			return true;
		}
	
		// Parse and validate next dot-separated label identifier
		static private bool TryParseLabelIdentifier(string input, int start, int to, out int end, out bool isNumber)
		{
			end = -1;
			isNumber = true;
	
			bool leadingZero = false;
			int i = start;
			for (; i < to; i++)
			{
				char ch = input[i];
	
				// 0-9
				if (ch >= '0' && ch <= '9')
				{
					// First char
					if (i == start)
					{
						// Leading zero
						if (ch == '0')
						{
							leadingZero = true;
						}
					}
				}
				// A-Z a-z -
				else if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || ch == '-')
				{
					// alphanumeric identifier
					isNumber = false;
				}
				// Any other char
				else
				{
					end = i;
					break;
				}
			}
	
			// Numeric identifiers MUST NOT include leading zeroes.
			if (isNumber && leadingZero && i - start > 1) { return false; }
	
			if (end < 0) { end = to; }
			if (i == start) { return false; }    // Identifiers MUST NOT be empty. 
			return true;
		}
	
		// Parse and validate next dot-separated build identifier. Leading zeroes allowed.
		static private bool TryParseBuildIdentifier(string input, int start, int to, out int end)
		{
			end = -1;
	
			int i = start;
			for (; i < to; i++)
			{
				char ch = input[i];
				
				// 0-9 A-Z a-z -
				if ((ch >= '0' && ch <= '9') || (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || ch == '-')
				{
					// Valid char
				}
				// Any other char
				else
				{
					end = i;
					break;
				}
			}
	
			if (end < 0) { end = to; }
			if (i == start) { return false; }    // Identifiers MUST NOT be empty. 
			return true;
		}
	
		// 
		static private int CompareNumber(string x, int startX, int endX, string y, int startY, int endY)
		{
			int lengthX = endX - startX;
			int lengthY = endY - startY;
	
			// We know that there is no leading 0's, since we've already performed number validation, so we can do this:
			if (lengthX != lengthY)
			{
				return lengthX > lengthY ? 1 : -1;
			}
			
			// Lengths equal. First bigger digit wins
			for (int i = 0; i < lengthX; i++)
			{
				char digitX = x[startX + i];
				char digitY = y[startY + i];
				if (digitX != digitY)
				{
					return digitX > digitY ? 1 : -1;
				}
			}
			
			// All digits are equal
			return 0;
		}
	
		// 
		static private int CompareWord(string x, int startX, int endX, string y, int startY, int endY)
		{
			int lengthX = endX - startX;
			int lengthY = endY - startY;
	
			int result;
			for (int i = 0; i < Math.Min(lengthX, lengthY); i++)
			{
				result = x[startX + i].CompareTo(y[startY + i]);
				if (result != 0)
				{
					return result;
				}
			}
	
			return lengthX.CompareTo(lengthY);
		}
		
		// 
		static private int CompareLabel(string x, int startX, int endX, string y, int startY, int endY)
		{
			bool preX = endX > startX;
			bool preY = endY > startY;
	
			// Only one version has prerelease label
			if (preX != preY)
			{
				// Pre-release version has lower precedence than a normal version. 
				return preX ? -1 : 1;
			}
			
			// Neither version has prerelease label
			if (!preX)
			{
				return 0;
			}
	
			// Both versions have prerelease label
			bool hasLabelX;
			bool hasLabelY;
	
			int posX = startX;
			int posY = startY;
	
			bool isNumberX;
			bool isNumberY;
	
			while (true)
			{
				int sX = posX;
				int sY = posY;
	
				hasLabelX = TryParseLabelIdentifier(x, posX, endX, out posX, out isNumberX);
				hasLabelY = TryParseLabelIdentifier(y, posY, endY, out posY, out isNumberY);
	
				if (hasLabelX && hasLabelY)
				{
					// Identifiers consisting of only digits are compared numerically.
					if (isNumberX && isNumberY)
					{
						int result = CompareNumber(x, sX, posX, y, sY, posY);
						if (result != 0)
						{
							return result;
						}
					}
					// Numeric identifiers always have lower precedence than non-numeric identifiers.
					else if (isNumberX)
					{
						return -1;
					}
					else if (isNumberY)
					{
						return 1;
					}
					else
					{
						// Identifiers with letters or hyphens are compared lexically in ASCII sort order.
						int result = CompareWord(x, sX, posX, y, sY, posY);
						if (result != 0)
						{
							return result;
						}
					}
	
					// Skip separators
					posX++;
					posY++;
				}
				// A larger set of pre-release fields has a higher precedence than a smaller set, if all of the preceding identifiers are equal.
				else if (hasLabelX)
				{
					return 1;
				}
				else if (hasLabelY)
				{
					return -1;
				}
				else
				{
					return 0;
				}
			}
		}
	
		// Bounds are inclusive
		static private int Clamp(int value, int min, int max)
		{
			if (value < min)
			{
				value = min;
			}
			else if (value > max)
			{
				value = max;
			}
			return value;
		}
		#endregion
	}
}