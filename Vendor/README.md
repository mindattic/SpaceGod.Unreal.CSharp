# Vendor

Everything this repo once compiled from sibling checkouts, copied in so a fresh clone builds on its own (US-T69).
The copies are byte-for-byte the source files at the commits below; edit them here, this repo no longer follows
the originals.

| Folder | Source | Commit | Compiled by |
|---|---|---|---|
| `Unity.SpaceGod/Core/` | [Unity.SpaceGod](https://github.com/mindattic/Unity.SpaceGod) `Assets/UnitySpace/Core/**/*.cs` (184 files; the `.meta` files are left out) | `45dc06b` (includes the U.SPC-T602 gone-escort fix); `Persistence/ObjectGraph.cs` since US-T70 from SpaceGod.Unity.CSharp `1f6af6d` (its static-object walk skips the ConditionalWeakTable caches, the US-T41 fix in Core itself), so this folder is byte-identical to that copy's `Assets/UnitySpace/Core` | `CSharp/Core/SpaceGod.Core` (the game) and `Bench/CSharp/Lib/SpaceGod.Core` (the Bench), assembly `Unity.SpaceGod.Core` |
| `MindAttic.Capture/` | [MindAttic.Capture](https://github.com/mindattic/MindAttic.Capture) `src/MindAttic.Capture` (the engine) and `src/MindAttic.Capture.Windows` (the desktop host), with its `Directory.Build.props` | `c427283`, with three changes for UnrealSharp (US-T72): the Windows host targets `net10.0` instead of `net10.0-windows10.0.19041.0` (NuGet will not let the plain-net10.0 game reference a Windows-TFM project), so `Wgc/` (Windows Graphics Capture, which needs the WinRT projection) is compiled out behind `MINDATTIC_NO_WGC` in `DesktopCaptureHost.cs` and a window is captured as its screen rectangle (Desktop Duplication, then GDI: the host's own fallback); the assembly is marked `[SupportedOSPlatform("windows")]`; `CS1591` is quiet | `CSharp/Script/ManagedUnrealSpaceGodCSharp` (project reference), assemblies `MindAttic.Capture`, `MindAttic.Capture.Windows`; NuGet restores their packages (NAudio, Vortice, System.Drawing.Common, Microsoft.Extensions.*) |
| `Unity.Shared/Core/` | [Unity.Shared](https://github.com/mindattic/Unity.Shared) `unity/com.mindattic.unity.shared/Runtime/Core/**/*.cs` (8 files) | `3ce3b8a` | `CSharp/Core/Unity.Shared.Core` and `Bench/CSharp/Lib/Unity.Shared.Core`, assembly `Unity.Shared.Core` |

The UnrealSharp plugin (`CSharp/Plugins/UnrealSharp/`) was a git submodule of
[UnrealSharp](https://github.com/UnrealSharp/UnrealSharp) pinned at `115e6753b2f8c830f084a3e453b7ef3b1230eb33`; it is
now plain tracked files of that commit (826 files, its own `LICENSE` and `.gitignore` kept), so no
`git submodule update` is needed.
