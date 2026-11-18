# Unity AudioLoom implementation notes

## Behavior

- Replay supports fixed delay or per-cue seeded xorshift32 min/max selection. Selection happens once per eligible observed natural end, not per tick. Nonfinite/negative bounds sanitize to zero; reversed bounds swap. Enable resets the sequence. Existing loop, stop, disable and autoReplay-off cancellation remains.
- `playOnStart` is false by default and runs only at Start for a playing-world object. Editor, discovery and imports do not prepare/start audio. Initially disabled cues opt in when first enabled; later enable cycles do not repeat Start.
- Blank OSC bases derive from scene-instance identity and hierarchy sibling indices. Explicit serialized overrides remain untouched. Derived addresses are not stable across reload/reorder/reparent; use explicit bases for persistent controller profiles. Manager validation diagnoses duplicate playback/live bases, including disabled objects; shared-address fan-out remains supported.
- Device/channel/exclusive/buffer changes and source assignment/revision changes restart active playback on the next main-thread tick, from the beginning. Idle/stopped/failed cues do not autoplay. CSV import remains stopped-only and leaves startup/random settings unchanged.
- Incoming ack/state feedback is suppressed before base matching on both input ports, including nested bases. Legacy float/int decisions are unchanged. Missing configured devices remain failures, never default-device requests.
- Optional additive native player/live exports expose output mode and OS output-path latency. Absence of these exports is cached per handle and does not break ABI-v1 plugins. Discovery reads cached data only. Output latency is separate from buffer/queue latency and is not acoustic or round-trip latency.

## Validation

- Portable CoreChecks: 196 checks passed (including cancellation from an EOF subscriber and cancellation on a later worker failure). From SpatialFabric-Unity: `dotnet run --project Tools/CoreChecks/CoreChecks.csproj -- Packages/com.nplaudio.spatialfabric/Tests/EditMode/Fixtures/osc-v1.json` (the vendored fixture path is also the default when run from this repository).
- Python: `python -m unittest discover -s Tools -p 'test_*.py'`: 54 tests, 52 passed, 2 skipped.
- Package Runtime, Editor, EditModeTests and PlayModeTests compiled against installed Unity 6000.6.9f1 assemblies using isolated temporary .NET projects: zero errors. Existing serialization-field warnings remain.
- Unity engine execution: **81 EditMode + 6 PlayMode passed**, zero skips/failures, with graphics and required native integrations enabled. Evidence: `C:/Users/davo_/AppData/Local/Temp/SpatialFabricUnityValidation-m62necnh/`. The isolated copy used a freshly rebuilt current native DLL; the working project's binary was not replaced. The earlier license failure preceded successful activation and is superseded by this actual engine pass.
- The serialization regression round-trips actual Editor JSON: Unity 6000.6 uses a MonoBehaviour wrapper, so the previous flat partial test input was silently ignored. Legacy authored overrides survive the real serialization shape.
- `Tools/validate_unity.py --isolated` copies current package/authored inputs, disables engine audio and assigns a unique validation persistent-data identity. NUnit leaf evidence/counts are checked; expected headless skips are not mistaken for aggregate failure, and unknown skips remain failures.
- New Unity regression coverage includes derived/explicit addresses, duplicate diagnostics, no editor/idle autoplay, deterministic replay cancellation, delayed active-route restart, source replacement detection without preparation, silent nested feedback on both ports, play-mode startup lifecycle, and optional idle-player/live diagnostics.
- Actual Windows Mono and IL2CPP Development players built/ran with all silent smoke assertions true and 9 scene records. Evidence: `C:/Users/davo_/AppData/Local/Temp/SpatialFabricUnityPlayer-in8uzb2i/` and `C:/Users/davo_/AppData/Local/Temp/SpatialFabricUnityPlayer-0_cf4jwx/`. Windows IL2CPP module installation was explicitly requested and succeeded through the official Unity CLI.
- No physical audio or microphone was requested by validation. Hardware, acoustic latency, macOS, and other release/stripping profiles remain external. An additional non-Development IL2CPP player passed with project-default stripping; only the disposable build driver's Development flag was removed. Evidence: `C:/Users/davo_/AppData/Local/Temp/SpatialFabricUnityPlayer-whkiijgj/`. Updated native diagnostics require deploying the newly built native plugin; bundled older ABI-v1 plugins report unavailable optional diagnostics.

## Exact files changed by Unity implementation

Paths are relative to `C:/dev/UnrealSpatial`.

```text
SpatialFabric-Unity/Docs/AudioLoomUnityParity.md
SpatialFabric-Unity/README.md
SpatialFabric-Unity/Packages/com.nplaudio.spatialfabric/README.md
SpatialFabric-Unity/Packages/com.nplaudio.spatialfabric/Documentation~/index.md
SpatialFabric-Unity/Packages/com.nplaudio.spatialfabric/Editor/LiveInputCueEditor.cs
SpatialFabric-Unity/Packages/com.nplaudio.spatialfabric/Editor/PlaybackCueEditor.cs
SpatialFabric-Unity/Packages/com.nplaudio.spatialfabric/Editor/RoutingCsv.cs
SpatialFabric-Unity/Packages/com.nplaudio.spatialfabric/Editor/SpatialAuthoring.cs
SpatialFabric-Unity/Packages/com.nplaudio.spatialfabric/Editor/SpatialFabricWindow.cs
SpatialFabric-Unity/Packages/com.nplaudio.spatialfabric/Runtime/Discovery/SceneInventory.cs
SpatialFabric-Unity/Packages/com.nplaudio.spatialfabric/Runtime/Osc/PlaybackControl.cs
SpatialFabric-Unity/Packages/com.nplaudio.spatialfabric/Runtime/Playback/LiveInputCue.cs
SpatialFabric-Unity/Packages/com.nplaudio.spatialfabric/Runtime/Playback/NativeAudio.cs
SpatialFabric-Unity/Packages/com.nplaudio.spatialfabric/Runtime/Playback/NativeLive.cs
SpatialFabric-Unity/Packages/com.nplaudio.spatialfabric/Runtime/Playback/PlaybackCue.cs
SpatialFabric-Unity/Packages/com.nplaudio.spatialfabric/Runtime/Playback/PlaybackObservation.cs
SpatialFabric-Unity/Packages/com.nplaudio.spatialfabric/Runtime/Playback/PlaybackPolicy.cs
SpatialFabric-Unity/Packages/com.nplaudio.spatialfabric/Runtime/Playback/PlaybackPolicy.cs.meta
SpatialFabric-Unity/Packages/com.nplaudio.spatialfabric/Runtime/SpatialFabricManager.cs
SpatialFabric-Unity/Packages/com.nplaudio.spatialfabric/Tests/EditMode/AcknowledgementTests.cs
SpatialFabric-Unity/Packages/com.nplaudio.spatialfabric/Tests/EditMode/LiveInputTests.cs
SpatialFabric-Unity/Packages/com.nplaudio.spatialfabric/Tests/EditMode/NativePlaybackTests.cs
SpatialFabric-Unity/Packages/com.nplaudio.spatialfabric/Tests/EditMode/PlaybackParityTests.cs
SpatialFabric-Unity/Packages/com.nplaudio.spatialfabric/Tests/EditMode/PlaybackParityTests.cs.meta
SpatialFabric-Unity/Packages/com.nplaudio.spatialfabric/Tests/PlayMode/PlaybackStartupTests.cs
SpatialFabric-Unity/Packages/com.nplaudio.spatialfabric/Tests/PlayMode/PlaybackStartupTests.cs.meta
SpatialFabric-Unity/Tools/CoreChecks/CoreChecks.csproj
SpatialFabric-Unity/Tools/CoreChecks/Program.cs
SpatialFabric-Unity/Tools/validate_unity.py
SpatialFabric-Unity/Tools/test_validate_unity.py
SpatialFabric-Unity/Tools/validate_player.py
SpatialFabric-Unity/Tools/test_validate_player.py
```

Shared Native/** and Docs/PARITY.md changes are tracked separately in the parity report. Existing InputManager.asset edits were preserved. Working-project Library/Temp were not modified; nothing was staged or committed.
