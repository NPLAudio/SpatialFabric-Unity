# Custom protocol and ADM controls (Unity 0.3)

Configure the selected manager in the SpatialFabric window/inspector. Legacy
`customAddressTemplate` remains the address source; `customProtocol` adds arguments,
coordinates, ranges, send mode, rate and change-only policy without changing old scenes.

## Custom arguments / ranges

All arguments are OSC float32, including `id`, matching the released Unreal adapter.
Names are whitespace-separated and case-insensitive:

| Token | Input before remap |
|---|---|
| `x y z` | Canonical stage front/right/up, each -1..1 |
| `gain` | Linear gain, clamped 0..1 for remapping |
| `width` | Width, clamped 0..1 for remapping |
| `id` | Positive effective Custom object ID, no remap |
| `azimuth` / `a` | -180..180 degrees; 0 front, positive left |
| `elevation` / `e` | -90..90 degrees |
| `distance` / `d` | Canonical 3D radius clamped 0..1 |

Polar arguments are computed only in **AED** mode; in XYZ mode they are zero, matching
Unreal. Selecting AED does not implicitly rewrite `x y z`: explicitly choose `a e d`.
XYZ and all polar values map to `positionMin..positionMax`; gain/width use their own
ranges. Reverse output ranges are allowed. Inputs clamp before remapping. Custom XYZ
is cube-normalized; it is not implicitly ADM sphere-limited.

**Packed:** one address and all arguments. Example `/renderer/{id}/position`, args `x y z`.
**Discrete:** one single-float message per argument. Example `/renderer/{id}/{axis}`,
args `a e d`, mode AED → `/azimuth`, `/elevation`, `/distance`. Aliases resolve to full
axis names; repeated tokens deliberately produce repeated messages.

## Address substitutions

`{id}`, `{x}`, `{y}`, `{z}`, `{gain}`, `{width}`, `{label}` and per-object custom fields
such as `{room}`. `{axis}` is available only in Discrete mode. Float substitutions use
invariant decimal text, up to six fractional digits, with at least `.0`, like Unreal's
sanitized numeric address formatting. Substitution is one pass, never executable code.

Each object's Custom ID override is -1 (inherit) or positive. Fields must have unique,
case-sensitive identifier keys, cannot shadow built-ins, and cannot contain embedded
NUL or template braces. Unknown arguments, unresolved tokens, invalid final addresses,
non-finite values and excessive sizes refuse the entire Custom frame **before any of
its packets are sent**. Correct configurations are not partially emitted; error text is
visible on the manager. These strict invalid-input rules intentionally avoid ambiguous
reserved-field/template injection behavior.

Bounds: 64 fields/object, 32 arguments, 4096 outgoing messages/frame. Port/IP and every
encoded message validate before sending. OSC UDP itself is not transactional or reliable;
a socket failure after some sends cannot retract packets already received.

## Rate / change policies

ADM and Custom each have an independent rate override (0 inherits manager `sendRateHz`)
and Send Only On Change. Successful packet bytes plus destination form the cache, so
changing endpoint/ID/template/metadata sends again. Restart clears the cache. Failed or
invalid batches are never committed to it. Change-only mode is not a delivery guarantee
and has no implicit heartbeat; use fixed-rate output when the receiver needs one.

ADM can emit XYZ, AED or Both. Metadata remains enabled; XYZ/AED share a sphere-limited
point. Query adapter records expose mode, effective rate, ranges, templates and custom
fields. `custom_fields` remains a JSON object; raw `custom_field_entries` also preserve
configuration diagnostics without discovery repairing anything.
