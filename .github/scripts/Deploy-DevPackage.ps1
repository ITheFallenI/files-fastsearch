# Copyright (c) Files Community
# SPDX-License-Identifier: MPL-2.0

# Builds Files.App (Debug|x64, JIT) and registers its output folder as the "Files - Dev" package.
#
# A loose registration of the build output lacks files that only the MSIX packaging step adds, such as
# Files.App.Server and the Assets content, and the app then fails at startup. This script stages that
# payload, as computed by the MSIX tooling, into the output folder before registering it.
# It only ever registers or replaces the FilesDev identity.

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$SkipBuild,
    [switch]$SkipRustBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$project = Join-Path $repoRoot "src\Files.App\Files.App.csproj"
$devIdentity = "FilesDev"

# MSBuild from the newest Visual Studio; the NativeAOT link of Files.App.Server also needs vswhere on PATH
$installerDir = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer"
$vswhere = Join-Path $installerDir "vswhere.exe"
if (-not (Test-Path $vswhere)) { throw "vswhere.exe was not found. Install Visual Studio 2026." }
$env:PATH = "$installerDir;$env:PATH"
$msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
if (-not $msbuild) { throw "MSBuild was not found." }

$properties = @("-p:Configuration=$Configuration", "-p:Platform=x64")
if ($SkipRustBuild) { $properties += "-p:SkipRustBuild=true" }

if (-not $SkipBuild) {
    Write-Host "Building Files.App ($Configuration|x64)..."
    & $msbuild $project -restore @properties -v:quiet -clp:ErrorsOnly -nologo
    if ($LASTEXITCODE -ne 0) { throw "The build failed." }
}

# The MSIX tooling's own view of the package: every file with its package-relative path
Write-Host "Computing the package payload..."
# With -getTargetResult, MSBuild logs to stderr; Windows PowerShell would turn each line into a terminating error
$ErrorActionPreference = "Continue"
$queryOutput = & $msbuild $project @properties -p:SkipRustBuild=true -nologo -v:quiet `
    "-t:GetPackagingOutputs;_ComputeAppxPackagePayload" `
    -getTargetResult:GetPackagingOutputs -getItem:AppxPackagePayload -getProperty:TargetDir 2>$null
$ErrorActionPreference = "Stop"
if ($LASTEXITCODE -ne 0) { throw "Computing the package payload failed." }
$queryText = $queryOutput -join "`n"
$query = $queryText.Substring($queryText.IndexOf("{")) | ConvertFrom-Json

$layoutDir = $query.Properties.TargetDir
$manifestPath = Join-Path $layoutDir "AppxManifest.xml"
if (-not (Test-Path $manifestPath)) { throw "$manifestPath was not found. Build Files.App first." }

[xml]$manifest = Get-Content $manifestPath
$identity = $manifest.Package.Identity.Name
if ($identity -ne $devIdentity) { throw "The manifest identity is '$identity', not '$devIdentity'. Only Dev builds can be deployed this way." }

$payload = @($query.TargetResults.GetPackagingOutputs.Items) + @($query.Items.AppxPackagePayload)
$staged = 0
foreach ($item in $payload) {
    if (-not $item.TargetPath -or $item.TargetPath -eq "AppxManifest.xml") { continue }

    $source = Get-Item -LiteralPath $item.FullPath -ErrorAction SilentlyContinue
    if (-not $source) { continue }

    $destination = Join-Path $layoutDir $item.TargetPath
    if ($source.FullName -eq $destination) { continue }

    $existing = Get-Item -LiteralPath $destination -ErrorAction SilentlyContinue
    if ($existing -and $existing.Length -eq $source.Length -and $existing.LastWriteTimeUtc -ge $source.LastWriteTimeUtc) { continue }

    New-Item -ItemType Directory -Force -Path (Split-Path $destination) | Out-Null
    Copy-Item -LiteralPath $source.FullName -Destination $destination -Force
    $staged++
}
Write-Host "Staged $staged payload file(s) into $layoutDir"

# Replace an existing Dev registration that points elsewhere; user data is kept
$registered = Get-AppxPackage -Name $devIdentity
if ($registered -and $registered.InstallLocation.TrimEnd("\") -ne $layoutDir.TrimEnd("\")) {
    Write-Host "Removing the Dev registration at $($registered.InstallLocation) (app data is kept)..."
    Remove-AppxPackage -Package $registered.PackageFullName -PreserveApplicationData
}

Add-AppxPackage -Register $manifestPath -ForceUpdateFromAnyVersion -ForceApplicationShutdown
$registered = Get-AppxPackage -Name $devIdentity
Write-Host "Registered $($registered.PackageFullName) from $($registered.InstallLocation)"
