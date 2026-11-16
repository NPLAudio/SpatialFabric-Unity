"""Deterministic local UPM + complete source, with a real importable room sample.
Does not publish, commit, include credentials or imply macOS/hardware certification.
"""
import argparse,gzip,hashlib,io,json,tarfile,zipfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
PACKAGE=ROOT/'Packages/com.nplaudio.spatialfabric'
EXCLUDED={'.git','.pi','library','temp','logs','usersettings','obj','bin','dist','__pycache__','build','builds','.vs','.idea'}
def files(base,native=False):
    for p in sorted(base.rglob('*')):
        if not p.is_file():continue
        rel=p.relative_to(base)
        if any(part.lower() in EXCLUDED or part.startswith('__SpatialFabric') for part in rel.parts):continue
        if not native and (p.name.endswith(('.dll','.dll.meta','.dylib','.dylib.meta','.so','.so.meta'))):continue
        if p.suffix.lower() in {'.pyc','.pdb','.sln','.user'}:continue
        yield rel.as_posix(),p.read_bytes()
def sample():
    source=ROOT/'ExampleProject/Assets/SpatialFabricShowcase';result={}
    names=['ShowcaseRooms.cs','ShowcaseVisitor.cs','RoomMotion.cs','RoomSign.cs','RoomText.shader','ShowcaseRoomHud.cs']
    for name in names:
        for suffix in ('','.meta'):
            p=source/(name+suffix);result['Samples~/FourRoomShowcase/'+name+suffix]=p.read_bytes()
    for directory in ['GeneratedRooms','Scenes']:
        meta=source/(directory+'.meta')
        result['Samples~/FourRoomShowcase/'+directory+'.meta']=meta.read_bytes()
        for rel,data in files(source/directory):
            if directory=='Scenes' and not rel.startswith('RoomShowcase.unity'):continue
            if rel.startswith('.'):continue
            result['Samples~/FourRoomShowcase/'+directory+'/'+rel]=data
    result['Samples~/FourRoomShowcase/README.md']=b'# Four-room showcase\nOpen Scenes/RoomShowcase.unity and press Play. WASD/mouse, Esc cursor, E play room cue, Q stop, X stop all. No automatic audio or networking. Windows native output is included only in a Windows-built package; macOS requires a matching local native build.\n'
    result['Samples~/FourRoomShowcase/ASSET_LICENSES.md']=(ROOT/'ExampleProject/ASSET_LICENSES.md').read_bytes()
    return result
def main():
    p=argparse.ArgumentParser();p.add_argument('--output',type=Path,default=ROOT/'dist');p.add_argument('--include-built-native',action='store_true');args=p.parse_args()
    descriptor=json.loads((PACKAGE/'package.json').read_text());version=descriptor['version'];folder=args.output/version;folder.mkdir(parents=True,exist_ok=True)
    if args.include_built_native and not (PACKAGE/'Runtime/Plugins/x86_64/SpatialFabricAudio.dll').exists():raise SystemExit('Build Windows native DLL first')
    entries=dict(files(PACKAGE,args.include_built_native));entries.update(sample());descriptor['samples']=[{'displayName':'Four-room walking showcase','description':'Original hallway/rooms, prepared sounds, walking and real explicit controls. No autoplay.','path':'Samples~/FourRoomShowcase'}]
    entries['package.json']=(json.dumps(descriptor,indent=2)+'\n').encode()
    target=folder/f'com.nplaudio.spatialfabric-{version}.tgz'
    with target.open('wb') as output,gzip.GzipFile(fileobj=output,mode='wb',mtime=0,filename='') as zipped,tarfile.open(fileobj=zipped,mode='w') as tar:
        for name,data in sorted(entries.items()):
            info=tarfile.TarInfo('package/'+name);info.size=len(data);info.mtime=0;info.mode=0o644;tar.addfile(info,io.BytesIO(data))
    source=folder/f'SpatialFabric-Unity-{version}-CompleteSource.zip'
    allowed={'Packages','Native','Tools','Docs','ExampleProject','.github','README.md','LICENSE.md','VALIDATION.md','.gitignore','.gitattributes'}
    with zipfile.ZipFile(source,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as archive:
        for name,data in files(ROOT,args.include_built_native):
            if Path(name).parts[0] not in allowed:continue
            info=zipfile.ZipInfo('SpatialFabric-Unity/'+name,(2026,1,1,0,0,0));info.compress_type=zipfile.ZIP_DEFLATED;info.external_attr=0o100644<<16;archive.writestr(info,data)
    artifacts=[{'name':f.name,'bytes':f.stat().st_size,'sha256':hashlib.sha256(f.read_bytes()).hexdigest()} for f in (target,source)]
    manifest={'version':version,'native_windows_included':args.include_built_native,'macos':'Core Audio source present, not compiled/validated on this Windows host','physical_audio':'not certified','source_state':'local uncommitted development snapshot','artifacts':artifacts}
    (folder/'RELEASE_MANIFEST.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8',newline='\n')
    checks=[*artifacts,{'name':'RELEASE_MANIFEST.json','sha256':hashlib.sha256((folder/'RELEASE_MANIFEST.json').read_bytes()).hexdigest()}]
    (folder/'SHA256SUMS.txt').write_text(''.join(f"{item['sha256']}  {item['name']}\n" for item in checks),encoding='utf-8',newline='\n');print(json.dumps(manifest,indent=2))
if __name__=='__main__':main()
