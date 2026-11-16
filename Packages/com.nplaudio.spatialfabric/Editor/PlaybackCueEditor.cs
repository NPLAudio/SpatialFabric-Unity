// Explicit authoring/playback actions; never invoked from scene discovery. MIT.
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using NPLAudio.SpatialFabric.Audio;
namespace NPLAudio.SpatialFabric.Editor
{
    [CustomEditor(typeof(PlaybackCue)),CanEditMultipleObjects]
    public sealed class PlaybackCueEditor : UnityEditor.Editor
    {
        [MenuItem("Tools/SpatialFabric/Import WAV as prepared PCM")]
        public static void ImportWav()
        {
            string source=EditorUtility.OpenFilePanel("Import original WAV as prepared PCM","","wav");if(string.IsNullOrEmpty(source))return;
            string destination=EditorUtility.SaveFilePanelInProject("Save prepared PCM","PreparedPCM","asset","Choose a new asset path");if(string.IsNullOrEmpty(destination))return;
            PreparedAudioAsset asset=null;
            try
            {
                if(new FileInfo(source).Length>512L*1024*1024)throw new FormatException("WAV byte size exceeds 512 MiB limit");
                if(File.Exists(destination))throw new InvalidOperationException("Choose a new asset path; existing assets are not overwritten");
                var pcm=WavDecoder.Decode(File.ReadAllBytes(source));asset=CreateInstance<PreparedAudioAsset>();asset.SetPcm(pcm,Path.GetFileName(source));
                AssetDatabase.CreateAsset(asset,destination);AssetDatabase.SaveAssets();Selection.activeObject=asset;EditorGUIUtility.PingObject(asset);
            }
            catch(Exception e) {if(asset!=null && !AssetDatabase.Contains(asset))DestroyImmediate(asset);EditorUtility.DisplayDialog("WAV import failed",e.Message,"OK");}
        }
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if(targets.Length>1)
            {
                EditorGUILayout.HelpBox("Editing common serialized fields uses Unity multi-object Undo. Prepare/Play/Stop below are explicit actions on all selected cues.",MessageType.Info);
                if(GUILayout.Button("Refresh devices")){NativeAudio.Probe();NativeAudio.RefreshDevices();}
                foreach(var device in NativeAudio.CachedDevices)if(GUILayout.Button("Use "+device.name+" for selected")){Undo.RecordObjects(targets,"Set selected output devices");foreach(PlaybackCue c in targets){c.configuredDeviceId=device.id;PrefabUtility.RecordPrefabInstancePropertyModifications(c);EditorUtility.SetDirty(c);}}
                if(GUILayout.Button("Prepare selected PCM"))foreach(PlaybackCue c in targets)c.Prepare();
                if(GUILayout.Button("Play selected physical outputs") && EditorUtility.DisplayDialog("Play selected cues?",targets.Length+" cues may use real output devices.","Play","Cancel"))foreach(PlaybackCue c in targets)c.TryPlay();
                if(GUILayout.Button("Stop selected + cancel replay"))foreach(PlaybackCue c in targets)c.Stop();
                if(GUILayout.Button("Release selected PCM"))foreach(PlaybackCue c in targets)c.ReleaseAudio();return;
            }
            var cue=(PlaybackCue)target;
            EditorGUILayout.Space();EditorGUILayout.LabelField("Direct output — native WASAPI",EditorStyles.boldLabel);
            if(!cue.BackendAvailable)EditorGUILayout.HelpBox(NativeAudio.AvailabilityError,MessageType.Warning);
            if(GUILayout.Button("Probe backend / refresh output devices (explicit)")) {NativeAudio.Probe();NativeAudio.RefreshDevices();}
            var devices=NativeAudio.CachedDevices;var labels=new System.Collections.Generic.List<string>{"System default output (channel count resolved at start)"};
            var ids=new System.Collections.Generic.List<string>{""};int selected=0;
            for(int i=0;i<devices.Length;++i) {labels.Add($"{devices[i].name} — {devices[i].channels} ch / {devices[i].sample_rate} Hz");ids.Add(devices[i].id);if(devices[i].id==cue.configuredDeviceId)selected=i+1;}
            bool missing=!string.IsNullOrEmpty(cue.configuredDeviceId) && selected==0;
            if(missing) {selected=labels.Count;labels.Add("Configured device not in cached list (ID retained)");ids.Add(cue.configuredDeviceId);}
            using(var change=new EditorGUI.ChangeCheckScope())
            {
                int next=EditorGUILayout.Popup("Select configured device",selected,labels.ToArray());
                if(change.changed) {Undo.RecordObject(cue,"Set direct output device");cue.configuredDeviceId=ids[next];EditorUtility.SetDirty(cue);}
            }
            if(missing)EditorGUILayout.HelpBox("Configured device is not in the cached list. Its ID is retained; playback will not fall back to a different device.",MessageType.Warning);
            if(!string.IsNullOrEmpty(NativeAudio.DeviceEnumerationError))EditorGUILayout.HelpBox(NativeAudio.DeviceEnumerationError,MessageType.None);
            EditorGUILayout.LabelField("Worker",$"{cue.WorkerState} / {cue.EndReason}");
            EditorGUILayout.LabelField("Actual output",$"{cue.ActualOutputChannels} ch / {cue.ActualOutputRate} Hz / {cue.ActualBufferFrames} frames / exclusive {cue.ExclusiveActive}");
            EditorGUILayout.LabelField("Prepared / playing / replay pending",$"{cue.IsPrepared} / {cue.IsPlaying} / {cue.ReplayPending}");
            if(!string.IsNullOrEmpty(cue.LastError))EditorGUILayout.HelpBox(cue.LastError,MessageType.Warning);
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("Prepare PCM"))cue.Prepare();
                if(GUILayout.Button("Play direct output"))cue.TryPlay();
                if(GUILayout.Button("Stop + cancel replay"))cue.Stop();
                if(GUILayout.Button("Release PCM"))cue.ReleaseAudio();
            }
            EditorGUILayout.HelpBox("Play uses the real configured output device. Channel 0 maps corresponding source/device channels; 1..N routes the first source channel to that output. Loop/replay are unscaled. Requested exclusive/buffer settings are not hardware guarantees. OSC/visual room walls do not spatialize this PCM.",MessageType.Info);
        }
        public override bool RequiresConstantRepaint()=>true;
    }
}
