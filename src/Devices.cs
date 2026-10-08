using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace USBPal {
    public sealed class Device {
        public string Id="", Parent="", Name="", Class="", Manufacturer="", Location="", Service="";
        public string[] Ancestors=new string[0];
        public uint Problem;
        public bool Present;
        public string Kind { get { return Service.IndexOf("hub",StringComparison.OrdinalIgnoreCase)>=0 ? "Hub" : Class=="USB" && Id.StartsWith("PCI",StringComparison.OrdinalIgnoreCase) ? "Controller" : "Device"; } }
    }
    internal static class Devices {
        [StructLayout(LayoutKind.Sequential)] struct Info { public uint Size; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }
        [DllImport("setupapi.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr SetupDiGetClassDevsW(IntPtr guid,string enumerator,IntPtr parent,uint flags);
        [DllImport("setupapi.dll",SetLastError=true)] static extern bool SetupDiEnumDeviceInfo(IntPtr set,uint index,ref Info info);
        [DllImport("setupapi.dll",CharSet=CharSet.Unicode)] static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr set,ref Info info,uint property,out uint type,byte[] buffer,uint size,out uint required);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("cfgmgr32.dll",CharSet=CharSet.Unicode)] static extern uint CM_Get_Device_IDW(uint node,StringBuilder buffer,int length,uint flags);
        [DllImport("cfgmgr32.dll")] static extern uint CM_Get_Parent(out uint parent,uint node,uint flags);
        [DllImport("cfgmgr32.dll")] static extern uint CM_Get_DevNode_Status(out uint status,out uint problem,uint node,uint flags);
        static string Id(uint node) { var b=new StringBuilder(512); return CM_Get_Device_IDW(node,b,b.Capacity,0)==0?b.ToString():""; }
        static string Property(IntPtr set,ref Info info,uint property) { uint type,needed; var b=new byte[8192]; return SetupDiGetDeviceRegistryPropertyW(set,ref info,property,out type,b,(uint)b.Length,out needed)?Encoding.Unicode.GetString(b,0,(int)Math.Min(needed,b.Length)).TrimEnd('\0').Replace('\0',';'):""; }
        internal static Dictionary<string,Device> Scan() {
            var all=new Dictionary<string,Device>(StringComparer.OrdinalIgnoreCase);
            IntPtr set=SetupDiGetClassDevsW(IntPtr.Zero,null,IntPtr.Zero,6);
            if(set==new IntPtr(-1)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try {
                for(uint i=0;;i++) {
                    var info=new Info { Size=(uint)Marshal.SizeOf(typeof(Info)) };
                    if(!SetupDiEnumDeviceInfo(set,i,ref info)) { int error=Marshal.GetLastWin32Error(); if(error!=259) throw new System.ComponentModel.Win32Exception(error); break; }
                    string id=Id(info.DevInst); if(id.Length==0) continue;
                    uint parent,status,problem; CM_Get_DevNode_Status(out status,out problem,info.DevInst,0);
                    var d=new Device { Id=id,Parent=CM_Get_Parent(out parent,info.DevInst,0)==0?Id(parent):"",Name=Property(set,ref info,12),Class=Property(set,ref info,7),Manufacturer=Property(set,ref info,11),Location=Property(set,ref info,13),Service=Property(set,ref info,4),Problem=problem,Present=true };
                    if(d.Name.Length==0) d.Name=Property(set,ref info,0); if(d.Name.Length==0) d.Name=id;
                    all[id]=d;
                }
            } finally { SetupDiDestroyDeviceInfoList(set); }
            foreach(var d in all.Values) {
                var chain=new List<string>(); var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase); string p=d.Parent;
                while(p.Length>0 && seen.Add(p) && chain.Count<64) { chain.Add(p); Device ancestor; if(!all.TryGetValue(p,out ancestor)) break; p=ancestor.Parent; }
                d.Ancestors=chain.ToArray();
            }
            var usb=new HashSet<string>(all.Values.Where(d=>d.Id.StartsWith("USB\\",StringComparison.OrdinalIgnoreCase)||d.Class=="USB").Select(d=>d.Id),StringComparer.OrdinalIgnoreCase);
            var keep=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var d in all.Values) if(usb.Contains(d.Id)||d.Ancestors.Any(a=>usb.Contains(a))) { keep.Add(d.Id); foreach(string a in d.Ancestors) keep.Add(a); }
            return all.Where(k=>keep.Contains(k.Key)).ToDictionary(k=>k.Key,k=>k.Value,StringComparer.OrdinalIgnoreCase);
        }
    }
    internal sealed class DeviceNotifications : IDisposable {
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Filter {
            public uint Size,Flags,Type,Reserved;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=200)] public string Instance;
        }
        delegate uint Callback(IntPtr notification,IntPtr context,uint action,IntPtr data,uint size);
        [DllImport("cfgmgr32.dll")] static extern uint CM_Register_Notification(ref Filter filter,IntPtr context,Callback callback,out IntPtr handle);
        [DllImport("cfgmgr32.dll")] static extern uint CM_Unregister_Notification(IntPtr handle);
        readonly Callback callback; IntPtr handle;
        public DeviceNotifications(Action<uint,string,DateTime> receive) {
            callback=delegate(IntPtr n,IntPtr c,uint action,IntPtr data,uint size) {
                try { if(size>10 && Marshal.ReadInt32(data)==2) receive(action,Marshal.PtrToStringUni(IntPtr.Add(data,8),(int)(size-8)/2).TrimEnd('\0'),DateTime.UtcNow); } catch { /* Never propagate an exception into Configuration Manager. */ }
                return 0;
            };
            var filter=new Filter { Size=(uint)Marshal.SizeOf(typeof(Filter)),Flags=2,Type=2,Instance="" };
            uint result=CM_Register_Notification(ref filter,IntPtr.Zero,callback,out handle);
            if(result!=0) throw new InvalidOperationException("Device notification registration failed: CONFIGRET "+result);
        }
        public void Dispose() { if(handle!=IntPtr.Zero) { CM_Unregister_Notification(handle); handle=IntPtr.Zero; } GC.KeepAlive(callback); }
    }
}
