using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Net.Sockets;

namespace DisplayHandoff {
static class Tests {
    class Fake : IHardware {
        public List<string> Calls=new List<string>(); public string Fail="";
        public List<MonitorInfo> Snapshot(){return new List<MonitorInfo>{new MonitorInfo {Device="lg",Identity="id-lg",Mode=new byte[220]},new MonitorInfo {Device="27a",Identity="id-left",Mode=new byte[220]},new MonitorInfo {Device="27b",Identity="id-right",Mode=new byte[220]}};}
        public AdlTarget ResolveTarget(AdlTarget saved){if(Fail=="resolve")throw new Exception("injected resolve");return Amd.ResolveTarget(saved,new List<AdlTarget>{new AdlTarget {Device="lg",Identity="id-lg",Name="LG ULTRAGEAR+",Adapter=2,Display=8}});}
        void Hit(string call){Calls.Add(call);if(Fail==call)throw new Exception("injected "+call);}
        public void Move(List<MonitorInfo> m,string t,string f){Hit("move");}
        public void Detach(List<MonitorInfo> m,string t,string f){Hit("detach");}
        public void Restore(List<MonitorInfo> m){Hit("restore");}
        public void Switch(AdlTarget t,byte input){Hit(input==0xd1?"usb":"dp");}
    }
    static int count;
    static void Assert(bool ok,string message){if(!ok)throw new Exception("FAIL: "+message);count++;}
    static void Throws(Action action,string message){bool threw=false;try{action();}catch{threw=true;}Assert(threw,message);}
    public static void Run(string output) {
        string oldRoot=Files.Root;Files.Root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output)),"test-state-"+Guid.NewGuid().ToString("N"));
        try {
            var c=new Settings {Role="game",Target="old-lg",Fallback="old-left",TargetIdentity="id-lg",FallbackIdentity="id-left",AmdTarget=new AdlTarget {Device="old-lg",Identity="id-lg",Name="LG ULTRAGEAR+"},Host="127.0.0.1"};c.SetKey(Wire.Random());
            Assert(!Files.Json.Serialize(c).Contains(c.Key),"key must not be stored in plaintext");
            var h=new Fake();var engine=new Controller(c,h);
            engine.Give();Assert(string.Join(",",h.Calls)=="move,usb,detach","DDC precedes DP detachment");Assert(engine.Pending,"persist recovery state");
            Assert(c.Target=="lg" && c.Fallback=="27a" && c.AmdTarget.Adapter==2,"handoff re-resolves renumbered devices before moving windows");
            var recovered=Files.Read<SavedState>("restore.json");Assert(recovered.Monitors.Count==3 && recovered.Monitors.All(m=>m.Mode.Length==220),"persist all three native display modes");
            Assert(new Controller(c,h).Pending,"recovery survives controller restart");
            engine.Give();Assert(h.Calls.Count==3,"give idempotent");
            engine.Restore();Assert(string.Join(",",h.Calls)=="move,usb,detach,restore,dp","restore topology before DP input");Assert(!engine.Pending,"clear only after success");
            engine.Restore();Assert(h.Calls.Count==5,"return idempotent");
            foreach(string failure in new[]{"move","usb","detach"}) {
                h.Calls.Clear();h.Fail=failure;Throws(()=>engine.Give(),"surface "+failure+" failure");
                Assert(h.Calls.Contains("restore") && h.Calls.Last()=="dp","rollback on "+failure);Assert(!engine.Pending,"rollback clears recovery state");
            }
            h.Fail="";engine.Give();h.Fail="restore";Throws(()=>engine.Restore(),"restore failure");Assert(engine.Pending,"keep state when restore fails");Assert(h.Calls.Last()=="dp","attempt DP despite topology failure");
            h.Fail="dp";Throws(()=>engine.Restore(),"DP failure");Assert(engine.Pending,"keep state when DP fails");h.Fail="";engine.Restore();
            c.FallbackIdentity="id-lg";h.Calls.Clear();Throws(()=>engine.Give(),"reject same fallback");Assert(h.Calls.Count==0,"validation before mutations");c.FallbackIdentity="id-left";
            c.AmdTarget.Identity="wrong";Throws(()=>engine.Give(),"reject mismatched AMD identity");c.AmdTarget.Identity="id-lg";
            h.Fail="resolve";Throws(()=>engine.Give(),"AMD preflight failure");Assert(!engine.Pending && h.Calls.Count==0,"failed preflight does not trap user in recovery or move windows");h.Fail="";
            c.FallbackIdentity=null;Throws(()=>engine.Give(),"legacy fallback requires explicit save");Assert(!engine.Pending,"legacy validation does not create recovery state");c.FallbackIdentity="id-left";
            var physical=h.Snapshot();physical[0].Primary=true;
            Displays.Put(physical[0].Mode,172,5120);Displays.Put(physical[0].Mode,176,2160);
            Displays.Put(physical[1].Mode,76,-1440);Displays.Put(physical[1].Mode,172,1440);Displays.Put(physical[1].Mode,176,2560);
            Displays.Put(physical[2].Mode,76,5120);Displays.Put(physical[2].Mode,172,1440);Displays.Put(physical[2].Mode,176,2560);
            var left=Displays.DetachedLayout(physical,"lg","27a");
            Assert(left.Single(m=>m.Device=="27a").Bounds.X==0 && left.Single(m=>m.Device=="27a").Primary,"left fallback becomes primary");
            Assert(left.Single(m=>m.Device=="27b").Bounds.X==1440,"close 5120px hole without disabling second 27");
            Assert(left.Single(m=>m.Device=="lg").Bounds.Width==0,"detach only LG");
            var right=Displays.DetachedLayout(physical,"lg","27b");
            Assert(right.Single(m=>m.Device=="27a").Bounds.X==-1440 && right.Single(m=>m.Device=="27b").Primary,"right fallback layout");
            Assert(physical[0].Bounds.Width==5120 && physical[2].Bounds.X==5120,"saved layout remains immutable");
            var renumbered=h.Snapshot();renumbered[0].Device="DISPLAY3";renumbered[1].Device="DISPLAY1";renumbered[2].Device="DISPLAY2";
            var remapped=Displays.ResolveSaved(physical,renumbered);
            Assert(remapped[0].Device=="DISPLAY3" && remapped[1].Device=="DISPLAY1","restore follows identities, not reused display numbers");
            Assert(remapped[1].Identity!=remapped[2].Identity,"two identical secondary models stay distinct");
            Assert(physical[0].Device=="lg" && remapped[2].Bounds==physical[2].Bounds,"remap preserves original snapshot and saved layout");
            Assert(Displays.Matches(physical,remapped),"already-restored topology matches despite numbering changes");
            Displays.Put(remapped[2].Mode,184,144);Assert(!Displays.Matches(physical,remapped),"refresh mismatch requires restore");
            Throws(()=>Displays.ResolveSaved(physical,renumbered.Take(2).ToList()),"missing physical display is not replaced by same number");
            var duplicates=renumbered.Concat(new[]{renumbered[0]}).ToList();Throws(()=>Displays.ResolveSaved(physical,duplicates),"ambiguous physical display is rejected");
            var legacy=new AdlTarget {Name="LG ULTRAGEAR+",Device="DISPLAY1",Adapter=0,Display=8};
            var detected=new AdlTarget {Name="LG ULTRAGEAR+",Device="DISPLAY3",Identity="id-lg",Adapter=2,Display=8};
            Assert(Amd.ResolveTarget(legacy,new List<AdlTarget>{detected}).Adapter==2,"legacy AMD index migration");
            legacy.Identity="id-lg";detected.Name="LG changed friendly name";
            Assert(Amd.ResolveTarget(legacy,new List<AdlTarget>{detected}).Device=="DISPLAY3","physical identity survives friendly-name and display-number changes");
            detected.Identity="different-lg";detected.Name=legacy.Name;
            Throws(()=>Amd.ResolveTarget(legacy,new List<AdlTarget>{detected}),"stable identity does not fall back to another LG");
            legacy.Identity=null;Throws(()=>Amd.ResolveTarget(legacy,new List<AdlTarget>{detected,detected}),"legacy same-name ambiguity rejected");
            detected.Identity=null;Throws(()=>Amd.ResolveTarget(legacy,new List<AdlTarget>{detected}),"missing identity is rejected");
            Files.Save("restore.json",new SavedState {Stage="preparing",Monitors=physical,Target=legacy});
            Assert(engine.Status.Contains("未完了") && !engine.Status.Contains("貸出中"),"preparing state is not misreported as loaned");
            engine.Restore();Assert(!engine.Pending && File.Exists(Files.PathFor("last-restored.json")),"legacy recovery keeps an archived snapshot and clears pending state");
            byte[] packet=Amd.Packet(0xd1);Assert(BitConverter.ToString(packet)=="6E-50-84-03-F4-00-D1-9C","USB-C vendor payload");Assert(Amd.Packet(0xd0)[7]==0x9d,"DP checksum");
            int executed=0;
            using(var server=new Server(0,c.Key,command=>{executed++;return command=="status"?"ready":"returned";})) {
                c.Port=server.Port;Assert(Wire.Call(c,"status")=="ready","authenticated status");Assert(Wire.Call(c,"return")=="returned","authenticated return");
                Throws(()=>Wire.Call(c,"give"),"reject remote give");Assert(executed==2,"unsupported operation not executed");
                string key=c.Key;c.SetKey(Wire.Random());Throws(()=>Wire.Call(c,"return"),"wrong key rejected");Assert(executed==2,"wrong key never runs");c.SetKey(key);
                string captured;
                using(var client=new TcpClient("127.0.0.1",server.Port)) {client.ReceiveTimeout=3000;var s=client.GetStream();string challenge=Wire.Read(s);captured="return|"+Wire.Sign(key,challenge+"|return");Wire.Write(s,captured);Wire.Read(s);}
                using(var client=new TcpClient("127.0.0.1",server.Port)) {client.ReceiveTimeout=3000;var s=client.GetStream();Wire.Read(s);Wire.Write(s,captured);Throws(()=>Wire.Read(s),"replay rejected");}
                Assert(executed==3,"replay never runs");Assert(Wire.Call(c,"status")=="ready","server survives invalid auth");
            }
            File.WriteAllText(output,"PASS: "+count+" assertions\r\nCovered: transition order, duplicate commands, rollback, crash recovery state retention, monitor identity validation, LG payload/checksum, DPAPI key storage, authenticated loopback, command allowlist, wrong key and replay rejection.\r\nNo physical display changes were made.\r\n");
        } finally {string temp=Files.Root;Files.Root=oldRoot;if(Directory.Exists(temp))Directory.Delete(temp,true);}
    }
}
}
