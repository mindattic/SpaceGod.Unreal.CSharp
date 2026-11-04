using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;

namespace Unity.SpaceGod.Core
{
    /// <summary>
    /// Writes a whole object graph (every field of every object reachable from a root, private ones
    /// included) to a stream and reads it back as an exact copy: shared references stay shared, cycles
    /// close, random number generators carry on where they were, and the delegates the simulation wires
    /// between its parts (a battle's callbacks into its war, the economy asking the fleet for a trip)
    /// point at the copies. It is what lets the simulation be saved without a hand-written save routine
    /// for each of its hundred-odd classes, and keeps a save complete as they grow.
    ///
    /// The format is self-describing: each type's fields are listed by name, so a save still reads after
    /// a field is removed (its value is skipped); one made before a field was added is refused. It is not a long-term
    /// archive format: compiler-generated closures are named per build, so a save made by one build may
    /// not read in another whose lambdas moved.
    ///
    /// Objects held in the simulation's static tables (ship designs, weapon specs, module catalogues)
    /// are written by name, not copied, so a loaded hull still shares its design with every other hull
    /// of its class. Delegates are kept only where they point into this assembly (or the root's):
    /// subscribers from outside (the Unity views' toasts) subscribe to the loaded world again themselves.
    ///
    /// Writing compacts every dictionary and set it passes (each is cleared and refilled in its own
    /// order), so the original and the copy hold their entries the same way and keep enumerating in
    /// step after later removals: the simulation iterates them, and a save must not fork the timeline.
    /// </summary>
    public static class ObjectGraph
    {
        const int Magic = 0x4A42474F; // "OGBJ"
        const int Version = 1;

        public static void Write(Stream stream, object root)
        {
            var w = new GraphWriter(root?.GetType().Assembly);
            int rootId = w.WriteAll(root);
            var bw = new BinaryWriter(stream);
            bw.Write(Magic);
            bw.Write(Version);
            w.WriteTypes(bw);
            bw.Write(w.EntryCount);
            bw.Flush();
            w.Table.WriteTo(stream);
            bw.Write(w.PayloadCount);
            bw.Flush();
            w.Payload.WriteTo(stream);
            bw.Write(rootId);
            bw.Flush();
        }

        public static object Read(Stream stream)
        {
            var br = new BinaryReader(stream);
            if (br.ReadInt32() != Magic) throw new InvalidDataException("Not an object graph");
            int version = br.ReadInt32();
            if (version != Version) throw new InvalidDataException("Unsupported object graph version " + version);
            return new GraphReader(br).ReadAll();
        }

        public static byte[] ToBytes(object root)
        {
            using (var ms = new MemoryStream())
            {
                Write(ms, root);
                return ms.ToArray();
            }
        }

        public static object FromBytes(byte[] data)
        {
            using (var ms = new MemoryStream(data)) return Read(ms);
        }

        /// <summary>A deep copy of <paramref name="root"/> by way of a write and a read.</summary>
        public static T Clone<T>(T root) where T : class => (T)FromBytes(ToBytes(root));

        // ---------------------------------------------------------------- shared type model

        enum Kind : byte { Primitive, Enum, Nullable, DateTime, TimeSpan, Decimal, Struct, String, Array, List, Dictionary, HashSet, Queue, Stack, Delegate, Object }

        enum Entry : byte { Object, String, Array, Collection, Delegate, External, EmptyArray }

        const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        const BindingFlags AnyMethod = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        static readonly Assembly CoreAssembly = typeof(ObjectGraph).Assembly;

        static Kind KindOf(Type t)
        {
            if (t.IsPointer || t.IsByRef || typeof(MemberInfo).IsAssignableFrom(t) || t == typeof(IntPtr) || t == typeof(UIntPtr))
                throw new NotSupportedException("Cannot save a " + t);
            if (t.IsEnum) return Kind.Enum;
            if (t.IsPrimitive) return Kind.Primitive;
            if (t == typeof(string)) return Kind.String;
            if (t == typeof(DateTime)) return Kind.DateTime;
            if (t == typeof(TimeSpan)) return Kind.TimeSpan;
            if (t == typeof(decimal)) return Kind.Decimal;
            if (t.IsArray) return Kind.Array;
            if (t.IsGenericType)
            {
                var d = t.GetGenericTypeDefinition();
                if (d == typeof(Nullable<>)) return Kind.Nullable;
                if (d == typeof(List<>)) return Kind.List;
                if (d == typeof(Dictionary<,>)) return Kind.Dictionary;
                if (d == typeof(HashSet<>)) return Kind.HashSet;
                if (d == typeof(Queue<>)) return Kind.Queue;
                if (d == typeof(Stack<>)) return Kind.Stack;
            }
            if (typeof(Delegate).IsAssignableFrom(t)) return Kind.Delegate;
            if (t.IsValueType) return Kind.Struct;
            return Kind.Object;
        }

        /// <summary>Every instance field of a type and its bases, in a fixed order (base first, then by name).</summary>
        static FieldInfo[] FieldsOf(Type t)
        {
            var chain = new List<Type>();
            for (var c = t; c != null && c != typeof(object) && c != typeof(ValueType); c = c.BaseType) chain.Add(c);
            chain.Reverse();
            var list = new List<FieldInfo>();
            foreach (var c in chain)
                list.AddRange(c.GetFields(InstanceFields).OrderBy(f => f.Name, StringComparer.Ordinal));
            return list.ToArray();
        }

        static string FieldKey(FieldInfo f) => f.DeclaringType.Name + ":" + f.Name;

        static readonly Dictionary<Type, Array> emptyArrays = new Dictionary<Type, Array>();

        /// <summary>The runtime's shared empty array of <paramref name="element"/> (<c>Array.Empty&lt;T&gt;()</c>), which every empty array loads as.</summary>
        static Array EmptyArrayOf(Type element)
        {
            lock (emptyArrays)
            {
                if (!emptyArrays.TryGetValue(element, out var a))
                    emptyArrays[element] = a = (Array)typeof(Array).GetMethod("Empty").MakeGenericMethod(element).Invoke(null, null);
                return a;
            }
        }

        static bool IsVector(Type t) => t.IsArray && t == t.GetElementType().MakeArrayType();

        static void WritePrimitive(BinaryWriter w, TypeCode code, object v)
        {
            switch (code)
            {
                case TypeCode.Boolean: w.Write((bool)v); break;
                case TypeCode.Byte: w.Write((byte)v); break;
                case TypeCode.SByte: w.Write((sbyte)v); break;
                case TypeCode.Char: w.Write((ushort)(char)v); break;
                case TypeCode.Int16: w.Write((short)v); break;
                case TypeCode.UInt16: w.Write((ushort)v); break;
                case TypeCode.Int32: w.Write((int)v); break;
                case TypeCode.UInt32: w.Write((uint)v); break;
                case TypeCode.Int64: w.Write((long)v); break;
                case TypeCode.UInt64: w.Write((ulong)v); break;
                case TypeCode.Single: w.Write((float)v); break;
                case TypeCode.Double: w.Write((double)v); break;
                default: throw new NotSupportedException("Primitive " + code);
            }
        }

        static object ReadPrimitive(BinaryReader r, TypeCode code)
        {
            switch (code)
            {
                case TypeCode.Boolean: return r.ReadBoolean();
                case TypeCode.Byte: return r.ReadByte();
                case TypeCode.SByte: return r.ReadSByte();
                case TypeCode.Char: return (char)r.ReadUInt16();
                case TypeCode.Int16: return r.ReadInt16();
                case TypeCode.UInt16: return r.ReadUInt16();
                case TypeCode.Int32: return r.ReadInt32();
                case TypeCode.UInt32: return r.ReadUInt32();
                case TypeCode.Int64: return r.ReadInt64();
                case TypeCode.UInt64: return r.ReadUInt64();
                case TypeCode.Single: return r.ReadSingle();
                case TypeCode.Double: return r.ReadDouble();
                default: throw new NotSupportedException("Primitive " + code);
            }
        }

        static void WriteVar(BinaryWriter w, int value)
        {
            uint v = (uint)((value << 1) ^ (value >> 31));
            while (v >= 0x80)
            {
                w.Write((byte)(v | 0x80));
                v >>= 7;
            }
            w.Write((byte)v);
        }

        static int ReadVar(BinaryReader r)
        {
            uint v = 0;
            int shift = 0;
            byte b;
            do
            {
                b = r.ReadByte();
                v |= (uint)(b & 0x7f) << shift;
                shift += 7;
            } while ((b & 0x80) != 0);
            return (int)(v >> 1) ^ -(int)(v & 1);
        }

        /// <summary>How a dictionary or set compares its keys, as a code: comparers are not simulation state.</summary>
        static int ComparerCode(object comparer, Type keyType)
        {
            if (comparer == null) return 0;
            var def = typeof(EqualityComparer<>).MakeGenericType(keyType).GetProperty("Default").GetValue(null);
            if (ReferenceEquals(comparer, def) || comparer.GetType() == def.GetType()) return 0;
            if (comparer.Equals(StringComparer.Ordinal)) return 1;
            if (comparer.Equals(StringComparer.OrdinalIgnoreCase)) return 2;
            if (comparer.Equals(StringComparer.InvariantCulture)) return 3;
            if (comparer.Equals(StringComparer.InvariantCultureIgnoreCase)) return 4;
            throw new NotSupportedException("Cannot save a collection compared by " + comparer.GetType());
        }

        static object ComparerOf(int code)
        {
            switch (code)
            {
                case 1: return StringComparer.Ordinal;
                case 2: return StringComparer.OrdinalIgnoreCase;
                case 3: return StringComparer.InvariantCulture;
                case 4: return StringComparer.InvariantCultureIgnoreCase;
                default: return null;
            }
        }

        sealed class RefEq : IEqualityComparer<object>
        {
            public static readonly RefEq Instance = new RefEq();
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object o) => RuntimeHelpers.GetHashCode(o);
        }

        /// <summary>Generic collection operations a non-generic caller needs, looked up once per collection type.</summary>
        sealed class CollectionOps
        {
            public MethodInfo Add, Clear, Push, Enqueue;
            public PropertyInfo Comparer;
            public Type Element;

            static readonly Dictionary<Type, CollectionOps> cache = new Dictionary<Type, CollectionOps>();

            public static CollectionOps For(Type t)
            {
                lock (cache)
                {
                    if (cache.TryGetValue(t, out var ops)) return ops;
                    var args = t.GetGenericArguments();
                    ops = new CollectionOps
                    {
                        Element = args[0],
                        Add = t.GetMethod("Add", args),
                        Clear = t.GetMethod("Clear", Type.EmptyTypes),
                        Push = t.GetMethod("Push", args),
                        Enqueue = t.GetMethod("Enqueue", args),
                        Comparer = t.GetProperty("Comparer"),
                    };
                    cache[t] = ops;
                    return ops;
                }
            }
        }

        // ---------------------------------------------------------------- statics

        /// <summary>
        /// Objects reachable from this assembly's static fields (design tables, catalogues, the shared
        /// instance that compiler-generated lambdas hang off), each named by its path from the field.
        /// They are the same in every run of a build, so a save refers to them by name instead of copying.
        /// </summary>
        static class Statics
        {
            static Dictionary<object, string> keyOf;
            static Dictionary<string, object> byKey;
            static readonly object gate = new object();

            public static bool TryKey(object o, out string key)
            {
                Ensure();
                return keyOf.TryGetValue(o, out key);
            }

            public static object Get(string key)
            {
                Ensure();
                if (byKey.TryGetValue(key, out var o)) return o;
                throw new InvalidDataException("The save refers to a static object this build does not have: " + key);
            }

            static void Ensure()
            {
                if (keyOf != null) return;
                lock (gate)
                {
                    if (keyOf != null) return;
                    var k = new Dictionary<object, string>(RefEq.Instance);
                    var b = new Dictionary<string, object>();
                    Type[] types;
                    try { types = CoreAssembly.GetTypes(); }
                    catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                    foreach (var t in types.Where(t => !t.ContainsGenericParameters).OrderBy(t => t.FullName, StringComparer.Ordinal))
                    {
                        foreach (var f in t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                                     .OrderBy(f => f.Name, StringComparer.Ordinal))
                        {
                            if (f.IsLiteral) continue;
                            object v;
                            try { v = f.GetValue(null); }
                            catch { continue; }
                            Walk(v, t.FullName + "::" + f.Name, 0, k, b);
                        }
                    }
                    byKey = b;
                    keyOf = k;
                }
            }

            // A static collection can still be filled by code running on another thread (parallel test
            // fixtures), so it is copied into an array before it is walked; a copy that collides with a
            // writer is taken again, so the walk itself never enumerates a live collection.
            static object[] Snapshot(IEnumerable source)
            {
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        var list = new List<object>();
                        foreach (var x in source) list.Add(x);
                        return list.ToArray();
                    }
                    catch (InvalidOperationException) when (attempt < 100) { System.Threading.Thread.Yield(); }
                }
            }

            static DictionaryEntry[] SnapshotEntries(IDictionary source)
            {
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        var list = new List<DictionaryEntry>();
                        foreach (DictionaryEntry e in source) list.Add(e);
                        return list.ToArray();
                    }
                    catch (InvalidOperationException) when (attempt < 100) { System.Threading.Thread.Yield(); }
                }
            }

            static void Walk(object v, string key, int depth, Dictionary<object, string> k, Dictionary<string, object> b)
            {
                if (v == null || v is string || v is Delegate || v is MemberInfo || depth > 24) return;
                var t = v.GetType();
                if (t.IsPrimitive || t.IsEnum || t.IsPointer) return;
                if (!t.IsValueType)
                {
                    if (k.ContainsKey(v)) return;
                    k[v] = key;
                    b[key] = v;
                }
                if (v is Array a)
                {
                    if (t.GetElementType().IsPrimitive) return;
                    int i = 0;
                    foreach (var x in Snapshot(a)) Walk(x, key + "[" + i++ + "]", depth + 1, k, b);
                    return;
                }
                if (v is IDictionary d)
                {
                    int i = 0;
                    foreach (DictionaryEntry e in SnapshotEntries(d))
                    {
                        Walk(e.Key, key + "{k" + i + "}", depth + 1, k, b);
                        Walk(e.Value, key + "{" + i + "}", depth + 1, k, b);
                        i++;
                    }
                    return;
                }
                if (t.Assembly != CoreAssembly && !t.IsValueType)
                {
                    if (v is IEnumerable en && t.IsGenericType)
                    {
                        int i = 0;
                        foreach (var x in Snapshot(en)) Walk(x, key + "[" + i++ + "]", depth + 1, k, b);
                    }
                    return;
                }
                foreach (var f in FieldsOf(t)) Walk(f.GetValue(v), key + "." + f.Name, depth + 1, k, b);
            }
        }

        // ---------------------------------------------------------------- writer

        sealed class WType
        {
            public int Id;
            public Type Type;
            public Kind Kind;
            public TypeCode Code;
            public FieldInfo[] Fields;
            public WType[] FieldTypes;
            public WType Element, Key, Value; // array/list/set/queue/stack/nullable element; dictionary key and value
            public WType[] Args;
            public CollectionOps Ops;
        }

        sealed class GraphWriter
        {
            public readonly MemoryStream Table = new MemoryStream();
            public readonly MemoryStream Payload = new MemoryStream();
            readonly BinaryWriter table, payload;
            readonly List<WType> types = new List<WType>();
            readonly Dictionary<Type, WType> typeOf = new Dictionary<Type, WType>();
            readonly Dictionary<object, int> ids = new Dictionary<object, int>(RefEq.Instance);
            readonly Dictionary<string, int> strings = new Dictionary<string, int>(StringComparer.Ordinal);
            readonly Dictionary<Type, int> emptyIds = new Dictionary<Type, int>();
            readonly Queue<(object o, WType t, int id)> pending = new Queue<(object, WType, int)>();
            int nextId = 1;
            public int EntryCount, PayloadCount;

            readonly Assembly home;

            public GraphWriter(Assembly home)
            {
                this.home = home;
                table = new BinaryWriter(Table);
                payload = new BinaryWriter(Payload);
            }

            /// <summary>Breadth first, so a long chain of references never runs deep on the stack.</summary>
            public int WriteAll(object root)
            {
                int rootId = Ref(root);
                while (pending.Count > 0)
                {
                    var (o, t, id) = pending.Dequeue();
                    WriteVar(payload, id);
                    Content(o, t);
                    PayloadCount++;
                }
                table.Flush();
                payload.Flush();
                return rootId;
            }

            WType Meta(Type t)
            {
                if (typeOf.TryGetValue(t, out var m)) return m;
                m = new WType { Id = types.Count, Type = t, Kind = KindOf(t) };
                typeOf[t] = m;
                types.Add(m);
                if (t.IsArray) m.Element = Meta(t.GetElementType());
                if (t.IsGenericType && !t.IsGenericTypeDefinition) m.Args = t.GetGenericArguments().Select(Meta).ToArray();
                switch (m.Kind)
                {
                    case Kind.Primitive:
                        m.Code = Type.GetTypeCode(t);
                        break;
                    case Kind.Enum:
                        m.Code = Type.GetTypeCode(Enum.GetUnderlyingType(t));
                        break;
                    case Kind.Nullable:
                        m.Element = m.Args[0];
                        break;
                    case Kind.List:
                    case Kind.HashSet:
                    case Kind.Queue:
                    case Kind.Stack:
                        m.Ops = CollectionOps.For(t);
                        m.Element = m.Args[0];
                        break;
                    case Kind.Dictionary:
                        m.Ops = CollectionOps.For(t);
                        m.Key = m.Args[0];
                        m.Value = m.Args[1];
                        break;
                    case Kind.Struct:
                    case Kind.Object:
                        m.Fields = FieldsOf(t);
                        m.FieldTypes = m.Fields.Select(f => Meta(f.FieldType)).ToArray();
                        break;
                }
                return m;
            }

            public void WriteTypes(BinaryWriter w)
            {
                w.Write(types.Count);
                foreach (var m in types)
                {
                    var t = m.Type;
                    if (t.IsArray)
                    {
                        w.Write((byte)1);
                        w.Write(t.GetArrayRank());
                        w.Write(IsVector(t) ? (byte)1 : (byte)0);
                        w.Write(m.Element.Id);
                    }
                    else if (t.IsGenericType)
                    {
                        var d = t.GetGenericTypeDefinition();
                        w.Write((byte)2);
                        w.Write(d.FullName);
                        w.Write(d.Assembly.GetName().Name);
                        w.Write(m.Args.Length);
                        foreach (var a in m.Args) w.Write(a.Id);
                    }
                    else
                    {
                        w.Write((byte)0);
                        w.Write(t.FullName);
                        w.Write(t.Assembly.GetName().Name);
                    }
                }
                foreach (var m in types)
                {
                    if (m.Fields == null)
                    {
                        w.Write(-1);
                        continue;
                    }
                    w.Write(m.Fields.Length);
                    for (int i = 0; i < m.Fields.Length; i++)
                    {
                        w.Write(FieldKey(m.Fields[i]));
                        w.Write(m.FieldTypes[i].Id);
                    }
                }
            }

            /// <summary>The id of a reference (0 for null), giving it one and queueing its contents the first time it is seen.</summary>
            int Ref(object o)
            {
                if (o == null) return 0;
                if (o is string s)
                {
                    if (strings.TryGetValue(s, out int sid)) return sid;
                    sid = nextId++;
                    strings[s] = sid;
                    BeginEntry(sid, Entry.String);
                    table.Write(s);
                    return sid;
                }
                if (ids.TryGetValue(o, out int id)) return id;
                if (o is Array empty && empty.Length == 0 && IsVector(empty.GetType()))
                {
                    // Every empty array is the same as any other of its type (the runtime hands out shared ones
                    // from more than one place): one entry per type, read back as Array.Empty.
                    var et = Meta(empty.GetType());
                    if (!emptyIds.TryGetValue(et.Type, out id))
                    {
                        id = nextId++;
                        emptyIds[et.Type] = id;
                        BeginEntry(id, Entry.EmptyArray);
                        table.Write(et.Id);
                    }
                    return id;
                }

                if (o is Delegate del)
                {
                    // Only what is wired inside the simulation: outside subscribers subscribe again after a load.
                    var calls = del.GetInvocationList().Where(c => c.Method.DeclaringType != null && (c.Method.DeclaringType.Assembly == CoreAssembly || c.Method.DeclaringType.Assembly == home)).ToArray();
                    if (calls.Length == 0) return 0;
                    id = nextId++;
                    ids[o] = id;
                    var targets = calls.Select(c => c.Target == null ? 0 : Ref(c.Target)).ToArray();
                    BeginEntry(id, Entry.Delegate);
                    table.Write(Meta(o.GetType()).Id);
                    table.Write(calls.Length);
                    for (int i = 0; i < calls.Length; i++)
                    {
                        var method = calls[i].Method;
                        if (method.IsGenericMethod) throw new NotSupportedException("Cannot save a delegate to the generic method " + method);
                        table.Write(Meta(method.DeclaringType).Id);
                        table.Write(method.Name);
                        var ps = method.GetParameters();
                        table.Write(ps.Length);
                        foreach (var p in ps) table.Write(Meta(p.ParameterType).Id);
                        WriteVar(table, targets[i]);
                    }
                    return id;
                }

                id = nextId++;
                ids[o] = id;
                if (Statics.TryKey(o, out string key))
                {
                    BeginEntry(id, Entry.External);
                    table.Write(key);
                    return id;
                }
                var m = Meta(o.GetType());
                switch (m.Kind)
                {
                    case Kind.Array:
                        var a = (Array)o;
                        BeginEntry(id, Entry.Array);
                        table.Write(m.Id);
                        table.Write(a.Rank);
                        for (int i = 0; i < a.Rank; i++) table.Write(a.GetLength(i));
                        break;
                    case Kind.List:
                    case Kind.Queue:
                    case Kind.Stack:
                        BeginEntry(id, Entry.Collection);
                        table.Write(m.Id);
                        table.Write(0);
                        break;
                    case Kind.Dictionary:
                    case Kind.HashSet:
                        BeginEntry(id, Entry.Collection);
                        table.Write(m.Id);
                        table.Write(ComparerCode(m.Ops.Comparer.GetValue(o), m.Ops.Element));
                        break;
                    case Kind.Object:
                        BeginEntry(id, Entry.Object);
                        table.Write(m.Id);
                        break;
                    default:
                        throw new NotSupportedException("Cannot save a " + m.Type + " by reference");
                }
                pending.Enqueue((o, m, id));
                return id;
            }

            void BeginEntry(int id, Entry kind)
            {
                WriteVar(table, id);
                table.Write((byte)kind);
                EntryCount++;
            }

            void Content(object o, WType m)
            {
                switch (m.Kind)
                {
                    case Kind.Object:
                        Fields(o, m);
                        break;
                    case Kind.Array:
                        if (o is double[] da) { foreach (var x in da) payload.Write(x); break; }
                        if (o is float[] fa) { foreach (var x in fa) payload.Write(x); break; }
                        if (o is int[] ia) { foreach (var x in ia) payload.Write(x); break; }
                        if (o is byte[] ba) { payload.Write(ba); break; }
                        foreach (var x in (Array)o) Value(m.Element, x);
                        break;
                    case Kind.List:
                        var list = (IList)o;
                        WriteVar(payload, list.Count);
                        foreach (var x in list) Value(m.Element, x);
                        break;
                    case Kind.Queue:
                    case Kind.Stack:
                        var items = ((IEnumerable)o).Cast<object>().ToList();
                        WriteVar(payload, items.Count);
                        foreach (var x in items) Value(m.Element, x);
                        break;
                    case Kind.HashSet:
                        var members = ((IEnumerable)o).Cast<object>().ToList();
                        m.Ops.Clear.Invoke(o, null);
                        foreach (var x in members) m.Ops.Add.Invoke(o, new[] { x });
                        WriteVar(payload, members.Count);
                        foreach (var x in members) Value(m.Element, x);
                        break;
                    case Kind.Dictionary:
                        var d = (IDictionary)o;
                        var keys = new List<object>(d.Count);
                        var values = new List<object>(d.Count);
                        foreach (DictionaryEntry e in d)
                        {
                            keys.Add(e.Key);
                            values.Add(e.Value);
                        }
                        d.Clear();
                        for (int i = 0; i < keys.Count; i++) d.Add(keys[i], values[i]);
                        WriteVar(payload, keys.Count);
                        for (int i = 0; i < keys.Count; i++)
                        {
                            Value(m.Key, keys[i]);
                            Value(m.Value, values[i]);
                        }
                        break;
                }
            }

            void Fields(object o, WType m)
            {
                for (int i = 0; i < m.Fields.Length; i++) Value(m.FieldTypes[i], m.Fields[i].GetValue(o));
            }

            /// <summary>A value of declared type <paramref name="m"/>: inline for value types, an id for references.</summary>
            void Value(WType m, object v)
            {
                switch (m.Kind)
                {
                    case Kind.Primitive:
                        WritePrimitive(payload, m.Code, v);
                        return;
                    case Kind.Enum:
                        WritePrimitive(payload, m.Code, Convert.ChangeType(v, Enum.GetUnderlyingType(m.Type)));
                        return;
                    case Kind.Nullable:
                        payload.Write(v != null);
                        if (v != null) Value(m.Element, v);
                        return;
                    case Kind.DateTime:
                        payload.Write(((DateTime)v).ToBinary());
                        return;
                    case Kind.TimeSpan:
                        payload.Write(((TimeSpan)v).Ticks);
                        return;
                    case Kind.Decimal:
                        foreach (int bits in decimal.GetBits((decimal)v)) payload.Write(bits);
                        return;
                    case Kind.Struct:
                        Fields(v, m);
                        return;
                }
                if (v != null && v.GetType().IsValueType)
                {
                    // A boxed value in an object or interface slot.
                    var bm = Meta(v.GetType());
                    WriteVar(payload, -1);
                    payload.Write(bm.Id);
                    Value(bm, v);
                    return;
                }
                WriteVar(payload, Ref(v));
            }
        }

        // ---------------------------------------------------------------- reader

        sealed class RType
        {
            public Type Type;
            public Kind Kind;
            public TypeCode Code;
            public RType Element, Key, Value;
            public RType[] Args;
            public RType[] FieldTypes;
            public FieldInfo[] Fields; // this build's field for each saved one, or null where it is gone
            public CollectionOps Ops;
        }

        sealed class GraphReader
        {
            readonly BinaryReader r;
            RType[] types;
            readonly Dictionary<Type, RType> metaOf = new Dictionary<Type, RType>();
            object[] objects;
            readonly List<Action> deferred = new List<Action>();

            public GraphReader(BinaryReader r) => this.r = r;

            public object ReadAll()
            {
                ReadTypes();
                int entries = r.ReadInt32();
                var delegates = new List<(int id, RType type, (RType decl, string name, RType[] ps, int target)[] calls)>();
                var made = new List<(int id, object o)>(entries);
                int maxId = 0;
                for (int e = 0; e < entries; e++)
                {
                    int id = ReadVar(r);
                    maxId = Math.Max(maxId, id);
                    var kind = (Entry)r.ReadByte();
                    switch (kind)
                    {
                        case Entry.String:
                            made.Add((id, r.ReadString()));
                            break;
                        case Entry.External:
                            made.Add((id, Statics.Get(r.ReadString())));
                            break;
                        case Entry.EmptyArray:
                            made.Add((id, EmptyArrayOf(types[r.ReadInt32()].Element.Type)));
                            break;
                        case Entry.Object:
                            made.Add((id, New(types[r.ReadInt32()].Type)));
                            break;
                        case Entry.Array:
                        {
                            var t = types[r.ReadInt32()];
                            int rank = r.ReadInt32();
                            var lengths = new int[rank];
                            for (int i = 0; i < rank; i++) lengths[i] = r.ReadInt32();
                            made.Add((id, Array.CreateInstance(t.Element.Type, lengths)));
                            break;
                        }
                        case Entry.Collection:
                        {
                            var t = types[r.ReadInt32()];
                            var comparer = ComparerOf(r.ReadInt32());
                            made.Add((id, comparer != null ? Activator.CreateInstance(t.Type, comparer) : Activator.CreateInstance(t.Type)));
                            break;
                        }
                        case Entry.Delegate:
                        {
                            var t = types[r.ReadInt32()];
                            int n = r.ReadInt32();
                            var calls = new (RType, string, RType[], int)[n];
                            for (int i = 0; i < n; i++)
                            {
                                var decl = types[r.ReadInt32()];
                                string name = r.ReadString();
                                var ps = new RType[r.ReadInt32()];
                                for (int p = 0; p < ps.Length; p++) ps[p] = types[r.ReadInt32()];
                                calls[i] = (decl, name, ps, ReadVar(r));
                            }
                            delegates.Add((id, t, calls));
                            break;
                        }
                        default:
                            throw new InvalidDataException("Unknown entry " + kind);
                    }
                }
                objects = new object[maxId + 1];
                foreach (var (id, o) in made) objects[id] = o;
                // Delegates once their targets exist.
                foreach (var (id, type, calls) in delegates)
                {
                    Delegate combined = null;
                    foreach (var (decl, name, ps, target) in calls)
                    {
                        var method = decl.Type.GetMethod(name, AnyMethod, null, ps.Select(p => p.Type).ToArray(), null);
                        if (method == null) throw new InvalidDataException("The save wires a method this build does not have: " + decl.Type + "." + name);
                        var one = method.IsStatic
                            ? Delegate.CreateDelegate(type.Type, method)
                            : Delegate.CreateDelegate(type.Type, objects[target], method);
                        combined = Delegate.Combine(combined, one);
                    }
                    objects[id] = combined;
                }

                int payloads = r.ReadInt32();
                for (int p = 0; p < payloads; p++)
                {
                    var o = objects[ReadVar(r)];
                    Content(o, metaOf[o.GetType()]);
                }
                // Keys go in only once every object is filled in, in case one hashes by its contents.
                foreach (var a in deferred) a();
                return objects[r.ReadInt32()];
            }

#pragma warning disable SYSLIB0050
            static object New(Type t) => FormatterServices.GetUninitializedObject(t);
#pragma warning restore SYSLIB0050

            void ReadTypes()
            {
                int n = r.ReadInt32();
                var raw = new (byte tag, int rank, bool vector, int element, string name, string assembly, int[] args)[n];
                for (int i = 0; i < n; i++)
                {
                    byte tag = r.ReadByte();
                    if (tag == 1)
                    {
                        int rank = r.ReadInt32();
                        bool vector = r.ReadByte() == 1;
                        raw[i] = (tag, rank, vector, r.ReadInt32(), null, null, null);
                    }
                    else if (tag == 2)
                    {
                        string name = r.ReadString(), asm = r.ReadString();
                        var args = new int[r.ReadInt32()];
                        for (int a = 0; a < args.Length; a++) args[a] = r.ReadInt32();
                        raw[i] = (tag, 0, false, 0, name, asm, args);
                    }
                    else
                    {
                        string name = r.ReadString(), asm = r.ReadString();
                        raw[i] = (tag, 0, false, 0, name, asm, null);
                    }
                }
                var resolved = new Type[n];
                Type Resolve(int i)
                {
                    if (resolved[i] != null) return resolved[i];
                    var x = raw[i];
                    Type t;
                    if (x.tag == 1)
                    {
                        var e = Resolve(x.element);
                        t = x.vector ? e.MakeArrayType() : e.MakeArrayType(x.rank);
                    }
                    else if (x.tag == 2) t = Find(x.name, x.assembly).MakeGenericType(x.args.Select(Resolve).ToArray());
                    else t = Find(x.name, x.assembly);
                    return resolved[i] = t;
                }
                types = new RType[n];
                for (int i = 0; i < n; i++)
                {
                    types[i] = new RType { Type = Resolve(i) };
                    metaOf[types[i].Type] = types[i];
                }
                for (int i = 0; i < n; i++)
                {
                    var m = types[i];
                    var t = m.Type;
                    m.Kind = KindOf(t);
                    if (t.IsArray) m.Element = types[raw[i].element];
                    if (raw[i].args != null) m.Args = raw[i].args.Select(a => types[a]).ToArray();
                    switch (m.Kind)
                    {
                        case Kind.Primitive: m.Code = Type.GetTypeCode(t); break;
                        case Kind.Enum: m.Code = Type.GetTypeCode(Enum.GetUnderlyingType(t)); break;
                        case Kind.Nullable: m.Element = m.Args[0]; break;
                        case Kind.List:
                        case Kind.HashSet:
                        case Kind.Queue:
                        case Kind.Stack:
                            m.Ops = CollectionOps.For(t);
                            m.Element = m.Args[0];
                            break;
                        case Kind.Dictionary:
                            m.Ops = CollectionOps.For(t);
                            m.Key = m.Args[0];
                            m.Value = m.Args[1];
                            break;
                    }
                }
                for (int i = 0; i < n; i++)
                {
                    int count = r.ReadInt32();
                    if (count < 0) continue;
                    var m = types[i];
                    m.FieldTypes = new RType[count];
                    m.Fields = new FieldInfo[count];
                    var current = new Dictionary<string, FieldInfo>();
                    foreach (var f in FieldsOf(m.Type)) current[FieldKey(f)] = f;
                    for (int f = 0; f < count; f++)
                    {
                        string key = r.ReadString();
                        m.FieldTypes[f] = types[r.ReadInt32()];
                        if (current.TryGetValue(key, out var fi))
                        {
                            m.Fields[f] = fi;
                            current.Remove(key);
                        }
                    }
                    // Objects are made without running their constructors, so a field the save lacks would load as
                    // null (not its initialiser) and break later: a save from before it was added is refused instead.
                    if (current.Count > 0)
                        throw new InvalidDataException("The save is from an older build: " + m.Type.FullName + " has fields it lacks (" + string.Join(", ", current.Keys) + ")");
                }
            }

            static readonly Dictionary<string, Type> found = new Dictionary<string, Type>();

            static Type Find(string fullName, string assembly)
            {
                string key = assembly + "|" + fullName;
                lock (found)
                {
                    if (found.TryGetValue(key, out var t)) return t;
                    var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                    var home = assemblies.FirstOrDefault(a => a.GetName().Name == assembly);
                    t = home?.GetType(fullName);
                    if (t == null)
                        foreach (var a in assemblies)
                            if ((t = a.GetType(fullName)) != null) break;
                    if (t == null) throw new InvalidDataException("The save holds a type this build does not have: " + fullName);
                    return found[key] = t;
                }
            }

            void Content(object o, RType m)
            {
                switch (m.Kind)
                {
                    case Kind.Object:
                        Fields(o, m);
                        break;
                    case Kind.Array:
                        if (o is double[] da) { for (int i = 0; i < da.Length; i++) da[i] = r.ReadDouble(); break; }
                        if (o is float[] fa) { for (int i = 0; i < fa.Length; i++) fa[i] = r.ReadSingle(); break; }
                        if (o is int[] ia) { for (int i = 0; i < ia.Length; i++) ia[i] = r.ReadInt32(); break; }
                        if (o is byte[] ba) { Buffer.BlockCopy(r.ReadBytes(ba.Length), 0, ba, 0, ba.Length); break; }
                        var a = (Array)o;
                        if (a.Rank == 1)
                        {
                            for (int i = 0; i < a.Length; i++) a.SetValue(Value(m.Element), i);
                            break;
                        }
                        var index = new int[a.Rank];
                        for (int flat = 0; flat < a.Length; flat++)
                        {
                            int rest = flat;
                            for (int d = a.Rank - 1; d >= 0; d--)
                            {
                                int len = a.GetLength(d);
                                index[d] = rest % len;
                                rest /= len;
                            }
                            a.SetValue(Value(m.Element), index);
                        }
                        break;
                    case Kind.List:
                    {
                        var list = (IList)o;
                        int n = ReadVar(r);
                        for (int i = 0; i < n; i++) list.Add(Value(m.Element));
                        break;
                    }
                    case Kind.Queue:
                    {
                        int n = ReadVar(r);
                        for (int i = 0; i < n; i++) m.Ops.Enqueue.Invoke(o, new[] { Value(m.Element) });
                        break;
                    }
                    case Kind.Stack:
                    {
                        int n = ReadVar(r);
                        var items = new object[n];
                        for (int i = 0; i < n; i++) items[i] = Value(m.Element);
                        for (int i = n - 1; i >= 0; i--) m.Ops.Push.Invoke(o, new[] { items[i] });
                        break;
                    }
                    case Kind.HashSet:
                    {
                        int n = ReadVar(r);
                        var items = new object[n];
                        for (int i = 0; i < n; i++) items[i] = Value(m.Element);
                        var add = m.Ops.Add;
                        deferred.Add(() => { foreach (var x in items) add.Invoke(o, new[] { x }); });
                        break;
                    }
                    case Kind.Dictionary:
                    {
                        int n = ReadVar(r);
                        var keys = new object[n];
                        var values = new object[n];
                        for (int i = 0; i < n; i++)
                        {
                            keys[i] = Value(m.Key);
                            values[i] = Value(m.Value);
                        }
                        var d = (IDictionary)o;
                        deferred.Add(() => { for (int i = 0; i < n; i++) d.Add(keys[i], values[i]); });
                        break;
                    }
                }
            }

            void Fields(object o, RType m)
            {
                for (int i = 0; i < m.FieldTypes.Length; i++)
                {
                    var v = Value(m.FieldTypes[i]);
                    var f = m.Fields[i];
                    if (f != null && Fits(f.FieldType, v)) f.SetValue(o, v);
                }
            }

            static bool Fits(Type t, object v) =>
                v == null ? !t.IsValueType || Nullable.GetUnderlyingType(t) != null
                    : t.IsInstanceOfType(v) || Nullable.GetUnderlyingType(t)?.IsInstanceOfType(v) == true;

            object Value(RType m)
            {
                switch (m.Kind)
                {
                    case Kind.Primitive:
                        return ReadPrimitive(r, m.Code);
                    case Kind.Enum:
                        return Enum.ToObject(m.Type, ReadPrimitive(r, m.Code));
                    case Kind.Nullable:
                        return r.ReadBoolean() ? Value(m.Element) : null;
                    case Kind.DateTime:
                        return DateTime.FromBinary(r.ReadInt64());
                    case Kind.TimeSpan:
                        return new TimeSpan(r.ReadInt64());
                    case Kind.Decimal:
                        return new decimal(new[] { r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32() });
                    case Kind.Struct:
                        var box = New(m.Type);
                        Fields(box, m);
                        return box;
                }
                int id = ReadVar(r);
                if (id == 0) return null;
                if (id == -1) return Value(types[r.ReadInt32()]);
                return objects[id];
            }
        }
    }
}
