# SpatialFabric for Unity 0.7.0

Functional SpatialFabric/AudioLoom port: stage/listener mapping, explicit pose/object
bindings, multiple independently configured ADM/Custom destinations with target overrides,
read-only scene discovery, native PCM playback, loop/replay/state, source-aware routing
CSV, multi-object authoring panels and Top/Front/Side/isometric/history radar.

Opening components/windows/profile data never starts networking, prepares PCM or plays.
AudioLoom parity adds deterministic random min/max replay, opt-in `playOnStart` (false by
default; playing-world Start only), derived unique blank OSC bases, duplicate-address
validation and active-route restart on the main tick. Explicit serialized OSC bases remain
unchanged. `/ack` and `/state` feedback never receives acknowledgements. New native builds
also expose optional output mode/OS latency diagnostics; old ABI-v1 plugins remain usable.
See [PCM / lifecycle](Documentation~/index.md#pcm--lifecycle) for exact policies and limits.

Configure the current Editor/Play context and explicitly Start OSC. Prepare/Play and device
refresh are explicit unless runtime startup/replay has been opted into. Native output never falls back to Unity AudioSource/mixer. State is
observed worker lifecycle, not request acceptance or physical audio confirmation.

OSC cue commands reply to the manager's monitor destination (default `127.0.0.1:9001`),
not the sender: `<base>/ack ,ssis command result playing detail`. Results are `received`,
`ignored`, `rejected`, or `failed`; playing is observed int32 0/1. A received `/play`
only accepts a start request: worker startup emits `/state 1`, or a later failed play
ack explains startup failure. Playback prepares an assigned PCM asset/loaded clip on demand;
no source/backend/device means failed, never a fabricated playing state. Local or OSC stops
cancel pending startup feedback. `/status` is address-only and replies with ack plus current
float `/state`; `/stop` also sends state even when already stopped. Incoming `/ack` and
`/state` are discarded without a reply to prevent reflected feedback loops. Unknown suffixes and inapplicable commands
(playback gain/live loop) are rejected. Loop nonnumeric payloads are ignored; live gain
must be numeric 0..4. Float arguments take precedence over integers. Golden ack packets
and decisions are vendored in `Tests/EditMode/Fixtures/ack-v1.json`; silent regression tests
use synthetic observations, missing sources, and deliberately invalid device IDs only.
Acknowledgements remain best-effort UDP, not reliable delivery or proof of audible output.

Windows WASAPI code/native/editor/Mono player validation is local. Core Audio source is
provided but is **not compiled/validated on this Windows host**. Windows IL2CPP support
was installed with approval; actual Development and non-Development IL2CPP validation
players passed. This does not certify arbitrary stripping profiles or new releases.
Physical channels/fidelity/latency/exclusive/device-loss acceptance remains external.
Do not advertise those gates as passed. See the source repository's VALIDATION.md/PARITY.md.

A nonempty Outputs list replaces legacy endpoints. Object ID, ADM/Custom overrides and
per-output keys/fields are explicit. Owner scene is the default world; additional loaded
scene paths opt into the same scope. Saved scene/editor keys persist; live prefab instance
keys are unique within a play session. Query never repairs IDs or enumerates hardware.

The packaged Four-room walking showcase sample contains original sounds, hallway/rooms,
walking and real HUD/keyboard controls. Open its Scenes/RoomShowcase.unity and press Play.
No automatic sound/listening. E play room cue, Q stop, X stop all, Esc cursor. Visual walls
and OSC positions do not spatialize/isolate direct PCM. Raw source/example tooling remains
in the CompleteSource repository snapshot.

CSV v1 remains compatible; v2 explicitly reassigns exact GUID/localFileId source references
only on confirmed Apply, with type/hash/stale/live guards and one Undo. Full panels are
under Tools → SpatialFabric → Open Window. Engine modules are the only runtime dependencies.

## Central scene authoring and audio routing

Open **Tools → SpatialFabric → Open Window**. **Scene Objects** includes ordinary,
unaltered scene objects and supports explicit capability attachment, live selection,
real mixed-value properties and Undo. Individual Inspectors remain available.
**Outputs** defaults to a true cue-row × interface/channel crosspoint grid, with
selectors also available on expanded Playback cues. Only **Refresh Interfaces**
enumerates devices; exact-ID cached offline layouts survive unplugging. Checks mean
configured, not live signal. Scene route edits are guarded and undoable, never
implicit audio/capture/network activation.

See [Scene Objects](https://github.com/NPLAudio/SpatialFabric-Unity/blob/main/Docs/SceneObjectManager.md)
and [Audio Routing](https://github.com/NPLAudio/SpatialFabric-Unity/blob/main/Docs/AudioRoutingMatrix.md)
for scope, protected targets and the 139 EditMode + 6 PlayMode validation gate.

## Baked paths from Path Studio

Path Studio paths ([Path v1](https://github.com/NPLAudio/SpatialFabric/blob/main/Specs/Path-v1.md),
`*.sfpath.json`) can drive a Transform deterministically in Unity, with no network.

1. Author the path in Path Studio and save it.
2. Tools → SpatialFabric → Import Path Studio Path... validates the file (problems are listed in a
   dialog and nothing is created when invalid) and stores it as a `SpatialPathAsset` in a folder
   under Assets that you pick.
3. Add `SpatialPathPlayer` to the object to move, assign the asset and a `SpatialStage` (empty =
   the first stage in the same scene; empty track id = first track).
4. Call `Play()` (or tick `playOnEnable`; the default is off, so nothing moves by itself). `Pause()`,
   `Stop()` (rewinds), `Seek(seconds)` and `Evaluate(seconds)` (move without changing play state) are
   also available; the inspector has a preview slider with Undo.

Mapping: positions are canonical `[front, right, up]` normalized to the stage half extents
(Unity stage-local `x = right`, `y = up`, `z = front`, honoring the stage flips and the same .05 m
minimum half extent as the exporter). The result goes through the stage transform, so a rotated,
scaled or moved stage carries the path with it. Only the position of the target is written
(rotation/scale are untouched); a `SpatialObject` with a bound transform moves that transform.
Loop, ping-pong, speed, easing and interpolation follow the spec and match its golden vectors
(`Tools/CoreChecks`). Optional `gain`/`spread` curves are exposed read-only as `Gain`/`Spread`
and not applied to anything. Invalid assets, a missing stage or an unknown track move nothing,
set `LastError` and log one warning. The player never touches audio, OSC, the network or
SpatialFabricManager. Stage listener-follow modes are not applied to path positions.

## Path Studio scene context export

Tools → SpatialFabric → Export Path Studio Context... writes a read-only reference file
([Context v1](https://github.com/NPLAudio/SpatialFabric/blob/main/Specs/Context-v1.md)) that
Path Studio draws behind paths. Needs a SpatialStage in the active scene (the first one found is
used; the summary dialog names it when there are several).

- Scope: the selected GameObjects (children only when they carry an enabled Renderer/Collider);
  with no selection, every active object in the active scene with an enabled Renderer or Collider
  (UI/Canvas, hidden and EditorOnly objects skipped).
- Output: each such object as a `box`, the stage's assigned listener as a `listener` with yaw
  (0 faces +front, positive turns toward the left), and each SpatialObject as a `point` at its Pose.
  Ids are the sanitized hierarchy path (`[A-Za-z0-9_./:-]`, unique, 128 max).
- Boxes are conservative axis-aligned bounds in stage space: rotated objects export the AABB of
  their eight corners, so they may be larger than the object. Positions are stage-normalized and
  not clamped to ±1 (clamped to the spec's ±64 only).
- The authored stage frame is used; listener follow mode is ignored. The scene is never modified.
  Nothing is sent, played or networked; the file is written atomically to the path you choose.
