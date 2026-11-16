#!/bin/bash
# Run on macOS with Xcode command line tools/CMake. Silent tests never open valid output.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BUILD="${SF_NATIVE_BUILD:-$(mktemp -d -t SpatialFabricAudio)}"
cmake -S "$ROOT/Native" -B "$BUILD" -DCMAKE_BUILD_TYPE=Release '-DCMAKE_OSX_ARCHITECTURES=arm64;x86_64' -DCMAKE_OSX_DEPLOYMENT_TARGET=12.0
cmake --build "$BUILD" --config Release
ctest --test-dir "$BUILD" -C Release --output-on-failure
if [[ "${1:-}" == "--install" ]]; then
  if [[ -e "$ROOT/ExampleProject/Temp/UnityLockfile" ]]; then echo 'Close the Unity example before installing native output.' >&2; exit 1; fi
  TARGET="$ROOT/Packages/com.nplaudio.spatialfabric/Runtime/Plugins/macOS"
  mkdir -p "$TARGET"
  cp "$BUILD/libSpatialFabricAudio.dylib" "$TARGET/"
  cp "$ROOT/Native/UnityPluginImporterMac.meta" "$TARGET/libSpatialFabricAudio.dylib.meta"
fi
printf 'Native build/test evidence: %s\n' "$BUILD"
