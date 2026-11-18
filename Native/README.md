# Engine-independent direct-device audio, C ABI v1

Original code, no Unity/Unreal headers or mixer fallback. Opaque handles own immutable
interleaved float PCM; workers never call managed code. Channel 0 maps corresponding
source/device channels, 1..N routes source channel 0 to one selected physical output.
Rate conversion, loop interpolation, zero EOF tails and observed start/end counters are
shared. Stop signals cancellation and joins; failed configured IDs never select default.

## Windows — locally built/tested

WASAPI shared mix format or exclusive-preferred request, alignment retry, device-format
float/PCM16/24/32 encoding, actual channels/rate/buffer and drain state. A nonzero
`exclusive` request first tries exclusive mix-format initialization, including a fresh-client
alignment retry, then retries shared initialization on the **same resolved endpoint** if
exclusive format support/initialization fails. Fallback uses a fresh audio client and
shared timing/event rules. `sf_status.exclusive_active` / `sf_live_status.exclusive_active`
reflect the negotiated mode, not the request. Explicit shared requests do not try exclusive.

This compatibility policy never substitutes the default device for a missing configured
ID, changes physical channel routing, retries after a running stream fails, or falls back
to an engine mixer. Default output is selected only by an explicitly empty ID. Start and
inventory remain explicit operations; diagnostic queries do not enumerate/open devices.
Physical channels/fidelity/latency/hot-unplug/exclusive compatibility still require actual
hardware acceptance.

Close Unity before replacing native output:

```sh
python Tools/build_native.py --install
```

MSVC 2022 x64 + Windows SDK; `--vs` overrides install location. Temporary build reports,
DLL/tests are outside Git. Local plugin/importer are installed under Runtime/Plugins/x86_64
and ignored. Target machines require the MSVC runtime.

## macOS — ARM64 native CI passes; Unity/hardware acceptance remains

[Hosted run 43886108349](https://github.com/NPLAudio/SpatialFabric-Unity/actions/runs/43886108349)
compiled with AppleClang 21 on macos-26-arm64 and passed native CTest plus portable checks.
This is not a Mac Unity player, Intel/universal or real-device playback result.

`src/core_audio.inc` supplies AudioQueue output on an exact Core Audio UID, explicit
discrete channel order, observed actual worker lifecycle, cancellation/drain/device-loss
handling and optional hog-mode ownership. Exclusive ownership conflicts fail closed;
cleanup releases only ownership acquired by this worker. No Unity mixer/managed callback.

Run on macOS with Xcode command-line tools/CMake (deployment target 12):

```sh
bash Tools/build_native_macos.sh --install
```

Builds arm64/x86_64 universal library, runs silent native tests, installs macOS-only
importer under Runtime/Plugins/macOS. This Windows host has no macOS SDK. Hosted ARM64
native compile/silent tests now pass as linked above; Mac Unity/player, Intel/universal
and physical hardware acceptance still need actual results before support certification.

## Additive output diagnostics

Existing C ABI versions and `sf_status` / `sf_live_status` layouts remain unchanged.
New exported functions (declared in `include/spatialfabric_audio.h`):

```c
double sf_player_output_latency_ms(sf_player* player);
double sf_live_output_latency_ms(sf_live* live);
int32_t sf_player_output_mode(sf_player* player);
int32_t sf_live_output_mode(sf_live* live);
```

Mode returns `SF_OUTPUT_INACTIVE=0`, `SF_OUTPUT_SHARED=1`, `SF_OUTPUT_EXCLUSIVE=2`, or
`SF_OUTPUT_SHARED_FALLBACK=3` (Windows exclusive request negotiated shared). Inactive
includes null handles, starting, stopped and failed workers. Actual active mode is
published only after OS start and remains available while draining.

Latency returns a cached millisecond value for the active output path, or **-1** for
null/inactive handles or unavailable OS properties. Zero is a valid OS report. Windows
uses `IAudioClient::GetStreamLatency`, not `buffer_frames / output_rate`. Core Audio
uses HAL device latency + safety offset + hardware buffer frame size + the maximum
output-stream latency, converted using the device nominal sample rate. This is a
HAL-reported hardware path, not AudioQueue's application-buffer duration. Properties
are sampled at initialization; later OS/device configuration changes are not tracked.
Neither value measures acoustic latency, live capture/bridge delay, application queue
occupancy, or end-to-end round-trip latency. A failed latency query does not fail playback.
macOS hog-mode refusal remains strict (no shared fallback), unlike Windows negotiation.

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

`tests/audio_tests.cpp`: silent checks for routing/EOF/loop/rate/format, immutable
ownership, ABI, inactive/null diagnostics, invalid-device exclusive requests and
cancellation stress, never a valid render stream. `tests/output_negotiation_tests.cpp`
(Windows CMake/CTest target) tests the real initialization helper with fake COM clients:
shared, exclusive, unsupported/refused exclusive, aligned retry, failed aligned retry,
and failure of both modes. It never creates an OS device/client. Unity/real player/clean-consumer evidence is in VALIDATION.md. Linux CMake tests
exercise the offline renderer and unsupported-output stub, not Linux audio support.

Windows IL2CPP validation is blocked by the missing editor module. CLI install requires
administrator approval; no automatic elevation was attempted. Mono player validation
is separate and passing. Actual listening/latency/device-loss acceptance remains open.
