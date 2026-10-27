using System;
using System.Drawing;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Forms;

namespace DisplayHandoff {
public partial class MainForm {
    public static bool Preview;
    public static string PreviewScenario="ready";
    readonly TabControl pages=new TabControl();
    readonly Label statusDetail=new Label(),sharedDetail=new Label(),secondaryDetail=new Label(),nextStep=new Label(),roleBadge=new Label(),resultLabel=new Label();
    string homeError,lastResult;
    static readonly Color Ink=Color.FromArgb(28,42,65),Blue=Color.FromArgb(43,92,210),Muted=Color.FromArgb(84,101,126);
    bool Configured() {return config.AmdTarget!=null && !string.IsNullOrEmpty(config.TargetIdentity) && !string.IsNullOrEmpty(config.FallbackIdentity);}
    public static string PrimaryAction(bool game,bool pending,bool configured,bool available) {return !game?"return":pending?"restore":configured && available?"give":"settings";}
    bool MonitorsAvailable() {var list=target.Items.Cast<MonitorInfo>();return list.Any(m=>m.Identity==config.TargetIdentity && m.Active) && list.Any(m=>m.Identity==config.FallbackIdentity && m.Active);}
    static Label TextLabel(string text,int size,bool bold) {return new Label {Text=text,Font=new Font("Yu Gothic UI",size,bold?FontStyle.Bold:FontStyle.Regular),ForeColor=Ink,Dock=DockStyle.Fill,AutoSize=false,TextAlign=ContentAlignment.MiddleLeft};}
    static FlowLayoutPanel Row() {return new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=true,AutoScroll=true,Padding=new Padding(0,6,0,0)};}
    Button ActionButton(string text,Action action) {
        var button=new Button {Text=text,AutoSize=true,Height=36,Padding=new Padding(10,4,10,4),FlatStyle=FlatStyle.Flat,BackColor=Color.White,ForeColor=Ink,Margin=new Padding(0,0,10,8)};
        button.FlatAppearance.BorderColor=Color.FromArgb(207,216,230);
        button.Click+=(s,e)=>{try{action();}catch(Exception error){Append(error.Message);homeError=error.Message;RenderHome();MessageBox.Show(this,error.Message,"設定を確認してください");}};
        return button;
    }
    TableLayoutPanel Stack(params int[] heights) {
        var panel=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=heights.Length,Margin=Padding.Empty};
        foreach(int height in heights)panel.RowStyles.Add(new RowStyle(height<0?SizeType.Percent:SizeType.Absolute,height<0?100:height));return panel;
    }
    void BuildInterface() {
        Text="Display Handoff 0.3.0";Font=new Font("Yu Gothic UI",10);BackColor=Color.FromArgb(245,247,251);ForeColor=Ink;
        ClientSize=new Size(900,760);MinimumSize=new Size(880,790);StartPosition=FormStartPosition.CenterScreen;AutoScaleMode=AutoScaleMode.Dpi;
        var shell=Stack(84,-1,30);shell.Padding=new Padding(24,12,24,12);Controls.Add(shell);
        var header=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2};header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,65));header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,35));
        header.Controls.Add(TextLabel("Display Handoff",24,true),0,0);roleBadge.TextAlign=ContentAlignment.MiddleRight;roleBadge.Dock=DockStyle.Fill;roleBadge.ForeColor=Muted;header.Controls.Add(roleBadge,1,0);shell.Controls.Add(header,0,0);
        pages.Dock=DockStyle.Fill;pages.Padding=new Point(22,10);shell.Controls.Add(pages,0,1);
        var home=new TabPage("切り替え") {BackColor=BackColor,Padding=new Padding(18)};
        var setup=new TabPage("設定") {BackColor=BackColor,Padding=new Padding(18)};
        var help=new TabPage("使い方・ログ") {BackColor=BackColor,Padding=new Padding(18)};
        pages.TabPages.AddRange(new[]{home,setup,help});
        shell.Controls.Add(TextLabel("×で閉じても、右下の通知領域で動作を続けます。",9,false),0,2);

        var homeLayout=Stack(110,160,68,54,64,-1);home.Controls.Add(homeLayout);
        var stateCard=Stack(48,-1);stateCard.BackColor=Color.FromArgb(229,237,255);stateCard.Padding=new Padding(16,10,16,10);stateCard.Margin=new Padding(0,0,0,14);
        status.Dock=DockStyle.Fill;status.Font=new Font(Font.FontFamily,19,FontStyle.Bold);status.ForeColor=Ink;status.TextAlign=ContentAlignment.MiddleLeft;
        statusDetail.Dock=DockStyle.Fill;statusDetail.ForeColor=Muted;stateCard.Controls.Add(status,0,0);stateCard.Controls.Add(statusDetail,0,1);homeLayout.Controls.Add(stateCard,0,0);
        var monitors=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,Margin=new Padding(0,0,0,14)};
        monitors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,55));monitors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,45));
        monitors.Controls.Add(MonitorCard("45インチ / LG",sharedDetail),0,0);monitors.Controls.Add(MonitorCard("サブモニター",secondaryDetail),1,0);homeLayout.Controls.Add(monitors,0,1);
        main.Dock=DockStyle.Fill;main.BackColor=Blue;main.ForeColor=Color.White;main.FlatStyle=FlatStyle.Flat;main.FlatAppearance.BorderSize=0;main.Font=new Font(Font.FontFamily,16,FontStyle.Bold);main.Margin=new Padding(0,0,0,8);main.Click+=async(s,e)=>await RunPrimary();homeLayout.Controls.Add(main,0,2);
        nextStep.Dock=DockStyle.Fill;nextStep.ForeColor=Muted;nextStep.Padding=new Padding(4,8,4,0);homeLayout.Controls.Add(nextStep,0,3);
        var tools=Row();tools.Controls.Add(ActionButton("元の画面構成へ復旧",async()=>await RunJob(()=>config.Role=="game"?controller.Restore():Wire.Call(config,"return"),"ゲーム用PCの画面に戻しています…")));
        tools.Controls.Add(ActionButton("設定を開く",()=>pages.SelectedIndex=1));tools.Controls.Add(ActionButton("使い方・詳しいログ",()=>pages.SelectedIndex=2));tools.Controls.Add(ActionButton("音声設定",()=>{RefreshAudio();pages.SelectedIndex=3;}));homeLayout.Controls.Add(tools,0,4);
        resultLabel.Dock=DockStyle.Fill;resultLabel.ForeColor=Muted;resultLabel.Padding=new Padding(4,8,4,0);homeLayout.Controls.Add(resultLabel,0,5);

        var setupLayout=Stack(54,252,32,130,-1);setup.Controls.Add(setupLayout);
        setupLayout.Controls.Add(TextLabel("最初の1回だけ設定します。変更後は「設定を保存」を押してください。",11,true),0,0);
        var fields=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=6};fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,200));fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        foreach(var c in new[]{role,target,fallback,amd}){c.DropDownStyle=ComboBoxStyle.DropDownList;c.Dock=DockStyle.Fill;}
        role.Items.AddRange(new object[]{"ゲーム用PC（デスクトップ）","仕事用PC（Windowsノート）"});role.SelectedIndex=config.Role=="work"?1:0;
        host.Text=config.Host;host.Dock=DockStyle.Fill;port.Minimum=1024;port.Maximum=65535;port.Value=config.Port;port.Dock=DockStyle.Fill;
        AddSetting(fields,0,"このPCの役割",role);AddSetting(fields,1,"切り替える45インチ",target);AddSetting(fields,2,"ウィンドウの移動先",fallback);AddSetting(fields,3,"LGの制御先",amd);AddSetting(fields,4,"ゲーム用PCのIP / 名前",host);AddSetting(fields,5,"通信ポート（通常45845）",port);setupLayout.Controls.Add(fields,0,1);
        setupLayout.Controls.Add(TextLabel("PCの登録：ゲーム用PCで書き出し → 仕事用PCで読み込み",10,true),0,2);
        actions.Dock=DockStyle.Fill;actions.WrapContents=true;actions.AutoScroll=true;setupLayout.Controls.Add(actions,0,3);
        actions.Controls.Add(ActionButton("設定を保存",()=>Save()));actions.Controls.Add(ActionButton("モニター再検出",()=>Scan()));
        actions.Controls.Add(ActionButton("接続を確認",async()=>await RunJob(()=>config.Role=="work"?Wire.Call(config,"status"):"このPCの状態: "+controller.Status,"接続を確認しています…")));
        actions.Controls.Add(ActionButton("登録ファイルを書き出す",()=>Export()));actions.Controls.Add(ActionButton("登録ファイルを読み込む",()=>Import()));
        actions.Controls.Add(ActionButton("ショートカット作成",()=>Shortcut(false)));actions.Controls.Add(ActionButton("ログイン時に起動",()=>Shortcut(true)));
        actions.Controls.Add(ActionButton("入力を15秒テスト",async()=>await RunJob(()=>{if(config.Role!="game")throw new Exception("ゲーム用PCで実行してください。");return controller.TestInput();},"入力をテスト中：約15秒後に戻します…")));
        network.Dock=DockStyle.Fill;network.ForeColor=Muted;network.Padding=new Padding(0,8,0,0);setupLayout.Controls.Add(network,0,4);

        var helpLayout=Stack(252,32,-1);help.Controls.Add(helpLayout);
        helpLayout.Controls.Add(TextLabel("いつもの使い方\r\n① ゲーム用PCで「仕事用PCへ渡す」を押します。\r\n② 45インチは仕事用へ。ゲーム用PCはサブモニターで使えます。\r\n③ 仕事用PCで「ゲーム用PCへ返す」を押すと、元の画面構成へ戻します。\r\n\r\n画面が戻らないとき\r\nゲーム用PCで「元の画面構成へ復旧」。映像が出ない場合は、\r\nモニター本体でDisplayPort入力を選んでください。\r\n各アプリのウィンドウを元の位置へ並べ直す機能はありません。",11,false),0,0);
        helpLayout.Controls.Add(TextLabel("詳しい操作記録（エラーの内容はここで確認できます）",10,true),0,1);
        log.Multiline=true;log.ReadOnly=true;log.Dock=DockStyle.Fill;log.ScrollBars=ScrollBars.Vertical;log.BackColor=Color.White;log.BorderStyle=BorderStyle.FixedSingle;helpLayout.Controls.Add(log,0,2);
        BuildAudioTab();
    }
    Control MonitorCard(string title,Label description) {
        var card=Stack(44,-1);card.BackColor=Color.White;card.Padding=new Padding(16,8,16,12);card.Margin=new Padding(0,0,10,0);
        card.Controls.Add(TextLabel(title,15,true),0,0);description.Dock=DockStyle.Fill;description.ForeColor=Muted;card.Controls.Add(description,0,1);return card;
    }
    void RenderHome() {
        if(busy)return;
        bool game=config.Role=="game",pending=controller.Pending;
        roleBadge.Text=game?"このPC：ゲーム用PC":"このPC：仕事用PC";
        var monitors=target.Items.Cast<MonitorInfo>().ToList();
        var shared=monitors.FirstOrDefault(m=>m.Identity==config.TargetIdentity);
        var sub=monitors.FirstOrDefault(m=>m.Identity==config.FallbackIdentity);
        string location=sub==null?"設定で移動先を選択":sub.Bounds.X<0?"左側":sub.Bounds.X>0?"右側":"移動先";
        sharedDetail.Text=game?(pending?"ゲーム用PCの出力を再開できます\r\n保存した画面構成あり":shared!=null?"ゲーム用PCの出力：有効\r\nDisplayPort / "+shared.Bounds.Width+" × "+shared.Bounds.Height:"「設定」でLGモニターを選択"):"仕事用PCはUSB-Cで接続\r\n返却はネットワーク経由";
        secondaryDetail.Text=game?(sub==null?location:location+" / "+sub.Bounds.Width+" × "+sub.Bounds.Height+"\r\n45インチ上のウィンドウの移動先"):"ゲーム用PCで引き続き使用\r\nノート側での設定は不要";
        main.Text=!game?"ゲーム用PCへ返す  →":pending?"ゲーム用PCに戻す  →":Configured()?"仕事用PCへ渡す  →":"最初の設定をする  →";
        status.Text=!game?"ゲーム用PCへ返せます":pending?(controller.Status.Contains("未完了")?"前の操作を完了できていません":"元の画面構成へ戻せます"):Configured()?"ゲーム用PCの画面構成":"まずは2台のPCを登録しましょう";
        statusDetail.Text=!game?"ゲーム用PCでアプリを起動しておいてください。":pending?"下のボタンで45インチの出力と元の配置を復元します。":Configured()?"45インチを仕事用PCに渡しても、サブモニターは使い続けられます。":"「設定」で45インチとウィンドウの移動先を選びます。";
        nextStep.Text=!game?"押すと：ゲーム用PCに連絡 → 元の画面構成へ復元 → DisplayPortへ切替要求":pending?"押すと：保存した画面構成へ復元 → DisplayPortへ切替要求":"押すと：ウィンドウをサブへ移動 → USB-Cへ切替要求 → 45インチの出力を停止";
        status.ForeColor=Ink;
        if(game && !pending && Configured() && !MonitorsAvailable()){status.Text="モニターの接続を確認してください";statusDetail.Text="「設定」でモニターを再検出し、切替先と移動先を確認してください。";main.Text="設定を確認する  →";}
        resultLabel.Text=AudioSummary()+"\r\n"+(string.IsNullOrEmpty(lastResult)?"映像が切り替わったかは、実際の画面で確認してください。":lastResult);
        if(Preview && PreviewScenario=="error")homeError="ゲーム用PCに接続できません。アプリが起動しているか確認してください。";
        if(!string.IsNullOrEmpty(homeError)){status.Text="操作を完了できませんでした";status.ForeColor=Color.FromArgb(165,63,33);statusDetail.Text=!game?"ゲーム用PCの起動と、同じネットワークへの接続を確認してください。":pending?"画面が戻らない場合は「元の画面構成へ復旧」を押してください。":"「設定」でモニターと接続先を確認してください。";resultLabel.Text=homeError+"\r\n詳しくは「使い方・ログ」で確認できます。";}
    }
    static List<AdlTarget> ReadAmd() {using(var amd=new Amd())return amd.List();}
    static List<MonitorInfo> DemoMonitors() {
        var lg=new MonitorInfo {Device="DISPLAY1",Identity="demo-lg",Name="LG ULTRAGEAR+",Primary=true,Mode=new byte[220],Active=PreviewScenario!="loan" && PreviewScenario!="recovery"};
        var sub=new MonitorInfo {Device="DISPLAY2",Identity="demo-sub",Name="サブモニター",Mode=new byte[220]};
        Displays.Put(lg.Mode,172,5120);Displays.Put(lg.Mode,176,2160);Displays.Put(sub.Mode,172,2560);Displays.Put(sub.Mode,176,1440);Displays.Put(sub.Mode,76,5120);return new List<MonitorInfo>{lg,sub};
    }
    public static void PreparePreview() {
        var c=new Settings {Role=PreviewScenario=="work"?"work":"game",Host="GAME-PC",Target="DISPLAY1",TargetIdentity="demo-lg",Fallback="DISPLAY2",FallbackIdentity="demo-sub",AudioFollowMonitor=true,HandoffAudioId="demo-audio-sub",AmdTarget=new AdlTarget {Name="LG ULTRAGEAR+",Device="DISPLAY1",Identity="demo-lg"}};c.SetKey(Wire.Random());
        if(PreviewScenario=="setup"){c.TargetIdentity=null;c.FallbackIdentity=null;c.AmdTarget=null;}
        Files.Save("settings.json",c);
        if(PreviewScenario=="loan" || PreviewScenario=="recovery")Files.Save("restore.json",new SavedState {Stage=PreviewScenario=="loan"?"work":"preparing",Monitors=DemoMonitors(),Target=c.AmdTarget});
    }
}
}
