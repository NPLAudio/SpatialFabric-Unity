# SpatialFabric four-room walking showcase

Open this directory in Unity **6000.6.9f1**, then open:

`Assets/SpatialFabricShowcase/Scenes/RoomShowcase.unity`

Press **Play**. You start in an enclosed hallway with four separate demo rooms:

1. **Spatial motion** — four OSC objects, three animated; fixed stage basis.
2. **Mono hit** — original warm-percussion cue, first source channel → output 1.
3. **Stereo groove** — original musical loop, corresponding L/R device channels.
4. **Replay cue** — original glass/mechanical cue, output 2, replay delay 2.5 seconds.

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

OSC: explicitly Start OSC in the HUD or manager window. Inputs `127.0.0.1:8200/9100`,
ADM output `50019`, Custom `9102`, playback state `9101`. Cue bases:
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
