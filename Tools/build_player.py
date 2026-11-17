"""Build, verify and package the Unity four-room showcase as a Windows x64 release player.

Silent verification only: a headless smoke run (native ABI, PCM, invalid device fails closed, no playing) and a
graphics run that renders five views from the RELEASE build (so shader stripping or missing assets would show).
Never plays audio. Output directory must be new/empty; the Unity editor must be closed.
"""
import argparse, hashlib, json, re, subprocess, zipfile
from pathlib import Path
from player_notices import archive_files, collect_notices, remove_player_backups, safe_file, write_notices
ROOT = Path(__file__).resolve().parents[1]
EXE = 'SpatialFabricRoomShowcase.exe'


def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()


def source_snapshot():
    commit = subprocess.check_output(['git', '-C', str(ROOT), 'rev-parse', '--verify', 'HEAD'], text=True).strip()
    if subprocess.check_output(['git', '-C', str(ROOT), 'status', '--porcelain'], text=True).strip():
        raise ValueError('Release player requires a clean committed source snapshot')
    return commit


def release_name(version):
    if not isinstance(version, str) or not re.fullmatch(r'\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?', version):
        raise ValueError('Invalid release package version')
    return f'SpatialFabricRoomShowcase-Unity-{version}-Windows-x64'


def require_passed(path):
    data = json.loads(path.read_text())
    if data.get('status') != 'passed' or data.get('failed') != []:
        raise ValueError(f'Release smoke verification failed: {path}')
    return data


def copy_release_documents(folder):
    for relative, target in [('ExampleProject/README.md', 'README.md'), ('ExampleProject/ASSET_LICENSES.md', 'ASSET_LICENSES.md'), ('LICENSE.md', 'LICENSE.md'), ('ExampleProject/OSCControllerProfile.json', 'OSCControllerProfile.json')]:
        (folder / target).write_bytes(safe_file(ROOT / relative, ROOT))


def main():
    p = argparse.ArgumentParser(); p.add_argument('--unity', required=True, type=Path); p.add_argument('--output', required=True, type=Path)
    p.add_argument('--license-cache', type=Path, help='Offline cache of the two exact pinned official notice documents')
    args = p.parse_args()
    out = args.output.resolve()
    if out.exists() and any(out.iterdir()): raise SystemExit('Output must be new or empty')
    if (ROOT / 'ExampleProject/Temp/UnityLockfile').exists(): raise SystemExit('Close the Unity editor first')
    source_commit = source_snapshot()
    source_dirty = False
    # Preflight notice availability/integrity before starting an expensive build.
    notice_files, notice_manifest = collect_notices(ROOT, args.unity, args.license_cache)
    notice_manifest.update(source_commit=source_commit, source_dirty=source_dirty)
    package = json.loads((ROOT / 'Packages/com.nplaudio.spatialfabric/package.json').read_text())
    name = release_name(package['version'])
    folder = out / name
    if folder.resolve().parent != out:
        raise ValueError('Release output escaped its approved directory')
    (out / 'evidence').mkdir(parents=True, exist_ok=True)
    subprocess.run([str(args.unity), '-batchmode', '-nographics', '-quit', '-projectPath', str(ROOT / 'ExampleProject'), '-executeMethod', 'NPLAudio.SpatialFabric.Example.Editor.BuildReleasePlayer.Export',
                    '-spatialFabricBuildDirectory', str(folder), '-logFile', str(out / 'evidence/build.log')], check=True, timeout=3600)
    remove_player_backups(folder)
    exe = folder / EXE
    libraries = list(folder.glob('**/SpatialFabricAudio.dll'))
    if not exe.is_file() or len(libraries) != 1: raise SystemExit(f'Unexpected build output: exe={exe.is_file()} dlls={libraries}')
    development = list(folder.glob('**/*.pdb')) + list(folder.glob('**/UnityPlayer_Win64_development*'))
    if development: raise SystemExit('A development build slipped in: ' + str(development[:3]))
    smoke = out / 'evidence/smoke.json'
    subprocess.run([str(exe), '-batchmode', '-nographics', '-spatialFabricRoomsSmoke', str(smoke), '-logFile', str(out / 'evidence/smoke.log')], check=True, timeout=120)
    data = require_passed(smoke)
    shots = out / 'evidence/screens'
    subprocess.run([str(exe), '-batchmode', '-screen-width', '1280', '-screen-height', '720', '-spatialFabricRoomsSmoke', str(out / 'evidence/render.json'), '-spatialFabricRoomShots', str(shots), '-logFile', str(out / 'evidence/render.log')], check=True, timeout=180)
    require_passed(out / 'evidence/render.json')
    rendered = sorted(shots.glob('*.png'))
    if len(rendered) != 5:
        raise ValueError(f'Expected five rendered views, got {len(rendered)}')
    copy_release_documents(folder)
    (folder / 'RUNNING.txt').write_text('SpatialFabric Unity four-room showcase (Windows x64, Mono, unsigned).\n\nRun SpatialFabricRoomShowcase.exe. Nothing plays or listens at startup.\n'
        'Walk with WASD + mouse (Esc frees the cursor). E plays the room cue THROUGH YOUR DEFAULT OUTPUT DEVICE; Q stops; X stops all.\n'
        'OSC and device selection are explicit in the Routing/OSC HUD. The Windows privacy/permission prompts and the unsigned-app warning are expected.\n'
        'Windows only: macOS and IL2CPP builds are not provided.\n', encoding='utf-8')
    # Fail if Unity changed the reviewed package resolution during the build.
    final_files, final_manifest = collect_notices(ROOT, args.unity, args.license_cache)
    final_manifest.update(source_commit=source_commit, source_dirty=source_dirty)
    if final_files != notice_files or final_manifest != notice_manifest:
        raise SystemExit('Notice inputs changed during build')
    if source_snapshot() != source_commit:
        raise ValueError('Source snapshot changed during build')
    notice_path = write_notices(folder, notice_files, notice_manifest)
    release_files = archive_files(folder)
    archive = out / (name + '.zip')
    with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as z:
        for item in release_files:
            z.write(item, item.relative_to(out))
    manifest = {'source_commit': source_commit, 'source_dirty': source_dirty,
                'license_bundle': {'manifest': notice_path.relative_to(folder).as_posix(), 'sha256': sha(notice_path), 'status': notice_manifest['status'], 'completeness_verified': False},
                'name': name, 'package_version': package['version'], 'unity': (ROOT / 'ExampleProject/ProjectSettings/ProjectVersion.txt').read_text().splitlines()[0].split(': ')[1], 'scripting_backend': 'Mono', 'development_build': False, 'signed': False,
                'smoke_checks': len(data['passed']), 'rendered_views': len(rendered), 'archive': {'name': archive.name, 'bytes': archive.stat().st_size, 'sha256': sha(archive)}, 'exe_sha256': sha(exe), 'native_dll_sha256': sha(libraries[0])}
    (out / 'BUILD_MANIFEST.json').write_text(json.dumps(manifest, indent=2) + '\n'); (out / 'SHA256SUMS.txt').write_text(f'{sha(archive)}  {archive.name}\n{sha(exe)}  {EXE}\n')
    print(json.dumps(manifest, indent=2))


if __name__ == '__main__': main()
