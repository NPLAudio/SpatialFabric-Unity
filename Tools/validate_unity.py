"""Run real Unity tests; nonempty passing NUnit XML is mandatory."""
import argparse,json,subprocess,tempfile,xml.etree.ElementTree as ET
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def main():
    parser=argparse.ArgumentParser();parser.add_argument('--unity',required=True,type=Path);parser.add_argument('--project',type=Path,default=ROOT/'ExampleProject');parser.add_argument('--output',type=Path);parser.add_argument('--generate-example',action='store_true');parser.add_argument('--graphics',action='store_true',help='Enable graphics for Editor Pause/UI lifecycle tests');parser.add_argument('--require-native',action='store_true',help='Require all native integration tests to run and pass, not skip');args=parser.parse_args()
    folder=(args.output or Path(tempfile.mkdtemp(prefix='SpatialFabricUnityValidation-'))).resolve();folder.mkdir(parents=True,exist_ok=True)
    base=[str(args.unity.resolve()),'-batchmode','-projectPath',str(args.project.resolve())]
    if not args.graphics:base.insert(2,'-nographics')
    print('Evidence:',folder,flush=True);checks=[]
    if args.generate_example and not (args.project/'Assets/SpatialFabricShowcase/Scenes/SpatialExample.unity').exists():
        subprocess.run(base+['-quit','-executeMethod','NPLAudio.SpatialFabric.Example.Editor.BuildShowcase.BuildBatch','-logFile',str(folder/'generate.log')],check=True,timeout=1800)
        if not (args.project/'Assets/SpatialFabricShowcase/Scenes/SpatialExample.unity').exists():raise RuntimeError('Generator did not create scene')
    for mode in ('EditMode','PlayMode'):
        xml=folder/(mode+'.xml');xml.unlink(missing_ok=True)
        subprocess.run(base+['-runTests','-testPlatform',mode,'-testResults',str(xml),'-logFile',str(folder/(mode+'.log'))],check=True,timeout=1800)
        result=ET.parse(xml).getroot().attrib
        if result.get('result')!='Passed' or int(result.get('total','0'))<1 or int(result.get('failed','0'))!=0:raise RuntimeError(f'{mode}: {result}')
        if mode=='EditMode' and args.require_native:
            native=[case for case in ET.parse(xml).getroot().iter('test-case') if case.get('classname','').endswith('.NativePlaybackTests')]
            if len(native)<5 or any(case.get('result')!='Passed' for case in native):raise RuntimeError('Native integration tests were missing/skipped/failed; build and install the plugin first')
        checks.append({'platform':mode,'total':int(result['total']),'passed':int(result['passed']),'xml':str(xml)})
        print(mode,result['passed'],'passed',flush=True)
    report={'status':'passed','project':str(args.project.resolve()),'unity':str(args.unity.resolve()),'checks':checks,'scope':'Spatial/query plus silent native PCM/ABI/invalid-device and explicit non-playing device-inventory checks. No physical sound/fidelity/latency certification.'}
    (folder/'validation.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2))
if __name__=='__main__':main()
