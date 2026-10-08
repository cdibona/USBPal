using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace USBPal {
    internal sealed class Preferences {
        public bool AutoUpdate=true;
        public static string Root { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"USBPal"); } }
        public static Preferences Load() { try { return new JavaScriptSerializer().Deserialize<Preferences>(File.ReadAllText(Path.Combine(Root,"settings.json")))??new Preferences(); } catch { return new Preferences(); } }
        public void Save() { Directory.CreateDirectory(Root); File.WriteAllText(Path.Combine(Root,"settings.json"),new JavaScriptSerializer().Serialize(this)); }
        public static bool Startup {
            get { using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) return key!=null&&key.GetValue("USBPal")!=null; }
            set { using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) { if(value) key.SetValue("USBPal","\""+Application.ExecutablePath+"\" --background"); else key.DeleteValue("USBPal",false); } }
        }
    }
    internal static class Program {
        [STAThread] static int Main(string[] args) {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if(args.Contains("--self-test")) return Tests.Run();
            if(args.Length==2&&args[0]=="--probe") { try { File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(Devices.Scan().Values.ToArray())); return 0; } catch(Exception e) { File.WriteAllText(args[1],e.ToString()); return 1; } }
            if(args.Length==2&&args[0]=="--runtime-test") return Tests.Runtime(args[1]);
            if(args.Length==2&&args[0]=="--verify-public-release") {
                string status=""; string file=new ReleaseUpdater(s=>status=s).Check(true).GetAwaiter().GetResult();
                File.WriteAllText(args[1],status+Environment.NewLine+(file??"No verified installer")); return file==null?1:0;
            }
            bool owner;
            using(var mutex=new Mutex(true,"Local\\USBPal."+Environment.UserName,out owner)) {
                if(!owner) { MessageBox.Show("USBPal is already recording. Open it from the UP tray icon.","USBPal"); return 0; }
                try { using(var context=new Tray(args.Contains("--show"))) Application.Run(context); return 0; }
                catch(Exception e) { MessageBox.Show(e.Message,"USBPal",MessageBoxButtons.OK,MessageBoxIcon.Error); return 1; }
            }
        }
    }
    internal sealed class Tray : ApplicationContext {
        readonly Recorder recorder;
        readonly Dashboard dashboard;
        readonly NotifyIcon icon;
        readonly Preferences preferences=Preferences.Load();
        readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer { Interval=1000 };
        DateTime nextUpdate=DateTime.UtcNow.AddSeconds(10);
        string staged;
        bool checking,exiting;
        public Tray(bool show) {
            var history=new History(Path.Combine(Preferences.Root,"Events"));
            recorder=new Recorder(history); dashboard=new Dashboard(recorder,history);
            dashboard.UpdateRequested+=delegate { CheckUpdates(); };
            var menu=new ContextMenuStrip();
            menu.Items.Add("Open USBPal",null,delegate { Open(); });
            menu.Items.Add("USBPal v"+ReleaseUpdater.Current.ToString(3)).Enabled=false;
            var startup=new ToolStripMenuItem("Record at sign-in") { Checked=Preferences.Startup,CheckOnClick=true };
            startup.Click+=delegate { try { Preferences.Startup=startup.Checked; } catch(Exception e) { startup.Checked=Preferences.Startup; dashboard.UpdateStatus(e.Message); } }; menu.Items.Add(startup);
            var auto=new ToolStripMenuItem("Automatic updates") { Checked=preferences.AutoUpdate,CheckOnClick=true };
            auto.Click+=delegate { preferences.AutoUpdate=auto.Checked; try { preferences.Save(); } catch(Exception e) { dashboard.UpdateStatus(e.Message); } }; menu.Items.Add(auto);
            menu.Items.Add("Check for updates",null,delegate { CheckUpdates(); });
            menu.Items.Add("Open history folder",null,delegate { Process.Start(history.Folder); });
            menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("Exit and stop recording",null,delegate { ExitThread(); });
            icon=new NotifyIcon { Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath),Text="USBPal • recording USB events",ContextMenuStrip=menu,Visible=true };
            icon.DoubleClick+=delegate { Open(); };
            SystemEvents.PowerModeChanged+=Power;
            timer.Tick+=delegate {
                icon.Text=recorder.Status.StartsWith("Recording •")?"USBPal • recording USB events":"USBPal • "+recorder.Status.Substring(0,Math.Min(50,recorder.Status.Length));
                if(preferences.AutoUpdate&&DateTime.UtcNow>=nextUpdate) CheckUpdates();
                if(staged!=null&&!dashboard.Visible&&!checking) Install();
            }; timer.Start(); if(show) Open();
        }
        void Power(object sender,PowerModeChangedEventArgs e) { try { recorder.Session("Power "+e.Mode,"Windows power transition; sleep can cause simultaneous device changes."); } catch(Exception ex) { recorder.Status="History error: "+ex.Message; } }
        void Open() { dashboard.Show(); if(dashboard.WindowState==FormWindowState.Minimized) dashboard.WindowState=FormWindowState.Normal; dashboard.Activate(); }
        async void CheckUpdates() {
            if(checking||exiting) return; checking=true; nextUpdate=DateTime.UtcNow.AddHours(6);
            try { string file=await new ReleaseUpdater(s=>{ if(!exiting) dashboard.UpdateStatus(s); }).Check(); if(!exiting&&file!=null) staged=file; }
            finally { checking=false; }
        }
        void Install() {
            string file=staged; staged=null;
            try { Process.Start(new ProcessStartInfo(file,"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /USBPALUPDATE") { UseShellExecute=true }); ExitThread(); }
            catch(Exception e) { dashboard.UpdateStatus("Update could not start: "+e.Message); }
        }
        protected override void ExitThreadCore() { if(exiting) return; exiting=true; timer.Stop(); SystemEvents.PowerModeChanged-=Power; icon.Visible=false; recorder.Dispose(); dashboard.AllowClose=true; dashboard.Close(); base.ExitThreadCore(); }
        protected override void Dispose(bool disposing) { if(disposing) { timer.Dispose(); icon.Dispose(); dashboard.Dispose(); } base.Dispose(disposing); }
    }
}
