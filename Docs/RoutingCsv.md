# Routing CSV — v1 compatibility and v2 source assignment

Window → CSV → export or preview. Import is explicit authoring, not discovery/transport.
All loaded-world OSC/audio/replay must be stopped. Nothing is stopped automatically.

Every record parses/validates/resolves its scene key before a preview exists. Apply rechecks
manager/target identities/configuration, resolves explicit v2 source references, rechecks
idle/unchanged state, and records the whole import as one Undo group. Invalid/duplicate/
unknown/stale records refuse the entire file, never label-match a different object.
Prefab modifications are recorded; every affected loaded scene is dirtied but not saved.
Native PCM/devices/transport are never started by CSV.

## Spatial routes v1

`Schema,Key,ActorPath,ObjectId,CustomObjectId,SendAdm,SendCustom,Gain,Width,Mute,CustomFields`

Schema `sf-unity-spatial-routing/1`. Key is exact saved-scene component identity; ActorPath
is context, never a rename/retarget fallback. ObjectId positive int32; CustomObjectId -1
(inherit) or positive. Booleans true/false or 1/0. Gain 0..2, width 0..1, finite invariant
decimals. CustomFields is quoted JSON `{"entries":[{"key":"room","value":"Hall_A"}]}`;
empty is `{"entries":[]}`. Sender field validation is shared. Exports cover distinct bound
components in the explicit loaded world, not automatic object creation/removal.

Independent destination definitions, explicit pose bindings and per-destination overrides
are Unity-serialized authoring configuration edited in Objects/Outputs panels; this v1
route table deliberately remains compatible rather than silently changing its columns.

## Playback routing v1

`Schema,Key,ActorPath,DeviceId,OutputChannel,OscBase,Loop,AutoReplay,ReplayDelaySeconds,ExclusiveRequested,BufferMsRequested`

Schema `sf-unity-playback-routing/1`. Preserves existing source references. DeviceId remains
literal; empty means default, no replacement enumeration. OutputChannel 0..64 (actual
hardware count still checked at playback); OSC base explicit, no unresolved/trailing slash.
Delay 0..86400 seconds, buffer request 0..2000 ms, finite invariant numbers/booleans.

## Playback routing + sources v2 (default UI export)

Same columns plus `PreparedAudioRef,ClipRef`, schema `sf-unity-playback-routing/2`.
References are **GUID:localFileId**, not machine paths or ambiguous asset names. Empty
explicitly clears that source slot. Both slots may be assigned; prepared PCM takes priority.
Signed local IDs support imported subassets. References must resolve to exactly one saved
PreparedAudioAsset or AudioClip of the correct type in Assets/Packages.

Preview captures asset path/dependency hash without loading the source. Only after explicit
Apply are assets resolved/loaded, all before any scene mutation. A changed hash/path/type,
missing local ID, stale scene target or newly live transport refuses application. Loading
an AudioClip may invoke Unity's importer/decompression policy; it is not a native PCM
Prepare call or playback. Native cache readiness becomes false when its source/revision
changes. Undo restores source and route references together.

These schemas are Unity authoring, not a claim Unreal actor/SoundWave references can be
loaded into Unity. V1 imports remain supported. No arbitrary scene load, hardware query,
manager endpoint update or hidden transport command occurs.

## Format/security

UTF-8 (BOM accepted), RFC4180 quotes, CRLF export, embedded commas/quotes/newlines/Unicode
preserved. Complete headers may reorder; unknown/duplicate/missing columns, short/extra
rows, duplicate keys, malformed quotes and non-finite/range-invalid values fail. Limits:
4 MiB/file, 16,384 routes, 16,384 characters/field, 64 columns. Nothing auto-saves assets.

Treat CSV as data. Quoting is not spreadsheet formula sanitization: do not execute formulas
from untrusted scene labels/device IDs. Exact scene keys can change when a scene is moved;
re-export deliberately rather than guessing a label match.
