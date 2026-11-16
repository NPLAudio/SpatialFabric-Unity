"""Install a built UPM tarball + its room sample into a CLEAN disposable Unity project.
Build/run its actual native player; no checkout-relative assets/code/dependency discovery.
"""
import argparse,json,subprocess,tarfile,tempfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def main():
    p=argparse.ArgumentParser();p.add_argument('--unity',required=True,type=Path);p.add_argument('--package',required=True,type=Path);p.add_argument('--output',type=Path);args=p.parse_args()
    folder=(args.output or Path(tempfile.mkdtemp(prefix='SpatialFabricUPMConsumer-'))).resolve();project=folder/'Project';project.mkdir(parents=True,exist_ok=True)
    if any(project.iterdir()):raise SystemExit('Consumer project must be empty; never overwrite a user project')
    (project/'Packages').mkdir();(project/'ProjectSettings').mkdir();(project/'Assets').mkdir()
    deps={'com.nplaudio.spatialfabric':'file:'+args.package.resolve().as_posix(),'com.unity.modules.imageconversion':'1.0.0'}
    (project/'Packages/manifest.json').write_text(json.dumps({'dependencies':deps},indent=2))
    (project/'ProjectSettings/ProjectVersion.txt').write_bytes((ROOT/'ExampleProject/ProjectSettings/ProjectVersion.txt').read_bytes())
    with tarfile.open(args.package) as tar:
        prefix='package/Samples~/FourRoomShowcase/'
        for entry in tar:
            if not entry.isfile() or not entry.name.startswith(prefix):continue
            name=Path(entry.name[len(prefix):]);assert '..' not in name.parts and not name.is_absolute()
            path=project/'Assets/FourRoomShowcase'/name;path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(tar.extractfile(entry).read())
    # Test-only smoke files, not required by the distributable sample itself.
    for name in ['RoomValidation.cs','RoomShowcaseSmoke.cs']:
        dest=project/'Assets/Validation'/name;dest.parent.mkdir(exist_ok=True);dest.write_bytes((ROOT/'ExampleProject/Assets/SpatialFabricShowcase'/name).read_bytes())
    editor=project/'Assets/Editor/ValidateConsumer.cs';editor.parent.mkdir()
    editor.write_text('''using System;using System.IO;using UnityEditor;using UnityEditor.SceneManagement;using UnityEditor.Build.Reporting;using UnityEngine;using NPLAudio.SpatialFabric;using NPLAudio.SpatialFabric.Example;using NPLAudio.SpatialFabric.Audio;
public static class ValidateConsumer {public static void Run(){
 string[] a=Environment.GetCommandLineArgs();string folder=a[Array.IndexOf(a,"-spatialFabricEvidence")+1];
 string scene="Assets/FourRoomShowcase/Scenes/RoomShowcase.unity";EditorSceneManager.OpenScene(scene);var rooms=UnityObjects.FindAll<ShowcaseRooms>()[0];var r=RoomValidation.Validate(rooms,true);
 if(r.failed.Count>0)throw new Exception(string.Join(";",r.failed));if(!NativeAudio.IsAvailable)throw new Exception(NativeAudio.AvailabilityError);
 File.WriteAllText(Path.Combine(folder,"consumer-editor.json"),JsonUtility.ToJson(r,true));
 var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{scene},locationPathName=Path.Combine(folder,"Player/RoomConsumer.exe"),target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development|BuildOptions.StrictMode});
 if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Clean consumer build failed");}}
''')
    print('Clean UPM consumer evidence:',folder,flush=True)
    subprocess.run([str(args.unity),'-batchmode','-nographics','-quit','-projectPath',str(project),'-executeMethod','ValidateConsumer.Run','-spatialFabricEvidence',str(folder),'-logFile',str(folder/'editor.log')],check=True,timeout=1800)
    result=folder/'consumer-player.json'
    subprocess.run([str(folder/'Player/RoomConsumer.exe'),'-batchmode','-nographics','-spatialFabricRoomsSmoke',str(result),'-logFile',str(folder/'player.log')],check=True,timeout=90)
    data=json.loads(result.read_text());assert data['status']=='passed' and not data['failed'],data
    print('Clean UPM native/player room checks passed:',len(data['passed']))
    (folder/'validation.json').write_text(json.dumps({'status':'passed','package':str(args.package.resolve()),'project':str(project),'checks':len(data['passed']),'scope':'Fresh UPM consumer, packaged sample/asset closure, native DLL and silent real-player geometry/PCM. No physical audio.'},indent=2)+'\n')
if __name__=='__main__':main()
