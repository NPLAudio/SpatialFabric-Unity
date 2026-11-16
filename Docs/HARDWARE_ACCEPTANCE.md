# External acceptance — explicit operator action required

Do not mark physical sound, macOS or IL2CPP as passing from offline/packet tests.
Use safe monitoring/headroom and stop all outputs before changing routes. These steps
may produce real sound; automated validation never runs them silently.

## Windows listening

1. Build/install native plugin, open RoomShowcase, choose a known output device explicitly.
2. Mono percussion → selected channel 1, verify no unintended outputs; repeat another
   physical channel and an invalid channel/device (must fail, never fall back).
3. Stereo groove → channel 0; verify independent L/R and corresponding multichannel mapping.
4. Loop without unintended gaps; natural EOF drains. Glass replay waits 2.5 s after natural
   completion; Stop/disable cancels any delayed replay. Changing policy cancels appropriately.
5. Observe state 1 only after worker start, 0 on finish/stop/error; compare listening with
   software state, do not treat OSC send as renderer/DAC acknowledgement.
6. Shared/exclusive requests on supported/unsupported hardware, actual buffer/rate/channel
   report, system/device busy failures, stopped and live device loss, no default fallback.
7. Background/paused editor, separate application focus, standalone player, sustained
   listening/fidelity/latency under target workload. Record driver/OS/device/rate/version.

## macOS source/build gate

Run `bash Tools/build_native_macos.sh --install` on a Mac with Xcode/CMake. Native silent
checks must pass, then Unity/standalone/clean-consumer equivalents, then repeat physical
UID/channel/device-loss/replay/drain/exclusive-hog tests. Verify only acquired hog ownership
is released; never claim no shared fallback from a request alone. Both architectures need
actual build/player results. Windows guarded compilation is not a Mac SDK compile.

## Windows IL2CPP prerequisite

Current attempt was blocked because the module is missing and installation requires
administrator rights. With explicit approval, from an administrator terminal:

```sh
unity install-modules --editor-version 6000.6.9f1 --module windows-il2cpp
```

Review any installer/license/UAC prompts yourself. Then:

```sh
python Tools/validate_player.py --unity "C:/Program Files/Unity/Hub/Editor/6000.6.9f1/Editor/Unity.exe" --il2cpp
```

Keep result JSON/logs and confirm native/scene/sample closure. No generated success file
or source-only branch can replace these external gates.
