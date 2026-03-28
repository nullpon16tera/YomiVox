# 単一ファイルの YomiVox.exe を dist に出力（DLL が横並びにならない配布用）
# 要: マシンに .NET 8 ランタイム（Windows デスクトップ）が入っていること
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$out = Join-Path $PSScriptRoot 'dist'
dotnet publish .\YomiVox\YomiVox.csproj -c Release -r win-x64 --self-contained false `
  -p:DebugType=None -p:DebugSymbols=false -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Sdk.Web が付ける web.config はデスクトップアプリでは不要
Remove-Item (Join-Path $out 'web.config') -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Output: $out\YomiVox.exe (single-file publish, no DLLs beside exe)"
Get-ChildItem $out | Select-Object Name, @{ N = 'SizeMB'; E = { [math]::Round($_.Length / 1MB, 2) } }
