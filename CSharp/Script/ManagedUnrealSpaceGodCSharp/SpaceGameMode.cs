using System;
using System.Globalization;
using Unity.Shared.Core;
using Unity.SpaceGod;
using Unity.SpaceGod.Bridge;
using Unity.SpaceGod.Coop;
using Unity.SpaceGod.Core;
using Unity.SpaceGod.Input;
using UnrealSharp.Attributes;
using UnrealSharp.Engine;
using UnrealSharp.Log;
using Time = UnityEngine.Time;
using UnityBridge = UnityEngine.UnityBridge;

namespace ManagedUnrealSpaceGodCSharp;

/// <summary>
/// The C# track's game mode: the Unity scene's host (US-T26). At BeginPlay it does what Unity did before the first
/// frame (Unity.SpaceGod's Bootstrap makes the <see cref="UnitySpaceApp"/>, a MonoBehaviour on the shared Unity facade),
/// and each Tick what Unity's player loop did: the window and input in (<see cref="UnrealHost"/>), every MonoBehaviour's
/// Update (UnityBridge.Tick: the app's own drives the Core exactly as the original's), then the camera and cursor out
/// and the scene pushed to Unreal (UnityBridge.Sync).
///
/// <c>-SpaceGodSmoke</c> (tools/play.ps1 -Track CSharp -Mode smoke, headless): runs the app loop for a scripted few
/// seconds — idle frames, a time-warp key, a free-swim camera move (W held, a right-drag look), a number-key focus on
/// Earth, a save/load round trip through Core/Persistence — logs what each did, and quits.
/// </summary>
[UClass]
public partial class ASpaceGameMode : AGameModeBase
{
    const string Log = "LogSpaceGod";
    const float Frame = 1f / 60f;

    UnrealHost? host;
    ScriptedInputDevice? script;

    // US-T67 (the C++ track's US-T64): -CoopHost / -CoopJoin=<address> launch co-op once, from the first standalone start.
    static bool coopLaunched;

    public override void BeginPlay()
    {
        base.BeginPlay();
        Bootstrap.MatchSettingsOrder(); // US-T70: the settings pages in the original's order, before anything reads a setting
        // US-T67: co-op from the command line in every build. A Shipping game ignores a map URL on its command line
        // (UGameInstance::StartGameInstance: no "?listen", no "<host ip>") and has no console for "open <host ip>", so
        // -CoopHost reopens this project's one map (GameDefaultMap) as a listen server, as the URL's "?listen" does, and
        // -CoopJoin=<address> connects to a host; each once (a client whose host has gone comes back here and stays).
        if (!coopLaunched && SystemLibrary.IsStandalone())
        {
            coopLaunched = true;
            string? joinAt = Bootstrap.CommandLineValue("-CoopJoin");
            if (!string.IsNullOrEmpty(joinAt))
            {
                UGameplayStatics.OpenLevel(new UnrealSharp.Core.FName(joinAt), true);
                return;
            }
            if (Bootstrap.HasCommandLineFlag("-CoopHost"))
            {
                UGameplayStatics.OpenLevel(new UnrealSharp.Core.FName("/Engine/Maps/Entry"), true, "listen");
                return;
            }
        }
        // The views on their own (US-T27, tools/play.ps1 -Mode views / shot): Sol's views built without the app.
        if (Bootstrap.HasCommandLineFlag("-SpaceGodViews") || ShotPath() != null)
        {
            RunViews();
            return;
        }
        bool smoke = Bootstrap.HasCommandLineFlag("-SpaceGodSmoke") || Bootstrap.HasCommandLineFlag("-SpaceGodAudit"); // US-T48: the audit runs after the smoke
        if (smoke)
        {
            // The smoke run's profile (and its saves) in a scratch folder, never the player's own.
            var root = System.IO.Path.Combine(SystemLibrary.ProjectSavedDirectory, "SmokeProfiles");
            System.IO.Directory.CreateDirectory(root);
            Profiles.UseRoot(root);
        }
        else if (Bootstrap.HasCommandLineFlag("-SpaceGodUiShots") || Unity.SpaceGod.Race.RaceProbe.On)
        {
            // US-T45: the screenshots' profile in a scratch folder, fresh each run (the C++ track's ShotProfiles), so the
            // menu reads as a first start ("No saves yet") whatever was played before.
            var root = System.IO.Path.Combine(SystemLibrary.ProjectSavedDirectory, "ShotProfiles");
            if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true);
            System.IO.Directory.CreateDirectory(root);
            Profiles.UseRoot(root);
        }
        // US-T39: a listen server (tools/play.ps1 -Mode host) hosts co-op; standalone stays the original single-player path.
        if (SystemLibrary.IsServer() && !SystemLibrary.IsStandalone() && !SystemLibrary.IsDedicatedServer())
        {
            if (CoopSmoke.Active)
            {
                var root = System.IO.Path.Combine(SystemLibrary.ProjectSavedDirectory, "SmokeProfiles", "CoopHost");
                System.IO.Directory.CreateDirectory(root);
                Profiles.UseRoot(root);
            }
            CoopPeer.StartHost(CoopPeer.NameFromCommandLine(CoopSmoke.Active ? "Host" : null));
            ASpaceCoopLink.WireHud();
        }

        UnityBridge.Begin(this);
        host = new UnrealHost(this);
        host.Begin();
        if (smoke) host.Override = script = new ScriptedInputDevice();
        // US-T28: the local player's HUD is the UI's (Unity's IMGUI on the Canvas: every MonoBehaviour's OnGUI).
        UGameplayStatics.GetPlayerController(0)?.ClientSetHUD(typeof(Unity.SpaceGod.UI.ASpaceHud));

        var started = System.Diagnostics.Stopwatch.StartNew();
        Bootstrap.EnsureApp();
        var app = Bootstrap.App;
        if (app != null)
            UnrealLogger.Log(Log, $"UnitySpaceApp up in {started.ElapsedMilliseconds} ms: {app.Galaxy.Systems.Count} systems, " +
                $"{app.Catalog.Count} bodies in {app.ActiveSystem.Name}, {app.Fleet.Ships.Count} ships (.NET {Environment.Version})");

        // US-T67: and in the co-op log, so a packaged host (-SpaceGodCoopLog; Shipping logs nothing) says it is up too.
        if (CoopSmoke.Active && app != null && CoopPeer.Current?.IsHost == true)
            CoopPeer.Log($"host app up: {app.Galaxy.Systems.Count} systems, {app.Fleet.Ships.Count} ships");
        if (CoopSmoke.Active && app != null) UnityEngine.Application.targetFrameRate = 60; // US-T39: the co-op smoke's pace

        if (smoke)
        {
            RunSmoke();
            // Through the engine, not Environment.Exit: the runtime is hosted by Unreal and must shut down with it.
            UnrealLogger.Log(Log, "SpaceGodSmoke: done, quitting");
            SystemLibrary.ExecuteConsoleCommand("quit");
        }
    }

    Unity.SpaceGod.Views.ViewsSmoke? views;
    int viewFrames;

    static string? ShotPath()
    {
        foreach (var a in Environment.GetCommandLineArgs())
            if (a.StartsWith("-SpaceGodShot=", StringComparison.OrdinalIgnoreCase)) return a.Substring("-SpaceGodShot=".Length).Trim('"');
        return null;
    }

    /// <summary>US-T27's views check: Sol's views for a frame, logged; headless it quits, windowed (-SpaceGodShot=file)
    /// it keeps them on screen, saves a screenshot after 90 frames and quits after 150.</summary>
    void RunViews()
    {
        try
        {
            // The C# track's fixed start (2026-11-03 12:00 UTC), as US-T6's smoke used.
            var clock = new SimClock(2461348.0);
            var galaxy = Galaxy.CreateDefault(clock.Jd);
            views = new Unity.SpaceGod.Views.ViewsSmoke(galaxy);
            UnrealLogger.Log(Log, "SpaceGodViews: " + views.Build(this, clock.Jd, ShotPath() != null));
        }
        catch (Exception e)
        {
            UnrealLogger.LogError(Log, "SpaceGodViews failed: " + e);
        }
        if (ShotPath() == null) SystemLibrary.ExecuteConsoleCommand("quit");
    }

    public override void Tick(float deltaSeconds)
    {
        base.Tick(deltaSeconds);
        if (views != null)
        {
            views.Frame(deltaSeconds);
            var shot = ShotPath();
            viewFrames++;
            if (shot != null && viewFrames == 90) SystemLibrary.ExecuteConsoleCommand("HighResShot 1600x900 filename=\"" + shot.Replace('\\', '/') + "\"");
            if (shot != null && viewFrames == 150) SystemLibrary.ExecuteConsoleCommand("quit");
            return;
        }
        if (script != null) return; // the smoke run steps its own frames
        if (Bootstrap.App == null && !Unity.SpaceGod.UI.MainMenu.Showing) return; // US-T28: the menu's own Update runs too
        Step(deltaSeconds);
    }

    // US-T39: each client that logs in gets its link actor (its RPCs), and leaving hands its seat back.
    readonly System.Collections.Generic.Dictionary<APlayerController, ASpaceCoopLink> coopLinks = new();

    public override void OnPostLogin(APlayerController newPlayer)
    {
        base.OnPostLogin(newPlayer);
        var peer = CoopPeer.Current;
        if (peer == null || !peer.IsHost || newPlayer == null || newPlayer.IsLocalController()) return;
        var link = ASpaceCoopLink.SpawnFor(newPlayer);
        if (link == null) { UnrealLogger.LogError(Log, "SpaceGodCoop: could not spawn a link for a new player"); return; }
        coopLinks[newPlayer] = link;
        peer.HostLogin(link);
        CoopPeer.Log("a player logged in; link spawned");
    }

    public override void OnLogout(AController exitingController)
    {
        if (exitingController is APlayerController pc && coopLinks.TryGetValue(pc, out var link))
        {
            coopLinks.Remove(pc);
            CoopPeer.Current?.HostLogout(link);
            link.DestroyActor();
        }
        base.OnLogout(exitingController);
    }

    public override void EndPlay(EEndPlayReason endPlayReason)
    {
        var app = Bootstrap.App;
        if (app != null) UnityBridge.Invoke(app, "OnApplicationQuit");
        Bootstrap.TearDown();
        CoopPeer.Stop(); // US-T39
        base.EndPlay(endPlayReason);
    }

    /// <summary>One frame of Unity's player loop.</summary>
    void Step(float dt)
    {
        long raceT0 = Unity.SpaceGod.Race.RaceProbe.Now(); // US-T46: the whole Unity frame, for the race
        host!.BeforeUpdate(Bootstrap.App!);
        UnityBridge.Tick(dt);
        CoopPeer.Current?.HostEndUpdate(Bootstrap.App); // US-T39: the frame to the clients
        host.AfterUpdate();
        UnityBridge.Sync();
        script?.EndFrame();
        Unity.SpaceGod.Race.RaceProbe.AppMs = Unity.SpaceGod.Race.RaceProbe.Since(raceT0); // US-T46
    }

    void Frames(int n)
    {
        for (int i = 0; i < n; i++) Step(Frame);
    }

    void RunSmoke()
    {
        // US-T28: the UI first -- the main menu's pages, New Game (which makes the app), then every screen and panel.
        Unity.SpaceGod.UI.UiSmoke.Run(Frames);
        var app = Bootstrap.App;
        if (app == null || script == null)
        {
            UnrealLogger.LogError(Log, "SpaceGodSmoke: no app");
            return;
        }
        var inv = CultureInfo.InvariantCulture;
        var t = System.Diagnostics.Stopwatch.StartNew();
        Frames(10);
        Report(app, $"10 frames in {t.ElapsedMilliseconds} ms");

        // Time warp: '.' one step faster.
        string warpBefore = app.Clock.Warp.Label;
        script.Tap(Key.Period);
        Frames(1);
        UnrealLogger.Log(Log, $"SpaceGodSmoke: '.' warp {warpBefore} -> {app.Clock.Warp.Label}");

        // Free swim: W held for 30 frames, then a right-drag look.
        var from = app.SpaceCamera.ScenePosition;
        script.Hold(Key.W);
        Frames(30);
        script.Release(Key.W);
        double moved = (app.SpaceCamera.ScenePosition - from).Length;
        var rotBefore = app.SpaceCamera.Rotation;
        script.HoldButton(MouseButton.Right);
        for (int i = 0; i < 15; i++)
        {
            script.MouseDelta = new UnityEngine.Vector2(12f, 0f);
            Frames(1);
        }
        script.ReleaseButton(MouseButton.Right);
        Frames(1);
        float turned = UnityEngine.Quaternion.Angle(rotBefore, app.SpaceCamera.Rotation);
        UnrealLogger.Log(Log, string.Format(inv, "SpaceGodSmoke: camera moved {0:G4} units (W, {1}), turned {2:F1} deg (right-drag)",
            moved, app.SpaceCamera.Mode, turned));

        // Number key 3 twice: select Earth and focus on it.
        script.Tap(Key.Digit3);
        Frames(1);
        script.Tap(Key.Digit3);
        Frames(90);
        UnrealLogger.Log(Log, $"SpaceGodSmoke: '3' x2 selected {Describe(app.Selected)}, camera {app.SpaceCamera.Mode} on {Describe(app.SpaceCamera.FocusTarget)}");

        // Save, run on, load: the clock comes back to the saved moment.
        double savedJd = app.Clock.Jd;
        int savedShips = app.Fleet.Ships.Count;
        bool saved = app.SaveTo("smoke", "US-T26 smoke");
        Frames(60);
        double ranOn = app.Clock.Jd;
        bool loaded = app.LoadFrom("smoke");
        Frames(2);
        UnrealLogger.Log(Log, string.Format(inv,
            "SpaceGodSmoke: save {0}, load {1}: JD saved {2:F6}, ran on to {3:F6}, back to {4:F6} (|d| = {5:G3} s); ships {6} -> {7}",
            saved ? "ok" : "FAILED", loaded ? "ok" : "FAILED", savedJd, ranOn, app.Clock.Jd, Math.Abs(app.Clock.Jd - savedJd) * 86400.0,
            savedShips, app.Fleet.Ships.Count));
        Report(app, $"smoke done in {t.ElapsedMilliseconds} ms");

        // US-T48: -SpaceGodAudit (tools/play.ps1 -Mode audit) presses every binding the probes cover after the smoke.
        if (Bootstrap.HasCommandLineFlag("-SpaceGodAudit")) Unity.SpaceGod.Audit.FeatureAudit.Run(app, script, Frames);
    }

    static string Describe(Target target) =>
        target.IsBody ? "body " + Bootstrap.App!.Catalog[target.Body].Name : target.IsShip ? "ship " + target.Ship.Name : target.Kind.ToString();

    void Report(UnitySpaceApp app, string label)
    {
        var inv = CultureInfo.InvariantCulture;
        var cam = app.SpaceCamera.ScenePosition;
        UnrealLogger.Log(Log, string.Format(inv,
            "[{0}] {1}: JD {2:F6} = {3:yyyy-MM-dd HH:mm:ss} UTC, warp {4}; camera scene ({5:G6}, {6:G6}, {7:G6}) {8}, fov {9:F1}, near {10:G3}; toast '{11}'; facade {12}",
            Time.frameCount, label, app.Clock.Jd, app.Clock.Utc, app.Clock.Warp.Label, cam.X, cam.Y, cam.Z, app.SpaceCamera.Mode,
            app.UnityCamera.fieldOfView, app.UnityCamera.nearClipPlane, app.Toast, UnityBridge.Last));
    }
}
