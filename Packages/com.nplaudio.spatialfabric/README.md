# SpatialFabric for Unity 0.5

Functional SpatialFabric/AudioLoom port: stage/listener mapping, explicit pose/object
bindings, multiple independently configured ADM/Custom destinations with target overrides,
read-only scene discovery, native PCM playback, loop/replay/state, source-aware routing
CSV, multi-object authoring panels and Top/Front/Side/isometric/history radar.

Opening components/windows/profile data never starts networking, prepares PCM or plays.
Configure the current Editor/Play context and explicitly Start OSC. Prepare/Play and device
refresh are explicit. Native output never falls back to Unity AudioSource/mixer. State is
observed worker lifecycle, not request acceptance or physical audio confirmation.

Windows WASAPI code/native/editor/Mono player validation is local. Core Audio source is
provided but is **not compiled/validated on this Windows host**. Windows IL2CPP validation
requires the currently missing editor module (installation needs administrator approval).
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
