# Port completion / validation boundary — Unity 0.5

"Implemented" describes code and controls, **not physical output certification**. Do not
advertise macOS or IL2CPP validation before the external gates below are actually passed.
The Unreal repository remains a separate, unchanged implementation.

| Capability | Unity implementation | Evidence / remaining gate |
|---|---|---|
| Stage dimensions, flips, virtual listener | Off / position-only / position+orientation, explicit root; authored stage never moves | Coordinate/follow tests; room stage fixed |
| Binding/registry identity | Stable saved-scene/editor IDs, explicit transform/label, per-protocol and per-destination IDs; unique live prefab instance keys | Save/reload, prefab import/override/Undo and runtime clone tests |
| Loaded world scope | Owner scene by default; explicit `additionalScenePaths` includes loaded additive scenes, no automatic global search | Isolation/opt-in/additive unload persistence probe |
| ADM | XYZ/AED/Both, coherent sphere limit, gain/width/mute/name metadata | Golden vectors and actual UDP tests |
| Custom | XYZ/AED, packed/discrete, aliases, fields, ranges/reverse ranges, preflight | Shared exact packet vectors and actual UDP/recovery tests; stricter invalid-input rules intentional |
| Output routing | Multiple independent destinations, individual rate/change caches, per-binding destination enable/ID/field overrides | Multiple real receiver/ID/pose tests |
| OSC safety/lifecycle | Literal exact-bind IPv4, bounded atomic decoding/queues, transactional multi-bind, paused/background editor service | Codec/socket/timeScale/Editor Pause tests and live capture; OS suspension is not guaranteed |
| Discovery | Schema-v1, sender-only reply, opt-in remote disclosure, rate/page/chunk bounds, local API, read-only cached inventory | Existing Python/Node clients and native player; no hardware enumeration/preparation/play in query |
| Direct PCM | Immutable buffers, bounded WAV preparation, channel mapping, rate conversion, loop/EOF/drain/cancellation | Native offline/lifetime/cancel/fail-closed tests; physical channels/fidelity/latency still unverified |
| Windows backend | WASAPI shared and requested exclusive mode, actual worker state, stable device routing, no fallback | Editor + Mono player tested; physical/exclusive/hot-unplug listening acceptance remains |
| macOS backend | Core Audio AudioQueue, exact UID routing, discrete channels, hog-mode exclusive requests, device loss/cleanup | **Source implemented but NOT compiled/validated on this Windows host. Mac SDK/runner required.** |
| Replay/state | AudioLoom-compatible float-first trigger thresholds, observed real start/end counters, unscaled cancellable natural-end replay, OSC state feedback | Synthetic policy traces + actual failed-device/no-false-ack tests; real audible drain/replay acceptance remains |
| CSV | v1 route compatibility + v2 exact GUID/localFileId source assignment, whole-file preview/stale guards/one Undo | Source assignment/type/hash/concurrent-edit/Undo tests; explicit Apply may load selected assets, never native PCM/transport |
| Editor workflows | Stage, Objects, Outputs, Playback, CSV, Diagnostics; binding creation, bulk IDs/mute, multi-cue inspector/device/source controls | All panels repaint without startup I/O; prefab override/Undo tests |
| Radar | Top/Front/Side/isometric, selectable real objects, bounded view-only trails | Projection/UI tests; history is authoring data, not renderer acknowledgement |
| Room showcase | Four enclosed rooms/hall, original walker, three original prepared cues, real explicit HUD/keyboard/OSC controls | 44 editor + 51 player room checks, five actual rendered views; no autoplay/listening |
| Deployment | Windows Mono standalone and locally packaged UPM/sample consumer tooling | Clean-consumer results in VALIDATION.md; see IL2CPP gate below |

## External validation gates (not silently waived)

1. **Physical audio acceptance:** correct real outputs, stereo image, EOF/replay timing,
   device loss, exclusive compatibility and sustained latency/fidelity on target hardware.
2. **macOS:** compile native source with the macOS SDK, run native/Unity/player tests and
   physical Core Audio checks. `bash Tools/build_native_macos.sh --install`; CI includes a
   macOS native job. Check actual published CI results; a workflow definition is not a passing platform result.
3. **Windows IL2CPP:** this editor lacks the Windows IL2CPP module. CLI installation
   reported `ELEVATION_REQUIRED`; no automatic elevation/license acceptance was performed.
   Install the module with administrator approval, then run the provided IL2CPP smoke.
4. Older Unity releases/other hardware remain outside the local certification matrix.
   Linux/WebGL direct-device playback is not advertised.

## Intentional engine differences

Unity editor components/asset GUIDs replace Unreal actors/UObjects/SoundWave paths;
CSV reference formats are not interchangeable between engines. Editor keys are stable
across saved-scene reload, live keys are scoped to one play session. Invalid/reserved
Custom field injection is rejected rather than guessed. Native PCM never uses AudioSource
as a fallback. OSC, PCM, visual walls and physical renderer acknowledgement remain separate.
