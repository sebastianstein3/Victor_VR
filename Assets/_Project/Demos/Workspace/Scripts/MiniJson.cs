using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CardboardXR.Demos.Workspace
{
    // A small JSON reader and writer for what Unity's JsonUtility can't do: documents of any shape (a tool's
    // input and schema, an MCP server's answers). Objects are Dictionary<string, object>, arrays List<object>,
    // numbers double, plus string, bool and null. Parse returns null for text that isn't JSON.
    public static class MiniJson
    {
        public static object Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
                return null;
            try
            {
                var parser = new Parser(json);
                var value = parser.ReadValue();
                parser.SkipSpace();
                return parser.AtEnd ? value : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static string Serialize(object value)
        {
            var text = new StringBuilder();
            Write(text, value);
            return text.ToString();
        }

        // ---- Reading helpers (all tolerate a missing key or another type) ----

        public static Dictionary<string, object> AsObject(object value) => value as Dictionary<string, object>;
        public static List<object> AsList(object value) => value as List<object>;

        public static object Get(object obj, string key) =>
            obj is Dictionary<string, object> dictionary && dictionary.TryGetValue(key, out var value) ? value : null;

        public static string GetString(object obj, string key) => Get(obj, key) as string;

        public static bool GetBool(object obj, string key, bool fallback = false) => Get(obj, key) is bool b ? b : fallback;

        public static double GetNumber(object obj, string key, double fallback = 0.0) => Get(obj, key) is double d ? d : fallback;

        // ---- Writing ----

        public static void Write(StringBuilder text, object value)
        {
            switch (value)
            {
                case null:
                    text.Append("null");
                    break;
                case string s:
                    WriteString(text, s);
                    break;
                case bool b:
                    text.Append(b ? "true" : "false");
                    break;
                case double d:
                    text.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case float f:
                    text.Append(f.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case int i:
                    text.Append(i.ToString(CultureInfo.InvariantCulture));
                    break;
                case long l:
                    text.Append(l.ToString(CultureInfo.InvariantCulture));
                    break;
                case Dictionary<string, object> dictionary:
                    text.Append('{');
                    bool first = true;
                    foreach (var pair in dictionary)
                    {
                        if (!first)
                            text.Append(',');
                        first = false;
                        WriteString(text, pair.Key);
                        text.Append(':');
                        Write(text, pair.Value);
                    }
                    text.Append('}');
                    break;
                case IList list:
                    text.Append('[');
                    for (int index = 0; index < list.Count; index++)
                    {
                        if (index > 0)
                            text.Append(',');
                        Write(text, list[index]);
                    }
                    text.Append(']');
                    break;
                default:
                    WriteString(text, value.ToString());
                    break;
            }
        }

        public static void WriteString(StringBuilder text, string value)
        {
            text.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            text.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            text.Append(c);
                        break;
                }
            }
            text.Append('"');
        }

        public static string Quote(string value)
        {
            var text = new StringBuilder();
            WriteString(text, value ?? "");
            return text.ToString();
        }

        // The raw text of a key's value in the top-level object, without parsing it (an array or object here):
        // an assistant turn must be sent back exactly as it came, thinking blocks and signatures included
        public static string RawTopLevel(string json, string key)
        {
            int depth = 0;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"')
                {
                    int end = SkipString(json, i);
                    if (end < 0)
                        return null;
                    if (depth == 1 && string.CompareOrdinal(json, i + 1, key, 0, key.Length) == 0 && end - i - 1 == key.Length)
                    {
                        int colon = end + 1;
                        while (colon < json.Length && char.IsWhiteSpace(json[colon])) colon++;
                        if (colon < json.Length && json[colon] == ':')
                        {
                            int start = colon + 1;
                            while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
                            int stop = SkipValue(json, start);
                            return stop > start ? json.Substring(start, stop - start) : null;
                        }
                    }
                    i = end;
                }
                else if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']') depth--;
            }
            return null;
        }

        static int SkipString(string json, int start)
        {
            for (int i = start + 1; i < json.Length; i++)
            {
                if (json[i] == '\\') i++;
                else if (json[i] == '"') return i;
            }
            return -1;
        }

        static int SkipValue(string json, int start)
        {
            if (start >= json.Length || (json[start] != '[' && json[start] != '{'))
                return -1;
            int depth = 0;
            for (int i = start; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '"')
                {
                    i = SkipString(json, i);
                    if (i < 0)
                        return -1;
                }
                else if (c == '{' || c == '[') depth++;
                else if ((c == '}' || c == ']') && --depth == 0)
                    return i + 1;
            }
            return -1;
        }

        // ---- The parser ----

        class Parser
        {
            readonly string m_Text;
            int m_Index;

            public Parser(string text) => m_Text = text;

            public bool AtEnd => m_Index >= m_Text.Length;

            public void SkipSpace()
            {
                while (m_Index < m_Text.Length && char.IsWhiteSpace(m_Text[m_Index]))
                    m_Index++;
            }

            public object ReadValue()
            {
                SkipSpace();
                if (AtEnd)
                    throw new FormatException();
                char c = m_Text[m_Index];
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default: return ReadNumber();
                }
            }

            void Expect(string word)
            {
                if (string.CompareOrdinal(m_Text, m_Index, word, 0, word.Length) != 0)
                    throw new FormatException();
                m_Index += word.Length;
            }

            Dictionary<string, object> ReadObject()
            {
                var result = new Dictionary<string, object>();
                m_Index++; // {
                SkipSpace();
                if (Peek() == '}')
                {
                    m_Index++;
                    return result;
                }
                while (true)
                {
                    SkipSpace();
                    string key = ReadString();
                    SkipSpace();
                    if (Next() != ':')
                        throw new FormatException();
                    result[key] = ReadValue();
                    SkipSpace();
                    char separator = Next();
                    if (separator == '}')
                        return result;
                    if (separator != ',')
                        throw new FormatException();
                }
            }

            List<object> ReadArray()
            {
                var result = new List<object>();
                m_Index++; // [
                SkipSpace();
                if (Peek() == ']')
                {
                    m_Index++;
                    return result;
                }
                while (true)
                {
                    result.Add(ReadValue());
                    SkipSpace();
                    char separator = Next();
                    if (separator == ']')
                        return result;
                    if (separator != ',')
                        throw new FormatException();
                }
            }

            string ReadString()
            {
                if (Next() != '"')
                    throw new FormatException();
                var text = new StringBuilder();
                while (true)
                {
                    char c = Next();
                    if (c == '"')
                        return text.ToString();
                    if (c != '\\')
                    {
                        text.Append(c);
                        continue;
                    }
                    char escape = Next();
                    switch (escape)
                    {
                        case '"': text.Append('"'); break;
                        case '\\': text.Append('\\'); break;
                        case '/': text.Append('/'); break;
                        case 'b': text.Append('\b'); break;
                        case 'f': text.Append('\f'); break;
                        case 'n': text.Append('\n'); break;
                        case 'r': text.Append('\r'); break;
                        case 't': text.Append('\t'); break;
                        case 'u':
                            text.Append((char)Convert.ToInt32(m_Text.Substring(m_Index, 4), 16));
                            m_Index += 4;
                            break;
                        default: throw new FormatException();
                    }
                }
            }

            double ReadNumber()
            {
                int start = m_Index;
                while (m_Index < m_Text.Length && "+-0123456789.eE".IndexOf(m_Text[m_Index]) >= 0)
                    m_Index++;
                if (m_Index == start)
                    throw new FormatException();
                return double.Parse(m_Text.Substring(start, m_Index - start), CultureInfo.InvariantCulture);
            }

            char Peek() => m_Index < m_Text.Length ? m_Text[m_Index] : '\0';

            char Next()
            {
                if (m_Index >= m_Text.Length)
                    throw new FormatException();
                return m_Text[m_Index++];
            }
        }
    }
}
