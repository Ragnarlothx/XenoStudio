# Builds XenoStudio and zips a shareable copy into release\.
#   powershell -ExecutionPolicy Bypass -File _dev\package.ps1            # full package (with pinned claude.exe + codex.exe, ~700 MB)
#   powershell -ExecutionPolicy Bypass -File _dev\package.ps1 -NoRuntime # no CLIs: the setup wizard installs them (~65 MB zip)
#   add -FrameworkDependent for a tiny zip that needs the .NET 8 Desktop Runtime installed
# By default the package is self-contained: .NET is included, so nothing has to be installed before the first launch.
# Never included: data\ (your chats, settings, MCP secrets, WebView2 profile). Sign-ins live in the user's own
# ~/.claude and ~/.codex, so whoever receives the zip signs in with their own accounts (the setup wizard walks them through it).
param([switch]$NoRuntime, [switch]$FrameworkDependent)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$ver = ([xml](Get-Content XenoStudio.csproj)).Project.PropertyGroup.Version | Select-Object -First 1
$stage = Join-Path $root "release\XenoStudio"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

if ($FrameworkDependent) {
    dotnet build XenoStudio.csproj -c Release -o $stage
} else {
    dotnet publish XenoStudio.csproj -c Release -r win-x64 --self-contained true -o $stage
}
if ($LASTEXITCODE -ne 0) { throw "build failed" }

foreach ($d in @('data') + $(if ($NoRuntime) { @('runtime') } else { @() })) {
    $p = Join-Path $stage $d; if (Test-Path $p) { Remove-Item $p -Recurse -Force }
}
if (-not $NoRuntime) {   # the pinned CLIs aren't in git; take them from the working copy in dist\
    Copy-Item (Join-Path $root 'dist\runtime') (Join-Path $stage 'runtime') -Recurse -Force
}
Get-ChildItem $stage -Recurse -Include *.pdb, *.xml | Remove-Item -Force
Copy-Item README.md, INSTALL.md, THIRD_PARTY_NOTICES.md $stage -ErrorAction SilentlyContinue

$suffix = $(if ($NoRuntime) { '' } else { '-with-cli' }) + $(if ($FrameworkDependent) { '-needs-dotnet' } else { '' })
$zip = Join-Path $root "release\XenoStudio-v$ver-win-x64$suffix.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $true)
"Packaged: $zip  ($([math]::Round((Get-Item $zip).Length / 1MB)) MB)"
