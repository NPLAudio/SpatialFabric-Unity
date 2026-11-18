"""Build/run Windows player: silent deployment/native PCM/invalid-device checks.

Use --isolated to avoid the working project's Library/Temp and persistent-data
identity. No release archive, module installation or physical audio acceptance.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
from validate_unity import prepare_isolated_project

ROOT = Path(__file__).resolve().parents[1]


def check_report(data):
    required = ('native_available', 'offline_render', 'invalid_device_failed', 'no_playing')
    if (data.get('status') != 'passed' or data.get('error') not in (None, '')
            or any(data.get(key) is not True for key in required)
            or data.get('scene_records') not in (7, 9)):
        raise RuntimeError('Standalone player smoke failed/incomplete: ' + str(data))
    return data


def check_output_path(folder, source):
    folder, source = Path(folder).resolve(), Path(source).resolve()
    if folder == ROOT or ROOT in folder.parents:
        raise RuntimeError('Use player evidence outside the source repository')
    if folder == source or source in folder.parents:
        raise RuntimeError('Player evidence must not be inside the source project')


def main():
    parser = argparse.ArgumentParser(__doc__)
    parser.add_argument('--unity', required=True, type=Path)
    parser.add_argument('--project', type=Path, default=ROOT/'ExampleProject')
    parser.add_argument('--output', type=Path)
    parser.add_argument('--isolated', action='store_true')
    parser.add_argument('--native-dll', type=Path, help='Override native DLL in the isolated copy ONLY; requires --isolated')
    parser.add_argument('--il2cpp', action='store_true')
    args = parser.parse_args()
    if args.native_dll and not args.isolated:
        raise RuntimeError('--native-dll requires --isolated; working plugin replacement is forbidden')
    folder = (args.output or Path(tempfile.mkdtemp(prefix='SpatialFabricUnityPlayer-'))).resolve()
    check_output_path(folder, args.project)
    if args.output and folder.exists() and any(folder.iterdir()):
        raise RuntimeError('Choose empty output; previous evidence is never replaced')
    folder.mkdir(parents=True, exist_ok=True)
    project = args.project.resolve()
    if args.isolated:
        project = folder/'Project'
        prepare_isolated_project(args.project.resolve(), ROOT/'Packages/com.nplaudio.spatialfabric', project)
        if args.native_dll:
            native = args.native_dll.resolve()
            if not native.is_file() or native.suffix.lower() != '.dll':
                raise RuntimeError('Expected a built native DLL')
            target = project/'Packages/com.nplaudio.spatialfabric/Runtime/Plugins/x86_64'
            target.mkdir(parents=True, exist_ok=True)
            shutil.copy2(native, target/'SpatialFabricAudio.dll')
            shutil.copy2(ROOT/'Native/UnityPluginImporter.meta', target/'SpatialFabricAudio.dll.meta')
    print('Player evidence:', folder, flush=True)
    player = folder/'Player'
    command = [str(args.unity.resolve()), '-batchmode', '-nographics', '-quit', '-projectPath', str(project),
               '-executeMethod', 'NPLAudio.SpatialFabric.Example.Editor.BuildStandaloneSmoke.Export',
               '-spatialFabricBuildDirectory', str(player), '-logFile', str(folder/'build.log')]
    if args.il2cpp:
        command.append('-spatialFabricIl2Cpp')
    subprocess.run(command, check=True, timeout=1800)
    libraries = list(player.glob('**/SpatialFabricAudio.dll'))
    if len(libraries) != 1:
        raise RuntimeError('Expected exactly one deployed native DLL: ' + str(libraries))
    native_hash = hashlib.sha256(libraries[0].read_bytes()).hexdigest()
    if args.native_dll and native_hash != hashlib.sha256(args.native_dll.read_bytes()).hexdigest():
        raise RuntimeError('Deployed native DLL does not match the requested current DLL')
    report = folder/'player-smoke.json'
    subprocess.run([str(player/'SpatialFabricSmoke.exe'), '-batchmode', '-nographics',
                    '-spatialFabricSmoke', str(report), '-logFile', str(folder/'player.log')], check=True, timeout=60)
    data = check_report(json.loads(report.read_text(encoding='utf-8')))
    result = {'status': 'passed', 'project': str(project), 'backend': 'IL2CPP' if args.il2cpp else 'Mono',
              'smoke': data, 'native_dll': str(libraries[0]),
              'native_sha256': native_hash,
              'scope': 'Actual Windows player; offline PCM and invalid-device only, no physical sound/microphone certification.'}
    (folder/'validation.json').write_text(json.dumps(result, indent=2)+'\n', encoding='utf-8')
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()
