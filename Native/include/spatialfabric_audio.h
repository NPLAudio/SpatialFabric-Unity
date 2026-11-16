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
SF_AUDIO_API int32_t sf_player_start(sf_player* player, sf_buffer* buffer, const char* device_id_utf8, int32_t output_channel, int32_t exclusive, int32_t buffer_ms, int32_t loop);
SF_AUDIO_API void sf_player_loop(sf_player* player, int32_t loop);
SF_AUDIO_API void sf_player_status(sf_player* player, sf_status* output);
// Stop cancels and joins the worker; destroy also stops. No callbacks enter managed code.
SF_AUDIO_API void sf_player_stop(sf_player* player);
SF_AUDIO_API void sf_player_destroy(sf_player* player);
#ifdef __cplusplus
}
#endif
