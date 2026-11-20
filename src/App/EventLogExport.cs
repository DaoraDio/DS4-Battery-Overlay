using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

sealed partial class EventLogPage
{
    readonly Button export=StudioTheme.Button("Export events");

    void AddExportButton(TableLayoutPanel header)
    {
        export.Dock=DockStyle.Fill;
        export.Margin=new Padding(0,4,8,4);
        export.AccessibleName="Export filtered events";
        export.AccessibleDescription="Save all events matching the current filters to a text file.";
        export.Click+=delegate { ExportEvents(); };
        header.Controls.Add(export,3,0);
    }

    ControllerEvent[] ExportSnapshot()
    {
        // Export all filtered rows, including rows outside the visible scroll area.
        return entries.Items.Cast<Row>().Select(row=>row.Event).ToArray();
    }

    static void WriteExport(string path,ControllerEvent[] snapshot)
    {
        File.WriteAllLines(path,snapshot.Select(item=>new Row(item).ToString()),new UTF8Encoding(true));
    }

    void ExportEvents()
    {
        var snapshot=ExportSnapshot();
        if(snapshot.Length==0)return;
        using(var dialog=new SaveFileDialog {
            Title="Export filtered events",Filter="Text files (*.txt)|*.txt|All files (*.*)|*.*",
            DefaultExt="txt",AddExtension=true,OverwritePrompt=true,RestoreDirectory=true,
            FileName="DS4-events-"+DateTime.Now.ToString("yyyy-MM-dd_HHmmss",CultureInfo.InvariantCulture)+".txt"
        })
        {
            if(dialog.ShowDialog(FindForm())!=DialogResult.OK)return;
            try { WriteExport(dialog.FileName,snapshot); }
            catch(Exception error)
            {
                MessageBox.Show(FindForm(),"Could not export events.\n\n"+error.Message,"Export events",
                    MessageBoxButtons.OK,MessageBoxIcon.Warning);
            }
        }
    }

    internal static int ExportSelfTest(string report)
    {
        string path=Path.Combine(Path.GetTempPath(),"DS4EventsExport-"+Guid.NewGuid().ToString("N")+".txt");
        try
        {
            var log=new ControllerEventLog(null);
            log.Add("DualShock 4 connected");
            string battery="Battery changed from 25% to 15% (Bluetooth) \u00b7 caf\u00e9 "+new string('x',400);
            log.Add(battery);
            log.Add("DualShock 4 disconnected");
            using(var page=new EventLogPage(log))
            {
                var all=page.ExportSnapshot();
                if(all.Length!=3||all[0].Message!="DualShock 4 disconnected"||!page.export.Enabled)
                    throw new Exception("Export did not preserve the displayed newest-first order.");
                foreach(var item in page.filterItems)item.Checked=false;
                if(page.ExportSnapshot().Length!=0||page.export.Enabled)
                    throw new Exception("Export remained available with no matching events.");
                page.filterItems[3].Checked=true;
                var filtered=page.ExportSnapshot();
                if(filtered.Length!=1||filtered[0].Message!=battery||!page.export.Enabled)
                    throw new Exception("Export included events excluded by the active filters.");
                WriteExport(path,filtered);
                string expected=filtered[0].Time.ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture)+"   "+battery;
                if(File.ReadAllLines(path).Length!=1||File.ReadAllLines(path)[0]!=expected)
                    throw new Exception("Export lost the timestamp, Unicode text or full event message.");
                page.filterItems[0].Checked=true;
                if(page.ExportSnapshot().Length!=2||log.Snapshot().Length!=3)
                    throw new Exception("Multiple filters or export changed the event history.");
                log.Clear();
                if(page.export.Enabled||page.ExportSnapshot().Length!=0)
                    throw new Exception("Empty history did not disable export.");
            }
            File.WriteAllText(report,"PASS export uses current filters, includes every matching row in displayed order, preserves local timestamps and full Unicode messages, supports multiple selected event types, leaves history unchanged and disables when empty.");
            return 0;
        }
        catch(Exception error){File.WriteAllText(report,error.ToString());return 1;}
        finally { if(File.Exists(path))File.Delete(path); }
    }
}
