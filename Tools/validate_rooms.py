"""Generate (no overwrite), silently audit, build and walk/render the actual room player."""
import argparse,json,subprocess,tempfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def main():
    p=argparse.ArgumentParser();p.add_argument('--unity',required=True,type=Path);p.add_argument('--output',type=Path);p.add_argument('--skip-player',action='store_true');args=p.parse_args()
    folder=(args.output or Path(tempfile.mkdtemp(prefix='SpatialFabricUnityRooms-'))).resolve();folder.mkdir(parents=True,exist_ok=True)
    base=[str(args.unity),'-batchmode','-nographics','-quit','-projectPath',str(ROOT/'ExampleProject')]
    scene=ROOT/'ExampleProject/Assets/SpatialFabricShowcase/Scenes/RoomShowcase.unity'
    print('Room evidence:',folder,flush=True)
    if not scene.exists():subprocess.run(base+['-executeMethod','NPLAudio.SpatialFabric.Example.Editor.BuildRoomShowcase.BuildBatch','-logFile',str(folder/'generate.log')],check=True,timeout=600)
    subprocess.run(base+['-executeMethod','NPLAudio.SpatialFabric.Example.Editor.ValidateRoomShowcase.Run','-spatialFabricEvidence',str(folder),'-logFile',str(folder/'editor.log')],check=True,timeout=300)
    report=json.loads((folder/'room-editor.json').read_text());assert report['status']=='passed' and not report['failed'],report
    print('Editor geometry/default checks:',len(report['passed']),flush=True)
    if not args.skip_player:
        subprocess.run(['python',str(ROOT/'Tools/validate_player.py'),'--unity',str(args.unity),'--output',str(folder/'Player')],check=True,timeout=1800)
        exe=folder/'Player/Player/SpatialFabricSmoke.exe';result=folder/'room-player.json'
        subprocess.run([str(exe),'-batchmode','-screen-width','1280','-screen-height','720','-spatialFabricRoomsSmoke',str(result),'-spatialFabricRoomShots',str(folder/'Screenshots'),'-logFile',str(folder/'room-player.log')],check=True,timeout=90)
        data=json.loads(result.read_text());assert data['status']=='passed' and not data['failed'],data
        assert len(list((folder/'Screenshots').glob('*.png')))==5
        print('Actual player room checks:',len(data['passed']),flush=True)
    (folder/'rooms-summary.json').write_text(json.dumps({'status':'passed','scene':str(scene),'evidence':str(folder),'scope':'No audible playback or valid-device stream requested. Geometry, walking/collision, prepared PCM offline, animation and rendered views.'},indent=2)+'\n')
if __name__=='__main__':main()
