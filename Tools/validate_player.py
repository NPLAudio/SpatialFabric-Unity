"""Build/run actual Windows x64 player, silently testing deployment/native ABI/PCM/fail-closed device behavior."""
import argparse,json,subprocess,tempfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def main():
    p=argparse.ArgumentParser();p.add_argument('--unity',required=True,type=Path);p.add_argument('--output',type=Path);p.add_argument('--il2cpp',action='store_true');args=p.parse_args()
    folder=(args.output or Path(tempfile.mkdtemp(prefix='SpatialFabricUnityPlayer-'))).resolve();folder.mkdir(parents=True,exist_ok=True)
    print('Player evidence:',folder,flush=True);player=folder/'Player'
    command=[str(args.unity),'-batchmode','-nographics','-quit','-projectPath',str(ROOT/'ExampleProject'),'-executeMethod','NPLAudio.SpatialFabric.Example.Editor.BuildStandaloneSmoke.Export','-spatialFabricBuildDirectory',str(player),'-logFile',str(folder/'build.log')]
    if args.il2cpp:command.append('-spatialFabricIl2Cpp')
    subprocess.run(command,check=True,timeout=1800)
    libraries=list(player.glob('**/SpatialFabricAudio.dll'))
    if len(libraries)!=1:raise RuntimeError('Expected exactly one deployed native audio library: '+str(libraries))
    report=folder/'player-smoke.json';report.unlink(missing_ok=True)
    subprocess.run([str(player/'SpatialFabricSmoke.exe'),'-batchmode','-nographics','-spatialFabricSmoke',str(report),'-logFile',str(folder/'player.log')],check=True,timeout=60)
    data=json.loads(report.read_text());assert data['status']=='passed' and data['invalid_device_failed'] and data['no_playing'],data
    print(json.dumps(data,indent=2));print('Deployed native library:',libraries[0])
if __name__=='__main__':main()
