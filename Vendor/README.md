# Vendor

Everything this repo once compiled from sibling checkouts, copied in so a fresh clone builds on its own (US-T69).
The copies are byte-for-byte the source files at the commits below; edit them here, this repo no longer follows
the originals.

| Folder | Source | Commit | Compiled by |
|---|---|---|---|
| `Unity.SpaceGod/Core/` | [Unity.SpaceGod](https://github.com/mindattic/Unity.SpaceGod) `Assets/UnitySpace/Core/**/*.cs` (184 files; the `.meta` files are left out) | `45dc06b` (includes the U.SPC-T602 gone-escort fix) | `CSharp/Core/SpaceGod.Core` (the game) and `Bench/CSharp/Lib/SpaceGod.Core` (the Bench), assembly `Unity.SpaceGod.Core` |
| `Unity.Shared/Core/` | [Unity.Shared](https://github.com/mindattic/Unity.Shared) `unity/com.mindattic.unity.shared/Runtime/Core/**/*.cs` (8 files) | `3ce3b8a` | `CSharp/Core/Unity.Shared.Core` and `Bench/CSharp/Lib/Unity.Shared.Core`, assembly `Unity.Shared.Core` |

The UnrealSharp plugin (`CSharp/Plugins/UnrealSharp/`) was a git submodule of
[UnrealSharp](https://github.com/UnrealSharp/UnrealSharp) pinned at `115e6753b2f8c830f084a3e453b7ef3b1230eb33`; it is
now plain tracked files of that commit (826 files, its own `LICENSE` and `.gitignore` kept), so no
`git submodule update` is needed.
