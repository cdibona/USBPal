using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace USBPal {
    internal sealed class Dashboard : Form {
        readonly Recorder recorder; readonly History history;
        readonly TreeView tree=new TreeView { Dock=DockStyle.Fill,HideSelection=false,ShowNodeToolTips=true,BorderStyle=BorderStyle.None };
        readonly TextBox search=new TextBox { Width=250,AccessibleName="Search event history" };
        readonly ComboBox range=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList,Width=105 };
        readonly ComboBox kind=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList,Width=125 };
        readonly CheckBox descendants=new CheckBox { Text="Include descendants",Checked=true,AutoSize=true };
        readonly Label metrics=new Label { Dock=DockStyle.Top,Height=62,Padding=new Padding(16,12,8,0),Font=new Font("Segoe UI",12),ForeColor=Color.FromArgb(86,225,195) };
        readonly Label status=new Label { Dock=DockStyle.Bottom,Height=30,Padding=new Padding(12,5,0,0) };
        readonly Label update=new Label { Dock=DockStyle.Bottom,Height=30,Padding=new Padding(12,5,0,0),Text="Updates are checked on GitHub. Preferences are in the tray menu." };
        readonly TextBox details=new TextBox { Dock=DockStyle.Bottom,Height=125,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BorderStyle=BorderStyle.None };
        readonly DataGridView grid=new DataGridView { Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,BackgroundColor=Color.FromArgb(23,32,43),BorderStyle=BorderStyle.None,AutoGenerateColumns=false };
        readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer { Interval=1000 };
        List<UsbEvent> events=new List<UsbEvent>(),filtered=new List<UsbEvent>();
        string selected=""; bool loading,rebuilding,dirty=true; int ticks,damaged;
        public bool AllowClose;
        public event Action UpdateRequested;
        public Dashboard(Recorder recorder,History history) {
            this.recorder=recorder; this.history=history;
            Text="USBPal • USB event explorer"; Size=new Size(1280,820); MinimumSize=new Size(950,600); StartPosition=FormStartPosition.CenterScreen; AutoScaleMode=AutoScaleMode.Dpi;
            Font=new Font("Segoe UI",9); BackColor=Color.FromArgb(16,23,32); ForeColor=Color.FromArgb(222,233,242); Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            var title=new Label { Text="UP   USBPal  /  USB event explorer     v"+ReleaseUpdater.Current.ToString(3),Dock=DockStyle.Top,Height=64,Padding=new Padding(16,18,0,0),Font=new Font("Segoe UI",18,FontStyle.Bold) };
            var toolbar=new FlowLayoutPanel { Dock=DockStyle.Top,Height=76,Padding=new Padding(12,8,4,4),AutoScroll=true,WrapContents=true };
            range.Items.AddRange(new object[]{"Last hour","Last 24 hours","Last 7 days","All history"}); range.SelectedIndex=1;
            kind.Items.AddRange(new object[]{"All events","Connected","Disconnected","Flapping","Problem changed","Enumerated","Observed"}); kind.SelectedIndex=0;
            toolbar.Controls.AddRange(new Control[]{new Label { Text="Search",AutoSize=true,Margin=new Padding(0,5,6,0) },search,range,kind,descendants});
            var clear=new Button { Text="All devices",AutoSize=true }; clear.Click+=delegate { selected=""; tree.SelectedNode=null; ApplyFilter(); }; toolbar.Controls.Add(clear);
            var export=new Button { Text="Export CSV",AutoSize=true }; export.Click+=delegate { Export(); }; toolbar.Controls.Add(export);
            var updates=new Button { Text="Check updates",AutoSize=true }; updates.Click+=delegate { if(UpdateRequested!=null) UpdateRequested(); }; toolbar.Controls.Add(updates);
            var split=new SplitContainer { Dock=DockStyle.Fill,Size=new Size(1200,500),SplitterDistance=420,Panel1MinSize=220,Panel2MinSize=400 };
            split.Panel1.Controls.Add(tree); split.Panel1.Controls.Add(new Label { Text="USB TOPOLOGY  •  select a device or hub",Dock=DockStyle.Top,Height=30,Padding=new Padding(8) });
            split.Panel2.Controls.Add(grid); split.Panel2.Controls.Add(details);
            Controls.Add(split); Controls.Add(metrics); Controls.Add(toolbar); Controls.Add(title); Controls.Add(update); Controls.Add(status);
            foreach(var pair in new[]{new[]{"Time","Local time"},new[]{"Kind","Event"},new[]{"Name","Device"},new[]{"Location","Port / location"},new[]{"Source","Source"}}) grid.Columns.Add(new DataGridViewTextBoxColumn { Name=pair[0],HeaderText=pair[1],FillWeight=pair[0]=="Name"?190:100,SortMode=DataGridViewColumnSortMode.Automatic });
            grid.EnableHeadersVisualStyles=false; grid.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(34,47,61); grid.ColumnHeadersDefaultCellStyle.ForeColor=ForeColor; grid.ColumnHeadersHeight=34; grid.RowTemplate.Height=28;
            grid.DefaultCellStyle.BackColor=Color.FromArgb(23,32,43); grid.DefaultCellStyle.ForeColor=ForeColor; grid.DefaultCellStyle.SelectionBackColor=Color.FromArgb(37,86,99); grid.GridColor=Color.FromArgb(39,51,64);
            tree.BackColor=BackColor; tree.ForeColor=ForeColor; tree.ItemHeight=26; details.BackColor=Color.FromArgb(23,32,43); details.ForeColor=ForeColor;
            search.ForeColor=Color.Black; range.ForeColor=Color.Black; kind.ForeColor=Color.Black;
            foreach(var combo in new[]{range,kind}) {
                combo.DrawMode=DrawMode.OwnerDrawFixed;
                combo.DrawItem+=delegate(object sender,DrawItemEventArgs e) { var c=(ComboBox)sender; e.DrawBackground(); if(e.Index>=0) TextRenderer.DrawText(e.Graphics,c.Items[e.Index].ToString(),c.Font,e.Bounds,Color.Black,TextFormatFlags.Left|TextFormatFlags.VerticalCenter); e.DrawFocusRectangle(); };
            }
            search.TextChanged+=delegate { ApplyFilter(); }; kind.SelectedIndexChanged+=delegate { ApplyFilter(); }; descendants.CheckedChanged+=delegate { ApplyFilter(); };
            range.SelectedIndexChanged+=delegate { dirty=true; LoadHistory(); };
            tree.AfterSelect+=delegate { if(rebuilding) return; selected=tree.SelectedNode==null?"":tree.SelectedNode.Name; ShowDevice(tree.SelectedNode==null?null:tree.SelectedNode.Tag as Device); ApplyFilter(); };
            grid.SelectionChanged+=delegate { if(grid.SelectedRows.Count>0) { var e=grid.SelectedRows[0].Tag as UsbEvent; if(e!=null) details.Text=e.Time.ToLocalTime().ToString("F")+"  •  "+e.Kind+"  •  "+e.Source+Environment.NewLine+e.Message+Environment.NewLine+Describe(e.Device); } };
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
                int bad=0; var data=await Task.Run(()=>history.Read(since,DateTime.UtcNow,"","",true,out bad));
                if(IsDisposed) return; if(selection!=range.SelectedIndex) { dirty=true; return; }
                events=data; damaged=bad; RebuildTree(); ApplyFilter();
            } catch(Exception e) { UpdateStatus("Could not read history: "+e.Message); }
            finally { loading=false; }
        }
        void RebuildTree() {
            var devices=new Dictionary<string,Device>(StringComparer.OrdinalIgnoreCase);
            foreach(var e in events) if(e.Device.Id.Length>0) { var d=e.Device; devices[d.Id]=new Device { Id=d.Id,Name=d.Name,Parent=d.Parent,Ancestors=d.Ancestors,Class=d.Class,Service=d.Service,Manufacturer=d.Manufacturer,Location=d.Location,Problem=d.Problem,Present=false }; }
            foreach(var d in recorder.Snapshot()) devices[d.Id]=d;
            string top=tree.TopNode==null?"":tree.TopNode.Name;
            var expanded=new HashSet<string>(); foreach(TreeNode n in AllNodes(tree.Nodes)) if(n.IsExpanded) expanded.Add(n.Name);
            rebuilding=true; tree.BeginUpdate(); tree.Nodes.Clear();
            var root=new TreeNode(Environment.MachineName+" / motherboard") { Name="" }; tree.Nodes.Add(root);
            var nodes=devices.Values.ToDictionary(d=>d.Id,d=>new TreeNode(d.Name+(d.Present?"":"  [offline]")) { Name=d.Id,Tag=d,ToolTipText=Describe(d),ForeColor=d.Present?ForeColor:Color.FromArgb(139,155,173) },StringComparer.OrdinalIgnoreCase);
            foreach(var d in devices.Values.OrderBy(d=>d.Name)) { TreeNode parent; if(d.Parent!=d.Id&&nodes.TryGetValue(d.Parent,out parent)&&!CreatesCycle(d,devices)) parent.Nodes.Add(nodes[d.Id]); else root.Nodes.Add(nodes[d.Id]); }
            root.Expand(); foreach(var node in nodes.Values) { if(expanded.Contains(node.Name)||expanded.Count==0) node.Expand(); if(node.Name==selected) tree.SelectedNode=node; }
            TreeNode topNode; tree.TopNode=nodes.TryGetValue(top,out topNode)?topNode:root;
            tree.EndUpdate(); rebuilding=false;
        }
        static bool CreatesCycle(Device d,Dictionary<string,Device> map) { var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase); while(d!=null) { if(!seen.Add(d.Id)) return true; Device p; d=map.TryGetValue(d.Parent,out p)?p:null; } return false; }
        static IEnumerable<TreeNode> AllNodes(TreeNodeCollection nodes) { foreach(TreeNode n in nodes) { yield return n; foreach(var child in AllNodes(n.Nodes)) yield return child; } }
        void ApplyFilter() {
            filtered=events.Where(e=>History.Matches(e,search.Text,selected,descendants.Checked)&&(kind.SelectedIndex==0||e.Kind==(string)kind.SelectedItem)).OrderByDescending(e=>e.Time).ToList();
            metrics.Text=filtered.Count+" events     /     "+filtered.Count(e=>e.Kind=="Disconnected")+" disconnects     /     "+filtered.Count(e=>e.Kind=="Flapping")+" flap alerts     /     "+filtered.Select(e=>e.Device.Id).Where(id=>id.Length>0).Distinct().Count()+" devices"+Environment.NewLine+(selected.Length==0?"All devices":selected)+(damaged>0?" • "+damaged+" unreadable history records":"")+(filtered.Count>5000?" • showing newest 5,000; export includes all matches":"");
            grid.SuspendLayout(); grid.Rows.Clear();
            foreach(var e in filtered.Take(5000)) { int i=grid.Rows.Add(e.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff"),e.Kind,e.Device.Name,e.Device.Location,e.Source); grid.Rows[i].Tag=e; if(e.Kind=="Flapping"||e.Kind=="Problem changed") grid.Rows[i].DefaultCellStyle.ForeColor=Color.FromArgb(255,190,99); }
            grid.ResumeLayout();
            if(selected.Length>0&&tree.SelectedNode!=null) ShowDevice(tree.SelectedNode.Tag as Device);
            else if(grid.Rows.Count>0) { var e=grid.Rows[0].Tag as UsbEvent; details.Text=e.Kind+" • "+e.Message+Environment.NewLine+Describe(e.Device); }
        }
        static string Describe(Device d) { return d==null?"":d.Name+"  •  "+d.Kind+"  •  "+(d.Present?"present":"offline / historical")+"  •  Windows problem code "+d.Problem+Environment.NewLine+"Instance: "+d.Id+Environment.NewLine+"Parent: "+d.Parent+Environment.NewLine+"Location: "+d.Location+"  |  Manufacturer: "+d.Manufacturer+Environment.NewLine+"Chain: "+string.Join(" > ",d.Ancestors.Reverse()); }
        void ShowDevice(Device d) { details.Text=Describe(d); }
        void Export() { using(var dialog=new SaveFileDialog { Filter="CSV files|*.csv",FileName="USBPal-events-"+DateTime.Now.ToString("yyyyMMdd-HHmm")+".csv" }) if(dialog.ShowDialog(this)==DialogResult.OK) { try { History.Export(dialog.FileName,filtered); UpdateStatus("Exported "+filtered.Count+" matching events."); } catch(Exception e) { UpdateStatus("Export failed: "+e.Message); } } }
        protected override void Dispose(bool disposing) { if(disposing) timer.Dispose(); base.Dispose(disposing); }
    }
}
