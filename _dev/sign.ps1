# Signs files with Azure Artifact Signing (formerly Trusted Signing) and timestamps them.
#   powershell -ExecutionPolicy Bypass -File _dev\sign.ps1 dist\XenoStudio.exe [more files…]
# Needs: `az login` done once (the signing client picks up that sign-in), the Artifact Signing client tools
# (winget install Microsoft.Azure.ArtifactSigningClientTools), and _dev\signing.json:
#   { "Endpoint": "https://<region>.codesigning.azure.net/", "CodeSigningAccountName": "...", "CertificateProfileName": "..." }
param([Parameter(Mandatory, ValueFromRemainingArguments)][string[]]$Files)
$ErrorActionPreference = 'Stop'

$meta = Join-Path $PSScriptRoot 'signing.json'
if (-not (Test-Path $meta)) { throw "Missing $meta (endpoint, account and certificate profile names)." }

$dlib = Join-Path $env:LOCALAPPDATA 'Microsoft\MicrosoftArtifactSigningClientTools\Azure.CodeSigning.Dlib.dll'
if (-not (Test-Path $dlib)) { throw "Artifact Signing client tools not found. Run: winget install Microsoft.Azure.ArtifactSigningClientTools" }

$signtool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
    Where-Object FullName -match '\\x64\\' | Sort-Object FullName | Select-Object -Last 1 -ExpandProperty FullName
if (-not $signtool) { throw "signtool.exe not found (Windows SDK)." }

foreach ($f in $Files) {
    $path = (Resolve-Path $f).Path
    & $signtool sign /v /fd SHA256 /tr 'http://timestamp.acs.microsoft.com' /td SHA256 /dlib $dlib /dmdf $meta $path
    if ($LASTEXITCODE -ne 0) { throw "signing failed: $path" }
    $sig = Get-AuthenticodeSignature $path
    "{0}: {1} ({2})" -f (Split-Path $path -Leaf), $sig.Status, $sig.SignerCertificate.Subject
}
