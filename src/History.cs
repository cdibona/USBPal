using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace USBPal {
    public sealed class UsbEvent {
        public string Utc,Kind,Source,Message;
        public Device Device;
        public DateTime Time { get { return DateTime.Parse(Utc,null,System.Globalization.DateTimeStyles.RoundtripKind); } }
    }
    internal sealed class History {
        public readonly string Folder;
        readonly object gate=new object();
        public History(string folder) { Folder=folder; Directory.CreateDirectory(folder); }
        public void Append(UsbEvent e) {
            lock(gate) File.AppendAllText(Path.Combine(Folder,e.Time.ToString("yyyy-MM-dd")+".jsonl"),new JavaScriptSerializer().Serialize(e)+Environment.NewLine,new UTF8Encoding(false));
        }
        public List<UsbEvent> Read(DateTime since,DateTime until,string search,string selected,bool descendants,out int damaged) {
            var result=new List<UsbEvent>(); damaged=0; var serializer=new JavaScriptSerializer();
            foreach(string file in Directory.GetFiles(Folder,"*.jsonl").OrderBy(f=>f)) {
                DateTime day; if(!DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file),"yyyy-MM-dd",null,System.Globalization.DateTimeStyles.None,out day)||day.Date<since.Date||day.Date>until.Date) continue;
                using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)) using(var reader=new StreamReader(stream)) {
                    string line; while((line=reader.ReadLine())!=null) {
                        UsbEvent e; try { e=serializer.Deserialize<UsbEvent>(line); if(e==null||e.Device==null||e.Utc==null||e.Device.Id==null||e.Device.Ancestors==null) throw new InvalidDataException(); if(e.Time<since||e.Time>until) continue; }
                        catch { damaged++; continue; }
                        if(!Matches(e,search,selected,descendants)) continue; result.Add(e);
                    }
                }
            }
            return result;
        }
        internal static bool Matches(UsbEvent e,string search,string selected,bool descendants) {
            if(!string.IsNullOrEmpty(selected)&&!string.Equals(e.Device.Id,selected,StringComparison.OrdinalIgnoreCase)&&!(descendants&&e.Device.Ancestors.Any(a=>string.Equals(a,selected,StringComparison.OrdinalIgnoreCase)))) return false;
            return string.IsNullOrWhiteSpace(search)||string.Join(" ",new[]{e.Kind,e.Message,e.Device.Name,e.Device.Id,e.Device.Parent,e.Device.Manufacturer,e.Device.Location,string.Join(" ",e.Device.Ancestors)}).IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0;
        }
        internal static string Csv(string s) { s=s??""; if(s.Length>0&&"=+-@\t\r".Contains(s[0])) s="'"+s; return "\""+s.Replace("\"","\"\"")+"\""; }
        public static void Export(string path,IEnumerable<UsbEvent> events) {
            using(var writer=new StreamWriter(path,false,new UTF8Encoding(true))) {
                writer.WriteLine("utc,event,source,device,instance_id,parent_id,ancestors,location,problem,message");
                foreach(var e in events) writer.WriteLine(string.Join(",",new[]{e.Utc,e.Kind,e.Source,e.Device.Name,e.Device.Id,e.Device.Parent,string.Join(" > ",e.Device.Ancestors),e.Device.Location,e.Device.Problem.ToString(),e.Message}.Select(Csv)));
            }
        }
    }
    internal sealed class DeviceState {
        public readonly Dictionary<string,Device> Known=new Dictionary<string,Device>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string,Queue<DateTime>> removals=new Dictionary<string,Queue<DateTime>>(StringComparer.OrdinalIgnoreCase);
        readonly Action<UsbEvent> emit;
        public DeviceState(Action<UsbEvent> emit) { this.emit=emit; }
        void Write(Device d,string kind,string source,DateTime time,string message) { emit(new UsbEvent { Utc=time.ToString("o"),Kind=kind,Source=source,Device=d,Message=message }); }
        public void Transition(Device device,bool present,DateTime time,string source,bool baseline) {
            Device old; bool existed=Known.TryGetValue(device.Id,out old);
            // Store separate objects so already-created events keep their original state.
            var d=new Device { Id=device.Id,Parent=device.Parent,Name=device.Name,Class=device.Class,Manufacturer=device.Manufacturer,Location=device.Location,Service=device.Service,Ancestors=device.Ancestors,Problem=device.Problem,Present=present };
            Known[d.Id]=d;
            if(baseline) { Write(d,"Observed",source,time,"Present when recording began; not a connection event."); return; }
            if(existed&&old.Present==present) { if(old.Problem!=d.Problem) Write(d,"Problem changed",source,time,"Windows problem code "+d.Problem); return; }
            Write(d,present?"Connected":"Disconnected",source,time,source=="Reconcile"?"Observed state change; exact transition time unavailable.":"Windows device-instance notification.");
            if(!present) {
                Queue<DateTime> q; if(!removals.TryGetValue(d.Id,out q)) removals[d.Id]=q=new Queue<DateTime>();
                q.Enqueue(time); while(q.Count>0&&time-q.Peek()>TimeSpan.FromMinutes(5)) q.Dequeue();
                if(q.Count>=3) Write(d,"Flapping",source,time,q.Count+" disconnects within five minutes. Inspect this device and upstream hubs; correlation does not prove the cause.");
            }
        }
        public void Native(uint action,string id,DateTime time,Dictionary<string,Device> fresh) {
            Device d; if(!fresh.TryGetValue(id,out d)&&!Known.TryGetValue(id,out d)) {
                if(!id.StartsWith("USB\\",StringComparison.OrdinalIgnoreCase)&&!id.StartsWith("USB4\\",StringComparison.OrdinalIgnoreCase)) return;
                d=new Device { Id=id,Name=id }; // Preserve even a device that vanished before enumeration.
            }
            if(action==9) Transition(d,false,time,"Notification",false);
            else if(action==8) Transition(d,true,time,"Notification",false);
            else if(action==7) Write(d,"Enumerated","Notification",time,"Windows enumerated device instance.");
        }
        public void Reconcile(Dictionary<string,Device> current,bool baseline) {
            DateTime now=DateTime.UtcNow;
            foreach(var d in current.Values) Transition(d,true,now,"Reconcile",baseline);
            foreach(var d in Known.Values.Where(d=>d.Present&&!current.ContainsKey(d.Id)).ToArray()) Transition(d,false,now,"Reconcile",false);
        }
    }
}
