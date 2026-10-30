# XIVToolHub

FINAL FANTASY XIV 向けの外部ツール（ACT・Triggevent Plus・Dalamud プラグインなど）を、インストールから環境構築・更新までまとめて行う Windows アプリです。

A Windows app that installs, sets up and updates third-party tools for FINAL FANTASY XIV (ACT, Triggevent Plus, Dalamud plugins and more) in one place.

## 注意 / Warning

FINAL FANTASY XIV の利用規約では外部ツールの使用が禁止されており、使用が確認された場合はアカウントの利用停止などの対象になります。利用は自己責任で行ってください。

Third-party tools are prohibited by the FINAL FANTASY XIV Terms of Service and may result in account suspension. Use at your own risk.

## インストール / Install

1. [Releases](https://github.com/ElenaChan-me/XIVToolHub/releases/latest) から `XIVToolHub-Setup.exe` をダウンロードして実行します。管理者の権限は要りません（`%LOCALAPPDATA%\Programs\XIVToolHub` に入ります）。
2. インストールが終わると XIVToolHub が起動します。使うツールにチェックを入れて「選択したツールをインストール・更新」を押してください。
3. 必要なツールや前提のソフトは、選んだときとインストールのときに確認してから一緒に入れます。

Download `XIVToolHub-Setup.exe` from the latest release and run it (no admin rights needed). After setup, tick the tools you want and press "Install / Update". Required tools and prerequisites are confirmed and installed together.

Windows 10（1809 以降）/ Windows 11 の 64 ビット版で動きます。.NET のランタイムは同梱しています。
Windows 10 (1809+) / 11, 64-bit. The .NET runtime is bundled.

SmartScreen の「Windows によって PC が保護されました」が出た場合は、「詳細情報」→「実行」で起動できます。
If SmartScreen warns about the installer, choose "More info" → "Run anyway".

## 入れられるツール / Tools

| ツール / Tool | 種類 / Kind | 取得元 / Source | 必要なツール / Requires | 前提のソフト / Prerequisites |
| --- | --- | --- | --- | --- |
| [Glamaholic Plus](https://github.com/ElenaChan-me/Glamaholic-Plus) | Dalamud プラグイン | ElenaChan-me/Glamaholic-Plus | - | XIVLauncher |
| ACT（Advanced Combat Tracker） | アプリ / App | EQAditu/AdvancedCombatTracker | - | .NET Framework 4.8 |
| FFXIV_ACT_Plugin | ACT プラグイン | ravahn/FFXIV_ACT_Plugin | ACT | - |
| OverlayPlugin | ACT プラグイン | OverlayPlugin/OverlayPlugin | ACT, FFXIV_ACT_Plugin | VC++ ランタイム |
| [ACT Localization Plus](https://github.com/ElenaChan-me/ACT-Localization-Plus) | ACT プラグイン | ElenaChan-me/ACT-Localization-Plus | ACT | - |
| [Triggevent Plus](https://github.com/ElenaChan-me/Triggevent-Plus) | アプリ / App | ElenaChan-me/Triggevent-Plus | ACT, FFXIV_ACT_Plugin, OverlayPlugin | - |
| Triggevent Plus ボイスパック（みあ・つきの） | ボイスパック | ElenaChan-me/Triggevent-VoiceGen | Triggevent Plus | - |

「ツールを追加」から、GitHub のリリースで配布されているほかのツールもカタログに追加できます。
Other tools released on GitHub can be added to the catalog with "Add tool".

## 環境構築の流れ / How setup works

- **必要なツール**: ツールにチェックを入れたとき、まだ入っていない必要なツールがあれば「同時にインストールしますか？」と確認します。断った場合は、そのツールだけでは動かないため選択を外します。
  When a tool needs others that are not installed, you are asked whether to install them too.
- **一緒に使うツール**: ACT を選ぶと、FFXIV_ACT_Plugin・OverlayPlugin・ACT Localization Plus も入れるか確認します（入れなくても ACT は動きます）。
  Selecting ACT offers its usual plugins.
- **前提のソフト**: VC++ ランタイム・.NET Framework 4.8・XIVLauncher など Windows に入れるソフトは、インストールのときに入っているか調べ、無ければ確認してから公式の配布元（Microsoft・XIVLauncher の GitHub）から取得して入れます。Microsoft の配布物は署名を確かめてから実行します。
  Prerequisites are detected at install time and, after confirmation, installed from their official sources (signatures are verified for Microsoft installers).
- **ACT の設定**: ACT はポータブル版として入れ、プラグインを ACT の設定に登録します。OverlayPlugin の WebSocket サーバー（127.0.0.1:10501）は、ACT を起動すると自動で始まるように設定します。
  ACT is installed portable, plugins are registered and the OverlayPlugin WebSocket server is set to start automatically.
- **Dalamud の設定**: Dalamud プラグインは XIVLauncher の「開発用プラグインの場所」に登録し、必要なら開発モードを有効にします。XIVLauncher で一度ゲームを起動してからインストールしてください。
  Dalamud plugins are registered as dev plugins; launch the game once with XIVLauncher first.
- **進み具合**: インストール・更新の間は、画面の下に全体の進み具合を表示します。処理中でも、入れ替えていないツールの起動などはできます。
  Overall progress is shown at the bottom; other tools stay usable meanwhile.

## できること / Features

- まとめてインストール・更新・アンインストール、更新のあるツールの「すべて更新」（ACT・Triggevent Plus・ゲームの起動中は終了を待ってから更新）
  Install / update / uninstall in bulk; "Update all" waits for running apps to close
- XIVToolHub 自身の更新（起動時に確認）
  Self-update on startup
- パッチの対応状況の表示（ゲームのバージョンと、Dalamud・FFXIV_ACT_Plugin の対応を比べる）
  Patch status for Dalamud and FFXIV_ACT_Plugin
- 「ACT と Triggevent をまとめて起動」（ACT を起動し、WebSocket が使えるようになってから Triggevent Plus を起動）
  One-click launch of ACT and Triggevent Plus
- 表示言語（日本語・English・한국어・简体中文）とテーマ（黒・赤 / 白・水色）をツールにまとめて反映。日本語では 30 種類の追加フォントから選べます
  Shared display language, theme and Japanese fonts for the localized tools
- Triggevent Plus のボイスパックのインストール
  Voice packs for Triggevent Plus

## 安全のための制限 / Safety

- ツールは GitHub のリリースアセットから、前提のソフトは公式の配布元から、フォントはカタログに載せた公式の URL からだけ取得します / Downloads only from GitHub releases, official prerequisite sources and the font catalog
- ZIP の展開先の外を指すパスや、異常に大きいファイルは拒否します / Zip-slip and oversized archives are rejected
- 更新は別の場所に用意してから入れ替え、失敗したら元のバージョンを残します / Updates are staged and rolled back on failure
- 設定ファイル（ACT・OverlayPlugin・Dalamud）は変更する前にバックアップし、ACT やゲームの起動中は変更しません / Config files are backed up and never edited while ACT or the game is running
- ゲームのプロセスやファイルには触れません（起動しているかはプロセス名だけで判断します） / The game itself is never touched

## 保存先 / Data locations

| パス / Path | 内容 / Contents |
| --- | --- |
| `%LOCALAPPDATA%\Programs\XIVToolHub\` | XIVToolHub 本体 / The app |
| `%LOCALAPPDATA%\XIVToolHub\tools\` | インストールしたツール / Installed tools |
| `%LOCALAPPDATA%\XIVToolHub\installed.json` | インストール記録・表示言語・フォント / Install records |
| `%LOCALAPPDATA%\XIVToolHub\fonts\` | 追加フォント / Fonts |
| `%APPDATA%\XIVToolHub\` | 追加したツールの定義・テーマ / User catalog and theme |
| 資格情報マネージャー `XIVToolHub:github-token` | GitHub トークン（任意） / Optional GitHub token |

アンインストールは Windows の「アプリと機能」から行えます。インストールしたツールと設定（`%LOCALAPPDATA%\XIVToolHub`）は残るので、不要なら削除してください。
Uninstall from "Apps & features". Installed tools and settings are kept in `%LOCALAPPDATA%\XIVToolHub`.

GitHub トークンは無くても使えます。短い時間に何度も確認して GitHub の利用制限（1時間に60回）に当たる場合に登録してください。
A GitHub token is optional; register one if you hit GitHub's rate limit.

## 開発 / Development

```powershell
dotnet test XIVToolHub.sln
dotnet run --project src/XIVToolHub.App
# 配布用の exe とインストーラー（Inno Setup 6 が必要）
./scripts/publish.ps1 -Tag v0.8.0
./scripts/build-installer.ps1 -Version v0.8.0
```

| パス / Path | 内容 / Contents |
| --- | --- |
| `src/XIVToolHub.Core` | カタログ・GitHub・展開・設定ファイルの編集・前提のソフト・インストール / Core logic |
| `src/XIVToolHub.App` | WPF アプリ / WPF app |
| `tests/XIVToolHub.Tests` | xUnit テスト（通信は偽の応答で再現） / Tests with fake responses |
| `installer/XIVToolHub.iss` | インストーラーの定義 / Installer script |
| `release-notes/` | リリースの説明 / Release notes |

`v*` のタグを push すると、GitHub Actions が exe とインストーラーを作ってリリースします。ブランチは Git-flow（`main` / `develop` / `feature/*`）です。
Pushing a `v*` tag builds and releases the exe and installer. Branching follows Git-flow.
