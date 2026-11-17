// Copyright (c) 2026 SpatialFabric Contributors. MIT.
// Pure C# (System only). Small strict JSON reader for Path v1 documents: bounded size/depth,
// rejects duplicate keys, non-finite numbers, trailing data and invalid UTF-8.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NPLAudio.SpatialFabric.Paths
{
    public enum JsonKind { Null, Bool, Number, String, Array, Object }

    /// <summary>Parsed JSON value. Numbers remember whether the literal was an integer (no '.', 'e', 'E').</summary>
    public sealed class JsonNode
    {
        public JsonKind Kind;
        public bool Bool;
        public double Number;
        public bool IsInteger;
        public string String;
        public List<JsonNode> Items;
        public Dictionary<string, JsonNode> Members;

        public bool IsNumber => Kind == JsonKind.Number;
        public bool IsString => Kind == JsonKind.String;
        public bool IsArray => Kind == JsonKind.Array;
        public bool IsObject => Kind == JsonKind.Object;

        /// <summary>Member lookup; null when this is not an object or the key is absent (JSON null is a non-null node of kind Null).</summary>
        public JsonNode Get(string key)
        {
            JsonNode value;
            return Kind == JsonKind.Object && Members.TryGetValue(key, out value) ? value : null;
        }
        public bool Has(string key) => Kind == JsonKind.Object && Members.ContainsKey(key);
    }

    public static class PathJson
    {
        public const int MaxBytes = 16 * 1024 * 1024, MaxDepth = 32;
        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary>Parse UTF-8 bytes (optional BOM). Throws FormatException on any problem.</summary>
        public static JsonNode Parse(byte[] utf8)
        {
            if (utf8 == null) throw new FormatException("JSON input is null.");
            if (utf8.Length > MaxBytes) throw new FormatException("JSON input exceeds " + MaxBytes + " bytes.");
            int start = utf8.Length >= 3 && utf8[0] == 0xEF && utf8[1] == 0xBB && utf8[2] == 0xBF ? 3 : 0;
            string text;
            try { text = StrictUtf8.GetString(utf8, start, utf8.Length - start); }
            catch (ArgumentException e) { throw new FormatException("JSON input is not valid UTF-8: " + e.Message); }
            return new Reader(text).ParseDocument();
        }

        /// <summary>Parse text. The UTF-8 encoded size must not exceed 16 MiB. Throws FormatException on any problem.</summary>
        public static JsonNode Parse(string text)
        {
            if (text == null) throw new FormatException("JSON input is null.");
            if (text.Length > MaxBytes || (long)text.Length * 3 > MaxBytes && Utf8Count(text) > MaxBytes)
                throw new FormatException("JSON input exceeds " + MaxBytes + " bytes.");
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);
            return new Reader(text).ParseDocument();
        }

        static long Utf8Count(string text)
        {
            try { return StrictUtf8.GetByteCount(text); }
            catch (ArgumentException) { return long.MaxValue; } // lone surrogate: not representable, refuse
        }

        static bool Digit(char c) => c >= '0' && c <= '9';

        sealed class Reader
        {
            readonly string s;
            int i;
            public Reader(string text) { s = text; }

            FormatException Fail(string message) => new FormatException("JSON error at offset " + i + ": " + message);

            void SkipSpace() { while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) ++i; }

            public JsonNode ParseDocument()
            {
                var value = ParseValue(0);
                SkipSpace();
                if (i != s.Length) throw Fail("unexpected trailing data");
                return value;
            }

            JsonNode ParseValue(int depth)
            {
                SkipSpace();
                if (i >= s.Length) throw Fail("unexpected end of input");
                char c = s[i];
                if (c == '{') return ParseObject(depth + 1);
                if (c == '[') return ParseArray(depth + 1);
                if (c == '"') return new JsonNode { Kind = JsonKind.String, String = ParseString() };
                if (c == '-' || Digit(c)) return ParseNumber();
                if (Literal("true")) return new JsonNode { Kind = JsonKind.Bool, Bool = true };
                if (Literal("false")) return new JsonNode { Kind = JsonKind.Bool };
                if (Literal("null")) return new JsonNode { Kind = JsonKind.Null };
                throw Fail("unexpected character '" + c + "'");
            }

            bool Literal(string word)
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) return false;
                i += word.Length;
                return true;
            }

            JsonNode ParseObject(int depth)
            {
                if (depth > MaxDepth) throw Fail("nesting deeper than " + MaxDepth);
                var node = new JsonNode { Kind = JsonKind.Object, Members = new Dictionary<string, JsonNode>(StringComparer.Ordinal) };
                ++i; SkipSpace();
                if (i < s.Length && s[i] == '}') { ++i; return node; }
                while (true)
                {
                    SkipSpace();
                    if (i >= s.Length || s[i] != '"') throw Fail("expected object key");
                    string key = ParseString();
                    SkipSpace();
                    if (i >= s.Length || s[i] != ':') throw Fail("expected ':'");
                    ++i;
                    var value = ParseValue(depth);
                    if (node.Members.ContainsKey(key)) throw Fail("duplicate key \"" + key + "\"");
                    node.Members.Add(key, value);
                    SkipSpace();
                    if (i < s.Length && s[i] == ',') { ++i; continue; }
                    if (i < s.Length && s[i] == '}') { ++i; return node; }
                    throw Fail("expected ',' or '}'");
                }
            }

            JsonNode ParseArray(int depth)
            {
                if (depth > MaxDepth) throw Fail("nesting deeper than " + MaxDepth);
                var node = new JsonNode { Kind = JsonKind.Array, Items = new List<JsonNode>() };
                ++i; SkipSpace();
                if (i < s.Length && s[i] == ']') { ++i; return node; }
                while (true)
                {
                    node.Items.Add(ParseValue(depth));
                    SkipSpace();
                    if (i < s.Length && s[i] == ',') { ++i; continue; }
                    if (i < s.Length && s[i] == ']') { ++i; return node; }
                    throw Fail("expected ',' or ']'");
                }
            }

            JsonNode ParseNumber()
            {
                int start = i;
                if (s[i] == '-') ++i;
                if (i >= s.Length) throw Fail("incomplete number");
                if (s[i] == '0') ++i;
                else if (s[i] >= '1' && s[i] <= '9') { while (i < s.Length && Digit(s[i])) ++i; }
                else throw Fail("invalid number");
                bool integer = true;
                if (i < s.Length && s[i] == '.')
                {
                    integer = false; ++i;
                    int digits = i;
                    while (i < s.Length && Digit(s[i])) ++i;
                    if (i == digits) throw Fail("digits required after '.'");
                }
                if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
                {
                    integer = false; ++i;
                    if (i < s.Length && (s[i] == '+' || s[i] == '-')) ++i;
                    int digits = i;
                    while (i < s.Length && Digit(s[i])) ++i;
                    if (i == digits) throw Fail("digits required in exponent");
                }
                double value;
                try { value = double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture); }
                catch (OverflowException) { throw Fail("number out of range"); }
                if (double.IsNaN(value) || double.IsInfinity(value)) throw Fail("number is not finite");
                return new JsonNode { Kind = JsonKind.Number, Number = value, IsInteger = integer };
            }

            string ParseString()
            {
                ++i; // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (i >= s.Length) throw Fail("unterminated string");
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c < 0x20) throw Fail("control character in string");
                    if (char.IsHighSurrogate(c))
                    {
                        if (i >= s.Length || !char.IsLowSurrogate(s[i])) throw Fail("invalid surrogate");
                        sb.Append(c).Append(s[i++]);
                        continue;
                    }
                    if (char.IsLowSurrogate(c)) throw Fail("invalid surrogate");
                    if (c != '\\') { sb.Append(c); continue; }
                    if (i >= s.Length) throw Fail("unterminated escape");
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            char u = Hex4();
                            if (char.IsHighSurrogate(u))
                            {
                                if (i + 1 >= s.Length || s[i] != '\\' || s[i + 1] != 'u') throw Fail("invalid surrogate escape");
                                i += 2;
                                char lo = Hex4();
                                if (!char.IsLowSurrogate(lo)) throw Fail("invalid surrogate escape");
                                sb.Append(u).Append(lo);
                            }
                            else if (char.IsLowSurrogate(u)) throw Fail("invalid surrogate escape");
                            else sb.Append(u);
                            break;
                        default: throw Fail("invalid escape '\\" + e + "'");
                    }
                }
            }

            char Hex4()
            {
                if (i + 4 > s.Length) throw Fail("truncated \\u escape");
                int v = 0;
                for (int k = 0; k < 4; ++k)
                {
                    char h = s[i++];
                    int d = h >= '0' && h <= '9' ? h - '0' : h >= 'a' && h <= 'f' ? h - 'a' + 10 : h >= 'A' && h <= 'F' ? h - 'A' + 10 : -1;
                    if (d < 0) throw Fail("invalid hex digit in \\u escape");
                    v = v * 16 + d;
                }
                return (char)v;
            }
        }
    }
}
