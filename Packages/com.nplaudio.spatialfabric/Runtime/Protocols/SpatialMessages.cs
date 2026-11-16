// Copyright (c) 2026 SpatialFabric Contributors. MIT.
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using NPLAudio.SpatialFabric.Osc;
namespace NPLAudio.SpatialFabric
{
    public enum AdmCoordinateMode { XYZ=0, AED=1, Both=2 }
    public static class SpatialMessages
    {
        public static Vector3 LimitSphere(Vector3 p) => p.magnitude > 1 ? p.normalized : p;
        public static Vector3 Aed(Vector3 p)
        {
            return new Vector3(Mathf.Atan2(-p.y,p.x)*Mathf.Rad2Deg, Mathf.Atan2(p.z,Mathf.Sqrt(p.x*p.x+p.y*p.y))*Mathf.Rad2Deg,p.magnitude);
        }
        public static IEnumerable<OscMessage> Adm(SpatialObject obj, Vector3 canonical, AdmCoordinateMode mode=AdmCoordinateMode.Both,int effectiveId=-1)
        {
            int id=effectiveId>=0?effectiveId:obj.AdmId;
            if (id <= 0) yield break;
            if(mode!=AdmCoordinateMode.XYZ && mode!=AdmCoordinateMode.AED && mode!=AdmCoordinateMode.Both)throw new FormatException("Invalid ADM coordinate mode");
            Vector3 p = LimitSphere(canonical), a = Aed(p); string b = "/adm/obj/" + id;
            if(mode!=AdmCoordinateMode.AED)yield return new OscMessage(b+"/xyz",OscArg.Float(p.y),OscArg.Float(p.x),OscArg.Float(p.z));
            if(mode!=AdmCoordinateMode.XYZ)yield return new OscMessage(b+"/aed",OscArg.Float(a.x),OscArg.Float(a.y),OscArg.Float(a.z));
            yield return new OscMessage(b+"/gain",OscArg.Float(obj.gain));
            yield return new OscMessage(b+"/w",OscArg.Float(obj.width));
            yield return new OscMessage(b+"/mute",OscArg.Int(obj.mute ? 1 : 0));
            yield return new OscMessage(b+"/name",OscArg.String(obj.DisplayName));
        }
        public static OscMessage Custom(SpatialObject obj, Vector3 p, string template)
        {
            if (obj.CustomId <= 0) throw new FormatException("Custom object ID must be positive");
            string address = template.Replace("{id}",obj.CustomId.ToString(CultureInfo.InvariantCulture));
            if (address.Contains("{") || address.Contains("}") || !OscCodec.ValidAddress(address)) throw new FormatException("Only {id} Custom XYZ address substitution is implemented in this preview");
            return new OscMessage(address,OscArg.Float(p.x),OscArg.Float(p.y),OscArg.Float(p.z));
        }
    }
}
