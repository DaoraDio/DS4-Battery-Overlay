using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

static class Ds4Power
{
    [StructLayout(LayoutKind.Sequential)]
    struct RadioParameters { public uint Size; }
    [StructLayout(LayoutKind.Sequential)]
    struct HidAttributes { public uint Size; public ushort Vendor, Product, Version; }

    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("hid.dll", SetLastError=true)]
    [return:MarshalAs(UnmanagedType.U1)] static extern bool HidD_GetAttributes(SafeFileHandle device,ref HidAttributes attributes);
    [DllImport("hid.dll", SetLastError=true)]
    [return:MarshalAs(UnmanagedType.U1)] static extern bool HidD_GetSerialNumberString(SafeFileHandle device,[Out] byte[] buffer,uint length);
    [DllImport("cfgmgr32.dll", CharSet=CharSet.Unicode, EntryPoint="CM_Locate_DevNodeW")]
    static extern uint LocateNode(out uint node,string instance,uint flags);
    [DllImport("cfgmgr32.dll", CharSet=CharSet.Unicode, EntryPoint="CM_Get_Device_IDW")]
    static extern uint GetNodeId(uint node,StringBuilder id,uint length,uint flags);
    [DllImport("cfgmgr32.dll", EntryPoint="CM_Get_Parent")]
    static extern uint GetParent(out uint parent,uint node,uint flags);
    [DllImport("bthprops.cpl", SetLastError=true)]
    static extern IntPtr BluetoothFindFirstRadio(ref RadioParameters parameters,out IntPtr radio);
    [DllImport("bthprops.cpl", SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)] static extern bool BluetoothFindNextRadio(IntPtr search,out IntPtr radio);
    [DllImport("bthprops.cpl")]
    [return:MarshalAs(UnmanagedType.Bool)] static extern bool BluetoothFindRadioClose(IntPtr search);
    [DllImport("kernel32.dll", SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)] static extern bool DeviceIoControl(SafeFileHandle radio,uint code,ref ulong address,uint inputLength,IntPtr output,uint outputLength,out uint returned,IntPtr overlapped);

    internal static bool CanTurnOff(ControllerConnection controller)
    {
        return controller!=null && controller.Connected && controller.LightbarBluetooth==true && !controller.CableConnected;
    }

    internal static bool TryAddress(string text,out ulong address)
    {
        address=0;
        if(text==null)return false;
        text=text.Trim();
        if(text.Length==17)
        {
            char separator=text[2];
            if(separator!=':'&&separator!='-')return false;
            for(int index=2;index<17;index+=3)if(text[index]!=separator)return false;
            text=text.Replace(separator.ToString(),"");
        }
        return text.Length==12 && ulong.TryParse(text,NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out address)
            && address!=0 && address!=0xffffffffffffUL;
    }

    internal static bool TryInstanceId(string path,out string id)
    {
        id=null;
        if(path==null||!path.StartsWith(@"\\?\",StringComparison.Ordinal))return false;
        string[] parts=path.Substring(4).Split('#');
        Guid deviceClass;
        if(parts.Length!=4||!parts[0].Equals("HID",StringComparison.OrdinalIgnoreCase)||
            parts[1].Length==0||parts[2].Length==0||!Guid.TryParse(parts[3],out deviceClass))return false;
        id=parts[0]+@"\"+parts[1]+@"\"+parts[2];
        return true;
    }

    static bool TryParentAddress(string path,out ulong address)
    {
        address=0;string instance;uint node;
        if(!TryInstanceId(path,out instance)||LocateNode(out node,instance,0)!=0)return false;
        for(int depth=0;depth<12;depth++)
        {
            var id=new StringBuilder(512);
            if(GetNodeId(node,id,(uint)id.Capacity,0)!=0)return false;
            const string prefix=@"BTHENUM\DEV_";
            string value=id.ToString();
            if(value.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))
            {
                int end=value.IndexOf('\\',prefix.Length);
                return end>=0 && TryAddress(value.Substring(prefix.Length,end-prefix.Length),out address);
            }
            uint parent;
            if(GetParent(out parent,node,0)!=0)break;
            node=parent;
        }
        return false;
    }

    static ulong ResolveAddress(string path)
    {
        // Open only the selected HID device; never choose a controller by its display name.
        using(var device=CreateFile(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero))
        {
            if(device.IsInvalid)throw new IOException("The controller is no longer available or another app is blocking access.");
            var attributes=new HidAttributes {Size=(uint)Marshal.SizeOf(typeof(HidAttributes))};
            if(!HidD_GetAttributes(device,ref attributes)||attributes.Vendor!=0x054c||
                (attributes.Product!=0x05c4&&attributes.Product!=0x09cc))
                throw new IOException("The selected device is not a supported DualShock 4.");
            ulong address;
            if(TryParentAddress(path,out address))return address;
            var serial=new byte[512];
            if(HidD_GetSerialNumberString(device,serial,(uint)serial.Length) &&
                TryAddress(Encoding.Unicode.GetString(serial).TrimEnd('\0'),out address))return address;
            throw new IOException("Windows could not identify this controller's Bluetooth address.");
        }
    }

    internal static void TurnOff(string path,bool bluetooth)
    {
        TurnOff(path,bluetooth,ResolveAddress,DisconnectAddress);
    }

    static void TurnOff(string path,bool bluetooth,Func<string,ulong> resolve,Action<ulong> disconnect)
    {
        if(!bluetooth)throw new NotSupportedException("Turning off the controller is available over Bluetooth only.");
        if(string.IsNullOrEmpty(path))throw new IOException("No controller is connected.");
        ulong address=resolve(path);
        if(address==0||address>=0xffffffffffffUL)throw new IOException("The controller's Bluetooth address is invalid.");
        disconnect(address);
    }

    static void DisconnectAddress(ulong address)
    {
        var parameters=new RadioParameters {Size=(uint)Marshal.SizeOf(typeof(RadioParameters))};
        IntPtr rawRadio;
        IntPtr search=BluetoothFindFirstRadio(ref parameters,out rawRadio);
        if(search==IntPtr.Zero)throw new IOException("No available Bluetooth adapter was found.");
        int lastError=0;
        try
        {
            do
            {
                using(var radio=new SafeFileHandle(rawRadio,true))
                {
                    uint returned;
                    if(DeviceIoControl(radio,0x41000c,ref address,8,IntPtr.Zero,0,out returned,IntPtr.Zero))return;
                    lastError=Marshal.GetLastWin32Error();
                }
            } while(BluetoothFindNextRadio(search,out rawRadio));
        }
        finally { BluetoothFindRadioClose(search); }
        throw new IOException("Windows could not disconnect the controller. "+new Win32Exception(lastError).Message);
    }

    internal static int SelfTest(string report)
    {
        try
        {
            ulong address;
            foreach(string serial in new[]{"50:14:DE:AD:BE:EF","5014deadbeef","50-14-DE-AD-BE-EF"})
                if(!TryAddress(serial,out address)||address!=0x5014deadbeefUL)throw new Exception("Bluetooth address ordering failed.");
            foreach(string serial in new[]{null,"","0","000000000000","FFFFFFFFFFFF","5014deadbeef00","50:14-DE:AD:BE:EF","Wireless Controller"})
                if(TryAddress(serial,out address))throw new Exception("Invalid address was accepted.");
            string id;
            const string path=@"\\?\HID#VID&0002054C_PID&09CC#8&example&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";
            if(!TryInstanceId(path,out id)||id!=@"HID\VID&0002054C_PID&09CC\8&example&0&0000"||TryInstanceId("not a HID path",out id))
                throw new Exception("Selected-device instance lookup failed.");
            var target=new ControllerConnection(path);
            if(CanTurnOff(null)||CanTurnOff(target))throw new Exception("Offline action enabled.");
            target.ObserveInput(0);target.ObserveInput(500);
            if(!CanTurnOff(target))throw new Exception("Bluetooth action disabled.");
            target.Bluetooth=false;
            if(CanTurnOff(target))throw new Exception("USB action enabled.");
            target.Bluetooth=true;
            var usb=new ControllerConnection(@"\\?\HID#VID_054C&PID_09CC#test");
            usb.ObserveInput(0);usb.ObserveInput(500);usb.Bluetooth=false;
            target.RecordBattery("100% · Full",true,false,1000);
            usb.RecordBattery("100% · Full",true,false,1000);
            for(int index=0;index<100;index++)
                if(CanTurnOff(index%2==0?target:usb))
                    throw new Exception("Power action flickered between Bluetooth and USB endpoints while plugged in.");
            target.RecordBattery("85%",true,true,2000);
            if(CanTurnOff(target))throw new Exception("Charging Bluetooth controller could be turned off.");
            target.RecordBattery("100%",false,false,3000);
            if(!CanTurnOff(target))throw new Exception("Unplugging USB did not restore Bluetooth power control.");
            target.Disconnect();
            if(target.CableConnected||CanTurnOff(target))throw new Exception("Disconnect retained cable state or power control.");
            bool resolved=false,disconnected=false;
            try { TurnOff(path,false,value=>{resolved=true;return 1;},value=>disconnected=true);throw new Exception("USB shutdown accepted."); }
            catch(NotSupportedException) { }
            if(resolved||disconnected)throw new Exception("USB shutdown reached native I/O.");
            TurnOff(path,true,value=>{if(value!=path)throw new Exception("Wrong controller selected.");return 0x5014deadbeefUL;},
                value=>{if(value!=0x5014deadbeefUL)throw new Exception("Wrong Bluetooth target.");disconnected=true;});
            if(!disconnected)throw new Exception("Shutdown was not dispatched.");
            File.WriteAllText(report,"PASS Bluetooth-only availability, stable disabled state across simultaneous USB/BT endpoints, cable/full/charging guards, unplug recovery, disconnect cleanup, strict address parsing and byte order, selected HID instance lookup and exact controller targeting. No physical controller was disconnected during this test.");
            return 0;
        }
        catch(Exception error){File.WriteAllText(report,error.ToString());return 1;}
    }
}
