// Explicit file dialogs + preview/confirmation around atomic routing plans. MIT.
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
namespace NPLAudio.SpatialFabric.Editor
{
    public static class RoutingCsvActions
    {
        public static void Export(SpatialFabricManager manager,bool playback)
        {
            try
            {
                string text=playback?PlaybackAssetCsv.Export(manager):RoutingCsv.ExportSpatial(manager);
                string path=EditorUtility.SaveFilePanel("Export SpatialFabric routing","",playback?"playback-routing.csv":"spatial-routing.csv","csv");
                if(string.IsNullOrEmpty(path))return;File.WriteAllText(path,text,new UTF8Encoding(false));
            }
            catch(Exception e){EditorUtility.DisplayDialog("Routing export failed",e.Message,"OK");}
        }
        public static void Import(SpatialFabricManager manager,bool playback)
        {
            try
            {
                RoutingCsv.EnsureIdle(manager);
                string path=EditorUtility.OpenFilePanel("Preview SpatialFabric routing import","","csv");if(string.IsNullOrEmpty(path))return;
                if(new FileInfo(path).Length>Routing.RoutingCsvCodec.MaxBytes)throw new FormatException("CSV byte size limit exceeded");
                string text=File.ReadAllText(path,new UTF8Encoding(false,true));
                var plan=playback?PlaybackAssetCsv.Preview(manager,text):RoutingCsv.PreviewSpatial(manager,text);
                if(EditorUtility.DisplayDialog("Apply routing import?",plan.Description+"\n\nOne Undo transaction. V2 playback CSV explicitly reassigns source references; v1 preserves them. Manager/network settings are not changed.","Apply routes","Cancel"))plan.Apply();
            }
            catch(Exception e){EditorUtility.DisplayDialog("Routing import refused — no partial changes",e.Message,"OK");}
        }
    }
}
