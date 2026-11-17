# SpatialFabric for Unity 0.6.0 — local functional port

SpatialFabric + AudioLoom functionality implemented for Unity: stage/listener mapping,
explicit bindings and multiple OSC outputs, ADM/Custom protocols, read-only discovery,
direct-device PCM, replay/state, source-aware CSV, authoring panels and a real four-room
walking example. **Software completion is not physical/platform certification.**
See [Docs/PARITY.md](Docs/PARITY.md) and actual evidence in [VALIDATION.md](VALIDATION.md).

## SpatialFabric repositories

| Repository | Purpose |
|---|---|
| [SpatialFabric](https://github.com/NPLAudio/SpatialFabric) | Canonical shared hub: OSC specs, JSON schemas, golden fixtures and [OSC Controller](https://github.com/NPLAudio/SpatialFabric/tree/main/Apps/OSCController) |
| [SpatialFabric-Unreal](https://github.com/NPLAudio/SpatialFabric-Unreal) | Unreal plugin and four-room example |
| [SpatialFabric-Unity](https://github.com/NPLAudio/SpatialFabric-Unity) | Unity UPM port, native audio backend and four-room walking showcase |
| [SpatialFabric-Godot](https://github.com/NPLAudio/SpatialFabric-Godot) | Godot implementation |
| [AudioLoom](https://github.com/NPLAudio/AudioLoom) | Legacy standalone Unreal playback repository; access-controlled/private, integrated into SpatialFabric |

Engine implementations share contracts, not engine assemblies. Validation for one engine
or platform does not certify another.

## Use the room showcase

Open `ExampleProject/` in Unity **6000.6.9f1**, then
`Assets/SpatialFabricShowcase/Scenes/RoomShowcase.unity`; press **Play**.
The example uses **URP 17.6.0 / Forward+**, including URP Lit room materials and a
URP depth-tested sign shader. The core package remains render-pipeline independent.

Four enclosed rooms share a hallway: spatial motion, warm percussion, stereo groove,
glass replay. **WASD/mouse**, **Esc** cursor, **E** play room cue, **Q** stop, **X** stop all.
Real HUD buttons expose cue/loop/replay/routing/OSC controls; no automatic sound/listening.
Original prepared cues are assigned. See [ExampleProject/README.md](ExampleProject/README.md).
Visual walls/OSC positions do not spatialize or acoustically isolate direct PCM.

The controller profile is `ExampleProject/OSCControllerProfile.json` (cue bases only; the ports are the standard ones). All SpatialFabric engines share the same default ports so one controller profile works everywhere: spatial listener `8100`, playback control `9000`, playback state out `9001`, ADM out `50018`, Custom example out `9002`. Only one engine app can own the input ports at a time (binds are exclusive); a second engine fails with a clear bind error instead of sharing silently. To run two engines at once, change the ports on one of them.
The original small SpatialExample scene remains a regression fixture, not the main demo.

## Live input (microphone / line in)

`LiveInputCue` routes a **selected capture device and channel** to a **selected output device and
channel** through the same native direct-device path as `PlaybackCue` (no Unity `Microphone`,
`AudioSource` or mixer). Selection mirrors output: explicit **refresh** of input/output devices, exact
configured IDs with **no default fallback**, input channel `0` = all / `1..N` = one input, output
channel `0` = corresponding / `1..N` = one output. Nothing opens a microphone until an explicit
**Start live** (OSC `/{base}/play`, inspector button). `/{base}/stop`, `/{base}/gain` (float 0..4),
observed `/{base}/state` and a read-only `live_input` discovery record are provided.

**Live means the native worker really started, not that Start was requested.** Latency is a bounded
ring (about twice the requested buffer) with a drift servo; underruns/overruns are counted and the input
peak is reported. The OS may prompt for microphone permission (Windows privacy setting for desktop apps;
macOS `NSMicrophoneUsageDescription`). **Monitoring a mic through nearby speakers causes feedback:** use
headphones or low gain. Exclusive mode applies to the output only.

Tested silently only: offline bridge tests plus invalid devices. **No real microphone has been opened
by any test and physical latency/quality is unverified. macOS capture is unbuilt source.**

## Release player (Windows x64)

`python Tools/build_player.py --unity <Unity.exe> --output <new empty dir>` builds the four-room showcase as a
**non-development Mono player**, runs its headless smoke checks, renders five views from the release build, and writes a ZIP,
`SHA256SUMS.txt` and `BUILD_MANIFEST.json`. It is **unsigned** (expect the Windows warning), Windows only (no macOS, no IL2CPP),
silent in testing, and **E plays through the default output device**. The editor must be closed.

## Install the package

Local source: Package Manager → Install package from disk →
`Packages/com.nplaudio.spatialfabric/package.json`. Close Unity and build/install native
output first for PCM: `python Tools/build_native.py --install` (MSVC 2022 x64 + Windows SDK).

Local distribution: `python Tools/package_unity.py --include-built-native` creates
`dist/0.6.0/com.nplaudio.spatialfabric-0.6.0.tgz`, CompleteSource ZIP, manifest and checksums.
The manifest records the full Git HEAD in `source_commit`: exact source revision for a
clean committed snapshot, base revision only for a local uncommitted snapshot. Failed
Git inspection is marked unknown. This does not certify locally built native binaries.
The tarball includes the locally built Windows plugin and an importable four-room sample;
Package Manager → Install package from tarball. Install URP before importing **Four-room
walking showcase**; in a new project explicitly assign the sample's `Rendering/ShowcaseURP.asset`
in Graphics and each Quality level. Keep your existing pipeline in an existing URP project.
Import never changes project settings automatically. Built-in/HDRP sample materials are not supplied.

**GitHub Actions is disabled by request across the related repositories. Validation is local;
existing workflow files and historical run links do not indicate active automation.**
Git source publication is separate from release publication. Generated native binaries,
archives and local validation logs are not committed; installable artifacts are built explicitly.

Runtime dependencies are Unity engine modules only, not a third-party OSC/audio runtime.
The native plugin bypasses Unity AudioSource/mixer. WebGL/Linux hardware output is not
advertised. Core Audio **ARM64 native compilation and silent tests now pass hosted macOS CI**;
Mac Unity/player, Intel/universal and physical audio acceptance remain unverified.
See [Native/README.md](Native/README.md) and the validation addendum.

## Editor / routing

**Tools → SpatialFabric → Open Window** provides Stage, Objects, Outputs, Playback, CSV
and Diagnostics panels. Opening panels never starts transports or prepares PCM.

- Stage: dimensions/flips, Off/position-only/position+orientation virtual listener, radar
  Top/Front/Side/isometric and bounded view-only history. Authored stage never follows.
- Objects: bind selected scene objects, explicit pose/label, bulk sequential IDs/mute,
  per-protocol and per-output ID/field overrides, standard multi-object Undo.
- Outputs: any number of independent ADM/Custom destinations with individual mode, rate,
  enable and successful-frame change cache. A nonempty list replaces legacy endpoints.
- Playback: prepared-source/loaded-clip assignment, explicit device refresh/prepare/play/
  stop/release, loop/replay, actual cached worker/format state, multi-cue inspector.
- CSV: bounded full-file preview/confirm/one Undo; v1 route compatibility and v2 exact
  GUID/localFileId source reassignment. Explicit Apply may resolve selected assets, never
  starts native preparation/devices/transport. See [Docs/RoutingCsv.md](Docs/RoutingCsv.md).
- Diagnostics: loaded-world references, configured vs bound sockets, errors/identity repair.

Owner scene is the default world. `additionalScenePaths` explicitly opts loaded additive
scenes into bindings/playback/discovery; unrelated scenes stay excluded. Saved editor
identity survives reload; live prefab instances get distinct session keys without query
mutation. Optional additive listener references require explicit stage opt-in too.

Custom XYZ/AED packed/discrete arguments, aliases, fields, ranges and strict preflight
are documented in [Docs/CustomProtocol.md](Docs/CustomProtocol.md). UDP/change filtering is
not renderer acknowledgement. Stop OSC never stops PCM; closing the window never stops
remote audio. Scene/Play/reload/quit lifecycle releases native resources.

## Validate

```sh
python Tools/build_native.py --install
dotnet run --project Tools/CoreChecks/CoreChecks.csproj -- Packages/com.nplaudio.spatialfabric/Tests/EditMode/Fixtures/osc-v1.json
python -m unittest discover -s Tools -p 'test_*.py'
python Tools/sync_interop.py --check
python Tools/validate_unity.py --unity "C:/Program Files/Unity/Hub/Editor/6000.6.9f1/Editor/Unity.exe" --graphics --require-native
python Tools/validate_rooms.py --unity "C:/Program Files/Unity/Hub/Editor/6000.6.9f1/Editor/Unity.exe"
python Tools/validate_interop.py --unity "C:/Program Files/Unity/Hub/Editor/6000.6.9f1/Editor/Unity.exe" --interop "../SpatialFabric" --rooms
python Tools/package_unity.py --include-built-native
python Tools/validate_package.py --unity "C:/Program Files/Unity/Hub/Editor/6000.6.9f1/Editor/Unity.exe" --package dist/0.6.0/com.nplaudio.spatialfabric-0.6.0.tgz
```

A separate batch `ValidatePersistence.Run` gate exercises saved-scene/additive unload
without disrupting NUnit's scene. Reports must contain actual nonempty passing checks;
process exit alone is insufficient. All automatic playback validation is silent/offline
or intentionally invalid-device. Real listening/latency/exclusive/hot-unplug acceptance
still requires target hardware.

IL2CPP smoke tooling is supplied (`Tools/validate_player.py --il2cpp`). This editor lacks
that module; CLI installation reported administrator rights required. Install with user
approval before claiming IL2CPP passed. Mac native source requires a real Mac SDK/runner.

## Repository boundaries

UPM package under Packages/, example under ExampleProject/, native C ABI under Native/,
portable/editor/build probes under Tools/. The canonical SpatialFabric hub contains shared
contracts/golden fixtures (vendored with hashes); SpatialFabric-Unreal is the Unreal port.
`sync_interop.py` defaults to sibling `SpatialFabric`, with a fallback for an existing
local `SpatialFabric-Interop` checkout; `--interop` selects an explicit hub checkout.
Generated Library/build state, credentials and locally compiled DLL/dylib files are not
committed. CompleteSource is filtered; archives/checksums are local development artifacts.
