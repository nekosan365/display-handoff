using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Net;
using System.Net.Sockets;

namespace DisplayHandoff {
static class Program {
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    public static EventWaitHandle ShowEvent, GiveEvent, ReturnEvent;
    [STAThread] static int Main(string[] args) {
        SetProcessDPIAware();
        if(args.Contains("--self-test")) {try {Tests.Run(args.Last());return 0;} catch(Exception e) {File.WriteAllText(args.Last(),e.ToString());return 1;}}
        if(args.Contains("--diagnose")) {
            try {
                object amd;
                try {using(var a=new Amd()) amd=a.List();} catch(Exception e) {amd=e.Message;}
                File.WriteAllText(args.Last(),Files.Json.Serialize(new {Monitors=Displays.List(),Amd=amd}),System.Text.Encoding.UTF8); return 0;
            } catch(Exception e) {File.WriteAllText(args.Last(),e.ToString());return 1;}
        }
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        if(args.Contains("--export-pairing")) {
            try {var c=Files.Load();File.WriteAllText(args.Last(),Files.Json.Serialize(new Pairing {Host=c.Host,Port=c.Port,Key=c.Key}),System.Text.Encoding.UTF8);return 0;}catch(Exception e){Files.Log(e.ToString());return 1;}
        }
        if(args.Contains("--check-peer")) {
            try {File.WriteAllText(args.Last(),Wire.Call(Files.Load(),"status"),System.Text.Encoding.UTF8);return 0;}catch(Exception e){File.WriteAllText(args.Last(),e.Message);return 1;}
        }
        if(args.Contains("--setup-game-left")) {
            try {
                if(File.Exists(Files.PathFor("restore.json")))throw new Exception("復旧データがあるためセットアップを中止しました。");
                var config=Files.Load();var displays=Displays.List();AdlTarget lg;
                using(var amd=new Amd())lg=amd.List().Single(t=>t.Name.IndexOf("LG",StringComparison.OrdinalIgnoreCase)>=0);
                var target=displays.Single(m=>m.Device==lg.Device);
                var fallback=displays.Where(m=>m.Device!=lg.Device && m.Bounds.Right<=target.Bounds.Left).OrderByDescending(m=>m.Bounds.Right).First();
                config.Role="game";config.Target=target.Device;config.Fallback=fallback.Device;config.TargetIdentity=target.Identity;config.FallbackIdentity=fallback.Identity;config.AmdTarget=lg;config.Host=args.Last();Files.Save("settings.json",config);
                return 0;
            } catch(Exception e){Files.Log(e.ToString());return 1;}
        }
        if(args.Contains("--preview")) {
            Files.Root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args.Last())),"preview-state");
            using(var form=new MainForm(new string[0])) {form.ShowInTaskbar=false;form.Opacity=0;form.Show();form.PerformLayout();Application.DoEvents();using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(args.Last());}}
            return 0;
        }
        string suffix=System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
        bool created;
        using(var mutex=new Mutex(true,"Local\\DisplayHandoff-"+suffix,out created)) {
            ShowEvent=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\DisplayHandoff-Show-"+suffix);
            GiveEvent=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\DisplayHandoff-Give-"+suffix);
            ReturnEvent=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\DisplayHandoff-Return-"+suffix);
            if(!created) {if(args.Contains("--repair-game-left")){File.WriteAllText(args.Last(),"アプリを終了してから修復してください。");return 2;}if(args.Contains("--give"))GiveEvent.Set();else if(args.Contains("--return"))ReturnEvent.Set();else ShowEvent.Set();return 0;}
            if(args.Contains("--repair-game-left")) {
                try {
                    var config=Files.Load();if(config.Role!="game")throw new Exception("ゲーム用PCで実行してください。");
                    var hardware=new Hardware();var controller=new Controller(config,hardware);
                    var message=controller.Restore();
                    var resolved=hardware.ResolveTarget(config.AmdTarget);var monitors=hardware.Snapshot();
                    var shared=monitors.Single(m=>m.Identity==resolved.Identity);
                    var left=monitors.Where(m=>m.Identity!=shared.Identity && m.Bounds.Right<=shared.Bounds.Left).OrderByDescending(m=>m.Bounds.Right).First();
                    config.Target=shared.Device;config.Fallback=left.Device;config.TargetIdentity=shared.Identity;config.FallbackIdentity=left.Identity;config.AmdTarget=resolved;
                    Files.Save("settings.json",config);
                    File.WriteAllText(args.Last(),Files.Json.Serialize(new {Result=message,Target=shared.Device,Fallback=left.Device,Amd=resolved,Pending=controller.Pending}),System.Text.Encoding.UTF8);
                    return 0;
                }catch(Exception e){Files.Log(e.ToString());File.WriteAllText(args.Last(),e.ToString());return 1;}
            }
            try {Application.Run(new MainForm(args));}
            catch(Exception e) {Files.Log(e.ToString());MessageBox.Show(e.Message,"Display Handoff",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
        }
        return 0;
    }
}
public class MainForm : Form {
    readonly Settings config; readonly Controller controller; Server server;
    readonly ComboBox role=new ComboBox(),target=new ComboBox(),fallback=new ComboBox(),amd=new ComboBox();
    readonly TextBox host=new TextBox(),log=new TextBox();
    readonly NumericUpDown port=new NumericUpDown();
    readonly Label status=new Label(),network=new Label();
    readonly Button main=new Button();
    readonly FlowLayoutPanel actions=new FlowLayoutPanel();
    readonly NotifyIcon tray=new NotifyIcon();
    readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    bool busy,exit;
    public MainForm(string[] args) {
        config=Files.Load(); controller=new Controller(config,new Hardware());
        Text="Display Handoff 0.1.1 • 45GX950A"; Font=new Font("Yu Gothic UI",10); BackColor=Color.FromArgb(245,247,251);
        ClientSize=new Size(820,930); MinimumSize=new Size(800,920); StartPosition=FormStartPosition.CenterScreen;
        var layout=new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(28),ColumnCount=1,RowCount=8};
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,68)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,82));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,264));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,62));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,126));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,30)); Controls.Add(layout);
        layout.Controls.Add(new Label {Text="45インチを、使うPCへ。",Font=new Font(Font.FontFamily,23,FontStyle.Bold),Dock=DockStyle.Fill,AutoSize=false},0,0);
        status.Font=new Font(Font.FontFamily,14,FontStyle.Bold); status.BackColor=Color.FromArgb(226,236,255);status.Padding=new Padding(16);status.Dock=DockStyle.Fill;status.Text="設定を保存して、切替を始めましょう。";layout.Controls.Add(status,0,1);
        var settings=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=6,Padding=new Padding(0,15,0,0)};
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,172)); settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        foreach(var c in new[]{role,target,fallback,amd}) {c.DropDownStyle=ComboBoxStyle.DropDownList;c.Dock=DockStyle.Fill;}
        role.Items.AddRange(new object[]{"ゲーム用PC（デスクトップ）","仕事用PC（Windowsノート）"});role.SelectedIndex=config.Role=="work"?1:0;
        host.Text=config.Host; host.Dock=DockStyle.Fill;port.Minimum=1024;port.Maximum=65535;port.Value=config.Port;port.Dock=DockStyle.Fill;
        AddSetting(settings,0,"このPC",role);AddSetting(settings,1,"45インチの画面",target);AddSetting(settings,2,"27インチの退避先",fallback);AddSetting(settings,3,"LGの入力切替先",amd);AddSetting(settings,4,"ゲーム用PCのIP / 名前",host);AddSetting(settings,5,"接続ポート",port);
        layout.Controls.Add(settings,0,2);
        main.Dock=DockStyle.Fill;main.BackColor=Color.FromArgb(45,96,229);main.ForeColor=Color.White;main.FlatStyle=FlatStyle.Flat;main.Font=new Font(Font.FontFamily,14,FontStyle.Bold);main.Click+=async(s,e)=>await RunPrimary();layout.Controls.Add(main,0,3);
        actions.Dock=DockStyle.Fill;actions.Padding=new Padding(0,8,0,0);layout.Controls.Add(actions,0,4);
        AddButton("設定を保存",()=>Save()); AddButton("モニター再検出",()=>Scan());
        AddButton("接続を確認",async()=>await RunJob(()=>config.Role=="work"?Wire.Call(config,"status"):"ゲーム用PCの状態: "+controller.Status));
        AddButton("ペアリングを書出し",()=>Export()); AddButton("ペアリングを読込",()=>Import());
        AddButton("ショートカット作成",()=>Shortcut(false));AddButton("ログイン時に起動",()=>Shortcut(true));
        AddButton("入力を15秒テスト",async()=>await RunJob(()=>{if(config.Role!="game")throw new Exception("ゲーム用PCから実行してください。");return controller.TestInput();}));
        AddButton("元の画面構成へ復旧",async()=>await RunJob(()=>config.Role=="game"?controller.Restore():Wire.Call(config,"return")));
        network.Dock=DockStyle.Fill;network.ForeColor=Color.FromArgb(65,78,101);layout.Controls.Add(network,0,5);
        log.Multiline=true;log.ReadOnly=true;log.Dock=DockStyle.Fill;log.ScrollBars=ScrollBars.Vertical;log.BorderStyle=BorderStyle.FixedSingle;log.BackColor=Color.White;layout.Controls.Add(log,0,6);
        layout.Controls.Add(new Label {Text="45GX950A  •  DP ⇄ USB-C     /     27インチ2台はゲーム用PCで継続",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=Color.DimGray},0,7);
        role.SelectedIndexChanged+=(s,e)=>RoleUI();Scan();RoleUI();StartServer();
        tray.Icon=SystemIcons.Application;tray.Text="Display Handoff";tray.Visible=true;tray.DoubleClick+=(s,e)=>ShowApp();
        var menu=new ContextMenuStrip();menu.Items.Add("アプリを開く",null,(s,e)=>ShowApp());menu.Items.Add("仕事用PCへ渡す",null,async(s,e)=>await RunJob(()=>{if(config.Role!="game")throw new Exception("ゲーム用PCで実行してください。");return controller.Give();}));
        menu.Items.Add("ゲーム用PCへ返す / 復旧",null,async(s,e)=>await RunJob(()=>config.Role=="game"?controller.Restore():Wire.Call(config,"return")));
        menu.Items.Add("終了",null,(s,e)=>{if(busy || controller.Pending){ShowApp();Append("実行中または貸出中です。復旧してから終了してください。");return;}exit=true;Close();});tray.ContextMenuStrip=menu;
        FormClosing+=(s,e)=>{if(!exit && e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();}};
        FormClosed+=(s,e)=>{timer.Stop();tray.Dispose();if(server!=null)server.Dispose();};
        timer.Interval=350;timer.Tick+=async(s,e)=>{if(Program.ShowEvent.WaitOne(0))ShowApp();if(!busy && Program.GiveEvent.WaitOne(0))await RunJob(()=>{if(config.Role!="game")throw new Exception("このPCは仕事用PCとして設定されています。");return controller.Give();});if(!busy && Program.ReturnEvent.WaitOne(0))await RunJob(()=>config.Role=="game"?controller.Restore():Wire.Call(config,"return"));};timer.Start();
        Shown+=async(s,e)=>{if(controller.Pending)Append("復旧データがあります。「元の画面構成へ復旧」で戻せます。");if(args.Contains("--give"))await RunPrimary();else if(args.Contains("--return"))await RunJob(()=>config.Role=="game"?controller.Restore():Wire.Call(config,"return"));else if(args.Contains("--tray"))Hide();};
        if(!string.IsNullOrEmpty(config.Target))status.Text=config.Role=="game"?controller.Status:"仕事用PC • ゲーム用PCへの返却準備";
        Append("まずモニターと退避先を確認し「設定を保存」。仕事用PCにはペアリングファイルを読み込ませてください。");
    }
    void AddSetting(TableLayoutPanel p,int row,string label,Control control) {p.RowStyles.Add(new RowStyle(SizeType.Percent,100f/6));p.Controls.Add(new Label {Text=label,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,row);p.Controls.Add(control,1,row);}
    void AddButton(string title,Action action) {var b=new Button {Text=title,AutoSize=true,Height=33,FlatStyle=FlatStyle.System};b.Click+=(s,e)=>{try {action();}catch(Exception error){Append(error.Message);MessageBox.Show(this,error.Message,"設定を確認してください");}};actions.Controls.Add(b);}
    void ShowApp() {Show();WindowState=FormWindowState.Normal;Activate();}
    void Append(string message) {if(IsDisposed)return;if(InvokeRequired){BeginInvoke(new Action<string>(Append),message);return;}log.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+message+Environment.NewLine);}
    void RoleUI() {bool game=role.SelectedIndex==0;target.Enabled=fallback.Enabled=amd.Enabled=game;main.Text=game?"仕事用PCへ渡す  →":"ゲーム用PCへ返す  →";}
    void Scan() {
        var monitors=Displays.List();target.Items.Clear();fallback.Items.Clear();amd.Items.Clear();
        foreach(var m in monitors){target.Items.Add(m);fallback.Items.Add(m);}
        try {using(var a=new Amd()) foreach(var t in a.List()){amd.Items.Add(t);foreach(var m in monitors.Where(m=>m.Device==t.Device))m.Name=t.Name;}}catch(Exception e){Append("AMD検出: "+e.Message);}
        AdlTarget preferred=null;
        if(config.AmdTarget!=null) {try {preferred=Amd.ResolveTarget(config.AmdTarget,amd.Items.Cast<AdlTarget>().ToList());}catch(Exception e){Append(e.Message);}}
        else {var candidates=amd.Items.Cast<AdlTarget>().Where(a=>a.Name.IndexOf("LG",StringComparison.OrdinalIgnoreCase)>=0).ToList();if(candidates.Count==1)preferred=candidates[0];}
        foreach(MonitorInfo m in target.Items)if(!string.IsNullOrEmpty(config.TargetIdentity)?m.Identity==config.TargetIdentity:preferred!=null && m.Identity==preferred.Identity)target.SelectedItem=m;
        foreach(MonitorInfo m in fallback.Items)if(!string.IsNullOrEmpty(config.FallbackIdentity) && m.Identity==config.FallbackIdentity)fallback.SelectedItem=m;
        if(fallback.SelectedIndex<0 && string.IsNullOrEmpty(config.FallbackIdentity) && target.SelectedItem!=null) {
            var shared=(MonitorInfo)target.SelectedItem;
            fallback.SelectedItem=monitors.Where(m=>m.Device!=shared.Device && m.Bounds.Right<=shared.Bounds.Left).OrderByDescending(m=>m.Bounds.Right).FirstOrDefault();
        }
        if(preferred!=null)amd.SelectedItem=preferred;
        if(!string.IsNullOrEmpty(config.Target) && string.IsNullOrEmpty(config.TargetIdentity))Append("旧設定の画面番号ではなくモニターを再識別しました。復旧後に退避先を確認し、設定を保存してください。");
        Append("画面を "+monitors.Count+" 台検出しました。退避先の27インチを選択してください。");
    }
    void Save() {
        if(busy || controller.Pending)throw new Exception("実行中・貸出中は設定を変更できません。先に復旧してください。");
        bool game=role.SelectedIndex==0;
        if(game && (target.SelectedItem==null || fallback.SelectedItem==null || amd.SelectedItem==null))throw new Exception("45インチ・27インチ・LG入力切替先を選択してください。");
        if(game) {
            var t=(MonitorInfo)target.SelectedItem;var f=(MonitorInfo)fallback.SelectedItem;var a=(AdlTarget)amd.SelectedItem;
            if(t.Device==f.Device || a.Device!=t.Device || a.Identity!=t.Identity || string.IsNullOrEmpty(t.Identity) || string.IsNullOrEmpty(f.Identity) || a.Name.IndexOf("LG",StringComparison.OrdinalIgnoreCase)<0)throw new Exception("45インチのLGモニターと、別の27インチを選択してください。");
            config.Target=t.Device;config.Fallback=f.Device;config.TargetIdentity=t.Identity;config.FallbackIdentity=f.Identity;config.AmdTarget=a;
        }
        if(string.IsNullOrWhiteSpace(host.Text))throw new Exception("ゲーム用PCのIPアドレスまたは名前を入力してください。");
        config.Role=game?"game":"work";config.Host=host.Text.Trim();config.Port=(int)port.Value;Files.Save("settings.json",config);StartServer();Append("設定を保存しました。");status.Text=config.Role=="game"?controller.Status:"仕事用PC • ゲーム用PCへの返却準備";
    }
    void StartServer() {
        if(server!=null){server.Dispose();server=null;}
        if(config.Role!="game"){network.Text="接続先: "+config.Host+":"+config.Port;return;}
        if(string.IsNullOrEmpty(config.Target)){network.Text="モニター設定を保存すると、返却要求の待受を開始します。";return;}
        try {server=new Server(config.Port,config.Key,Remote);
            var ips=Dns.GetHostAddresses(Environment.MachineName).Where(ip=>ip.AddressFamily==AddressFamily.InterNetwork).Select(ip=>ip.ToString());
            network.Text="返却待受: "+string.Join(" / ",ips)+"  ポート "+config.Port;
        }catch(Exception e){network.Text="待受エラー: "+e.Message;Append(network.Text);}
    }
    string Remote(string command) {
        if(command=="status")return controller.Status;
        var completion=new TaskCompletionSource<string>();
        BeginInvoke(new Action(async delegate {
            if(busy){completion.SetException(new Exception("別の切替処理を実行中です。少し待って再試行してください。"));return;}
            busy=true;main.Enabled=false;actions.Enabled=false;status.Text="仕事用PCからの返却要求を処理中…";
            try {var result=await Task.Run(()=>controller.Restore());Append(result);status.Text=controller.Status;completion.SetResult(result);}
            catch(Exception e){Append(e.Message);status.Text="復旧できませんでした • ログを確認してください";completion.SetException(e);}
            finally {busy=false;main.Enabled=true;actions.Enabled=true;}
        }));
        return completion.Task.GetAwaiter().GetResult();
    }
    async Task RunPrimary() {bool gameSelected=role.SelectedIndex==0;await RunJob(()=>{if(gameSelected!=(config.Role=="game"))throw new Exception("PCの役割を保存してください。");if(config.Role=="game" && server==null)throw new Exception("設定を保存して返却の待受を開始してください。");return config.Role=="game"?controller.Give():Wire.Call(config,"return");});}
    async Task RunJob(Func<string> action) {
        if(busy)return;busy=true;main.Enabled=false;actions.Enabled=false;status.Text="切替処理中…（入力テストは15秒後に戻ります）";
        try {var result=await Task.Run(action);Append(result);status.Text=config.Role=="game"?controller.Status:"ゲーム用PCから応答を受信";}
        catch(Exception e){Append(e.Message);status.Text="完了できませんでした • ログを確認してください";ShowApp();}
        finally {busy=false;main.Enabled=true;actions.Enabled=true;}
    }
    void Export() {
        if(config.Role!="game" || server==null)throw new Exception("ゲーム用PCの設定を保存してください。");
        using(var dialog=new SaveFileDialog {Filter="ペアリング (*.handoff)|*.handoff",FileName="ゲーム用PC.handoff"})if(dialog.ShowDialog(this)==DialogResult.OK) {
            File.WriteAllText(dialog.FileName,Files.Json.Serialize(new Pairing {Host=config.Host,Port=config.Port,Key=config.Key}),System.Text.Encoding.UTF8);
            Append("ペアリングを書き出しました。仕事用PCで読み込んだ後、このファイルを削除してください（認証キーを含みます）。");
        }
    }
    void Import() {
        if(busy || controller.Pending)throw new Exception("先に画面構成を復旧してください。");
        using(var dialog=new OpenFileDialog {Filter="ペアリング (*.handoff)|*.handoff"})if(dialog.ShowDialog(this)==DialogResult.OK) {
            if(new FileInfo(dialog.FileName).Length>8192)throw new Exception("ペアリングファイルが不正です。");
            var p=Files.Json.Deserialize<Pairing>(File.ReadAllText(dialog.FileName));
            if(p.Format!="DisplayHandoff/1" || Convert.FromBase64String(p.Key).Length!=32 || string.IsNullOrWhiteSpace(p.Host) || p.Port<1024 || p.Port>65535)throw new Exception("ペアリングファイルが不正です。");
            config.SetKey(p.Key);config.Role="work";config.Host=p.Host;config.Port=p.Port;role.SelectedIndex=1;host.Text=p.Host;port.Value=p.Port;
            Files.Save("settings.json",config);StartServer();Append("仕事用PCとしてペアリングしました。「接続を確認」を押してください。");
        }
    }
    void Shortcut(bool startup) {
        Save();
        string folder=Environment.GetFolderPath(startup?Environment.SpecialFolder.Startup:Environment.SpecialFolder.DesktopDirectory);
        string title=startup?"Display Handoff":config.Role=="game"?"仕事用PCへ渡す":"ゲーム用PCへ返す";
        dynamic shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        dynamic link=shell.CreateShortcut(Path.Combine(folder,title+".lnk"));
        link.TargetPath=Application.ExecutablePath;link.WorkingDirectory=Application.StartupPath;link.Arguments=startup?"--tray":config.Role=="game"?"--give":"--return";link.Description=title;link.Save();
        Marshal.FinalReleaseComObject(link);Marshal.FinalReleaseComObject(shell);Append(title+" のショートカットを作成しました。");
    }
}
}
