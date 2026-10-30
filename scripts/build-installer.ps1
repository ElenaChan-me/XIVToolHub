# publish 済みの XIVToolHub.exe から、インストーラー（publish\XIVToolHub-Setup.exe）を作る。
# Inno Setup 6 が必要（ISCC.exe の場所は自動で探す。見つからなければ -Iscc で指定する）。
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$Runtime = "win-x64",
    [string]$Iscc = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$version = $Version.TrimStart("v", "V")
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "バージョンの形式が違います: $Version" }

$exe = Join-Path $root "publish\$Runtime\XIVToolHub.exe"
if (-not (Test-Path $exe)) { throw "先に scripts/publish.ps1 で $exe を作ってください。" }

if ($Iscc -eq "") {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    $Iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $Iscc) { throw "Inno Setup 6（ISCC.exe）が見つかりません。" }

& $Iscc "/DAppVersion=$version" "/DSourceExe=$exe" "/DOutputDir=$(Join-Path $root 'publish')" (Join-Path $root "installer\XIVToolHub.iss")
if ($LASTEXITCODE -ne 0) { throw "インストーラーの作成に失敗しました。" }
Write-Host "出力先: $(Join-Path $root 'publish\XIVToolHub-Setup.exe')"
