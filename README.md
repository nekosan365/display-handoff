# Display Handoff

A Windows utility for sharing an LG 45GX950A between an AMD desktop (DisplayPort) and a Windows laptop (USB-C). Includes a Japanese UI and one-click shortcuts.

**Preview software:** automated tests and read-only display enumeration have passed. Physical switching, real window migration, and communication between two PCs have not yet been validated with this application.

## Features

- **Hand to work PC:** move ordinary visible windows to a selected secondary display, request USB-C input, then disable the desktop DisplayPort display path.
- **Return to gaming PC:** the laptop sends an authenticated LAN request; the desktop restores its saved display configuration and requests DisplayPort input.
- Keep secondary displays enabled and close the desktop gap left by the shared display.
- Persist recovery data before changes and attempt rollback on failure.
- Input-only test with a DisplayPort return request after approximately 15 seconds.

Monitor control runs on the AMD desktop; the laptop does not need an AMD GPU. The desktop must remain on, logged in, and running the tray application.

## Requirements and download

Windows 10/11 x64, .NET Framework 4.8, an AMD desktop graphics driver providing `atiadlxx.dll`, an LG 45GX950A connected over DP/USB-C, at least one secondary desktop display, and LAN connectivity on TCP 45845 (configurable).

Download the ZIP from [Releases](https://github.com/nekosan365/display-handoff/releases), extract it into a fixed folder on each PC, and run `DisplayHandoff.exe`. No SDK or Python is needed to run it. The executable is unsigned. Other monitors and non-AMD desktop GPUs are not supported.

## セットアップ

### ゲーム用PC

1. アプリを起動し、「このPC」を「ゲーム用PC」にします。
2. 「45インチの画面」と「LGの入力切替先」が同じモニターを指していることを確認します。
3. 「27インチの退避先」を選びます。45インチの左側にある画面を初期選択しますが、別の画面も選べます。
4. 「ゲーム用PCのIP / 名前」に、ノートからアクセスできるデスクトップのIPアドレスまたはホスト名を入力し、「設定を保存」を押します。
5. 「ペアリングを書出し」で `.handoff` ファイルを作成し、ノートへ転送します。
6. 「ショートカット作成」で「仕事用PCへ渡す」を作成します。

×ボタンではタスクトレイに移ります。「ログイン時に起動」は任意です。解除する場合はWindowsの `shell:startup` フォルダーから `Display Handoff.lnk` を削除します。EXEの場所を変えたらショートカットを作り直してください。

### 仕事用PC

1. アプリを起動し、「ペアリングを読込」で転送したファイルを開きます。仕事用PCとして自動設定されます。
2. 「接続を確認」でゲーム用PCの状態が表示されることを確認します。
3. 「ショートカット作成」で「ゲーム用PCへ返す」を作成します。
4. ペアリングファイルは認証キーを含むため、読み込み後に転送用ファイルを削除します。リポジトリやIssueには添付しないでください。

### ファイアウォール

接続できない場合はゲーム用PCの常駐アプリ、LAN接続、VPNやネットワーク分離を確認します。受信が遮断される場合はゲーム用PCの管理者PowerShellでアプリフォルダーへ移動し、実際の仕事用PCのIPv4アドレスを指定します。

```powershell
.\Allow-WorkPC.ps1 -WorkPcIPv4 '<WORK_PC_IPV4>'
```

指定した接続元IP・このEXE・設定ポートだけを許可します。ネットワーク種別は限定しません。スクリプトは自動実行されません。IPやEXEの場所を変更した場合は再実行してください。

## 初回の実機テスト

1. ノートをUSB-Cで45GX950Aに接続し起動します。モニターにDDC/CI設定があれば有効にします。
2. ノートで「接続を確認」を成功させます。
3. ゲーム用PCで「入力を15秒テスト」を実行し、ノートの画面が映ってからDisplayPortへ戻ることを目視します。Windowsの出力は無効にしません。
4. 45インチにメモ帳などを置き、「仕事用PCへ渡す」でウィンドウ退避、45インチの非アクティブ化、残る画面の動作を確認します。
5. ノートで「ゲーム用PCへ返す」を押し、DisplayPortと元の画面配置へ戻ることを確認します。

「切替要求を送信」はAMD APIが要求を受理したことを意味します。現在のモニター入力を読み取る機能はありません。15秒テストもアプリやOSが停止すると自動復帰できません。

## Recovery and limitations

- Use **元の画面構成へ復旧** on the desktop if LAN access fails. Recovery data survives an app restart and is retained when restoration fails.
- If needed, use the monitor's physical controls to select DisplayPort and Windows Display Settings to re-enable extended display mode.
- Window migration is best effort. Minimized, elevated, or exclusive-fullscreen windows may not move. Exact per-application window positions are not restored on return.
- Secondary displays are arranged in a horizontal row during handoff; their saved modes and positions are restored on return.
- Driver updates and cable changes can invalidate display identities. Recover through Windows and reconfigure the app if identities change.
- No wake-on-LAN, Windows service, ARM Windows, or DisplayLink support.

## Data and protocol

Settings, logs, and recovery data live in `%LOCALAPPDATA%\DisplayHandoff`. The key is protected locally using current-user Windows DPAPI. Exported pairing files contain the key in plaintext for transfer.

The TCP protocol uses a fresh server challenge and HMAC-SHA256 authentication for requests and responses. Only status and return operations are accepted remotely; no shell commands are accepted. Traffic is authenticated but not encrypted. Use a trusted LAN and a restricted firewall rule. One connection is served at a time; this is not an Internet-facing service.

## Build and test

Run in Windows PowerShell:

```powershell
.\build.ps1
$test = Start-Process .\DisplayHandoff.exe -ArgumentList '--self-test test-results.txt' -Wait -PassThru
Get-Content .\test-results.txt
if ($test.ExitCode -ne 0) { throw 'Tests failed' }
```

Tests cover transitions, duplicate requests, rollback, persisted recovery, display layout planning, packet checksums, DPAPI storage, authenticated loopback, and replay rejection. They do not change physical displays. GitHub Actions builds and tests on Windows.

## License and references

MIT. See [LICENSE](LICENSE) and [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt). No AMD SDK headers or driver binaries are redistributed. This is not an official LG or AMD product.

- [LGInputSwitch](https://github.com/phillip9933/LGInputSwitch): reference AMD/LG input-switching implementation.
- [ChangeDisplaySettingsExW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-changedisplaysettingsexw) and [DEVMODEW](https://learn.microsoft.com/en-us/windows/win32/api/wingdi/ns-wingdi-devmodew): Windows display APIs.
