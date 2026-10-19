# XIVToolHub

FINAL FANTASY XIV 向けの外部ツール（Dalamud プラグインや単体アプリ）を、GitHub のリリースからまとめてインストール・更新・管理する Windows アプリです。

A Windows app that installs, updates and manages third-party tools for FINAL FANTASY XIV (Dalamud plugins and standalone apps) from GitHub releases.

> Glamaholic Plus と Triggevent Plus は、2026-10 に Glamaholic JP・Triggevent JP から改名しました。インストール済みのものはそのまま更新できます。
> Glamaholic Plus and Triggevent Plus were renamed from Glamaholic JP and Triggevent JP in October 2026. Existing installs keep updating.

## 注意 / Warning

FINAL FANTASY XIV の利用規約では外部ツールの使用が禁止されており、使用が確認された場合はアカウントの利用停止などの対象になります。利用は自己責任で行ってください。

Third-party tools are prohibited by the FINAL FANTASY XIV Terms of Service and may result in account suspension. Use at your own risk.

## できること / Features

- カタログに載っているツールを個別に、または複数選択してまとめてインストール・更新
  Install or update tools individually or several at once
- 最新リリースとの比較による更新の確認
  Check for updates against the latest release
- アンインストール、インストール先フォルダ・GitHub ページを開く、アプリの起動
  Uninstall, open the install folder or GitHub page, launch applications
- GitHub リポジトリを指定して新しいツールをカタログに追加
  Add new tools to the catalog by GitHub repository
- Private リポジトリ用の GitHub トークンを Windows の資格情報マネージャーに保存
  Store a GitHub token for private repositories in Windows Credential Manager
- Glamaholic Plus・Triggevent Plus の表示言語（日本語・英語・韓国語・中国語）をインストール時に選択し、日本語の場合は後から追加フォントを選択
  Choose the display language (Japanese, English, Korean, Chinese) of the localized tools at install time, and an additional font for Japanese later

### 同梱カタログ / Built-in catalog

| ツール / Tool | 種類 / Kind | 取得元 / Source | 必要なツール / Requires |
| --- | --- | --- | --- |
| Glamaholic Plus | Dalamud プラグイン | ElenaChan-me/Glamaholic-Plus | - |
| ACT（Advanced Combat Tracker） | アプリ / Application | EQAditu/AdvancedCombatTracker | - |
| FFXIV_ACT_Plugin | ACT プラグイン | ravahn/FFXIV_ACT_Plugin | ACT |
| OverlayPlugin | ACT プラグイン | OverlayPlugin/OverlayPlugin | ACT |
| Triggevent Plus | アプリ / Application | ElenaChan-me/Triggevent-Plus | ACT, FFXIV_ACT_Plugin, OverlayPlugin |

ツールを選ぶと、必要なツールも自動で選択され、必要とされる側から順にインストールされます。
OverlayPlugin は、更新が止まっている ngld 版ではなく、後継の OverlayPlugin/OverlayPlugin を使います。

Selecting a tool also selects the tools it requires, and they are installed in dependency order.
OverlayPlugin uses the maintained successor (OverlayPlugin/OverlayPlugin) rather than the inactive ngld version.

## ACT とプラグイン / ACT and its plugins

ACT は `%LOCALAPPDATA%\XIVToolHub\tools\act` にポータブル版としてインストールし、`-portable` で起動します。設定は同じフォルダの `Config\Advanced Combat Tracker.config.xml` に保存され、既存の ACT（`%APPDATA%\Advanced Combat Tracker`）には触れません。

- ACT プラグインは展開後、上記の設定ファイルの `<ActPlugins>` に登録します。
- ACT を一度も起動していない場合は、最小限の設定ファイルを作成します。ACT はこの状態でもプラグインを読み込みます。
- 変更前には `Advanced Combat Tracker.config.xml.xivtoolhub-日時.bak` としてバックアップします。
- ACT の起動中はプラグインのインストール・更新・削除を行いません。

Triggevent を使うには、ACT の「Plugins」タブ → OverlayPlugin の設定で WebSocket サーバーを開始してください。

ACT is installed as a portable copy under `%LOCALAPPDATA%\XIVToolHub\tools\act` and started with `-portable`; an existing ACT installation is not touched.
ACT plugins are registered in the portable ACT configuration (with a backup), and nothing is changed while ACT is running.
For Triggevent, start the OverlayPlugin WebSocket server in ACT.

## 表示言語とフォント / Display language and fonts

Glamaholic Plus と Triggevent Plus は、初めてインストールするときに表示言語（日本語・English・한국어・简体中文）を選びます。
インストール後は、ツールの「表示設定」から言語を変更でき、日本語の場合は 30 種類の追加フォントから選べます。

- 選んだ設定は、ツールの起動ファイル・DLL と同じフォルダの `xivtoolhub-settings.json`（`{"language": "ja", "fontPath": "...", "theme": "dark"}`）に書き込み、ツールは起動時に読み込みます。更新しても設定は引き継ぎます。
- フォントは同梱していません。公式に直接ダウンロードできるもの（源暎ぽっぷる、ふんわりラウンド、ポプらむ☆キュート、はちまるポップ、モッチーポップ）は配布元から取得し、それ以外は公式ページで入手したファイル（.ttf / .otf / .ttc または ZIP）を取り込みます。
- 取得したフォントは `%LOCALAPPDATA%\XIVToolHub\fonts\<ID>` に置きます。ファイルの先頭のバイトでフォントであることを確認してから取り込みます。
- 収録文字が少ないフォント（かなのみ等）でも、足りない文字は標準フォントで表示します。
- 各フォントの利用規約は配布元で確認してください。さなフォン丸には「アプリへの使用禁止」の記載があるため、画面に注意を表示します。

Glamaholic Plus and Triggevent Plus ask for a display language on first install. Afterwards, the language can be changed from the tool's settings, and for Japanese one of 30 additional fonts can be selected.
The choice is written to `xivtoolhub-settings.json` next to the tool's entry file and kept across updates.
Fonts are not bundled: those with an official direct download are fetched from the author's site, the others are imported from a file downloaded from the official page. Missing glyphs fall back to the default font. Please check each font's license on its official page.

## テーマ / Themes

画面下部の「テーマ」で、配色を「黒・赤」と「白・水色」から選べます。選んだテーマは XIVToolHub にすぐ反映され、Glamaholic Plus と Triggevent Plus にも `xivtoolhub-settings.json` の `theme`（`dark` / `light`）で伝わり、次回の起動時から同じ配色で表示されます。
ハブから起動した Triggevent Plus では、テーマを XIVToolHub でまとめて管理するため、Triggevent 側のテーマ選択は表示しません。

Choose "Black / Red" or "White / Light Blue" at the bottom of the window. The theme is applied to XIVToolHub immediately and passed to Glamaholic Plus and Triggevent Plus through the `theme` field of `xivtoolhub-settings.json`, so all tools share the same colors from their next start.

## Dalamud プラグインの扱い / Dalamud plugins

プラグインは `%LOCALAPPDATA%\XIVToolHub\tools\<ID>` に展開し、Dalamud の設定ファイル（`%APPDATA%\XIVLauncher\dalamudConfig.json`）の「開発用プラグインの場所」に DLL を登録します。

- 登録したプラグインを読み込むため、Dalamud の開発モードが無効なら有効にします。
- 設定を変更する前に、同じフォルダへ `dalamudConfig.json.xivtoolhub-日時.bak` としてバックアップします。
- ゲームの起動中は設定が上書きされるため、変更しません。
- 開発用プラグインの場所以外の設定には触れません。

Plugins are extracted to `%LOCALAPPDATA%\XIVToolHub\tools\<ID>` and their DLL is added to Dalamud's dev plugin locations.
Dev mode is enabled if needed, a backup of `dalamudConfig.json` is made before each change, and nothing is changed while the game is running.

## 安全のための制限 / Safety

- ツールのダウンロード先は `api.github.com` のリリースアセットに限定し、フォントはカタログに載せた公式の HTTPS の URL だけから取得 / Tools are downloaded only from GitHub release assets, fonts only from the official HTTPS URLs in the font catalog
- ZIP 内の展開先の外を指すパスや、異常に大きいファイルは拒否 / Zip-slip and oversized archives are rejected
- 更新は別フォルダに用意してから入れ替え、失敗時は元のバージョンを残す / Updates are staged and swapped; the previous version is kept on failure
- 起動できるのは各ツールのフォルダ内の実行ファイルのみ / Only executables inside the tool's folder can be launched
- ゲームのプロセスやファイルには一切アクセスしない（起動しているかの確認はプロセス名のみ）/ The game process and files are never accessed

## 保存先 / Data locations

| パス / Path | 内容 / Contents |
| --- | --- |
| `%LOCALAPPDATA%\XIVToolHub\tools\` | インストールしたツール / Installed tools |
| `%LOCALAPPDATA%\XIVToolHub\installed.json` | インストール記録・表示言語・フォント / Install records, language and font |
| `%LOCALAPPDATA%\XIVToolHub\fonts\` | 取得した追加フォント / Downloaded or imported fonts |
| `%APPDATA%\XIVToolHub\catalog.json` | 追加したツールの定義 / User-added tools |
| `%APPDATA%\XIVToolHub\settings.json` | テーマ / Theme |
| 資格情報マネージャー `XIVToolHub:github-token` | GitHub トークン / GitHub token |

## 開発 / Development

```powershell
dotnet build XIVToolHub.sln
dotnet test XIVToolHub.sln
dotnet run --project src/XIVToolHub.App
```

| パス / Path | 内容 / Contents |
| --- | --- |
| `src/XIVToolHub.Core` | カタログ、GitHub 取得、展開、Dalamud 設定編集、インストール処理 / Core logic |
| `src/XIVToolHub.App` | WPF アプリ / WPF app |
| `tests/XIVToolHub.Tests` | xUnit テスト（GitHub 通信は偽の応答で再現） / Tests with a fake GitHub |

ブランチは Git-flow（`main` / `develop` / `feature/*`）です。 / Branching follows Git-flow.
