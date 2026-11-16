# SpatialFabric / AudioLoom Unity port 0.5

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
