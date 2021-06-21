using System.Collections.Generic;
using System.Text;

namespace DeepDreamGames
{
	static public class Extensions
	{
		// 
		static public TValue GetOrDefault<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue defaultValue)
		{
			TValue value;
			if (dictionary.TryGetValue(key, out value))
			{
				return value;
			}
			return defaultValue;
		}
		
		// 
		static public void AppendString(this StringBuilder sb, string value)
		{
			if (value != null)
			{
				sb.Append('"').Append(value).Append('"');
			}
			else
			{
				sb.Append("null");
			}
		}
	}
}
