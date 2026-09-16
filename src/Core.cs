using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Web.Script.Serialization;

namespace DisplayHandoff {
public class Settings {
    public string Role="game", Target="", Fallback="", Host="";
    public string TargetIdentity, FallbackIdentity;
    public AdlTarget AmdTarget;
    public int Port=45845;
    public string ProtectedKey="";
    [ScriptIgnore] public string Key { get { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(ProtectedKey),null,DataProtectionScope.CurrentUser)); } }
    public void SetKey(string key) { ProtectedKey=Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key),null,DataProtectionScope.CurrentUser)); }
}
public class Pairing { public string Host; public int Port; public string Key; public string Format="DisplayHandoff/1"; }
public class SavedState { public string Stage; public List<MonitorInfo> Monitors; public AdlTarget Target; }
public static class Files {
    public static string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DisplayHandoff");
    public static JavaScriptSerializer Json { get { return new JavaScriptSerializer {MaxJsonLength=1024*1024}; } }
    public static string PathFor(string name) { Directory.CreateDirectory(Root); return Path.Combine(Root,name); }
    public static T Read<T>(string name) { return Json.Deserialize<T>(File.ReadAllText(PathFor(name),Encoding.UTF8)); }
    public static void Save(string name,object data) {
        var path=PathFor(name); var temp=path+".tmp";
        var bytes=Encoding.UTF8.GetBytes(Json.Serialize(data));
        using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)) {stream.Write(bytes,0,bytes.Length); stream.Flush(true);}
        if(File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path);
    }
    public static void Log(string text) { try {File.AppendAllText(PathFor("activity.log"),DateTime.Now.ToString("s")+" "+text+Environment.NewLine,Encoding.UTF8);} catch {} }
    public static Settings Load() {
        if(File.Exists(PathFor("settings.json"))) return Read<Settings>("settings.json");
        var c=new Settings(); c.Host=Environment.MachineName; c.SetKey(Wire.Random()); Save("settings.json",c); return c;
    }
}
public interface IHardware {
    List<MonitorInfo> Snapshot();
    AdlTarget ResolveTarget(AdlTarget saved);
    void Move(List<MonitorInfo> monitors,string target,string fallback);
    void Detach(List<MonitorInfo> monitors,string target,string fallback);
    void Restore(List<MonitorInfo> monitors);
    void Switch(AdlTarget target,byte code);
}
public class Hardware : IHardware {
    public List<MonitorInfo> Snapshot() { return Displays.List(); }
    public AdlTarget ResolveTarget(AdlTarget saved) {using(var amd=new Amd())return Amd.ResolveTarget(saved,amd.List());}
    public void Move(List<MonitorInfo> list,string target,string fallback) { int n=Displays.MoveWindows(list.Single(m=>m.Device==target),list.Single(m=>m.Device==fallback)); Files.Log("退避したウィンドウ: "+n); }
    public void Detach(List<MonitorInfo> list,string target,string fallback) { Displays.Detach(list,target,fallback); }
    public void Restore(List<MonitorInfo> list) { Displays.Restore(list); }
    public void Switch(AdlTarget target,byte code) { using(var amd=new Amd()) amd.Switch(target,code); }
}
public class Controller {
    readonly object gate=new object(); readonly IHardware hw;
    public Settings Config;
    public Controller(Settings config,IHardware hardware) {Config=config;hw=hardware;}
    public bool Pending {get{return File.Exists(Files.PathFor("restore.json"));}}
    public string Status {get{if(!Pending)return "ゲーム用PCで使用";try{return Files.Read<SavedState>("restore.json").Stage=="work"?"仕事用PCへ貸出中":"前回の処理が未完了 / 復旧が必要";}catch{return "復旧データを確認してください";}}}
    void Validate(List<MonitorInfo> list) {
        if(Config.Role!="game") throw new Exception("ゲーム用PCの設定が必要です。");
        if(string.IsNullOrEmpty(Config.TargetIdentity) || string.IsNullOrEmpty(Config.FallbackIdentity))throw new Exception("旧バージョンの画面設定です。モニター再検出で45インチと退避先を確認し、設定を保存してください。");
        var target=list.Where(m=>m.Identity==Config.TargetIdentity).ToList();
        var fallback=list.Where(m=>m.Identity==Config.FallbackIdentity).ToList();
        if(target.Count!=1 || fallback.Count!=1 || Config.TargetIdentity==Config.FallbackIdentity)throw new Exception("保存した45インチまたは退避先を識別できません。接続とモニター設定を確認してください。");
        var resolved=hw.ResolveTarget(Config.AmdTarget);
        if(resolved.Name.IndexOf("LG",StringComparison.OrdinalIgnoreCase)<0 || resolved.Identity!=Config.TargetIdentity || resolved.Device!=target[0].Device)throw new Exception("45インチのWindows画面とAMDのLGモニターが一致していません。");
        Config.Target=target[0].Device;Config.Fallback=fallback[0].Device;Config.AmdTarget=resolved;
        Files.Save("settings.json",Config);
    }
    public string Give() {
        lock(gate) {
            if(Pending) return Status+"。「元の画面構成へ復旧」を実行してください。";
            var list=hw.Snapshot(); Validate(list);
            var state=new SavedState {Stage="preparing",Monitors=list,Target=Config.AmdTarget};
            Files.Save("restore.json",state);
            try {
                hw.Move(list,Config.Target,Config.Fallback);
                // DDC must be sent while the DP display path is still active.
                hw.Switch(Config.AmdTarget,0xd1);
                hw.Detach(list,Config.Target,Config.Fallback);
                state.Stage="work"; Files.Save("restore.json",state);
                Files.Log("USB-C切替要求を送信、45インチのWindows出力を無効化");
                return "仕事用PCへ切替要求を送り、45インチ出力を無効にしました。";
            } catch(Exception error) {
                Files.Log("引渡し失敗: "+error.Message);
                try { RestoreInternal(); } catch(Exception recovery) { throw new Exception(error.Message+"\r\n復旧も未完了: "+recovery.Message); }
                throw new Exception(error.Message+"\r\n元の画面構成へ復旧しました。");
            }
        }
    }
    string RestoreInternal() {
        if(!Pending) return "貸出中の画面構成はありません。";
        var state=Files.Read<SavedState>("restore.json");
        Exception topologyError=null;
        try { hw.Restore(state.Monitors); } catch(Exception e) {topologyError=e;}
        // Even if verification failed, try to bring the physical monitor back.
        try {hw.Switch(state.Target,0xd0);} catch(Exception e) { throw new Exception("DisplayPortへの切替に失敗。復旧データは保持しています。 "+e.Message+(topologyError==null?"":" / "+topologyError.Message)); }
        if(topologyError!=null) throw topologyError;
        Files.Save("last-restored.json",state);
        File.Delete(Files.PathFor("restore.json")); Files.Log("元のWindows画面構成を復元、DisplayPort切替要求を送信");
        return "ゲーム用PCの画面構成を復元し、DisplayPortへ切替要求を送りました。";
    }
    public string Restore() {lock(gate) {return RestoreInternal();}}
    public string TestInput() {
        lock(gate) {
            if(Pending) throw new Exception("まず元の画面構成へ復旧してください。");
            var list=hw.Snapshot(); Validate(list);
            Files.Save("restore.json",new SavedState {Stage="input-test",Monitors=list,Target=Config.AmdTarget});
            try {hw.Switch(Config.AmdTarget,0xd1); Thread.Sleep(15000);} finally {RestoreInternal();}
            return "15秒テスト終了。DisplayPortへの復帰要求を送りました。";
        }
    }
}
public static class Wire {
    public static string Random() {var bytes=new byte[32];using(var rng=RandomNumberGenerator.Create()) rng.GetBytes(bytes);return Convert.ToBase64String(bytes);}
    public static string Sign(string key,string text) {using(var h=new HMACSHA256(Convert.FromBase64String(key))) return Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes(text)));}
    public static bool Equal(string a,string b) {if(a==null || b==null || a.Length!=b.Length)return false;int diff=0;for(int i=0;i<a.Length;i++)diff|=a[i]^b[i];return diff==0;}
    public static string Read(Stream stream) {
        var bytes=new List<byte>();
        for(int i=0;i<8192;i++) {int b=stream.ReadByte();if(b<0)throw new IOException("接続が閉じられました。");if(b==10)return Encoding.UTF8.GetString(bytes.ToArray());bytes.Add((byte)b);}
        throw new IOException("通信データが長すぎます。");
    }
    public static void Write(Stream stream,string line) {var bytes=Encoding.UTF8.GetBytes(line+"\n");stream.Write(bytes,0,bytes.Length);}
    public static string Call(Settings config,string command) {
        using(var client=new TcpClient()) {
            var connect=client.BeginConnect(config.Host,config.Port,null,null);
            using(connect.AsyncWaitHandle) {if(!connect.AsyncWaitHandle.WaitOne(4000)) throw new Exception("ゲーム用PCに接続できません。IPアドレス・常駐アプリ・ファイアウォールを確認してください。");client.EndConnect(connect);}
            client.ReceiveTimeout=30000;client.SendTimeout=5000;
            using(var stream=client.GetStream()) {
                var challenge=Read(stream);
                if(!challenge.StartsWith("DH1|")) throw new IOException("対応していない接続先です。");
                string key=config.Key;
                Write(stream,command+"|"+Sign(key,challenge+"|"+command));
                var response=Read(stream).Split('|');
                if(response.Length!=2 || !Equal(response[1],Sign(key,challenge+"|response|"+response[0]))) throw new Exception("接続認証が一致しません。ペアリングをやり直してください。");
                var text=Encoding.UTF8.GetString(Convert.FromBase64String(response[0]));
                if(text.StartsWith("ERROR:")) throw new Exception(text.Substring(6));
                return text;
            }
        }
    }
}
public sealed class Server : IDisposable {
    readonly TcpListener listener; readonly Thread worker; volatile bool stopped;
    readonly string key; readonly Func<string,string> action;
    public Server(int port,string secret,Func<string,string> callback) {
        key=secret;action=callback;listener=new TcpListener(IPAddress.Any,port);listener.Start(4);
        worker=new Thread(Run);worker.IsBackground=true;worker.Start();
    }
    public int Port {get{return ((IPEndPoint)listener.LocalEndpoint).Port;}}
    void Run() {
        while(!stopped) {
            try {
                using(var client=listener.AcceptTcpClient()) {
                    client.ReceiveTimeout=4000;client.SendTimeout=4000;
                    using(var stream=client.GetStream()) {
                        string challenge="DH1|"+Wire.Random();Wire.Write(stream,challenge);
                        var parts=Wire.Read(stream).Split('|');
                        if(parts.Length!=2 || !Wire.Equal(parts[1],Wire.Sign(key,challenge+"|"+parts[0]))) continue;
                        string result;
                        if(parts[0]!="status" && parts[0]!="return") result="ERROR:対応していない操作です。";
                        else {try {result=action(parts[0]);} catch(Exception e) {result="ERROR:"+e.Message;Files.Log(result);}}
                        var payload=Convert.ToBase64String(Encoding.UTF8.GetBytes(result));
                        Wire.Write(stream,payload+"|"+Wire.Sign(key,challenge+"|response|"+payload));
                    }
                }
            } catch(Exception e) {if(!stopped)Files.Log("通信: "+e.Message);}
        }
    }
    public void Dispose() {stopped=true;listener.Stop();}
}
}
