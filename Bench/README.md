# Bench

A headless runner over the simulation Core, driven by fixed scenarios with a fixed seed, whose digests prove the game
is the same game on every SpaceGod track: the Unity original, the Unreal C++ port and this C# track all produce the
digests recorded in `expected-digests.json`, and `tools/bench.ps1` checks every run against them. (The C++ runner,
`Bench/Cpp`, lives on in SpaceGod.Unreal.Cpp; the sections below on comparing the two are history.)

| Runner | Core |
|---|---|
| `Bench/CSharp/` (`Bench.CSharp.exe`, .NET 10 and .NET 8) | `Vendor/Unity.SpaceGod/Core` + `Vendor/Unity.Shared/Core` (this repo's copies, see `Vendor/README.md`), compiled by `CSharp/Lib/*.csproj` (netstandard2.1, C# 9, as Unity.SpaceGod's `Tests/UnitySpace.Core` does) |

```powershell
tools/bench.ps1                         # dotnet build -c Release, then every scenario, one process each (~2 min)
tools/bench.ps1 -Scenario battle -NoBuild
tools/bench.ps1 -DotNet net8.0          # Bench.CSharp on .NET 8 (default net10.0, what UnrealSharp runs)
Bench/CSharp/bin/Release/net10.0/Bench.CSharp.exe --list
Bench/CSharp/bin/Release/net10.0/Bench.CSharp.exe --scenario sol-idle [--ticks N] [--scenarios path] [--out dir]
```

Output, per scenario (gitignored): `Bench/results/csharp-<scenario>.json` (hashes, timings, memory) and
`Bench/results/csharp-<scenario>.digest.txt` (the full digest text). Bench.Cpp writes `cpp-<scenario>.*` beside them.

## How the simulation is built (`CSharp/src/Simulation.cs`)

Exactly what `UnitySpaceApp.Awake` does, minus everything that draws, plays sound or reads input:

1. `Clock = new SimClock(startJd)` — the game uses `SimClock.StartingNow()`; the Bench uses `startJd` from
   `scenarios.json` (2461324.0, the date Unity.SpaceGod's Core tests use).
2. `Clock.SetWarp(SimClock.DefaultWarpIndex)` — the game reads the `simulation.startingWarp` setting, whose default is
   that index; the Bench never binds settings (no profile on disk is read).
3. `Galaxy = Galaxy.CreateDefault(Clock.Jd, seed)` with `seed` from `scenarios.json` (1977 = `Galaxy.DefaultSeed`).
4. Subscribes to every system's `World.War.BattleStarted` and to `Galaxy.SystemAdded` (the game toasts; the Bench counts,
   reported as `battlesStarted`).
5. `Activate(Galaxy.Systems[0])`: `Galaxy.Promote(system, Clock.Jd)` then `Galaxy.Viewpoint = system.Origin`.

Then each scenario sets `Clock.SetWarp(warp)`, runs its setup, then `Clock.SetWarp(thenWarp)` if given.

### What a tick is

One frame of `UnitySpaceApp.Update`, simulation steps only, in the game's order:

```
dt = (double)min(1.0f / fps, 0.1f)        // float arithmetic, then widened: Time.unscaledDeltaTime is a float
Clock.Advance(dt)
Galaxy.Update(Clock.Jd)
ActiveWorld.UpdateAdventures(Clock.Jd, PlayerHelioKm())
BattleTime()        // a followed ship in a new battle: battleTimeFor = it; if WarpIndex > 0, SetWarp(0)
UpdateCourseShip()  // a followed crew course (below)
```

`PlayerHelioKm()` is the active system's `Ephemeris.Helio(i)` for the first body named `Earth`, else body 0 (the game's
free camera starts beside Earth and the Bench never moves it). Before each tick the stop condition is checked; a run
stops at `ticks`, or when `simDays` of sim time have passed since the setup, or at its `until` condition.
Only `sim.Tick` is timed per tick.

## Scenarios (`scenarios.json`)

| Name | Setup | Exercises |
|---|---|---|
| `sol-idle` | none; warp 4 (1 h/s), 60 fps, 3600 ticks (2.5 days) | the game as it opens: Sol at full detail (ephemeris, lanes, traffic, law, sensors, adventures), the rest coarse every 0.25 d |
| `high-warp` | none; warp 9 (1 yr/s), 60 fps, until 365.25 days (60 ticks) | huge strides: every coarse system stepped ~24 times a frame, interstellar traffic, construction, wars, statecraft |
| `economy-year` | none; warp 8 (1 month/s), 60 fps, until 365.25 days (731 ticks) | a year of Sol's economy, trade, mining, colonies, companies, law and war at full detail in half-day steps |
| `battle` | `skirmish`: SceneTour's `TriggerSkirmish` + `WatchBattle` — 2 Battleships + Cruiser each for Sol and Reach, spawned by `Fleet.SpawnAt` off Uranus, `War.TryStartBattle(uranus, Sol)`; battle time drops warp to 0, then `thenWarp` 2 (1 min/s, the player pressing '.'); until `battle.Over` | the combat engine end to end: gunnery, missiles, subsystems, flight, retreat/surrender, the end of battle |
| `galaxy-ftl` | `crew-course`: the first non-military ship (by Id) in Sol for which `UnitySpaceApp.StartCrewCourse` succeeds (`TravelCourse.Plan` + `Galaxy.Traffic.SendPlotted`) sent to Alpha Centauri and followed; warp 6 (1 day/s); until the course ends | gate routing, interstellar traffic and gates, deep space, `Galaxy.Promote` when the followed ship comes out in another system |

Setup kinds: `none`, `skirmish` (`body`, `aggressor`, `defender` — `Sol|Belt|Reach|Independents|Pirates` of the active
world —, `classes` — `ShipClass` member names), `crew-course` (`to` — system name, `military` — optional filter on
`ship.Spec.IsMilitary`). `until`: `battle-over` (the skirmish's `Battle.Over`), `course-ended` (`UpdateCourseShip`
saw the voyage end: `stopReason` is `course-arrived` when it ended in the destination).

`UpdateCourseShip` (the game's, for a followed crew course): if the ship is destroyed the course ends; otherwise
`where = SystemHolding(ship)` (the system whose `Fleet.Ships[ship.Id] == ship`, first in `Galaxy.Systems` order);
inside a gate (`null`) nothing happens; if `where != ActiveSystem`, `Activate(where)`; then if `ship.Voyage` is null or
not `Plotted` the course ends (`arrived` = `where == destination`). The followed ship also triggers `BattleTime`.

## Digest format (version `spacegod-digest 1`)

The digest is UTF-8 text, lines ending in a single `\n` (no `\r`), no trailing spaces; tokens separated by one space.
`digestHash` is **64-bit FNV-1a** (offset `0xCBF29CE484222325`, prime `0x100000001B3`) over the digest's UTF-8 bytes,
printed as 16 upper-case hex digits. `buildDigestHash` is the same over the digest written right after the build,
before the scenario's setup (it includes the `scenario` line, so it differs per scenario name).

Value encodings:

| Notation | Encoding |
|---|---|
| `H(x)` | a double as `BitConverter.DoubleToInt64Bits(x)` → 16 upper-case hex digits (two's complement for negatives), e.g. `1.0` → `3FF0000000000000`, `-0.0` → `8000000000000000` |
| `V(v)` | a `DVec3` as `H(X),H(Y),H(Z)` (commas, no spaces) |
| `I(n)` | an int in invariant decimal, `-` sign for negatives |
| `B(b)` | `1` / `0` |
| `S(s)` | a string with every space, `\n` and `\r` replaced by `_`; null or empty → `-` |
| `F(f)` | a faction: `S(f.Prefix)`, null → `-` |
| `E(e)` | an enum: its C# member name (`ToString()`) |

Lines, in this order (`[...]` is optional, `{...}` repeats):

```
spacegod-digest 1
scenario S(name)
clock jd=H(Clock.Jd) warp=I(Clock.WarpIndex) paused=B(Clock.Paused)
galaxy systems=I(Systems.Count) nearby=I(NearbyStars.Count) intransit=I(count of Traffic.InTransit(), 0 if Traffic null) focus=S(Focus?.Name) active=S(ActiveSystem.Name) battlesStarted=I(n)
arrivals {E(p)=I(Traffic.Arrivals[p] or 0)}           -- every VoyagePurpose member in declaration order, space-separated
[course ship=S(name) to=S(dest.Name) ended=B arrived=B in=S(SystemHolding(ship)?.Name)]    -- crew-course scenarios
[skirmish                                              -- skirmish scenarios, then the BATTLE block of that battle
 BATTLE]
{ for each system s in Galaxy.Systems order (index si):
system I(si) S(s.Name) jd=H(World.Jd) coarse=B(World.Coarse) bodies=I(Catalog.Count) ships=I(Fleet.Ships.Count) settlements=I(Economy.Settlements.Count) battles=I(War.Battles.Count) ended=I(War.Ended.Count) wrecks=I(War.Wrecks.Items.Count) events=I(Events.Count) eventsHash=HEX16(FNV-1a of EVENTS)
  { body I(i) S(Catalog[i].Name) V(Ephemeris.Helio(i)) }                   -- i = 0 .. Catalog.Count-1
  { faction F(f) S(f.Name) treasury=H(f.Treasury) losses=I(War.Losses(f)) }   -- Fleet.Factions order
  { settlement I(Id) S(Name) owner=F(Owner) pop=H(Population) stock=H(Stock[0]),H(Stock[1]),... }   -- Economy.Settlements order, all GoodInfo.Count entries
  { ship I(Id) S(Name) E(Spec.Class)|- F(Faction) phase=E(Phase) destroyed=B(Destroyed) battle=B(Battle != null) voyage=B(Voyage != null) hull=H(Combat.Hull)|-[ STATE] }   -- Fleet.Ships order
  { BATTLE }                                                               -- War.Battles order
}
end
```

- `STATE` (only for ships with `Destroyed == false`), with `jd = s.World.Jd` and `st = Fleet.StateOf(ship, jd)`:
  ` pos=V(st.HelioKm) facing=V(st.Facing) speed=H(st.SpeedKmS) vel=V(Fleet.HelioVelocityOf(ship, jd))`.
  If `StateOf` throws, ` pos=! facing=! speed=!` instead of its three fields; if `HelioVelocityOf` throws, ` vel=!`
  (neither happens in the current scenarios). Make the calls in this order (state, then velocity, ship by ship).
- `BATTLE` is `battle S(Name) units=I(Units.Count) present=I(count of Units where Present) over=B(Over) winner=I(Winner)`
  followed by one line per `Units` entry:
  `unit I(Id) side=I(Side) E(Kind) F(Faction) present=B(Present) pos=V(Pos) vel=V(Vel) hull=H(Combat.Hull)|-`.
- `EVENTS` (hashed, not printed) is, for each `World.Events` entry in order,
  `H(e.Jd)|E(e.Kind)|I(e.Body)|e.Text\n` (the text raw, not `S()`-escaped). An empty list hashes to `CBF29CE484222325`.

The digest is written after the last tick, outside the timings. The runner proves its own determinism: two runs of a
scenario give the same `digestHash` (checked for sol-idle, high-warp, battle and galaxy-ftl when this was written).

## Results JSON

```
track, scenario, description, runtime, digestVersion, startJd, seed, warp, thenWarp, fps, dtSeconds,
ticksRun, stopReason (ticks | simDays | battle-over | course-arrived | course-ended), endJd, simDaysAdvanced, endWarp,
buildDigestHash, digestHash, digestBytes, digestFile,
timings { buildMs, tickMeanMs, tickP50Ms, tickP95Ms, tickP99Ms, tickMaxMs, ticksTotalMs, totalMs },
memory  { allocatedBuildBytes, allocatedRunBytes, peakWorkingSetBytes }
```

`buildMs` is `Simulation.Build` (galaxy creation); `totalMs` is build + setup + ticks (digests excluded). Percentiles
are nearest-rank (`sorted[ceil(p*n)-1]`). Allocations are `GC.GetTotalAllocatedBytes(true)` deltas (the C++ runner
reports its allocator's bytes, or `null`); peak working set is the process's `PeakWorkingSet64` (one scenario per
process).

## Sample timings (C#, .NET 8.0.33, this workstation, 2026-11-03)

| Scenario | Ticks | Sim days | Build ms | Tick mean / p50 / p95 / max ms | Total ms | Alloc MB | Peak WS MB | digestHash |
|---|---|---|---|---|---|---|---|---|
| sol-idle | 3600 | 2.5 | 4700 | 1.03 / 0.35 / 0.88 / 1035 | 8413 | 674 | 95 | `B30509691F52C9E0` |
| high-warp | 60 | 365.25 | 4679 | 653 / 622 / 837 / 1124 | 43851 | 3741 | 134 | `A8A790EC13FD23C8` |
| economy-year | 731 | 365.5 | 4760 | 59.3 / 55.2 / 78.6 / 961 | 48086 | 4301 | 146 | `7F6533416B74D14D` |
| battle | 2149 | 0.025 | 5127 | 1.72 / 0.44 / 7.84 / 125 | 8826 | 537 | 89 | `ABD80179E1D7F1AC` |
| galaxy-ftl | 745 | 12.4 | 5821 | 5.04 / 1.53 / 14.6 / 1160 | 9575 | 845 | 105 | `F1E614170D1B1960` |

The max tick is the first (JIT) or a coarse 0.25-day step of every other system landing in that frame.

## Compare (US-T37)

```powershell
tools/bench.ps1 -Track Both                  # build both runners, run every scenario on each, then compare
tools/bench.ps1 -Track Cpp -NoBuild -Scenario battle
tools/bench.ps1 -Compare                     # compare what is already in Bench/results
```

Per scenario the compare prints `IDENTICAL` with the digest hash, or the first differing line (with the `system` /
`battle` line it belongs to, two lines of context from each side and the first differing token), then both runners'
timings. Bench.Cpp reports allocations as the bytes its replaced `operator new` handed out (the C# side's
`GC.GetTotalAllocatedBytes`); its `runtime` names the compiler. On a native fault it prints a symbolised stack.

**Finding a divergence.** Both runners take `--trace N` (writes `<track>-<scenario>.trace.txt`: at tick 0, after the
setup, and every N ticks, `tick T head=<hash> s0=<hash> s1=...`, the FNV-1a of the digest's head and of each system's
block) and `--dump-at T` (the full digest at tick T as `<track>-<scenario>.t<T>.digest.txt`). Diff the two traces for
the first differing line, then dump both at that tick (or the tick before) and diff the system that differs. Tracing
is outside the timings. The root causes found so far are in docs/MIGRATION.md, "Parity findings".

**First comparison (2026-11-03, US-T37): every scenario identical.**

| Scenario | Ticks | digestHash | C# build / tick mean / p50 / p95 / total ms | C++ build / tick mean / p50 / p95 / total ms |
|---|---|---|---|---|
| sol-idle | 3600 | `B30509691F52C9E0` | 6277 / 1.79 / 0.65 / 1.73 / 12725 | 5435 / 2.38 / 1.15 / 2.25 / 14014 |
| high-warp | 60 | `A8A790EC13FD23C8` | 4679 / 653 / 622 / 837 / 43851 | 5601 / 1557 / 1513 / 1895 / 98996 |
| economy-year | 731 | `7F6533416B74D14D` | 4760 / 59.3 / 55.2 / 78.6 / 48086 | 5624 / 142 / 131 / 208 / 109071 |
| battle | 2149 | `ABD80179E1D7F1AC` | 6261 / 1.71 / 0.63 / 5.36 / 9943 | 5457 / 2.22 / 1.44 / 6.16 / 10230 |
| galaxy-ftl | 745 | `F1E614170D1B1960` | 5821 / 5.04 / 1.53 / 14.6 / 9575 | 5537 / 9.70 / 3.62 / 31.7 / 12762 |

(C#: .NET 8.0.33 RyuJIT; C++: MSVC 19.44 `/O2 /fp:precise`, not tuned. The workstation was shared with other builds,
so treat differences under ~20% as noise.) The C++ Core is 2–2.5× slower per simulation tick on the heavy scenarios
and about even on building the galaxy; see docs/MIGRATION.md for the likely causes. Peak working set: C++ 38–81 MB,
C# 85–146 MB.

## C++ performance (US-T42)

The C++ Core after tuning (whole-program optimisation, `/arch:AVX`, inline IEEE tests, an open-addressing
`cs::Dictionary` index with cheap hashes, `const cs::Ref&` where accessors and delegates copied: docs/MIGRATION.md,
"C++ performance"), raced **interleaved** against the untuned build and both .NET runtimes. Each scenario ran its four
runners back to back, three rounds; the table gives medians of each run's tick mean / p50 in ms. All 60 runs were
digest-identical to C#. The machine was shared with other builds and an UnrealEditor, which is why the C# figures sit
above the 2026-11-03 sample table. Compare within a row.

| Scenario | C++ before | C++ after | C# .NET 10 | C# .NET 8 | Build (C++ after / .NET 10) |
|---|---|---|---|---|---|
| high-warp | 1532 / 1470 | 1379 / 1282 | 1436 / 1371 | 1289 / 1209 | 4.5 / 7.8 s |
| economy-year | 138.6 / 129.6 | 127.6 / 112.8 | 161.9 / 109.3 | 136.5 / 109.1 | 4.3 / 7.3 s |
| galaxy-ftl | 8.88 / 3.31 | 7.41 / 2.90 | 8.09 / 2.43 | 7.46 / 2.45 | 4.0 / 6.3 s |
| sol-idle | 2.26 / 1.10 | 1.94 / 0.99 | 1.85 / 0.60 | 1.72 / 0.60 | 4.0 / 6.0 s |
| battle | 2.06 / 1.33 | 1.81 / 1.23 | 1.98 / 0.63 | 1.69 / 0.59 | 4.1 / 6.6 s |

The heavy scenarios are now level (within noise) with .NET 10, and the galaxy builds in ~0.6× the time. .NET keeps the
lighter median frame (sol-idle, battle). Interleaving matters: run back to back, the untuned C++ was 1.1–1.9× behind,
not the 2–2.5× of the first table above, where the two tracks were timed at different moments of a shared machine.
Concurrent GC (`DOTNET_gcConcurrent=1`, UnrealSharp's host default) measured within noise of the Bench's setting.

## The race (US-T46)

The final interleaved comparison of the three runners (after US-T49), with p99, allocations, peak memory and the
machine's background load per run, is in docs/RACE.md, section 1, beside the in-game, co-op and build numbers.
