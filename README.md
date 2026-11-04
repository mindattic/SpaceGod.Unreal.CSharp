# SpaceGod – Unreal C# (SpaceGod.Unreal.CSharp)

> **Archival candidate.** One of three self-contained copies of the same game (US-T69):
> [SpaceGod.Unity.CSharp](https://github.com/mindattic/SpaceGod.Unity.CSharp) (the Unity original),
> [SpaceGod.Unreal.Cpp](https://github.com/mindattic/SpaceGod.Unreal.Cpp) (Unreal, C++) and this one (Unreal, C# through
> UnrealSharp). One will be promoted to work in progress and the other two archived; the race that compared the
> Unreal tracks is in [docs/RACE.md](docs/RACE.md).

The real Sol system in Unreal Engine 5, played co-op: Unity.SpaceGod's own C# simulation Core running inside Unreal
through [UnrealSharp](https://github.com/UnrealSharp/UnrealSharp). The planets, dwarf planets, moons and asteroids sit
where they really are for today's date, placed from JPL orbital elements, and you and your friends fly around them
together on one shared clock.

This repo was split out of [Unreal.SpaceGod](https://github.com/mindattic/Unreal.SpaceGod) (whose history it keeps) and
holds the C# track alone. It is **self-contained**: a fresh clone needs no sibling repos and no submodules.

| What | Where | Came from |
|---|---|---|
| The simulation Core (unchanged) | `Vendor/Unity.SpaceGod/Core/` | Unity.SpaceGod `Assets/UnitySpace/Core` at `45dc06b` |
| Unity.Shared's Core (profiles, settings.json) | `Vendor/Unity.Shared/Core/` | Unity.Shared `unity/com.mindattic.unity.shared/Runtime/Core` at `3ce3b8a` |
| The UnrealSharp plugin | `CSharp/Plugins/UnrealSharp/` (plain files) | UnrealSharp `115e6753` (was a submodule) |
| The ported shaders and materials | `Shared/Plugins/SpaceGodRender/` | this repo |

Details in [Vendor/README.md](Vendor/README.md).

## Needs

- Unreal Engine 5.8 (found by `tools/engine.ps1`: the registry, the launcher's list, `D:\Games\UE_5.8` and the other
  usual folders, or set `UE_ROOT`).
- Visual Studio 2022 with the "Game development with C++" workload (UnrealSharp's glue and the small C++ module).
- A .NET 10 SDK: `tools/dotnet-sdk.ps1` installs a private one under `CSharp/Intermediate/DotNet` when the machine's
  cannot build UnrealSharp.

## Deploy and play

```powershell
./run.ps1 -Launch          # pull, package Shipping, publish to C:\Apps\SpaceGod.Unreal.CSharp\, refresh the shortcut, play
./run.ps1 -Force -Launch   # the same even when nothing changed
./run.ps1 -LauncherOnly    # only (re)create the Desktop shortcut
```

`run.ps1` never prompts. The Desktop shortcut **SpaceGod – Unreal C#** opens `run.bat`, which deploys (only
when something changed) and then starts the game, with no menu. `deploy.bat` asks 1) Deploy & Run or 2) Run (Run
deploys first when nothing is published yet).

## Building and running from the checkout

```powershell
./tools/build.ps1                                   # editor target + UnrealSharp glue + Script/ + the Core
./tools/play.ps1 -Mode smoke                        # headless: build the Galaxy, step it, log Earth's position, quit
./tools/play.ps1                                    # game window
./tools/play.ps1 -Mode host                         # co-op: a listen server on UDP 7777
./tools/play.ps1 -Mode join -Address 192.168.1.20   # join a host
./tools/play.ps1 -Mode coop-smoke                   # headless: a host and a client on localhost, digests compared
./tools/package.ps1 -Configuration Shipping         # Builds/CSharp/Windows/Unreal.SpaceGod.CSharp.exe
./tools/play.ps1 -Mode coop-smoke -PackageDir Builds/CSharp/Windows   # the co-op smoke from a package
./tools/bench.ps1                                   # the headless Bench; checks the five recorded digests
```

`-Track CSharp` is still accepted by the tools (the only track here). In game, `Host` in the `` ` `` console turns a
solo session into a hosted one, and `open <ip>` joins one.

## Co-op

One player hosts a listen server and the others join it. The server owns the simulation clock (the date, the time warp
and pause), so everyone sees the planets in the same places; any player can change the time, and everyone is told who
did. Each player flies their own camera and sees the others as markers. A camera is shared as a frame body plus a true
offset in km, so players in different scale modes still agree on where they are.

## Controls

Unity.SpaceGod's own bindings, unchanged (H / F1 in game shows them all).

## Layout

- `CSharp/UnrealSpaceGodCSharp.uproject`: the UE 5.8 project (plugins UnrealSharp and SpaceGodRender).
- `CSharp/Script/ManagedUnrealSpaceGodCSharp/`: the game's managed code, Unity.SpaceGod's `Runtime/` on UnrealSharp
  (`Runtime/`, `UnityCompat/`: Unity's API over Unreal).
- `CSharp/Core/`: the csprojs that compile the vendored Core (`SpaceGod.Core`, `Unity.Shared.Core`) and `SaveCompat/`.
- `CSharp/Source/UnrealSpaceGodCSharp/`: the small C++ module (audio, GUI overlay).
- `Shared/Plugins/SpaceGodRender/`: Unity.SpaceGod's shaders ported to Unreal, with their materials.
- `Bench/`: the headless Bench over the Core (`Bench/README.md`); `Bench/expected-digests.json` holds the digests every
  SpaceGod track produces.
- `tools/`: build, package, play, race, bench. `tools/unity_extract.py` is an optional one-time importer from a built
  Unity player; its output is committed.
- `docs/`: the migration, faithfulness, persistence and race write-ups (written while the repo held both tracks; the
  C++ track now lives in SpaceGod.Unreal.Cpp).

The game needs no level: it plays on the engine's empty `/Engine/Maps/Entry` and builds everything at runtime.
