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
        readonly System.Diagnostics.Stopwatch uptime=System.Diagnostics.Stopwatch.StartNew();
        public TimeSpan Uptime { get { return uptime.Elapsed; } }
        readonly Queue<UsbEvent> outbox=new Queue<UsbEvent>();
        readonly object writeGate=new object();
        DeviceNotifications notifications;
        WindowsEvents windowsEvents;
        volatile bool stopping;
        public volatile string Status="Starting recorder...";
        public volatile int Revision;
        public Recorder(History history) {
            this.history=history; state=new DeviceState(Save);
            worker=new Thread(Run) { IsBackground=true,Name="USBPal recorder" }; worker.Start();
        }
        void Save(UsbEvent e) { lock(writeGate) outbox.Enqueue(e); Revision++; }
        void Flush() { lock(writeGate) while(outbox.Count>0) { history.Append(outbox.Peek()); outbox.Dequeue(); } }
        public void Session(string kind,string message) { Save(new UsbEvent { Utc=DateTime.UtcNow.ToString("o"),Kind=kind,Source="Recorder",Device=new Device { Name=Environment.MachineName },Message=message }); }
        public Device[] Snapshot() { lock(gate) return state.Known.Values.ToArray(); }
        void Run() {
            try {
                notifications=new DeviceNotifications((action,id,time)=>{ pending.Enqueue(new Notice { Action=action,Id=id,Time=time }); wake.Set(); });
                windowsEvents=new WindowsEvents(Snapshot,Save);
                Session("Recording started","No events are captured while USBPal is exited, signed out, or asleep.");
                bool baseline=true;
                while(!stopping) {
                    try {
                        var batch=new List<Notice>(); Notice n; while(pending.TryDequeue(out n)) batch.Add(n);
                        var current=Devices.Scan();
                        lock(gate) {
                            if(baseline) { state.Reconcile(current,true); baseline=false; }
                            foreach(var notice in batch) state.Native(notice.Action,notice.Id,notice.Time,current);
                            // Notifications arriving during enumeration belong to the next scan.
                            // Never reconcile an older snapshot over a newer removal notification.
                            if(pending.IsEmpty) state.Reconcile(current,false);
                        }
                        Flush();
                        Status="Recording • "+current.Count+" topology nodes • last scan "+DateTime.Now.ToString("HH:mm:ss")+windowsEvents.Status;
                    } catch(Exception ex) { Status="Recording error: "+ex.Message; }
                    wake.WaitOne(3000);
                }
                notifications.Dispose(); windowsEvents.Dispose();
                Session("Recording stopped","USBPal exited or restarted for an update.");
                Flush();
            } catch(Exception ex) { Status="Recorder stopped: "+ex.Message; }
            finally { if(notifications!=null) notifications.Dispose(); if(windowsEvents!=null) windowsEvents.Dispose(); }
        }
        public void Dispose() { stopping=true; wake.Set(); worker.Join(); wake.Dispose(); }
    }
}
