// Bounded DTO-only JSON writer: dictionary fields stay JSON objects, never executable templates. MIT.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
namespace NPLAudio.SpatialFabric.Protocols
{
    public static class ContractJson
    {
        public static string Serialize(object value,int maximumBytes=96*1024)
        {
            var output=new StringBuilder();
            void String(string text)
            {
                output.Append('"');
                for(int i=0;i<text.Length;++i)
                {
                    char c=text[i];
                    if(char.IsHighSurrogate(c)) {if(i+1>=text.Length || !char.IsLowSurrogate(text[i+1]))throw new FormatException("Unpaired JSON surrogate");output.Append(c).Append(text[++i]);}
                    else if(char.IsLowSurrogate(c))throw new FormatException("Unpaired JSON surrogate");
                    else if(c=='"' || c=='\\')output.Append('\\').Append(c);
                    else if(c<32)output.Append("\\u").Append(((int)c).ToString("x4",CultureInfo.InvariantCulture));
                    else output.Append(c);
                }
                output.Append('"');
            }
            void Write(object item,int depth)
            {
                if(depth>24 || output.Length>maximumBytes)throw new FormatException("JSON depth/size limit");
                if(item==null){output.Append("null");return;}
                if(item is string text){String(text);return;}
                if(item is bool b){output.Append(b?"true":"false");return;}
                if(item is float f){if(float.IsNaN(f)||float.IsInfinity(f))throw new FormatException("Non-finite JSON float");output.Append(f.ToString("R",CultureInfo.InvariantCulture));return;}
                if(item is double d){if(double.IsNaN(d)||double.IsInfinity(d))throw new FormatException("Non-finite JSON double");output.Append(d.ToString("R",CultureInfo.InvariantCulture));return;}
                if(item is int || item is long || item is uint || item is ulong || item is short || item is byte){output.Append(Convert.ToString(item,CultureInfo.InvariantCulture));return;}
                if(item is IDictionary dictionary)
                {
                    var keys=new List<string>();foreach(object key in dictionary.Keys){if(!(key is string s))throw new FormatException("JSON key must be string");keys.Add(s);}keys.Sort(StringComparer.Ordinal);
                    output.Append('{');bool first=true;foreach(string key in keys){if(!first)output.Append(',');first=false;String(key);output.Append(':');Write(dictionary[key],depth+1);}output.Append('}');return;
                }
                if(item is IEnumerable sequence)
                {
                    output.Append('[');bool first=true;foreach(object child in sequence){if(!first)output.Append(',');first=false;Write(child,depth+1);}output.Append(']');return;
                }
                Type type=item.GetType();if(!type.IsDefined(typeof(SerializableAttribute),false))throw new FormatException("Only explicitly serializable contract DTOs are allowed");
                output.Append('{');bool fieldFirst=true;
                foreach(var field in type.GetFields(BindingFlags.Public|BindingFlags.Instance))
                {
                    if(field.IsStatic || field.IsDefined(typeof(NonSerializedAttribute),false))continue;
                    if(!fieldFirst)output.Append(',');fieldFirst=false;String(field.Name);output.Append(':');Write(field.GetValue(item),depth+1);
                }
                output.Append('}');
            }
            Write(value,0);string result=output.ToString();if(Encoding.UTF8.GetByteCount(result)>maximumBytes)throw new FormatException("JSON byte size limit");return result;
        }
    }
}
