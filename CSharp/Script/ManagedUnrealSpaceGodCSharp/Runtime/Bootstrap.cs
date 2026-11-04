using System;
using Unity.Shared.Core;

namespace Unity.SpaceGod
{
    /// <summary>
    /// Makes the game playable: if no <see cref="UnitySpaceApp"/> exists once the level is up, one is created. With no
    /// flags that is all that happens. <c>-demo</c> runs the scripted tour once and quits (<see cref="CaptureRunner"/>) —
    /// the same flag name used for it in every MindAttic project. Pair it with <c>-capture</c> to record the session.
    ///
    /// Port of Runtime/Bootstrap.cs. Unity ran <see cref="EnsureApp"/> from an AfterSceneLoad hook; here the game mode
    /// calls it at BeginPlay (after <see cref="InitialiseOnce"/>, which stands for the SubsystemRegistration and
    /// BeforeSceneLoad hooks: Unity.Shared's ProfilesBootstrap and <see cref="SessionAudio"/>). Unity's two scenes
    /// (the menu, the game) are <see cref="LoadScene"/>: this game has no level content, so loading the game "scene"
    /// is making a fresh app and loading the menu is tearing it down under the menu screen.
    /// </summary>
    public static class Bootstrap
    {
        /// <summary>The app in play (null while the menu is up).</summary>
        public static UnitySpaceApp App { get; private set; }

        /// <summary>Raised when an app is made or torn down (the host wires its camera and input to it).</summary>
        public static event Action<UnitySpaceApp> AppChanged;

        static bool initialised, viewerEnsured, captureBegun;

        static bool settingsOrdered;

        /// <summary>
        /// US-T70: the settings menu lists each page in the order its settings were declared, which is the order their classes
        /// were first initialised. In Unity the URP post-processing settings live in an assembly of their own
        /// (Unity.SpaceGod.Rendering.Urp) that declares them before the game's own, so the original's Render pipeline page
        /// reads URP's rows, then MSAA, then the pipeline and HDRP's; this port compiles them all into one assembly, where
        /// they came last. Their class is initialised first here, before anything touches a setting, so the page is the
        /// original's. Called first thing by every host (the game mode, a co-op client's link, the headless tools).
        /// </summary>
        public static void MatchSettingsOrder()
        {
            if (settingsOrdered) return;
            settingsOrdered = true;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(Rendering.Urp.UrpPostProcessSettings).TypeHandle);
        }

        /// <summary>Unity's SubsystemRegistration / BeforeSceneLoad hooks: the profile store and the CLI mute.</summary>
        public static void InitialiseOnce()
        {
            MatchSettingsOrder(); // US-T70
            if (initialised) return;
            initialised = true;
            // US-T72: the capture package's own BeforeSceneLoad boot (CaptureUnity's [RuntimeInitializeOnLoadMethod] in Unity).
            MindAttic.Capture.Unity.CaptureUnity.Boot();
            // Unity.Shared's ProfilesBootstrap.Initialize (its Unity half does not compile outside Unity).
            Profiles.ApplyMutedOverride();
            if (!Profiles.IsConfigured) Profiles.UseDataPath(Application.persistentDataPath);
            SessionAudio.OnLoad();
            GameLaunch.SceneLoader = LoadScene;
        }

        /// <summary>The first scene has loaded (Unity's AfterSceneLoad; the game mode's BeginPlay).</summary>
        public static void EnsureApp()
        {
            InitialiseOnce();
            // Unity's other AfterSceneLoad hook (US-T40): ModelViewer.Ensure, the F9 viewer that lives in every scene
            // (DontDestroyOnLoad); the facade runs no Awake/Start on AddComponent, so they are sent here as Unity would.
            if (!viewerEnsured)
            {
                viewerEnsured = true;
                ModelViewer.Ensure();
                var viewer = Object.FindAnyObjectByType<ModelViewer>();
                if (viewer != null)
                {
                    UnityBridge.Invoke(viewer, "Awake");
                    UnityBridge.Invoke(viewer, "Start");
                }
            }
            // US-T72: the capture events' AfterSceneLoad hook (the shipped capture.json into the options), then the capture
            // host's first frame (Unity's CaptureRunner.Start), which starts the engine; both no-ops without -capture.
            if (!captureBegun)
            {
                captureBegun = true;
                CaptureEvents.ApplyDefaults();
                MindAttic.Capture.Unity.CaptureUnity.Begin();
            }
            // The main menu is a scene of its own, and the app is not made in it: the menu loads the game scene
            // when the player starts. A run for the tour, a capture or -continue goes straight there.
            if (UI.MainMenu.Available && !GameLaunch.SkipsMenu)
            {
                LoadScene(GameLaunch.MenuScene);
                return;
            }
            Equip();
        }

        /// <summary>Unity's SceneManager.LoadScene for the game's two scenes.</summary>
        static void LoadScene(string scene)
        {
            TearDown();
            if (scene == GameLaunch.MenuScene) UI.MainMenu.Show();
            else
            {
                UI.MainMenu.Hide();
                Equip();
            }
        }

        static void Equip()
        {
            if (App == null)
            {
                App = new GameObject("Unity.SpaceGod").AddComponent<UnitySpaceApp>();
                // Unity runs Awake as the component is added.
                if (!App.Awoken) UnityBridge.Invoke(App, "Awake");
                AppChanged?.Invoke(App);
            }

            if (HasCommandLineFlag("-demo") && App.GetComponent<CaptureRunner>() == null)
            {
                // -capture's own timer (if requested) keeps running unmodified: -demo only adds
                // the tour, it does not change what -capture does.
                var runner = App.gameObject.AddComponent<CaptureRunner>();
                UnityBridge.Invoke(runner, "Awake");
            }
        }

        /// <summary>The game scene unloads: the app's GameObject (and with it OnDestroy) goes.</summary>
        public static void TearDown()
        {
            if (App == null) return;
            Object.Destroy(App.gameObject);
            App = null;
            AppChanged?.Invoke(null);
        }

        public static bool HasCommandLineFlag(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            foreach (var a in args)
                if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>The value of a <c>-Name=value</c> argument, or null.</summary>
        public static string CommandLineValue(string name)
        {
            string prefix = name + "=";
            foreach (var a in Environment.GetCommandLineArgs())
                if (a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return a.Substring(prefix.Length).Trim('"');
            return null;
        }
    }
}
