using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace USBPal {
    internal sealed class Recorder : IDisposable {
        sealed class Notice { public uint Action; public string Id; public DateTime Time; }
        readonly ConcurrentQueue<Notice> pending=new ConcurrentQueue<Notice>();
        readonly AutoResetEvent wake=new AutoResetEvent(false);
        readonly History history;
        readonly DeviceState state;
        readonly object gate=new object();
        readonly Thread worker;
        DeviceNotifications notifications;
        volatile bool stopping;
        public volatile string Status="Starting recorder...";
        public volatile int Revision;
        public Recorder(History history) {
            this.history=history; state=new DeviceState(Save);
            worker=new Thread(Run) { IsBackground=true,Name="USBPal recorder" }; worker.Start();
        }
        void Save(UsbEvent e) { history.Append(e); Revision++; }
        public void Session(string kind,string message) { Save(new UsbEvent { Utc=DateTime.UtcNow.ToString("o"),Kind=kind,Source="Recorder",Device=new Device { Name=Environment.MachineName },Message=message }); }
        public Device[] Snapshot() { lock(gate) return state.Known.Values.ToArray(); }
        void Run() {
            try {
                notifications=new DeviceNotifications((action,id,time)=>{ pending.Enqueue(new Notice { Action=action,Id=id,Time=time }); wake.Set(); });
                Session("Recording started","No events are captured while USBPal is exited, signed out, or asleep.");
                bool baseline=true;
                while(!stopping) {
                    try {
                        var current=Devices.Scan();
                        lock(gate) {
                            if(baseline) { state.Reconcile(current,true); baseline=false; }
                            Notice n; while(pending.TryDequeue(out n)) state.Native(n.Action,n.Id,n.Time,current);
                            state.Reconcile(current,false);
                        }
                        Status="Recording • "+current.Count+" topology nodes • last scan "+DateTime.Now.ToString("HH:mm:ss");
                    } catch(Exception ex) { Status="Recording error: "+ex.Message; }
                    wake.WaitOne(3000);
                }
                Session("Recording stopped","USBPal exited or restarted for an update.");
            } catch(Exception ex) { Status="Recorder stopped: "+ex.Message; }
            finally { if(notifications!=null) notifications.Dispose(); }
        }
        public void Dispose() { stopping=true; wake.Set(); worker.Join(); wake.Dispose(); }
    }
}
