# MindAttic project agent entrypoint

Read the shared protocol at ..\mindattic-agent-standard\AGENTS.md and this project's own
documentation. Common prompt commands are implemented by the shared runner; do not add a second
copy under a provider-specific command folder.

This repo (SpaceGod.Unreal.CSharp) is the Unreal Engine C# copy of SpaceGod: Unity.SpaceGod's own Core,
vendored unchanged in `Vendor/Unity.SpaceGod/Core/`, running inside Unreal through UnrealSharp (vendored
as plain files in `CSharp/Plugins/UnrealSharp/`). It is self-contained: never add a reference to a
sibling checkout or a submodule; copy what is needed under `Vendor/` and record its source and commit
in `Vendor/README.md`. It is an archival candidate beside SpaceGod.Unreal.Cpp and SpaceGod.Unity.CSharp;
keep the game the same as theirs (the Bench's digests in `Bench/expected-digests.json` must match).
Unreal Engine is not installed on every workstation; when it is missing, say so instead of claiming
a build passed.
