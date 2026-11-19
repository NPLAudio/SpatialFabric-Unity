# AudioLoom playback parity

Implementation details, changed-file manifest and commands:
[AudioLoomUnityParity.md](AudioLoomUnityParity.md). The shared
[playback parity contract](https://github.com/NPLAudio/SpatialFabric/blob/main/Specs/Playback-Parity-v1.md)
defines the baseline, not interchangeable Unity/Unreal asset identifiers.

## Closed audited gaps

- Per-cue fixed/random min/max natural-end replay with deterministic seeded selection
  once per completion, cancellation and zero-delay support.
- Runtime-only `playOnStart`, false by default; editor/import/discovery remain silent.
- Blank-derived unique OSC bases, preserved serialized overrides, duplicate diagnostics,
  and feedback suppression before dispatch, including reflected nested-base traffic.
- Main-tick restart of active device/channel/exclusive/buffer/source changes; idle
  editing never prepares or starts audio. Restart begins the source from the start.
- Native Windows same-endpoint exclusive-preferred/shared fallback, actual mode and
  cached OS output-path latency. Missing-device fail-closed policy remains.
- Existing source import, manager, stopped-only CSV workflow and Undo/persistence
  remain. CSV leaves random/startup settings untouched; it is not Unreal asset import.

Derived defaults include scene-instance and hierarchy identity. They can change on
reload/reorder/reparent: author an explicit base for persistent controller profiles.
Do not automatically replace an existing `/unity/playback/cue` literal on upgrade;
shared explicit addresses remain supported fan-out and generate diagnostics.

Native diagnostic APIs are optional for older ABI-v1 libraries. Old binaries remain
loadable but cannot provide the new fallback behavior/diagnostics. Build/deploy the
updated native plugin explicitly using `python Tools/build_native.py --install`
with the example closed. This changes a DLL, not physical device routing.

## Central editor authoring

- [Scene Objects](SceneObjectManager.md) lists loaded authoring-scene objects,
  including unconfigured/inactive objects, with explicit capability attachment,
  real mixed-value properties, Undo and Hierarchy selection following.
- Stage/Objects/Playback also show existing unassigned/unbound components; manager
  resolution is read-only and ambiguous managers require an explicit choice.
- [Audio routing](AudioRoutingMatrix.md) uses true cue-row × interface/channel
  crosspoints, sticky headers, bounded visible painting and dropdown selectors.
  Only explicit Refresh Interfaces enumerates devices; exact-ID offline history
  retains unplugged layouts without changing any cue route. Checks mean configured,
  not live signal. Fresh mutation guards and native Undo apply to both controls.
- Object/property foldouts keep individual Inspectors available. These editor
  surfaces do not implicitly enroll objects or activate audio/capture/networking.

## Broader spatial-port surface

The AudioLoom follow-up does not replace the existing spatial implementation or
its historical validation record in [../VALIDATION.md](../VALIDATION.md).

| Capability | Retained Unity implementation |
|---|---|
| Stage/listener | Stage dimensions/flips, off/position/orientation follow, fixed authored stage |
| Binding identity | Saved-scene/editor IDs, prefab/live-instance keys, per-protocol/destination IDs |
| Loaded worlds | Owner scene by default, explicit opt-in loaded additive scenes |
| ADM | XYZ/AED/Both, coherent sphere limit, gain/width/mute/name metadata |
| Custom | XYZ/AED, packed/discrete, aliases, fields, ranges and preflight |
| Output routing | Independent destinations, change/rate caches, per-binding overrides |
| OSC lifecycle | Exact IPv4 binds, bounded atomic decoding/queues, transactional binds, paused editor service |
| Discovery | Schema-v1, sender-only reply, remote opt-in, bounds, read-only cached inventory |
| Direct PCM | Immutable bounded preparation, routing/rate conversion, loop/EOF/drain/cancellation |
| Windows | WASAPI shared/exclusive-preferred with same-endpoint fallback; fail-closed configured routing |
| macOS | Core Audio AudioQueue, exact UID routing, discrete channels, hog-mode requests |
| CSV | v1 routes/v2 GUID-localFileId sources, preview/stale guards/one Undo |
| Authoring | Scene Objects, Stage/Objects/Outputs/Playback/CSV/Diagnostics, prefab overrides, bulk controls; crosspoint audio grid, interface/channel selectors and offline inventory cache |
| Radar | Top/Front/Side/isometric, selection and bounded view-only trails |
| Showcase/deployment | Four-room walker/cues/HUD, Windows Mono/IL2CPP Development-player checks and UPM/sample consumer tooling |

Saved Unity keys and engine GUIDs replace Unreal actors/UObjects/SoundWave paths;
CSV asset references are not interchangeable. Custom field injection is rejected
rather than guessed. Native PCM never falls back to AudioSource. OSC, PCM, visual
walls and physical renderer acknowledgement remain separate.

## Fresh software checks

- Portable CoreChecks: **196 passed**.
- Python Tools: **52 passed, 2 skipped**.
- Windows native renderer/lifecycle: **55 passed**.
- Fake-COM WASAPI negotiation/diagnostics: **55 passed**; included in the default
  Windows build script as well as CMake/CTest.
- Runtime, editor and EditMode/PlayMode test assemblies compiled against installed
  Unity **6000.6.9f1** assemblies: **0 errors**. The source-change regression was
  included in the final compile pass.

- **Actual Unity engine execution:** **139/139 EditMode + 6/6 PlayMode passed**, zero
  skips/failures, with `--graphics --require-native` in a disposable project. This
  supersedes the earlier 81-test playback-only gate and includes actual-window
  live binding, grid clicks/Undo, ancestor clipping, dropdowns and offline history.
  Evidence: `C:/Temp/SFSceneObjectManager-20261118/true-crosspoint-clipping/validation.json`.
- The earlier licensing error is historical: activation succeeded afterward and the
  current Editor accepted the license. No activation changes were made by this work.
- Fixed a test's flat Editor JSON assumption: Unity 6000.6 wraps MonoBehaviour
  fields. Round-tripping actual `EditorJsonUtility.ToJson` data verifies preservation.

Repeat safely with `python Tools/validate_unity.py --unity /path/to/Unity --isolated
--graphics --require-native`. Isolation copies authored inputs/current package,
never reuses working Library/Temp, disables engine audio and assigns a unique
validation persistent-data identity. Unity may write an additional internal result
beneath LocalLow despite `-testResults`; it must not share the example's identity.
The validator checks individual NUnit cases/counts and only allows explicit known
headless/optional-native skips when those capabilities were not required.

## Actual Windows player checks

Both **Mono and IL2CPP Development players built and ran successfully** with the
fresh native DLL. JSON independently confirms native availability, offline PCM/EOF,
invalid-device refusal, no active playback and 9 scene records. No real stream opens.

- Mono: `C:/Users/davo_/AppData/Local/Temp/SpatialFabricUnityPlayer-in8uzb2i/`.
- IL2CPP: `C:/Users/davo_/AppData/Local/Temp/SpatialFabricUnityPlayer-0_cf4jwx/`.

At the user's request, installed **Windows Build Support (IL2CPP)** for 6000.6.9f1
using the official Unity CLI. No other modules were installed. The missing-module
gate is superseded by the successful actual player check, not silently waived.

Repeat with `python Tools/validate_player.py --unity /path/to/Unity --isolated
--native-dll /path/to/current/SpatialFabricAudio.dll` (add `--il2cpp` for IL2CPP).
The override installs only into the isolated copy; evidence must be outside both
repo and source project (including custom projects). Deployed DLL hash must match.
The gate checks individual smoke assertions, not just process exit or status text.
An additional **non-Development IL2CPP player** also passed at
`C:/Users/davo_/AppData/Local/Temp/SpatialFabricUnityPlayer-whkiijgj/`. Only the disposable
build driver's options changed to StrictMode without Development; source stayed
unchanged and stripping used copied project defaults. These are validation players,
not clean-snapshot release publications.

## External validation gates (not silently waived)

No physical audio/microphone, macOS, cross-engine asset migration or acoustic
latency acceptance occurred in this pass. Decoding remains bounded whole-PCM
preparation, not unlimited long-form streaming.

- Physical acceptance: channel/image/fidelity, natural-end timing, sustained latency,
  device loss and exclusive compatibility on the target hardware.
- macOS: earlier hosted ARM64 native-only checks are historical; Mac Unity/player,
  Intel/universal and physical Core Audio still require qualification.
- Other Windows release/stripping configurations and arbitrary migrated assets still
  need qualification; these player passes are not a universal AOT certificate.
- Older Unity versions and Linux/WebGL direct-device playback are not certified.

Existing user edits to `ExampleProject/ProjectSettings/InputManager.asset` were
preserved and excluded from feature commits. Validation never reused the working
project's Library/Temp; source publication does not imply new release builds.
