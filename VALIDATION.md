# Unity 0.5 completion evidence — Windows functional port

Host: Windows 11, Unity **6000.6.9f1**, MSVC 2022 x64 / Windows SDK 10.0.26100.
Implementation/deployment checks below do not certify physical sound or untested platforms.

## Current measured gates

| Gate | Measured result |
|---|---|
| Native Windows C ABI/PCM/lifetime/cancellation | **23 silent checks passed** |
| Unity EditMode | **50 passed**, no failures/skips; all 6 installed-native tests included |
| Unity PlayMode | **5 passed**, no failures/skips |
| Separate saved/additive scene lifecycle | **14 checks passed** |
| Portable actual-package OSC/WAV/Custom/CSV/JSON/replay policy | **74 checks passed** |
| Source/package/room provenance/layout | **15 checks passed** |
| Shared interoperability repository | **5 checks passed**, fixture hashes match |
| Room editor geometry/defaults | **44 passed** |
| Actual Windows Mono room player | **51 passed**, including five actual rendered room views |
| Existing Python + Node clients, both Unity inputs | All four queries passed: **9 records / 3 pages** each |
| Latest live spatial capture | **16,492 packets**, exactly all reported sends; four ADM/four Custom IDs |
| Clean packaged UPM consumer | New isolated project installed tgz/sample, strict-built its own native Windows player; **50 silent player room/native checks passed** |

Nothing in these automatic probes plays valid-device audio. Explicit device inventory is
non-playing; render/decoder tests are offline; worker start/error tests use an intentionally
invalid configured ID with no fallback. Software state/packet reception is not DAC/speaker
confirmation or renderer acknowledgement.

## Feature/lifecycle coverage

Coordinate axes/flips, authored/position-only/full virtual listener basis, explicit poses/
labels, per-protocol and per-destination IDs/fields, multiple real UDP destinations,
independent rates/change caches, coherent ADM XYZ/AED, exact AudioLoom float-first mixed-argument trigger rules (play >=0.5,
loop >0.5, address-only loop disables) including real-wire tests, Custom packed/discrete/ranges/aliases/
strict preflight, invalid-frame recovery, exact bind/release and transactional multi-bind.

Read-only schema-v1 paging/chunking/UTF-8/finite JSON, actual Python/Node interop, custom
field dictionaries, cached native metadata with no preparation/enumeration/play in query.
Editor Pause/timeScale zero dispatch and view-only Top/Front/Side/isometric/radar history.

Scene-key route transactions: full preview, invalid last row/no partial changes, unknown/
duplicate IDs/no label fallback, changed preview refusal, Undo/Redo, v2 exact source
assignment/hash/type/local-ID guards, source/route undo together, native readiness stale
invalidation and no transport/native preparation side effects. Six real panels repaint
without startup I/O. Bulk binding/IDs/mute and prefab property modifications/Undo tested.

Prefab asset import preserves IDs/pose references. Cloned active/inactive runtime instances
have unique session keys. Separate batch scene test saved/reloaded original IDs/pose/source
references, verified scene unload closes the native PCM handle, proved default additive
isolation and explicit loaded-scene opt-in, and validated additive unload. That gate runs
outside NUnit so its scene switch cannot destroy the test runner.

Windows native renderer covers corresponding/selected channels, loop/EOF zero tail,
up/down-rate interpolation, PCM formats, immutable ownership and cancellation stress.
WAV coverage includes PCM8/16/24/32, float32/64, extensible valid bits, malformed bounds/
rate/alignment/truncation/subtype and non-finite rejection. Synthetic observed-state traces
cover short starts/ends between polls and replay/cancellation/errors without fake network
acknowledgements. Managed native tests never substitute AudioSource.

## Real four-room showcase

Saved `Assets/SpatialFabricShowcase/Scenes/RoomShowcase.unity`: continuous floor, ceilings,
partitions/hallway, four doors and original walking controller. Warm-percussion mono,
stereo musical groove and glass/replay sources are original MIT WAVs, hashed and imported
to prepared PCM assets. Four non-colliding OSC spheres, three animated, fixed stage.

Actual CharacterController doorway passages, wall/divider blocking and hallway landing
passed, as did all capsule casts/floor samples. Defaults: OSC stopped, cue caches unprepared,
none playing, output 1 / matching channels / output 2, stereo loop and replay delay 2.5 s.
Keyboard/HUD controls call real APIs; no fake decorative controls or automatic play/listen.
Rendered review caught/fixed default font through-wall/oversized text using an original
depth-tested shader and backed-up focused update. Five final player views were reviewed.

## Clean package closure

Local `dist/0.5.0/` contains UPM tarball (Windows native plugin + importable room sample),
CompleteSource ZIP, manifest and SHA256SUMS. Source provenance is **uncommitted local
snapshot**, not a published release/tag. Native build products remain ignored in Git.

`Tools/validate_package.py` creates a new empty project using only the tarball dependency
and its sample, with no sibling checkout/plugin discovery. It imports serialized assets,
validates room/default/native availability, strict-builds a separate player and runs the
silent actual-room/native smoke. Its native DLL/scene/material/audio/script closure passed.
Test-only smoke helpers are not dependencies required by the distributable sample.

## Evidence locations (outside Git)

- `%TEMP%/SpatialFabricNativeAudio-_o4y2m08/`: latest native build/tests.
- `%TEMP%/SpatialFabricFinalParity-h0y366br/WindowsFinal/`: aggregate `summary.json`.
  - `Persistence/persistence.json` — 14 actual scene lifecycle checks.
  - `UnityTests/{EditMode,PlayMode}.xml` — 50/5, no skips.
  - `Rooms/` — editor/player checks, strict Mono build, 5 renders.
  - `Interop/` — 9-record inventories and 16,492-message capture.
  - `UPMConsumer/` — isolated installed-project/build/player evidence, 50 checks.
- Earlier room visual backup: `%TEMP%/SpatialFabricRoomShowcase-po_w7aqi/BeforeVisualFix/`.

## Explicit external/blocked gates

- **Core Audio source is implemented but NOT compiled/tested on this Windows host.**
  No macOS SDK/runner is available. Universal build script and macOS native CI definition
  do not count as a passing compile, Unity player or physical device result.
- **IL2CPP was attempted, not passed.** Unity reported that Windows IL2CPP support is not
  installed. CLI dry-run identified the module; install with `--no-elevate` returned
  `ELEVATION_REQUIRED`. No automatic elevation/license acceptance occurred. With approved
  installation, rerun `Tools/validate_player.py --il2cpp` and record real results.
- Real physical channel mapping, stereo image/fidelity, working-stream device loss,
  exclusive/hog compatibility, audible EOF/replay behavior and sustained latency require
  explicit target-hardware acceptance. No silent test can certify those.
- Older Unity, other machines and OS suspension guarantees remain outside this host matrix.
  Linux/WebGL direct hardware playback is not advertised. Hosted CI results are separate
  from this local validation record and must be checked on the published repository.

The implementation work did not change Unreal plugin behavior. Repository-publication
commits add reciprocal README links separately. The local artifacts above predate Git
publication and retain their original development-snapshot provenance; they are not
silently relabeled as release/tag builds. See Docs/PARITY.md for external gates.
