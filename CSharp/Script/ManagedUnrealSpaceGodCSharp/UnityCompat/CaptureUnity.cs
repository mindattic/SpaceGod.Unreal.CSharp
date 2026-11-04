using System;
using System.Collections.Generic;
using System.Linq;
using Stopwatch = System.Diagnostics.Stopwatch;
using System.Threading.Tasks;
using MindAttic.Capture.Hosting;
using MindAttic.Capture.Windows;
using UnityEngine;

namespace MindAttic.Capture.Unity
{
    /// <summary>
    /// US-T72: the Unity package's <c>CaptureUnity</c> (com.mindattic.capture, Runtime/CaptureUnity.cs), with the same
    /// members, so the mirrored game code calls it unchanged. Off unless asked: only the literal <c>-capture</c> flag on
    /// the command line turns it on, and only in a build with MINDATTIC_CAPTURE (this repo's default, see the game
    /// csproj):
    /// <list type="bullet">
    /// <item><c>-capture video</c>: film the game from start to exit; <c>-capture screen 5</c>: a PNG every 5 seconds; both
    /// together; <c>hq</c>, a duration, <c>-capture-config file.json</c> for trigger rules and hotkeys, as everywhere.</item>
    /// <item>Game code: <c>CaptureUnity.Raise("battle-won")</c>, <c>Marker</c>, <c>TakeScreenshot</c>, <c>StartRecording</c>.</item>
    /// </list>
    /// Output: R:\MindAttic\Capture\SpaceGod.Unreal.CSharp\SpaceGod.Unreal.CSharp_{timestamp}.{ext} (Videos\MindAttic when R:
    /// is missing). Needs ffmpeg on PATH (or beside the exe) for video.
    ///
    /// The host differs from Unity's, which reads the game view back from the GPU: here MindAttic.Capture's Windows desktop
    /// host records the game window's rectangle on screen (Desktop Duplication, GDI as the fallback; Windows Graphics
    /// Capture needs a Windows TFM UnrealSharp cannot build, see Vendor/MindAttic.Capture), and the sound is the system's
    /// (WASAPI loopback), not the game's mix alone. The hotkeys are the game's own (InputState), as docs/CAPTURE.md says.
    /// </summary>
    public static class CaptureUnity
    {
        static WindowsCaptureSessionLike session;
        static Task startTask;
        static readonly object gate = new object();
        static bool quitting, booted;

        /// <summary>True in a build with the MINDATTIC_CAPTURE define (Unity: also the editor and development builds).</summary>
        public static bool IsCaptureBuild =>
#if MINDATTIC_CAPTURE
            true;
#else
            false;
#endif

        /// <summary>The running engine (a no-op instance until capture starts, and in builds without capture).</summary>
        public static ICapture Engine => Capture.Current;

        /// <summary>The parsed -capture flags (null when capture is off).</summary>
        public static CaptureArgs Args { get; private set; }

        /// <summary>Effective options, or null when capture is off.</summary>
        public static CaptureOptions Options { get; private set; }

        /// <summary>Raises a game event for the capture.json rules. Cheap; never blocks.</summary>
        public static void Raise(string eventName, CaptureData data = null) => Capture.Current.Raise(eventName, data);

        /// <summary>Drops a marker (a chapter in exported video).</summary>
        public static void Marker(string name, CaptureData data = null) => Capture.Current.Marker(name, data);

        /// <summary>Takes a screenshot of the game (fire and forget).</summary>
        public static void TakeScreenshot(string tag = "screenshot") => _ = Capture.Current.ScreenshotAsync(tag);

        /// <summary>Starts an on-demand recording (fire and forget; cut from the tape, so starting is instant).</summary>
        public static void StartRecording(string tag = "recording") => _ = Capture.Current.StartRecordingAsync(tag);

        /// <summary>Stops the recording started by <see cref="StartRecording"/> and exports it (fire and forget).</summary>
        public static void StopRecording() => _ = Capture.Current.StopRecordingAsync();

        /// <summary>
        /// Unity's BeforeSceneLoad boot, called from <c>Bootstrap.InitialiseOnce</c>: parses the flags and, when capture
        /// is asked for, starts the engine on the game window. Runs once per process.
        /// </summary>
        public static void Boot()
        {
            if (booted) return;
            booted = true;
            var parsed = CaptureArgs.Parse(Environment.GetCommandLineArgs().Skip(1));
            if (!IsCaptureBuild)
            {
                if (parsed.Any) Debug.Log("[MindAttic.Capture] -capture is ignored in this build (built without MINDATTIC_CAPTURE).");
                return;
            }
            if (parsed.Ignored != null)
                Debug.Log($"[MindAttic.Capture] \"-capture {parsed.Ignored}\" is not a capture mode (video, screen N); left for the game.");
            if (!parsed.Any) return;

            CaptureOptions options;
            try
            {
                options = new CaptureOptions
                {
                    AppName = "SpaceGod.Unreal.CSharp",
                    AppVersion = Application.version,
                    Log = new UnrealCaptureLog(),
                };
                options.Apply(parsed);
                options.DetachedFinalizerPath = AgentLocator.Find();
            }
            catch (CaptureException ex)
            {
                Debug.LogError("[MindAttic.Capture] Capture disabled: " + ex.Message);
                return;
            }
            Args = parsed;
            Options = options;
            // docs/CAPTURE.md section 5: the capture hotkeys are the game's own (InputState's Alt+F9/F10/F11), never the
            // capture.json map. So the engine is started directly, not through DesktopCapture.StartAsync, which would bind
            // that map system-wide (RegisterHotKey).
            options.Hotkeys.Clear();
            var host = new DesktopCaptureHost(CaptureTarget.CurrentApp, new DesktopHostOptions { PreferWgcForWindows = false });
            startTask = StartEngineAsync(host, options);
        }

        static async Task StartEngineAsync(ICaptureHost host, CaptureOptions options)
        {
            try
            {
                var engine = await CaptureEngine.StartNewAsync(host, options).ConfigureAwait(false);
                bool late;
                lock (gate)
                {
                    late = quitting;
                    if (!late) session = new WindowsCaptureSessionLike(engine);
                }
                if (late)
                {
                    await engine.DisposeAsync().ConfigureAwait(false);
                    return;
                }
                if (options.SessionRecording) Debug.Log("[MindAttic.Capture] Recording the game window to " + engine.SessionFolder);
            }
            catch (Exception ex)
            {
                Debug.LogError("[MindAttic.Capture] Could not start capture: " + ex.Message);
            }
        }


        /// <summary>The game is ending (the game mode's EndPlay): stop capture and hand the session to the exporter, waiting
        /// for it as Unity's OnQuitting does (with capture.exe installed the export runs after exit).</summary>
        public static void Shutdown()
        {
            WindowsCaptureSessionLike s;
            lock (gate)
            {
                if (quitting) return;
                quitting = true;
                s = session;
                session = null;
            }
            var shutdown = s != null ? Task.Run(() => s.Engine.DisposeAsync().AsTask()) : startTask;
            if (shutdown == null || shutdown.IsCompleted) return;
            var sw = Stopwatch.StartNew();
            try
            {
                if (!shutdown.Wait(TimeSpan.FromMinutes(15)))
                    Debug.LogWarning("[MindAttic.Capture] Capture did not finish shutting down within 15 minutes.");
            }
            catch (AggregateException ex)
            {
                Debug.LogError("[MindAttic.Capture] Capture shutdown failed: " + ex.GetBaseException().Message);
            }
            Options?.Log.Log(CaptureLogLevel.Info, $"Capture stopped in {sw.Elapsed.TotalSeconds:0.0}s.");
        }

        sealed class WindowsCaptureSessionLike
        {
            public WindowsCaptureSessionLike(CaptureEngine engine) => Engine = engine;
            public CaptureEngine Engine { get; }
        }

        /// <summary>Capture's log in Unreal's (LogSpaceGod), as Unity's UnityCaptureLog writes to the player log.</summary>
        sealed class UnrealCaptureLog : ICaptureLog
        {
            public void Log(CaptureLogLevel level, string message, Exception exception = null)
            {
                var text = "[MindAttic.Capture] " + message + (exception != null ? " (" + exception.Message + ")" : "");
                if (level >= CaptureLogLevel.Error) Debug.LogError(text);
                else if (level == CaptureLogLevel.Warning) Debug.LogWarning(text);
                else if (level == CaptureLogLevel.Info) Debug.Log(text);
            }
        }
    }
}
