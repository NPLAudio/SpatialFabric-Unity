// No test/NuGet dependencies; compile the actual UPM transport sources under .NET.
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using NPLAudio.SpatialFabric.Osc;
using NPLAudio.SpatialFabric.Audio;
using NPLAudio.SpatialFabric.Protocols;
using NPLAudio.SpatialFabric.Routing;
using NPLAudio.SpatialFabric.Paths;
using System.Text;

int passed = 0;
void Check(string name,Action test) { test(); Console.WriteLine("PASS " + name); ++passed; }
void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
void Reject(Action test) { bool rejected = false; try { test(); } catch (Exception e) when (e is FormatException || e is ArgumentException) { rejected = true; } Assert(rejected); }
byte[] Bundle(params byte[][] packets)
{
    var list = new List<byte>(Convert.FromHexString("2362756e646c65000000000000000001"));
    foreach (var p in packets) { var size = BitConverter.GetBytes(p.Length); if (BitConverter.IsLittleEndian) Array.Reverse(size); list.AddRange(size); list.AddRange(p); }
    return list.ToArray();
}
using var fixtures = JsonDocument.Parse(File.ReadAllText(args.Length > 0 ? args[0] : "../SpatialFabric-Interop/Fixtures/osc-v1.json"));
foreach (var v in fixtures.RootElement.GetProperty("valid").EnumerateArray())
{
    string name = v.GetProperty("name").GetString(),hex = v.GetProperty("hex").GetString();
    Check("golden " + name,() => { var p = Convert.FromHexString(hex); var m = OscCodec.Decode(p); Assert(m.Count == 1 && m[0].Address == v.GetProperty("address").GetString()); Assert(Convert.ToHexString(OscCodec.Encode(m[0])).ToLowerInvariant() == hex); });
}
foreach (var v in fixtures.RootElement.GetProperty("invalid").EnumerateArray())
    Check("reject " + v.GetProperty("name").GetString(),() => Reject(() => OscCodec.Decode(Convert.FromHexString(v.GetProperty("hex").GetString()))));
var valid = OscCodec.Encode(new OscMessage("/test",OscArg.Int(int.MaxValue),OscArg.Float(-.5f),OscArg.String("日本語 🎧")));
Check("all truncations reject",() => { for (int i = 0; i < valid.Length; ++i) Reject(() => OscCodec.Decode(valid.Take(i).ToArray())); });
Check("int32 extremes",() => { foreach (int i in new[] {int.MinValue,int.MaxValue,0}) Assert((int)OscCodec.Decode(OscCodec.Encode(new OscMessage("/i",OscArg.Int(i))))[0].Arguments[0].Value == i); });
Check("nonfinite encode",() => { foreach (float f in new[] {float.NaN,float.NegativeInfinity,float.PositiveInfinity}) Reject(() => OscCodec.Encode(new OscMessage("/f",OscArg.Float(f)))); });
Check("embedded NUL",() => Reject(() => OscCodec.Encode(new OscMessage("/s",OscArg.String("a\0b")))));
Check("unsupported output tag",() => Reject(() => OscCodec.Encode(new OscMessage("/s",new OscArg('q',null)))));
Check("wildcard/invalid addresses",() => { foreach (string a in new[] {"/", "", "test", "/a/*", "/a/{id}", "/a//b", "/a/", "/a b", "/é"}) Assert(!OscCodec.ValidAddress(a)); });
Check("argument limit",() => Reject(() => OscCodec.Encode(new OscMessage("/s",Enumerable.Repeat(OscArg.Int(1),129).ToArray()))));
Check("bundle two messages",() => Assert(OscCodec.Decode(Bundle(valid,valid)).Count == 2));
Check("bundle invalid is atomic",() => Reject(() => OscCodec.Decode(Bundle(valid,new byte[8]))));
Check("nested bundle depth",() => { byte[] p = valid; for (int i = 0; i < 10; ++i) p = Bundle(p); Reject(() => OscCodec.Decode(p)); });
Check("message count limit",() => Reject(() => OscCodec.Decode(Bundle(Enumerable.Repeat(OscCodec.Encode(new OscMessage("/x")),129).ToArray()))));
Check("UTF8 rejects invalid sequence",() => Reject(() => OscCodec.Decode(Convert.FromHexString("2f7800002c730000ff000000"))));
Check("trailing bytes",() => Reject(() => OscCodec.Decode(valid.Concat(new byte[4]).ToArray())));
Check("oversized datagram",() => Reject(() => OscCodec.Decode(new byte[65508])));
Check("typed encoding enforced",() => Reject(() => OscCodec.Encode(new OscMessage("/x",new OscArg('i',1f)))));
int FreePort() { using var s = new Socket(AddressFamily.InterNetwork,SocketType.Dgram,ProtocolType.Udp); s.Bind(new IPEndPoint(IPAddress.Loopback,0)); return ((IPEndPoint)s.LocalEndPoint).Port; }
Check("exact bind, collision, release and UDP dispatch",() =>
{
    int port = FreePort(); using var a = new OscEndpoint(); using var b = new OscEndpoint();
    a.Start("127.0.0.1",port); Assert(a.IsListening && a.Bound.Port == port && a.Bound.Address.Equals(IPAddress.Loopback));
    bool collision = false; try { b.Start("127.0.0.1",port); } catch (SocketException) { collision = true; } Assert(collision);
    using var sender = new UdpClient(); sender.Send(new byte[8],8,new IPEndPoint(IPAddress.Loopback,port)); sender.Send(valid,valid.Length,new IPEndPoint(IPAddress.Loopback,port));
    List<ReceivedOsc> rows = new(); var deadline = DateTime.UtcNow.AddSeconds(3);
    while (rows.Count == 0 && DateTime.UtcNow < deadline) { Thread.Sleep(10); rows.AddRange(a.Drain()); }
    Assert(rows.Count == 1 && rows[0].Message.Address == "/test" && a.Rejected >= 1);
    a.Stop(); Assert(!a.IsListening && a.Bound == null); b.Start("127.0.0.1",port); Assert(b.Bound.Port == port);
});
Check("no listen on construction",() => { using var endpoint = new OscEndpoint(); Assert(endpoint.Bound == null && endpoint.Drain().Count == 0); });
Check("invalid endpoints rejected",() => { foreach (var pair in new[] {("localhost",8100),("127.1",8100),("224.0.0.1",8100),("127.0.0.1",0),("127.0.0.1",65536),("0.0.0.0",9000)}) Reject(() => OscEndpoint.Address(pair.Item1,pair.Item2)); });
byte[] Wav(ushort format,ushort bits,ushort channels,uint rate,byte[] data,bool extensible=false,ushort validBits=0)
{
    using var stream=new MemoryStream();using var w=new BinaryWriter(stream);
    void Tag(string s)=>w.Write(System.Text.Encoding.ASCII.GetBytes(s));
    Tag("RIFF");w.Write(0u);Tag("WAVE");Tag("fmt ");w.Write(extensible?40u:16u);w.Write(extensible?(ushort)0xfffe:format);w.Write(channels);w.Write(rate);
    ushort align=(ushort)(channels*(bits/8));w.Write(rate*align);w.Write(align);w.Write(bits);
    if(extensible) {w.Write((ushort)22);w.Write(validBits==0?bits:validBits);w.Write(0u);w.Write(new Guid(format==1?"00000001-0000-0010-8000-00aa00389b71":"00000003-0000-0010-8000-00aa00389b71").ToByteArray());}
    Tag("data");w.Write((uint)data.Length);w.Write(data);if(data.Length%2!=0)w.Write((byte)0);stream.Position=4;w.Write((uint)(stream.Length-8));return stream.ToArray();
}
Check("WAV PCM16 stereo",()=> {var p=WavDecoder.Decode(Wav(1,16,2,48000,new byte[]{0,128,255,127}));Assert(p.Channels==2 && p.SampleRate==48000 && p.Samples.Length==2 && p.Samples[0]==-1 && p.Samples[1]>.999f);});
Check("WAV unsigned PCM8 and odd padding",()=> {var p=WavDecoder.Decode(Wav(1,8,1,48000,new byte[]{0,128,255}));Assert(p.Samples[0]==-1 && p.Samples[1]==0 && p.Samples[2]>.99f);});
Check("WAV PCM24 sign extension",()=> {var p=WavDecoder.Decode(Wav(1,24,1,48000,new byte[]{0,0,128,0,0,64}));Assert(p.Samples[0]==-1 && p.Samples[1]==.5f);});
Check("WAV PCM32 extrema",()=> {var p=WavDecoder.Decode(Wav(1,32,1,48000,BitConverter.GetBytes(int.MinValue)));Assert(p.Samples[0]==-1);});
Check("WAV float32 and float64",()=> {Assert(WavDecoder.Decode(Wav(3,32,1,48000,BitConverter.GetBytes(.25f))).Samples[0]==.25f);Assert(WavDecoder.Decode(Wav(3,64,1,48000,BitConverter.GetBytes(-.25))).Samples[0]==-.25f);});
Check("WAV extensible left-aligned valid bits",()=> {Assert(WavDecoder.Decode(Wav(1,32,1,48000,BitConverter.GetBytes(0x40000000),true,24)).Samples[0]==.5f);});
Check("WAV rejects nonfinite floats",()=>Reject(()=>WavDecoder.Decode(Wav(3,32,1,48000,BitConverter.GetBytes(float.NaN)))));
Check("WAV rejects compression",()=>Reject(()=>WavDecoder.Decode(Wav(2,16,1,48000,new byte[2]))));
Check("WAV validates format rate and alignment",()=> {Reject(()=>WavDecoder.Decode(Wav(1,16,1,1,new byte[2])));var b=Wav(1,16,2,48000,new byte[4]);b[32]=1;Reject(()=>WavDecoder.Decode(b));});
Check("WAV rejects partial frames",()=>Reject(()=>WavDecoder.Decode(Wav(1,16,2,48000,new byte[2]))));
Check("WAV all truncated headers/data rejected",()=> {var b=Wav(1,16,1,48000,new byte[4]);for(int i=0;i<b.Length;++i)Reject(()=>WavDecoder.Decode(b.Take(i).ToArray()));});
Check("WAV rejects oversized chunks before allocation",()=> {var b=Wav(1,16,1,48000,new byte[2]);for(int i=16;i<20;++i)b[i]=255;Reject(()=>WavDecoder.Decode(b));});
Check("WAV rejects unknown extensible GUID",()=> {var b=Wav(1,32,1,48000,new byte[4],true);b[44]=99;Reject(()=>WavDecoder.Decode(b));});
Check("WAV rejects invalid extension length",()=> {var b=Wav(1,32,1,48000,new byte[4],true);b[36]=255;Reject(()=>WavDecoder.Decode(b));});
Check("observed starts are not request acknowledgements",()=> {var o=new PlaybackObservation();var events=new List<bool>();o.StateChanged+=events.Add;o.Observe(AudioWorkerState.Starting,AudioEndReason.None,0,0,true,true,false,1,0);o.Observe(AudioWorkerState.Failed,AudioEndReason.DeviceError,0,0,true,true,false,1,1);Assert(events.Count==0 && !o.IsPlaying && !o.ReplayPending);});
Check("short-clip observed start and end survive one poll",()=> {var o=new PlaybackObservation();var events=new List<bool>();o.StateChanged+=events.Add;o.Observe(AudioWorkerState.Stopped,AudioEndReason.NaturalEnd,1,1,true,true,false,2,10);Assert(events.SequenceEqual(new[]{true,false}) && o.ReplayPending && !o.IsPlaying);Assert(!o.TakeReplay(11.9) && o.TakeReplay(12) && !o.TakeReplay(13));});
Check("manual stop cancels queued replay",()=> {var o=new PlaybackObservation();o.Observe(AudioWorkerState.Stopped,AudioEndReason.NaturalEnd,1,1,true,true,false,1,0);o.CancelReplay();Assert(!o.TakeReplay(100));});
Check("loop or replay policy changes cancel queued replay",()=> {foreach(bool loop in new[]{false,true}){var o=new PlaybackObservation();o.Observe(AudioWorkerState.Stopped,AudioEndReason.NaturalEnd,1,1,true,true,false,1,0);o.ApplyReplayPolicy(loop,loop);Assert(!o.TakeReplay(100));}});
Check("worker failure never schedules replay",()=> {var o=new PlaybackObservation();o.Observe(AudioWorkerState.Failed,AudioEndReason.DeviceError,1,1,true,true,false,0,1);Assert(!o.ReplayPending);});
Check("repeated ended snapshots do not postpone replay",()=> {var o=new PlaybackObservation();o.Observe(AudioWorkerState.Stopped,AudioEndReason.NaturalEnd,1,1,true,true,false,2,0);o.Observe(AudioWorkerState.Stopped,AudioEndReason.NaturalEnd,1,1,true,true,false,2,1);Assert(o.TakeReplay(2));});
using(var customFixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(args[0]),"custom-v1.json"))))
foreach(var row in customFixture.RootElement.GetProperty("vectors").EnumerateArray())
{
    Check("Custom golden "+row.GetProperty("name").GetString(),()=>
    {
        var options=new JsonSerializerOptions{IncludeFields=true};
        var config=JsonSerializer.Deserialize<CustomProtocolSettings>(row.GetProperty("config").GetRawText(),options);
        var state=JsonSerializer.Deserialize<CustomObjectState>(row.GetProperty("state").GetRawText(),options);
        var messages=CustomProtocol.Build(config,row.GetProperty("address").GetString(),state);var expected=row.GetProperty("expected").EnumerateArray().ToArray();Assert(messages.Count==expected.Length);
        for(int i=0;i<messages.Count;++i)Assert(Convert.ToHexString(OscCodec.Encode(messages[i])).ToLowerInvariant()==expected[i].GetProperty("hex").GetString());
    });
}
CustomObjectState State()=>new(){id=1,front=.5f,right=0,up=0};
Check("Custom rejects unresolved tokens atomically",()=>Reject(()=>CustomProtocol.Build(new CustomProtocolSettings{sendMode=CustomSendMode.Discrete},"/{id}/{axis}/{missing}",State())));
Check("Custom rejects invalid argument",()=>Reject(()=>CustomProtocol.Build(new CustomProtocolSettings{argumentTemplate="x mute"},"/obj/{id}",State())));
Check("Custom rejects packed axis placeholder",()=>Reject(()=>CustomProtocol.Build(new CustomProtocolSettings(),"/obj/{axis}",State())));
Check("Custom rejects nonfinite input/range",()=>{Reject(()=>CustomProtocol.Build(new CustomProtocolSettings{gainMax=float.NaN},"/x",State()));var s=State();s.up=float.PositiveInfinity;Reject(()=>CustomProtocol.Build(new CustomProtocolSettings(),"/x",s));});
Check("Custom fields reject collisions and template injection",()=>{foreach(var f in new[]{new CustomField{key="id",value="9"},new CustomField{key="room",value="{id}"}}){var s=State();s.fields=new[]{f};Reject(()=>CustomProtocol.Build(new CustomProtocolSettings(),"/x",s));}});
Check("Custom validates final address after label replacement",()=>{var s=State();s.label="room with space";Reject(()=>CustomProtocol.Build(new CustomProtocolSettings(),"/{label}",s));});
Check("Custom argument bounds",()=>Reject(()=>CustomProtocol.Build(new CustomProtocolSettings{argumentTemplate=string.Join(" ",Enumerable.Repeat("x",33))},"/x",State())));
Check("Custom empty packed args are address-only",()=>Assert(CustomProtocol.Build(new CustomProtocolSettings{argumentTemplate=""},"/x",State()).Single().Arguments.Length==0));
Check("XYZ mode keeps AED args zero like Unreal",()=>Assert(CustomProtocol.Build(new CustomProtocolSettings{argumentTemplate="a e d"},"/x",State())[0].Arguments.All(a=>(float)a.Value==0)));
Check("CSV multiline unicode quotes and final empty field roundtrip",()=>{var rows=new[]{new[]{"Key","Value","Empty"},new[]{"unity:1","日本語, \"Tap\"\r\nsecond line",""}};var parsed=RoutingCsvCodec.Parse(RoutingCsvCodec.Write(rows));Assert(parsed.Count==2 && parsed[1].SequenceEqual(rows[1]));});
Check("CSV BOM and CRLF",()=>Assert(RoutingCsvCodec.Parse("\ufeffa,b\r\n1,2\r\n").Count==2));
Check("CSV malformed quoting rejects",()=>{foreach(string s in new[]{"\"unclosed","a\"b,c","\"closed\"junk,field","\"closed\" \n"})Reject(()=>RoutingCsvCodec.Parse(s));});
Check("CSV bounded fields and NUL",()=>{Reject(()=>RoutingCsvCodec.Parse(new string('x',RoutingCsvCodec.MaxField+1)));Reject(()=>RoutingCsvCodec.Parse("x\0y"));});
Check("CSV headers reject duplicate unknown and missing columns",()=>{foreach(var header in new[]{new[]{"Key","Key"},new[]{"Key","Other"},new[]{"Key"}})Reject(()=>RoutingCsvCodec.Header(header,new[]{"Key","Value"}));});
Check("CSV reordered complete columns map by name",()=>Assert(RoutingCsvCodec.Header(new[]{"Value","Key"},new[]{"Key","Value"})["Key"]==1));
Check("contract JSON dictionaries preserve object types and unicode",()=>{string text=ContractJson.Serialize(new Dictionary<string,object>{{"custom_fields",new Dictionary<string,string>{{"room","日本語 \"A\"\n🎧"}}},{"position",new[]{.5f,-1f,0f}}});using var doc=JsonDocument.Parse(text);Assert(doc.RootElement.GetProperty("custom_fields").GetProperty("room").GetString()=="日本語 \"A\"\n🎧");});
Check("contract JSON finite and byte/depth bounds",()=>{Reject(()=>ContractJson.Serialize(new[]{float.NaN}));Reject(()=>ContractJson.Serialize(new string('x',100),32));var cyclic=new System.Collections.ArrayList();cyclic.Add(cyclic);Reject(()=>ContractJson.Serialize(cyclic));});
Check("contract JSON rejects lone surrogates",()=>Reject(()=>ContractJson.Serialize("\ud800")));
Check("outgoing batch suppression and atomic preflight",()=>
{
    int port=FreePort();using var listener=new OscEndpoint();using var sender=new OscEndpoint();listener.Start("127.0.0.1",port);var batch=new OutgoingBatch();var messages=new[]{new OscMessage("/good",OscArg.Float(1))};
    Assert(batch.Send(sender,"127.0.0.1",port,messages,true)==1);Assert(batch.Send(sender,"127.0.0.1",port,messages,true)==0);batch.Reset();Assert(batch.Send(sender,"127.0.0.1",port,messages,true)==1);
    Thread.Sleep(50);Assert(listener.Drain().Count==2);
    Reject(()=>batch.Send(sender,"127.0.0.1",port,new[]{messages[0],new OscMessage("/{bad}")},false));Thread.Sleep(50);Assert(listener.Drain().Count==0);
});
Check("AudioLoom play/loop half-threshold distinction",()=>{var m=new OscMessage("/cue",OscArg.Float(.5f));Assert(PlaybackControl.Play(m) && !PlaybackControl.Loop(m));});
Check("AudioLoom float precedence across mixed args",()=>{var m=new OscMessage("/cue",OscArg.Int(1),OscArg.String("metadata"),OscArg.Float(0));Assert(!PlaybackControl.Play(m) && !PlaybackControl.Loop(m));});
Check("AudioLoom address-only play but loop off",()=>{var m=new OscMessage("/cue");Assert(PlaybackControl.Play(m) && !PlaybackControl.Loop(m));});
Check("AudioLoom unknown nonnumeric payload never plays",()=>{var m=new OscMessage("/cue",OscArg.String("1"),new OscArg('T',true));Assert(!PlaybackControl.Play(m) && !PlaybackControl.Loop(m));});
using(var ackFixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(args[0]),"ack-v1.json"))))
{
    foreach(var row in ackFixture.RootElement.GetProperty("vectors").EnumerateArray())
        Check("ack golden "+row.GetProperty("name").GetString(),()=>
        {
            var message=new OscMessage(row.GetProperty("address").GetString(),OscArg.String(row.GetProperty("command").GetString()),OscArg.String(row.GetProperty("result").GetString()),OscArg.Int(row.GetProperty("playing").GetInt32()),OscArg.String(row.GetProperty("detail").GetString()));
            Assert(Convert.ToHexString(OscCodec.Encode(message)).ToLowerInvariant()==row.GetProperty("hex").GetString());
            string cueBase=message.Address.Substring(0,message.Address.Length-4);
            Assert(OscCodec.Encode(PlaybackControl.Ack(cueBase,(string)message.Arguments[0].Value,(string)message.Arguments[1].Value,(int)message.Arguments[2].Value!=0,(string)message.Arguments[3].Value)).SequenceEqual(OscCodec.Encode(message)));
        });
    foreach(var row in ackFixture.RootElement.GetProperty("decisions").EnumerateArray())
        Check("ack decision "+row.GetProperty("kind").GetString()+" "+row.GetProperty("op").GetString()+" "+row.GetProperty("args").GetRawText(),()=>
        {
            var arguments=row.GetProperty("args").EnumerateArray().Select(a=>a[0].GetString() switch {"f"=>OscArg.Float(a[1].GetSingle()),"i"=>OscArg.Int(a[1].GetInt32()),_=>OscArg.String(a[1].GetString())}).ToArray();
            string command=row.GetProperty("op").GetString();
            Assert(PlaybackControl.Decide(command,new OscMessage("/cue/"+command,arguments),row.GetProperty("kind").GetString()=="live")==row.GetProperty("result").GetString());
        });
}
Check("ack command sanitization and bound",()=>Assert(PlaybackControl.SanitizeCommand("x/\n"+new string('a',50))=="x__"+new string('a',29)));
Check("status payload rejected",()=>Assert(PlaybackControl.Decide("status",new OscMessage("/cue/status",OscArg.Int(1)),false)=="rejected"));
// ---- Path Studio context exporter core (Editor/ContextExportCore.cs) ----
bool Near(double a,double b)=>Math.Abs(a-b)<1e-9;
var ctxFrame=new NPLAudio.SpatialFabric.Editor.ContextStageFrame(20,15,8);
Check("context ToCanonical hand values",()=>{var c=NPLAudio.SpatialFabric.Editor.ContextExportCore.ToCanonical(ctxFrame,1.5,2,5);Assert(Near(c.front,.5)&&Near(c.right,.2)&&Near(c.up,.5));});
Check("context ToCanonical flips negate axes",()=>
{
    var c=NPLAudio.SpatialFabric.Editor.ContextExportCore.ToCanonical(new NPLAudio.SpatialFabric.Editor.ContextStageFrame(20,15,8,true,false,false),1.5,2,5);Assert(Near(c.front,-.5)&&Near(c.right,.2)&&Near(c.up,.5));
    c=NPLAudio.SpatialFabric.Editor.ContextExportCore.ToCanonical(new NPLAudio.SpatialFabric.Editor.ContextStageFrame(20,15,8,false,true,false),1.5,2,5);Assert(Near(c.front,.5)&&Near(c.right,-.2)&&Near(c.up,.5));
    c=NPLAudio.SpatialFabric.Editor.ContextExportCore.ToCanonical(new NPLAudio.SpatialFabric.Editor.ContextStageFrame(20,15,8,false,false,true),1.5,2,5);Assert(Near(c.front,.5)&&Near(c.right,.2)&&Near(c.up,-.5));
});
Check("context ToCanonical min half extent .05",()=>{var c=NPLAudio.SpatialFabric.Editor.ContextExportCore.ToCanonical(new NPLAudio.SpatialFabric.Editor.ContextStageFrame(0,.02,0),.1,.5,1);Assert(Near(c.front,20)&&Near(c.right,2)&&Near(c.up,10));});
Check("context ToCanonical is unclamped",()=>{var c=NPLAudio.SpatialFabric.Editor.ContextExportCore.ToCanonical(ctxFrame,0,0,40);Assert(Near(c.front,4.0));});
Check("context rotated box conservative AABB",()=>
{
    // Unity-local box half (1,1,1) turned 45 degrees about up: corners in x/z at (+-sqrt2,0),(0,+-sqrt2); stage 20x20x20 => half extent 10.
    double r=Math.Sqrt(2);var corners=new List<(double,double,double)>();
    foreach(var xz in new[]{(r,0.0),(-r,0.0),(0.0,r),(0.0,-r)}) foreach(double y in new[]{-1.0,1.0}) corners.Add((xz.Item1+3,y,xz.Item2));
    var box=NPLAudio.SpatialFabric.Editor.ContextExportCore.BoxFromLocalCorners(new NPLAudio.SpatialFabric.Editor.ContextStageFrame(20,20,20),corners);
    Assert(Near(box.center[0],0)&&Near(box.center[1],.3)&&Near(box.center[2],0));
    Assert(Near(box.halfExtents[0],r/10)&&Near(box.halfExtents[1],r/10)&&Near(box.halfExtents[2],.1));
});
Check("context listener yaw",()=>
{
    double Yaw(double x,double y,double z,bool flipRight=false,bool flipFront=false)=>NPLAudio.SpatialFabric.Editor.ContextExportCore.ListenerYawDegrees(new NPLAudio.SpatialFabric.Editor.ContextStageFrame(20,15,8,flipFront,flipRight,false),x,y,z);
    Assert(Near(Yaw(0,0,1),0)&&Near(Yaw(-1,0,0),90)&&Near(Yaw(1,0,0),-90)&&Near(Yaw(0,0,-1),180));
    Assert(Near(Yaw(-1,0,0,true),-90)&&Near(Yaw(0,0,1,false,true),180)&&Near(Yaw(-1,.7,1),45));
    Assert(Near(Yaw(0,1,0),0));
});
NPLAudio.SpatialFabric.Editor.ContextEntry CtxEntry(string id,NPLAudio.SpatialFabric.Editor.ContextKind kind,double x=0,double y=0,double z=0,string label="")=>new(){Id=id,Kind=kind,Label=label,Position=new[]{x,y,z},HalfExtents=new[]{1.0,1.0,1.0}};
Check("context sanitize and unique ids",()=>
{
    var r=NPLAudio.SpatialFabric.Editor.ContextExportCore.Build("n",new[]{CtxEntry("a b#c",NPLAudio.SpatialFabric.Editor.ContextKind.Point),CtxEntry("a_b_c",NPLAudio.SpatialFabric.Editor.ContextKind.Point),CtxEntry("a_b_c",NPLAudio.SpatialFabric.Editor.ContextKind.Point),CtxEntry("Ok/x.y:z-1",NPLAudio.SpatialFabric.Editor.ContextKind.Point),CtxEntry("",NPLAudio.SpatialFabric.Editor.ContextKind.Point)});
    var ids=JsonDocument.Parse(r.Json).RootElement.GetProperty("objects").EnumerateArray().Select(o=>o.GetProperty("id").GetString()).ToArray();
    Assert(string.Join(",",ids)=="a_b_c,a_b_c-2,a_b_c-3,Ok/x.y:z-1,object"&&r.Report.Exported==5);
});
Check("context id/label truncation",()=>
{
    string longId=new string('a',200);
    var r=NPLAudio.SpatialFabric.Editor.ContextExportCore.Build(new string('n',300),new[]{CtxEntry(longId,NPLAudio.SpatialFabric.Editor.ContextKind.Point,label:new string('L',300)),CtxEntry(longId,NPLAudio.SpatialFabric.Editor.ContextKind.Point)});
    var root=JsonDocument.Parse(r.Json).RootElement;var o=root.GetProperty("objects");
    Assert(o[0].GetProperty("id").GetString()==new string('a',128)&&o[1].GetProperty("id").GetString()==new string('a',126)+"-2");
    Assert(o[0].GetProperty("label").GetString().Length==256&&root.GetProperty("name").GetString().Length==256&&r.Report.TruncatedText==2);
});
Check("context non-finite skipped, clamping counted",()=>
{
    var K=NPLAudio.SpatialFabric.Editor.ContextKind.Box;
    var nan=CtxEntry("nan",K,double.NaN);var inf=CtxEntry("inf",K,0,double.PositiveInfinity);var ext=CtxEntry("ext",K);ext.HalfExtents[1]=double.NaN;
    var big=CtxEntry("big",K,100,-100,5);big.HalfExtents[0]=99;big.HalfExtents[1]=-3;
    var sph=CtxEntry("s",NPLAudio.SpatialFabric.Editor.ContextKind.Sphere);sph.Radius=70;
    var ok=CtxEntry("ok",K,64,-64,0);
    var r=NPLAudio.SpatialFabric.Editor.ContextExportCore.Build("n",new[]{nan,inf,ext,big,sph,ok});
    Assert(r.Report.SkippedNonFinite==3&&r.Report.Exported==3&&r.Report.Clamped==2&&r.Report.TruncatedByCap==0);
    var o=JsonDocument.Parse(r.Json).RootElement.GetProperty("objects");
    Assert(o[0].GetProperty("position")[0].GetDouble()==64&&o[0].GetProperty("position")[1].GetDouble()==-64&&o[0].GetProperty("half_extents")[0].GetDouble()==64&&o[0].GetProperty("half_extents")[1].GetDouble()==0&&o[1].GetProperty("radius").GetDouble()==64);
});
Check("context 4096 cap",()=>
{
    var list=Enumerable.Range(0,4100).Select(i=>CtxEntry("p"+i,NPLAudio.SpatialFabric.Editor.ContextKind.Point)).ToList();
    var r=NPLAudio.SpatialFabric.Editor.ContextExportCore.Build("n",list);
    Assert(r.Report.Exported==4096&&r.Report.TruncatedByCap==4&&JsonDocument.Parse(r.Json).RootElement.GetProperty("objects").GetArrayLength()==4096);
});
Check("context JSON string escaping",()=>
{
    string s="q\"b\\n\n\r\t\b\f\u0001\u001f é日🎧";
    var r=NPLAudio.SpatialFabric.Editor.ContextExportCore.Build("n",new[]{CtxEntry("x",NPLAudio.SpatialFabric.Editor.ContextKind.Point,label:s)});
    Assert(r.Json.Contains("\\\"b\\\\n\\n\\r\\t\\b\\f\\u0001\\u001f é日🎧"));
    Assert(JsonDocument.Parse(r.Json).RootElement.GetProperty("objects")[0].GetProperty("label").GetString()==s);
    Assert(NPLAudio.SpatialFabric.Editor.ContextExportCore.Quote("\ud800x")=="\"\ufffdx\"");
});
List<NPLAudio.SpatialFabric.Editor.ContextEntry> CtxSample()
{
    var K=NPLAudio.SpatialFabric.Editor.ContextKind.Box;
    var box=CtxEntry("wall a",K,1.15,0,0.5,"Wall \"A\"");box.HalfExtents=new[]{0.05,1.2,0.6};
    var sph=CtxEntry("fountain",NPLAudio.SpatialFabric.Editor.ContextKind.Sphere);sph.Radius=.25;
    var pt=CtxEntry("sp-fl",NPLAudio.SpatialFabric.Editor.ContextKind.Point,1,-1,0.8);
    var ls=CtxEntry("listener",NPLAudio.SpatialFabric.Editor.ContextKind.Listener,-0.6,0,0.15);ls.YawDegrees=-0.0;
    return new(){box,sph,pt,ls};
}
Check("context exact 4-object document",()=>
{
    string expected=string.Join("\n",new[]{
        "{","  \"schema\": \"org.nplaudio.spatialfabric.context\",","  \"schema_version\": 1,","  \"space\": \"canonical\",","  \"name\": \"Test\",","  \"objects\": [",
        "    {","      \"id\": \"wall_a\",","      \"kind\": \"box\",","      \"label\": \"Wall \\\"A\\\"\",","      \"position\": [1.15, 0, 0.5],","      \"half_extents\": [0.05, 1.2, 0.6]","    },",
        "    {","      \"id\": \"fountain\",","      \"kind\": \"sphere\",","      \"position\": [0, 0, 0],","      \"radius\": 0.25","    },",
        "    {","      \"id\": \"sp-fl\",","      \"kind\": \"point\",","      \"position\": [1, -1, 0.8]","    },",
        "    {","      \"id\": \"listener\",","      \"kind\": \"listener\",","      \"position\": [-0.6, 0, 0.15],","      \"yaw_degrees\": 0","    }","  ]","}",""});
    var r=NPLAudio.SpatialFabric.Editor.ContextExportCore.Build("Test",CtxSample());
    Assert(r.Json==expected&&!r.Json.Contains('\r')&&r.Report.Exported==4&&r.Report.Clamped==0);
    using var doc=JsonDocument.Parse(r.Json);Assert(doc.RootElement.GetProperty("objects").GetArrayLength()==4);
    string outPath=Environment.GetEnvironmentVariable("SF_CONTEXT_SAMPLE_OUT");
    if(!string.IsNullOrEmpty(outPath)) File.WriteAllText(outPath,r.Json,new System.Text.UTF8Encoding(false));
});
Check("context empty document is valid JSON",()=>Assert(JsonDocument.Parse(NPLAudio.SpatialFabric.Editor.ContextExportCore.Build("",new NPLAudio.SpatialFabric.Editor.ContextEntry[0]).Json).RootElement.GetProperty("objects").GetArrayLength()==0));
// ---- Path v1 (baked path playback) ----
string pathFixturePath=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args.Length > 0 ? args[0] : "../SpatialFabric-Interop/Fixtures/osc-v1.json"))??".","path-v1.json");
using var pathFixture=JsonDocument.Parse(File.ReadAllText(pathFixturePath));
double pathTol=pathFixture.RootElement.GetProperty("tolerance").GetDouble();
foreach(var c in pathFixture.RootElement.GetProperty("cases").EnumerateArray())
{
    string caseName=c.GetProperty("name").GetString();
    Check("path case "+caseName,()=>
    {
        var doc=PathDocument.TryLoad(c.GetProperty("document").GetRawText(),out var problems);
        Assert(doc!=null&&problems.Count==0);
        foreach(var s in c.GetProperty("samples").EnumerateArray())
        {
            var r=PathSampler.Sample(doc,s.GetProperty("track").GetString(),s.GetProperty("time").GetDouble());
            var p=s.GetProperty("position");
            Assert(Math.Abs(r.Front-p[0].GetDouble())<=pathTol&&Math.Abs(r.Right-p[1].GetDouble())<=pathTol&&Math.Abs(r.Up-p[2].GetDouble())<=pathTol);
            if(s.TryGetProperty("parameters",out var prm))
            {
                foreach(var kv in prm.EnumerateObject())
                {
                    double? got=kv.Name=="gain"?r.Gain:kv.Name=="spread"?r.Spread:null;
                    Assert(got.HasValue&&Math.Abs(got.Value-kv.Value.GetDouble())<=pathTol);
                }
            }
            else Assert(!r.Gain.HasValue||doc.FindTrack(s.GetProperty("track").GetString()).Gain!=null);
        }
    });
}
foreach(var c in pathFixture.RootElement.GetProperty("invalid").EnumerateArray())
{
    Check("path invalid "+c.GetProperty("name").GetString(),()=>
    {
        var expected=c.GetProperty("codes").EnumerateArray().Select(e=>e.GetString()).OrderBy(e=>e,StringComparer.Ordinal).ToList();
        var got=PathValidator.Validate(c.GetProperty("document").GetRawText()).Select(e=>e.Code).Distinct().OrderBy(e=>e,StringComparer.Ordinal).ToList();
        Assert(expected.SequenceEqual(got));
        Assert(PathDocument.TryLoad(c.GetProperty("document").GetRawText(),out _)==null);
    });
}
string PathMinimal(string track="")=>"{\"schema\":\"org.nplaudio.spatialfabric.path\",\"schema_version\":1,\"space\":\"canonical\",\"tracks\":[{\"id\":\"t\",\"target\":{\"kind\":\"object\",\"key\":\"k\"}"+track+",\"keyframes\":[{\"t\":0,\"position\":[0,0,0]},{\"t\":2,\"position\":[2,4,0]}]}]}";
List<string> PathCodes(string json)=>PathValidator.Validate(json).Select(e=>e.Code).ToList();
Check("path minimal document valid",()=>Assert(PathCodes(PathMinimal()).Count==0));
Check("path schema_version 1.0 accepted but true rejected",()=>{Assert(PathCodes(PathMinimal().Replace("\"schema_version\":1","\"schema_version\":1.0")).Count==0);Assert(PathCodes(PathMinimal().Replace("\"schema_version\":1","\"schema_version\":true")).SequenceEqual(new[]{"bad_version"}));});
Check("path target label length and object_id rules",()=>
{
    string T(string extra)=>PathMinimal().Replace("\"key\":\"k\"","\"key\":\"k\""+extra);
    Assert(PathCodes(T(",\"label\":\""+new string('a',256)+"\",\"object_id\":7")).Count==0);
    Assert(PathCodes(T(",\"label\":\""+new string('a',257)+"\"")).SequenceEqual(new[]{"bad_target"}));
    foreach(string bad in new[]{",\"object_id\":0",",\"object_id\":1.0",",\"object_id\":\"1\"",",\"object_id\":null",",\"label\":3",",\"label\":null"})Assert(PathCodes(T(bad)).SequenceEqual(new[]{"bad_target"}));
    Assert(PathCodes(T(",\"label\":\""+string.Concat(Enumerable.Repeat("\U0001F3A7",256))+"\"")).Count==0);
});
Check("path limits: tracks, keyframes, ids",()=>
{
    string track(int i)=>"{\"id\":\"t"+i+"\",\"target\":{\"kind\":\"camera\",\"key\":\"k\"},\"keyframes\":[{\"t\":0,\"position\":[0,0,0]}]}";
    string docOf(int n)=>"{\"schema\":\"org.nplaudio.spatialfabric.path\",\"schema_version\":1,\"space\":\"canonical\",\"tracks\":["+string.Join(",",Enumerable.Range(0,n).Select(track))+"]}";
    Assert(PathCodes(docOf(256)).Count==0&&PathCodes(docOf(257)).SequenceEqual(new[]{"too_many_tracks"}));
    string keys(int n)=>"["+string.Join(",",Enumerable.Range(0,n).Select(i=>"{\"t\":"+i+",\"position\":[0,0,0]}"))+"]";
    string kdoc(int n)=>"{\"schema\":\"org.nplaudio.spatialfabric.path\",\"schema_version\":1,\"space\":\"canonical\",\"tracks\":[{\"id\":\"t\",\"target\":{\"kind\":\"object\",\"key\":\"k\"},\"keyframes\":"+keys(n)+"}]}";
    Assert(PathCodes(kdoc(10000)).Count==0&&PathCodes(kdoc(10001)).SequenceEqual(new[]{"too_many_keyframes"}));
    Assert(PathValidator.IsId(new string('a',64))&&!PathValidator.IsId(new string('a',65))&&!PathValidator.IsId("")&&!PathValidator.IsId("a b")&&!PathValidator.IsId("a\n")&&PathValidator.IsId("A_z-9"));
});
Check("path root not an object",()=>{foreach(string j in new[]{"[]","1","\"x\"","null","true"})Assert(PathCodes(j).SequenceEqual(new[]{"bad_schema"}));Assert(PathValidator.Validate((JsonNode)null).Count==1);});
Check("path sampler extra semantics",()=>
{
    var doc=PathDocument.TryLoad(PathMinimal(",\"loop\":\"loop\",\"speed\":2"),out _);
    var r=PathSampler.Sample(doc,"t",0.5);Assert(Near(r.Front,1)&&Near(r.Right,2));     // tau=1
    r=PathSampler.Sample(doc,"t",1);Assert(Near(r.Front,0)&&Near(r.Right,0));           // tau=2 == tN -> first key
    r=PathSampler.Sample(doc,"t",1.25);Assert(Near(r.Front,.5)&&Near(r.Right,1));       // tau=2.5 -> .5
    r=PathSampler.Sample(doc,"t",-5);Assert(Near(r.Front,0));
    r=PathSampler.Sample(doc,"t",double.NaN);Assert(Near(r.Front,0));
    r=PathSampler.Sample(doc,"t",double.PositiveInfinity);Assert(!double.IsNaN(r.Front));
    var pp=PathDocument.TryLoad(PathMinimal(",\"loop\":\"ping_pong\""),out _);
    r=PathSampler.Sample(pp,"t",3);Assert(Near(r.Front,1)&&Near(r.Right,2));             // reflect: m=3>2 -> 1
    bool threw=false;try{PathSampler.Sample(doc,"nope",0);}catch(ArgumentException){threw=true;}Assert(threw);
    Assert(doc.FindTrack("t").Duration==1);                                              // 2s span / speed 2
});
Check("path parameters absent -> null; spread",()=>
{
    var doc=PathDocument.TryLoad(PathMinimal(",\"parameters\":{\"spread\":[{\"t\":0,\"value\":0},{\"t\":2,\"value\":1}],\"other\":5}"),out var pr);
    Assert(doc!=null);var r=PathSampler.Sample(doc,"t",1);Assert(!r.Gain.HasValue&&Near(r.Spread.Value,.5));
});
Check("path loaded document resolves waypoints, defaults and ignores unknown fields",()=>
{
    string j="{\"schema\":\"org.nplaudio.spatialfabric.path\",\"schema_version\":1,\"space\":\"canonical\",\"extra\":{\"x\":[1]},\"name\":\"n\",\"waypoints\":[{\"id\":\"w\",\"position\":[1,2,3]}],\"tracks\":[{\"id\":\"t\",\"target\":{\"kind\":\"listener\",\"key\":\"k\",\"label\":\"L\",\"object_id\":3},\"keyframes\":[{\"t\":0,\"waypoint\":\"w\",\"future\":1}]}]}";
    var doc=PathDocument.TryLoad(j,out var pr);Assert(doc!=null&&doc.Name=="n");var t=doc.Tracks[0];
    Assert(t.TargetKind=="listener"&&t.TargetLabel=="L"&&t.TargetObjectId==3&&t.Loop==PathLoop.None&&t.Speed==1);
    var s=PathSampler.Sample(doc,"t",9);Assert(s.Front==1&&s.Right==2&&s.Up==3);
});
Check("path json parser accepts valid documents and BOM",()=>
{
    var n=PathJson.Parse(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(" {\"a\":[1,-2.5e1,\"\\u00e9\\ud83c\\udfa7\\n\",true,false,null,{}]} ")).ToArray());
    var a=n.Get("a").Items;Assert(a[0].IsInteger&&a[0].Number==1&&!a[1].IsInteger&&a[1].Number==-25&&a[2].String=="é\U0001F3A7\n"&&a[3].Bool&&a[5].Kind==JsonKind.Null&&a[6].IsObject);
    Assert(PathJson.Parse("\uFEFF[]").IsArray);
});
Check("path json parser rejects malformed text",()=>
{
    foreach(string bad in new[]{"","  ","{","[1,]","{\"a\":1,}","{\"a\"}","{a:1}","[01]","[1.]","[.5]","[-]","[1e]","[+1]","tru","nul","'a'","\"\\x\"","\"\\u12\"","\"\\ud800\"","\"\\udc00\"","\"\\ud800\\u0041\"","\"a\nb\"","[1] x","{} {}","[1]]","\"abc","/*c*/[]","[1 2]","NaN","Infinity","[-Infinity]"})
    {
        bool rejected=false;try{PathJson.Parse(bad);}catch(FormatException){rejected=true;}Assert(rejected);
    }
});
Check("path json parser rejects non-finite (overflowing) numbers",()=>{foreach(string bad in new[]{"[1e999]","[-1e999]","[1e400]"})Reject(()=>PathJson.Parse(bad));Assert(PathJson.Parse("[1e-999]").Items[0].Number==0);});
Check("path json parser rejects duplicate keys (also nested)",()=>{Reject(()=>PathJson.Parse("{\"a\":1,\"a\":2}"));Reject(()=>PathJson.Parse("{\"o\":{\"b\":1,\"b\":1}}"));Reject(()=>PathJson.Parse("{\"a\":1,\"\\u0061\":2}"));Assert(PathJson.Parse("{\"a\":{\"a\":1}}").Get("a").Get("a").Number==1);});
Check("path json parser depth limit 32",()=>
{
    string Nest(int d)=>new string('[',d)+new string(']',d);
    Assert(PathJson.Parse(Nest(32)).IsArray);Reject(()=>PathJson.Parse(Nest(33)));
    string Obj(int d){var sb=new StringBuilder();for(int i=0;i<d;++i)sb.Append("{\"a\":");sb.Append('1');sb.Append('}',d);return sb.ToString();}
    Assert(PathJson.Parse(Obj(32)).IsObject);Reject(()=>PathJson.Parse(Obj(33)));
    Reject(()=>PathJson.Parse(new string('[',200000)));
});
Check("path json parser size limit 16 MiB",()=>
{
    var ok=new byte[PathJson.MaxBytes];Array.Fill(ok,(byte)' ');ok[0]=(byte)'1';Assert(PathJson.Parse(ok).Number==1);
    Reject(()=>PathJson.Parse(new byte[PathJson.MaxBytes+1]));
    Reject(()=>PathJson.Parse(new string(' ',PathJson.MaxBytes+1)));
    Reject(()=>PathJson.Parse("\"" +new string('\u00e9',PathJson.MaxBytes/2)+"\""));    // 2 bytes per char: over the byte limit
});
Check("path json parser rejects invalid UTF-8 and truncation",()=>
{
    foreach(var bytes in new[]{new byte[]{0x5b,0x22,0xff,0x22,0x5d},new byte[]{0x5b,0x22,0xc3,0x22,0x5d},new byte[]{0x5b,0x22,0xed,0xa0,0x80,0x22,0x5d},new byte[]{0x5b,0x22,0xc0,0x80,0x22,0x5d}})Reject(()=>PathJson.Parse(bytes));
    var full=Encoding.UTF8.GetBytes(PathMinimal(",\"loop\":\"loop\""));
    for(int i=0;i<full.Length;++i){int n=i;Reject(()=>PathJson.Parse(full.Take(n).ToArray()));}
    var utf=Encoding.UTF8.GetBytes("[\"\u00e9\u3042\U0001F3A7\"]");
    for(int i=1;i<utf.Length;++i){int n=i;Reject(()=>PathJson.Parse(utf.Take(n).ToArray()));}
    Reject(()=>PathJson.Parse((byte[])null));Reject(()=>PathJson.Parse((string)null));
});
Check("path stage math hand values",()=>
{
    var f=new PathStageFrame(20,15,8);
    var l=PathStageMath.ToLocal(f,.5,.2,.5);Assert(Near(l.x,1.5)&&Near(l.y,2)&&Near(l.z,5));
    l=PathStageMath.ToLocal(new PathStageFrame(20,15,8,true,false,false),.5,.2,.5);Assert(Near(l.x,1.5)&&Near(l.y,2)&&Near(l.z,-5));
    l=PathStageMath.ToLocal(new PathStageFrame(20,15,8,false,true,false),.5,.2,.5);Assert(Near(l.x,-1.5)&&Near(l.y,2)&&Near(l.z,5));
    l=PathStageMath.ToLocal(new PathStageFrame(20,15,8,false,false,true),.5,.2,.5);Assert(Near(l.x,1.5)&&Near(l.y,-2)&&Near(l.z,5));
    l=PathStageMath.ToLocal(new PathStageFrame(0,.02,0),20,2,10);Assert(Near(l.z,1)&&Near(l.x,.1)&&Near(l.y,.5));   // .05 min half extent
    l=PathStageMath.ToLocal(f,1,1,1);Assert(Near(l.x,7.5)&&Near(l.y,4)&&Near(l.z,10));                                // canonical [front,right,up] -> x=right,y=up,z=front
});
Check("path stage math round-trips ContextExportCore.ToCanonical",()=>
{
    var rnd=new Random(7);
    foreach(var dims in new[]{(20.0,15.0,8.0),(1.0,2.0,3.0),(0.0,.02,.1),(100.0,.5,40.0)})
    foreach(int mask in Enumerable.Range(0,8))
    {
        var pf=new PathStageFrame(dims.Item1,dims.Item2,dims.Item3,(mask&1)!=0,(mask&2)!=0,(mask&4)!=0);
        var cf=new NPLAudio.SpatialFabric.Editor.ContextStageFrame(dims.Item1,dims.Item2,dims.Item3,(mask&1)!=0,(mask&2)!=0,(mask&4)!=0);
        for(int k=0;k<5;++k)
        {
            double f=rnd.NextDouble()*4-2,r=rnd.NextDouble()*4-2,u=rnd.NextDouble()*4-2;
            var local=PathStageMath.ToLocal(pf,f,r,u);var back=NPLAudio.SpatialFabric.Editor.ContextExportCore.ToCanonical(cf,local.x,local.y,local.z);
            Assert(Math.Abs(back.front-f)<1e-9&&Math.Abs(back.right-r)<1e-9&&Math.Abs(back.up-u)<1e-9);
        }
    }
});
Check("path validate/load never throw on ~200 deterministic mutations",()=>
{
    string seed="{\"schema\":\"org.nplaudio.spatialfabric.path\",\"schema_version\":1,\"name\":\"n\",\"space\":\"canonical\",\"waypoints\":[{\"id\":\"w\",\"position\":[0,1,0]}],\"tracks\":[{\"id\":\"t\",\"target\":{\"kind\":\"object\",\"key\":\"k\",\"label\":\"l\",\"object_id\":2},\"loop\":\"ping_pong\",\"speed\":1.5,\"keyframes\":[{\"t\":0,\"position\":[0,0,0],\"interp\":\"bezier\",\"ease\":\"in\",\"tangent_out\":[1,0,0]},{\"t\":1,\"waypoint\":\"w\",\"interp\":\"catmull_rom\",\"tangent_in\":[0,1,0]},{\"t\":2,\"position\":[1,1,1]}],\"parameters\":{\"gain\":[{\"t\":0,\"value\":0},{\"t\":2,\"value\":1}],\"spread\":[{\"t\":0,\"value\":0.5}]}}]}";
    Assert(PathCodes(seed).Count==0);
    var rnd=new Random(20240601);
    JsonNode[] repl={new JsonNode{Kind=JsonKind.Null},new JsonNode{Kind=JsonKind.Bool,Bool=true},new JsonNode{Kind=JsonKind.Number,Number=-1,IsInteger=true},new JsonNode{Kind=JsonKind.Number,Number=1e300},new JsonNode{Kind=JsonKind.Number,Number=double.NaN},new JsonNode{Kind=JsonKind.Number,Number=double.PositiveInfinity},new JsonNode{Kind=JsonKind.String,String=""},new JsonNode{Kind=JsonKind.String,String="w"},new JsonNode{Kind=JsonKind.Array,Items=new List<JsonNode>()},new JsonNode{Kind=JsonKind.Object,Members=new Dictionary<string,JsonNode>()}};
    void Collect(JsonNode n,List<JsonNode> all){all.Add(n);if(n.IsArray)foreach(var c in n.Items)Collect(c,all);else if(n.IsObject)foreach(var c in n.Members.Values)Collect(c,all);}
    int valid=0;
    for(int i=0;i<200;++i)
    {
        string text=seed;
        if(i%2==0)  // text-level: delete / replace / insert a character
        {
            int pos=rnd.Next(text.Length);
            text=rnd.Next(3)==0?text.Remove(pos,1):rnd.Next(2)==0?text.Remove(pos,1).Insert(pos,"{[]}\",:0-e. xtn"[rnd.Next(15)].ToString()):text.Insert(pos,"{[]}\",:0-e. xtn"[rnd.Next(15)].ToString());
            var problems=PathValidator.Validate(text);if(problems.Count==0)++valid;
            var d=PathDocument.TryLoad(text,out _);if(d!=null)foreach(var t in d.Tracks)for(double time=-1;time<6;time+=.7){var s=PathSampler.Sample(t,time);Assert(!double.IsNaN(s.Front+s.Right+s.Up));}
        }
        else        // tree-level: type-confuse a random node, optionally drop a member
        {
            var root=PathJson.Parse(seed);var all=new List<JsonNode>();Collect(root,all);
            var victim=all[rnd.Next(all.Count)];var r=repl[rnd.Next(repl.Length)];
            if(rnd.Next(4)==0&&victim.IsObject&&victim.Members.Count>0)victim.Members.Remove(victim.Members.Keys.ElementAt(rnd.Next(victim.Members.Count)));
            else{victim.Kind=r.Kind;victim.Bool=r.Bool;victim.Number=r.Number;victim.IsInteger=r.IsInteger;victim.String=r.String;victim.Items=r.Items==null?null:new List<JsonNode>();victim.Members=r.Members==null?null:new Dictionary<string,JsonNode>();}
            var problems=PathValidator.Validate(root);if(problems.Count==0)++valid;
            var d=PathDocument.TryLoad(root,out _);if(d!=null)foreach(var t in d.Tracks)for(double time=-1;time<6;time+=.7){var s=PathSampler.Sample(t,time);Assert(!double.IsNaN(s.Front+s.Right+s.Up));}
        }
    }
    Assert(valid<200);
});
Console.WriteLine($"{passed} core checks passed");
