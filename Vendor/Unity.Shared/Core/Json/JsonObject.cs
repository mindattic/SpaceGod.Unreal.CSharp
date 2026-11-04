using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace Unity.Shared.Core.Json
{
    /// <summary>
    /// A JSON object that keeps its keys in the order they were read or first set, so a settings file
    /// written back looks like the one that was read, and fields this code knows nothing about survive.
    /// Values are <see cref="string"/>, <see cref="bool"/>, <see cref="JsonNumber"/>, <see cref="JsonObject"/>,
    /// <see cref="JsonArray"/> or null (JSON null).
    /// </summary>
    public sealed class JsonObject : IEnumerable<KeyValuePair<string, object>>
    {
        readonly List<string> keys = new List<string>();
        readonly Dictionary<string, object> values = new Dictionary<string, object>(StringComparer.Ordinal);

        /// <summary>How many fields the object has.</summary>
        public int Count => keys.Count;

        /// <summary>The field names, in file order.</summary>
        public IReadOnlyList<string> Keys => keys;

        /// <summary>Whether the object has a field called <paramref name="key"/> (even one holding null).</summary>
        public bool ContainsKey(string key) => values.ContainsKey(key);

        /// <summary>The raw value of <paramref name="key"/>, or null when it is missing.</summary>
        public object this[string key]
        {
            get => values.TryGetValue(key, out var v) ? v : null;
            set => Set(key, value);
        }

        /// <summary>The raw value of <paramref name="key"/>, if there is one.</summary>
        public bool TryGetValue(string key, out object value) => values.TryGetValue(key, out value);

        /// <summary>
        /// Sets <paramref name="key"/>, keeping its place if it is already there. Numbers of any CLR numeric
        /// type are stored as <see cref="JsonNumber"/>; anything else must already be a JSON value.
        /// </summary>
        public JsonObject Set(string key, object value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            value = Normalize(value);
            if (!values.ContainsKey(key)) keys.Add(key);
            values[key] = value;
            return this;
        }

        /// <summary>Removes <paramref name="key"/>; false when it was not there.</summary>
        public bool Remove(string key)
        {
            if (!values.Remove(key)) return false;
            keys.Remove(key);
            return true;
        }

        /// <summary>The string at <paramref name="key"/>, or <paramref name="fallback"/> when it is missing or not a string.</summary>
        public string GetString(string key, string fallback = null) => this[key] is string s ? s : fallback;

        /// <summary>The boolean at <paramref name="key"/>, or <paramref name="fallback"/> when it is missing or not a boolean.</summary>
        public bool GetBool(string key, bool fallback = false) => this[key] is bool b ? b : fallback;

        /// <summary>The number at <paramref name="key"/>, or <paramref name="fallback"/> when it is missing, not a number or not finite.</summary>
        public double GetDouble(string key, double fallback = 0)
        {
            if (!(this[key] is JsonNumber n)) return fallback;
            double d = n.Value;
            return double.IsNaN(d) || double.IsInfinity(d) ? fallback : d;
        }

        /// <summary>The number at <paramref name="key"/> as a float, or <paramref name="fallback"/>.</summary>
        public float GetFloat(string key, float fallback = 0) => (float)GetDouble(key, fallback);

        /// <summary>The whole number at <paramref name="key"/>, or <paramref name="fallback"/> when it is missing or not a whole number in range.</summary>
        public long GetLong(string key, long fallback = 0) =>
            this[key] is JsonNumber n && long.TryParse(n.Raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long l) ? l : fallback;

        /// <summary>The whole number at <paramref name="key"/> as an int, or <paramref name="fallback"/>.</summary>
        public int GetInt(string key, int fallback = 0)
        {
            long l = GetLong(key, fallback);
            return l < int.MinValue || l > int.MaxValue ? fallback : (int)l;
        }

        /// <summary>The object at <paramref name="key"/>, or null when it is missing or not an object.</summary>
        public JsonObject GetObject(string key) => this[key] as JsonObject;

        /// <summary>The object at <paramref name="key"/>, put there (replacing a non-object) when it is missing.</summary>
        public JsonObject GetOrAddObject(string key)
        {
            if (this[key] is JsonObject o) return o;
            o = new JsonObject();
            Set(key, o);
            return o;
        }

        /// <summary>The array at <paramref name="key"/>, or null when it is missing or not an array.</summary>
        public JsonArray GetArray(string key) => this[key] as JsonArray;

        /// <summary>A deep copy: nothing in it is shared with this object.</summary>
        public JsonObject Clone()
        {
            var copy = new JsonObject();
            foreach (var key in keys) copy.Set(key, CloneValue(values[key]));
            return copy;
        }

        /// <summary>This object as indented JSON text, written by <see cref="MiniJson"/>.</summary>
        public string ToJson() => MiniJson.Write(this);

        /// <summary>Reads JSON text whose top level is an object, with <see cref="MiniJson"/>. Throws <see cref="JsonFormatException"/> on bad text.</summary>
        public static JsonObject Parse(string json) => MiniJson.ParseObject(json);

        /// <inheritdoc />
        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            foreach (var key in keys) yield return new KeyValuePair<string, object>(key, values[key]);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <inheritdoc />
        public override string ToString() => ToJson();

        internal static object CloneValue(object v) =>
            v is JsonObject o ? o.Clone() : v is JsonArray a ? (object)a.Clone() : v;

        internal static object Normalize(object value)
        {
            switch (value)
            {
                case null:
                case string _:
                case bool _:
                case JsonNumber _:
                case JsonObject _:
                case JsonArray _:
                    return value;
                case int i: return JsonNumber.From(i);
                case long l: return JsonNumber.From(l);
                case short s: return JsonNumber.From(s);
                case byte b: return JsonNumber.From(b);
                case uint ui: return JsonNumber.From(ui);
                case sbyte sb: return JsonNumber.From(sb);
                case ushort us: return JsonNumber.From(us);
                case ulong ul: return JsonNumber.FromRaw(ul.ToString(CultureInfo.InvariantCulture));
                case float f: return JsonNumber.From(f);
                case double d: return JsonNumber.From(d);
                case decimal m: return JsonNumber.From(m);
                default:
                    throw new ArgumentException("Not a JSON value: " + value.GetType().Name, nameof(value));
            }
        }
    }

    /// <summary>A JSON array; its items are JSON values as in <see cref="JsonObject"/>.</summary>
    public sealed class JsonArray : List<object>
    {
        /// <summary>An empty array.</summary>
        public JsonArray() { }

        /// <summary>An array of <paramref name="items"/>, each normalised as by <see cref="JsonObject.Set"/>.</summary>
        public JsonArray(IEnumerable<object> items)
        {
            foreach (var item in items) Add(JsonObject.Normalize(item));
        }

        /// <summary>A deep copy.</summary>
        public JsonArray Clone()
        {
            var copy = new JsonArray();
            foreach (var item in this) copy.Add(JsonObject.CloneValue(item));
            return copy;
        }
    }

    /// <summary>
    /// A JSON number, kept as the exact text it was read as so it is written back unchanged
    /// (no 0.6 turning into 0.6000000238 and no large integer losing digits).
    /// </summary>
    public sealed class JsonNumber : IEquatable<JsonNumber>
    {
        /// <summary>The number's JSON text.</summary>
        public readonly string Raw;

        /// <summary>The number as a double (an out-of-range number reads as an infinity).</summary>
        public double Value =>
            // Unity's Mono/IL2CPP class library (.NET Framework rules) throws OverflowException for an
            // out-of-range number where .NET Core returns an infinity; Raw is always a valid JSON number,
            // so a failed parse can only be an overflow.
            double.TryParse(Raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d
                : Raw.StartsWith("-", StringComparison.Ordinal) ? double.NegativeInfinity : double.PositiveInfinity;

        JsonNumber(string raw) => Raw = raw;

        /// <summary>A number from its JSON text; throws <see cref="FormatException"/> when the text is not a JSON number.</summary>
        public static JsonNumber FromRaw(string raw)
        {
            if (!MiniJson.IsNumber(raw)) throw new FormatException("Not a JSON number: " + raw);
            return new JsonNumber(raw);
        }

        /// <summary>A whole number.</summary>
        public static JsonNumber From(long value) => new JsonNumber(value.ToString(CultureInfo.InvariantCulture));

        /// <summary>A float, written in its shortest round-trip form. NaN and infinities become 0.</summary>
        public static JsonNumber From(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) ? new JsonNumber("0") : new JsonNumber(value.ToString("R", CultureInfo.InvariantCulture));

        /// <summary>A double, written in its shortest round-trip form. NaN and infinities become 0.</summary>
        public static JsonNumber From(double value) =>
            double.IsNaN(value) || double.IsInfinity(value) ? new JsonNumber("0") : new JsonNumber(value.ToString("R", CultureInfo.InvariantCulture));

        /// <summary>A decimal.</summary>
        public static JsonNumber From(decimal value) => new JsonNumber(value.ToString(CultureInfo.InvariantCulture));

        /// <inheritdoc />
        public bool Equals(JsonNumber other) => !(other is null) && other.Raw == Raw;

        /// <inheritdoc />
        public override bool Equals(object obj) => Equals(obj as JsonNumber);

        /// <inheritdoc />
        public override int GetHashCode() => Raw.GetHashCode();

        /// <inheritdoc />
        public override string ToString() => Raw;
    }
}
