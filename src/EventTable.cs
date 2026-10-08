using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace USBPal {
    internal sealed class EventRow {
        public string Key; public string[] Cells; public UsbEvent Event; public bool Alert;
    }
    // Virtual rows avoid clearing/recreating native rows on each history refresh.
    internal sealed class EventTable : DataGridView {
        List<EventRow> rows=new List<EventRow>();
        int sortColumn=-1; bool ascending;
        public int RefreshCount { get; private set; }
        public EventTable() {
            DoubleBuffered=true; VirtualMode=true;
            CellValueNeeded+=delegate(object s,DataGridViewCellValueEventArgs e) { if(e.RowIndex<rows.Count) e.Value=rows[e.RowIndex].Cells[e.ColumnIndex]; };
            CellFormatting+=delegate(object s,DataGridViewCellFormattingEventArgs e) { if(e.RowIndex>=0&&e.RowIndex<rows.Count&&rows[e.RowIndex].Alert) e.CellStyle.ForeColor=System.Drawing.Color.FromArgb(255,190,99); };
            CellToolTipTextNeeded+=delegate(object s,DataGridViewCellToolTipTextNeededEventArgs e) { if(e.RowIndex>=0&&e.RowIndex<rows.Count) e.ToolTipText=string.Join(" • ",rows[e.RowIndex].Cells)+Environment.NewLine+rows[e.RowIndex].Event.Device.Id; };
            ColumnHeaderMouseClick+=delegate(object s,DataGridViewCellMouseEventArgs e) { ascending=sortColumn==e.ColumnIndex?!ascending:true; sortColumn=e.ColumnIndex; SetRows(rows,true); };
        }
        public UsbEvent SelectedEvent { get { return SelectedRows.Count>0&&SelectedRows[0].Index<rows.Count?rows[SelectedRows[0].Index].Event:null; } }
        public static string Key(UsbEvent e) { return string.Join("\u001f",new[]{e.Utc,e.Kind,e.Source,e.Device.Id,e.Message}); }
        public void SetRows(IEnumerable<EventRow> input,bool force=false) {
            var next=input.ToList();
            if(sortColumn>=0) next=ascending?next.OrderBy(r=>r.Cells[sortColumn],StringComparer.CurrentCultureIgnoreCase).ToList():next.OrderByDescending(r=>r.Cells[sortColumn],StringComparer.CurrentCultureIgnoreCase).ToList();
            if(!force&&rows.Count==next.Count&&rows.Select((r,i)=>r.Key==next[i].Key&&r.Alert==next[i].Alert&&r.Cells.SequenceEqual(next[i].Cells)).All(s=>s)) return;
            string selected=SelectedEvent==null?null:Key(SelectedEvent);
            int topIndex=FirstDisplayedScrollingRowIndex;
            string top=topIndex>=0&&topIndex<rows.Count?rows[topIndex].Key:null;
            rows=next; RowCount=rows.Count; RefreshCount++;
            ClearSelection(); int index=selected==null?-1:rows.FindIndex(r=>r.Key==selected);
            if(index>=0) Rows[index].Selected=true;
            if(topIndex>0&&top!=null) { int found=rows.FindIndex(r=>r.Key==top); if(found>=0) FirstDisplayedScrollingRowIndex=found; }
            foreach(DataGridViewColumn c in Columns) c.HeaderCell.SortGlyphDirection=c.Index==sortColumn?(ascending?SortOrder.Ascending:SortOrder.Descending):SortOrder.None;
            Invalidate();
        }
    }
}
