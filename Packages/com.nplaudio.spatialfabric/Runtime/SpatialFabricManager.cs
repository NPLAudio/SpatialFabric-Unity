// Copyright (c) 2026 SpatialFabric Contributors. MIT.
using System;
using System.Collections.Generic;
using UnityEngine;
using NPLAudio.SpatialFabric.Osc;

namespace NPLAudio.SpatialFabric
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class SpatialFabricManager : SpatialIdentity
    {
        public SpatialStage stage;
        [Tooltip("Explicit loaded additive scene paths included in this manager's world. Empty means owner scene only; no automatic world search.")]
        public List<string> additionalScenePaths = new List<string>();
        public bool ContainsScene(UnityEngine.SceneManagement.Scene scene)=>scene.IsValid() && scene.isLoaded && (scene==gameObject.scene || !string.IsNullOrEmpty(scene.path) && additionalScenePaths.Contains(scene.path));
        public List<SpatialObject> objects = new List<SpatialObject>();
        public bool enableInEditor, enableInPlay;
        public bool runInBackground = true;
        public string listenIp = "127.0.0.1";
        public int spatialInputPort = 8100, playbackInputPort = 9000;
        public string targetIp = "127.0.0.1";
        public int admPort = 50018, customPort = 9002, monitorPort = 9001;
        [Range(1,60)] public int sendRateHz = 30;
        public bool sendAdm = true, sendCustom = true;
        public string customAddressTemplate = "/unity/{id}/xyz";
        public Protocols.CustomProtocolSettings customProtocol = new Protocols.CustomProtocolSettings();
        public AdmCoordinateMode admCoordinateMode = AdmCoordinateMode.Both;
        public bool admSendOnlyOnChange;
        [Range(0,60)] public int admSendRateHz; // 0 inherits sendRateHz.
        [Tooltip("When nonempty these independent destinations replace the legacy ADM/Custom endpoints above.")]
        public List<Protocols.SpatialOutput> outputs = new List<Protocols.SpatialOutput>();
        public bool enableSceneQueries = true, allowRemoteSceneQueries;
        public bool IsListening => spatial != null && spatial.IsListening && playback != null && playback.IsListening;
        public string LastError { get; private set; } = "Networking is stopped. Enable the desired context and explicitly Start.";
        public string BoundSpatialIp => spatial?.Bound?.Address.ToString() ?? "";
        public int BoundSpatialPort => spatial?.Bound?.Port ?? 0;
        public string BoundPlaybackIp => playback?.Bound?.Address.ToString() ?? "";
        public int BoundPlaybackPort => playback?.Bound?.Port ?? 0;
        public long Sent { get; private set; }
        public long Received { get; private set; }
        OscEndpoint spatial, playback;
        readonly SceneQueryResponder query = new SceneQueryResponder();
        double nextAdm,nextCustom;
        sealed class OutputRuntime {public double next;public OutgoingBatch batch=new OutgoingBatch();}
        readonly Dictionary<string,OutputRuntime> outputRuntime=new Dictionary<string,OutputRuntime>();
        public bool Resolves(SpatialObject obj)=>obj!=null && obj.isActiveAndEnabled && ContainsScene(obj.gameObject.scene) && ContainsScene(obj.Pose.gameObject.scene);
        readonly OutgoingBatch admBatch = new OutgoingBatch(), customBatch = new OutgoingBatch();
        public bool NetworkingAllowed => Application.isPlaying ? enableInPlay : enableInEditor;
        protected override void Awake(){base.Awake();if(customProtocol==null)customProtocol=new Protocols.CustomProtocolSettings();}
        protected override void OnValidate(){base.OnValidate();if(customProtocol==null)customProtocol=new Protocols.CustomProtocolSettings();foreach(var output in outputs)if(output!=null && string.IsNullOrEmpty(output.key))output.key=System.Guid.NewGuid().ToString("N");}
        public bool StartNetworking()
        {
            if (!NetworkingAllowed) { LastError = "Networking is disabled in this context"; return false; }
            StopNetworking();
            var a = new OscEndpoint(); var b = new OscEndpoint();
            try
            {
                if (spatialInputPort == playbackInputPort) throw new ArgumentException("Use distinct spatial and playback input ports");
                a.Start(listenIp,spatialInputPort); b.Start(listenIp,playbackInputPort);
                spatial = a; playback = b; nextAdm = nextCustom = 0; admBatch.Reset(); customBatch.Reset(); outputRuntime.Clear(); LastError = "";
                if (Application.isPlaying && runInBackground) Application.runInBackground = true;
                return true;
            }
            catch (Exception error) { a.Dispose(); b.Dispose(); LastError = error.Message; return false; }
        }
        public void StopNetworking() { spatial?.Dispose(); playback?.Dispose(); spatial = playback = null; }
        void OnEnable() { PlaybackEvents.StateChanged += OnPlaybackState; }
        void OnDisable() { PlaybackEvents.StateChanged -= OnPlaybackState; StopNetworking(); }
        void OnPlaybackState(PlaybackCue cue,bool playing)
        {
            if(!IsListening || !NetworkingAllowed || !ContainsScene(cue.gameObject.scene) || !cue.ValidBase)return;
            try {spatial.Send(targetIp,monitorPort,new OscMessage(cue.oscBase+"/state",OscArg.Float(playing?1:0)));++Sent;}
            catch(Exception error) when(error is FormatException || error is ArgumentException || error is System.Net.Sockets.SocketException) {LastError=error.Message;}
        }
        void OnDestroy() { StopNetworking(); }
        void Update() { if (Application.isPlaying) Tick(Time.realtimeSinceStartupAsDouble); }
        /// <summary>Main-thread editor bridge calls this independently of scene repaint.</summary>
        public void Tick(double now)
        {
            if (!NetworkingAllowed) { if (spatial != null || playback != null) StopNetworking(); return; }
            if (!IsListening) return;
            Drain(spatial,now); Drain(playback,now);
            if (stage == null || !ContainsScene(stage.gameObject.scene)) return;
            if(outputs.Count>0){TickOutputs(now);return;}
            if(sendAdm && now>=nextAdm)
            {
                nextAdm=now+1.0/Mathf.Clamp(admSendRateHz==0?sendRateHz:admSendRateHz,1,60);
                SendFrame(true);
            }
            if(sendCustom && now>=nextCustom)
            {
                nextCustom=now+1.0/Mathf.Clamp(customProtocol==null || customProtocol.sendRateHz==0?sendRateHz:customProtocol.sendRateHz,1,60);
                SendFrame(false);
            }
        }
        void TickOutputs(double now)
        {
            var keys=new HashSet<string>(StringComparer.Ordinal);
            foreach(var output in outputs)if(output==null || string.IsNullOrWhiteSpace(output.key) || !keys.Add(output.key)){LastError="Missing/duplicate output keys; frame refused";return;}
            foreach(var output in outputs)
            {
                if(!output.enabled){outputRuntime.Remove(output.key);continue;}
                if(!outputRuntime.TryGetValue(output.key,out var runtime)){runtime=new OutputRuntime();outputRuntime.Add(output.key,runtime);}
                if(now<runtime.next)continue;runtime.next=now+1.0/Mathf.Clamp(output.sendRateHz,1,60);
                try{Sent+=runtime.batch.Send(spatial,output.ip,output.port,Protocols.SpatialOutputs.Build(this,output),output.sendOnlyOnChange);}
                catch(Exception e)when(e is FormatException || e is ArgumentException || e is System.Net.Sockets.SocketException){LastError=e.Message;}
            }
            var removed=new List<string>();foreach(var key in outputRuntime.Keys)if(!keys.Contains(key))removed.Add(key);foreach(var key in removed)outputRuntime.Remove(key);
        }
        void SendFrame(bool adm)
        {
            try
            {
                var messages=new List<OscMessage>();
                foreach(var obj in objects)
                {
                    if(!Resolves(obj) || (adm?!obj.sendAdm:!obj.sendCustom))continue;
                    Vector3 p=stage.Normalize(obj.Pose.position);
                    if(adm)messages.AddRange(SpatialMessages.Adm(obj,p,admCoordinateMode));
                    else messages.AddRange(Protocols.CustomProtocol.Build(customProtocol,customAddressTemplate,new Protocols.CustomObjectState {id=obj.CustomId,label=obj.DisplayName,front=p.x,right=p.y,up=p.z,gain=obj.gain,width=obj.width,fields=obj.customFields}));
                    if(messages.Count>4096)throw new FormatException("OSC frame message limit exceeded");
                }
                Sent+=(adm?admBatch:customBatch).Send(spatial,targetIp,adm?admPort:customPort,messages,adm?admSendOnlyOnChange:customProtocol.sendOnlyOnChange);
            }
            catch(Exception error) when(error is FormatException || error is ArgumentException || error is System.Net.Sockets.SocketException) {LastError=error.Message;}
        }
        void Drain(OscEndpoint endpoint,double now)
        {
            foreach (var received in endpoint.Drain())
            {
                ++Received;
                try
                {
                    if (query.Handle(this,endpoint,received,now)) continue;
                    DispatchPlayback(received.Message);
                }
                catch (Exception error) when (error is FormatException || error is ArgumentException || error is System.Net.Sockets.SocketException) { LastError = error.Message; }
            }
        }
        void DispatchPlayback(OscMessage message)
        {
            foreach (var cue in SceneInventory.InWorld<PlaybackCue>(this))
            {
                if (!cue.isActiveAndEnabled || !cue.ValidBase) continue;
                if (message.Address == cue.oscBase+"/play" && PlaybackControl.Play(message)) { cue.TryPlay(); LastError = cue.LastError; }
                else if (message.Address == cue.oscBase+"/stop") cue.Stop();
                else if (message.Address == cue.oscBase+"/loop") cue.SetLoop(PlaybackControl.Loop(message));
            }
        }
    }
}
