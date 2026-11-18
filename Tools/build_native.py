"""Build native Windows x64 plugin and run SILENT tests. No CMake install required.
Refuses to replace a plugin while this Unity project is open. No audio device test
opens a valid/default endpoint; physical playback requires a separate explicit action.
"""
import argparse,json,os,shutil,subprocess,tempfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def main():
    p=argparse.ArgumentParser();p.add_argument('--vs',type=Path,default=Path('C:/Program Files/Microsoft Visual Studio/2022/Community'));p.add_argument('--output',type=Path);p.add_argument('--install',action='store_true');args=p.parse_args()
    if os.name!='nt':raise SystemExit('Use CMake/CTest for portable native tests; Windows installer requires MSVC')
    folder=(args.output or Path(tempfile.mkdtemp(prefix='SpatialFabricNativeAudio-'))).resolve();folder.mkdir(parents=True,exist_ok=True)
    if args.install and (ROOT/'ExampleProject/Temp/UnityLockfile').exists():raise SystemExit('Close the Unity example before replacing its native DLL (or omit --install).')
    dev=args.vs/'Common7/Tools/VsDevCmd.bat';native=ROOT/'Native'
    cmd=f'call "{dev}" -arch=x64 >nul && cl /nologo /std:c++17 /EHsc /MD /W4 /O2 /LD /I"{native / "include"}" /I"{native / "src"}" "{native / "src/audio.cpp"}" /Fe:SpatialFabricAudio.dll /link ole32.lib uuid.lib && cl /nologo /std:c++17 /EHsc /MD /W4 /O2 /I"{native / "include"}" /I"{native / "src"}" "{native / "tests/audio_tests.cpp"}" SpatialFabricAudio.lib /Fe:sf_audio_tests.exe && sf_audio_tests.exe && cl /nologo /std:c++17 /EHsc /MD /W4 /O2 /I"{native / "include"}" /I"{native / "src"}" "{native / "tests/output_negotiation_tests.cpp"}" /Fe:sf_output_negotiation_tests.exe /link ole32.lib uuid.lib && sf_output_negotiation_tests.exe'
    script=folder/'build.cmd';script.write_text('@echo off\n'+cmd+'\n',encoding='utf-8')
    with (folder/'build.log').open('w') as log:result=subprocess.run(['cmd.exe','/c',str(script)],cwd=folder,stdout=log,stderr=subprocess.STDOUT,timeout=240)
    print((folder/'build.log').read_text(errors='replace'))
    if result.returncode:raise SystemExit(result.returncode)
    if args.install:
        target=ROOT/'Packages/com.nplaudio.spatialfabric/Runtime/Plugins/x86_64';target.mkdir(parents=True,exist_ok=True)
        shutil.copy2(folder/'SpatialFabricAudio.dll',target/'SpatialFabricAudio.dll')
        shutil.copy2(native/'UnityPluginImporter.meta',target/'SpatialFabricAudio.dll.meta');print('Installed',target/'SpatialFabricAudio.dll')
    report={'status':'passed','platform':'Windows x64','output':str(folder),'installed':args.install,'scope':'Silent native renderer, ABI/lifetime/cancellation, invalid-device and mocked WASAPI negotiation checks only; physical output not tested.'}
    (folder/'build-result.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2))
if __name__=='__main__':main()
