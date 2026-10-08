using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace USBPal {
    internal sealed class FilterChoice : Button {
        public readonly List<object> Items=new List<object>();
        readonly ContextMenuStrip menu=new ContextMenuStrip();
        int selected=-1;
        public event EventHandler SelectedIndexChanged;
        public object SelectedItem { get { return selected<0?null:Items[selected]; } }
        public int SelectedIndex { get { return selected; } set { if(selected==value) return; selected=value; Text=Items[value]+" ▾"; if(SelectedIndexChanged!=null) SelectedIndexChanged(this,EventArgs.Empty); } }
        public FilterChoice() { Height=25; }
        protected override void OnClick(EventArgs e) { base.OnClick(e); menu.Items.Clear(); for(int i=0;i<Items.Count;i++) { int index=i; var item=new ToolStripMenuItem(Items[i].ToString()) { Checked=i==selected }; item.Click+=delegate { SelectedIndex=index; }; menu.Items.Add(item); } menu.Show(this,new Point(0,Height)); }
        protected override void Dispose(bool disposing) { if(disposing) menu.Dispose(); base.Dispose(disposing); }
    }
    internal sealed class Dashboard : Form {
        readonly Recorder recorder; readonly History history; readonly DeviceNames names;
        readonly Dictionary<string,TreeNode> nodes=new Dictionary<string,TreeNode>(StringComparer.OrdinalIgnoreCase);
        readonly TreeNode root=new TreeNode(Environment.MachineName+" / motherboard") { Name="" };
        readonly TextBox nickname=new TextBox { Width=240,MaxLength=100,AccessibleName="Device nickname" };
        readonly Button saveName=new Button { Text="Save nickname",AutoSize=true,Enabled=false };
        readonly Button resetName=new Button { Text="Use default",AutoSize=true,Enabled=false };
        readonly Label activityStatus=new Label { Dock=DockStyle.Top,Height=30,Padding=new Padding(12,6,0,0) };
        readonly EventTable activity=new EventTable { Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,BorderStyle=BorderStyle.None };
        readonly TreeView tree=new TreeView { Dock=DockStyle.Fill,HideSelection=false,ShowNodeToolTips=true,BorderStyle=BorderStyle.None };
        readonly TextBox search=new TextBox { Width=250,AccessibleName="Search event history" };
        readonly FilterChoice range=new FilterChoice { Width=115,AccessibleName="History time range" };
        readonly FilterChoice kind=new FilterChoice { Width=130,AccessibleName="Event type" };
        readonly CheckBox descendants=new CheckBox { Text="Include descendants",Checked=true,AutoSize=true };
        readonly Label metrics=new Label { Dock=DockStyle.Top,Height=62,Padding=new Padding(16,12,8,0),Font=new Font("Segoe UI",12),ForeColor=Color.FromArgb(86,225,195) };
        readonly Label status=new Label { Dock=DockStyle.Bottom,Height=30,Padding=new Padding(12,5,0,0) };
        readonly Label update=new Label { Dock=DockStyle.Bottom,Height=30,Padding=new Padding(12,5,0,0),Text="Updates are checked on GitHub. Preferences are in the tray menu." };
        readonly TextBox details=new TextBox { Dock=DockStyle.Bottom,Height=125,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BorderStyle=BorderStyle.None };
        readonly EventTable grid=new EventTable { Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,BackgroundColor=Color.FromArgb(23,32,43),BorderStyle=BorderStyle.None,AutoGenerateColumns=false };
        readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer { Interval=1000 };
        List<UsbEvent> events=new List<UsbEvent>(),filtered=new List<UsbEvent>(),allBuses=new List<UsbEvent>();
        string selected=""; bool loading,rebuilding,dirty=true; int ticks,damaged;
        public bool AllowClose;
        internal TreeView Topology { get { return tree; } }
        internal EventTable HistoryTable { get { return grid; } }
        internal EventTable ActivityTable { get { return activity; } }
        internal TextBox NicknameEntry { get { return nickname; } }
        internal TextBox SearchEntry { get { return search; } }
        internal Button SaveNicknameButton { get { return saveName; } }
        internal Button ResetNicknameButton { get { return resetName; } }
        internal string ActivitySummary { get { return activityStatus.Text; } }
        public event Action UpdateRequested;
        public Dashboard(Recorder recorder,History history) {
            this.recorder=recorder; this.history=history; names=new DeviceNames(Path.GetDirectoryName(history.Folder));
            Text="USBPal • USB event explorer"; Size=new Size(1380,960); MinimumSize=new Size(1050,800); StartPosition=FormStartPosition.CenterScreen; AutoScaleMode=AutoScaleMode.Dpi; DoubleBuffered=true;
            Font=new Font("Segoe UI",9); BackColor=Color.FromArgb(16,23,32); ForeColor=Color.FromArgb(222,233,242); Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            var title=new Label { Text="UP   USBPal  /  USB event explorer     v"+ReleaseUpdater.Current.ToString(3),Dock=DockStyle.Top,Height=64,Padding=new Padding(16,18,0,0),Font=new Font("Segoe UI",18,FontStyle.Bold) };
            var toolbar=new FlowLayoutPanel { Dock=DockStyle.Top,Height=76,Padding=new Padding(12,8,4,4),AutoScroll=true,WrapContents=true };
            range.Items.AddRange(new object[]{"Last hour","Last 24 hours","Last 7 days","All history"}); range.SelectedIndex=1;
            kind.Items.AddRange(new object[]{"All events","Connected","Disconnected","Flapping","Problem changed","Enumerated","Started","Windows event","Observed"}); kind.SelectedIndex=0;
            toolbar.Controls.AddRange(new Control[]{new Label { Text="Search",AutoSize=true,Margin=new Padding(0,5,6,0) },search,range,kind,descendants});
            var clear=new Button { Text="All devices",AutoSize=true }; clear.Click+=delegate { selected=""; tree.SelectedNode=null; EditDevice(null); ApplyFilter(); }; toolbar.Controls.Add(clear);
            var export=new Button { Text="Export CSV",AutoSize=true }; export.Click+=delegate { Export(); }; toolbar.Controls.Add(export);
            var updates=new Button { Text="Check updates",AutoSize=true }; updates.Click+=delegate { if(UpdateRequested!=null) UpdateRequested(); }; toolbar.Controls.Add(updates);
            var split=new SplitContainer { Dock=DockStyle.Fill,Size=new Size(1200,500),SplitterDistance=420,Panel1MinSize=220,Panel2MinSize=400 };
            split.Panel1.Controls.Add(tree); split.Panel1.Controls.Add(new Label { Text="USB TOPOLOGY  •  select a device or hub",Dock=DockStyle.Top,Height=30,Padding=new Padding(8) });
            split.Panel2.Controls.Add(grid); split.Panel2.Controls.Add(details);
            var nameBar=new FlowLayoutPanel { Dock=DockStyle.Top,Height=64,Padding=new Padding(4),WrapContents=true };
            nameBar.Controls.AddRange(new Control[]{new Label { Text="Local nickname",AutoSize=true,Margin=new Padding(0,6,6,0) },nickname,saveName,resetName});
            split.Panel2.Controls.Add(nameBar);
            var bottom=new Panel { Dock=DockStyle.Bottom,Height=220,Padding=new Padding(8,0,8,0) };
            bottom.Controls.Add(activity); bottom.Controls.Add(activityStatus);
            Controls.Add(split); Controls.Add(bottom); Controls.Add(metrics); Controls.Add(toolbar); Controls.Add(title); Controls.Add(update); Controls.Add(status);
            foreach(var pair in new[]{new[]{"Time","Local time"},new[]{"Kind","Event"},new[]{"Name","Nickname / device"},new[]{"Location","Port / location"},new[]{"Source","Source"}}) grid.Columns.Add(new DataGridViewTextBoxColumn { Name=pair[0],HeaderText=pair[1],FillWeight=pair[0]=="Name"?190:100,SortMode=DataGridViewColumnSortMode.Programmatic });
            activity.Columns.Add(new DataGridViewTextBoxColumn { HeaderText="Local time",Width=175,AutoSizeMode=DataGridViewAutoSizeColumnMode.None,SortMode=DataGridViewColumnSortMode.Programmatic });
            activity.Columns.Add(new DataGridViewTextBoxColumn { HeaderText="All USB buses — last 24 hours (independent of filters above)",SortMode=DataGridViewColumnSortMode.Programmatic });
            grid.EnableHeadersVisualStyles=false; grid.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(34,47,61); grid.ColumnHeadersDefaultCellStyle.ForeColor=ForeColor; grid.ColumnHeadersHeight=34; grid.RowTemplate.Height=28;
            grid.DefaultCellStyle.BackColor=Color.FromArgb(23,32,43); grid.DefaultCellStyle.ForeColor=ForeColor; grid.DefaultCellStyle.SelectionBackColor=Color.FromArgb(37,86,99); grid.GridColor=Color.FromArgb(39,51,64);
            activity.BackgroundColor=grid.BackgroundColor; activity.DefaultCellStyle=grid.DefaultCellStyle.Clone(); activity.ColumnHeadersDefaultCellStyle=grid.ColumnHeadersDefaultCellStyle.Clone(); activity.EnableHeadersVisualStyles=false; activity.GridColor=grid.GridColor; activity.RowTemplate.Height=28; activity.ColumnHeadersHeight=30;
            tree.BackColor=BackColor; tree.ForeColor=ForeColor; tree.ItemHeight=26; details.BackColor=Color.FromArgb(23,32,43); details.ForeColor=ForeColor;
            search.ForeColor=Color.Black; nickname.ForeColor=Color.Black; nickname.Enabled=false; tree.Nodes.Add(root);
            if(names.LoadError!=null) UpdateStatus(names.LoadError);
            saveName.Click+=delegate { SaveNickname(false); }; resetName.Click+=delegate { SaveNickname(true); };
            nickname.KeyDown+=delegate(object s,KeyEventArgs e) { if(e.KeyCode==Keys.Enter) { SaveNickname(false); e.SuppressKeyPress=true; } };
            search.TextChanged+=delegate { ApplyFilter(); }; kind.SelectedIndexChanged+=delegate { ApplyFilter(); }; descendants.CheckedChanged+=delegate { ApplyFilter(); };
            range.SelectedIndexChanged+=delegate { dirty=true; LoadHistory(); };
            tree.AfterSelect+=delegate { if(rebuilding) return; selected=tree.SelectedNode==null?"":tree.SelectedNode.Name; EditDevice(tree.SelectedNode==null?null:tree.SelectedNode.Tag as Device); ApplyFilter(); };
            grid.SelectionChanged+=delegate { if(rebuilding) return; var e=grid.SelectedEvent; if(e!=null) details.Text=e.Time.ToLocalTime().ToString("F")+"  •  "+e.Kind+"  •  "+e.Source+Environment.NewLine+e.Message+Environment.NewLine+Describe(e.Device); };
            activity.SelectionChanged+=delegate { var e=activity.SelectedEvent; if(e!=null) details.Text=e.Message+Environment.NewLine+Describe(e.Device); };
            activity.CellDoubleClick+=delegate { var e=activity.SelectedEvent; TreeNode node; if(e!=null&&nodes.TryGetValue(e.Device.Id,out node)) { tree.SelectedNode=node; node.EnsureVisible(); } };
            FormClosing+=delegate(object s,FormClosingEventArgs e) { if(!AllowClose&&e.CloseReason==CloseReason.UserClosing) { e.Cancel=true; Hide(); } };
            Resize+=delegate { if(WindowState==FormWindowState.Minimized) Hide(); };
            VisibleChanged+=delegate { if(Visible) { dirty=true; LoadHistory(); } };
            timer.Tick+=delegate { status.Text=recorder.Status; status.ForeColor=recorder.Status.StartsWith("Recording •")?Color.FromArgb(86,225,195):Color.Orange; if(Visible&&(dirty||++ticks%5==0)) LoadHistory(); }; timer.Start();
        }
        public void UpdateStatus(string text) { if(!IsDisposed) update.Text=text; }
        async void LoadHistory() {
            if(loading||IsDisposed) return; loading=true; dirty=false;
            int selection=range.SelectedIndex; DateTime since=selection==0?DateTime.UtcNow.AddHours(-1):selection==1?DateTime.UtcNow.AddDays(-1):selection==2?DateTime.UtcNow.AddDays(-7):DateTime.MinValue;
            try {
                DateTime activitySince=DateTime.UtcNow.AddDays(-1),readSince=since<activitySince?since:activitySince;
                int bad=0; var data=await Task.Run(()=>history.Read(readSince,DateTime.UtcNow,"","",true,out bad));
                if(IsDisposed) return; if(selection!=range.SelectedIndex) { dirty=true; return; }
                events=data.Where(e=>e.Time>=since).ToList(); allBuses=data.Where(e=>e.Time>=activitySince).ToList(); damaged=bad; RebuildTree(); ApplyFilter(); RefreshActivity();
            } catch(Exception e) { UpdateStatus("Could not read history: "+e.Message); }
            finally { loading=false; }
        }
        void RebuildTree() {
            var devices=new Dictionary<string,Device>(StringComparer.OrdinalIgnoreCase);
            foreach(var e in events) if(e.Device.Id.Length>0) { var d=e.Device; devices[d.Id]=new Device { Id=d.Id,Name=d.Name,Parent=d.Parent,Ancestors=d.Ancestors,Class=d.Class,Service=d.Service,Manufacturer=d.Manufacturer,Location=d.Location,Problem=d.Problem,Present=false }; }
            foreach(var d in recorder.Snapshot()) devices[d.Id]=d;
            bool initial=nodes.Count==0; TreeNode top=tree.TopNode;
            rebuilding=true;
            try {
                foreach(string id in nodes.Keys.Where(id=>!devices.ContainsKey(id)).ToArray()) { nodes[id].Remove(); nodes.Remove(id); }
                foreach(var d in devices.Values.OrderBy(d=>d.Name)) {
                    TreeNode node; if(!nodes.TryGetValue(d.Id,out node)) { node=new TreeNode { Name=d.Id }; nodes[d.Id]=node; }
                    string text=names.Label(d)+(d.Present?"":"  [offline]"),tip=Describe(d);
                    if(node.Text!=text) node.Text=text; if(node.ToolTipText!=tip) node.ToolTipText=tip;
                    Color color=d.Present?ForeColor:Color.FromArgb(139,155,173); if(node.ForeColor!=color) node.ForeColor=color;
                    node.Tag=d;
                }
                foreach(var d in devices.Values.OrderBy(d=>d.Name)) {
                    TreeNode parent; if(d.Parent==d.Id||!nodes.TryGetValue(d.Parent,out parent)||CreatesCycle(d,devices)) parent=root;
                    var node=nodes[d.Id]; if(node.Parent!=parent) { node.Remove(); parent.Nodes.Add(node); }
                }
                if(initial) { root.ExpandAll(); tree.TopNode=root; }
                else if(top!=null&&top.TreeView==tree&&tree.TopNode!=top) tree.TopNode=top;
                TreeNode picked; if(selected.Length>0&&nodes.TryGetValue(selected,out picked)&&tree.SelectedNode!=picked) tree.SelectedNode=picked;
                if(selected.Length>0&&!nodes.ContainsKey(selected)) { selected=""; EditDevice(null); }
            } finally { rebuilding=false; }
        }
        static bool CreatesCycle(Device d,Dictionary<string,Device> map) { var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase); while(d!=null) { if(!seen.Add(d.Id)) return true; Device p; d=map.TryGetValue(d.Parent,out p)?p:null; } return false; }
        static IEnumerable<TreeNode> AllNodes(TreeNodeCollection nodes) { foreach(TreeNode n in nodes) { yield return n; foreach(var child in AllNodes(n.Nodes)) yield return child; } }
        void ApplyFilter() {
            filtered=events.Where(e=>History.Matches(e,"",selected,descendants.Checked)&&(History.Matches(e,search.Text,"",true)||names.Get(e.Device).IndexOf(search.Text,StringComparison.OrdinalIgnoreCase)>=0)&&(kind.SelectedIndex==0||e.Kind==(string)kind.SelectedItem)).OrderByDescending(e=>e.Time).ToList();
            metrics.Text=filtered.Count+" events     /     "+filtered.Count(e=>e.Kind=="Disconnected")+" disconnects     /     "+filtered.Count(e=>e.Kind=="Flapping")+" flap alerts     /     "+filtered.Select(e=>e.Device.Id).Where(id=>id.Length>0).Distinct().Count()+" devices"+Environment.NewLine+(selected.Length==0?"All devices":selected)+(damaged>0?" • "+damaged+" unreadable history records":"")+(filtered.Count>5000?" • showing newest 5,000; export includes all matches":"");
            rebuilding=true;
            try { grid.SetRows(filtered.Take(5000).Select(e=>new EventRow { Key=EventTable.Key(e),Event=e,Cells=new[]{e.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff"),e.Kind,names.Label(e.Device),e.Device.Location,e.Source},Alert=e.Kind=="Flapping"||e.Kind=="Problem changed" })); }
            finally { rebuilding=false; }
        }
        string Describe(Device d) { return d==null?"":names.Label(d)+"  •  "+d.Kind+"  •  "+(d.Present?"present":"offline / historical")+"  •  Windows problem code "+d.Problem+Environment.NewLine+"Instance: "+d.Id+Environment.NewLine+"Parent: "+d.Parent+Environment.NewLine+"Location: "+d.Location+"  |  Manufacturer: "+d.Manufacturer+Environment.NewLine+"Chain: "+string.Join(" > ",d.Ancestors.Reverse()); }
        void ShowDevice(Device d) { details.Text=Describe(d); }
        void EditDevice(Device d) { nickname.Enabled=saveName.Enabled=resetName.Enabled=d!=null&&d.Id.Length>0; nickname.Text=d==null?"":names.Get(d); ShowDevice(d); }
        void SaveNickname(bool reset) {
            var d=tree.SelectedNode==null?null:tree.SelectedNode.Tag as Device; if(d==null) return;
            try { names.Set(d,reset?"":nickname.Text); RebuildTree(); ApplyFilter(); RefreshActivity(); EditDevice(d); UpdateStatus("Nickname saved locally. Windows device settings are unchanged."); }
            catch(Exception e) { UpdateStatus("Nickname could not be saved: "+e.Message); }
        }
        void RefreshActivity() {
            var recent=allBuses.Where(e=>e.Kind=="Disconnected"&&e.Time>=DateTime.UtcNow.AddMinutes(-5)).GroupBy(e=>e.Device.Id,StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>=3).ToList();
            activityStatus.Text=recent.Count==0?"ALL-BUS ACTIVITY  •  No repeated disconnects detected in the last 5 minutes":"POSSIBLE FLAPPING  •  "+string.Join("; ",recent.Take(3).Select(g=>names.Get(g.Last().Device)+" — "+g.Count()+" disconnects in 5 min"))+(recent.Count>3?"; +"+(recent.Count-3)+" more":"");
            activityStatus.ForeColor=recent.Count==0?ForeColor:Color.FromArgb(255,190,99);
            var lines=ActivityLog.Build(allBuses,names);
            if(lines.Count==0) activityStatus.Text+="  •  No activity logged in the last 24 hours";
            activity.SetRows(lines.Take(500).Select(line=>new EventRow { Key=EventTable.Key(line.Event),Event=line.Event,Cells=new[]{line.Event.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff"),line.Text},Alert=line.Alert }));
        }
        void Export() { using(var dialog=new SaveFileDialog { Filter="CSV files|*.csv",FileName="USBPal-events-"+DateTime.Now.ToString("yyyyMMdd-HHmm")+".csv" }) if(dialog.ShowDialog(this)==DialogResult.OK) { try { History.Export(dialog.FileName,filtered); UpdateStatus("Exported "+filtered.Count+" matching events."); } catch(Exception e) { UpdateStatus("Export failed: "+e.Message); } } }
        protected override void Dispose(bool disposing) { if(disposing) timer.Dispose(); base.Dispose(disposing); }
    }
}
