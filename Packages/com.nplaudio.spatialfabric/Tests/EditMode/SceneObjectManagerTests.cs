// Actual Unity EditMode regressions for loaded-scene authoring. No device/audio/network starts. MIT.
using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using UnityEngine.TestTools;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using NPLAudio.SpatialFabric.Editor;
namespace NPLAudio.SpatialFabric.Tests
{
    public sealed class SceneObjectManagerPlayGuardTests
    {
        [UnityTest] public IEnumerator PlayModeRefusesComponentAdditionAndSerializedApply()
        {
            yield return new EnterPlayMode();
            AssertPlayGuard();
            yield return new ExitPlayMode();
        }
        [UnityTearDown] public IEnumerator LeavePlayModeAfterFailure()
        {
            if(EditorApplication.isPlaying)yield return new ExitPlayMode();
        }
        static void AssertPlayGuard()
        {
            var obj=new GameObject("Play guard");
            try
            {
                Assert.Throws<InvalidOperationException>(()=>SceneObjectAuthoring.AddComponents(new[]{obj},typeof(PlaybackCue)));
                Assert.That(obj.GetComponent<PlaybackCue>(),Is.Null);
                var component=obj.AddComponent<SpatialObject>();
                using(var serialized=new SerializedObject(component))
                {
                    serialized.Update();serialized.FindProperty("gain").floatValue=.2f;
                    Assert.Throws<InvalidOperationException>(()=>SceneObjectAuthoring.Apply(serialized));
                    Assert.That(component.gain,Is.EqualTo(1));
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(obj);}
        }
    }
    public sealed class SceneObjectManagerTests
    {
        Scene scene,other,previous;
        string folder;
        [SetUp] public void Setup()
        {
            previous=SceneManager.GetActiveScene();
            folder="Assets/SceneObjectManagerTests_"+Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets",folder.Substring("Assets/".Length));
            // Open saved empty scenes additively: NewScene(Additive) rejects the runner's
            // untitled scene. Never replace or save that scene (or a user's open scene).
            scene=OpenEmptyScene("First");other=OpenEmptyScene("Second");
            SceneManager.SetActiveScene(scene);
        }
        [TearDown] public void Cleanup()
        {
            Selection.objects=Array.Empty<UnityEngine.Object>();
            Undo.ClearAll();
            if(previous.IsValid() && previous.isLoaded)SceneManager.SetActiveScene(previous);
            if(scene.IsValid() && scene.isLoaded)EditorSceneManager.CloseScene(scene,true);
            if(other.IsValid() && other.isLoaded)EditorSceneManager.CloseScene(other,true);
            if(!string.IsNullOrEmpty(folder))AssetDatabase.DeleteAsset(folder);
        }
        Scene OpenEmptyScene(string name)
        {
            string path=folder+"/"+name+".unity";
            File.WriteAllText(path,"%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!1660057539 &9223372036854775807\nSceneRoots:\n  m_ObjectHideFlags: 0\n  m_Roots: []\n");
            AssetDatabase.ImportAsset(path);
            return EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
        }
        GameObject Make(string name,Scene destination=default)
        {
            var obj=new GameObject(name);
            SceneManager.MoveGameObjectToScene(obj,destination.IsValid()?destination:scene);return obj;
        }
        static object Panel(SpatialFabricWindow window)=>typeof(SpatialFabricWindow).GetField("sceneObjects",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
        static T PanelValue<T>(object panel,string name)=>(T)panel.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(panel);
        static void SetPanelValue(object panel,string name,object value)=>panel.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(panel,value);
        static IEnumerator AwaitPaint(Func<bool> condition,string message)
        {
            double deadline=EditorApplication.timeSinceStartup+5;
            while(EditorApplication.timeSinceStartup<deadline){yield return null;if(condition())yield break;}
            Assert.Fail(message);
        }
        [UnityTest] public IEnumerator ActualWindowFollowsHierarchyAndPaintsLiveSceneChangesWithoutMouseInput()
        {
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)Assert.Ignore("Requires graphics for actual window rendering");
            var obj=Make("LiveSceneProbe_"+Guid.NewGuid().ToString("N"));var spatial=obj.AddComponent<SpatialObject>();
            spatial.objectId=417;Selection.activeGameObject=obj;
            var window=ScriptableObject.CreateInstance<SpatialFabricWindow>();
            try
            {
                var panel=Panel(window);SetPanelValue(panel,"search",obj.name);
                window.position=new Rect(50,50,1000,900);window.Show();window.Repaint();
                var painted=PanelValue<Dictionary<GameObject,string>>(panel,"paintedRows");
                var properties=PanelValue<HashSet<string>>(panel,"paintedProperties");
                yield return AwaitPaint(()=>painted.ContainsKey(obj) && properties.Contains(typeof(SpatialObject).FullName+".objectId"),"Actual window did not paint selected scene object and its existing properties");
                Assert.That(PanelValue<Rect>(panel,"lastViewport").width,Is.GreaterThan(0));
                Assert.That(PanelValue<Rect>(panel,"lastViewport").height,Is.GreaterThan(0));
                var targets=PanelValue<Dictionary<Type,UnityEngine.Object[]>>(panel,"propertyTargets");
                Assert.That(targets[typeof(SpatialObject)],Is.EqualTo(new UnityEngine.Object[]{spatial}));
                spatial.objectId=918;EditorUtility.SetDirty(spatial);
                using(var serialized=SceneObjectAuthoring.SerializedSelection(new[]{obj},typeof(SpatialObject)))
                {serialized.Update();Assert.That(serialized.FindProperty("objectId").intValue,Is.EqualTo(918));}
                obj.name+="_Renamed";
                yield return AwaitPaint(()=>painted.TryGetValue(obj,out var label) && label.Contains("_Renamed"),"Idle repaint did not show renamed live scene object");
                SceneObjectAuthoring.AddComponents(new[]{obj},typeof(PlaybackCue));
                yield return AwaitPaint(()=>painted.TryGetValue(obj,out var label) && label.Contains("PlaybackCue") && properties.Contains(typeof(PlaybackCue).FullName+".outputChannel"),"New scene component did not appear in actual window");
                Undo.PerformUndo();
                yield return AwaitPaint(()=>painted.TryGetValue(obj,out var label) && !label.Contains("PlaybackCue") && !properties.Contains(typeof(PlaybackCue).FullName+".outputChannel"),"Undo did not remove component from painted scene data");
                var otherObj=Make(obj.name+"_Other");var otherSpatial=otherObj.AddComponent<SpatialObject>();
                Selection.activeGameObject=otherObj;
                yield return AwaitPaint(()=>PanelValue<HashSet<GameObject>>(panel,"selected").SetEquals(new[]{otherObj}) && targets.TryGetValue(typeof(SpatialObject),out var values) && values.SequenceEqual(new UnityEngine.Object[]{otherSpatial}),"Hierarchy selection change did not bind real scene component");
                UnityEngine.Object.DestroyImmediate(otherObj);
                yield return AwaitPaint(()=>!PanelValue<SceneObjectViewCache>(panel,"cache").Objects.Contains(otherObj) && targets.Count==0,"Deleted selected object left stale live controls");
                LogAssert.NoUnexpectedReceived();
            }
            finally{window.Close();}
        }
        [UnityTest] public IEnumerator ActualRowLabelClickSelectsAndOpensExistingProperties()
        {
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)Assert.Ignore("Requires graphics for actual window rendering");
            var obj=Make("ClickableSceneProbe_"+Guid.NewGuid().ToString("N"));obj.AddComponent<SpatialObject>();Selection.objects=Array.Empty<UnityEngine.Object>();
            var window=ScriptableObject.CreateInstance<SpatialFabricWindow>();
            try
            {
                var panel=Panel(window);SetPanelValue(panel,"search",obj.name);
                window.position=new Rect(50,50,1000,900);window.Show();window.Repaint();
                yield return AwaitPaint(()=>PanelValue<Dictionary<GameObject,string>>(panel,"paintedRows").ContainsKey(obj),"Scene row was never painted");
                // SendEvent coordinates include the host title bar; use the actual
                // painted screen rectangle rather than assuming its platform/DPI height.
                var screen=PanelValue<Dictionary<GameObject,Rect>>(panel,"paintedRowScreens")[obj];
                var point=screen.center-window.position.position;
                window.SendEvent(new Event{type=EventType.MouseDown,button=0,mousePosition=point});
                window.SendEvent(new Event{type=EventType.MouseUp,button=0,mousePosition=point});
                yield return AwaitPaint(()=>PanelValue<HashSet<GameObject>>(panel,"selected").Contains(obj) && PanelValue<HashSet<string>>(panel,"paintedProperties").Contains(typeof(SpatialObject).FullName+".objectId"),"Clicking row label did not select and expose actual component values");
                Assert.That(PanelValue<bool>(panel,"followHierarchy"),Is.False);
                LogAssert.NoUnexpectedReceived();
            }
            finally{window.Close();}
        }
        [Test] public void ManagerDiscoveryIsReadOnlyAndRejectsAmbiguityAndStaleContext()
        {
            var a=Make("Manager A").AddComponent<SpatialFabricManager>();
            var b=Make("Manager B").AddComponent<SpatialFabricManager>();
            var unrelated=Make("Ordinary selected object");
            Assert.That(SceneObjectAuthoring.ResolveManager(new[]{a},null,unrelated),Is.SameAs(a));
            Assert.That(SceneObjectAuthoring.ResolveManager(new[]{a,b},null,unrelated),Is.Null);
            Assert.That(SceneObjectAuthoring.ResolveManager(new[]{a,b},a,b.gameObject),Is.SameAs(b));
            Assert.That(SceneObjectAuthoring.ResolveManager(new[]{a,b},b,unrelated),Is.SameAs(b));
            Assert.That(SceneObjectAuthoring.ResolveManager(new[]{a},b,unrelated),Is.SameAs(a));
            Assert.That(SceneObjectAuthoring.ResolveManager(Array.Empty<SpatialFabricManager>(),a,unrelated),Is.Null);
            Assert.That(a.objects,Is.Empty);Assert.That(a.stage,Is.Null);Assert.That(a.IsListening,Is.False);
        }
        [UnityTest] public IEnumerator OperatorTabsDiscoverManagerAndPaintExistingUnboundComponents()
        {
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)Assert.Ignore("Requires graphics for actual window rendering");
            var owner=Make("Existing manager").AddComponent<SpatialFabricManager>();
            var stage=Make("Existing unassigned stage").AddComponent<SpatialStage>();
            var spatial=Make("Existing unbound spatial object").AddComponent<SpatialObject>();spatial.objectId=812;
            var cue=Make("Existing inactive playback cue").AddComponent<PlaybackCue>();cue.gameObject.SetActive(false);
            Selection.activeGameObject=Make("Ordinary selected object");
            var window=ScriptableObject.CreateInstance<SpatialFabricWindow>();
            try
            {
                var expanded=PanelValue<Dictionary<UnityEngine.Object,bool>>(window,"expandedTargets");
                expanded[stage]=true;expanded[spatial]=true;expanded[cue]=true;
                SetPanelValue(window,"networkOutputsExpanded",true);
                window.position=new Rect(50,50,1000,900);window.Show();
                var targets=PanelValue<HashSet<UnityEngine.Object>>(window,"paintedOperatorTargets");
                var expected=new UnityEngine.Object[]{stage,spatial,owner,cue};
                for(int i=0;i<4;i++)
                {
                    SetPanelValue(window,"tab",i);window.Repaint();
                    var target=expected[i];yield return AwaitPaint(()=>targets.Contains(target),"Operator tab "+i+" did not paint actual scene data");
                }
                Assert.That(PanelValue<SpatialFabricManager>(window,"manager"),Is.SameAs(owner));
                Assert.That(owner.stage,Is.Null);Assert.That(owner.objects,Is.Empty);Assert.That(owner.IsListening,Is.False);
                Assert.That(spatial.objectId,Is.EqualTo(812));Assert.That(cue.IsPlaying,Is.False);Assert.That(cue.ReplayPending,Is.False);
                // Manual context is pinned even when another manager remains selected.
                var second=Make("Second manager").AddComponent<SpatialFabricManager>();Selection.activeGameObject=owner.gameObject;
                SetPanelValue(window,"manager",second);SetPanelValue(window,"autoFindManager",false);SetPanelValue(window,"contextDirty",true);
                yield return AwaitPaint(()=>PanelValue<SpatialFabricManager[]>(window,"sceneManagers").Contains(second),"New manager did not appear in context snapshot");
                Assert.That(PanelValue<SpatialFabricManager>(window,"manager"),Is.SameAs(second));
                // Real scene changes appear while the object tab stays open.
                var added=Make("Added while window open").AddComponent<SpatialObject>();expanded[added]=true;
                SetPanelValue(window,"tab",1);window.Repaint();
                yield return AwaitPaint(()=>targets.Contains(added),"New scene object did not populate the open Objects tab");
                UnityEngine.Object.DestroyImmediate(added.gameObject);
                yield return AwaitPaint(()=>!targets.Contains(added),"Deleted object remained in the painted Objects tab");
                LogAssert.NoUnexpectedReceived();
            }
            finally{window.Close();}
        }
        [UnityTest] public IEnumerator OperatorTabsBrowseWithoutManagerAndRespectCollapsedRows()
        {
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)Assert.Ignore("Requires graphics for actual window rendering");
            var stage=Make("Managerless stage").AddComponent<SpatialStage>();
            var spatial=Make("Managerless object").AddComponent<SpatialObject>();
            var cue=Make("Managerless cue").AddComponent<PlaybackCue>();
            Selection.objects=Array.Empty<UnityEngine.Object>();
            var window=ScriptableObject.CreateInstance<SpatialFabricWindow>();
            try
            {
                SetPanelValue(window,"autoFindManager",false);
                var expanded=PanelValue<Dictionary<UnityEngine.Object,bool>>(window,"expandedTargets");
                expanded[stage]=true;expanded[spatial]=true;expanded[cue]=true;
                window.position=new Rect(50,50,1000,900);window.Show();
                var targets=PanelValue<HashSet<UnityEngine.Object>>(window,"paintedOperatorTargets");
                var tabs=new[]{0,1,3};var expected=new UnityEngine.Object[]{stage,spatial,cue};
                for(int i=0;i<tabs.Length;i++)
                {
                    SetPanelValue(window,"tab",tabs[i]);window.Repaint();
                    var target=expected[i];yield return AwaitPaint(()=>targets.Contains(target),"Managerless tab did not paint component");
                    expanded[target]=false;window.Repaint();
                    yield return AwaitPaint(()=>!targets.Contains(target),"Collapsed component still painted its property block");
                    expanded[target]=true;window.Repaint();
                    yield return AwaitPaint(()=>targets.Contains(target),"Reopened component lost its property block");
                }
                Assert.That(PanelValue<SpatialFabricManager>(window,"manager"),Is.Null);
                Assert.That(cue.IsPlaying,Is.False);LogAssert.NoUnexpectedReceived();
            }
            finally{window.Close();}
        }
        sealed class ForbiddenRoutingInventory : IAudioRoutingInventory
        {
            public void Probe()=>throw new InvalidOperationException("Drawing must not probe interfaces");
            public NPLAudio.SpatialFabric.Audio.AudioDeviceInfo[] RefreshDevices()=>throw new InvalidOperationException("Drawing must not enumerate devices");
            public string DeviceEnumerationError=>"";
        }
        [UnityTest] public IEnumerator ActualAudioRoutingGridClickIsUndoableAndSurvivesOfflineRefresh()
        {
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)Assert.Ignore("Requires graphics for actual window rendering");
            var cue=Make("Audio grid route").AddComponent<PlaybackCue>();cue.configuredDeviceId="fake-studio-interface";cue.outputChannel=1;
            var cache=new AudioInterfaceCache(()=>null,s=>{});
            cache.AcceptInventory(new[]{new NPLAudio.SpatialFabric.Audio.AudioDeviceInfo{id=cue.configuredDeviceId,name="Test studio interface",channels=8,sample_rate=48000}},"",DateTime.UtcNow);
            var routing=new AudioRoutingPanel(cache,new ForbiddenRoutingInventory());
            var window=ScriptableObject.CreateInstance<SpatialFabricWindow>();
            try
            {
                SetPanelValue(routing,"showMatrix",true);
                SetPanelValue(window,"audioRouting",routing);SetPanelValue(window,"tab",2);
                window.position=new Rect(50,50,1200,1000);window.Show();window.Repaint();
                var cells=PanelValue<Dictionary<(PlaybackCue cue,string device,int channel),Rect>>(PanelValue<AudioRoutingMatrixView>(routing,"matrix"),"paintedCellScreens");
                var key=(cue,"fake-studio-interface",3);
                yield return AwaitPaint(()=>cells.ContainsKey(key),"Actual audio output grid did not paint interface channel cells");
                var point=cells[key].center-window.position.position;
                window.SendEvent(new Event{type=EventType.MouseDown,button=0,mousePosition=point});
                window.SendEvent(new Event{type=EventType.MouseUp,button=0,mousePosition=point});
                yield return AwaitPaint(()=>cue.outputChannel==3,"Clicking the interface grid did not assign the actual scene cue");
                Undo.FlushUndoRecordObjects();Undo.PerformUndo();
                Assert.That(cue.configuredDeviceId,Is.EqualTo("fake-studio-interface"));Assert.That(cue.outputChannel,Is.EqualTo(1));
                cache.AcceptInventory(Array.Empty<NPLAudio.SpatialFabric.Audio.AudioDeviceInfo>(),"",DateTime.UtcNow);window.Repaint();
                yield return AwaitPaint(()=>cells.ContainsKey(key),"Unplugged interface cells disappeared from the grid");
                Assert.That(cache.Devices.Single().online,Is.False);Assert.That(cache.Devices.Single().channels,Is.EqualTo(8));
                Assert.That(cue.configuredDeviceId,Is.EqualTo("fake-studio-interface"));Assert.That(cue.outputChannel,Is.EqualTo(1));
                Assert.That(cue.IsPlaying,Is.False);Assert.That(cue.ReplayPending,Is.False);LogAssert.NoUnexpectedReceived();
            }
            finally{window.Close();}
        }
        [UnityTest] public IEnumerator ActualGridRejectsCellsClippedByTheOuterScrollViewport()
        {
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)Assert.Ignore("Requires graphics for actual window rendering");
            var cues=Enumerable.Range(0,12).Select(i=>Make("Clip cue "+i.ToString("D2")).AddComponent<PlaybackCue>()).ToArray();
            foreach(var cue in cues){cue.configuredDeviceId="clip-interface";cue.outputChannel=1;}
            var cache=new AudioInterfaceCache(()=>null,s=>{});
            cache.AcceptInventory(new[]{new NPLAudio.SpatialFabric.Audio.AudioDeviceInfo{id="clip-interface",name="Clip test",channels=8,sample_rate=48000}},"",DateTime.UtcNow);
            var routing=new AudioRoutingPanel(cache,new ForbiddenRoutingInventory());var window=ScriptableObject.CreateInstance<SpatialFabricWindow>();
            try
            {
                SetPanelValue(window,"audioRouting",routing);SetPanelValue(window,"tab",2);
                window.position=new Rect(50,50,1200,700);window.Show();window.Repaint();
                var cells=PanelValue<Dictionary<(PlaybackCue cue,string device,int channel),Rect>>(PanelValue<AudioRoutingMatrixView>(routing,"matrix"),"paintedCellScreens");
                yield return AwaitPaint(()=>cells.ContainsKey((cues[0],"clip-interface",3)),"Visible matrix cells failed to paint");
                var viewport=PanelValue<Rect>(window,"scrollViewportScreen");
                foreach(var rect in cells.Values)Assert.That(AudioRoutingMatrixView.Intersection(rect,viewport),Is.EqualTo(rect));
                var clippedPoint=cells[(cues[0],"clip-interface",3)].center+new Vector2(0,11*AudioRoutingMatrixView.RowHeight);
                Assert.That(viewport.Contains(clippedPoint),Is.False,"Fixture must target a logically present but clipped-away row");
                Assert.That(cells.ContainsKey((cues[11],"clip-interface",3)),Is.False);
                var point=clippedPoint-window.position.position;
                window.SendEvent(new Event{type=EventType.MouseDown,button=0,mousePosition=point});
                window.SendEvent(new Event{type=EventType.MouseUp,button=0,mousePosition=point});
                yield return null;
                Assert.That(cues[11].outputChannel,Is.EqualTo(1));LogAssert.NoUnexpectedReceived();
            }
            finally{window.Close();}
        }
        [UnityTest] public IEnumerator ActualAudioDropdownsListCachedInterfacesAndAvailableChannels()
        {
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)Assert.Ignore("Requires graphics for actual window rendering");
            var cue=Make("Cached dropdown cue").AddComponent<PlaybackCue>();cue.configuredDeviceId="interface-a";cue.outputChannel=7;
            var unknown=Make("Unknown dropdown cue").AddComponent<PlaybackCue>();unknown.configuredDeviceId="unknown-interface";unknown.outputChannel=45;
            string json=null;var seed=new AudioInterfaceCache(()=>null,s=>json=s);
            seed.AcceptInventory(new[]{new NPLAudio.SpatialFabric.Audio.AudioDeviceInfo{id="interface-a",name="Studio USB",channels=8,sample_rate=48000},new NPLAudio.SpatialFabric.Audio.AudioDeviceInfo{id="interface-b",name="Studio USB",channels=2,sample_rate=48000}},"",DateTime.UtcNow);
            var routing=new AudioRoutingPanel(new AudioInterfaceCache(()=>json,s=>{}),new ForbiddenRoutingInventory());
            var window=ScriptableObject.CreateInstance<SpatialFabricWindow>();
            try
            {
                SetPanelValue(routing,"showMatrix",false);
                SetPanelValue(window,"audioRouting",routing);SetPanelValue(window,"tab",2);
                window.position=new Rect(50,50,1000,1000);window.Show();window.Repaint();
                var dropdowns=PanelValue<Dictionary<(PlaybackCue cue,string field),Rect>>(routing,"paintedDropdownScreens");
                yield return AwaitPaint(()=>dropdowns.ContainsKey((cue,"device")) && dropdowns.ContainsKey((cue,"channel")),"Actual interface/channel dropdowns did not paint");
                var rows=PanelValue<Array>(routing,"rows");
                object row=rows.Cast<object>().Single(r=>(PlaybackCue)r.GetType().GetField("cue").GetValue(r)==cue);var type=row.GetType();
                var ids=(string[])type.GetField("deviceIds").GetValue(row);var labels=(string[])type.GetField("deviceLabels").GetValue(row);
                Assert.That(ids,Is.EqualTo(new[]{"","interface-a","interface-b"}));
                Assert.That(labels[1],Does.Contain("OFFLINE").And.Contain("interface-a"));Assert.That(labels[2],Does.Contain("interface-b"));
                Assert.That((int[])type.GetField("channelValues").GetValue(row),Is.EqualTo(Enumerable.Range(0,9).ToArray()));
                Assert.That((int)type.GetField("channelIndex").GetValue(row),Is.EqualTo(7));
                row=rows.Cast<object>().Single(r=>(PlaybackCue)r.GetType().GetField("cue").GetValue(r)==unknown);
                Assert.That((int[])type.GetField("channelValues").GetValue(row),Is.EqualTo(new[]{0,45}));
                Assert.That(PanelValue<Dictionary<(PlaybackCue cue,string device,int channel),Rect>>(PanelValue<AudioRoutingMatrixView>(routing,"matrix"),"paintedCellScreens"),Is.Empty);
                // The same dropdowns are present on an expanded Playback-tab cue.
                SetPanelValue(window,"tab",3);PanelValue<Dictionary<UnityEngine.Object,bool>>(window,"expandedTargets")[cue]=true;
                dropdowns.Clear();window.Repaint();
                yield return AwaitPaint(()=>dropdowns.ContainsKey((cue,"device")) && dropdowns.ContainsKey((cue,"channel")),"Playback cue did not expose interface/channel dropdowns");
                Assert.That(cue.configuredDeviceId,Is.EqualTo("interface-a"));Assert.That(cue.outputChannel,Is.EqualTo(7));
                Assert.That(cue.IsPlaying,Is.False);LogAssert.NoUnexpectedReceived();
            }
            finally{window.Close();}
        }
        [Test] public void DropdownPresentationBoundsRowsAndPinsStateUntilNextLayout()
        {
            var cache=new AudioInterfaceCache(()=>null,s=>{});
            cache.AcceptInventory(Enumerable.Range(0,256).Select(i=>new NPLAudio.SpatialFabric.Audio.AudioDeviceInfo{id="device-"+i,name="Interface "+i,channels=64,sample_rate=48000}).ToArray(),"",DateTime.UtcNow);
            var cues=Enumerable.Range(0,32).Select(i=>Make("Route "+i).AddComponent<PlaybackCue>()).ToArray();
            cues[0].configuredDeviceId="device-0";cues[0].outputChannel=1;
            var routing=new AudioRoutingPanel(cache,new ForbiddenRoutingInventory());
            SetPanelValue(routing,"showMatrix",false);
            var capture=typeof(AudioRoutingPanel).GetMethod("Capture",BindingFlags.Instance|BindingFlags.NonPublic);capture.Invoke(routing,new object[]{cues});
            var rows=PanelValue<Array>(routing,"rows");
            Assert.That(rows.Length,Is.EqualTo(16));
            Assert.That(((string[])rows.GetValue(0).GetType().GetField("deviceIds").GetValue(rows.GetValue(0))).Length,Is.EqualTo(257));
            var row=rows.GetValue(0);var type=row.GetType();Assert.That(type.GetField("editable").GetValue(row),Is.True);
            cues[0].outputChannel=7;
            typeof(PlaybackCue).GetProperty("WorkerState").GetSetMethod(true).Invoke(cues[0],new object[]{NPLAudio.SpatialFabric.Audio.AudioWorkerState.Starting});
            Assert.That(type.GetField("channel").GetValue(row),Is.EqualTo(1));Assert.That(type.GetField("editable").GetValue(row),Is.True);
            Assert.That(AudioRoutingPanel.AssignRoute(cues[0],"other",3,out _),Is.False);
            capture.Invoke(routing,new object[]{cues});row=PanelValue<Array>(routing,"rows").GetValue(0);
            Assert.That(type.GetField("channel").GetValue(row),Is.EqualTo(7));Assert.That(type.GetField("editable").GetValue(row),Is.False);
        }
        [TestCase(0,0,100,0,0)]
        [TestCase(10000,0,100,0,5)]
        [TestCase(10000,19,100,0,6)]
        [TestCase(10000,200,100,10,15)]
        [TestCase(10000,999999,100,9995,10000)]
        [TestCase(2,2000,100,0,2)]
        public void FixedHeightRangeBoundsWorkToViewport(int count,float scroll,float height,int expectedFirst,int expectedEnd)
        {
            SceneObjectViewCache.VisibleRange(count,scroll,height,out int first,out int end);
            Assert.That(first,Is.EqualTo(expectedFirst));Assert.That(end,Is.EqualTo(expectedEnd));
        }
        [Test] public void DisplayCacheReusesRowsRefiltersImmediatelyAndBoundsRefresh()
        {
            var a=Make("Alpha");var b=Make("Beta");b.AddComponent<SpatialObject>();
            var cache=new SceneObjectViewCache();Assert.That(cache.Refresh(0,"",0),Is.True);
            var rows=cache.Rows;var visible=cache.Visible;
            a.name="Renamed";
            Assert.That(cache.Refresh(.1,"",0),Is.False);Assert.That(cache.Rows,Is.SameAs(rows));Assert.That(cache.Visible,Is.SameAs(visible));
            cache.Refresh(.1,"ALPHA",0);Assert.That(cache.VisibleObjects.SetEquals(new[]{a}),Is.True);
            Assert.That(cache.Rows,Is.SameAs(rows)); // Searching never rescans Unity components.
            cache.Refresh(.1,"",2);Assert.That(cache.VisibleObjects.SetEquals(new[]{b}),Is.True);
            cache.Refresh(.25,"Renamed",1);Assert.That(cache.Rows,Is.Not.SameAs(rows));Assert.That(cache.VisibleObjects.SetEquals(new[]{a}),Is.True);
            Assert.That(cache.Visible.Single().Summary,Is.EqualTo("Unconfigured"));
        }
        [Test] public void DisplayInvalidationRefreshesComponentsPathsAndDeletedTargetsImmediately()
        {
            var a=Make("Parent");var b=Make("Child");b.transform.SetParent(a.transform);
            var cache=new SceneObjectViewCache();cache.Refresh(0,"",1);
            SceneObjectAuthoring.AddComponents(new[]{b},typeof(SpatialObject));
            cache.Invalidate();cache.Refresh(.01,"Parent/Child",2);
            Assert.That(cache.VisibleObjects.SetEquals(new[]{b}),Is.True);
            Assert.That(cache.Visible.Single().Summary,Is.EqualTo("SpatialObject"));
            Undo.PerformUndo();cache.Invalidate();cache.Refresh(.02,"",1);
            Assert.That(cache.VisibleObjects,Does.Contain(b));
            b.transform.SetParent(null);a.name="Moved";
            UnityEngine.Object.DestroyImmediate(b);cache.Invalidate();cache.Refresh(.03,"Moved",0);
            Assert.That(cache.Objects,Has.No.Member(b));Assert.That(cache.VisibleObjects.SetEquals(new[]{a}),Is.True);
        }
        [Test] public void FreshMutationPreflightRejectsLockAfterDisplaySnapshot()
        {
            var obj=Make("Initially editable");obj.AddComponent<SpatialObject>();
            var cache=new SceneObjectViewCache();cache.Refresh(0,"",0);
            Assert.That(cache.EditableObjects,Does.Contain(obj));
            using(var serialized=SceneObjectAuthoring.SerializedSelection(new[]{obj},typeof(SpatialObject)))
            {
                serialized.Update();serialized.FindProperty("gain").floatValue=.2f;
                obj.hideFlags=HideFlags.NotEditable;
                Assert.Throws<InvalidOperationException>(()=>SceneObjectAuthoring.Apply(serialized));
                Assert.Throws<InvalidOperationException>(()=>SceneObjectAuthoring.AddComponents(new[]{obj},typeof(PlaybackCue)));
            }
            Assert.That(obj.GetComponent<SpatialObject>().gain,Is.EqualTo(1));Assert.That(obj.GetComponent<PlaybackCue>(),Is.Null);
        }
        [Test] public void InventoryIncludesInactiveUnconfiguredNestedAndAdditiveObjectsWithoutCreatingAnything()
        {
            var root=Make("Unconfigured");var child=Make("Child");child.transform.SetParent(root.transform);root.SetActive(false);
            var configured=Make("Configured",other);configured.AddComponent<SpatialObject>();
            var hiddenAuthoring=Make("Hidden but saved");hiddenAuthoring.hideFlags=HideFlags.HideInHierarchy;
            var before=scene.GetRootGameObjects().Length;
            var all=SceneObjectAuthoring.Enumerate();
            Assert.That(all,Does.Contain(root));Assert.That(all,Does.Contain(child));Assert.That(all,Does.Contain(configured));Assert.That(all,Does.Contain(hiddenAuthoring));
            Assert.That(root.GetComponents<Component>().Length,Is.EqualTo(1));
            Assert.That(scene.GetRootGameObjects().Length,Is.EqualTo(before));
            Assert.That(all.Distinct().Count(),Is.EqualTo(all.Length));
        }
        [Test] public void InventoryExcludesAssetsPreviewScenesAndHiddenEditorHierarchies()
        {
            var source=Make("Prefab");string path=folder+"/Test.prefab";
            var asset=PrefabUtility.SaveAsPrefabAsset(source,path);
            var hidden=Make("Editor internals");hidden.hideFlags=HideFlags.HideAndDontSave;
            var child=Make("Hidden child");child.transform.SetParent(hidden.transform);
            var preview=EditorSceneManager.NewPreviewScene();var previewObject=Make("Preview",preview);
            var contents=PrefabUtility.LoadPrefabContents(path);
            try
            {
                var all=SceneObjectAuthoring.Enumerate();
                foreach(var excluded in new[]{asset,hidden,child,previewObject,contents})
                {Assert.That(all,Has.No.Member(excluded));Assert.That(SceneObjectAuthoring.IsAuthoringObject(excluded),Is.False);}
                Assert.That(all,Does.Contain(source));
            }
            finally{PrefabUtility.UnloadPrefabContents(contents);EditorSceneManager.ClosePreviewScene(preview);}
        }
        [Test] public void BulkAdditionSkipsDuplicatesAndIsOneUndoWithRedo()
        {
            var a=Make("Existing");var existing=a.AddComponent<PlaybackCue>();var b=Make("New");b.SetActive(false);var c=Make("Other",other);
            Undo.ClearAll();
            Assert.That(SceneObjectAuthoring.AddComponents(new[]{a,b,b,c},typeof(PlaybackCue)),Is.EqualTo(2));
            Assert.That(a.GetComponents<PlaybackCue>().Length,Is.EqualTo(1));Assert.That(a.GetComponent<PlaybackCue>(),Is.SameAs(existing));
            Assert.That(b.GetComponent<PlaybackCue>().IsPrepared,Is.False);Assert.That(b.GetComponent<PlaybackCue>().playOnStart,Is.False);
            Undo.PerformUndo();Assert.That(b.GetComponent<PlaybackCue>(),Is.Null);Assert.That(c.GetComponent<PlaybackCue>(),Is.Null);Assert.That(a.GetComponent<PlaybackCue>(),Is.SameAs(existing));
            Undo.PerformRedo();Assert.That(b.GetComponents<PlaybackCue>().Length,Is.EqualTo(1));Assert.That(c.GetComponents<PlaybackCue>().Length,Is.EqualTo(1));
            Assert.That(SceneObjectAuthoring.AddComponents(new[]{a,b,c},typeof(PlaybackCue)),Is.Zero);
        }
        [Test] public void EntireBatchIsValidatedBeforeAnyMutation()
        {
            var valid=Make("Valid");var invalid=Make("Locked");invalid.hideFlags=HideFlags.NotEditable;
            Assert.That(SceneObjectAuthoring.Enumerate(),Does.Contain(invalid));
            Assert.That(SceneObjectAuthoring.IsEditableObject(invalid),Is.False);
            Assert.Throws<InvalidOperationException>(()=>SceneObjectAuthoring.AddComponents(new[]{valid,invalid},typeof(SpatialObject)));
            Assert.That(valid.GetComponent<SpatialObject>(),Is.Null);
            Assert.Throws<InvalidOperationException>(()=>SceneObjectAuthoring.AddComponents(new[]{valid,(GameObject)null},typeof(SpatialObject)));
            Assert.Throws<ArgumentException>(()=>SceneObjectAuthoring.AddComponents(new[]{valid},typeof(AudioSource)));
            Assert.Throws<ArgumentException>(()=>SceneObjectAuthoring.AddComponents(new[]{valid},null));
            Assert.That(valid.GetComponents<Component>().Length,Is.EqualTo(1));
        }
        [Test] public void SerializedApplyRejectsNonSpatialFabricComponentsAndLockedTargets()
        {
            var obj=Make("Transform must not be edited");
            using(var serialized=new SerializedObject(obj.transform))
            {
                serialized.Update();serialized.FindProperty("m_LocalPosition").vector3Value=Vector3.one;
                Assert.Throws<InvalidOperationException>(()=>SceneObjectAuthoring.Apply(serialized));
                Assert.That(obj.transform.localPosition,Is.EqualTo(Vector3.zero));
            }
            var binding=obj.AddComponent<SpatialObject>();obj.hideFlags=HideFlags.NotEditable;
            using(var serialized=new SerializedObject(binding))
            {
                serialized.Update();serialized.FindProperty("gain").floatValue=.2f;
                Assert.Throws<InvalidOperationException>(()=>SceneObjectAuthoring.Apply(serialized));
                Assert.That(binding.gain,Is.EqualTo(1));
            }
        }
        [Test] public void EveryAllowedComponentAddsOnlyItselfWithSafeStartupDefaults()
        {
            foreach(var type in SceneObjectAuthoring.ComponentTypes)
            {
                var obj=Make(type.Name);
                Assert.That(SceneObjectAuthoring.AddComponents(new[]{obj},type),Is.EqualTo(1));
                Assert.That(obj.GetComponents<Component>().Length,Is.EqualTo(2),type.Name);
            }
            Assert.That(scene.GetRootGameObjects().SelectMany(o=>o.GetComponents<SpatialFabricManager>()).All(m=>!m.IsListening && !m.enableInEditor && !m.enableInPlay),Is.True);
            Assert.That(scene.GetRootGameObjects().SelectMany(o=>o.GetComponents<LiveInputCue>()).All(c=>!c.IsLive && c.WorkerState==Audio.AudioWorkerState.Stopped),Is.True);
            Assert.That(scene.GetRootGameObjects().SelectMany(o=>o.GetComponents<SpatialPathPlayer>()).All(c=>!c.IsPlaying && !c.playOnEnable),Is.True);
            Assert.That(scene.GetRootGameObjects().SelectMany(o=>o.GetComponents<PlaybackCue>()).All(c=>!c.IsPrepared && !c.IsPlaying && !c.playOnStart),Is.True);
        }
        [Test] public void MixedSerializedEditSharesInspectorValuesAndUndoAndMarksScenesDirty()
        {
            var a=Make("A").AddComponent<SpatialObject>();var b=Make("B",other).AddComponent<SpatialObject>();var empty=Make("No component");
            a.gain=.25f;b.gain=.75f;
            EditorSceneManager.SaveScene(scene,folder+"/First.unity");EditorSceneManager.SaveScene(other,folder+"/Second.unity");Undo.ClearAll();
            using(var serialized=SceneObjectAuthoring.SerializedSelection(new[]{a.gameObject,b.gameObject,empty},typeof(SpatialObject)))
            {
                serialized.Update();Assert.That(serialized.targetObjects.Length,Is.EqualTo(2));
                var gain=serialized.FindProperty("gain");Assert.That(gain.hasMultipleDifferentValues,Is.True);
                gain.floatValue=.5f;Assert.That(SceneObjectAuthoring.Apply(serialized),Is.True);
            }
            Assert.That(a.gain,Is.EqualTo(.5f));Assert.That(b.gain,Is.EqualTo(.5f));
            Assert.That(scene.isDirty && other.isDirty,Is.True);Assert.That(empty.GetComponent<SpatialObject>(),Is.Null);
            using(var inspector=new SerializedObject(a))Assert.That(inspector.FindProperty("gain").floatValue,Is.EqualTo(.5f));
            Undo.PerformUndo();Assert.That(a.gain,Is.EqualTo(.25f));Assert.That(b.gain,Is.EqualTo(.75f));
            Undo.PerformRedo();Assert.That(a.gain,Is.EqualTo(.5f));Assert.That(b.gain,Is.EqualTo(.5f));
        }
        [Test] public void SerializedEditsPersistAcrossSaveAndReloadAndReflectInspectorChanges()
        {
            var obj=Make("Persisted");SceneObjectAuthoring.AddComponents(new[]{obj},typeof(PlaybackCue));
            using(var serialized=SceneObjectAuthoring.SerializedSelection(new[]{obj},typeof(PlaybackCue)))
            {serialized.Update();serialized.FindProperty("outputChannel").intValue=7;SceneObjectAuthoring.Apply(serialized);}
            using(var inspector=new SerializedObject(obj.GetComponent<PlaybackCue>()))
            {inspector.Update();inspector.FindProperty("outputChannel").intValue=9;inspector.ApplyModifiedProperties();}
            using(var serialized=SceneObjectAuthoring.SerializedSelection(new[]{obj},typeof(PlaybackCue)))
            {serialized.Update();Assert.That(serialized.FindProperty("outputChannel").intValue,Is.EqualTo(9));}
            string path=folder+"/Persisted.unity";EditorSceneManager.SaveScene(scene,path);EditorSceneManager.CloseScene(scene,true);
            scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            var cue=scene.GetRootGameObjects().Single().GetComponent<PlaybackCue>();
            Assert.That(cue.outputChannel,Is.EqualTo(9));Assert.That(cue.IsPrepared || cue.IsPlaying || cue.playOnStart,Is.False);
        }
        [Test] public void PrefabInstanceEditsAreRecordedAsOverrides()
        {
            var source=Make("Prefab");source.AddComponent<SpatialObject>();string path=folder+"/Override.prefab";
            var asset=PrefabUtility.SaveAsPrefabAsset(source,path);var instance=(GameObject)PrefabUtility.InstantiatePrefab(asset,scene);
            using(var serialized=SceneObjectAuthoring.SerializedSelection(new[]{instance},typeof(SpatialObject)))
            {serialized.Update();serialized.FindProperty("gain").floatValue=.3f;SceneObjectAuthoring.Apply(serialized);}
            Assert.That(PrefabUtility.GetPropertyModifications(instance).Any(p=>p.propertyPath=="gain"),Is.True);
            Assert.That(asset.GetComponent<SpatialObject>().gain,Is.EqualTo(1));
        }
        [TestCase("playOnStart")][TestCase("playOnEnable")][TestCase("enableInEditor")][TestCase("enableInPlay")][TestCase("m_Enabled")][TestCase("persistentId")]
        public void CentralPanelDoesNotExposeStartupOrIdentityOptIns(string name)
        {Assert.That(SceneObjectAuthoring.IsSafeProperty(name),Is.False);Assert.That(SceneObjectAuthoring.IsSafeProperty("gain"),Is.True);}
    }
}
