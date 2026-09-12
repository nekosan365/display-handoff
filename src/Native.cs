using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Threading;

namespace DisplayHandoff {
public class MonitorInfo {
    public string Device, Identity, Name;
    public bool Primary;
    public byte[] Mode;
    public override string ToString() { return Name + "  [" + Device.Replace(@"\\.\", "") + "] " + BitConverter.ToInt32(Mode,172) + " × " + BitConverter.ToInt32(Mode,176) + (Bounds.X<0?" 左側":Bounds.X>0?" 右側":""); }
    public Rectangle Bounds { get { return new Rectangle(BitConverter.ToInt32(Mode,76), BitConverter.ToInt32(Mode,80), BitConverter.ToInt32(Mode,172), BitConverter.ToInt32(Mode,176)); } }
}
public static class Displays {
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    struct DeviceInfo {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string Description;
        public int Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string Key;
    }
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern bool EnumDisplayDevices(string name, uint n, ref DeviceInfo info, uint flags);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern bool EnumDisplaySettings(string name, int n, byte[] mode);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int ChangeDisplaySettingsEx(string name, byte[] mode, IntPtr window, uint flags, IntPtr param);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindow proc, IntPtr data);
    delegate bool EnumWindow(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr window);
    [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowTextLength(IntPtr window);
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int L,T,R,B; }
    public static void Put(byte[] bytes, int offset, int value) { Buffer.BlockCopy(BitConverter.GetBytes(value),0,bytes,offset,4); }
    public static List<MonitorInfo> List() {
        var result = new List<MonitorInfo>();
        for (uint i=0;;i++) {
            var d = new DeviceInfo(); d.cb=Marshal.SizeOf(d);
            if (!EnumDisplayDevices(null,i,ref d,0)) break;
            if ((d.Flags & 1)==0 || (d.Flags & 8)!=0) continue;
            byte[] mode = new byte[220]; mode[68]=220;
            if (!EnumDisplaySettings(d.Name,-1,mode)) continue;
            var child = new DeviceInfo(); child.cb=Marshal.SizeOf(child);
            EnumDisplayDevices(d.Name,0,ref child,0);
            result.Add(new MonitorInfo { Device=d.Name, Identity=child.Id, Name=child.Description ?? d.Description, Primary=(d.Flags & 4)!=0, Mode=mode });
        }
        return result;
    }
    public static void ValidateIdentity(IEnumerable<MonitorInfo> saved) {
        foreach(var item in saved) {
            var child=new DeviceInfo(); child.cb=Marshal.SizeOf(child);
            if(!EnumDisplayDevices(item.Device,0,ref child,0) || child.Id!=item.Identity)
                throw new Exception("モニターの接続が変わりました。Windows のディスプレイ設定で復旧してください: " + item.Device);
        }
    }
    static void Stage(MonitorInfo item, byte[] mode, bool primary) {
        int rc=ChangeDisplaySettingsEx(item.Device,mode,IntPtr.Zero,0x10000001u | (primary?0x10u:0u),IntPtr.Zero);
        if(rc!=0) throw new Exception("画面構成の設定に失敗: " + item.Device + " / " + rc);
    }
    static void Commit() { int rc=ChangeDisplaySettingsEx(null,null,IntPtr.Zero,0,IntPtr.Zero); if(rc!=0) throw new Exception("画面構成の適用に失敗: " + rc); }
    public static void Restore(List<MonitorInfo> saved) {
        ValidateIdentity(saved);
        foreach(var m in saved.OrderByDescending(m=>m.Primary)) Stage(m,(byte[])m.Mode.Clone(),m.Primary);
        Commit();
        Thread.Sleep(800);
        var active=List();
        foreach(var m in saved) if(!active.Any(a=>a.Device==m.Device && a.Bounds==m.Bounds && a.Primary==m.Primary)) throw new Exception("元の画面構成への復帰を確認できません。復旧ボタンを再実行してください。");
    }
    public static List<MonitorInfo> DetachedLayout(List<MonitorInfo> saved, string target, string fallback) {
        var keep=saved.Single(m=>m.Device==fallback);
        var primary=saved.Single(m=>m.Primary);
        var origin=primary.Device==target ? keep : primary;
        var remaining=saved.Where(m=>m.Device!=target).OrderBy(m=>m.Bounds.X).ToList();
        int originX=remaining.TakeWhile(m=>m.Device!=origin.Device).Sum(m=>m.Bounds.Width),nextX=0;
        var plan=new List<MonitorInfo>();
        foreach(var m in remaining) {
            var mode=(byte[])m.Mode.Clone();
            // Close the gap left by the wide monitor; disconnected desktop islands can be rejected.
            Put(mode,76,nextX-originX); Put(mode,80,0);nextX+=m.Bounds.Width;
            plan.Add(new MonitorInfo {Device=m.Device,Identity=m.Identity,Name=m.Name,Mode=mode,Primary=m.Device==origin.Device});
        }
        var off=saved.Single(m=>m.Device==target); var disabled=(byte[])off.Mode.Clone();
        Put(disabled,72,0x180020); Put(disabled,172,0); Put(disabled,176,0);
        plan.Add(new MonitorInfo {Device=off.Device,Identity=off.Identity,Name=off.Name,Mode=disabled,Primary=false});
        return plan;
    }
    public static void Detach(List<MonitorInfo> saved, string target, string fallback) {
        foreach(var m in DetachedLayout(saved,target,fallback).OrderByDescending(m=>m.Primary))Stage(m,m.Mode,m.Primary);
        Commit(); Thread.Sleep(700);
        if(List().Any(m=>m.Device==target)) throw new Exception("45インチ出力の無効化を確認できません。");
    }
    public static int MoveWindows(MonitorInfo target, MonitorInfo fallback) {
        int count=0;
        var screen=System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==fallback.Device);
        var area=screen==null?fallback.Bounds:screen.WorkingArea;
        EnumWindows(delegate(IntPtr h, IntPtr unused) {
            if (!IsWindowVisible(h) || GetWindow(h,4)!=IntPtr.Zero || GetWindowTextLength(h)==0 || IsIconic(h)) return true;
            Rect r; if(!GetWindowRect(h,out r)) return true;
            var rect=Rectangle.FromLTRB(r.L,r.T,r.R,r.B);
            var intersection=Rectangle.Intersect(rect,target.Bounds);
            if(intersection.Width<=0 || intersection.Height<=0) return true;
            // Move only windows mainly on the shared display.
            long overlap=(long)intersection.Width*intersection.Height;
            if(overlap < (long)rect.Width*rect.Height/2) return true;
            bool zoomed=IsZoomed(h); if(zoomed) ShowWindowAsync(h,9);
            int offset=(count%7)*26;
            if(SetWindowPos(h,IntPtr.Zero,area.X+offset,area.Y+offset,Math.Min(Math.Max(320,rect.Width),area.Width-offset),Math.Min(Math.Max(180,rect.Height),area.Height-offset),0x0014 | 0x4000)) count++;
            if(zoomed) ShowWindowAsync(h,3);
            return true;
        },IntPtr.Zero);
        return count;
    }
}
public class AdlTarget {
    public int Adapter, Display;
    public string Name, Device;
    public override string ToString() { return Name+"  (AMD "+Adapter+" / "+Display+")"; }
}
public sealed class Amd : IDisposable {
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr Alloc(int size);
    [DllImport("atiadlxx.dll", CallingConvention=CallingConvention.Cdecl)] static extern int ADL_Main_Control_Create(Alloc cb,int active);
    [DllImport("atiadlxx.dll", CallingConvention=CallingConvention.Cdecl)] static extern int ADL_Main_Control_Destroy();
    [DllImport("atiadlxx.dll", CallingConvention=CallingConvention.Cdecl)] static extern int ADL_Adapter_NumberOfAdapters_Get(out int count);
    [DllImport("atiadlxx.dll", CallingConvention=CallingConvention.Cdecl)] static extern int ADL_Adapter_AdapterInfo_Get(IntPtr adapters,int size);
    [DllImport("atiadlxx.dll", CallingConvention=CallingConvention.Cdecl)] static extern int ADL_Display_DisplayInfo_Get(int adapter,out int count,out IntPtr displays,int force);
    [DllImport("atiadlxx.dll", CallingConvention=CallingConvention.Cdecl)] static extern int ADL_Display_DDCBlockAccess_Get(int adapter,int display,int option,int command,int length,byte[] data,ref int read,IntPtr output);
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Ansi)] struct AdapterInfo {
        public int Size, Index;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string Udid;
        public int Bus,Device,Function,Vendor;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string Display;
        public int Present,Exist;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string Path;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string PathExt;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string Pnp;
        public int OsIndex;
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Ansi)] struct DisplayInfo {
        public int Logical,Physical,LogicalAdapter,PhysicalAdapter,Controller;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string Manufacturer;
        public int Type,Output,Connector,Mask,Value;
    }
    readonly Alloc allocator=Marshal.AllocHGlobal;
    public Amd() { if(ADL_Main_Control_Create(allocator,1)!=0) throw new Exception("AMD ADL を初期化できません。"); }
    public List<AdlTarget> List() {
        int count; if(ADL_Adapter_NumberOfAdapters_Get(out count)!=0 || count<0 || count>128) throw new Exception("AMD GPU の列挙に失敗しました。");
        var result=new List<AdlTarget>(); int size=Marshal.SizeOf(typeof(AdapterInfo));
        IntPtr p=Marshal.AllocHGlobal(size*count);
        try {
            for(int i=0;i<size*count;i++) Marshal.WriteByte(p,i,0);
            for(int i=0;i<count;i++) Marshal.WriteInt32(p,i*size,size);
            if(ADL_Adapter_AdapterInfo_Get(p,size*count)!=0) throw new Exception("AMD GPU 情報を取得できません。");
            for(int i=0;i<count;i++) {
                var a=(AdapterInfo)Marshal.PtrToStructure(IntPtr.Add(p,i*size),typeof(AdapterInfo));
                IntPtr dp=IntPtr.Zero; int n;
                try {
                    if(ADL_Display_DisplayInfo_Get(a.Index,out n,out dp,0)!=0 || n<0 || n>128) continue;
                    for(int j=0;j<n;j++) {
                        var d=(DisplayInfo)Marshal.PtrToStructure(IntPtr.Add(dp,j*Marshal.SizeOf(typeof(DisplayInfo))),typeof(DisplayInfo));
                        if(d.LogicalAdapter!=a.Index || (d.Value&1)==0) continue;
                        if(!result.Any(t=>t.Adapter==a.Index && t.Display==d.Logical)) result.Add(new AdlTarget {Adapter=a.Index,Display=d.Logical,Name=d.Name,Device=a.Display});
                    }
                } finally { if(dp!=IntPtr.Zero) Marshal.FreeHGlobal(dp); }
            }
        } finally { Marshal.FreeHGlobal(p); }
        return result;
    }
    public static byte[] Packet(byte input) {
        byte[] bytes={0x6e,0x50,0x84,0x03,0xf4,0x00,input,0};
        for(int i=0;i<7;i++) bytes[7]^=bytes[i];
        return bytes;
    }
    public void Switch(AdlTarget target, byte input) {
        var current=List().SingleOrDefault(t=>t.Adapter==target.Adapter && t.Display==target.Display && t.Name==target.Name && t.Device==target.Device);
        if(current==null) throw new Exception("AMD のモニター識別が変わりました。設定で再選択してください。");
        int read=0; var packet=Packet(input);
        int rc=ADL_Display_DDCBlockAccess_Get(target.Adapter,target.Display,0,0,packet.Length,packet,ref read,IntPtr.Zero);
        if(rc!=0) throw new Exception("LG入力切替コマンドが失敗しました: AMD "+rc);
        Thread.Sleep(900);
    }
    public void Dispose() { ADL_Main_Control_Destroy(); GC.KeepAlive(allocator); }
}
}
