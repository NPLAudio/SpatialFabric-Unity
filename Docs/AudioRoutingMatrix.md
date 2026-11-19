# Audio routing matrix

## Integration

Open **Tools → SpatialFabric → Open Window → Outputs**. The audio-routing grid appears first; **OSC / network destinations (not audio channels)** is a separate collapsed section below it. No manager is required to route existing playback cues. The host lazily keeps one `NPLAudio.SpatialFabric.Editor.AudioRoutingPanel` instance and calls `Draw(PlaybackCue[] cues, Rect visibleScreen)` with the loaded authoring-scene inventory and ancestor scroll viewport in screen coordinates. Construction and drawing only read the project-local cache and cue properties. They do not enumerate devices, prepare audio, play, stop, or capture.

**Refresh Interfaces** explicitly calls `NativeAudio.Probe()` then `NativeAudio.RefreshDevices()`. A nonempty `DeviceEnumerationError` invalidates the result even when native code returns an older `CachedDevices` array. A failed refresh marks session entries offline, preserves history and cue routes, and does not save the cache. This is an inventory action, not a stream test.

## Authoring

**Routing grid** is the default view: one rectangular crosspoint matrix, with playback-cue rows on the left, grouped audio-interface headers across the top, and aligned output-channel columns. Click an intersection to assign that cue's endpoint/channel. A green check means **configured route**, not live playback or a verified hardware connection. Each cue has one route; clicking its existing check is a no-op, not a disconnect. This is not a Dante network subscription editor or a per-source DSP/multi-send mixer.

Both axes scroll while row and column headers remain fixed. Only visible cells are rendered; all cues and interfaces remain reachable by scrolling, without per-interface foldouts or channel pages. Narrow interface headers are vertical. Hover highlights the row/column and exposes the exact endpoint ID and routing semantics. Input and painted-cell rectangles are clipped to the enclosing window/scroll viewport. At an internal scroll limit, wheel input is left for the parent panel. Presentation snapshots are captured on Layout, while every mutation still validates the live cue.

Choose **Interface / channel dropdowns** for a compact alternative with 16 cue rows per page. Each cue has **Audio interface** and **Output channel** selectors. The same selectors appear on expanded cues in the **Playback** tab, with a shared **Refresh Interfaces** button. Duplicate interface names include their IDs. Changing interfaces preserves the current channel rather than silently clamping it.

Channel **Auto / 0** routes source channels to corresponding output channels. Positive channels are **1-based destinations for the first source channel**. Multi-channel source fit is not inferred or guaranteed. Known interfaces expose all cached channels even offline; unknown IDs expose only Auto and explicit stored positive channels, not invented capacity. Out-of-range stored channels remain visible (marked `*` in the matrix), never clamped or erased.

Cached/offline interfaces remain assignable with an offline indication. Successful enumeration that omits an interface keeps its cached layout and last-seen timestamp; it never rewrites `configuredDeviceId` or `outputChannel`. Reconnection matches exact IDs, not names. The separate **Dynamic OS default** group writes the empty device ID and follows the OS at playback time. Its capacity is unknown/dynamic, not copied from today's default hardware interface. Refresh explicitly discovers interfaces; opening the window never does so.

Changing a cell records Undo on that cue only, records prefab property overrides, sets the cue dirty, and marks its scene dirty. Other cue fields are untouched. A fresh mutation-time preflight rejects missing/destroyed cues, Play Mode/transitions, compiling/importing, active workers, active playback/pending replay, persistent assets, preview/prefab stages, locked/temporary hierarchy objects, immutable prefabs and scenes not open for edit. The panel never stops a cue to make an edit succeed. Other stopped cues can still be authored while one cue is active.

## Persistence and seams

`AudioInterfaceCache` uses project-local `EditorUserSettings` key `SpatialFabric.AudioInterfaces.v1`. Only successful, validated explicit refreshes save. The schema stores endpoint ID, name, channel count, sample rate (0 means unknown), and UTC last-seen timestamp. Online state is session-only and always false on load, including domain reload. No cue references or routes are stored in this cache; cue assignments serialize normally in scenes/prefabs.

Bounds: schema version 1, 256 historical endpoints, 4096 channels/interface, 1024 characters/text field, 2 MiB JSON, sample rates 0–768000 Hz. Empty IDs, duplicate IDs, control characters and invalid bounds reject the entire inventory. When history is full, new inventory is rejected rather than evicting an unplugged endpoint. No implicit deletion or eviction occurs.

Hardware-free public seams:

- `AudioInterfaceCache(Func<string> load, Action<string> save)` injects persistence.
- `AcceptInventory(AudioDeviceInfo[], string enumerationError, DateTime utcNow)` validates/merges an explicit inventory. `Devices` returns defensive snapshots.
- `AudioRoutingPanel(AudioInterfaceCache, IAudioRoutingInventory)` injects probe/refresh/error behavior; `RefreshInterfaces()` is the explicit action.
- `AssignRoute(PlaybackCue, string deviceId, int channel, out string reason)` performs the same fresh guarded Undo mutation used by cells; false also represents a no-op.
- `CanAssign`, `IsStopped`, `IsRoute` and `ChannelLabel` expose guard/route semantics for tests.

`Tests/EditMode/AudioRoutingPanelTests.cs` covers offline read-only loading/no automatic native calls, explicit refresh, stale enumeration errors, unplug/reconnect preservation, invalid inventory/schema bounds, defensive copies, unknown/default/channel distinctions, Undo/Redo, unrelated-field preservation, active/replay guard semantics and locked/destroyed targets. Run in the disposable Unity project; no hardware is required. The graphics-enabled Unity **6000.6.9f1** suite passed **139 EditMode + 6 PlayMode**, zero skips/failures, with native checks required. `SceneObjectManagerTests` additionally exercises actual window channel-cell mouse input and Undo, cached offline cells after a simulated unplug, actual dropdown rendering in Outputs and Playback, duplicate-name/offline choices, available-channel lists and unknown stored channels, ancestor clipping, all four operator tabs with existing/unbound components, managerless browsing, collapsed property sections, and bounded/immutable presentation snapshots. `AudioRoutingMatrixViewTests` additionally covers channel/column mapping, geometry, large compact channel spans, visible ranges, ancestor intersections and wheel limits. Evidence: `C:/Temp/SFSceneObjectManager-20261118/true-crosspoint-clipping/validation.json`. No physical stream or microphone capture was started; unplug/reconnect is simulated inventory, not a physical hotplug certification.

## Manual UX checks / limits

Verify both-axis scrolling and dropdown pagination with large cue inventories and high-channel interfaces, actual endpoint unplug/reconnect, prefab-instance overrides, read-only/version-controlled scenes, and Play Mode transition timing in the host window. Default/unknown routes intentionally cannot predict hardware capacity. The matrix does not validate source width or silently fix invalid legacy routes. Session online means last explicit successful enumeration, not continuous hotplug monitoring. No automatic polling, stream health test, cache-clear UI, search, or per-source mixing is provided.
