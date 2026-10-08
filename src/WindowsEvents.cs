using System;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Xml;

namespace USBPal {
    // The existing System log supplements PnP notifications; no debug channels are enabled.
    internal sealed class WindowsEvents : IDisposable {
        EventLogWatcher watcher;
        readonly Func<Device[]> snapshot; readonly Action<UsbEvent> save;
        public volatile string Status="";
        public WindowsEvents(Func<Device[]> snapshot,Action<UsbEvent> save) {
            this.snapshot=snapshot; this.save=save;
            try {
                var query=new EventLogQuery("System",PathType.LogName,"*[System[Provider[@Name='Microsoft-Windows-Kernel-PnP'] or Provider[@Name='Microsoft-Windows-UserPnp'] or Provider[@Name='Microsoft-Windows-DriverFrameworks-UserMode'] or Provider[@Name='Microsoft-Windows-USB-USBHUB3'] or Provider[@Name='Microsoft-Windows-USB-UCX']]]");
                watcher=new EventLogWatcher(query); watcher.EventRecordWritten+=Received; watcher.Enabled=true;
            } catch(Exception e) { Status=" • System event log unavailable: "+e.Message; }
        }
        void Received(object sender,EventRecordWrittenEventArgs args) {
            if(args.EventException!=null) { Status=" • System event log error: "+args.EventException.Message; return; }
            if(args.EventRecord==null) return;
            using(var record=args.EventRecord) try {
                var xml=new XmlDocument(); xml.LoadXml(record.ToXml());
                var data=xml.SelectNodes("//*[local-name()='EventData']/*[local-name()='Data']").Cast<XmlNode>().Select(n=>n.InnerText).ToArray();
                var device=snapshot().FirstOrDefault(d=>data.Any(v=>v.Equals(d.Id,StringComparison.OrdinalIgnoreCase)));
                if(device==null) { string id=data.FirstOrDefault(v=>v.StartsWith("USB\\",StringComparison.OrdinalIgnoreCase)||v.StartsWith("USB4\\",StringComparison.OrdinalIgnoreCase)); if(id==null) return; device=new Device { Id=id,Name=id }; }
                string message; try { message=record.FormatDescription(); } catch { message=string.Join("; ",data); }
                save(new UsbEvent { Utc=(record.TimeCreated??DateTime.Now).ToUniversalTime().ToString("o"),Kind="Windows event",Source=record.ProviderName+" / "+record.Id,Device=device,Message=(message??string.Join("; ",data))+" [Record "+record.RecordId+"]" });
            } catch(Exception e) { Status=" • System event read error: "+e.Message; }
        }
        public void Dispose() { if(watcher!=null) { watcher.Enabled=false; watcher.EventRecordWritten-=Received; watcher.Dispose(); watcher=null; } }
    }
}
