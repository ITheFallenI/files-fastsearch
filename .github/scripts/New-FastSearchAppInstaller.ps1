# Copyright (c) Files Community
# Licensed under the MIT License.

# Abstract:
#  Collects a Files FastSearch release into a flat folder of GitHub release assets and writes the
#  FilesFastSearch.appinstaller that points at them. GitHub release assets cannot live in
#  subfolders, so this replaces the folder layout Create-MsixBundle.ps1 produces for the CDN.
#  The appinstaller's own Uri is the "latest release" link, which is what the in-app updater reads.

param(
    [Parameter(Mandatory)]
    [string]$AppxPackageDir,

    [Parameter(Mandatory)]
    [string]$OutputDir,

    [Parameter(Mandatory)]
    [string]$PackageManifestPath,

    [Parameter(Mandatory)]
    [string]$Repository, # owner/name

    [Parameter(Mandatory)]
    [string]$Tag
)

$ErrorActionPreference = "Stop"

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

[xml]$manifest = Get-Content $PackageManifestPath
$packageName = $manifest.Package.Identity.Name
$packagePublisher = $manifest.Package.Identity.Publisher
$version = $manifest.Package.Identity.Version

$bundle = Get-ChildItem -Path $AppxPackageDir -Filter "*.msixbundle" -Recurse | Select-Object -First 1
if ($null -eq $bundle) {
    Write-Error "No .msixbundle found in '$AppxPackageDir'"
    exit 1
}

$bundleAsset = "FilesFastSearch_${version}_x64.msixbundle"
Copy-Item $bundle.FullName (Join-Path $OutputDir $bundleAsset)

$downloadBase = "https://github.com/$Repository/releases/download/$Tag"
$dependencyEntries = ""
Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
Get-ChildItem -Path $bundle.DirectoryName -Recurse -Include *.appx, *.msix | Where-Object { $_.DirectoryName -match '\\Dependencies\\' } | ForEach-Object {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($_.FullName)
    try {
        $entry = $zip.Entries | Where-Object { $_.FullName -eq "AppxManifest.xml" } | Select-Object -First 1
        if ($null -eq $entry) { return }
        $reader = New-Object System.IO.StreamReader($entry.Open())
        [xml]$depManifest = $reader.ReadToEnd()
        $reader.Close()
    } finally { $zip.Dispose() }

    $nsMgr = New-Object System.Xml.XmlNamespaceManager($depManifest.NameTable)
    $nsMgr.AddNamespace("pkg", "http://schemas.microsoft.com/appx/manifest/foundation/windows10")
    $id = $depManifest.SelectSingleNode("/pkg:Package/pkg:Identity", $nsMgr)
    if ($null -eq $id) { return }

    # Asset names are flat, so keep the architecture in every dependency's file name
    $arch = $id.GetAttribute("ProcessorArchitecture")
    $asset = if ($_.Name -match [regex]::Escape($arch)) { $_.Name } else { "$($_.BaseName)_$arch$($_.Extension)" }
    Copy-Item $_.FullName (Join-Path $OutputDir $asset) -Force

    $dependencyEntries += @"

		<Package
			Name="$($id.GetAttribute("Name"))"
			Publisher="$($id.GetAttribute("Publisher"))"
			ProcessorArchitecture="$arch"
			Uri="$downloadBase/$asset"
			Version="$($id.GetAttribute("Version"))" />
"@
}

@"
<?xml version="1.0" encoding="utf-8"?>
<AppInstaller
	Uri="https://github.com/$Repository/releases/latest/download/FilesFastSearch.appinstaller"
	Version="$version" xmlns="http://schemas.microsoft.com/appx/appinstaller/2018">
	<MainBundle
		Name="$packageName"
		Publisher="$packagePublisher"
		Version="$version"
		Uri="$downloadBase/$bundleAsset" />
	<Dependencies>$dependencyEntries
	</Dependencies>
</AppInstaller>
"@ | Set-Content -Path (Join-Path $OutputDir "FilesFastSearch.appinstaller") -Encoding UTF8

Get-ChildItem $OutputDir | ForEach-Object { Write-Host "Release asset: $($_.Name)" }
