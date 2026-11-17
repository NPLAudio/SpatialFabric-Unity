# SpatialFabric Unity 0.7.0

## Changes

- **Baked path playback.** Author a path in [Path Studio](https://github.com/NPLAudio/SpatialFabric/tree/main/Apps/PathStudio),
  import it with *Tools > SpatialFabric > Import Path Studio Path...* (validated before an asset is created), add a
  `SpatialPathPlayer` to an object, assign the asset and a `SpatialStage`, and call `Play()`. A pure-C# strict JSON reader,
  validator, sampler and stage mapping (`NPLAudio.SpatialFabric.Paths`) reproduce the shared
  [Path v1](https://github.com/NPLAudio/SpatialFabric/blob/main/Specs/Path-v1.md) golden vectors within 1e-9. Position
  only; nothing moves until you call `Play()`/`Seek()`/`Evaluate()`; no audio, OSC or network.
- **Scene context export.** *Tools > SpatialFabric > Export Path Studio Context...* writes a
  [Context v1](https://github.com/NPLAudio/SpatialFabric/blob/main/Specs/Context-v1.md) file (boxes, listener and spatial
  objects in canonical stage space) so Path Studio can show your scene around a path. Boxes are conservative stage-space
  AABBs, and the authored stage frame is used (virtual-listener follow is ignored).
- Packaging inherits the 0.6.0 gates: clean source snapshot, no debug backups, hash-verified runtime notices.

## Downloads and use

- `com.nplaudio.spatialfabric-0.7.0.tgz`: Unity Package Manager package, including the
  importable URP four-room sample and Windows native library.
- `SpatialFabric-Unity-0.7.0-CompleteSource.zip`: full source and example project.
- `SpatialFabricRoomShowcase-Unity-0.7.0-Windows-x64.zip`: non-development Windows
  standalone showcase; extract the entire folder and run its EXE.

Open `ExampleProject` in Unity 6000.6.9f1. Use **Tools → SpatialFabric → Open four-room
showcase**, or `Assets/SpatialFabricShowcase/Scenes/RoomShowcase.unity`.
For a UPM install, import **Four-room walking showcase** from Package Manager and open
`Assets/Samples/SpatialFabric/0.7.0/Four-room walking showcase/Scenes/RoomShowcase.unity`.
Install URP first; assign the sample's `Rendering/ShowcaseURP.asset` in Graphics and each
Quality level for a new project (retain your pipeline in an existing URP project).
The package stores the sample at `Samples~/FourRoomShowcase`.

Use the [shared hub OSC Controller](https://github.com/NPLAudio/SpatialFabric/tree/main/Apps/OSCController).
Load `ExampleProject/OSCControllerProfile.json` from CompleteSource, or
`OSCControllerProfile.json` beside the player EXE, and explicitly
start Unity OSC in the intended editor/play context. Inputs default to 8100/9000;
monitor is 9001. Mono play address: `/unity/playback/mono/play`. Entering Play mode
stops editor networking; explicitly restart it in the desired context.

## Verification / limits

EditMode: 71 tests in an isolated copy (69 passed, 2 skipped, 0 failed), including 11 for the path
player on a rotated, scaled and flipped stage. 188 core checks (fixtures, strict JSON reader limits,
validator, sampler, stage mapping, fuzz) and 45 Python checks (two platform-dependent skips) pass.
PlayMode tests were not re-run for 0.7.0; the standalone player passes its silent smoke and render
checks. The new editor menus' file dialogs and the inspector preview slider were compile-checked,
not exercised interactively. Standalone build evidence and checksums accompany the player download.
Automated checks use synthetic state, offline rendering or invalid devices and do not prove
physical audio output.

Nothing listens or plays at startup. Audio bypasses Unity's mixer; choose the correct
physical device/channel and maintain an independent mute. Windows executables are
unsigned. Windows x64/Mono is the provided player; no macOS/IL2CPP certification or
hardware/latency certification is claimed. Preserve included licenses/notices.
