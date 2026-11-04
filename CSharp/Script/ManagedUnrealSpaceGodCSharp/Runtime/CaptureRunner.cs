using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using MindAttic.Capture.Unity;
using UnityEngine;
using UnrealSharp.Engine;

namespace Unity.SpaceGod
{
    /// <summary>
    /// Started by <c>-demo</c> (see <see cref="Bootstrap"/>): runs the <see cref="SceneTour"/>
    /// once, then quits. Used to verify a build visually and to produce the README images. Add
    /// <c>-capture screen</c> (or <c>video</c>, or both) to save a screenshot at each stop through
    /// MindAttic.Capture — shots go to R:\MindAttic\Capture\SpaceGod.Unreal.CSharp\SpaceGod.Unreal.CSharp_{timestamp}.png,
    /// each sidecar tagged with its stop (US-T72, as in Unity). Without <c>-capture</c> the tour still plays, just with no
    /// screenshots. The repo's tools pass <c>-capturedir=folder</c> instead (tools/play.ps1 -Mode demo): every stop is then
    /// Unreal's own screenshot (<c>shot showui</c>, the HUD in it) saved as <c>folder/stop.png</c>, with no capture session.
    ///
    /// (Port of Runtime/CaptureRunner.cs. Unity ran Start as a coroutine; the facade's MonoBehaviour has none, so
    /// Update steps it by hand with the same yields: null is one frame, a nested IEnumerator runs to its end first.)
    /// </summary>
    public sealed class CaptureRunner : MonoBehaviour
    {
        Coroutine main;

        void Update()
        {
            main ??= new Coroutine(Start());
            main.Step();
        }

        IEnumerator Start()
        {
            var app = GetComponent<UnitySpaceApp>();
            var toFolder = Bootstrap.CommandLineValue("-capturedir") != null;
            var capturing = toFolder || (CaptureUnity.Args != null && CaptureUnity.Args.Any);
            if (capturing && !toFolder)
            {
                var wait = 0f;
                while (!CaptureUnity.Engine.Enabled && wait < 15f)
                {
                    wait += Time.unscaledDeltaTime;
                    yield return null;
                }
                capturing = CaptureUnity.Engine.Enabled;
                if (!capturing) Debug.LogError("[Unity.SpaceGod] MindAttic.Capture did not start; the tour will not be captured.");
            }

            // A time reset rebuilds the galaxy under the tour (its ships, bodies and battles are gone):
            // stop there rather than run on, framing and waiting on objects that no longer exist.
            bool done = false, rebuilt = false;
            void OnRebuilt() => rebuilt = true;
            app.GalaxyRebuilt += OnRebuilt;
            var tour = new Coroutine(RunTour(app, capturing ? (toFolder ? Shot : CaptureShot) : NoShot, () => done = true));
            while (!done && !rebuilt)
            {
                tour.Step();
                if (done) break;
                yield return null;
            }
            app.GalaxyRebuilt -= OnRebuilt;
            if (!done)
            {
                // A stop cut off mid-way never lets go of the camera itself (a dogfight's chase lock on a hull nobody flies).
                if (!app.IsPiloting) app.SpaceCamera.ExitFocus();
                Debug.LogWarning("[Unity.SpaceGod] The galaxy was rebuilt mid-tour; the tour stops here.");
            }

            if (capturing) Debug.Log("[Unity.SpaceGod] Tour captured to " + (toFolder ? ShotFolder : CaptureUnity.Engine.SessionFolder));
            Application.Quit();
        }

        static IEnumerator RunTour(UnitySpaceApp app, Func<string, IEnumerator> shot, Action finished)
        {
            yield return SceneTour.Run(app, shot);
            finished();
        }

        static IEnumerator NoShot(string name)
        {
            yield break;
        }

        /// <summary>Unity's Shot: a MindAttic.Capture screenshot tagged with the stop's name (US-T72).</summary>
        static IEnumerator CaptureShot(string name)
        {
            var shot = CaptureUnity.Engine.ScreenshotAsync(name);
            while (!shot.IsCompleted) yield return null;
            var result = shot.Result;
            if (result.Succeeded) Debug.Log("[Unity.SpaceGod] Captured " + result.PrimaryFile);
            else Debug.LogError("[Unity.SpaceGod] Screenshot " + name + " failed: " + result.Error);
        }

        /// <summary>Where the tour's shots go: -capturedir=..., else the project's Saved/Screenshots/Tour.</summary>
        public static string ShotFolder
        {
            get
            {
                foreach (var a in Environment.GetCommandLineArgs())
                    if (a.StartsWith("-capturedir=", StringComparison.OrdinalIgnoreCase)) return a.Substring("-capturedir=".Length).Trim('"');
                return Path.Combine(Paths.ProjectSavedDir(), "Screenshots", "Tour");
            }
        }

        /// <summary>Unreal's screenshot of the next frame, HUD included, then moved to <paramref name="name"/>.png.</summary>
        public static IEnumerator Shot(string name)
        {
            string dir = Path.Combine(Paths.ProjectSavedDir(), "Screenshots");
            var before = DateTime.UtcNow;
            SystemLibrary.ExecuteConsoleCommand("shot showui");
            float deadline = Time.unscaledTime + 10f;
            string found = null;
            while (found == null && Time.unscaledTime < deadline)
            {
                yield return null;
                if (!Directory.Exists(dir)) continue;
                foreach (var f in Directory.GetFiles(dir, "*.png", SearchOption.AllDirectories))
                    if (File.GetLastWriteTimeUtc(f) >= before && !f.Contains(Path.DirectorySeparatorChar + "Tour" + Path.DirectorySeparatorChar)) found = f;
            }
            if (found == null)
            {
                Debug.LogError("[Unity.SpaceGod] Screenshot " + name + " failed: no file appeared under " + dir);
                yield break;
            }
            yield return null; // let the writer finish
            var target = Path.Combine(ShotFolder, name + ".png");
            Directory.CreateDirectory(ShotFolder);
            File.Copy(found, target, true);
            File.Delete(found);
            Debug.Log("[Unity.SpaceGod] Captured " + target);
        }

        /// <summary>A Unity-style coroutine run by hand: each <see cref="Step"/> advances it to its next <c>yield return null</c>.</summary>
        public sealed class Coroutine
        {
            readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();

            public Coroutine(IEnumerator routine) => stack.Push(routine);

            public bool Done => stack.Count == 0;

            public void Step()
            {
                while (stack.Count > 0)
                {
                    var top = stack.Peek();
                    if (!top.MoveNext())
                    {
                        stack.Pop();
                        continue;
                    }
                    if (top.Current is IEnumerator nested)
                    {
                        stack.Push(nested);
                        continue;
                    }
                    return; // null (or anything else): the next frame
                }
            }
        }
    }
}
