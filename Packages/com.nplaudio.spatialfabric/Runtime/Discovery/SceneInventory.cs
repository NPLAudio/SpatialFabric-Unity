// Copyright (c) 2026 SpatialFabric Contributors. MIT.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NPLAudio.SpatialFabric
{
    [Serializable] public sealed class SceneControls { public string play, stop, loop, gain; }
    [Serializable] public sealed class SceneTransform
    {
        public float[] position_cm, scale, unity_rotation_euler_deg;
    }
    [Serializable] public sealed class SceneAdapter
    {
        public string protocol, target_ip, output_key, label;
        public bool enabled;
        public int target_port, send_rate_hz, adm_coordinate_mode = 2;
        public bool send_only_on_change;
        public string custom_address_template, custom_arg_template, custom_coordinate_mode, custom_send_mode, configuration_error;
        public float[] custom_position_range, custom_gain_range, custom_width_range;
    }
    [Serializable] public sealed class SceneTarget
    {
        public string protocol;
        public int object_id, object_id_override = -1;
        public bool enabled;
        public SceneAdapter adapter;
        public Dictionary<string,string> custom_fields;
        public Protocols.CustomField[] custom_field_entries;
    }
    [Serializable] public sealed class SceneRecord
    {
        public string kind, key, actor_label, actor_path, name;
        public bool enabled;
        public SceneTransform world_transform;
        public string manager_path, label;
        public int binding_index, default_object_id;
        public bool snapshot_resolved;
        public float[] stage_normalized, stage_meters, dimensions_meters;
        public float gain, width;
        public bool mute;
        public SceneTarget[] targets;
        public bool listening;
        public string configured_listen_ip, bound_ip;
        public int configured_listen_port, bound_port;
        public bool scene_query_enabled, allow_remote_scene_queries;
        public string listener_path,listener_follow_mode,resolved_actor_path;
        public bool follow_listener, listener_resolved;
        public string osc_base, state_address, configured_device_id, sound_path, channel_policy, last_error;
        public SceneControls controls;
        public int output_channel, source_channels;
        public float duration_seconds;
        public bool registered, playing, loop, auto_replay, backend_available, pcm_prepared, replay_pending, exclusive_requested, exclusive_active;
        public int buffer_ms_requested, actual_output_channels, actual_output_rate, actual_buffer_frames;
        public string worker_state, end_reason;
        public string input_device_id;
        public int input_channel, actual_input_channels, actual_input_rate;
        public float input_peak, latency_ms;
        public ulong underruns, overruns;
    }
    [Serializable] public sealed class ScenePlaybackNetwork
    {
        public string configured_listen_ip, bound_ip, monitor_ip;
        public int configured_listen_port, bound_port, monitor_port;
        public bool listening;
    }
    [Serializable] public sealed class ScenePage
    {
        public string schema = "org.nplaudio.spatialfabric.scene";
        public int schema_version = 1;
        public string engine = "Unity";
        public string world, world_type, snapshot_utc;
        public string[] world_scenes;
        public double world_time_seconds;
        public string coordinates = "Canonical front/right/up, normalized stage half-extents in meters. ADM XYZ right/front/up, sphere-limited. world_transform.position_cm uses canonical world front/right/up centimeters; unity_rotation_euler_deg is engine-native XYZ.";
        public string state_contract = "Read-only loaded-scene snapshot. Playback is cached observed native worker state, not a request acknowledgement or physical audio confirmation. Query never prepares PCM, enumerates devices or starts playback.";
        public ScenePlaybackNetwork playback_network;
        public int offset, limit, returned, total, next_offset;
        public SceneRecord[] objects;
    }
    [Serializable] sealed class SceneFailure
    {
        public string schema = "org.nplaudio.spatialfabric.scene";
        public int schema_version = 1;
        public string error;
    }
    public static class SceneInventory
    {
        static int mainThreadId;
        public static bool IsMainThread => System.Threading.Thread.CurrentThread.ManagedThreadId == mainThreadId;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void RegisterMainThread() { mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId; }
        public static T[] InScene<T>(Scene scene) where T : Component
        {
            if (!scene.IsValid() || !scene.isLoaded) return Array.Empty<T>();
            return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
        }
        public static Scene[] WorldScenes(SpatialFabricManager manager)
        {
            var scenes=new List<Scene>{manager.gameObject.scene};for(int i=0;i<SceneManager.sceneCount;++i){var scene=SceneManager.GetSceneAt(i);if(manager.ContainsScene(scene) && !scenes.Contains(scene))scenes.Add(scene);}return scenes.ToArray();
        }
        public static T[] InWorld<T>(SpatialFabricManager manager)where T:Component=>WorldScenes(manager).SelectMany(InScene<T>).ToArray();
        public static string Path(Transform t)
        {
            string path = t.name; while (t.parent != null) { t = t.parent; path = t.name + "/" + path; } return path;
        }
        public static float[] Vec(Vector3 p) => new[] { p.x,p.y,p.z };
        static SceneRecord Row(string kind,SpatialIdentity item,string suffix = "")
        {
            Vector3 p = item.transform.position;
            return new SceneRecord {kind = kind,key = item.Key+suffix,actor_label = item.name,actor_path = Path(item.transform),name = item.GetType().Name,
                enabled = item.isActiveAndEnabled,world_transform = new SceneTransform {position_cm = new[] {p.z*100,p.x*100,p.y*100},scale = Vec(item.transform.lossyScale),unity_rotation_euler_deg = Vec(item.transform.eulerAngles)}};
        }
        static SceneTarget Target(string protocol,SpatialFabricManager manager,SpatialObject obj)
        {
            bool adm = protocol == "ADM";
            var config=manager.customProtocol;
            int requestedRate=adm?manager.admSendRateHz:config?.sendRateHz??0;
            var adapter=new SceneAdapter {protocol=protocol,enabled=adm?manager.sendAdm && obj.sendAdm:manager.sendCustom && obj.sendCustom,
                target_ip=manager.targetIp,target_port=adm?manager.admPort:manager.customPort,
                send_rate_hz=Mathf.Clamp(requestedRate==0?manager.sendRateHz:requestedRate,1,60),adm_coordinate_mode=(int)manager.admCoordinateMode,
                send_only_on_change=adm?manager.admSendOnlyOnChange:config!=null && config.sendOnlyOnChange};
            var fields=new Dictionary<string,string>(StringComparer.Ordinal);
            if(obj.customFields!=null)foreach(var field in obj.customFields)if(field!=null && field.key!=null && !fields.ContainsKey(field.key))fields.Add(field.key,field.value);
            if(!adm)
            {
                adapter.custom_address_template=manager.customAddressTemplate;adapter.custom_arg_template=config?.argumentTemplate;
                adapter.custom_coordinate_mode=config?.coordinates.ToString();adapter.custom_send_mode=config?.sendMode.ToString();
                if(config!=null){adapter.custom_position_range=new[]{config.positionMin,config.positionMax};adapter.custom_gain_range=new[]{config.gainMin,config.gainMax};adapter.custom_width_range=new[]{config.widthMin,config.widthMax};}
                try{Protocols.CustomProtocol.ValidateFields(obj.customFields);}catch(FormatException error){adapter.configuration_error=error.Message;}
            }
            return new SceneTarget {protocol=protocol,object_id=adm?obj.AdmId:obj.CustomId,object_id_override=adm?obj.admObjectIdOverride:obj.customObjectIdOverride,
                enabled=adapter.enabled,adapter=adapter,custom_fields=fields,custom_field_entries=obj.customFields?.ToArray()};
        }
        static SceneTarget OutputTarget(Protocols.SpatialOutput output,SpatialFabricManager manager,SpatialObject obj)
        {
            if(output==null)return new SceneTarget{protocol="Invalid",adapter=new SceneAdapter{configuration_error="Null output"}};
            bool adm=output.protocol==Protocols.SpatialProtocol.ADM;var target=Target(adm?"ADM":"Custom",manager,obj);var a=target.adapter;
            a.output_key=output.key;a.label=output.label;a.target_ip=output.ip;a.target_port=output.port;a.send_rate_hz=output.sendRateHz;a.send_only_on_change=output.sendOnlyOnChange;a.adm_coordinate_mode=(int)output.admMode;
            target.enabled=output.enabled && (adm?obj.sendAdm:obj.sendCustom);a.enabled=target.enabled;
            if(!adm){var c=output.custom;a.custom_address_template=output.addressTemplate;a.custom_arg_template=c?.argumentTemplate;a.custom_coordinate_mode=c?.coordinates.ToString();a.custom_send_mode=c?.sendMode.ToString();if(c!=null){a.custom_position_range=new[]{c.positionMin,c.positionMax};a.custom_gain_range=new[]{c.gainMin,c.gainMax};a.custom_width_range=new[]{c.widthMin,c.widthMax};}}
            try{var o=Protocols.SpatialOutputs.Override(obj,output);if(o!=null){target.enabled&=o.enabled;a.enabled=target.enabled;target.object_id_override=o.objectIdOverride;}target.object_id=Protocols.SpatialOutputs.Id(obj,output);var fields=Protocols.SpatialOutputs.Fields(obj,output);target.custom_fields=fields.ToDictionary(f=>f.key,f=>f.value,StringComparer.Ordinal);target.custom_field_entries=fields.ToArray();}
            catch(FormatException e){a.configuration_error=e.Message;}
            return target;
        }
        public static string Failure(string error) => JsonUtility.ToJson(new SceneFailure {error = error});
        public static string GetJson(SpatialFabricManager owner,int offset = 0,int limit = 64)
        {
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != mainThreadId) return "{\"schema\":\"org.nplaudio.spatialfabric.scene\",\"schema_version\":1,\"error\":\"game_thread_required\"}";
            if (owner == null || offset < 0 || offset > 16384 || limit < 1 || limit > 64) return Failure("invalid_page");
            var rows = new List<SceneRecord>(); var scene = owner.gameObject.scene;
            if(!scene.IsValid() || !scene.isLoaded)return Failure("invalid_world");
            foreach (var manager in InWorld<SpatialFabricManager>(owner))
            {
                var row = Row("manager",manager); row.listening = manager.IsListening;
                row.configured_listen_ip = manager.listenIp; row.configured_listen_port = manager.spatialInputPort;
                row.bound_ip = manager.BoundSpatialIp; row.bound_port = manager.BoundSpatialPort;
                row.scene_query_enabled = manager.enableSceneQueries; row.allow_remote_scene_queries = manager.allowRemoteSceneQueries;
                rows.Add(row);
                for (int i = 0; i < manager.objects.Count; ++i)
                {
                    var obj = manager.objects[i]; if (obj == null || !owner.ContainsScene(obj.gameObject.scene)) continue;
                    var binding = Row("spatial_binding",obj,"/manager/"+manager.Key+"/binding/"+i);
                    binding.manager_path = Path(manager.transform); binding.binding_index = i; binding.label = obj.DisplayName;binding.resolved_actor_path=Path(obj.Pose);
                    binding.default_object_id = obj.objectId; binding.snapshot_resolved = manager.stage != null && owner.ContainsScene(manager.stage.gameObject.scene) && owner.ContainsScene(obj.Pose.gameObject.scene);
                    binding.enabled = manager.isActiveAndEnabled && obj.isActiveAndEnabled;
                    if (binding.snapshot_resolved)
                    {
                        Vector3 position = manager.stage.Normalize(obj.Pose.position);
                        binding.stage_normalized = Vec(position); binding.stage_meters = Vec(manager.stage.Meters(position));
                    }
                    binding.gain = obj.gain; binding.width = obj.width; binding.mute = obj.mute;
                    binding.targets = manager.outputs.Count>0?manager.outputs.Select(o=>OutputTarget(o,manager,obj)).ToArray():new[] {Target("ADM",manager,obj),Target("Custom",manager,obj)}; rows.Add(binding);
                }
            }
            foreach (var stage in InWorld<SpatialStage>(owner))
            {
                var row = Row("stage",stage); row.dimensions_meters = Vec(stage.dimensionsMeters);
                row.follow_listener = stage.EffectiveFollowMode!=ListenerFollowMode.Off;row.listener_follow_mode=stage.EffectiveFollowMode.ToString();
                row.listener_path = stage.listener != null ? Path(stage.listener) : ""; rows.Add(row);
                row.listener_resolved = stage.listener != null && owner.ContainsScene(stage.listener.gameObject.scene) && (stage.listener.gameObject.scene==stage.gameObject.scene || stage.allowLoadedAdditiveListener);
                if (row.listener_resolved)
                {
                    var listener = Row("listener",stage,"/listener"); listener.actor_path = Path(stage.listener); listener.actor_label = stage.listener.name;
                    Vector3 p = stage.listener.position;
                    listener.world_transform = new SceneTransform {position_cm = new[] {p.z*100,p.x*100,p.y*100},scale = Vec(stage.listener.lossyScale),unity_rotation_euler_deg = Vec(stage.listener.eulerAngles)};
                    rows.Add(listener);
                }
            }
            foreach (var cue in InWorld<PlaybackCue>(owner))
            {
                var row = Row("playback",cue); row.osc_base = cue.oscBase;
                row.controls = new SceneControls {play = cue.oscBase+"/play",stop = cue.oscBase+"/stop",loop = cue.oscBase+"/loop"};
                row.state_address = cue.oscBase+"/state"; row.configured_device_id = cue.configuredDeviceId;
                row.output_channel = cue.outputChannel; row.channel_policy = cue.outputChannel == 0 ? "corresponding_source_device_channels" : "first_source_channel_to_selected_output";
                // No AssetDatabase.Load, Resources.Load, GetData, LoadAudioData or device enumeration.
                row.sound_path = cue.preparedAudio != null ? cue.preparedAudio.SourceName : cue.clip != null ? cue.clip.name : "";
                row.source_channels = cue.preparedAudio != null ? cue.preparedAudio.Channels : cue.clip != null ? cue.clip.channels : 0;
                row.duration_seconds = cue.preparedAudio != null ? (float)cue.preparedAudio.Duration : cue.clip != null ? cue.clip.length : 0; row.playing = cue.IsPlaying;
                row.loop = cue.loop; row.auto_replay = cue.autoReplay; row.last_error = cue.LastError;
                row.registered = cue.isActiveAndEnabled; row.backend_available = cue.BackendAvailable; row.pcm_prepared = cue.IsPrepared;
                row.worker_state = cue.WorkerState.ToString(); row.end_reason = cue.EndReason.ToString(); row.replay_pending = cue.ReplayPending;
                row.exclusive_requested = cue.exclusiveRequested; row.buffer_ms_requested = cue.bufferMsRequested;
                row.exclusive_active = cue.ExclusiveActive; row.actual_output_channels = cue.ActualOutputChannels;
                row.actual_output_rate = cue.ActualOutputRate; row.actual_buffer_frames = cue.ActualBufferFrames; rows.Add(row);
            }
            foreach (var live in InWorld<LiveInputCue>(owner))
            {
                var row = Row("live_input",live); row.osc_base = live.oscBase;
                row.controls = new SceneControls {play = live.oscBase+"/play",stop = live.oscBase+"/stop",gain = live.oscBase+"/gain"};
                row.state_address = live.oscBase+"/state"; row.input_device_id = live.inputDeviceId; row.configured_device_id = live.outputDeviceId;
                row.input_channel = live.inputChannel; row.output_channel = live.outputChannel; row.gain = live.gain;
                // Cached observed state only: discovery never enumerates devices or opens a microphone.
                row.playing = live.IsLive; row.last_error = live.LastError; row.registered = live.isActiveAndEnabled; row.backend_available = live.BackendAvailable;
                row.worker_state = live.WorkerState.ToString(); row.end_reason = live.EndReason.ToString(); row.exclusive_requested = live.exclusiveOutputRequested; row.buffer_ms_requested = live.bufferMsRequested;
                row.actual_input_channels = live.ActualInputChannels; row.actual_input_rate = live.ActualInputRate; row.actual_output_channels = live.ActualOutputChannels; row.actual_output_rate = live.ActualOutputRate;
                row.input_peak = live.InputPeak; row.latency_ms = live.LatencyMilliseconds; row.underruns = live.Underruns; row.overruns = live.Overruns; rows.Add(row);
            }
            rows.Sort((a,b) => string.CompareOrdinal(a.key,b.key));
            if (rows.Count > 16384) return Failure("inventory_limit");
            if (rows.Select(r => r.key).Distinct().Count() != rows.Count) return Failure("duplicate_identity_repair_required");
            if (offset > rows.Count) return Failure("invalid_offset");
            var page = new ScenePage {world_scenes=WorldScenes(owner).Select(s=>string.IsNullOrEmpty(s.path)?s.name:s.path).ToArray(),world = string.IsNullOrEmpty(scene.path) ? scene.name : scene.path,
                world_type = Application.isPlaying ? (Application.isEditor ? "PIE" : "Game") : "Editor",
                snapshot_utc = DateTime.UtcNow.ToString("o"),world_time_seconds = Application.isPlaying ? Time.timeAsDouble : 0,
                playback_network = new ScenePlaybackNetwork {configured_listen_ip = owner.listenIp,configured_listen_port = owner.playbackInputPort,
                    bound_ip = owner.BoundPlaybackIp,bound_port = owner.BoundPlaybackPort,listening = owner.IsListening,monitor_ip = owner.targetIp,monitor_port = owner.monitorPort},
                offset = offset,limit = limit,total = rows.Count};
            page.objects = rows.Skip(offset).Take(limit).ToArray(); page.returned = page.objects.Length;
            page.next_offset = offset + page.returned < page.total ? offset + page.returned : -1;
            try{return Protocols.ContractJson.Serialize(page);}
            catch(FormatException){return Failure("snapshot_encoding_or_page_limit_reduce_page_size");}
        }
    }
}
