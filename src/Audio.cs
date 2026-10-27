using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace DisplayHandoff {
public class AudioOutput {
    public string Id,Name;
    public override string ToString(){return Name;}
}
public class AudioDefaults {
    public string Console,Multimedia,Communications;
    public bool IncludeCommunications;
    public static bool IsSelected(AudioDefaults actual,string id) {return actual.Console==id && actual.Multimedia==id && (!actual.IncludeCommunications || actual.Communications==id);}
}
public interface IAudioRouting {
    List<AudioOutput> List();
    AudioDefaults Capture(bool communications);
    void Validate(string id);
    void Select(string id,bool communications);
    void Restore(AudioDefaults saved);
}
public sealed class WindowsAudio : IAudioRouting {
    [ComImport,Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class EnumeratorClass {}
    [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface Enumerator {
        [PreserveSig] int EnumAudioEndpoints(int flow,uint state,out Collection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow,int role,out Device device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)]string id,out Device device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr callback);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr callback);
    }
    [ComImport,Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface Collection {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index,out Device device);
    }
    [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface Device {
        [PreserveSig] int Activate(ref Guid iid,uint context,IntPtr parameters,out IntPtr instance);
        [PreserveSig] int OpenPropertyStore(uint access,out PropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)]out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [StructLayout(LayoutKind.Sequential)] struct PropertyKey {public Guid Format; public uint Id;}
    [StructLayout(LayoutKind.Explicit,Size=24)] struct PropertyValue {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }
    [ComImport,Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface PropertyStore {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index,out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key,out PropertyValue value);
        [PreserveSig] int SetValue(ref PropertyKey key,ref PropertyValue value);
        [PreserveSig] int Commit();
    }
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropertyValue value);
    [ComImport,Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")] class PolicyClass {}
    // Windows' undocumented default-endpoint policy interface. Only slot 10 is invoked.
    [ComImport,Guid("F8679F50-850A-41CF-9C72-430F290290C8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface Policy {
        void GetMixFormat();void GetDeviceFormat();void ResetDeviceFormat();void SetDeviceFormat();
        void GetProcessingPeriod();void SetProcessingPeriod();void GetShareMode();void SetShareMode();
        void GetPropertyValue();void SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)]string id,int role);
        void SetEndpointVisibility();
    }
    static void Release(object value){if(value!=null && Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}
    static void Check(int hr){Marshal.ThrowExceptionForHR(hr);}
    static string DefaultId(Enumerator e,int role) {
        Device device=null;
        try {int hr=e.GetDefaultAudioEndpoint(0,role,out device);if(hr==unchecked((int)0x80070490))return null;Check(hr);string id;Check(device.GetId(out id));return id;}
        finally {Release(device);}
    }
    public List<AudioOutput> List() {
        Enumerator e=(Enumerator)new EnumeratorClass();Collection devices=null;
        try {
            Check(e.EnumAudioEndpoints(0,1,out devices));uint count;Check(devices.GetCount(out count));var result=new List<AudioOutput>();
            for(uint i=0;i<count;i++) {
                Device device=null;PropertyStore properties=null;PropertyValue value=new PropertyValue();
                try {
                    Check(devices.Item(i,out device));string id;Check(device.GetId(out id));Check(device.OpenPropertyStore(0,out properties));
                    var key=new PropertyKey {Format=new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),Id=14};Check(properties.GetValue(ref key,out value));
                    string name=value.Type==31?Marshal.PtrToStringUni(value.Pointer):id;
                    result.Add(new AudioOutput {Id=id,Name=name??id});
                }finally {PropVariantClear(ref value);Release(properties);Release(device);}
            }return result;
        }finally {Release(devices);Release(e);}
    }
    public AudioDefaults Capture(bool communications) {
        Enumerator e=(Enumerator)new EnumeratorClass();
        try {return new AudioDefaults {Console=DefaultId(e,0),Multimedia=DefaultId(e,1),Communications=DefaultId(e,2),IncludeCommunications=communications};}
        finally {Release(e);}
    }
    public void Validate(string id) {if(string.IsNullOrEmpty(id) || !List().Any(d=>d.Id==id))throw new Exception("選択した音声出力が見つかりません。「音声」タブで接続先を確認してください。");}
    void SetRoles(AudioDefaults desired) {
        var ids=new[]{desired.Console,desired.Multimedia,desired.Communications};int count=desired.IncludeCommunications?3:2;
        for(int role=0;role<count;role++){if(string.IsNullOrEmpty(ids[role]))throw new Exception("元の音声出力を取得できません。Windowsのサウンド設定を確認してください。");Validate(ids[role]);}
        Policy policy=(Policy)new PolicyClass();
        try {
            for(int role=0;role<count;role++)Check(policy.SetDefaultEndpoint(ids[role],role));
        }finally {Release(policy);}
        for(int retry=0;retry<20;retry++) {
            var actual=Capture(desired.IncludeCommunications);
            if(actual.Console==desired.Console && actual.Multimedia==desired.Multimedia && (!desired.IncludeCommunications || actual.Communications==desired.Communications))return;
            Thread.Sleep(50);
        }
        throw new Exception("音声出力の変更を確認できませんでした。Windowsのサウンド設定を確認してください。");
    }
    public void Select(string id,bool communications) {SetRoles(new AudioDefaults {Console=id,Multimedia=id,Communications=id,IncludeCommunications=communications});}
    public void Restore(AudioDefaults saved) {
        // A DP/HDMI endpoint may reappear slightly after its display is re-enabled.
        for(int retry=0;;retry++) {try {SetRoles(saved);return;}catch {if(retry>=7)throw;Thread.Sleep(250);}}
    }
}
}
