using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Collections.Generic;

namespace DisplayHandoff {
public partial class MainForm {
    readonly CheckBox audioFollow=new CheckBox(),audioCalls=new CheckBox();
    readonly ComboBox audioOutput=new ComboBox();
    readonly Label audioCurrent=new Label(),audioHint=new Label();
    string currentAudioName="未確認";
    void BuildAudioTab() {
        var tab=new TabPage("音声") {BackColor=BackColor,Padding=new Padding(18)};pages.TabPages.Add(tab);
        var layout=Stack(56,76,46,40,42,46,72,-1);tab.Controls.Add(layout);
        layout.Controls.Add(TextLabel("画面と一緒に、ゲーム用PCの音声も切り替えます。",13,true),0,0);
        audioCurrent.Dock=DockStyle.Fill;audioCurrent.Padding=new Padding(12);audioCurrent.BackColor=Color.FromArgb(229,237,255);layout.Controls.Add(audioCurrent,0,1);
        audioFollow.Text="画面切替に通常の音声出力を連動する";audioFollow.Dock=DockStyle.Fill;audioFollow.Checked=config.AudioFollowMonitor;layout.Controls.Add(audioFollow,0,2);
        layout.Controls.Add(TextLabel("45インチを仕事用PCに渡している間の、ゲーム用PCの音声出力",10,true),0,3);
        audioOutput.DropDownStyle=ComboBoxStyle.DropDownList;audioOutput.Dock=DockStyle.Fill;layout.Controls.Add(audioOutput,0,4);
        audioCalls.Text="通話用の音声も同じ出力へ切り替える（通常はオフ）";audioCalls.Dock=DockStyle.Fill;audioCalls.Checked=config.AudioIncludeCommunications;layout.Controls.Add(audioCalls,0,5);
        var buttons=Row();buttons.Controls.Add(ActionButton("音声設定を保存",()=>SaveAudio()));buttons.Controls.Add(ActionButton("出力先を再確認",()=>RefreshAudio()));layout.Controls.Add(buttons,0,6);
        audioHint.Dock=DockStyle.Fill;audioHint.ForeColor=Muted;audioHint.Text="返すときは、切替前の音声出力へ自動的に戻します。\r\n設定を保存しただけでは、現在の出力は変わりません。\r\n\r\n対象はゲーム用PCのWindows既定の出力です。\r\nアプリ内で出力先を固定しているゲームや通話アプリは、\r\nそのアプリ側で「既定のデバイス」を選んでください。\r\n仕事用PC自身の音声出力や、マイク入力・音量は変更しません。";layout.Controls.Add(audioHint,0,7);
        RefreshAudio();
    }
    string AudioName(List<AudioOutput> devices,string id) {var device=devices.FirstOrDefault(d=>d.Id==id);return device==null?"未接続 / 未設定":device.Name;}
    string AudioBrief(string name) {return name.Contains("LG ULTRAGEAR")?"45インチ（LG）":name.Contains("MF27X3AQS")?"27インチ（MF27X3AQS）":name;}
    void RefreshAudio() {
        audioOutput.Items.Clear();
        try {
            var devices=Preview?new List<AudioOutput>{new AudioOutput {Id="demo-audio-lg",Name="45インチ（LG）"},new AudioOutput {Id="demo-audio-sub",Name="27インチ（MF27X3AQS）"}}:new WindowsAudio().List();
            var defaults=Preview?new AudioDefaults {Console="demo-audio-lg",Multimedia="demo-audio-lg",Communications="demo-audio-lg"}:new WindowsAudio().Capture(true);
            foreach(var device in devices)audioOutput.Items.Add(device);
            var selected=devices.FirstOrDefault(d=>d.Id==config.HandoffAudioId);
            if(selected!=null)audioOutput.SelectedItem=selected;
            else if(!string.IsNullOrEmpty(config.HandoffAudioId)){var missing=new AudioOutput {Id=config.HandoffAudioId,Name="保存した出力先（現在は未接続）"};audioOutput.Items.Add(missing);audioOutput.SelectedItem=missing;}
            currentAudioName=AudioBrief(AudioName(devices,defaults.Multimedia));
            audioCurrent.Text="このPCの通常音声："+AudioName(devices,defaults.Multimedia)+"\r\n通話用："+AudioName(devices,defaults.Communications);
        }catch(Exception error){audioCurrent.Text="音声出力を確認できません："+error.Message;currentAudioName="未確認";}
        bool game=config.Role=="game";audioFollow.Enabled=audioOutput.Enabled=audioCalls.Enabled=game;
        if(game)audioHint.Text="返すときは、切替前の音声出力へ自動的に戻します。\r\n設定を保存しただけでは、現在の出力は変わりません。\r\n\r\n対象はゲーム用PCのWindows既定の出力です。\r\nアプリ内で出力先を固定している場合は「既定のデバイス」を選んでください。\r\n仕事用PC自身の音声出力や、マイク入力・音量は変更しません。";
        if(!game)audioHint.Text="音声連動は、ゲーム用PCの「音声」タブで設定してください。\r\n仕事用PCから「ゲーム用PCへ返す」を押すと、\r\nゲーム用PCの画面と音声が一緒に復元されます。\r\n仕事用PC自身の音声出力はWindowsの設定で選んでください。";
    }
    void SaveAudio() {
        if(config.Role!="game")throw new Exception("ゲーム用PCで音声連動を設定してください。");
        if(busy || controller.Pending)throw new Exception("切替中・貸出中は設定を変更できません。先に画面と音声を戻してください。");
        var selected=audioOutput.SelectedItem as AudioOutput;
        if(audioFollow.Checked) {
            if(selected==null)throw new Exception("貸出中の音声出力先を選択してください。");
            if(!Preview)new WindowsAudio().Validate(selected.Id);
        }
        config.AudioFollowMonitor=audioFollow.Checked;config.AudioIncludeCommunications=audioCalls.Checked;config.HandoffAudioId=selected==null?null:selected.Id;
        Files.Save("settings.json",config);lastResult="音声設定を保存しました。次の画面切替から反映します。";homeError=null;RefreshAudio();RenderHome();pages.SelectedIndex=0;
    }
    string AudioSummary() {
        if(config.Role!="game")return "音声連動はゲーム用PC側の設定に従います。";
        var endpoint=audioOutput.Items.Cast<AudioOutput>().FirstOrDefault(d=>d.Id==config.HandoffAudioId);
        return "通常音声："+currentAudioName+" / "+(config.AudioFollowMonitor?"貸出中 → "+AudioBrief(endpoint==null?"出力先を確認":endpoint.Name)+"、返却時 → 切替前の出力":"連動オフ（Windowsの設定に従います）");
    }
}
}
