using System;
using System.Globalization;
using System.IO;
using System.Text;

static class Ds4BatteryScale
{
    internal static readonly int[] Unplugged={5,12,25,37,50,62,75,87,100};
    internal static readonly int[] Wired={5,15,25,35,45,55,65,75,85,95,100};
    internal static readonly int[] DisplayLevels={5,12,15,25,35,37,45,50,55,62,65,75,85,87,95,100};
    // Retain the original eleven popup mask bits; new percentages use appended bits.
    internal static readonly int[] TriggerLevels={5,15,25,35,45,55,65,75,85,95,100,12,37,50,62,87};

    internal static string Decode(byte status,out bool charging)
    {
        int level=status&15;
        bool cable=(status&16)!=0;
        charging=cable&&level<=10;
        if(cable&&level==11)return "100% · Full";
        if(level>10)return "";
        int percent=cable?Wired[level]:Unplugged[Math.Min(level,Unplugged.Length-1)];
        return percent.ToString(CultureInfo.InvariantCulture)+"%";
    }

    internal static bool SelfTest()
    {
        bool charging;
        int[] expectedUnplugged={5,12,25,37,50,62,75,87,100,100,100};
        int[] expectedWired={5,15,25,35,45,55,65,75,85,95,100};
        foreach(int otherFlags in new[]{0,32,64,128,224})
            for(int level=0;level<16;level++) {
                string unplugged=Decode((byte)(otherFlags|level),out charging);
                if(charging||unplugged!=(level<=10?expectedUnplugged[level]+"%":""))return false;
                string wired=Decode((byte)(otherFlags|16|level),out charging);
                if(charging!=(level<=10)||wired!=(level<=10?expectedWired[level]+"%":level==11?"100% · Full":""))return false;
            }
        int[] original={5,15,25,35,45,55,65,75,85,95,100};
        for(int index=0;index<original.Length;index++)
            if(PopupSettings.TriggerBit(NoticeKind.BatteryLevel,original[index])!=(1<<(6+index)))return false;
        int seen=0,previous=-1;
        foreach(int level in DisplayLevels) {
            int bit=PopupSettings.TriggerBit(NoticeKind.BatteryLevel,level);
            if(level<=previous||bit==0||(seen&bit)!=0||(bit&PopupSettings.MaxTriggers)==0)return false;
            seen|=bit;previous=level;
            if(!new PopupSettings(2,2,triggers:bit).Allows(NoticeKind.BatteryLevel,level))return false;
        }
        return PopupSettings.TriggerBit(NoticeKind.BatteryLevel,0)==0;
    }

    internal static int WriteSelfTest(string path)
    {
        bool passed=SelfTest();
        var report=new StringBuilder(passed?"PASS ":"FAIL ");
        report.AppendLine("unplugged 0–8 scale with minimum 5%, original USB scale, full/charging states, invalid statuses, other flags and compatible popup masks.");
        bool charging;
        for(int level=0;level<=10;level++)report.AppendLine("Raw "+level+": unplugged "+Decode((byte)level,out charging)+"; USB "+Decode((byte)(16|level),out charging));
        report.AppendLine("USB full (0x1B): "+Decode(0x1b,out charging));
        File.WriteAllText(path,report.ToString());return passed?0:1;
    }
}
