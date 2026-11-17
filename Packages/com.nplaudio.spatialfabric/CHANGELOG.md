# Changelog

## Unreleased

- Editor: Tools → SpatialFabric → Export Path Studio Context... writes a Context v1 scene file
  (stage-normalized, unclamped boxes/listener/points; conservative AABBs; follow-listener ignored)
  for Path Studio. Pure-C# core (`ContextExportCore`) covered by `Tools/CoreChecks`. Nothing is
  sent or played.

## 0.6.0 — acknowledgement/status feedback and example startup

- Standard OSC `/ack` received/ignored/rejected/failed results and `/status` snapshots
  go to the configured monitor destination (default 9001); request acceptance remains
  separate from observed worker state.
- Request-version tracking prevents stale asynchronous start-failure replies after
  cancellation; playback prepares stale/unprepared PCM on demand.
- Shared acknowledgement/decision fixtures and device-free lifecycle/invalid-device tests.
- Interactive editor startup restores the saved four-room example only from a clean
  empty/default scene, preserving dirty, saved and multi-scene work. No autoplay/networking.
- Shared contracts/controller now live in the canonical SpatialFabric hub; Unreal and
  Godot ports are SpatialFabric-Unreal and SpatialFabric-Godot.
- Live input: `LiveInputCue`, native C ABI additions (`sf_live_*`, `sf_input_devices_json`), OSC play/stop/gain/state, discovery record and inspector. Silent tests only; macOS capture unbuilt.

## 0.5.0 — functional completion / deployment gates

- Independent protocol destinations, per-target IDs/fields/enables, explicit bound poses
  and labels, position-only listener mode, opt-in loaded additive-world scope.
- Stage/Objects/Outputs/Playback/CSV/Diagnostics panels, bulk binding/ID/mute workflows,
  multi-cue inspector, isometric/trails, saved/prefab/runtime identity lifecycle coverage.
- Playback CSV v2 exact source references with metadata hash/type/local-ID validation and
  atomic Undo; v1 compatibility retained. Native readiness invalidates on source changes.
- Plain-pointer retained-handle ABI adapter for AOT deployment; IL2CPP build/smoke tooling.
- Core Audio implementation/source/universal build scripts and native macOS CI definition.
  **Mac compilation/hardware not validated on this Windows host.**
- Deterministic local UPM + importable full room sample / CompleteSource / checksums;
  clean consumer project build/run tooling. No remote/release publication implied.

Physical listening/exclusive/device-loss certification remains external. IL2CPP is blocked
by the missing editor module; CLI installation requires administrator approval.

## Four-room showcase milestone

- Saved RoomShowcase scene: continuous foundation, ceilings/partitions, hallway and four
  capsule-accessible rooms; original WASD/mouse visitor, no template characters.
- Original warm-percussion mono, stereo groove and glass replay WAVs/prepared assets.
- Real explicit in-game/keyboard cue, loop/replay/stop-all, device and OSC controls.
- Fixed stage, four non-colliding OSC objects/three animated, concise depth-tested signs.
- Silent editor/player walking/collision/PCM/motion checks and actual room renders.

## 0.3.0 — Custom protocol and routing authoring

- Custom XYZ/AED packed/discrete args, aliases, remapped/reversed ranges, built-ins and
  per-object fields, strict unresolved-token/invalid-frame preflight.
- Independent ADM/Custom rate and successful-frame change-only caching; ADM XYZ/AED/Both.
- Scene-v1 JSON keeps Custom field dictionaries as objects with bounded finite encoding.
- Scene-keyed spatial/playback routing CSV: bounded parser, full preview, one Undo,
  stale/live guards and no implicit asset/device/transport changes.
- Read-only selectable Top/Front/Side stage projections and shared Custom golden vectors.

Audio-source CSV reassignment, full bulk panels, room showcase, Core Audio and physical
hardware certification remain pending.

## 0.2.0 — Windows direct-device audio development

- Engine-independent C ABI and WASAPI shared/exclusive worker; no Unity mixer fallback.
- Immutable native PCM, channel mapping, rate conversion, loop/EOF/drain and cancellation.
- Bounded RIFF PCM/float decoder, explicit prepared-asset import and loaded-clip capture.
- Cue/device inspector, native observed-state OSC feedback and cancellable unscaled replay.
- Read-only cached native metadata, no implicit device enumeration or PCM preparation.
- Editor Pause query service, native/managed silent tests and Windows player smoke tooling.

Physical output/fidelity/latency/exclusive compatibility is not certified. Core Audio,
full custom adapters, CSV/radar and room showcase remain pending.

## 0.1.0 — initial development

- Unity UPM Runtime/Editor/EditMode/PlayMode assembly boundaries.
- Stage front/right/up mapping, object bindings, ADM XYZ/AED and Custom XYZ subset.
- Exact-bind bounded UDP input and explicit networking lifecycle.
- Read-only version-1 scene queries compatible with existing clients.
- Compact editor window and source-generated spatial example.
- Vendored engine-neutral golden fixtures with SHA-256 provenance.

Native direct-device playback, full custom adapters, CSV/radar and four-room playable
showcase are not yet implemented. This is not a parity release or hardware certification.
