# Central Scene Object Manager

Open **Tools → SpatialFabric → Open Window → Scene Objects** (the default tab).
No SpatialFabric manager is required. This is a scene authoring grid, **not** a
network-routing matrix or a system of links between properties. The existing
Stage/radar, Objects/bindings, Outputs, Playback, CSV and Diagnostics tabs remain.

## Scope and selection

- Lists every placed GameObject in **all loaded main authoring scenes**,
  including inactive roots/children, unconfigured objects, scene prefab instances,
  and saved objects hidden from the Hierarchy.
  This is independent of any manager's `additionalScenePaths` runtime world scope.
- Excludes project/prefab assets, Prefab Mode/preview scenes, unloaded scenes, and
  non-saveable editor hierarchies. Locked placed objects remain visible
  with a `[locked]` marker; remove them from the selection to enable mutation.
  It does not scan project assets.
- Rows show scene/hierarchy, inactive status and attached SpatialFabric components.
  Search matches scene/hierarchy names; the filter selects unconfigured objects or a
  component type. Checkboxes support multi-selection; **Select visible rows** adds
  visible rows, **Clear rows** clears all, and **Use Hierarchy selection** replaces it.
- **Follow Hierarchy selection** is on by default, including the selection that
  already exists when the window opens. Selecting an object in Unity immediately
  binds its real SpatialFabric components; existing property sections open automatically.
  Clicking a row name also selects it and opens its properties, rather than merely pinging.
- Manual row/checkbox/bulk selection switches off following so an unrelated Hierarchy
  event cannot replace a carefully chosen batch. **Use Hierarchy selection** or the
  follow toggle restores synchronization. Selection survives search/filter changes.
  Selected and hidden-selected counts are explicit: **bulk operations affect all
  selected rows, including hidden rows**. **Select in Hierarchy** and **Frame**
  synchronize Unity selection/frame on demand. Deleted/unloaded targets are pruned.
- **Refresh scene** explicitly invalidates the display snapshot. Empty property
  sections explain whether no row is selected or the objects still need SF components.

## Large-scene display behavior

Rows have a fixed 20-pixel height; only rows intersecting the scroll viewport are
painted or handle clicks. **Select visible rows** still means every row matching
the current search/filter, not merely the viewport. Hidden-selection counts use
hash-set membership, so selecting 10,000 rows no longer causes quadratic work.

Inventory, hierarchy/search paths, component summaries and display-only editability
are cached for at most 250 ms while the panel is drawing. Hierarchy changes and
Undo/Redo invalidate the cache, as do panel additions/serialized edits (including
failed operations). External component, scene, active/locked-state and rename
changes are also picked up by the bounded refresh. Filter/search changes reuse the
inventory snapshot. Snapshots and shared-property section membership update on
Layout, avoiding a changing GUILayout tree during input/Repaint. Destroyed property
targets abort that pass and are recaptured on Layout. Row clicks retain object
identity across scrolling/filtering and cannot toggle another row on release.

The cached idle/lock status is only UI feedback: **every actual add/apply still
runs the uncached Edit Mode/idle and target-validity guards immediately before
mutation**. Serialized values are not cached; Inspector edits remain live.

## Add components explicitly

Choose a type under **Add to selected**, then **Add missing (one Undo)**. There is
no preselected component and no automatic creation/binding of managers, stages,
listeners, AudioSources or audio. Supported types are `SpatialObject`, `PlaybackCue`,
`LiveInputCue`, `SpatialPathPlayer`, `SpatialStage` and `SpatialFabricManager`.
Adding a manager or stage adds **only that explicitly chosen component**, never
another object. Adding `SpatialObject` does not insert it into a manager's binding
list; the existing Objects tab still owns that explicit workflow.

The whole batch is validated before addition. Existing components are retained,
never duplicated or reset. All additions share one Undo/Redo group; an unexpected
Unity addition failure rolls back the group's additions where Unity Undo supports
it. Prefab instance overrides and affected scene dirtiness are recorded. As with
other Unity authoring operations, arbitrary external editor callbacks cannot be
transactionally reversed beyond what Unity records in Undo.

## Shared values, not copies

**Expand all / Collapse all** controls the shared property sections. Stage, Objects
and Playback also have compact per-component foldouts and their own expand/collapse
controls; Objects and Playback subdivide longer property sets into collapsible groups.
The operator tabs now find existing scene managers without requiring a manager to be
selected in the Hierarchy. Stage/Objects/Playback browse all loaded authoring components,
including unassigned/unbound/inactive instances; browsing does not change runtime scope
or enroll anything. An explicit manager selection disables auto-find, preserving the
chosen context for output edits. Outputs includes a separate cached
[audio-routing matrix](AudioRoutingMatrix.md), with OSC destinations in their own section.

Component sections open automatically for a new selection or newly added type;
you can collapse them afterward. They edit actual serialized properties, grouped by
type across the selected rows. Section counts identify how many components are
being edited; rows without that component are not changed or silently configured.
Unity `SerializedObject`/`SerializedProperty` fields display mixed values as **—**
and use normal Undo, prefab override and scene persistence semantics. Setting a
mixed field assigns it to every component in that section. Normal Inspector
changes appear here on the next repaint, and central changes appear in the
Inspector; no parallel property store or link is created. Only SpatialFabric
component properties are drawn, never generic GameObject/Transform/third-party
properties or custom Inspector transport controls.

## Safety policy

The grid is view/select-only in Play Mode, during a pending Play transition,
compilation/import, or while any listed scene has active OSC, cue playback/replay,
live input (including starting workers), or path playback. Stop those through
existing controls before editing. Mutation helpers independently enforce the same
Edit Mode/idle guard, not just disabled buttons.

Startup/activation fields (`playOnStart`, `playOnEnable`, `enableInEditor`,
`enableInPlay`, `runInBackground`, component enabled state) and internal identity
are not exposed here. Existing values are preserved. Use the individual Inspector
for deliberate startup opt-ins. Opening, searching, adding, or editing the grid
never probes devices, prepares PCM, starts capture/playback/networking, or moves a
path. There is no Start button here. **LiveInputCue capture still requires the
existing Inspector's explicit Start live confirmation and any OS permission**;
adding or configuring a live cue is not capture consent. Existing external OSC
control is unchanged. No physical audio/microphone certification is implied.

## Regression tests and validation

`Packages/com.nplaudio.spatialfabric/Tests/EditMode/SceneObjectManagerTests.cs`
contains real Unity tests for inactive/unconfigured/nested/additive enumeration;
asset/prefab-content/preview/editor hierarchy exclusion; batch add, duplicate
prevention, one-step Undo/Redo; invalid-batch preflight; every allowed type's safe
defaults/no dependent objects; multi-object mixed values, shared Inspector values,
scene dirtiness, Undo/Redo, prefab overrides, saved-scene reload, startup-field
exclusion, rejection of locked/non-SpatialFabric edits, and actual Play Mode mutation refusal.
Nine additional cases cover viewport bounds (including 10,000-row and stale-scroll
inputs), snapshot reuse, immediate filtering, the 250-ms refresh boundary,
component/path/deletion invalidation with Undo, and fresh mutation refusal after a
cached editable target becomes locked. Two additional graphics tests show the actual
window and assert painted scene rows and property controls: initial/current Hierarchy
selection, idle rename/component changes, Undo, selection replacement/deletion, and
real mouse down/up on a row label. The row-click test uses the painted screen rectangle
so native title-bar height and DPI do not corrupt its input coordinates.

Run the portable checks from the repository root:

```sh
python -m unittest discover -s Tools -p 'test_*.py'
dotnet run --project Tools/CoreChecks/CoreChecks.csproj -- Packages/com.nplaudio.spatialfabric/Tests/EditMode/Fixtures/osc-v1.json
python Tools/validate_unity.py --isolated --unity "C:/Program Files/Unity/Hub/Editor/6000.6.9f1/Editor/Unity.exe" --output "C:/Temp/SpatialFabricSceneObjectValidation"
```

Use a fresh output directory outside the repository. Isolated validation copies
only authored inputs/package, disables engine audio in the copy, and writes Unity
Library/Temp/logs/results only in that copy. It never rewrites the original
`ExampleProject/ProjectSettings/InputManager.asset`, Library, Temp or native binaries.
The full suite includes existing silent/offline/invalid-device tests; these new
authoring tests do not open devices or start networking. XML results, not merely
Unity's exit code, are the pass evidence. Automated tests do not certify physical
device behavior or human interaction with selection/frame.

### Local evidence (2026-11-18)

Unity **6000.6.9f1**, graphics-enabled, in the isolated project: **139 EditMode +
6 PlayMode passed, zero skipped/failed**, with `--require-native`. Coverage includes
performance/cache, actual-window live-binding/input, all four operator tabs with
existing unbound components, managerless browsing, collapse/reopen, pinned manager
context, audio-interface cache/unplug safety, real crosspoint-grid mouse input/Undo,
ancestor clipping, and rendered interface/channel dropdowns in Outputs and Playback.
All seven window tabs remain covered. Local XML/report evidence is under
`C:/Temp/SFSceneObjectManager-20261118/true-crosspoint-clipping/` (`EditMode.xml`, `PlayMode.xml`,
`validation.json`). The copy was created with `--isolated`; subsequent full-suite
runs used `--project` pointing only at that same isolated copy after syncing the
current package Editor and EditMode test files; the copy path was confirmed from the prior
`verified/validation.json` as `C:/Temp/SFSceneObjectManager-20261118/Project`.
No original Library, InputManager or native binary was copied or changed by this
followup. Prior portable checks: **54 Python tests (2 skipped)**,
**196 core checks passed**, and interop fixtures verified. No valid-device audio
or microphone capture was started. Visual layout and physical hardware behavior
remain manual acceptance items.
