# Copyright (c) Files FastSearch contributors. Licensed under the MIT License.
param(
    [Parameter(Mandatory)][string]$PackagePath,
    [Parameter(Mandatory)][string]$RuntimePackagePath,
    [Parameter(Mandatory)][string]$PublicCertificatePath,
    [Parameter(Mandatory)][version]$Version,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$out = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $out) { throw 'Choose a new output directory; existing files are preserved.' }
New-Item -ItemType Directory -Path $out | Out-Null
$payload = New-Item -ItemType Directory -Path (Join-Path $out 'payload')
Copy-Item -LiteralPath $PackagePath -Destination (Join-Path $payload 'FilesFastSearch.msix')
Copy-Item -LiteralPath $RuntimePackagePath -Destination (Join-Path $payload 'Microsoft.WindowsAppRuntime.2.msix')
$cert = [Security.Cryptography.X509Certificates.X509Certificate2]::new((Resolve-Path -LiteralPath $PublicCertificatePath).Path)
if ($cert.HasPrivateKey) { throw 'Only a public certificate may be bundled.' }
Copy-Item -LiteralPath $PublicCertificatePath -Destination (Join-Path $payload 'FilesFastSearch.cer')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'preview-installer\Install.ps1') -Destination $payload
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'preview-installer\Install.cmd') -Destination $payload
$manifest = @{
    version = $Version.ToString()
    certificateThumbprint = $cert.Thumbprint
    files = @(Get-ChildItem -LiteralPath $payload -File | Where-Object Extension -in '.msix', '.cer' | ForEach-Object {
        @{ name = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $payload 'payload.json') -Encoding UTF8
$exe = Join-Path $out "FilesFastSearch-$Version-x64-Setup.exe"
$files = @(Get-ChildItem -LiteralPath $payload -File)
$strings = for ($i = 0; $i -lt $files.Count; $i++) { "FILE$i=`"$($files[$i].Name)`"" }
$entries = for ($i = 0; $i -lt $files.Count; $i++) { "%FILE$i%=" }
$sed = @"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=1
HideExtractAnimation=0
UseLongFileName=1
Compress=0
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=6144
PackageInstallSpace=0
KeepCabinet=0
RebootMode=N
InstallPrompt=Install Files FastSearch $Version unofficial preview? The setup explains its self-signed certificate before making changes.
DisplayLicense=
FinishMessage=
TargetName=$exe
FriendlyName=Files FastSearch $Version Preview Setup
AppLaunched=$env:SystemRoot\System32\cmd.exe /d /c Install.cmd
PostInstallCmd=<None>
AdminQuietInstCmd=
UserQuietInstCmd=
SourceFiles=SourceFiles
[Strings]
$($strings -join "`r`n")
[SourceFiles]
SourceFiles0=$($payload.FullName)\
[SourceFiles0]
$($entries -join "`r`n")
"@
$sedPath = Join-Path $out 'setup.sed'
[IO.File]::WriteAllText($sedPath, ($sed -replace '\r?\n', "`r`n"), [Text.Encoding]::ASCII)
$process = Start-Process -FilePath "$env:SystemRoot\System32\iexpress.exe" -ArgumentList @('/N', '/Q', $sedPath) -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $exe)) { throw "EXE packaging failed: $($process.ExitCode)" }
Write-Output $exe
