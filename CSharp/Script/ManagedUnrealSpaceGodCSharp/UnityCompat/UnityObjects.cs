using System;
using System.Collections.Generic;
using System.Linq; // US-T26: the project's implicit usings are off

// UnityEngine's object model as the ported Runtime uses it: Object (with Unity's "destroyed equals null"),
// GameObject, Component, Transform (a managed hierarchy with Unity's local/world semantics), MonoBehaviour, Time,
// Random, Debug, Application. Nothing here touches Unreal: UnityBridge pushes what is drawn to Unreal once a frame.
namespace UnityEngine
{
    [Flags]
    public enum HideFlags { None = 0, HideInHierarchy = 1, HideInInspector = 2, DontSaveInEditor = 4, NotEditable = 8, DontSaveInBuild = 16, DontUnloadUnusedAsset = 32, DontSave = 52, HideAndDontSave = 61 }

    public enum FindObjectsSortMode { None = 0, InstanceID = 1 }
    public enum FindObjectsInactive { Exclude = 0, Include = 1 }

    public class Object
    {
        /// <summary>Every scene GameObject not yet destroyed (assets excluded), for FindObjectsByType.</summary>
        internal static readonly HashSet<GameObject> Live = new HashSet<GameObject>();

        public static T[] FindObjectsByType<T>(FindObjectsSortMode sortMode) where T : Object =>
            FindObjectsByType<T>(FindObjectsInactive.Exclude, sortMode);

        public static T[] FindObjectsByType<T>(FindObjectsInactive inactive, FindObjectsSortMode sortMode) where T : Object
        {
            var found = new List<T>();
            foreach (var go in Live)
            {
                if (inactive == FindObjectsInactive.Exclude && !go.activeInHierarchy) continue;
                if (go.isAsset || go.transform.root.gameObjectInternal!.isAsset) continue;
                if (go is T g) { found.Add(g); continue; }
                foreach (var comp in go.components) if (comp is T t && !comp.destroyed) found.Add(t);
            }
            return found.ToArray();
        }

        public static T? FindAnyObjectByType<T>() where T : Object
        {
            var all = FindObjectsByType<T>(FindObjectsSortMode.None);
            return all.Length > 0 ? all[0] : null;
        }
        public static T? FindFirstObjectByType<T>() where T : Object => FindAnyObjectByType<T>();
        public static T? FindObjectOfType<T>() where T : Object => FindAnyObjectByType<T>();
        public string name { get; set; } = "";
        public HideFlags hideFlags { get; set; }
        internal bool destroyed;

        public static void Destroy(Object? obj)
        {
            if (obj is null || obj.destroyed) return;
            obj.OnDestroyInternal();
        }
        public static void Destroy(Object? obj, float t) => Destroy(obj);
        public static void DestroyImmediate(Object? obj) => Destroy(obj);
        public static void DestroyImmediate(Object? obj, bool allowDestroyingAssets) => Destroy(obj);
        public static void DontDestroyOnLoad(Object target) { }

        /// <summary>Destroys this object: a GameObject takes its components and children with it.</summary>
        internal virtual void OnDestroyInternal() => destroyed = true;

        public static T Instantiate<T>(T original) where T : Object => (T)original.CloneInternal(null, false);
        public static T Instantiate<T>(T original, Transform parent) where T : Object => Instantiate(original, parent, false);
        public static T Instantiate<T>(T original, Transform? parent, bool instantiateInWorldSpace) where T : Object =>
            (T)original.CloneInternal(parent, instantiateInWorldSpace);
        public static T Instantiate<T>(T original, Vector3 position, Quaternion rotation, Transform? parent = null) where T : Object
        {
            var clone = (T)original.CloneInternal(parent, false);
            if (clone is GameObject go) { go.transform.position = position; go.transform.rotation = rotation; }
            return clone;
        }
        internal virtual Object CloneInternal(Transform? parent, bool worldSpace) => throw new NotSupportedException($"Instantiate {GetType().Name}");

        public static bool operator ==(Object? a, Object? b)
        {
            bool aNull = a is null || a.destroyed, bNull = b is null || b.destroyed;
            if (aNull || bNull) return aNull && bNull;
            return ReferenceEquals(a, b);
        }
        public static bool operator !=(Object? a, Object? b) => !(a == b);
        public static implicit operator bool(Object? o) => !(o is null) && !o.destroyed;
        public override bool Equals(object? other) => other is Object o ? this == o : other is null && destroyed;
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        public override string ToString() => name;
        public int GetInstanceID() => GetHashCode();
    }

    public sealed class GameObject : Object
    {
        public readonly Transform transform;
        internal readonly List<Component> components = new List<Component>();
        bool activeSelfValue = true;
        public int layer { get; set; }
        public string tag { get; set; } = "Untagged";

        public GameObject() : this("New Game Object") { }
        public GameObject(string name)
        {
            this.name = name;
            transform = new Transform { gameObjectInternal = this };
            components.Add(transform);
            Live.Add(this);
        }
        public GameObject(string name, params Type[] components) : this(name)
        {
            foreach (var t in components) AddComponent(t);
        }

        public GameObject gameObject => this;

        /// <summary>A loaded asset (Resources' model prototypes): never in the scene itself, only its Instantiate copies are.</summary>
        internal bool isAsset;
        public bool activeSelf => activeSelfValue;
        public bool activeInHierarchy
        {
            get
            {
                for (var t = transform; t != null; t = t.parentInternal)
                    if (!t.gameObjectInternal!.activeSelfValue || t.gameObjectInternal.isAsset) return false;
                return true;
            }
        }
        public void SetActive(bool value)
        {
            if (activeSelfValue == value) return;
            activeSelfValue = value;
            UnityBridge.MarkHierarchyChanged();
        }

        public T AddComponent<T>() where T : Component, new() => (T)AddComponent(typeof(T));
        public Component AddComponent(Type type)
        {
            if (type == typeof(Transform)) return transform;
            var c = (Component)Activator.CreateInstance(type, true)!;
            c.gameObjectInternal = this;
            components.Add(c);
            c.OnAddedInternal();
            return c;
        }

        public T? GetComponent<T>() where T : class
        {
            foreach (var c in components) if (c is T t && !c.destroyed) return t;
            return null;
        }
        public bool TryGetComponent<T>(out T? component) where T : class => (component = GetComponent<T>()) != null;
        public T? GetComponentInChildren<T>(bool includeInactive = false) where T : class
        {
            var all = GetComponentsInChildren<T>(includeInactive);
            return all.Length > 0 ? all[0] : null;
        }
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : class
        {
            var list = new List<T>();
            Collect(transform, includeInactive, list);
            return list.ToArray();
        }
        static void Collect<T>(Transform t, bool includeInactive, List<T> into) where T : class
        {
            var go = t.gameObjectInternal!;
            if (!includeInactive && !go.activeSelf) return;
            foreach (var c in go.components) if (c is T x && !c.destroyed) into.Add(x);
            foreach (var child in t.children) Collect(child, includeInactive, into);
        }

        internal override void OnDestroyInternal()
        {
            if (destroyed) return;
            base.OnDestroyInternal();
            foreach (var child in transform.children.ToArray()) child.gameObjectInternal!.OnDestroyInternal();
            foreach (var c in components) c.OnDestroyInternal();
            transform.SetParentRaw(null);
            Live.Remove(this);
            UnityBridge.MarkHierarchyChanged();
        }

        internal override Object CloneInternal(Transform? parent, bool worldSpace)
        {
            var clone = CloneTree(this);
            clone.transform.SetParent(parent, worldSpace);
            return clone;
        }

        static GameObject CloneTree(GameObject source)
        {
            var clone = new GameObject(source.name) { layer = source.layer, tag = source.tag };
            clone.activeSelfValue = source.activeSelfValue;
            clone.transform.localPosition = source.transform.localPosition;
            clone.transform.localRotation = source.transform.localRotation;
            clone.transform.localScale = source.transform.localScale;
            foreach (var c in source.components)
            {
                if (c is Transform || c.destroyed) continue;
                var copy = c.CloneInternalComponent();
                copy.gameObjectInternal = clone;
                clone.components.Add(copy);
                copy.OnAddedInternal();
            }
            foreach (var child in source.transform.children)
            {
                var childClone = CloneTree(child.gameObjectInternal!);
                childClone.transform.SetParent(clone.transform, false);
            }
            return clone;
        }

        public static GameObject? Find(string name) => null;
    }

    public class Component : Object
    {
        internal GameObject? gameObjectInternal;
        public GameObject gameObject => gameObjectInternal!;
        public Transform transform => gameObjectInternal!.transform;
        public new string name { get => gameObjectInternal?.name ?? ""; set { if (gameObjectInternal != null) gameObjectInternal.name = value; } }
        public T? GetComponent<T>() where T : class => gameObjectInternal!.GetComponent<T>();
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : class => gameObjectInternal!.GetComponentsInChildren<T>(includeInactive);
        public T? GetComponentInChildren<T>(bool includeInactive = false) where T : class => gameObjectInternal!.GetComponentInChildren<T>(includeInactive);
        internal virtual void OnAddedInternal() { }
        internal virtual Component CloneInternalComponent() => (Component)MemberwiseClone();
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; } = true;
        public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy;
    }

    /// <summary>
    /// A script component. Unity calls its Update/LateUpdate/OnGUI/OnDestroy by name; UnityBridge does the same
    /// (found once per type by reflection), from <see cref="UnityBridge.Tick"/>, <see cref="UnityBridge.Sync"/> and
    /// <see cref="GUI"/>'s pass.
    /// </summary>
    public class MonoBehaviour : Behaviour
    {
        internal override void OnAddedInternal() => UnityBridge.Register(this);
        internal override void OnDestroyInternal()
        {
            if (destroyed) return;
            base.OnDestroyInternal();
            UnityBridge.Unregister(this);
            UnityBridge.Invoke(this, "OnDestroy");
        }
    }

    public sealed class Transform : Component, IEnumerable<Transform>
    {
        internal Transform? parentInternal;
        internal readonly List<Transform> children = new List<Transform>();
        Vector3 localPositionValue;
        Quaternion localRotationValue = Quaternion.identity;
        Vector3 localScaleValue = Vector3.one;

        /// <summary>Bumped whenever this transform or any ancestor moves, so UnityBridge re-pushes only what moved.</summary>
        internal int version;
        static int versionCounter;

        internal Transform() { }
        internal GameObject? gameObjectInternalAlias => gameObjectInternal;

        void Touch()
        {
            version = ++versionCounter;
            foreach (var c in children) c.Touch();
        }

        internal int WorldVersion
        {
            get
            {
                int v = version;
                for (var p = parentInternal; p != null; p = p.parentInternal) if (p.version > v) v = p.version;
                return v;
            }
        }

        public Transform? parent { get => parentInternal; set => SetParent(value, true); }
        public Transform root
        {
            get
            {
                var t = this;
                while (t.parentInternal != null) t = t.parentInternal;
                return t;
            }
        }
        public int childCount => children.Count;
        public Transform GetChild(int index) => children[index];
        public IEnumerator<Transform> GetEnumerator() => children.ToList().GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public Transform? Find(string n)
        {
            foreach (var c in children) if (c.gameObjectInternal!.name == n) return c;
            return null;
        }
        public void SetAsLastSibling() { }

        public void SetParent(Transform? p) => SetParent(p, true);
        public void SetParent(Transform? p, bool worldPositionStays)
        {
            if (worldPositionStays)
            {
                var pos = position;
                var rot = rotation;
                var scale = lossyScale;
                SetParentRaw(p);
                position = pos;
                rotation = rot;
                if (p == null) localScale = scale;
                else
                {
                    var ps = p.lossyScale;
                    localScale = new Vector3(Div(scale.x, ps.x), Div(scale.y, ps.y), Div(scale.z, ps.z));
                }
            }
            else
            {
                SetParentRaw(p);
            }
            Touch();
            UnityBridge.MarkHierarchyChanged();
        }
        static float Div(float a, float b) => b != 0 ? a / b : 0;

        internal void SetParentRaw(Transform? p)
        {
            parentInternal?.children.Remove(this);
            parentInternal = p;
            p?.children.Add(this);
        }

        public Vector3 localPosition { get => localPositionValue; set { localPositionValue = value; Touch(); } }
        public Quaternion localRotation { get => localRotationValue; set { localRotationValue = value; Touch(); } }
        public Vector3 localScale { get => localScaleValue; set { localScaleValue = value; Touch(); } }
        public Vector3 localEulerAngles { get => localRotationValue.eulerAngles; set => localRotation = Quaternion.Euler(value); }
        public Vector3 eulerAngles { get => rotation.eulerAngles; set => rotation = Quaternion.Euler(value); }

        public Vector3 position
        {
            get => parentInternal == null ? localPositionValue : parentInternal.TransformPoint(localPositionValue);
            set => localPosition = parentInternal == null ? value : parentInternal.InverseTransformPoint(value);
        }
        public Quaternion rotation
        {
            get => parentInternal == null ? localRotationValue : parentInternal.rotation * localRotationValue;
            set => localRotation = parentInternal == null ? value : Quaternion.Inverse(parentInternal.rotation) * value;
        }
        public Vector3 lossyScale
        {
            get
            {
                var s = localScaleValue;
                for (var p = parentInternal; p != null; p = p.parentInternal) s = Vector3.Scale(s, p.localScaleValue);
                return s;
            }
        }

        public void SetPositionAndRotation(Vector3 p, Quaternion r) { position = p; rotation = r; }
        public void SetLocalPositionAndRotation(Vector3 p, Quaternion r) { localPositionValue = p; localRotationValue = r; Touch(); }

        public Vector3 forward { get => rotation * Vector3.forward; set => rotation = Quaternion.LookRotation(value); }
        public Vector3 up { get => rotation * Vector3.up; set => rotation = Quaternion.FromToRotation(Vector3.up, value); }
        public Vector3 right { get => rotation * Vector3.right; set => rotation = Quaternion.FromToRotation(Vector3.right, value); }

        public Vector3 TransformPoint(Vector3 p)
        {
            var t = this;
            while (t != null)
            {
                p = t.localRotationValue * Vector3.Scale(p, t.localScaleValue) + t.localPositionValue;
                t = t.parentInternal;
            }
            return p;
        }
        public Vector3 InverseTransformPoint(Vector3 p)
        {
            var local = parentInternal == null ? p : parentInternal.InverseTransformPoint(p);
            local = Quaternion.Inverse(localRotationValue) * (local - localPositionValue);
            return new Vector3(Div(local.x, localScaleValue.x), Div(local.y, localScaleValue.y), Div(local.z, localScaleValue.z));
        }
        public Vector3 TransformDirection(Vector3 d) => rotation * d;
        public Vector3 InverseTransformDirection(Vector3 d) => Quaternion.Inverse(rotation) * d;
        public Vector3 TransformVector(Vector3 v)
        {
            var t = this;
            while (t != null)
            {
                v = t.localRotationValue * Vector3.Scale(v, t.localScaleValue);
                t = t.parentInternal;
            }
            return v;
        }

        public void LookAt(Vector3 worldPosition) => LookAt(worldPosition, Vector3.up);
        public void LookAt(Vector3 worldPosition, Vector3 worldUp) => rotation = Quaternion.LookRotation(worldPosition - position, worldUp);
        public void LookAt(Transform target) => LookAt(target.position);
        public void Rotate(Vector3 eulers) => localRotation = localRotationValue * Quaternion.Euler(eulers);
        public void Rotate(float x, float y, float z) => Rotate(new Vector3(x, y, z));
        public void Translate(Vector3 t) => position += rotation * t;

        internal GameObject? gameObjectInternalPublic => gameObjectInternal;
        internal override Component CloneInternalComponent() => throw new InvalidOperationException();
    }

    /// <summary>Unity's frame clock, advanced by <see cref="UnityBridge.Tick"/>.</summary>
    public static class Time
    {
        public static float time { get; internal set; }
        public static float unscaledTime { get; internal set; }
        public static float deltaTime { get; internal set; }
        public static float unscaledDeltaTime { get; internal set; }
        public static float smoothDeltaTime => deltaTime;
        public static float timeScale { get; set; } = 1f;
        public static int frameCount { get; internal set; }
        public static double timeAsDouble => time;
        public static double unscaledTimeAsDouble => unscaledTime;
        public static float realtimeSinceStartup => (float)Clock.Elapsed.TotalSeconds;
        static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    }

    /// <summary>UnityEngine.Random: a process-wide generator (Unity's xorshift128 is not reproduced; nothing the
    /// views draw from it is part of the simulation, which uses its own seeded System.Random).</summary>
    public static class Random
    {
        static System.Random rng = new System.Random();
        public static void InitState(int seed) => rng = new System.Random(seed);
        public static float value => (float)rng.NextDouble();
        public static float Range(float min, float max) => min + (float)rng.NextDouble() * (max - min);
        public static int Range(int min, int max) => max <= min ? min : rng.Next(min, max);
        public static Vector3 insideUnitSphere
        {
            get
            {
                while (true)
                {
                    var v = new Vector3(Range(-1f, 1f), Range(-1f, 1f), Range(-1f, 1f));
                    if (v.sqrMagnitude <= 1) return v;
                }
            }
        }
        public static Vector3 onUnitSphere => insideUnitSphere.normalized is var n && n.sqrMagnitude > 0 ? n : Vector3.up;
        public static Vector2 insideUnitCircle
        {
            get
            {
                while (true)
                {
                    var v = new Vector2(Range(-1f, 1f), Range(-1f, 1f));
                    if (v.sqrMagnitude <= 1) return v;
                }
            }
        }
        public static Quaternion rotation => Quaternion.Euler(Range(0f, 360f), Range(0f, 360f), Range(0f, 360f));
    }

    /// <summary>Unity's log, routed to Unreal's (category LogSpaceGod).</summary>
    public static class Debug
    {
        public static void Log(object message) => UnityBridge.Log(message?.ToString() ?? "null", 0);
        public static void LogWarning(object message) => UnityBridge.Log(message?.ToString() ?? "null", 1);
        public static void LogError(object message) => UnityBridge.Log(message?.ToString() ?? "null", 2);
        public static void LogException(Exception e) => UnityBridge.Log(e.ToString(), 2);
        public static void Assert(bool condition, string message = "") { if (!condition) LogError("Assertion failed: " + message); }
    }

    public static class Application
    {
        /// <summary>Where Unity keeps a player's data; here the project's Saved folder, which a packaged game keeps
        /// per user as Unity's persistentDataPath is.</summary>
        public static string persistentDataPath => UnityBridge.PersistentDataPath;
        public static bool isPlaying => true;
        public static bool isEditor => false;
        public static string version => "1.0";

        /// <summary>Unity's StreamingAssets (files shipped loose beside the game): here Content/Capture, which DefaultGame.ini
        /// stages as non-UFS files, so a packaged game reads them from disk as Unity's player does (US-T72: the shipped
        /// SpaceGod.capture.json).</summary>
        public static string streamingAssetsPath => System.IO.Path.Combine(UnrealSharp.Engine.SystemLibrary.ProjectContentDirectory, "Capture");

        /// <summary>The frame cap (-1 none); the host applies it as t.MaxFPS (US-T26: EngineSettings.FrameRateCap).</summary>
        public static int targetFrameRate { get; set; } = -1;

        /// <summary>Set by the host: ends the game (US-T26).</summary>
        public static Action QuitHandler = () => { };
        public static void Quit() => QuitHandler();
    }
}
