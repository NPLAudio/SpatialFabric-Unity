"""Run real Unity tests; nonempty passing NUnit XML is mandatory."""
import argparse,json,re,shutil,subprocess,tempfile,uuid,xml.etree.ElementTree as ET
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def prepare_isolated_project(source, package, target):
    """Copy only authored project inputs; never reuse working Library/Temp.

    Unity's PlayMode runner can additionally write TestResults.xml beneath
    Application.persistentDataPath despite -testResults. Use a unique validation
    company/product identity so that cannot overwrite the example's user data.
    """
    source, package, target = Path(source).resolve(), Path(package).resolve(), Path(target).resolve()
    if any(target == root or root in target.parents for root in (source, package)):
        raise RuntimeError('Isolated project must be outside source project and package')
    target.mkdir()
    for name in ('Assets','ProjectSettings','Packages'):
        shutil.copytree(source/name,target/name)
    destination=target/'Packages/com.nplaudio.spatialfabric'
    if destination.exists():raise RuntimeError('Isolated destination already embeds the package; refusing to replace it')
    shutil.copytree(package,destination)
    manifest=target/'Packages/manifest.json';data=json.loads(manifest.read_text(encoding='utf-8-sig'))
    data['dependencies']['com.nplaudio.spatialfabric']='file:com.nplaudio.spatialfabric'
    manifest.write_text(json.dumps(data,indent=2)+'\n',encoding='utf-8')
    (target/'Packages/packages-lock.json').unlink(missing_ok=True)
    settings=target/'ProjectSettings/ProjectSettings.asset';text=settings.read_text(encoding='utf-8')
    for field,value in (('companyName','NPLAudioValidation'),('productName','SpatialFabricTests_'+uuid.uuid4().hex)):
        text,count=re.subn(r'(?m)^(  '+field+r':).*$',lambda match:match[1]+' '+value,text)
        if count!=1:raise RuntimeError('Unexpected Unity project identity field: '+field)
    settings.write_text(text,encoding='utf-8')
    audio=target/'ProjectSettings/AudioManager.asset';text=audio.read_text(encoding='utf-8')
    text,count=re.subn(r'(?m)^  m_DisableAudio:.*$','  m_DisableAudio: 1',text)
    if count!=1:raise RuntimeError('Unexpected Unity audio configuration; refusing to guess')
    audio.write_text(text,encoding='utf-8')

GRAPHICS_SKIPS={
    'NPLAudio.SpatialFabric.Tests.CompletionTests.EveryOperatorPanelRendersWithoutStartupIo':'Panel repaint requires graphics',
    'NPLAudio.SpatialFabric.Tests.RoutingAndProtocolTests.WindowRadarRepaintNeverStartsNetworkingOrPreparesAudio':'Radar UI repaint test requires --graphics',
    'NPLAudio.SpatialFabric.Tests.NetworkLifecycleTests.EditorPauseButtonStillDispatchesQueries':'Editor Pause UI test requires graphics-enabled batch validation (--graphics)',
}
NATIVE_SKIP_REASONS={
    'Build/install Windows native plugin to run silent native integration tests',
    'Build/install the Windows native plugin (with live input) to run silent live-input tests',
    'Matching native plugin required for invalid-device regression',
}

def check_results(root,graphics=False,require_native=False):
    result=root.attrib;cases=list(root.iter('test-case'))
    passed=sum(case.get('result')=='Passed' for case in cases)
    skipped=sum(case.get('result')=='Skipped' for case in cases)
    if (result.get('result') not in ('Passed','Skipped','Skipped:Ignored') or passed<1
            or int(result.get('failed','0'))!=0 or int(result.get('inconclusive','0'))!=0
            or len(cases)!=int(result.get('total','0')) or passed!=int(result.get('passed','0'))
            or skipped!=int(result.get('skipped','0')) or passed+skipped!=len(cases)):
        raise RuntimeError('Invalid/failed/incomplete NUnit evidence: '+str(result))
    for case in cases:
        if case.get('result')!='Skipped':continue
        reason=case.findtext('reason/message','').strip();name=case.get('fullname','')
        graphics_skip=not graphics and GRAPHICS_SKIPS.get(name)==reason
        native_skip=(not require_native and reason in NATIVE_SKIP_REASONS
                     and case.get('classname','').endswith(('.NativePlaybackTests','.LiveInputTests','.AcknowledgementTests')))
        if not (graphics_skip or native_skip):raise RuntimeError('Unexpected skipped test: '+name+': '+reason)
    return result

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--unity',required=True,type=Path);parser.add_argument('--project',type=Path,default=ROOT/'ExampleProject');parser.add_argument('--output',type=Path);parser.add_argument('--isolated',action='store_true',help='Copy authored inputs/current package to evidence directory, disable engine audio, use unique validation persistent-data identity');parser.add_argument('--generate-example',action='store_true');parser.add_argument('--graphics',action='store_true',help='Enable graphics for Editor Pause/UI lifecycle tests');parser.add_argument('--require-native',action='store_true',help='Require all native integration tests to run and pass, not skip');args=parser.parse_args()
    folder=(args.output or Path(tempfile.mkdtemp(prefix='SpatialFabricUnityValidation-'))).resolve()
    source=args.project.resolve()
    if args.isolated and any(folder==root or root in folder.parents for root in (ROOT,source)):
        raise RuntimeError('Isolated validation output must be outside source repository and project')
    folder.mkdir(parents=True,exist_ok=True)
    if args.isolated:
        args.project=folder/'Project'
        prepare_isolated_project(source,ROOT/'Packages/com.nplaudio.spatialfabric',args.project)
    base=[str(args.unity.resolve()),'-batchmode','-projectPath',str(args.project.resolve())]
    if not args.graphics:base.insert(2,'-nographics')
    print('Evidence:',folder,flush=True);checks=[]
    if args.generate_example and not (args.project/'Assets/SpatialFabricShowcase/Scenes/SpatialExample.unity').exists():
        subprocess.run(base+['-quit','-executeMethod','NPLAudio.SpatialFabric.Example.Editor.BuildShowcase.BuildBatch','-logFile',str(folder/'generate.log')],check=True,timeout=1800)
        if not (args.project/'Assets/SpatialFabricShowcase/Scenes/SpatialExample.unity').exists():raise RuntimeError('Generator did not create scene')
    for mode in ('EditMode','PlayMode'):
        xml=folder/(mode+'.xml');xml.unlink(missing_ok=True)
        subprocess.run(base+['-runTests','-testPlatform',mode,'-testResults',str(xml),'-logFile',str(folder/(mode+'.log'))],check=True,timeout=1800)
        result=check_results(ET.parse(xml).getroot(),args.graphics,args.require_native)
        if mode=='EditMode' and args.require_native:
            native=[case for case in ET.parse(xml).getroot().iter('test-case') if case.get('classname','').endswith('.NativePlaybackTests')]
            if len(native)<5 or any(case.get('result')!='Passed' for case in native):raise RuntimeError('Native integration tests were missing/skipped/failed; build and install the plugin first')
            live=[case for case in ET.parse(xml).getroot().iter('test-case') if case.get('classname','').endswith('.LiveInputTests')]
            if len(live)<4 or any(case.get('result')!='Passed' for case in live):raise RuntimeError('Live input tests were missing/skipped/failed; rebuild and install the plugin')
        checks.append({'platform':mode,'total':int(result['total']),'passed':int(result['passed']),'skipped':int(result.get('skipped','0')),'xml':str(xml)})
        print(mode,result['passed'],'passed',flush=True)
    report={'status':'passed','project':str(args.project.resolve()),'unity':str(args.unity.resolve()),'checks':checks,'scope':'Spatial/query plus silent native PCM/ABI/invalid-device and explicit non-playing device-inventory checks. No physical sound/fidelity/latency certification.'}
    (folder/'validation.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2))
if __name__=='__main__':main()
