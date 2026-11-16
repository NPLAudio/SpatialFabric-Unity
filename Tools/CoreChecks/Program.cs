// No test/NuGet dependencies; compile the actual UPM transport sources under .NET.
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using NPLAudio.SpatialFabric.Osc;
using NPLAudio.SpatialFabric.Audio;
using NPLAudio.SpatialFabric.Protocols;
using NPLAudio.SpatialFabric.Routing;

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
Console.WriteLine($"{passed} core checks passed");
