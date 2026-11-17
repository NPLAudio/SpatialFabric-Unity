# SpatialFabric four-room walking showcase

Open this directory in Unity **6000.6.9f1**, then open:

`Assets/SpatialFabricShowcase/Scenes/RoomShowcase.unity`

If Unity shows an empty/Untitled scene, use **Tools → SpatialFabric → Open four-room showcase** (or double-click that scene asset). The project now restores it automatically on a clean empty startup scene; modified, saved or multi-scene setups are left alone. This does not enter Play mode, start audio or enable OSC.

Press **Play**. You start in an enclosed hallway with four separate demo rooms:

1. **Spatial motion** — four OSC objects, three animated; fixed stage basis.
2. **Mono hit** — original warm-percussion cue, first source channel → output 1.
3. **Stereo groove** — original musical loop, corresponding L/R device channels.
4. **Replay cue** — original glass/mechanical cue, output 2, replay delay 2.5 seconds.

## Rendering: Universal Render Pipeline

The Unity 6.6 example uses **URP 17.6.0 / Forward+**, explicitly assigned in Graphics
and every Quality level. Owned materials use URP Lit; the original sign shader uses
URP HLSL with depth testing. Point-light intensities account for URP attenuation.
No Built-in pipeline, template scene or template character is required.

For the packaged room sample, install URP before importing and explicitly assign its
`Rendering/ShowcaseURP.asset` in a **new** project's Graphics/Quality settings. In an
existing URP project keep your own pipeline. Sample import never changes settings
automatically. The core SpatialFabric package remains render-pipeline independent.

An explicit owned-assets-only repair command is **Tools → SpatialFabric → Configure
example URP**. It refuses unrelated pipelines and unsaved scenes; it never rebuilds
rooms, prepares audio or starts transport. GitHub Actions is disabled; validation is local.

## Real controls

- **WASD + mouse** walk/look; Shift sprint; Space jump.
- **Esc** releases the cursor; HUD **Walk / capture mouse** resumes.
- Inside a playback room: **E** play, **Q** stop/cancel replay, **L** toggle loop,
  **R** toggle auto replay. **X** stops all cues, including with the cursor released.
- HUD buttons call the same actual playback APIs. There are no fake in-world buttons.
- **Routing / OSC** opens explicit network/device controls. Stop all audio/replay before
  editing output routing. The default device ID is empty (system default); output 2
  requires a device with at least two channels. Unknown/unsupported devices fail closed.

**Nothing auto-plays or auto-listens.** Playback uses the native Windows x64 plugin;
from the repo root, with the editor closed: `python Tools/build_native.py --install`.
Prepared PCM assets and original WAV sources are already included. Do not press E/Play
unless you intend to use your physical output device.

OSC: explicitly Start OSC in the HUD or manager window. Inputs `127.0.0.1:8100/9000`,
ADM output `50018`, Custom `9002`, playback state `9001`. Cue bases:
`/unity/playback/mono`, `/unity/playback/stereo`, `/unity/playback/replay`.
Load `OSCControllerProfile.json` into the existing standalone OSC Controller for these
ports/cue bases (loading it does not send or bind anything). Start Unreal/Unity tracking
only on the intended ports; simultaneous controllers must not compete for reply/monitor
sockets. The controller can query either Unity input. OSC is not PCM;
visual partitions do not acoustically isolate or spatialize direct playback.

## Source and reproducibility

`SourceAssets/` contains the original MIT room plan and three WAVs from SpatialFabric's
Unreal example, with SHA-256 provenance. No Unreal/Epic packages or template characters
were copied. `GeneratedRooms/` contains original Unity materials and prepared PCM assets.
The scene has continuous floor, walls/ceilings, 2–3 m doorways and a 5.5 m hallway.

Generator: **Tools → SpatialFabric → Build four-room showcase (new scene)**. It refuses
to overwrite an existing room scene or generated-assets directory. Existing user work
must be backed up/reviewed explicitly. **Open four-room showcase** opens the saved map.
`SpatialExample.unity` is retained as the small protocol regression fixture, not the main
walking demo. The default player build now uses RoomShowcase.

Validation: repo `Tools/validate_rooms.py` audits real capsule/visitor passages and wall
blocking, silent defaults, asset metadata, actual player animation/offline native PCM,
and five rendered room views. No physical sound is used as a validation shortcut.
