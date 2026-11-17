"""Build, verify and package the Unity four-room showcase as a Windows x64 release player.

Silent verification only: a headless smoke run (native ABI, PCM, invalid device fails closed, no playing) and a
graphics run that renders five views from the RELEASE build (so shader stripping or missing assets would show).
Never plays audio. Output directory must be new/empty; the Unity editor must be closed.
"""
import argparse, hashlib, json, shutil, subprocess, tempfile, zipfile
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
EXE = 'SpatialFabricRoomShowcase.exe'


def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    p = argparse.ArgumentParser(); p.add_argument('--unity', required=True, type=Path); p.add_argument('--output', required=True, type=Path)
    args = p.parse_args()
    out = args.output.resolve()
    if out.exists() and any(out.iterdir()): raise SystemExit('Output must be new or empty')
    if (ROOT / 'ExampleProject/Temp/UnityLockfile').exists(): raise SystemExit('Close the Unity editor first')
    package = json.loads((ROOT / 'Packages/com.nplaudio.spatialfabric/package.json').read_text())
    name = f'SpatialFabricRoomShowcase-Unity-{package["version"]}-Windows-x64'
    folder = out / name; (out / 'evidence').mkdir(parents=True, exist_ok=True)
    subprocess.run([str(args.unity), '-batchmode', '-nographics', '-quit', '-projectPath', str(ROOT / 'ExampleProject'), '-executeMethod', 'NPLAudio.SpatialFabric.Example.Editor.BuildReleasePlayer.Export',
                    '-spatialFabricBuildDirectory', str(folder), '-logFile', str(out / 'evidence/build.log')], check=True, timeout=3600)
    exe = folder / EXE
    libraries = list(folder.glob('**/SpatialFabricAudio.dll'))
    if not exe.is_file() or len(libraries) != 1: raise SystemExit(f'Unexpected build output: exe={exe.is_file()} dlls={libraries}')
    development = list(folder.glob('**/*.pdb')) + list(folder.glob('**/UnityPlayer_Win64_development*'))
    if development: raise SystemExit('A development build slipped in: ' + str(development[:3]))
    smoke = out / 'evidence/smoke.json'
    subprocess.run([str(exe), '-batchmode', '-nographics', '-spatialFabricRoomsSmoke', str(smoke), '-logFile', str(out / 'evidence/smoke.log')], check=True, timeout=120)
    data = json.loads(smoke.read_text()); assert data['status'] == 'passed' and not data['failed'], data
    shots = out / 'evidence/screens'
    subprocess.run([str(exe), '-batchmode', '-screen-width', '1280', '-screen-height', '720', '-spatialFabricRoomsSmoke', str(out / 'evidence/render.json'), '-spatialFabricRoomShots', str(shots), '-logFile', str(out / 'evidence/render.log')], check=True, timeout=180)
    rendered = sorted(shots.glob('*.png')); assert len(rendered) == 5, rendered
    for source, target in [(ROOT / 'ExampleProject/README.md', 'README.md'), (ROOT / 'ExampleProject/ASSET_LICENSES.md', 'ASSET_LICENSES.md'), (ROOT / 'LICENSE.md', 'LICENSE.md'), (ROOT / 'ExampleProject/OSCControllerProfile.json', 'OSCControllerProfile.json')]:
        if source.is_file(): shutil.copy2(source, folder / target)
    (folder / 'RUNNING.txt').write_text('SpatialFabric Unity four-room showcase (Windows x64, Mono, unsigned).\n\nRun SpatialFabricRoomShowcase.exe. Nothing plays or listens at startup.\n'
        'Walk with WASD + mouse (Esc frees the cursor). E plays the room cue THROUGH YOUR DEFAULT OUTPUT DEVICE; Q stops; X stops all.\n'
        'OSC and device selection are explicit in the Routing/OSC HUD. The Windows privacy/permission prompts and the unsigned-app warning are expected.\n'
        'Windows only: macOS and IL2CPP builds are not provided.\n', encoding='utf-8')
    archive = out / (name + '.zip')
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as z:
        for item in sorted(folder.rglob('*')):
            if item.is_file(): z.write(item, item.relative_to(out))
    manifest = {'name': name, 'package_version': package['version'], 'unity': (ROOT / 'ExampleProject/ProjectSettings/ProjectVersion.txt').read_text().splitlines()[0].split(': ')[1], 'scripting_backend': 'Mono', 'development_build': False, 'signed': False,
                'smoke_checks': len(data['passed']), 'rendered_views': len(rendered), 'archive': {'name': archive.name, 'bytes': archive.stat().st_size, 'sha256': sha(archive)}, 'exe_sha256': sha(exe), 'native_dll_sha256': sha(libraries[0])}
    (out / 'BUILD_MANIFEST.json').write_text(json.dumps(manifest, indent=2) + '\n'); (out / 'SHA256SUMS.txt').write_text(f'{sha(archive)}  {archive.name}\n{sha(exe)}  {EXE}\n')
    print(json.dumps(manifest, indent=2))


if __name__ == '__main__': main()
