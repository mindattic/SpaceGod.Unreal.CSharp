using System;
using System.Collections.Generic;
using System.Reflection;
using UnrealSharp.Core;
using UnrealSharp.CoreUObject;
using UnrealSharp.Engine;
using UnrealSharp.Log;
using UnrealSharp.ProceduralMeshComponent;

namespace UnityEngine
{
    /// <summary>
    /// Where the Unity facade meets Unreal (US-T27). The app calls <see cref="Begin"/> once with an actor to hang the
    /// scene's primitives on, then each frame <see cref="Tick"/> (clock, finished loads, particles, MonoBehaviour.Update)
    /// before its own Update and <see cref="Sync"/> after it (LateUpdate; then every Unity renderer that is enabled,
    /// active and has something to draw gets an Unreal primitive whose transform, mesh, materials and visibility are
    /// brought up to date, only where they changed). Unity's scene is camera-relative, so the Unreal camera stays at
    /// the origin looking where Camera.main does.
    /// </summary>
    public static class UnityBridge
    {
        public const string LogCategory = "LogSpaceGod";

        static AActor? host;
        static readonly List<Renderer> renderers = new List<Renderer>();
        static readonly HashSet<Renderer> rendererSet = new HashSet<Renderer>();
        static readonly List<MonoBehaviour> behaviours = new List<MonoBehaviour>();
        static readonly List<ParticleSystem> particleSystems = new List<ParticleSystem>();
        static readonly List<Action> pendingLoads = new List<Action>();
        static int hierarchyVersion;
        static UMaterialParameterCollection? globals;
        static bool globalsLooked;
        static int globalsPushed = -1;

        /// <summary>Whether the Unreal side exists (false in a pure .NET run: everything then stays managed data).</summary>
        public static bool HasHost => host != null;
        public static AActor? Host => host;

        /// <summary>Procedural draws (Graphics.RenderPrimitives) by shader name: AsteroidBelt's GPU-propagated belt.</summary>
        public static readonly Dictionary<string, Action<RenderParams, MeshTopology, int>> ProceduralDrawers =
            new Dictionary<string, Action<RenderParams, MeshTopology, int>>(StringComparer.Ordinal)
            {
                ["Asteroids"] = ProceduralAsteroids.Draw,
            };

        /// <summary>Hangs the scene's Unreal primitives on <paramref name="actor"/> (it gets a root at the origin if it has none).</summary>
        public static void Begin(AActor actor)
        {
            // Not on the given actor itself: a game mode (an AInfo) is hidden in game, and so would be everything on it.
            // The scene gets its own plain actor at the origin, owned by it.
            var scene = UObject.SpawnActor<AActor>(new UnrealSharp.TSubclassOf<AActor>(), new FTransform(),
                ESpawnActorCollisionHandlingMethod.AlwaysSpawn, null, actor);
            host = scene ?? actor;
            // Unity's Application.Quit ends the player (the -demo tour, the screenshot tours, the menus' Quit): the
            // handler was never set, so the game stayed up after them (US-T40).
            Application.QuitHandler = () => SystemLibrary.ExecuteConsoleCommand("quit");
            if (host.RootComponent == null)
                host.AddComponentByClass<USceneComponent>(false, new FTransform());
        }

        public static void Log(string message, int severity)
        {
            switch (severity)
            {
                case 2: UnrealLogger.LogError(LogCategory, message); break;
                case 1: UnrealLogger.LogWarning(LogCategory, message); break;
                default: UnrealLogger.Log(LogCategory, message); break;
            }
            MirrorLog(message, severity);
        }

        static readonly object mirrorGate = new object();
        static System.IO.StreamWriter? mirror;
        static bool mirrorChecked;

        /// <summary>US-T72: <c>-SpaceGodLog=&lt;file&gt;</c> also appends every Debug line to that file. A Shipping package
        /// compiles Unreal's log out, so this is how a packaged game's own log (the Unity player log's lines, such as
        /// docs/CAPTURE.md's <c>[capture] event</c> ones) can be read; tools/play.ps1 -Mode coop-smoke -CaptureHost uses it.</summary>
        static void MirrorLog(string message, int severity)
        {
            lock (mirrorGate)
            {
                if (!mirrorChecked)
                {
                    mirrorChecked = true;
                    foreach (var a in Environment.GetCommandLineArgs())
                        if (a.StartsWith("-SpaceGodLog=", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                var path = a.Substring("-SpaceGodLog=".Length).Trim('"');
                                var dir = System.IO.Path.GetDirectoryName(path);
                                if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
                                mirror = new System.IO.StreamWriter(path, true) { AutoFlush = true };
                            }
                            catch (Exception) { mirror = null; }
                        }
                }
                if (mirror == null) return;
                try { mirror.WriteLine((severity == 2 ? "Error: " : severity == 1 ? "Warning: " : "") + message); }
                catch (Exception) { mirror = null; }
            }
        }

        public static string PersistentDataPath
        {
            get
            {
                try { return System.IO.Path.Combine(SystemLibrary.ProjectSavedDirectory, "SpaceGod"); }
                catch { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Unreal.SpaceGod"); }
            }
        }

        internal static void MarkHierarchyChanged() => hierarchyVersion++;

        internal static void Register(Renderer r) { if (rendererSet.Add(r)) renderers.Add(r); }
        internal static void Unregister(Renderer r)
        {
            if (!rendererSet.Remove(r)) return;
            renderers.Remove(r);
            r.Primitive?.Destroy();
            r.Primitive = null;
        }
        internal static void Register(MonoBehaviour b) => behaviours.Add(b);
        internal static void Unregister(MonoBehaviour b) => behaviours.Remove(b);
        internal static void Register(ParticleSystem p) => particleSystems.Add(p);
        internal static void Unregister(ParticleSystem p) => particleSystems.Remove(p);

        /// <summary>Runs <paramref name="complete"/> on the game thread at the next <see cref="Tick"/> (finished async loads).</summary>
        internal static void Post(Action complete) { lock (pendingLoads) pendingLoads.Add(complete); }

        // ---------------------------------------------------------------- MonoBehaviour messages

        static readonly Dictionary<(Type, string), MethodInfo?> Messages = new Dictionary<(Type, string), MethodInfo?>();
        internal static void Invoke(MonoBehaviour b, string message)
        {
            var key = (b.GetType(), message);
            if (!Messages.TryGetValue(key, out var m))
                Messages[key] = m = b.GetType().GetMethod(message, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (m == null) return;
            try { m.Invoke(b, null); }
            catch (TargetInvocationException e) { Debug.LogException(e.InnerException ?? e); }
        }

        /// <summary>The OnGUI of every live MonoBehaviour that has one (AsteroidBelt's labels), for the IMGUI host (US-T28's
        /// ImguiHost.Frame) to run with the app's own.</summary>
        public static List<Action> GuiHandlers()
        {
            var list = new List<Action>();
            foreach (var b in behaviours)
            {
                if (!b.enabled || !b.gameObject.activeInHierarchy) continue;
                var m = b.GetType().GetMethod("OnGUI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (m != null) list.Add((Action)Delegate.CreateDelegate(typeof(Action), b, m));
            }
            return list;
        }

        // ---------------------------------------------------------------- per frame

        /// <summary>Start of a frame: Unity's clock, loads that finished, particles, then MonoBehaviour.Update.</summary>
        public static void Tick(float realDeltaSeconds)
        {
            tickStart = System.Diagnostics.Stopwatch.GetTimestamp();
            Time.unscaledDeltaTime = realDeltaSeconds;
            Time.unscaledTime += realDeltaSeconds;
            Time.deltaTime = realDeltaSeconds * Time.timeScale;
            Time.time += Time.deltaTime;
            Time.frameCount++;

            Action[] done;
            lock (pendingLoads)
            {
                done = pendingLoads.ToArray();
                pendingLoads.Clear();
            }
            foreach (var a in done)
            {
                try { a(); }
                catch (Exception e) { Debug.LogException(e); }
            }

            foreach (var p in particleSystems.ToArray())
                if (p.gameObject.activeInHierarchy) p.Simulate(Time.deltaTime);
            foreach (var b in behaviours.ToArray())
                if (b.enabled && b.gameObject.activeInHierarchy) Invoke(b, "Update");
            float ms = (float)System.Diagnostics.Stopwatch.GetElapsedTime(tickStart).TotalMilliseconds;
            Timing.UpdateMs = Timing.UpdateMs * 0.9f + ms * 0.1f;
            Timing.FrameMs = Timing.FrameMs * 0.9f + realDeltaSeconds * 1000f * 0.1f;
        }

        /// <summary>Smoothed frame times (US-T40): real frame, every MonoBehaviour.Update, and Sync.</summary>
        public struct FrameTiming
        {
            public float FrameMs, UpdateMs, SyncMs;
            public override string ToString() =>
                string.Format(System.Globalization.CultureInfo.InvariantCulture, "frame {0:F1} ms ({1:F1} fps), Update {2:F1} ms, Sync {3:F1} ms",
                    FrameMs, FrameMs > 0 ? 1000f / FrameMs : 0, UpdateMs, SyncMs);
        }

        public static FrameTiming Timing;
        static long tickStart;

        public struct Stats
        {
            public int Renderers, Primitives, Visible, Triangles, MeshUploads, MaterialInstances;
            public override string ToString() =>
                $"{Renderers} renderers, {Primitives} Unreal primitives ({Visible} visible, {Triangles} triangles), {MeshUploads} mesh uploads, {MaterialInstances} material instances";
        }

        public static Stats Last;
        static int meshUploads, materialInstances;

        /// <summary>End of a frame (Unity's LateUpdate and render): pushes everything drawn to Unreal.</summary>
        public static Stats Sync()
        {
            long syncStart = System.Diagnostics.Stopwatch.GetTimestamp();
            foreach (var b in behaviours.ToArray())
                if (b.enabled && b.gameObject.activeInHierarchy) Invoke(b, "LateUpdate");

            ProceduralAsteroids.EndFrame();
            screenCamera = FindScreenCamera();
            var cam = screenCamera;
            var camPos = cam != null ? cam.transform.position : Vector3.zero;
            var camRot = cam != null ? cam.transform.rotation : Quaternion.identity;
            PushGlobals();
            // Unity's Light components reach only URP Lit materials (M_Lit), per renderer layer (US-T57).
            UnityLights.BeginFrame(captureCamera ?? cam);
            // What the screen shows is what its camera renders: the layers in its culling mask (ModelViewer's camera
            // sees only its own layer) over its clear colour (Backdrop); a Camera.Render capture adds its own mask.
            int mask = (cam?.cullingMask ?? -1) | captureMask;
            Backdrop.Update(captureCamera ?? cam);

            var s = new Stats { Renderers = renderers.Count };
            meshUploads = 0;
            foreach (var r in renderers)
            {
                if (r is ParticleSystemRenderer pr && r.enabled && r.gameObject.activeInHierarchy) pr.Rebuild(camPos, camRot);
                var mesh = r.DrawMesh;
                bool want = r.enabled && mesh != null && mesh.vertexCount + (mesh.StaticMesh != null ? 1 : 0) > 0 && r.gameObject.activeInHierarchy
                    && (Backdrop.Is(r) || ((mask >> (r.gameObject.layer & 31)) & 1) != 0);
                if (!want)
                {
                    r.Primitive?.SetVisible(false);
                    continue;
                }
                if (host == null) { s.Visible++; s.Triangles += mesh!.StaticMesh == null ? mesh.Unreal.TriangleCount : 0; continue; }
                r.Primitive ??= new Primitive(r);
                if (r.Primitive.Update(r, mesh!))
                {
                    s.Visible++;
                    s.Triangles += r.Primitive.Triangles;
                }
            }
            foreach (var r in renderers) if (r.Primitive != null) s.Primitives++;
            s.MeshUploads = meshUploads;
            s.MaterialInstances = materialInstances;
            Last = s;
            if (captureCamera == null)
                Timing.SyncMs = Timing.SyncMs * 0.9f + (float)System.Diagnostics.Stopwatch.GetElapsedTime(syncStart).TotalMilliseconds * 0.1f;
            return s;
        }

        static Camera? screenCamera, captureCamera;
        static int captureMask;

        /// <summary>The camera that draws the screen, as Unity picks it: of the enabled, active cameras without a target
        /// texture, the one with the highest depth (the model viewer's, depth 1000, over the game's); Camera.main if
        /// there is none.</summary>
        public static Camera? ScreenCamera => FindScreenCamera();

        static Camera? FindScreenCamera()
        {
            Camera? best = null;
            for (int i = Camera.All.Count - 1; i >= 0; i--)
            {
                var c = Camera.All[i];
                if (c.destroyed) { Camera.All.RemoveAt(i); continue; }
                if (!c.enabled || c.targetTexture != null || !c.gameObject.activeInHierarchy) continue;
                if (best == null || c.depth > best.depth || (c.depth == best.depth && ReferenceEquals(c, Camera.main))) best = c;
            }
            return best ?? Camera.main;
        }

        /// <summary>A camera's clear colour (CameraClearFlags.SolidColor, not black, which is what Unreal clears to):
        /// a Sky cube on a 1x1 texture of that colour, pushed to the far distance around whichever view draws it.</summary>
        static class Backdrop
        {
            static GameObject? go;
            static MeshRenderer? renderer;
            static Material? material;
            static (Color, bool) shown = (new Color(-1, -1, -1, -1), false);

            public static bool Is(Renderer r) => ReferenceEquals(r, renderer);

            static float Encode(float c) => c <= 0.0031308f ? c * 12.92f : 1.055f * Mathf.Pow(c, 1f / 2.4f) - 0.055f;
            public static Renderer? Renderer => renderer;

            public static void Update(Camera? cam)
            {
                var c = cam != null && cam.clearFlags == CameraClearFlags.SolidColor ? cam.backgroundColor : Color.black;
                bool want = host != null && c.r + c.g + c.b > 0;
                if (!want)
                {
                    if (renderer != null) renderer.enabled = false;
                    return;
                }
                if (go == null)
                {
                    go = new GameObject("Camera backdrop") { hideFlags = HideFlags.HideAndDontSave };
                    go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
                    renderer = go.AddComponent<MeshRenderer>();
                    material = new Material(Shader.Find("Unity.SpaceGod/Sky")) { name = "Backdrop" };
                    material.SetFloat("_Brightness", 1f);
                    renderer.sharedMaterial = material;
                }
                go.transform.position = cam!.transform.position;
                if (shown != (c, captureCamera != null))
                {
                    shown = (c, captureCamera != null);
                    var t = new Texture2D(1, 1) { name = "Backdrop" };
                    // The texel is chosen so the screen shows Unity's value (its gamma colour). On screen, under the
                    // render backend's look (manual exposure cancelled by SG_Expose, no tone curve, no vignette), M_Sky
                    // draws its texel as it is (US-T57; the transfer US-T40 measured, a * decoded^g with a 10.57, g 1.432,
                    // was Unreal's filmic curve before US-T45 bound the look, and lifted the clear colour ~3x since). A
                    // capture still goes through the measured transfer (a 13/15/23 texel showed as 1/1/1; 63 as 22).
                    bool capture = captureCamera != null;
                    const float a = 3.36f, g = 1.224f;
                    float Texel(float v) => capture ? Encode(Mathf.Pow(Mathf.Max(v, 0f) / a, 1f / g)) : Mathf.Max(v, 0f);
                    t.SetPixel(0, 0, new Color(Texel(c.r), Texel(c.g), Texel(c.b), 1));
                    t.Apply();
                    material!.SetTexture("_MainTex", t);
                }
                renderer!.enabled = true;
            }
        }

        static UProceduralMeshComponent? keeper;
        static int keptSlots;

        /// <summary>Unreal's GC does not see C# references: a shared material instance that no component uses directly
        /// (only property-block children, or per-chunk ones) would be collected under them. A hidden component holds every
        /// shared instance in its material slots.</summary>
        static void Keep(UMaterialInstanceDynamic mid, int slot)
        {
            if (host == null) return;
            keeper ??= host.AddComponentByClass<UProceduralMeshComponent>(false, new FTransform(), c =>
            {
                c.CastShadow = false;
                c.CollisionEnabled = ECollisionEnabled.NoCollision;
                c.SetVisibility(false);
            });
            keeper.SetMaterial(slot, mid);
        }

        /// <summary>Unity's shader globals (Shader.SetGlobal*) to MPC_SpaceGod. Unity's Light components never touch them
        /// (the game's shaders light themselves from these; Lights reach only URP Lit, UnityLights, US-T57).</summary>
        static void PushGlobals()
        {
            if (globalsPushed == Shader.globalsVersion) return;
            globalsPushed = Shader.globalsVersion;
            if (host == null) return;
            if (!globalsLooked)
            {
                globalsLooked = true;
                try { globals = UObject.StaticLoadObject<UMaterialParameterCollection>(ShaderBinding.GlobalsCollection); }
                catch { globals = null; }
            }
            if (globals == null) return;
            foreach (var kv in Shader.GlobalVectors)
                MaterialLibrary.SetVectorParameterValue(globals, new FName(kv.Key), ShaderBinding.ToUnreal(kv.Key, kv.Value, Shader.GlobalColorNames.Contains(kv.Key)));
            foreach (var kv in Shader.GlobalFloats)
                MaterialLibrary.SetScalarParameterValue(globals, new FName(kv.Key), kv.Value);
        }

        internal static UMaterialInstanceDynamic? InstanceOf(Material m)
        {
            if (host == null) return null;
            if (m.Instance == null)
            {
                var parent = ShaderBinding.Parent(m.shader);
                if (parent == null) return null;
                m.Instance = MaterialLibrary.CreateDynamicMaterialInstance(parent, new FName(m.name.Length > 0 ? Sanitize(m.name) : "Material"));
                m.pushedVersion = -1;
                Keep(m.Instance, keptSlots++);
                materialInstances++;
            }
            if (m.pushedVersion != m.version)
            {
                ShaderBinding.Push(m, m.Instance);
                m.pushedVersion = m.version;
            }
            return m.Instance;
        }

        static string Sanitize(string n)
        {
            var c = n.ToCharArray();
            for (int i = 0; i < c.Length; i++) if (!char.IsLetterOrDigit(c[i])) c[i] = '_';
            return new string(c);
        }

        /// <summary>One Unity renderer's Unreal primitive.</summary>
        internal sealed class Primitive
        {
            UPrimitiveComponent? component;
            UProceduralMeshComponent? procedural;
            UStaticMeshComponent? staticMesh;
            Mesh? shownMesh;
            int shownMeshVersion = -1, shownTransform = -1, shownMaterials = -1, shownBlock = -1, sections;
            double shownLineScale = 1; // US-T45: UnrealMeshData.LineScale the transform was pushed with
            readonly List<(UMaterialInstanceDynamic? Mid, Material? Of, int Version)> blockInstances = new List<(UMaterialInstanceDynamic?, Material?, int)>();
            bool visible;
            public int Triangles;
            public UPrimitiveComponent? Component => component;
            public bool Visible => visible && component != null;

            public Primitive(Renderer r) { }

            public void SetVisible(bool v)
            {
                if (visible == v || component == null) return;
                visible = v;
                component.SetVisibility(v);
            }

            public void Destroy()
            {
                try { component?.DestroyComponent(component); } catch { }
                component = null;
            }

            /// <summary>Brings the primitive up to date; false if it has nothing to show.</summary>
            public bool Update(Renderer r, Mesh mesh)
            {
                bool isStatic = mesh.StaticMesh != null;
                if (component == null || (isStatic ? staticMesh == null : procedural == null))
                {
                    Destroy();
                    var h = host!;
                    if (isStatic)
                        component = staticMesh = h.AddComponentByClass<UStaticMeshComponent>(false, new FTransform(), c =>
                        {
                            c.CastShadow = false;
                            c.CollisionEnabled = ECollisionEnabled.NoCollision;
                        });
                    else
                        component = procedural = h.AddComponentByClass<UProceduralMeshComponent>(false, new FTransform(), c =>
                        {
                            c.CastShadow = false;
                            c.CollisionEnabled = ECollisionEnabled.NoCollision;
                        });
                    shownMesh = null;
                    shownMeshVersion = shownTransform = shownMaterials = shownBlock = -1;
                    visible = true;
                }

                // Mesh.
                if (!ReferenceEquals(shownMesh, mesh) || shownMeshVersion != mesh.version)
                {
                    shownMesh = mesh;
                    shownMeshVersion = mesh.version;
                    shownMaterials = -1;
                    meshUploads++;
                    if (isStatic)
                    {
                        staticMesh!.SetStaticMesh(mesh.StaticMesh!);
                        sections = Math.Max(1, r.RawMaterials.Length);
                        Triangles = 0;
                    }
                    else
                    {
                        var data = mesh.Unreal;
                        procedural!.ClearAllMeshSections();
                        int i = 0;
                        foreach (var s in data.Sections)
                        {
                            if (s.Vertices.Count > 0 && s.Triangles.Count > 0)
                                procedural.CreateMeshSection(i, s.Vertices, s.Triangles, s.Normals, s.Uv0, s.Uv1, s.Uv2, s.Uv3, s.Colors,
                                    new List<FProcMeshTangent>(), false);
                            i++;
                        }
                        sections = data.Sections.Count;
                        Triangles = data.TriangleCount;
                    }
                }

                // Transform: Unity world -> Unreal world (identity for geometry already in world space).
                int tv = r.WorldSpaceGeometry ? 0 : r.transform.version;
                double lineScale = isStatic ? 1 : mesh.Unreal.LineScale;
                if (tv != shownTransform || lineScale != shownLineScale)
                {
                    shownTransform = tv;
                    shownLineScale = lineScale;
                    FTransform t;
                    if (r.WorldSpaceGeometry) t = lineScale == 1 ? new FTransform() : UnrealSpace.ToUnreal(Vector3.zero, Quaternion.identity, Vector3.one * (float)lineScale);
                    else
                    {
                        var rot = r.transform.rotation;
                        var scale = r.transform.lossyScale;
                        if (mesh.Correction is { } c)
                        {
                            rot = rot * c.Rotation;
                            scale = Vector3.Scale(scale, c.Scale);
                        }
                        if (lineScale != 1) scale *= (float)lineScale; // the line mesh was divided by it (UnrealMeshData.LineScale)
                        t = UnrealSpace.ToUnreal(r.transform.position, rot, scale);
                    }
                    component!.SetWorldTransform(t, false, out _, true);
                }

                // Materials (+ the renderer's property block on per-primitive instances).
                if (shownMaterials != r.materialsVersion + MaterialsStamp(r) || shownBlock != r.blockVersion)
                {
                    shownMaterials = r.materialsVersion + MaterialsStamp(r);
                    shownBlock = r.blockVersion;
                    var mats = r.RawMaterials;
                    var first = mats.Length > 0 ? mats[0] : null;
                    if (first != null)
                    {
                        // Unity's render queue -> translucency sort; the far field (sky, stars) is never culled.
                        component!.TranslucencySortPriority = ShaderBinding.SortPriority(first.shader);
                        if (ShaderBinding.IsFarField(first.shader)) component.BoundsScale = 1e6f;
                    }
                    for (int i = 0; i < sections && mats.Length > 0; i++)
                    {
                        var m = mats[Math.Min(i, mats.Length - 1)];
                        if (m == null) continue;
                        var parent = InstanceOf(m);
                        if (parent == null) continue;
                        if (r.Block == null)
                        {
                            component!.SetMaterial(i, parent);
                            continue;
                        }
                        while (blockInstances.Count <= i) blockInstances.Add((null, null, -1));
                        // A MaterialPropertyBlock = the shared material's values with the block's on top, on a per-primitive
                        // instance of the shader's parent material. Not parented to the shared instance: Unreal accepts only
                        // a Material or a MaterialInstanceConstant as a parent (an MID parent is refused, leaving the
                        // instance parentless, which crashes the renderer, US-T40).
                        var (mid, ofMaterial, _) = blockInstances[i];
                        if (mid == null || !ReferenceEquals(ofMaterial, m))
                        {
                            var root = ShaderBinding.Parent(m.shader);
                            if (root == null) continue;
                            mid = MaterialLibrary.CreateDynamicMaterialInstance(root, new FName("Block"));
                            materialInstances++;
                        }
                        ShaderBinding.Push(m, mid);
                        ShaderBinding.Push(r.Block, mid);
                        blockInstances[i] = (mid, m, m.version);
                        component!.SetMaterial(i, mid);
                    }
                }
                else
                {
                    // Shared materials whose values moved since: push them (primitives using them directly follow their
                    // instance; block instances take the material's values again, then the block's on top).
                    foreach (var m in r.RawMaterials) if (m != null && m.pushedVersion != m.version) InstanceOf(m);
                    for (int i = 0; i < blockInstances.Count; i++)
                    {
                        var (mid, m, pushed) = blockInstances[i];
                        if (mid == null || m == null || pushed == m.version || r.Block == null) continue;
                        ShaderBinding.Push(m, mid);
                        ShaderBinding.Push(r.Block, mid);
                        blockInstances[i] = (mid, m, m.version);
                    }
                }

                // Unity's lights on URP Lit materials: those whose culling mask has this renderer's layer (US-T57).
                for (int i = 0; i < sections && r.RawMaterials.Length > 0; i++)
                {
                    var m = r.RawMaterials[Math.Min(i, r.RawMaterials.Length - 1)];
                    if (!UnityLights.IsLit(m)) continue;
                    var mid = r.Block == null ? m!.Instance : i < blockInstances.Count ? blockInstances[i].Mid : null;
                    if (mid != null) UnityLights.Apply(mid, r.gameObject.layer);
                }

                SetVisible(true);
                return true;
            }

            static int MaterialsStamp(Renderer r)
            {
                // A material made into an instance after the renderer was first shown re-binds once.
                int stamp = 0;
                foreach (var m in r.RawMaterials) if (m?.Instance != null) stamp += 1 << 20;
                return stamp;
            }
        }

        // ---------------------------------------------------------------- textures, cameras, procedural draws

        /// <summary>An Unreal texture made from RGBA pixels (a swatch, a seeded sky, an icon): encoded as a PNG (alpha
        /// kept) and handed to RenderingLibrary.ImportBufferAsTexture2D, the runtime import UnrealSharp exposes.</summary>
        internal static UTexture2D? TextureFromPixels(int width, int height, Color32[] pixels)
        {
            if (host == null) return null;
            try { return RenderingLibrary.ImportBufferAsTexture2D(Png.Encode(width, height, pixels)); }
            catch (Exception e) { Log("TextureFromPixels: " + e.Message, 1); return null; }
        }
        internal static void EnsureRenderTarget(RenderTexture rt)
        {
            if (rt.Unreal != null || host == null) return;
            try { rt.Unreal = RenderingLibrary.CreateRenderTarget2D(rt.width, rt.height, ETextureRenderTargetFormat.RTF_RGBA8); }
            catch (Exception e) { Log("RenderTexture: " + e.Message, 1); }
        }

        static USceneCaptureComponent2D? capture;
        /// <summary>How many primitives the last Camera.Render capture drew (its layers + the backdrop).</summary>
        public static int LastCaptureShown;

        /// <summary>Camera.Render into its targetTexture (ShipThumbnails): a scene capture from the camera's pose.</summary>
        internal static void RenderCamera(Camera camera)
        {
            if (host == null || camera.targetTexture == null) return;
            EnsureRenderTarget(camera.targetTexture);
            if (camera.targetTexture.Unreal is not UTextureRenderTarget2D target) return;
            // The capture renders what this camera would: its culling mask's layers (ShipThumbnails: only the preview
            // hull on the ModelViewer layer) over its clear colour, nothing else of the scene.
            captureCamera = camera;
            captureMask = camera.cullingMask;
            try { Sync(); }
            finally { captureCamera = null; captureMask = 0; }
            capture ??= host.AddComponentByClass<USceneCaptureComponent2D>(false, new FTransform(), c =>
            {
                c.CaptureEveryFrame = false;
                c.CaptureOnMovement = false;
                c.PrimitiveRenderMode = ESceneCapturePrimitiveRenderMode.PRM_UseShowOnlyList;
                // The final LDR colour with the main camera's look (RenderBackend: manual exposure, linear tone curve).
                c.CaptureSource = ESceneCaptureSource.SCS_FinalColorLDR;
                var pp = c.PostProcessSettings;
                pp.Override_ToneCurveAmount = true;
                pp.ToneCurveAmount = 0f;
                pp.Override_ExpandGamut = true;
                pp.ExpandGamut = 0f;
                pp.Override_AutoExposureMethod = true;
                pp.MeteringMode = EAutoExposureMethod.AEM_Manual;
                pp.Override_AutoExposureBias = true;
                pp.ExposureCompensation = 0f;
                pp.Override_AutoExposureApplyPhysicalCameraExposure = true;
                pp.ApplyPhysicalCameraExposure = false;
                pp.Override_BloomIntensity = true;
                pp.BloomIntensity = 0f;
                pp.Override_MotionBlurAmount = true;
                pp.Amount = 0f;
                c.PostProcessSettings = pp;
                c.PostProcessBlendWeight = 1f;
            });
            // The camera's own clip planes (r.SetNearClipPlane cannot go under 1 Unreal unit; a thumbnail's camera sits
            // a hull's few units away with a near plane of 1% of its radius).
            capture.Override_CustomNearClippingPlane = true;
            capture.CustomNearClippingPlane = (float)Math.Max(camera.nearClipPlane * UnrealSpace.UnitsPerUnity, 1e-4);
            capture.MaxViewDistanceOverride = (float)(camera.farClipPlane * UnrealSpace.UnitsPerUnity);
            capture.ClearShowOnlyComponents();
            int shown = 0;
            foreach (var r in renderers)
            {
                if (r.Primitive?.Component is not { } pc || !r.Primitive.Visible) continue;
                if (!Backdrop.Is(r) && ((camera.cullingMask >> (r.gameObject.layer & 31)) & 1) == 0) continue;
                capture.ShowOnlyComponent(pc);
                shown++;
            }
            LastCaptureShown = shown;
            // Unity's fieldOfView is vertical, Unreal's horizontal; a camera's rotation maps through CameraRotationToUnreal
            float aspect = camera.targetTexture.height > 0 ? (float)camera.targetTexture.width / camera.targetTexture.height : camera.aspect;
            capture.FieldofView = 2f * Mathf.Atan(Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * aspect) * Mathf.Rad2Deg;
            capture.TextureTarget = target;
            // (Unity cameras look along +Z, Unreal's along +X).
            capture.SetWorldTransform(new FTransform(UnrealSpace.CameraRotationToUnreal(camera.transform.rotation), UnrealSpace.ToUnreal(camera.transform.position)), false, out _, true);
            capture.CaptureScene();
        }

        internal static void DrawProcedural(RenderParams rp, MeshTopology topology, int vertexCount)
        {
            if (rp.material == null) return;
            if (ProceduralDrawers.TryGetValue(ShaderBinding.ShaderName(rp.material.shader), out var draw)) draw(rp, topology, vertexCount);
        }
    }
}
