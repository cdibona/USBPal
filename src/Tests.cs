using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace USBPal {
    internal static class Tests {
        static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
        public static int Run() {
            string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-history-"+Guid.NewGuid().ToString("N"));
            try {
                var events=new List<UsbEvent>(); var state=new DeviceState(events.Add);
                var hub=new Device { Id="USB\\HUB",Name="Hub",Parent="PCI\\HOST",Ancestors=new[]{"PCI\\HOST"},Present=true };
                var device=new Device { Id="USB\\DEVICE",Name="Keyboard",Parent=hub.Id,Ancestors=new[]{hub.Id,"PCI\\HOST"},Present=true };
                DateTime t=DateTime.UtcNow;
                state.Transition(hub,true,t,"Reconcile",true); state.Transition(device,true,t,"Reconcile",true);
                Check(events.All(e=>e.Kind=="Observed"),"Baseline invented connections");
                for(int i=0;i<3;i++) { state.Transition(device,false,t.AddSeconds(i*2),"Notification",false); state.Transition(device,false,t.AddSeconds(i*2),"Reconcile",false); state.Transition(device,true,t.AddSeconds(i*2+1),"Notification",false); }
                Check(events.Count(e=>e.Kind=="Disconnected")==3,"Duplicate removals");
                Check(events.Count(e=>e.Kind=="Flapping")==1,"Flapping threshold");
                Check(!events.First(e=>e.Kind=="Disconnected").Device.Present,"Event mutated after reconnect");
                var removed=events.First(e=>e.Kind=="Disconnected");
                Check(History.Matches(removed,"keyboard",hub.Id,true),"Historical descendant filter");
                Check(!History.Matches(removed,"",hub.Id,false),"Exact device filter");
                Check(History.Matches(removed,"PCI\\HOST","",true),"Ancestor search");
                state.Transition(device,false,t.AddMinutes(6),"Notification",false);
                Check(events.Count(e=>e.Kind=="Flapping")==1,"Expired flap window");
                device.Problem=43; state.Transition(device,true,t.AddMinutes(7),"Reconcile",false); device.Problem=0; state.Transition(device,true,t.AddMinutes(8),"Reconcile",false);
                Check(events.Any(e=>e.Kind=="Problem changed"),"Problem transitions");
                var nativeEvents=new List<UsbEvent>(); var native=new DeviceState(nativeEvents.Add);
                var fresh=new Dictionary<string,Device>(StringComparer.OrdinalIgnoreCase) { { device.Id,device } };
                native.Native(8,device.Id,t,fresh); native.Native(9,device.Id,t.AddMilliseconds(10),fresh); native.Native(8,device.Id,t.AddMilliseconds(20),fresh); native.Native(8,device.Id,t.AddMilliseconds(30),fresh);
                Check(nativeEvents.Count(e=>e.Kind=="Connected")==2&&nativeEvents.Count(e=>e.Kind=="Disconnected")==1&&nativeEvents.Count(e=>e.Kind=="Started")==3,"Rapid native flap/restart retention");
                native.Native(9,"USB\\VANISHED",t,new Dictionary<string,Device>());
                Check(nativeEvents.Last().Device.Id=="USB\\VANISHED","Unresolved transient USB device");
                var store=new History(folder); foreach(var e in events) store.Append(e);
                File.AppendAllText(Directory.GetFiles(folder,"*.jsonl")[0],"{broken\n"); int bad;
                var read=store.Read(t.AddSeconds(-1),t.AddHours(1),"","",true,out bad);
                Check(read.Count==events.Count&&bad==1,"JSONL round trip / corruption recovery");
                Check(store.Read(t.AddMinutes(7),t.AddHours(1),"","",true,out bad).Count==2,"UTC date range");
                Check(History.Csv("=1+1")=="\"'=1+1\"","CSV formula escaping");
                History.Export(Path.Combine(folder,"export.csv"),read); Check(File.ReadAllLines(Path.Combine(folder,"export.csv")).Length==events.Count+1,"CSV export count");
                Check(!ReleaseUpdater.ValidDownload(new Uri("https://evil.example/payload")),"Updater host restriction");
                Check(!ReleaseUpdater.ValidDownload(new Uri("http://api.github.com/repos/cdibona/USBPal/releases/assets/1")),"Updater HTTPS restriction");
                Check(ReleaseUpdater.ValidDownload(new Uri("https://api.github.com/repos/cdibona/USBPal/releases/assets/1")),"Valid update URL");
                string json="{\"draft\":false,\"prerelease\":false,\"tag_name\":\"v0.2.0\",\"assets\":[{\"name\":\"USBPal-Setup-0.2.0-win-x64.exe\",\"url\":\"https://api.github.com/repos/cdibona/USBPal/releases/assets/1\",\"size\":123,\"digest\":\"sha256:"+new string('a',64)+"\"}]}";
                Check(ReleaseUpdater.Parse(json).Version==new Version(0,2,0,0),"Release version parsing");
                Check(ReleaseUpdater.Parse(json.Replace("\"prerelease\":false","\"prerelease\":true"))==null,"Skip prereleases");
                Check(!ReleaseUpdater.Verify(Path.Combine(folder,"export.csv"),new string('a',64),new FileInfo(Path.Combine(folder,"export.csv")).Length),"Reject bad digest");
                var names=new DeviceNames(folder); device.Manufacturer="Acme Hardware";
                Check(names.Get(device)=="Acme Hardware","Default manufacturer nickname");
                names.Set(device,"Desk keyboard"); Check(new DeviceNames(folder).Get(device)=="Desk keyboard","Persisted nickname");
                Check(device.Name=="Keyboard"&&device.Manufacturer=="Acme Hardware","Nickname changed device metadata");
                var same=new Device { Id=device.Id.ToLowerInvariant(),Name="Keyboard" }; Check(names.Get(same)=="Desk keyboard","Nickname identity casing");
                names.Set(device,""); Check(new DeviceNames(folder).Get(device)=="Acme Hardware","Reset nickname");
                Check(DeviceNames.Default(new Device { Name="USB input device",Manufacturer="(Standard system devices)" })=="USB input device","Unknown manufacturer fallback");
                bool rejected=false; try { names.Set(device,new string('a',101)); } catch(ArgumentException) { rejected=true; } Check(rejected,"Nickname length validation");
                var activity=ActivityLog.Build(events,names);
                Check(activity.Any(e=>e.Text.Contains("reconnected after 1.0 seconds")),"Readable reconnect duration");
                Check(activity.Any(e=>e.Alert&&e.Text.Contains("POSSIBLE FLAPPING")),"Readable flapping alert");
                Check(!activity.Any(e=>e.Event.Kind=="Observed"||e.Event.Kind=="Enumerated"),"Activity suppresses baseline noise");
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-result.txt"),"PASS: device transitions, flapping, history/CSV, updater validation, nickname defaults/persistence/reset/identity, unchanged hardware metadata, readable reconnects and flap alerts."); return 0;
            } catch(Exception e) { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-result.txt"),e.ToString()); return 1; }
        }
        public static int Runtime(string folder) {
            Directory.CreateDirectory(folder);
            try {
                var history=new History(Path.Combine(folder,"Events"));
                var demo=new Device { Id="USB\\USBPAL_UI_TEST",Name="Example USB hub",Manufacturer="Example Hardware",Parent="",Present=true };
                var demoState=new DeviceState(history.Append); DateTime demoTime=DateTime.UtcNow.AddSeconds(-30);
                demoState.Transition(demo,true,demoTime,"Notification",true);
                for(int i=0;i<3;i++) { demoState.Transition(demo,false,demoTime.AddSeconds(i*2+1),"Notification",false); demoState.Transition(demo,true,demoTime.AddSeconds(i*2+2),"Notification",false); }
                using(var recorder=new Recorder(history)) {
                    Thread.Sleep(5000); Check(recorder.Status.StartsWith("Recording •"),recorder.Status); Check(recorder.Snapshot().Length>0,"No real USB topology");
                    using(var form=new Dashboard(recorder,history)) {
                        form.Show(); Pump(3);
                        TreeNode node=form.Topology.Nodes.Find(demo.Id,true).Single(); form.Topology.SelectedNode=node;
                        form.NicknameEntry.Text="Desk hub"; form.SaveNicknameButton.PerformClick();
                        Check(new DeviceNames(folder).Get(demo)=="Desk hub","UI nickname persisted");
                        Check(node.Text.Contains("Desk hub"),"Tree nickname updated");
                        form.SearchEntry.Text="unmatched-search-text";
                        Check(form.HistoryTable.RowCount==0&&form.ActivityTable.RowCount>0,"All-bus log must ignore device/search filters");
                        Check(form.ActivitySummary.Contains("POSSIBLE FLAPPING")&&form.ActivitySummary.Contains("Desk hub"),"Global flap summary uses nickname");
                        form.SearchEntry.Text="Desk hub"; Check(form.HistoryTable.RowCount>0,"Search by nickname");
                        int refreshes=form.HistoryTable.RefreshCount; node.Collapse();
                        form.NicknameEntry.Text="Unsaved typing"; Pump(6);
                        Check(object.ReferenceEquals(node,form.Topology.Nodes.Find(demo.Id,true).Single()),"Refresh recreated tree node");
                        Check(form.Topology.SelectedNode==node&&!node.IsExpanded,"Refresh changed selection/expansion");
                        Check(form.HistoryTable.RefreshCount==refreshes,"Unchanged table was repainted");
                        Check(form.NicknameEntry.Text=="Unsaved typing","Refresh overwrote nickname edit");
                        form.ResetNicknameButton.PerformClick(); Check(new DeviceNames(folder).Get(demo)=="Example Hardware","UI default nickname reset");
                        form.NicknameEntry.Text="Desk hub"; form.SaveNicknameButton.PerformClick(); form.SearchEntry.Text="";
                        using(var bitmap=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size)); bitmap.Save(Path.Combine(folder,"dashboard.png")); }
                        form.Close(); Check(!form.Visible&&!form.IsDisposed,"Close must hide dashboard"); form.Show(); Check(form.Visible,"Reopen dashboard"); form.AllowClose=true; form.Close();
                    }
                }
                int bad; var events=history.Read(DateTime.MinValue,DateTime.MaxValue,"","",true,out bad);
                Check(events.Any(e=>e.Kind=="Recording started")&&events.Any(e=>e.Kind=="Recording stopped")&&events.Any(e=>e.Kind=="Observed"),"Lifecycle persistence");
                File.WriteAllText(Path.Combine(folder,"runtime-result.txt"),"PASS: live topology, history, nickname edit/save/reset/search, independent all-bus activity and flapping summary, stable tree identity/selection and unchanged table across refresh, edit preservation, dashboard render, close/reopen, recorder shutdown. Observed nodes: "+events.Count(e=>e.Kind=="Observed")); return 0;
            } catch(Exception e) { File.WriteAllText(Path.Combine(folder,"runtime-result.txt"),e.ToString()); return 1; }
        }
        static void Pump(int seconds) { DateTime until=DateTime.UtcNow.AddSeconds(seconds); while(DateTime.UtcNow<until) { Application.DoEvents(); Thread.Sleep(25); } }
    }
}
