// SpatialFabric direct-output C ABI v1. Original implementation; MIT.
#pragma once
#include <stdint.h>
#if defined(_WIN32)
# if defined(SF_AUDIO_BUILD)
#  define SF_AUDIO_API __declspec(dllexport)
# else
#  define SF_AUDIO_API __declspec(dllimport)
# endif
#else
# define SF_AUDIO_API __attribute__((visibility("default")))
#endif
#ifdef __cplusplus
extern "C" {
#endif
typedef struct sf_buffer sf_buffer;
typedef struct sf_player sf_player;
enum sf_state { SF_STOPPED=0, SF_STARTING=1, SF_PLAYING=2, SF_DRAINING=3, SF_FAILED=4 };
enum sf_reason { SF_NONE=0, SF_NATURAL_END=1, SF_CANCELLED=2, SF_DEVICE_ERROR=3 };
typedef struct sf_status {
    int32_t state, reason, output_channels, output_rate, exclusive_active, buffer_frames;
    uint64_t started_sequence, ended_sequence, rendered_frames;
    char error_utf8[1024];
} sf_status;
SF_AUDIO_API uint32_t sf_audio_abi_version(void);
// Returns required bytes INCLUDING NUL, or -1 on failure. No device is opened for rendering.
SF_AUDIO_API int32_t sf_devices_json(char* output, int32_t capacity, char* error, int32_t error_capacity);
// Copies finite interleaved float PCM into an immutable native allocation (<=64M samples).
SF_AUDIO_API sf_buffer* sf_buffer_create(const float* samples, int64_t count, int32_t channels, int32_t rate, char* error, int32_t error_capacity);
SF_AUDIO_API void sf_buffer_destroy(sf_buffer* buffer);
// Offline deterministic render. position is in source frames; no OS audio/device API is called.
SF_AUDIO_API int32_t sf_buffer_render(sf_buffer* buffer, float* output, int32_t frames, int32_t output_channels, int32_t output_rate, int32_t output_channel, int32_t loop, double* position);
SF_AUDIO_API sf_player* sf_player_create(void);
// Start accepts a request, not an acknowledgement. Poll status for actual worker start/error.
// Empty device ID selects the default endpoint. Channel 0 maps corresponding channels;
// channel 1..N maps the first source channel to one selected physical output.
// Windows exclusive != 0 prefers exclusive, with shared fallback on the SAME endpoint
// if exclusive format/initialization fails. macOS retains strict exclusive hog mode.
SF_AUDIO_API int32_t sf_player_start(sf_player* player, sf_buffer* buffer, const char* device_id_utf8, int32_t output_channel, int32_t exclusive, int32_t buffer_ms, int32_t loop);
SF_AUDIO_API void sf_player_loop(sf_player* player, int32_t loop);
SF_AUDIO_API void sf_player_status(sf_player* player, sf_status* output);
// Additive diagnostics; existing status layouts and ABI versions are unchanged.
// Mode is active only while playing/draining. SHARED_FALLBACK means Windows retried
// the same endpoint after an exclusive initialization failure (never a different device).
enum sf_output_mode { SF_OUTPUT_INACTIVE=0, SF_OUTPUT_SHARED=1, SF_OUTPUT_EXCLUSIVE=2, SF_OUTPUT_SHARED_FALLBACK=3 };
SF_AUDIO_API int32_t sf_player_output_mode(sf_player* player);
// Cached OS output-path latency in milliseconds, or -1 if inactive/unavailable/null.
// WASAPI: GetStreamLatency. Core Audio: device + safety offset + hardware buffer +
// maximum output-stream latency. Excludes application queues and live bridge delay;
// not a measured acoustic/round-trip latency. This query never opens a device.
SF_AUDIO_API double sf_player_output_latency_ms(sf_player* player);
// Stop cancels and joins the worker; destroy also stops. No callbacks enter managed code.
SF_AUDIO_API void sf_player_stop(sf_player* player);
SF_AUDIO_API void sf_player_destroy(sf_player* player);

// ---- Live input (additive; ABI v1 clients are unaffected) ------------------------------
// Capture endpoint inventory, same JSON shape as sf_devices_json. Opens no stream.
SF_AUDIO_API int32_t sf_input_devices_json(char* output, int32_t capacity, char* error, int32_t error_capacity);
// Returns 1 when this build includes live input, so clients can feature-detect.
SF_AUDIO_API uint32_t sf_live_abi_version(void);
typedef struct sf_live sf_live;
typedef struct sf_live_status {
    int32_t state, reason, input_channels, input_rate, output_channels, output_rate, exclusive_active, buffer_frames, latency_frames;
    uint64_t started_sequence, ended_sequence, captured_frames, rendered_frames, underruns, overruns;
    float input_peak; // 0..1 since the previous status call
    char error_utf8[1024];
} sf_live_status;
SF_AUDIO_API sf_live* sf_live_create(void);
// Start accepts a request. Empty IDs select the default endpoints. input_channel 0 = all input
// channels, 1..N = that single input. output_channel follows sf_player_start. exclusive applies to
// the OUTPUT endpoint only; capture is always shared. Poll status for the real worker state.
// gain is linear 0..4; output is clamped to +/-1. Live monitoring can feed back acoustically.
SF_AUDIO_API int32_t sf_live_start(sf_live* live, const char* input_device_id_utf8, int32_t input_channel, const char* output_device_id_utf8, int32_t output_channel, int32_t exclusive, int32_t buffer_ms, float gain);
SF_AUDIO_API void sf_live_gain(sf_live* live, float gain);
SF_AUDIO_API void sf_live_status_get(sf_live* live, sf_live_status* output);
// Same output-only diagnostics/latency semantics as the sf_player queries above.
SF_AUDIO_API int32_t sf_live_output_mode(sf_live* live);
SF_AUDIO_API double sf_live_output_latency_ms(sf_live* live);
SF_AUDIO_API void sf_live_stop(sf_live* live);
SF_AUDIO_API void sf_live_destroy(sf_live* live);
#ifdef __cplusplus
}
#endif
