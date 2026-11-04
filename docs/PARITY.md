# Parity with SpaceGod.Unity.CSharp (US-T70)

This copy and [SpaceGod.Unity.CSharp](https://github.com/mindattic/SpaceGod.Unity.CSharp) run the same C# game. The audit below
checks that they are the same game and that only the engine underneath differs: every file compared, every difference
classified, the gaps closed and both copies proven. The Unity copy's own `docs/PARITY.md` carries the same table from its side.

## What was compared

| Layer | Here | In the Unity copy | Result |
|---|---|---|---|
| Simulation (Core) | `Vendor/Unity.SpaceGod/Core/` (184 files) | `Assets/UnitySpace/Core/` (184 files) | **byte-identical** (one file, `Persistence/ObjectGraph.cs`, was behind: re-vendored, see below) |
| Shared core | `Vendor/Unity.Shared/Core/` | `Packages/com.mindattic.unity.shared/Runtime/Core/` | byte-identical (both from Unity.Shared `3ce3b8a`) |
| Game code (Runtime) | `CSharp/Script/ManagedUnrealSpaceGodCSharp/Runtime/` | `Assets/UnitySpace/Runtime/` | 36 files identical; 32 differ only in engine plumbing (classified below); none differ in what the player sees or does |
| Save format | `ObjectGraph` + `CSharp/Core/SaveCompat/` | the same files (SaveCompat copied in by US-T70) | the same format; saves cross both ways (proof below) |

## The Runtime, file by file

Every hunk of every mirrored file was read and classified as one of four kinds:

- **(a) engine adaptation**, with no effect a player can see;
- **(b) a bug fix** present on one side only;
- **(c) a behaviour, feature or text difference**;
- **(d) a file on one side only**.

| Kind | Files | What |
|---|---|---|
| (a) port headers only | `UnitySpaceApp.{Boarding,Broadsides,Collisions,Course,Embodiment,Ftl,Hauler,Lanes,Practice,Streaming}.cs`, `Camera/SpaceCamera.cs`, `Camera/StarTravel.cs`, `ActiveProfile.cs`, `GameSettingsHost.cs`, `Target.cs` | a "Port of ..." comment; the code is the original's |
| (a) engine plumbing | `Bootstrap.cs`, `GameLaunch.cs`, `SessionAudio.cs`, `ModelViewer.cs` | Unity's scene loads and `RuntimeInitializeOnLoadMethod` hooks become the game mode's calls (`SceneLoader`, `InitialiseOnce`); the same menu → game → menu flow |
| (a) | `EngineSettings.cs` | MSAA set on the main camera (the facade's one camera) instead of `Camera.allCameras`; the thumbnails camera sets its own |
| (a) | `Input/InputDevice.cs`, `Input/InputState.cs`, `Input/UnrealInputDevice.cs` | the keys come from Unreal's player controller instead of the Input System; **every key of the `Key` enum is mapped** (`UnrealInputDevice.BuildMap`). The one merge, numpad Enter reported as Enter, changes nothing: the only binding (`EngageFtl`) takes both |
| (a) | `UnitySpaceApp.cs` + `UnitySpaceApp.Helm.cs` | the original's 3,108-line file, split in two only to keep it reviewable (lines 976-2085 are `Helm.cs`); line for line the same but for the camera-relative doubles Unreal's large-world coordinates take, the audio listener and the mouse read through `Input.Device` |
| (a) tooling | `CaptureRunner.cs` | the `-demo` tour unchanged; `-capture` saves Unreal screenshots where Unity's MindAttic.Capture recorded |
| (a) | `Coop/CoopPeer.cs`, `Coop/CoopSmoke.cs` | Unreal's logger and `quit` command for Unity's `Debug.Log` / `Application.Quit` |
| (a) | `UI/MainMenu.cs` (+ `UI/MainMenu.Scene.cs`), `Rendering/AssetStreamer.cs` | `partial`, and a global using |
| (d) here only | `Bridge/UnrealHost.cs`, `Coop/SpaceCoopLink.cs`, `UI/SpaceHud.cs`, `UnityCompat/UnityScreen.cs`, `GlobalUsings.cs`, `Rendering/RenderBackend.cs` | the Unreal host, link actor, HUD actor and facade, what Unity itself is to the other copy |
| (d) here only, the same settings | `Rendering/RenderPipelineSettings.cs`, `Rendering/UrpPostProcessSettings.cs` | the settings halves of the Unity copy's `Rendering/Pipeline/...`: **every setting declared identically** (key, group, default, range, text); only the backend they apply to differs |
| (d) here only, tooling | `Race/RaceRun.cs`, `UI/FidelityShots.cs`, `UI/UiSmoke.cs`, `Views/ViewsSmoke.cs`, `Audit/*` | the race and screenshot runs; `Audit/*` now runs in the Unity copy too |
| (d) Unity only | `Coop/CoopNet.cs`, `UnitySpaceApp.CoopNet.cs` | co-op's TCP link and client loop there, standing for `SpaceCoopLink` and the game mode here |
| (d) Unity only | `Rendering/Pipeline/{RenderBackend,RenderPipelineSelection}.cs`, `Urp/*`, `Hdrp/*` | Unity's two render pipelines (below) |

**(b) bug fixes on one side only: none left.** The Unity copy's US-T69 work had already brought over the three this copy
found:
- US-T58, the player's own gate jump;
- US-T41, saves made once caches were full;
- F12 surviving `InputState.Reset`.

US-T70 found two more gaps, both on the Unity side:

| Fix | Where | Commit |
|---|---|---|
| The pointer-over-UI test read the real mouse (`Mouse.current`), not `Input.Device`, so a scripted device (the co-op smoke's client, the audit) was tested against wherever the real pointer was. It now reads `Input.Device`, as here. | SpaceGod.Unity.CSharp `UnitySpaceApp.cs` `IsPointerOverUi` | Unity copy `ad968a1` |
| The Spawn & Pilot screen's ship thumbnails were blank there. Asked from `OnGUI`, which Unity 6 runs inside URP's camera stack, `ShipThumbnails`' manual `Camera.Render` threw and cached a blank snapshot. They now render at the next `LateUpdate`. Here the same file always drew them, since Unreal draws the UI outside such a pass (the fidelity shots check them), so this copy keeps the original's file. | SpaceGod.Unity.CSharp `Rendering/ShipThumbnails.cs` | Unity copy `86d6834` |

And one behaviour gap, on this side:

| Fix | Where | Commit |
|---|---|---|
| The settings menu's **Render pipeline** page lists its rows in the order their classes are first initialised. In Unity the URP post-processing settings live in their own assembly, which declares them first: URP's 13 rows, then MSAA, then the pipeline and HDRP's 6. This copy compiles everything into one assembly, where URP's rows came last. `Bootstrap.MatchSettingsOrder` now initialises `UrpPostProcessSettings` before anything reads a setting, and the audit's `render.settings` probe expects the original's order on both copies. | `Runtime/Bootstrap.cs`, `SpaceGameMode.cs`, `Coop/SpaceCoopLink.cs`, `Audit/SettingsAudit.cs` | `57c111a` |

The Core gap went the other way: the Unity copy fixes the weak-table bug (US-T41) in Core's own `ObjectGraph.cs`, where
this copy had fixed it from outside (`SaveCompat/SaveStatics.cs`, which rebuilds the table). `Vendor/.../ObjectGraph.cs`
now has the Unity copy's fix (`13bf391`), so the two Cores are byte-identical again; `SaveStatics` stays as it was and is
now a no-op rebuild of the same table. The Bench's five digests are unchanged (5 of 5 match after it).

**(c) behaviour differences: none in the game.**
- **Screens and HUD:** the HUD, every screen and menu (`UI/Hud*.cs`, `GalaxyMapScreen*.cs`, `HelpScreen.cs`,
  `PauseMenu.cs`, `DisplayOptions.cs`, `Icons.cs`), the views and the co-op HUD are identical files.
- **Controls, help and toasts:** the bindings, the controls help (`Core/ControlsHelp.cs`) and every notification the
  player sees come from identical code.
- **Settings:** the settings defaults are identical declarations.
- **Time warp:** the steps are Core's `SimClock`.

What the engine makes differ:

| | Unity copy | This copy |
|---|---|---|
| Render pipeline | URP, or HDRP by restarting into a second player built beside it (`render.pipeline`) | Unreal's one renderer, with URP's post-process profile; the pipeline choice is kept and reported as pending, and restarts nothing (HDRP's extras are "Ignored on URP" in both) |
| `-pipeline urp\|hdrp` | picks the player | accepted, no effect |
| Co-op transport | TCP, `-CoopHost [-port=N]`, `-CoopJoin=addr[:port]` | Unreal's replication on the same switches (`-port=` is Unreal's own) |
| On a co-op client, purely visual timers (animations, cache ages) | the client's own clock (the frames the game steps run on the host's seconds) | the host's seconds |

## Co-op

The Unity copy's co-op is this copy's code. `CoopPeer`, `CoopWire`, `CoopInput`, `CoopSettings`, `CoopSmoke`,
`UnitySpaceApp.Coop.cs`, `Hud.Coop.cs` and `GalaxyMapScreen.Coop.cs` are identical, or differ only in the logger. So are
every co-op hook in the game files: the same message types, resync triggers, seat and helm rules (`SeatAuthority`), and
the same list of what a client may ask of the host.

The client loop (`CoopNet.ClientUpdate` there, `ASpaceCoopLink.Tick` here) was compared statement by statement. The
steps match:
- **Per tick:** drain the link; `EngineTick++` and `CoopSmoke.ClientTick`; hello once; wait for the host's save.
- **Per host frame,** up to 8 a tick: `NextFrame`, `ClientRecord`, the app's Update on that record, `EndFrame`; stop
  if it was not consumed (the pause menu); then `CoopApplyPost`, `ClientFrameDone` and `CoopSmoke.ClientFrame`.

The one difference is where the host's seconds come from. Here every `Time` read on a client is the host's
(`UnityBridge.Tick(rec.Dt)`). There, the two reads that step the game are the host's, the frame's dt and the pause
menu's (`CoopUnscaledDeltaTime`). The other reads stay on the client's clock; they drive visuals and UI cache timers
only, so nothing in the shared state depends on them, and the digests agree.

## Saves

Both copies write the same format: Core's `ObjectGraph`, under the same `SaveGame` header.

- **C# saves, either way:** a save from either C# copy loads in the other.
- **C++ saves:** both C# copies now load saves written by the C++ copy. US-T70 copied `SaveCompat`
  (`SaveStatics.cs`, `SaveWiring.cs`) and its two hooks in `UnitySpaceApp.Saves.cs` into the Unity copy.

Proof, headless: a save round trip between the two Cores, a harness built twice, once against each repo's own Core and
Bench `Simulation`:

The harness follows the same steps on each side:
1. The Bench's galaxy at JD 2461348.0, seed 1.
2. 600 ticks of 1/60 s at 1 month/s.
3. A save, then 600 ticks more.
4. A digest of the whole game (the Bench's) at the save and at the end.

| Run (2026-11-04) | Digest at the save | 600 ticks on |
|---|---|---|
| Unreal C# Core saves | `36421EA0A26852F4` | `3CFE5E6B204D5740` |
| Unity Core saves | `36421EA0A26852F4` | `3CFE5E6B204D5740` |
| Unreal C# Core loads the Unity save | `36421EA0A26852F4` | `3CFE5E6B204D5740` |
| Unity Core loads the Unreal C# save | `36421EA0A26852F4` | `3CFE5E6B204D5740` |
| each loads its own (control) | `36421EA0A26852F4` | `3CFE5E6B204D5740` |

The two save files are byte-identical. A save carries the whole game from either copy to the other, and both run it on
alike. In the game the slots live in each copy's own profile folder, so a save is moved between them by copying its file.

## Audit

`-SpaceGodAudit` (US-T48) presses every binding of the original on a scripted device and checks the state the
original's code changes. The Unity copy now runs it too: `Runtime/Audit/FeatureAudit.cs` is this file, verbatim but
for the logger and `Time.timeScale` read on Unity's main thread. Its settings and display probes read Unity's engine
where these read Unreal's. Same ids, same order:

| Run (2026-11-04) | Result |
|---|---|
| This copy, `tools/play.ps1 -Mode audit` in a fresh clone of `57c111a` | **80 PASS, 0 FAIL, 5 INFO** |
| SpaceGod.Unity.CSharp, `tools/audit.ps1` on a player of `e5efa29` | **80 PASS, 0 FAIL, 5 INFO** |

The two logs have the same 85 rows: the same ids, in the same order, with the same result each. The 5 INFO are the
same keys on both, silent for a practice fighter: helm.ftl-engage, lane-reenter, phasers, run-hot and lock.
`render.settings` lists the page identically on both: URP's 13 rows, render.msaa, render.pipeline, then HDRP's 6.

## Proof (US-T70)

| Check | This copy | The Unity copy |
|---|---|---|
| Core byte-identical | 184/184 files | (the same files) |
| Bench, 5 digests | 5 of 5 match | 5 of 5 match |
| Save cross-load | both directions, same digests (above) | (the same runs) |
| Audit | 80 / 0 / 5 INFO, fresh clone | 80 / 0 / 5 INFO |
| Co-op smoke | 15/15 checks, 54 of 54 digests identical (Shipping package of the fresh clone) | 15/15 checks, 55 of 55 identical, 0 exception lines |
| Published to C:\Apps by `run.ps1 -Force` | **not yet:** the deploy was stopped by the machine's low-memory guard while packaging; C:\Apps still holds the US-T69 build (`a53203b`) | **not yet** (queued behind it); C:\Apps holds the US-T69 build, URP only |
