// Explicit live-input authoring. Device refresh and Start are buttons; inspecting never opens a microphone. MIT.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using NPLAudio.SpatialFabric.Audio;
namespace NPLAudio.SpatialFabric.Editor
{
    [CustomEditor(typeof(LiveInputCue))]
    public sealed class LiveInputCueEditor : UnityEditor.Editor
    {
        static string Pick(string title,string current,AudioDeviceInfo[] devices,string defaultLabel,out bool changed)
        {
            var labels=new List<string>{defaultLabel};var ids=new List<string>{""};int selected=0;
            for(int i=0;i<devices.Length;++i) {labels.Add($"{devices[i].name} — {devices[i].channels} ch / {devices[i].sample_rate} Hz");ids.Add(devices[i].id);if(devices[i].id==current)selected=i+1;}
            bool missing=!string.IsNullOrEmpty(current) && selected==0;
            if(missing) {selected=labels.Count;labels.Add("Configured device not in cached list (ID retained)");ids.Add(current);}
            int next=EditorGUILayout.Popup(title,selected,labels.ToArray());changed=next!=selected;
            if(missing)EditorGUILayout.HelpBox("Configured device is not in the cached list. Its ID is retained; there is no fallback to a different device.",MessageType.Warning);
            return ids[next];
        }
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var cue=(LiveInputCue)target;
            EditorGUILayout.Space();EditorGUILayout.LabelField("Live input → direct output",EditorStyles.boldLabel);
            if(!cue.BackendAvailable)EditorGUILayout.HelpBox(NativeLive.AvailabilityError,MessageType.Warning);
            if(GUILayout.Button("Probe backend / refresh input + output devices (explicit)")) {NativeLive.Probe();NativeLive.RefreshInputs();NativeAudio.RefreshDevices();}
            bool changed;
            string input=Pick("Input device",cue.inputDeviceId,NativeLive.CachedInputs,"System default input",out changed);
            if(changed) {Undo.RecordObject(cue,"Set live input device");cue.inputDeviceId=input;EditorUtility.SetDirty(cue);}
            string output=Pick("Output device",cue.outputDeviceId,NativeAudio.CachedDevices,"System default output",out changed);
            if(changed) {Undo.RecordObject(cue,"Set live output device");cue.outputDeviceId=output;EditorUtility.SetDirty(cue);}
            if(!string.IsNullOrEmpty(NativeLive.InputEnumerationError))EditorGUILayout.HelpBox(NativeLive.InputEnumerationError,MessageType.None);
            EditorGUILayout.LabelField("Worker",$"{cue.WorkerState} / {cue.EndReason}");
            EditorGUILayout.LabelField("Actual input",$"{cue.ActualInputChannels} ch / {cue.ActualInputRate} Hz");
            EditorGUILayout.LabelField("Actual output",$"{cue.ActualOutputChannels} ch / {cue.ActualOutputRate} Hz");
            EditorGUILayout.LabelField("Output mode / OS output latency",$"{cue.OutputMode} / "+(cue.OutputLatencyMilliseconds<0?"unavailable":cue.OutputLatencyMilliseconds.ToString("F2")+" ms (not acoustic)"));
            EditorGUILayout.LabelField("Live / queue latency / underruns / overruns",$"{cue.IsLive} / {cue.LatencyMilliseconds:0} ms / {cue.Underruns} / {cue.Overruns}");
            var meter=EditorGUILayout.GetControlRect(false,EditorGUIUtility.singleLineHeight);EditorGUI.ProgressBar(meter,Mathf.Clamp01(cue.InputPeak),"Input peak (observed)");
            if(!string.IsNullOrEmpty(cue.LastError))EditorGUILayout.HelpBox(cue.LastError,MessageType.Warning);
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("Start live") && EditorUtility.DisplayDialog("Start live input?","This opens the selected microphone/line input (the OS may ask for permission) and plays it through the real selected output. Use headphones or a low gain to avoid acoustic feedback.","Start","Cancel"))cue.TryStart();
                if(GUILayout.Button("Stop"))cue.Stop();
            }
            EditorGUILayout.HelpBox("Input channel 0 passes all input channels; 1..N selects one. Output channel 0 maps corresponding channels (a single selected input lands on output 1); 1..N routes to exactly one output. Exclusive mode applies to the output only. Live means the native worker actually started, not that Start was requested.",MessageType.Info);
        }
        public override bool RequiresConstantRepaint()=>true;
    }
}
