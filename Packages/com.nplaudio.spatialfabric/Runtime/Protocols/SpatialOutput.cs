// Multiple independent protocol destinations and per-binding target overrides. MIT.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using NPLAudio.SpatialFabric.Osc;
namespace NPLAudio.SpatialFabric.Protocols
{
    public enum SpatialProtocol { ADM=0,Custom=1 }
    [Serializable] public sealed class SpatialOutput
    {
        public string key="",label="Output";
        public bool enabled=true;
        public SpatialProtocol protocol;
        public string ip="127.0.0.1";
        public int port=50018,sendRateHz=30;
        public bool sendOnlyOnChange;
        public AdmCoordinateMode admMode=AdmCoordinateMode.Both;
        public string addressTemplate="/object/{id}/xyz";
        public CustomProtocolSettings custom=new CustomProtocolSettings();
    }
    [Serializable] public sealed class SpatialTargetOverride
    {
        public string outputKey="";
        public bool enabled=true;
        public int objectIdOverride=-1;
        public List<CustomField> fields=new List<CustomField>();
    }
    public static class SpatialOutputs
    {
        public static SpatialTargetOverride Override(SpatialObject obj,SpatialOutput output)
        {
            var values=obj.targetOverrides.Where(o=>o!=null && o.outputKey==output.key).ToArray();if(values.Length>1)throw new FormatException("Duplicate per-output overrides");return values.FirstOrDefault();
        }
        public static int Id(SpatialObject obj,SpatialOutput output)
        {
            var target=Override(obj,output);int id=target!=null && target.objectIdOverride!=-1?target.objectIdOverride:output.protocol==SpatialProtocol.ADM?obj.AdmId:obj.CustomId;
            if(id<1)throw new FormatException("Output object ID must be positive");return id;
        }
        public static List<CustomField> Fields(SpatialObject obj,SpatialOutput output)
        {
            CustomProtocol.ValidateFields(obj.customFields);var fields=obj.customFields.ToDictionary(f=>f.key,f=>f.value,StringComparer.Ordinal);var target=Override(obj,output);
            if(target!=null){CustomProtocol.ValidateFields(target.fields);foreach(var f in target.fields)fields[f.key]=f.value;}
            return fields.Select(p=>new CustomField{key=p.Key,value=p.Value}).ToList();
        }
        public static List<OscMessage> Build(SpatialFabricManager manager,SpatialOutput output)
        {
            if(output==null || string.IsNullOrWhiteSpace(output.key) || output.sendRateHz<1 || output.sendRateHz>60 || !Enum.IsDefined(typeof(SpatialProtocol),output.protocol))throw new FormatException("Invalid output identity/protocol/rate");
            OscEndpoint.Address(output.ip,output.port);var messages=new List<OscMessage>();
            foreach(var obj in manager.objects)
            {
                if(!manager.Resolves(obj))continue;var target=Override(obj,output);if(target!=null && !target.enabled)continue;
                if(output.protocol==SpatialProtocol.ADM?!obj.sendAdm:!obj.sendCustom)continue;
                Vector3 p=manager.stage.Normalize(obj.Pose.position);int id=Id(obj,output);
                if(output.protocol==SpatialProtocol.ADM)messages.AddRange(SpatialMessages.Adm(obj,p,output.admMode,id));
                else messages.AddRange(CustomProtocol.Build(output.custom,output.addressTemplate,new CustomObjectState{id=id,label=obj.DisplayName,front=p.x,right=p.y,up=p.z,gain=obj.gain,width=obj.width,fields=Fields(obj,output)}));
                if(messages.Count>4096)throw new FormatException("Output frame size limit");
            }
            return messages;
        }
    }
}
