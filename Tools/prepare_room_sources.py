"""Snapshot original MIT room geometry and WAVs, without changing the Unreal repo."""
import argparse,hashlib,importlib.util,json,shutil
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def main():
    p=argparse.ArgumentParser();p.add_argument('--unreal',type=Path,default=ROOT.parent/'SpatialSandbox/Plugins/SpatialFabric');p.add_argument('--check',action='store_true');args=p.parse_args()
    source=args.unreal.resolve();target=ROOT/'ExampleProject/SourceAssets';target.mkdir(parents=True,exist_ok=True)
    spec=importlib.util.spec_from_file_location('sf_room_plan',source/'Tools/Unreal/room_layout.py');layout=importlib.util.module_from_spec(spec);spec.loader.exec_module(layout)
    data={'format':'SpatialFabric.Unity.RoomPlan/1','boxes':[{'name':b['name'],'position':b['location'],'size':b['size'],'material':b['material'],'collision':b['collision']} for b in layout.geometry()],
          'rooms':[{'name':r['name'],'bounds':r['bounds'],'height':r['height'],'doorWidth':r['door_width'],'centerRight':r['center_y']} for r in layout.ROOMS],
          'spawn':layout.PLAYER_START,'yaw':layout.PLAYER_YAW,
          'floorSamples':[{'name':n,'point':v} for n,v in layout.floor_samples()],
          'doors':[{'name':n,'start':a,'end':b} for n,a,b in layout.doorway_segments()],
          'walls':[{'name':n,'start':a,'end':b} for n,a,b in layout.wall_segments()]}
    payload=json.dumps(data,indent=2)+'\n';plan=target/'RoomLayout.json'
    if args.check:assert plan.read_text()==payload,'Room layout differs'
    else:plan.write_text(payload,encoding='utf-8',newline='\n')
    hashes={}
    for name in ['SW_SF_MonoCue.wav','SW_SF_StereoLoop.wav','SW_SF_ReplayCue.wav']:
        path=source/'Examples/SpatialFabricShowcase/SourceAssets/SpatialFabricDemo/Audio'/name;dest=target/'Audio'/name;raw=path.read_bytes();hashes[name]=hashlib.sha256(raw).hexdigest()
        if args.check:assert dest.read_bytes()==raw,name
        else:
            dest.parent.mkdir(parents=True,exist_ok=True)
            if dest.exists() and dest.read_bytes()!=raw:raise RuntimeError('Refusing to replace modified source '+str(dest))
            shutil.copy2(path,dest)
    hashes['RoomLayout.json']=hashlib.sha256(payload.encode()).hexdigest()
    manifest=json.dumps({'source':'NPLAudio/SpatialFabric v2.1.0 original room plan and procedural WAVs','license':'MIT for original content; no Unreal/Epic packages copied','units':'layout front/right/up centimeters; builder maps to Unity Z/X/Y meters','sha256':hashes},indent=2)+'\n'
    if args.check:assert (target/'PROVENANCE.json').read_text()==manifest
    else:(target/'PROVENANCE.json').write_text(manifest,encoding='utf-8',newline='\n')
    print('Room plan and three original WAVs verified' if args.check else 'Prepared original room plan and three WAV sources')
if __name__=='__main__':main()
