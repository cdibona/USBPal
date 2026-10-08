using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace USBPal {
    // Display metadata only. No Configuration Manager, registry or USB writes.
    internal sealed class DeviceNames {
        readonly string path;
        Dictionary<string,string> names=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        public string LoadError { get; private set; }
        public DeviceNames(string folder) {
            path=Path.Combine(folder,"device-nicknames.json");
            try { if(File.Exists(path)) names=new Dictionary<string,string>(new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(File.ReadAllText(path)),StringComparer.OrdinalIgnoreCase); }
            catch(Exception e) { LoadError="Could not read device nicknames: "+e.Message; }
        }
        public static string Default(Device d) {
            string maker=(d.Manufacturer??"").Trim();
            if(maker.Length>0&&!maker.StartsWith("(")&&!maker.StartsWith("Generic",StringComparison.OrdinalIgnoreCase)&&!maker.StartsWith("Standard",StringComparison.OrdinalIgnoreCase)&&!maker.Equals("Microsoft",StringComparison.OrdinalIgnoreCase)&&!maker.Equals("Unknown",StringComparison.OrdinalIgnoreCase)) return maker;
            return string.IsNullOrWhiteSpace(d.Name)?d.Id:d.Name;
        }
        public string Get(Device d) { string value; return names.TryGetValue(d.Id,out value)&&!string.IsNullOrWhiteSpace(value)?value:Default(d); }
        public string Label(Device d) { string nick=Get(d); return nick.Equals(d.Name,StringComparison.OrdinalIgnoreCase)?nick:nick+" · "+d.Name; }
        public void Set(Device d,string nickname) {
            string value=(nickname??"").Trim();
            if(d.Id.Length==0) throw new InvalidOperationException("Select a device first.");
            if(value.Length>100||value.IndexOfAny(new[]{'\r','\n','\t','\0'})>=0) throw new ArgumentException("Use a nickname of at most 100 characters on one line.");
            var next=new Dictionary<string,string>(names,StringComparer.OrdinalIgnoreCase);
            if(value.Length==0) next.Remove(d.Id); else next[d.Id]=value;
            Directory.CreateDirectory(Path.GetDirectoryName(path)); string temp=path+".tmp";
            File.WriteAllText(temp,new JavaScriptSerializer().Serialize(next));
            if(File.Exists(path)) File.Replace(temp,path,path+".bak"); else File.Move(temp,path);
            names=next; LoadError=null;
        }
    }
    internal sealed class ActivityLine {
        public UsbEvent Event; public string Text; public bool Alert;
    }
    internal static class ActivityLog {
        public static List<ActivityLine> Build(IEnumerable<UsbEvent> events,DeviceNames names) {
            var lines=new List<ActivityLine>(); var removed=new Dictionary<string,DateTime>(StringComparer.OrdinalIgnoreCase);
            foreach(var e in System.Linq.Enumerable.OrderBy(events,x=>x.Time)) {
                string name=names.Get(e.Device), text=null; bool alert=false;
                if(e.Kind=="Disconnected") { removed[e.Device.Id]=e.Time; text=name+" disconnected."; }
                else if(e.Kind=="Connected") { DateTime time; text=name+(removed.TryGetValue(e.Device.Id,out time)?" reconnected after "+Duration(e.Time-time)+".":" connected."); removed.Remove(e.Device.Id); }
                else if(e.Kind=="Flapping") { text="POSSIBLE FLAPPING — "+name+": "+e.Message; alert=true; }
                else if(e.Kind=="Problem changed") { text=name+(e.Device.Problem==0?": Windows cleared the device problem.":": Windows reports device problem "+e.Device.Problem+"."); alert=e.Device.Problem!=0; }
                else if(e.Kind=="Windows event") text=name+": "+(e.Message??"").Replace("\r"," ").Replace("\n"," ");
                else if(e.Kind=="Recording started") text="USBPal started watching all USB buses.";
                else if(e.Kind=="Recording stopped") text="USBPal stopped recording. Events during this gap are unavailable.";
                else if(e.Kind.StartsWith("Power ")) text="Computer power state: "+e.Kind.Substring(6)+". Several devices may change together.";
                if(text!=null) lines.Add(new ActivityLine { Event=e,Text=text+(e.Source=="Reconcile"?" (Detected by scan; exact time unknown.)":""),Alert=alert });
            }
            lines.Reverse(); return lines;
        }
        static string Duration(TimeSpan time) { return time.TotalSeconds<60?Math.Max(0,time.TotalSeconds).ToString("0.0")+" seconds":time.TotalMinutes<60?time.TotalMinutes.ToString("0.0")+" minutes":time.TotalHours.ToString("0.0")+" hours"; }
    }
}
