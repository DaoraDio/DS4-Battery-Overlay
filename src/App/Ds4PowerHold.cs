using System;
using System.IO;

sealed class Ds4PowerHold
{
    internal const long DurationMs=8000;
    const long MaxReportGapMs=2000;
    long started=-1,lastReport=-1;
    bool armed,fired;

    internal void Reset()
    {
        started=lastReport=-1;armed=fired=false;
    }

    internal bool Observe(bool down,long now,bool enabled)
    {
        if(!enabled){Reset();return false;}
        if(lastReport>=0&&(now<lastReport||now-lastReport>MaxReportGapMs))Reset();
        lastReport=now;
        if(!down){started=-1;armed=true;fired=false;return false;}
        // Require a released PS button after startup, reconnection or lost reports.
        if(!armed||fired)return false;
        if(started<0)started=now;
        if(now-started<DurationMs)return false;
        fired=true;return true;
    }

    internal static int ButtonOffset(int reportId,int length,byte header)
    {
        if(reportId==1&&length>=10)return 7;
        if(reportId==0x11&&length>=78&&(header&0x80)!=0)return 9;
        return -1;
    }

    internal static int SelfTest(string report)
    {
        try
        {
            var hold=new Ds4PowerHold();
            for(int time=0;time<=10000;time+=100)
                if(hold.Observe(true,time,true))throw new Exception("PS held during connection triggered shutdown.");
            hold.Reset();hold.Observe(false,0,true);
            for(int time=100;time<8100;time+=100)
                if(hold.Observe(true,time,true))throw new Exception("Shutdown occurred before an eight-second hold.");
            if(!hold.Observe(true,8100,true)||hold.Observe(true,8200,true))
                throw new Exception("Eight-second hold did not trigger exactly once.");
            hold.Observe(false,8300,true);
            for(int time=8400;time<16300;time+=100)
                if(hold.Observe(true,time,true))throw new Exception("A new press reused the previous hold.");
            hold.Observe(false,16300,true);
            for(int time=16400;time<24400;time+=100)
                if(hold.Observe(true,time,true))throw new Exception("Releasing early did not cancel the hold.");
            if(!hold.Observe(true,24400,true))throw new Exception("Rearmed hold failed.");
            hold.Reset();hold.Observe(false,0,true);hold.Observe(true,100,true);hold.Observe(true,1000,true);
            for(int time=5000;time<=15000;time+=100)
                if(hold.Observe(true,time,true))throw new Exception("A gap in input reports completed a hold.");
            hold.Reset();hold.Observe(false,0,false);
            for(int time=100;time<=10000;time+=100)
                if(hold.Observe(true,time,false))throw new Exception("USB or offline shutdown was allowed.");
            hold.Reset();
            for(int time=0;time<=10000;time+=100)
                if(hold.Observe(true,time,true))throw new Exception("Reconnection retained an armed hold.");
            if(ButtonOffset(1,10,0)!=7||ButtonOffset(0x11,78,0x80)!=9||
                ButtonOffset(0x11,78,0)==9||ButtonOffset(0x11,9,0x80)>=0||ButtonOffset(0x99,78,0x80)>=0)
                throw new Exception("PS button report parsing failed.");
            File.WriteAllText(report,"PASS eight-second continuous PS hold, no early firing, single action per press, early release cancellation, reconnect/startup release guard, missing-report cancellation, Bluetooth-only behavior and basic/full report offsets. No controller was disconnected.");
            return 0;
        }
        catch(Exception error){File.WriteAllText(report,error.ToString());return 1;}
    }
}
