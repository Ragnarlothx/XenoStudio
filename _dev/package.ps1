# Builds XenoStudio and zips a shareable copy into release\.
#   powershell -ExecutionPolicy Bypass -File _dev\package.ps1            # full package (with pinned claude.exe + codex.exe, ~650 MB)
#   powershell -ExecutionPolicy Bypass -File _dev\package.ps1 -NoRuntime # small package; the receiver's installed CLIs are used
# Never included: data\ (your chats, settings, MCP secrets, WebView2 profile). Sign-ins live in the user's own
# ~/.claude and ~/.codex, so whoever receives the zip signs in with their own accounts.
param([switch]$NoRuntime)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

dotnet build -c Release -o dist
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$ver = ([xml](Get-Content XenoStudio.csproj)).Project.PropertyGroup.Version | Select-Object -First 1
$stage = Join-Path $root "release\XenoStudio"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

$skipDirs = @('data')
if ($NoRuntime) { $skipDirs += 'runtime' }
Get-ChildItem dist -Force | Where-Object { $skipDirs -notcontains $_.Name } | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $stage $_.Name) -Recurse -Force
}
Get-ChildItem $stage -Recurse -Include *.pdb, *.xml | Remove-Item -Force
Copy-Item README.md $stage

$suffix = if ($NoRuntime) { '-lite' } else { '' }
$zip = Join-Path $root "release\XenoStudio-v$ver$suffix.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
"Packaged: $zip  ($([math]::Round((Get-Item $zip).Length / 1MB)) MB)"
