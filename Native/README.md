# Engine-independent direct-device audio, C ABI v1

Original code, no Unity/Unreal headers or mixer fallback. Opaque handles own immutable
interleaved float PCM; workers never call managed code. Channel 0 maps corresponding
source/device channels, 1..N routes source channel 0 to one selected physical output.
Rate conversion, loop interpolation, zero EOF tails and observed start/end counters are
shared. Stop signals cancellation and joins; failed configured IDs never select default.

## Windows — locally built/tested

WASAPI shared mix format or explicit exclusive request, alignment retry, device-format
float/PCM16/24/32 encoding, actual channels/rate/buffer and drain state. Exclusive refusal
never silently becomes shared. Physical channels/fidelity/latency/hot-unplug/exclusive
compatibility still require actual hardware acceptance.

Close Unity before replacing native output:

```sh
python Tools/build_native.py --install
```

MSVC 2022 x64 + Windows SDK; `--vs` overrides install location. Temporary build reports,
DLL/tests are outside Git. Local plugin/importer are installed under Runtime/Plugins/x86_64
and ignored. Target machines require the MSVC runtime.

## macOS — source implemented, NOT locally compiled/certified

`src/core_audio.inc` supplies AudioQueue output on an exact Core Audio UID, explicit
discrete channel order, observed actual worker lifecycle, cancellation/drain/device-loss
handling and optional hog-mode ownership. Exclusive ownership conflicts fail closed;
cleanup releases only ownership acquired by this worker. No Unity mixer/managed callback.

Run on macOS with Xcode command-line tools/CMake (deployment target 12):

```sh
bash Tools/build_native_macos.sh --install
```

Builds arm64/x86_64 universal library, runs silent native tests, installs macOS-only
importer under Runtime/Plugins/macOS. This Windows host has no macOS SDK; code review,
guarded Windows compilation and a CI definition are **not** a passing macOS compile or
hardware result. Do not claim support certification until a Mac runner actually passes.

## ABI / safety

- PCM creation copies finite bounded data (<=64M samples). Players retain shared native
  memory independently of a caller's buffer handle; offline render never opens devices.
- Start accepts a request. Only successful OS stream start increments start sequence;
  end sequences preserve very short start/end transitions between main-thread polls.
- Managed handles retain lifetime around plain-pointer P/Invoke calls (AOT-friendly);
  explicit scene/reload/quit cleanup releases players and buffers.
- Device inventory is explicit, bounded UTF-8 JSON and never creates a playing stream.
  Discovery reads cached Unity state only, never prepares PCM or enumerates devices.
- Natural completion alone can schedule managed replay. Stop/disable and changed policy
  cancel it; device errors never produce a false Playing acknowledgement or replay.

## Evidence / blocked tooling

`audio_tests.cpp`: 23 silent Windows checks for routing/EOF/loop/rate/format, immutable
ownership, ABI, invalid-device failure and cancellation stress, never a valid render
stream. Unity/real player/clean-consumer evidence is in VALIDATION.md. Linux CMake tests
exercise the offline renderer and unsupported-output stub, not Linux audio support.

Windows IL2CPP validation is blocked by the missing editor module. CLI install requires
administrator approval; no automatic elevation was attempted. Mono player validation
is separate and passing. Actual listening/latency/device-loss acceptance remains open.
