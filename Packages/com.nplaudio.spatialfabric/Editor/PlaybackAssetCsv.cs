// Explicit v2 source-reference assignment; no label/path guessing and no native PCM/device calls. MIT.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using NPLAudio.SpatialFabric.Routing;
namespace NPLAudio.SpatialFabric.Editor
{
    public static class PlaybackAssetCsv
    {
        public const string Schema="sf-unity-playback-routing/2";
        public static readonly string[] Header=RoutingCsv.PlaybackHeader.Concat(new[]{"PreparedAudioRef","ClipRef"}).ToArray();
        static string Reference(UnityEngine.Object asset)
        {
            if(asset==null)return "";
            if(!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset,out string guid,out long local))throw new FormatException("Source must be a saved project asset");
            return guid+":"+local.ToString(CultureInfo.InvariantCulture);
        }
        public static string Export(SpatialFabricManager manager)
        {
            var rows=RoutingCsvCodec.Parse(RoutingCsv.ExportPlayback(manager));int key=Array.IndexOf(rows[0],"Key");var cues=SceneInventory.InWorld<PlaybackCue>(manager).ToDictionary(c=>c.Key,StringComparer.Ordinal);
            rows[0]=Header;
            for(int i=1;i<rows.Count;++i){var cue=cues[rows[i][key]];rows[i][0]=Schema;rows[i]=rows[i].Concat(new[]{Reference(cue.preparedAudio),Reference(cue.clip)}).ToArray();}
            return RoutingCsvCodec.Write(rows);
        }
        sealed class AssetReference
        {
            readonly string token,path,hash;
            readonly long local;
            public AssetReference(string value)
            {
                token=value;if(value=="")return;
                var match=Regex.Match(value,@"^([0-9a-fA-F]{32}):(-?[0-9]+)$",RegexOptions.CultureInvariant);
                if(!match.Success || !long.TryParse(match.Groups[2].Value,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out local))throw new FormatException("Source reference must be GUID:localFileId");
                path=AssetDatabase.GUIDToAssetPath(match.Groups[1].Value);if(string.IsNullOrEmpty(path) || !path.StartsWith("Assets/",StringComparison.Ordinal) && !path.StartsWith("Packages/",StringComparison.Ordinal))throw new FormatException("Unknown source asset GUID");
                hash=AssetDatabase.GetAssetDependencyHash(path).ToString();
            }
            public T Resolve<T>()where T:UnityEngine.Object
            {
                if(token=="")return null;
                string guid=token.Substring(0,32);
                if(AssetDatabase.GUIDToAssetPath(guid)!=path || AssetDatabase.GetAssetDependencyHash(path).ToString()!=hash)throw new InvalidOperationException("Source asset changed since preview");
                var values=AssetDatabase.LoadAllAssetsAtPath(path).OfType<T>().Where(item=>AssetDatabase.TryGetGUIDAndLocalFileIdentifier(item,out string id,out long fileId)&&fileId==local&&string.Equals(id,guid,StringComparison.OrdinalIgnoreCase)).ToArray();
                if(values.Length!=1)throw new FormatException("Source reference is missing or has the wrong asset type");return values[0];
            }
        }
        public static RoutingImportPlan Preview(SpatialFabricManager manager,string text)
        {
            RoutingCsv.EnsureIdle(manager);var rows=RoutingCsvCodec.Parse(text);if(rows.Count<2)throw new FormatException("CSV has no routes");
            if(!rows[0].Contains("PreparedAudioRef"))return RoutingCsv.PreviewPlayback(manager,text);
            var header=RoutingCsvCodec.Header(rows[0],Header);var legacy=new List<string[]>{RoutingCsv.PlaybackHeader};
            var refs=new List<(string key,AssetReference prepared,AssetReference clip)>();
            for(int i=1;i<rows.Count;++i)
            {
                if(rows[i].Length!=Header.Length || rows[i][header["Schema"]]!=Schema)throw new FormatException("Invalid v2 playback record");var row=rows[i];
                var data=RoutingCsv.PlaybackHeader.Select(name=>name=="Schema"?RoutingCsv.PlaybackSchema:row[header[name]]).ToArray();legacy.Add(data);
                refs.Add((row[header["Key"]],new AssetReference(row[header["PreparedAudioRef"]]),new AssetReference(row[header["ClipRef"]])));
            }
            var plan=RoutingCsv.PreviewPlayback(manager,RoutingCsvCodec.Write(legacy));var targets=SceneInventory.InWorld<PlaybackCue>(manager).ToDictionary(c=>c.Key,StringComparer.Ordinal);
            foreach(var source in refs)
            {
                var cue=targets[source.key];PreparedAudioAsset prepared=null;AudioClip clip=null;
                plan.AttachAssets(source.key,()=>{prepared=source.prepared.Resolve<PreparedAudioAsset>();clip=source.clip.Resolve<AudioClip>();},()=>{cue.preparedAudio=prepared;cue.clip=clip;});
            }
            return plan;
        }
    }
}
