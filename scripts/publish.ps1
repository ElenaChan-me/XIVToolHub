# 配布用の XIVToolHub.exe を publish フォルダに作成する。
# .NET ランタイムを同梱した単一ファイル形式にし、利用者側でのインストールを不要にする。
param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root "publish\$Runtime"

dotnet test (Join-Path $root "XIVToolHub.sln") -c Release
if ($LASTEXITCODE -ne 0) { throw "テストに失敗したため中止しました。" }

dotnet publish (Join-Path $root "src\XIVToolHub.App\XIVToolHub.App.csproj") `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o $output
if ($LASTEXITCODE -ne 0) { throw "publish に失敗しました。" }

Write-Host "出力先: $output"
