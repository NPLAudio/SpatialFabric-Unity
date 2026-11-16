// Bounded strict RFC4180 data codec; no Unity APIs, scene lookup or mutation. MIT.
using System;
using System.Collections.Generic;
using System.Text;
namespace NPLAudio.SpatialFabric.Routing
{
    public static class RoutingCsvCodec
    {
        public const int MaxBytes=4*1024*1024,MaxRows=16385,MaxField=16384,MaxColumns=64;
        public static List<string[]> Parse(string text)
        {
            if(text==null || Encoding.UTF8.GetByteCount(text)>MaxBytes || text.IndexOf('\0')>=0)throw new FormatException("CSV is null, oversized or contains NUL");
            if(text.StartsWith("\ufeff",StringComparison.Ordinal))text=text.Substring(1);
            var rows=new List<string[]>();var fields=new List<string>();var field=new StringBuilder();bool quoted=false,closed=false;
            void AddField() {if(field.Length>MaxField || fields.Count>=MaxColumns)throw new FormatException("CSV field/column limit exceeded");fields.Add(field.ToString());field.Clear();closed=false;}
            void AddRow() {if(rows.Count>=MaxRows)throw new FormatException("CSV record limit exceeded");rows.Add(fields.ToArray());fields.Clear();}
            for(int i=0;i<text.Length;++i)
            {
                char c=text[i];
                if(quoted)
                {
                    if(c=='"') {if(i+1<text.Length && text[i+1]=='"'){field.Append('"');++i;}else {quoted=false;closed=true;}}
                    else field.Append(c);
                }
                else if(c==',' || c=='\r' || c=='\n') {AddField();if(c!=','){AddRow();if(c=='\r' && i+1<text.Length && text[i+1]=='\n')++i;}}
                else if(c=='"') {if(field.Length!=0 || closed)throw new FormatException("Unexpected CSV quote");quoted=true;}
                else {if(closed)throw new FormatException("Characters after closing CSV quote");field.Append(c);}
                if(field.Length>MaxField)throw new FormatException("CSV field limit exceeded");
            }
            if(quoted)throw new FormatException("Unclosed CSV quote");
            if(field.Length>0 || fields.Count>0 || closed){AddField();AddRow();}
            return rows;
        }
        public static string Write(IEnumerable<string[]> rows)
        {
            var text=new StringBuilder();int count=0;
            foreach(var row in rows)
            {
                if(++count>MaxRows || row==null || row.Length>MaxColumns)throw new FormatException("CSV record/column limit exceeded");
                for(int i=0;i<row.Length;++i)
                {
                    if(i>0)text.Append(',');string value=row[i]??"";
                    if(value.Length>MaxField || value.IndexOf('\0')>=0)throw new FormatException("Invalid CSV field");
                    if(value.IndexOfAny(new[]{',','"','\r','\n'})>=0)text.Append('"').Append(value.Replace("\"","\"\"")).Append('"');else text.Append(value);
                }
                text.Append("\r\n");if(text.Length>MaxBytes)throw new FormatException("CSV size limit exceeded");
            }
            string result=text.ToString();if(Encoding.UTF8.GetByteCount(result)>MaxBytes)throw new FormatException("CSV byte size limit exceeded");return result;
        }
        public static Dictionary<string,int> Header(string[] actual,string[] required)
        {
            var map=new Dictionary<string,int>(StringComparer.Ordinal);
            foreach(string name in required)map.Add(name,-1);
            for(int i=0;i<actual.Length;++i)
            {
                if(!map.TryGetValue(actual[i],out int old) || old>=0)throw new FormatException("Unknown/duplicate CSV column: "+actual[i]);map[actual[i]]=i;
            }
            foreach(var pair in map)if(pair.Value<0)throw new FormatException("Missing CSV column: "+pair.Key);
            return map;
        }
    }
}
