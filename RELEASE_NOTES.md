# SpatialFabric Unity 0.6.0

## Changes

- Standard OSC `/ack` results (received/ignored/rejected/failed) and `/status` snapshots,
  sent to the configured monitor destination, default 9001. Real worker state remains
  separate from acceptance of a play request.
- Request-version tracking reports asynchronous start failures without stale replies
  after cancellation. Playback prepares stale/unprepared PCM on demand.
- Seven golden acknowledgement packets and 34 shared decision fixtures, device-free
  lifecycle tests and invalid-device regression coverage.
- The example restores its saved four-room scene when an interactive editor starts
  on a clean empty/default scene, never replacing dirty, saved or multi-scene work.

## Downloads and use

- `com.nplaudio.spatialfabric-0.6.0.tgz`: Unity Package Manager package, including the
  importable URP four-room sample and Windows native library.
- `SpatialFabric-Unity-0.6.0-CompleteSource.zip`: full source and example project.
- `SpatialFabricRoomShowcase-Unity-0.6.0-Windows-x64.zip`: non-development Windows
  standalone showcase; extract the entire folder and run its EXE.

Open `ExampleProject` in Unity 6000.6.9f1. Use **Tools → SpatialFabric → Open four-room
showcase**, or `Assets/SpatialFabricShowcase/Scenes/RoomShowcase.unity`.
For a UPM install, import **Four-room walking showcase** from Package Manager and open
`Assets/Samples/SpatialFabric/0.6.0/Four-room walking showcase/Scenes/RoomShowcase.unity`.
Install URP first; assign the sample's `Rendering/ShowcaseURP.asset` in Graphics and each
Quality level for a new project (retain your pipeline in an existing URP project).
The package stores the sample at `Samples~/FourRoomShowcase`.

Use the [shared hub OSC Controller](https://github.com/NPLAudio/SpatialFabric/tree/main/Apps/OSCController).
Load `ExampleProject/OSCControllerProfile.json` from CompleteSource, or
`OSCControllerProfile.json` beside the player EXE, and explicitly
start Unity OSC in the intended editor/play context. Inputs default to 8100/9000;
monitor is 9001. Mono play address: `/unity/playback/mono/play`. Entering Play mode
stops editor networking; explicitly restart it in the desired context.

## Verification / limits

60 EditMode tests, five PlayMode tests, 117 core checks and 18 Python checks passed.
The startup helper also compiled against the installed Unity APIs with no warnings.
Standalone build evidence and checksums accompany the player download. Automated
checks use synthetic state, offline rendering or invalid devices and do not prove
physical audio output. The operator reported the interactive issues resolved.

Nothing listens or plays at startup. Audio bypasses Unity's mixer; choose the correct
physical device/channel and maintain an independent mute. Windows executables are
unsigned. Windows x64/Mono is the provided player; no macOS/IL2CPP certification or
hardware/latency certification is claimed. Preserve included licenses/notices.
