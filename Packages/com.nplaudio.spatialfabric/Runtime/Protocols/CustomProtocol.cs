// Engine-independent Custom OSC templates, ranges and XYZ/AED. Wire semantics follow Unreal v2.1. MIT.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using NPLAudio.SpatialFabric.Osc;
namespace NPLAudio.SpatialFabric.Protocols
{
    public enum CustomCoordinates { XYZ=0, AED=1 }
    public enum CustomSendMode { Packed=0, Discrete=1 }
    [Serializable] public sealed class CustomField { public string key="",value=""; }
    [Serializable] public sealed class CustomProtocolSettings
    {
        public CustomCoordinates coordinates;
        public CustomSendMode sendMode;
        public string argumentTemplate="x y z";
        public float positionMin=-1,positionMax=1,gainMin=0,gainMax=1,widthMin=0,widthMax=1;
        public bool sendOnlyOnChange;
        public int sendRateHz; // 0 inherits manager rate; otherwise 1..60.
    }
    public sealed class CustomObjectState
    {
        public int id;
        public string label="";
        public float front,right,up,gain=1,width;
        public IList<CustomField> fields=Array.Empty<CustomField>();
    }
    public static class CustomProtocol
    {
        static readonly HashSet<string> Arguments=new HashSet<string>(StringComparer.Ordinal){"x","y","z","gain","width","id","azimuth","a","elevation","e","distance","d"};
        static readonly HashSet<string> Reserved=new HashSet<string>(StringComparer.Ordinal){"x","y","z","gain","width","id","label","axis"};
        static readonly Regex Identifier=new Regex(@"^[A-Za-z_][A-Za-z0-9_]{0,63}$",RegexOptions.CultureInvariant);
        public static bool Finite(float n)=>!float.IsNaN(n)&&!float.IsInfinity(n);
        static float Map(float value,float low,float high,float toLow,float toHigh)
        {
            double t=Math.Max(0,Math.Min(1,((double)value-low)/(high-low)));
            float result=(float)(toLow+t*((double)toHigh-toLow));if(!Finite(result))throw new FormatException("Custom range result is non-finite");return result;
        }
        static string Number(float value)
        {
            if(value==0)value=0; // Normalize negative zero, like FString::SanitizeFloat.
            return value.ToString("0.0#####",CultureInfo.InvariantCulture);
        }
        public static void ValidateFields(IList<CustomField> fields)
        {
            if(fields==null)throw new FormatException("Custom fields cannot be null");
            if(fields.Count>64)throw new FormatException("At most 64 custom fields per object");
            var keys=new HashSet<string>(StringComparer.Ordinal);
            foreach(var f in fields)
            {
                if(f==null || f.key==null || !Identifier.IsMatch(f.key) || Reserved.Contains(f.key) || !keys.Add(f.key) || f.value==null || f.value.Length>4096 || f.value.IndexOf('\0')>=0 || f.value.Contains("{") || f.value.Contains("}"))throw new FormatException("Invalid, duplicate or reserved Custom field");
            }
        }
        public static List<OscMessage> Build(CustomProtocolSettings config,string addressTemplate,CustomObjectState state)
        {
            if(config==null || state==null || state.id<1 || state.label==null || state.label.Length>4096)throw new FormatException("Invalid Custom state/configuration");
            if(config.coordinates!=CustomCoordinates.XYZ && config.coordinates!=CustomCoordinates.AED || config.sendMode!=CustomSendMode.Packed && config.sendMode!=CustomSendMode.Discrete || config.sendRateHz<0 || config.sendRateHz>60)throw new FormatException("Unknown Custom coordinate/send mode or invalid rate");
            foreach(float f in new[]{state.front,state.right,state.up,state.gain,state.width,config.positionMin,config.positionMax,config.gainMin,config.gainMax,config.widthMin,config.widthMax})if(!Finite(f))throw new FormatException("Non-finite Custom input/range");
            ValidateFields(state.fields);
            if(addressTemplate==null || addressTemplate.Length>8192 || config.argumentTemplate==null || config.argumentTemplate.Length>1024)throw new FormatException("Custom template bounds exceeded");
            string[] names=config.argumentTemplate.Split((char[])null,StringSplitOptions.RemoveEmptyEntries);
            if(names.Length>32)throw new FormatException("At most 32 Custom arguments");
            float x=Map(state.front,-1,1,config.positionMin,config.positionMax),y=Map(state.right,-1,1,config.positionMin,config.positionMax),z=Map(state.up,-1,1,config.positionMin,config.positionMax);
            float gain=Map(state.gain,0,1,config.gainMin,config.gainMax),width=Map(state.width,0,1,config.widthMin,config.widthMax);
            double horizontal=Math.Sqrt((double)state.front*state.front+(double)state.right*state.right);
            float a=0,e=0,d=0;
            if(config.coordinates==CustomCoordinates.AED)
            {
                a=Map((float)(Math.Atan2(-state.right,state.front)*180/Math.PI),-180,180,config.positionMin,config.positionMax);
                e=Map((float)(Math.Atan2(state.up,horizontal)*180/Math.PI),-90,90,config.positionMin,config.positionMax);
                d=Map((float)Math.Min(1,Math.Sqrt(horizontal*horizontal+(double)state.up*state.up)),0,1,config.positionMin,config.positionMax);
            }
            var values=new Dictionary<string,float>(StringComparer.Ordinal){{"x",x},{"y",y},{"z",z},{"gain",gain},{"width",width},{"id",state.id},{"azimuth",a},{"a",a},{"elevation",e},{"e",e},{"distance",d},{"d",d}};
            var substitutions=new Dictionary<string,string>(StringComparer.Ordinal){{"id",state.id.ToString(CultureInfo.InvariantCulture)},{"x",Number(x)},{"y",Number(y)},{"z",Number(z)},{"gain",Number(gain)},{"width",Number(width)},{"label",state.label}};
            foreach(var f in state.fields)substitutions.Add(f.key,f.value);
            var args=new OscArg[names.Length];
            for(int i=0;i<names.Length;++i)
            {
                names[i]=names[i].ToLowerInvariant();if(!Arguments.Contains(names[i]))throw new FormatException("Unknown Custom argument: "+names[i]);args[i]=OscArg.Float(values[names[i]]);
            }
            string Resolve(string axis)
            {
                // One substitution pass: values never become second-stage templates.
                string address=Regex.Replace(addressTemplate,@"\{([^{}]+)\}",m=>m.Groups[1].Value=="axis"&&axis!=null?axis:substitutions.TryGetValue(m.Groups[1].Value,out string v)?v:throw new FormatException("Unresolved Custom placeholder: "+m.Value));
                if(address.Contains("{") || address.Contains("}") || !OscCodec.ValidAddress(address))throw new FormatException("Invalid resolved Custom OSC address");return address;
            }
            var messages=new List<OscMessage>();
            if(config.sendMode==CustomSendMode.Packed)messages.Add(new OscMessage(Resolve(null),args));
            else for(int i=0;i<names.Length;++i)
            {
                string axis=names[i]=="a"?"azimuth":names[i]=="e"?"elevation":names[i]=="d"?"distance":names[i];
                messages.Add(new OscMessage(Resolve(axis),args[i]));
            }
            // Validate every packet before the caller can send the first: no partial invalid discrete update.
            foreach(var message in messages)OscCodec.Encode(message);
            return messages;
        }
    }
}
