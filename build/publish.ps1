<#
.SYNOPSIS
  Publish MMUD-Mimic as a single signed win-x64 exe and zip it for distribution.

.DESCRIPTION
  1. dotnet publish with the win-x64-single profile (framework-dependent single
     file, native libs bundled, no compression, embedded PDB) - or the
     self-contained profile with -SelfContained.
  2. Sign the exe with Azure Artifact Signing (formerly Trusted Signing) through
     the `sign` .NET global tool, unless -NoSign is given.
  3. Verify the Authenticode signature (signtool verify /pa, when signtool is on
     PATH or in a Windows Kits folder) and print the publisher.
  4. Zip the result to artifacts\MMUD-Mimic-<version>-win-x64.zip.

  Authentication for local signing: run once per session
      az login --use-device-code --scope "https://codesigning.azure.net/.default"
  (your account needs the "Trusted Signing Certificate Profile Signer" role on
  the signing account). CI uses a service principal instead - see
  .github/workflows/release.yml.

.PARAMETER Endpoint
  Regional Artifact Signing endpoint, e.g. https://eus.codesigning.azure.net
  (East US), https://wus2.codesigning.azure.net (West US 2). Defaults to the
  MME_SIGN_ENDPOINT environment variable.
.PARAMETER Account
  Artifact Signing account name (case-sensitive). Default: $env:MME_SIGN_ACCOUNT.
.PARAMETER Profile
  Certificate profile name (case-sensitive). Default: $env:MME_SIGN_PROFILE.
.PARAMETER NoSign
  Publish + zip only (for local smoke builds). The zip is named -UNSIGNED.
.PARAMETER SelfContained
  Bundle the .NET runtime into the exe (~148 MB) instead of the default
  framework-dependent build (~5 MB, needs the .NET 8 Desktop Runtime).
.PARAMETER Version
  Overrides the version stamped on the zip name; default reads <Version> from
  the csproj.

.EXAMPLE
  .\build\publish.ps1 -Endpoint https://eus.codesigning.azure.net -Account mmud-mimic -Profile mmud-mimic-public
.EXAMPLE
  .\build\publish.ps1 -NoSign
#>
[CmdletBinding()]
param(
    [string]$Endpoint = $env:MME_SIGN_ENDPOINT,
    [string]$Account  = $env:MME_SIGN_ACCOUNT,
    [string]$Profile  = $env:MME_SIGN_PROFILE,
    [switch]$NoSign,
    [switch]$SelfContained,
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$proj = Join-Path $root 'src\Mme.App\Mme.App.csproj'
$pubProfile = if ($SelfContained) { 'win-x64-selfcontained' } else { 'win-x64-single' }
$publishDir = Join-Path $root ("artifacts\publish\" + $(if ($SelfContained) { 'win-x64-selfcontained' } else { 'win-x64' }))
$artifacts = Join-Path $root 'artifacts'

if (-not $Version) {
    $Version = ([xml](Get-Content $proj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if (-not $Version) { $Version = '0.0.0' }
}

Write-Host "== MMUD-Mimic $Version : publish ==" -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish $proj -c Release -p:PublishProfile=$pubProfile -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

$exe = Join-Path $publishDir 'MMUD-Mimic.exe'
if (-not (Test-Path $exe)) { throw "publish output missing: $exe" }
Write-Host ("   {0}  {1:N1} MB" -f $exe, ((Get-Item $exe).Length / 1MB))

# anything else that ended up beside the exe is a bug in the profile - the
# whole point is ONE signed file (mme-log.txt etc. are created at runtime)
$strays = Get-ChildItem $publishDir -File | Where-Object { $_.Name -ne 'MMUD-Mimic.exe' }
if ($strays) {
    Write-Warning ("Unexpected files beside the exe: " + ($strays.Name -join ', '))
}

$signed = $false
if (-not $NoSign) {
    if (-not $Endpoint -or -not $Account -or -not $Profile) {
        throw "Signing needs -Endpoint, -Account and -Profile (or MME_SIGN_* env vars). Use -NoSign for an unsigned smoke build."
    }
    Write-Host "== sign (Azure Artifact Signing) ==" -ForegroundColor Cyan
    if (-not (Get-Command sign -ErrorAction SilentlyContinue)) {
        Write-Host "   installing the 'sign' .NET tool..."
        dotnet tool install --global sign --prerelease
        if ($LASTEXITCODE -ne 0) { throw "could not install the sign tool" }
    }
    # -b must be an absolute directory; file args are relative to it.
    sign code trusted-signing 'MMUD-Mimic.exe' `
        -b $publishDir `
        -tse $Endpoint `
        -tsa $Account `
        -tscp $Profile `
        -d 'MMUD-Mimic' `
        -u 'https://github.com/' `
        -fd SHA256 `
        -t 'http://timestamp.acs.microsoft.com' `
        -td SHA256 `
        -v Information
    if ($LASTEXITCODE -ne 0) { throw "signing failed ($LASTEXITCODE)" }
    $signed = $true

    Write-Host "== verify ==" -ForegroundColor Cyan
    $sig = Get-AuthenticodeSignature $exe
    Write-Host ("   Status   : {0}" -f $sig.Status)
    Write-Host ("   Signer   : {0}" -f $sig.SignerCertificate.Subject)
    Write-Host ("   Timestamp: {0}" -f $sig.TimeStamperCertificate.Subject)
    if ($sig.Status -ne 'Valid') { throw "signature is not Valid: $($sig.StatusMessage)" }

    $signtool = Get-Command signtool -ErrorAction SilentlyContinue
    if (-not $signtool) {
        $kits = 'C:\Program Files (x86)\Windows Kits\10\bin'
        if (Test-Path $kits) {
            $signtool = Get-ChildItem $kits -Recurse -Filter signtool.exe |
                Where-Object { $_.FullName -match '\\x64\\' } |
                Sort-Object FullName -Descending | Select-Object -First 1
        }
    }
    if ($signtool) {
        & $signtool.Source verify /pa /v $exe
        if ($LASTEXITCODE -ne 0) { throw "signtool verify failed" }
    }
}

Write-Host "== zip ==" -ForegroundColor Cyan
New-Item -ItemType Directory -Force $artifacts | Out-Null
$suffix = $(if ($SelfContained) { '-selfcontained' } else { '' }) + $(if ($signed) { '' } else { '-UNSIGNED' })
$zip = Join-Path $artifacts "MMUD-Mimic-$Version-win-x64$suffix.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $exe -DestinationPath $zip -CompressionLevel Optimal
Write-Host ("   {0}  {1:N1} MB" -f $zip, ((Get-Item $zip).Length / 1MB)) -ForegroundColor Green
if (-not $signed) {
    Write-Warning "UNSIGNED build - SmartScreen will flag it. Do not distribute; sign with the Artifact Signing flow first."
}
