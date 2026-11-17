# Unity 0.6 player packaging notices

`build_player.py` now preflights the reviewed notice inputs before invoking Unity,
rechecks them after the build, writes `ThirdPartyNotices/`, verifies its manifest,
and only then creates the ZIP. It removes both Unity backup directory names
(`BackUpThisFolder_ButDontShipItWithYourGame` and the actual product-prefixed
`SpatialFabricRoomShowcase_BackUpThisFolder_ButDontShipItWithYourGame`) before
checking for development files. It rejects remaining backups, symlinks/junctions,
`unity_license.json`, and `.ulf` files before ZIP creation.

## Reviewed sources

- Unity **6000.6.9f1**, revision **8c45277fb9c5**, Windows x64 **Mono** only.
  The installed `Unity.exe` and `Unity.dll` are hash-pinned; they are inspected,
  never copied into the notice bundle.
- Official release page: https://unity.com/releases/editor/whats-new/6000.6.9f1
  Its Third Party Notices section links the Windows Mono Player PDF pinned in
  `Tools/player_notices.py`. The complete PDF includes Mono (Unity Fork) and
  the Player's listed third-party copyright/license texts. Do not substitute an
  Editor, IL2CPP, WebGL, or unrelated generic Mono license document.
- Unity Companion License: https://unity.com/legal/licenses/unity-companion-license
  Complete official main-body text and link destinations are normalized and SHA256-pinned
  (v1.4, 10.29.2024). Navigation, analytics and volatile page markup are excluded;
  any change to the legal body or its links fails verification.
- Actual resolved URP, Core, URP Config, Collections, Profiling Core and Shader
  Graph notice files from the isolated project's `Library/PackageCache`.
  UGUI and Mathematics are conservative inclusions from the selected Editor's
  `Data/Resources/PackageManager/BuiltInPackages`, not represented as lock-resolved.
  Missing/ambiguous cache packages never fall back to a different installed copy.
- Exact Editor `Data/Tools/Burst/LICENSE.md` and `Third Party Notices.md`, both
  SHA256-pinned, conservatively cover the generated Burst code's notice set,
  including LLVM exception/NCSA texts. This does not prove a specific LLVM version.
- Project MIT covers SpatialFabricAudio / sf_audio, whose other calls are OS
  system APIs. No arbitrary native third-party dependency is assumed.

Only exact allowlisted notice filenames are copied; package trees, Editor
activation files, and `licenseloader_template.html` are **not** notice sources.
The template is merely a license-server redirect, not distributable notices.
Project manifest, lockfile, version/revision, package metadata and notice bytes
are pinned. Repository text input pins use LF normalization for Git checkouts;
installed package and official document pins verify exact bytes.

`ThirdPartyNotices/LICENSE_BUNDLE_MANIFEST.json` records file SHA256/length,
source URL/path, package version/resolution/metadata hash, excluded editor/test
packages, engine-provided components, input pins and limitations.
`BUILD_MANIFEST.json` anchors that manifest by SHA256 and records `git HEAD`
(`source_commit`) and whether the source checkout was dirty (`source_dirty`).
The archive hash covers both notices and their manifest. The builder requires a
clean committed snapshot before and after building and validates both silent smoke
and graphical render results explicitly, including under Python `-O`. These hashes
are local integrity pins, **not** Unity signatures.

## Offline / reproducible input use

Optional `--license-cache DIRECTORY` reads only these two fixed files and never
falls back to the network if one is absent or wrong:

- `Unity-Player-Windows-Mono-6000.6.9f1.pdf`
- `Unity-Companion-License.txt` (the canonical main-body text, not raw site HTML)

Their exact URLs and SHA256 pins are in `DOCUMENTS` in `Tools/player_notices.py`.
Without this option the same two documents are downloaded over HTTPS and checked.
If an upstream page changes, obtain/review the official replacement and update
pins deliberately; do not bypass checks or silently omit a notice.

## Limits / remaining release review

The manifest says `verified_allowlisted_inputs_only`, **not complete compliance**.
The matching Windows Mono Player TPN and exact Editor Burst notices are included.
Individual built-in modules are engine-provided components; no fabricated per-module
notice hashes are assigned. The sample uses engine `LegacyRuntime.ttf` and default
IMGUI resources, not an imported font package. Retain `ASSET_LICENSES.md` alongside MIT;
engine resources are not relicensed under the project's MIT license.

This is reviewed notice bundling, **not whole-distribution legal certification**.
The Burst documents do not independently prove generated DLL contents or LLVM version.
A changed package resolution, target/backend, native dependency or asset inventory
needs renewed review; this tooling cannot infer legal completeness from installed packages.

Offline tests (no Unity execution or player build):

```text
python -m unittest discover -s Tools -p test_player_notices.py -v
```

No existing release output is repaired in place by these changes. Commit and
rebuild from the isolated clone only when separately authorized.
