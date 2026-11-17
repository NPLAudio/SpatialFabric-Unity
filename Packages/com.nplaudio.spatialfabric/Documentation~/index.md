# SpatialFabric / AudioLoom Unity port 0.6.0

## Authoring and routing

Tools → SpatialFabric → Open Window: Stage, Objects, Outputs, Playback, CSV, Diagnostics.
Create an authored stage/manager, bind selected scene objects, or assign an explicit pose
transform/label. Bulk sequential IDs/mute and multi-object inspectors are Undoable; prefab
instance property changes are recorded. No startup network/audio action.

A nonempty Outputs array replaces legacy endpoints. Each stable output key defines
ADM/Custom, IP/port, rate/change policy and coordinate/template configuration. Per-binding
overrides select enable/ID/fields for each destination. Independent destinations cache
successful actual packet bytes plus address, so failed frames never suppress retries.

Stage full dimensions are front/right/up meters (Unity Z/X/Y), with independent flips.
Virtual listener follow can be Off, PositionOnly or PositionAndOrientation, without moving
the authored stage or implicitly using camera-eye offsets. Radar projections and bounded
history reflect authoring transforms, not renderer acknowledgements; clicks select objects.

Owner scene is the default world. Additional loaded scene paths explicitly expand bindings,
playback and discovery; unrelated scenes remain excluded. Additive listener references also
require stage opt-in. Saved editor identity survives reload; cloned runtime instances have
unique session keys, including inactive instances. Discovery does not synthesize GUIDs.

## Protocols

ADM XYZ is right/front/up; XYZ/AED share one sphere-limited point, modes XYZ/AED/Both.
Metadata gain/width/mute/name remains separate from direct PCM. Custom supports packed or
discrete XYZ/AED, aliases, remapped/reverse ranges, built-in/per-object fields and strict
whole-frame preflight. Unknown/unresolved/non-finite/invalid addresses are not partly sent.
Rates and change-only policies are per destination; UDP/change filtering is not guaranteed
delivery. See source Docs/CustomProtocol.md for exact argument/address conventions.

## PCM / lifecycle

Native shared/exclusive WASAPI, immutable prepared float PCM, bounded WAV importer,
loaded Decompress On Load clip capture, corresponding/selected channels, linear rate
conversion, native loop/EOF/drain, observed start/end counters and unscaled cancellable
natural-end replay. Stop/disable cancels pending replay. Failed configured devices never
become default or emit false Playing. Explicit refresh enumerates devices; queries do not.

Channel 0 maps corresponding source/device channels; 1..N maps source channel 0 to one
physical output. Requested settings and cached actual worker/format state remain distinct.
No AudioSource fallback. Visual walls/OSC positions do not attenuate/spatialize this PCM.

macOS Core Audio source is present, but this Windows host cannot certify its compile or
hardware behavior. IL2CPP module installation is an administrator prerequisite here.
Physical device/fidelity/latency/exclusive/hot-unplug checks remain external. Linux/WebGL
hardware output is not advertised. See source Native/README.md and validation reports.

## Explicit CSV transactions

V1 scene-keyed route files remain compatible. V2 playback exports GUID:localFileId source
references, not guessed labels/machine paths. Preview validates all rows and captures hashes;
confirmed Apply resolves correct typed assets, rechecks idle/stale targets, then changes all
rows/sources in one Undo. Assets may load on explicit Apply, but native PCM/devices/transport
never start. No source file is silently loaded by a query; assets/scenes are not auto-saved.
Exact schemas/security limits are in source Docs/RoutingCsv.md.

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

## Discovery v1

`/spatialfabric/scene/query ,siii id reply_port offset limit` (short `,si` accepted).
Responses `/spatialfabric/scene/response ,siis id index count json_chunk`. Sender-IP only,
remote disclosure off by default, bounded requests/rates/queues/pages/UTF-8 chunks. Local
main-thread API: `SceneInventory.GetJson(manager,offset,limit)`.

Schema `org.nplaudio.spatialfabric.scene`, version 1. World position_cm maps Unity Z/X/Y
to canonical front/right/up centimeters; optional native Euler field is engine-specific.
Unknown fields are forward-compatible. Pages are not globally atomic snapshots. Cached
backend/targets/fields/loaded scenes are inventory, not hardware or transport acknowledgements.

Closing the window/stopping OSC never stops PCM. Cue disable, editor reload, play transitions,
scene unload and quit release native resources. timeScale zero/Editor Pause are serviced;
OS suspension is not a realtime guarantee. No hidden startup sender/listener is installed.
