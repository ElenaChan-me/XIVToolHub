# XIVToolHub

FINAL FANTASY XIV 向けの外部ツール（Dalamud プラグインや単体アプリ）を、GitHub のリリースからまとめてインストール・更新・管理する Windows アプリです。

A Windows app that installs, updates and manages third-party tools for FINAL FANTASY XIV (Dalamud plugins and standalone apps) from GitHub releases.

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

### 同梱カタログ / Built-in catalog

| ツール / Tool | 種類 / Kind | 取得元 / Source |
| --- | --- | --- |
| Glamaholic JP | Dalamud プラグイン | ElenaChan-me/Glamaholic-JP |
| Triggevent JP | アプリ / Application | ElenaChan-me/Triggevent-JP |

## Dalamud プラグインの扱い / Dalamud plugins

プラグインは `%LOCALAPPDATA%\XIVToolHub\tools\<ID>` に展開し、Dalamud の設定ファイル（`%APPDATA%\XIVLauncher\dalamudConfig.json`）の「開発用プラグインの場所」に DLL を登録します。

- 登録したプラグインを読み込むため、Dalamud の開発モードが無効なら有効にします。
- 設定を変更する前に、同じフォルダへ `dalamudConfig.json.xivtoolhub-日時.bak` としてバックアップします。
- ゲームの起動中は設定が上書きされるため、変更しません。
- 開発用プラグインの場所以外の設定には触れません。

Plugins are extracted to `%LOCALAPPDATA%\XIVToolHub\tools\<ID>` and their DLL is added to Dalamud's dev plugin locations.
Dev mode is enabled if needed, a backup of `dalamudConfig.json` is made before each change, and nothing is changed while the game is running.

## 安全のための制限 / Safety

- ダウンロード先は `api.github.com` のリリースアセットに限定 / Downloads are limited to GitHub release assets
- ZIP 内の展開先の外を指すパスや、異常に大きいファイルは拒否 / Zip-slip and oversized archives are rejected
- 更新は別フォルダに用意してから入れ替え、失敗時は元のバージョンを残す / Updates are staged and swapped; the previous version is kept on failure
- 起動できるのは各ツールのフォルダ内の実行ファイルのみ / Only executables inside the tool's folder can be launched
- ゲームのプロセスやファイルには一切アクセスしない（起動しているかの確認はプロセス名のみ）/ The game process and files are never accessed

## 保存先 / Data locations

| パス / Path | 内容 / Contents |
| --- | --- |
| `%LOCALAPPDATA%\XIVToolHub\tools\` | インストールしたツール / Installed tools |
| `%LOCALAPPDATA%\XIVToolHub\installed.json` | インストール記録 / Install records |
| `%APPDATA%\XIVToolHub\catalog.json` | 追加したツールの定義 / User-added tools |
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
