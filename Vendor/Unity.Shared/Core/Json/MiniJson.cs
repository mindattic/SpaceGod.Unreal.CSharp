using System;
using System.Globalization;
using System.Text;

namespace Unity.Shared.Core.Json
{
    /// <summary>
    /// Reads and writes JSON text for the settings format: settings files must be readable in plain dotnet
    /// tests and in Unity, so this package carries its own small reader/writer instead of a second copy of
    /// Newtonsoft.Json (games already ship one) or System.Text.Json (not in Unity's .NET Standard 2.1 profile).
    /// It is strict RFC 8259 JSON, keeps key order and number text, and writes two-space indented LF text.
    /// </summary>
    public static class MiniJson
    {
        /// <summary>The deepest nesting the reader accepts, so a hostile file cannot overflow the stack.</summary>
        public const int MaxDepth = 128;

        /// <summary>Reads <paramref name="json"/>, whose top level must be an object. Throws <see cref="JsonFormatException"/> on anything else.</summary>
        public static JsonObject ParseObject(string json)
        {
            if (Parse(json) is JsonObject o) return o;
            throw new JsonFormatException("The top level is not a JSON object.", 0);
        }

        /// <summary>Reads any JSON value. Throws <see cref="JsonFormatException"/> on bad text.</summary>
        public static object Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            var reader = new Reader(json);
            reader.SkipBom();
            reader.SkipWhitespace();
            var value = reader.ReadValue(0);
            reader.SkipWhitespace();
            if (!reader.AtEnd) throw reader.Error("Unexpected text after the JSON value");
            return value;
        }

        /// <summary>Writes <paramref name="value"/> (any JSON value) as indented JSON text ending in a newline.</summary>
        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, JsonObject.Normalize(value), 0);
            sb.Append('\n');
            return sb.ToString();
        }

        /// <summary>Whether <paramref name="text"/> is exactly one JSON number.</summary>
        public static bool IsNumber(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            int i = 0, n = text.Length;
            if (text[i] == '-') i++;
            if (i >= n) return false;
            if (text[i] == '0') i++;
            else if (text[i] >= '1' && text[i] <= '9') { while (i < n && IsDigit(text[i])) i++; }
            else return false;
            if (i < n && text[i] == '.')
            {
                i++;
                int start = i;
                while (i < n && IsDigit(text[i])) i++;
                if (i == start) return false;
            }
            if (i < n && (text[i] == 'e' || text[i] == 'E'))
            {
                i++;
                if (i < n && (text[i] == '+' || text[i] == '-')) i++;
                int start = i;
                while (i < n && IsDigit(text[i])) i++;
                if (i == start) return false;
            }
            return i == n;
        }

        static bool IsDigit(char c) => c >= '0' && c <= '9';

        static void WriteValue(StringBuilder sb, object value, int indent)
        {
            switch (value)
            {
                case null: sb.Append("null"); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case string s: WriteString(sb, s); break;
                case JsonNumber n: sb.Append(n.Raw); break;
                case JsonObject o:
                    if (o.Count == 0) { sb.Append("{}"); break; }
                    sb.Append('{');
                    bool first = true;
                    foreach (var pair in o)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        sb.Append('\n');
                        Indent(sb, indent + 1);
                        WriteString(sb, pair.Key);
                        sb.Append(": ");
                        WriteValue(sb, pair.Value, indent + 1);
                    }
                    sb.Append('\n');
                    Indent(sb, indent);
                    sb.Append('}');
                    break;
                case JsonArray a:
                    if (a.Count == 0) { sb.Append("[]"); break; }
                    sb.Append('[');
                    for (int i = 0; i < a.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append('\n');
                        Indent(sb, indent + 1);
                        WriteValue(sb, JsonObject.Normalize(a[i]), indent + 1);
                    }
                    sb.Append('\n');
                    Indent(sb, indent);
                    sb.Append(']');
                    break;
                default:
                    throw new ArgumentException("Not a JSON value: " + value.GetType().Name, nameof(value));
            }
        }

        static void Indent(StringBuilder sb, int level) => sb.Append(' ', level * 2);

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        sealed class Reader
        {
            readonly string text;
            int pos;

            public Reader(string text) => this.text = text;

            public bool AtEnd => pos >= text.Length;

            public JsonFormatException Error(string message) => new JsonFormatException(message + " at character " + pos + ".", pos);

            public void SkipBom()
            {
                if (pos < text.Length && text[pos] == '﻿') pos++;
            }

            public void SkipWhitespace()
            {
                while (pos < text.Length)
                {
                    char c = text[pos];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') pos++;
                    else break;
                }
            }

            public object ReadValue(int depth)
            {
                if (depth > MaxDepth) throw Error("JSON nested deeper than " + MaxDepth);
                if (AtEnd) throw Error("Unexpected end of JSON");
                char c = text[pos];
                switch (c)
                {
                    case '{': return ReadObject(depth);
                    case '[': return ReadArray(depth);
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || IsDigit(c)) return ReadNumber();
                        throw Error("Unexpected character '" + c + "'");
                }
            }

            void Expect(string word)
            {
                if (string.CompareOrdinal(text, pos, word, 0, word.Length) != 0) throw Error("Expected " + word);
                pos += word.Length;
            }

            JsonObject ReadObject(int depth)
            {
                var obj = new JsonObject();
                pos++;
                SkipWhitespace();
                if (!AtEnd && text[pos] == '}') { pos++; return obj; }
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || text[pos] != '"') throw Error("Expected a field name");
                    string key = ReadString();
                    SkipWhitespace();
                    if (AtEnd || text[pos] != ':') throw Error("Expected ':'");
                    pos++;
                    SkipWhitespace();
                    obj.Set(key, ReadValue(depth + 1));
                    SkipWhitespace();
                    if (AtEnd) throw Error("Unexpected end of JSON in an object");
                    if (text[pos] == ',') { pos++; continue; }
                    if (text[pos] == '}') { pos++; return obj; }
                    throw Error("Expected ',' or '}'");
                }
            }

            JsonArray ReadArray(int depth)
            {
                var arr = new JsonArray();
                pos++;
                SkipWhitespace();
                if (!AtEnd && text[pos] == ']') { pos++; return arr; }
                while (true)
                {
                    SkipWhitespace();
                    arr.Add(ReadValue(depth + 1));
                    SkipWhitespace();
                    if (AtEnd) throw Error("Unexpected end of JSON in an array");
                    if (text[pos] == ',') { pos++; continue; }
                    if (text[pos] == ']') { pos++; return arr; }
                    throw Error("Expected ',' or ']'");
                }
            }

            string ReadString()
            {
                pos++; // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw Error("Unterminated string");
                    char c = text[pos++];
                    if (c == '"') return sb.ToString();
                    if (c < 0x20) throw Error("Control character in a string");
                    if (c != '\\') { sb.Append(c); continue; }
                    if (AtEnd) throw Error("Unterminated escape");
                    char e = text[pos++];
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
                            if (pos + 4 > text.Length) throw Error("Short \\u escape");
                            if (!int.TryParse(text.Substring(pos, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code))
                                throw Error("Bad \\u escape");
                            pos += 4;
                            sb.Append((char)code);
                            break;
                        default: throw Error("Bad escape '\\" + e + "'");
                    }
                }
            }

            JsonNumber ReadNumber()
            {
                int start = pos;
                while (pos < text.Length)
                {
                    char c = text[pos];
                    if (IsDigit(c) || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') pos++;
                    else break;
                }
                string raw = text.Substring(start, pos - start);
                if (!IsNumber(raw)) { pos = start; throw Error("Bad number '" + raw + "'"); }
                return JsonNumber.FromRaw(raw);
            }
        }
    }

    /// <summary>JSON text that could not be read.</summary>
    public sealed class JsonFormatException : FormatException
    {
        /// <summary>The character offset the reader stopped at.</summary>
        public readonly int Position;

        /// <summary>A read failure at <paramref name="position"/>.</summary>
        public JsonFormatException(string message, int position) : base(message) => Position = position;
    }

    /// <summary>
    /// Turns settings text into a <see cref="JsonObject"/> and back. The package defaults to
    /// <see cref="MiniJsonCodec"/>; a game that would rather use its own JSON library (Newtonsoft, say)
    /// implements this by converting to and from <see cref="JsonObject"/>, and passes it to
    /// <see cref="ProfileStore"/>. A codec signals unreadable text by throwing any exception.
    /// </summary>
    public interface IJsonCodec
    {
        /// <summary>Reads text whose top level is a JSON object.</summary>
        JsonObject Parse(string text);

        /// <summary>Writes the object as JSON text.</summary>
        string Write(JsonObject value);
    }

    /// <summary>The default codec: <see cref="MiniJson"/>, no dependencies, works in dotnet and in Unity.</summary>
    public sealed class MiniJsonCodec : IJsonCodec
    {
        /// <summary>The one shared instance (it has no state).</summary>
        public static readonly MiniJsonCodec Instance = new MiniJsonCodec();

        /// <inheritdoc />
        public JsonObject Parse(string text) => MiniJson.ParseObject(text);

        /// <inheritdoc />
        public string Write(JsonObject value) => MiniJson.Write(value);
    }
}
