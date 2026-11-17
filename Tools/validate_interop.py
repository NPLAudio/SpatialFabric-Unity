"""Silent real Unity ↔ shared-hub Python/Node client compatibility probe.
Requires the generated example and the standard shared ports 8100/9000/50018/9002 free (close any Unreal/Godot instance that owns them).
"""
import argparse,json,select,socket,subprocess,sys,tempfile,threading,time
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def main():
    p=argparse.ArgumentParser();p.add_argument('--unity',required=True,type=Path);p.add_argument('--interop','--unreal',dest='interop',required=True,type=Path,help='SpatialFabric shared hub checkout (--unreal is a legacy alias)');p.add_argument('--output',type=Path);p.add_argument('--rooms',action='store_true',help='Query the full walking room showcase (9 records)');args=p.parse_args()
    folder=(args.output or Path(tempfile.mkdtemp(prefix='SpatialFabricUnityInterop-'))).resolve();folder.mkdir(parents=True,exist_ok=True)
    sys.path.insert(0,str(args.interop/'Apps/OSCController'))
    from osc_controller.scene_query import query_scene, SceneQueryError
    from osc_controller.protocol import decode
    listeners=[];messages=[];stop=threading.Event();process=None
    for name in ('ready.json','engine-result.json'): (folder/name).unlink(missing_ok=True)
    try:
        for port in (50018,9002):
            s=socket.socket(socket.AF_INET,socket.SOCK_DGRAM)
            if hasattr(socket,'SO_EXCLUSIVEADDRUSE'):s.setsockopt(socket.SOL_SOCKET,socket.SO_EXCLUSIVEADDRUSE,1)
            s.setsockopt(socket.SOL_SOCKET,socket.SO_RCVBUF,1024*1024);s.bind(('127.0.0.1',port));listeners.append(s)
        def receive():
            while not stop.is_set():
                for s in select.select(listeners,[],[],.05)[0]:
                    data,peer=s.recvfrom(65507)
                    if peer[0]=='127.0.0.1':
                        try:messages.extend(decode(data))
                        except ValueError:pass
        thread=threading.Thread(target=receive,daemon=True);thread.start()
        command=[str(args.unity.resolve()),'-batchmode','-nographics','-projectPath',str(ROOT/'ExampleProject'),'-executeMethod','NPLAudio.SpatialFabric.Example.Editor.LiveInteropProbe.Run','-spatialFabricEvidence',str(folder),'-logFile',str(folder/'unity.log')]
        if args.rooms:command.append('-spatialFabricRoomScene')
        process=subprocess.Popen(command,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
        deadline=time.monotonic()+180
        while not (folder/'ready.json').exists():
            if process.poll() is not None:raise RuntimeError('Unity quit before ready; see '+str(folder/'unity.log'))
            if time.monotonic()>deadline:raise RuntimeError('Unity startup timeout')
            time.sleep(.1)
        results=[]
        def wait_until_responsive(port):
            # The editor starts answering a few seconds after ready.json (asset import/domain reload), so retry rather than assume.
            end=time.monotonic()+60
            while True:
                try:return query_scene('127.0.0.1',port,page_size=3)
                except SceneQueryError:
                    if time.monotonic()>end:raise
        def verify(scene):
            expected=9 if args.rooms else 7
            assert scene['schema_version']==1 and scene['engine']=='Unity' and scene['returned']==scene['total']==expected,scene
            assert len({o['key'] for o in scene['objects']})==expected
            assert len([o for o in scene['objects'] if o['kind']=='spatial_binding'])==4
            cue=next(o for o in scene['objects'] if o['kind']=='playback')
            assert not cue['playing'] and not cue['pcm_prepared']
        for port in (8100,9000):
            time.sleep(.35);scene=wait_until_responsive(port);verify(scene)
            (folder/f'python-{port}.json').write_text(json.dumps(scene,indent=2))
            results.append({'client':'existing Python','port':port,'records':scene['total'],'pages':len(scene['page_snapshot_times'])})
            time.sleep(.35)
            output=subprocess.check_output(['node',str(args.interop/'Examples/OSCSceneQuery/scene_query.js'),'127.0.0.1',str(port),'3'],text=True,timeout=12)
            scene=json.loads(output);verify(scene);(folder/f'node-{port}.json').write_text(output)
            results.append({'client':'existing Node','port':port,'records':scene['total'],'pages':len(scene['page_snapshot_times'])})
        if process.wait(timeout=120)!=0:raise RuntimeError('Unity probe exit failed')
        engine=json.loads((folder/'engine-result.json').read_text());assert engine['status']=='passed' and engine['configuration_unchanged'] and not engine['any_playing'],engine
        stop.set();thread.join(2)
        adm={m.address.split('/')[3] for m in messages if m.address.startswith('/adm/obj/') and m.address.endswith('/xyz')}
        custom={m.address.split('/')[2] for m in messages if m.address.startswith('/unity/') and m.address.endswith('/xyz')}
        assert adm==custom=={'1','2','3','4'},(adm,custom)
        report={'status':'passed','queries':results,'captured_messages':len(messages),'adm_ids':sorted(adm),'custom_ids':sorted(custom),'engine':engine,'scope':'No transport/audio commands. Software interop only, not hardware certification.'}
        (folder/'interop.json').write_text(json.dumps(report,indent=2)+'\n');print('Evidence:',folder);print(json.dumps(report,indent=2))
    finally:
        stop.set()
        if process is not None and process.poll() is None:process.terminate();process.wait(timeout=15)
        for s in listeners:s.close()
if __name__=='__main__':main()
