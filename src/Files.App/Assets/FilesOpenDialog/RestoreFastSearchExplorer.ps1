# Copyright (c) Files FastSearch contributors. Licensed under the MIT License.
param([ValidateSet('Enable', 'Restore')][string]$Action)
$ErrorActionPreference = 'Stop'

function Get-FastSearchShellEntries {
    param([string]$LauncherPath)
    @(
        @{ Path = 'Folder\shell\open\command'; Command = '"' + $LauncherPath + '" "%1"' },
        @{ Path = 'Folder\shell\explore\command'; Command = '"' + $LauncherPath + '" "%1"' },
        @{ Path = 'CLSID\{52205fd8-5dfb-447d-801a-d0b52f2e83e1}\shell\opennewwindow\command'; Command = '"' + $LauncherPath + '"' }
    )
}

function Read-FastSearchRegistryValue {
    param([Microsoft.Win32.RegistryKey]$Key, [string]$Name)
    if ($Key -and $Name -in $Key.GetValueNames()) {
        return @{ Present = $true; Value = $Key.GetValue($Name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames); Kind = $Key.GetValueKind($Name).ToString() }
    }
    return @{ Present = $false }
}

function Write-FastSearchRegistryValue {
    param([Microsoft.Win32.RegistryKey]$Key, [string]$Name, $Snapshot)
    if ($Snapshot.Present) {
        $Key.SetValue($Name, $Snapshot.Value, [Microsoft.Win32.RegistryValueKind]$Snapshot.Kind)
    } else {
        $Key.DeleteValue($Name, $false)
    }
}

function Restore-FastSearchShellEntries {
    param([Microsoft.Win32.RegistryKey]$ClassesRoot, [string]$LauncherPath, $Backup)
    $entries = @(Get-FastSearchShellEntries $LauncherPath)
    if ($Backup.Version -ne 1 -or @($Backup.Entries).Count -ne $entries.Count) { throw 'Invalid Explorer registration backup.' }
    for ($i = 0; $i -lt $entries.Count; $i++) {
        $key = $ClassesRoot.OpenSubKey($entries[$i].Path, $true)
        if (-not $key) { continue }
        try {
            $current = Read-FastSearchRegistryValue $key ''
            if (-not $current.Present -or $current.Value -ine $entries[$i].Command) { continue }
            $delegate = Read-FastSearchRegistryValue $key 'DelegateExecute'
            Write-FastSearchRegistryValue $key '' $Backup.Entries[$i].Default
            if ($delegate.Present -and $delegate.Kind -eq 'String' -and $delegate.Value -ceq '') {
                Write-FastSearchRegistryValue $key 'DelegateExecute' $Backup.Entries[$i].Delegate
            }
        } finally { $key.Dispose() }
    }
}

function Set-FastSearchShellIntegration {
    param(
        [Microsoft.Win32.RegistryKey]$ClassesRoot,
        [string]$LauncherPath,
        [string]$StateDirectory,
        [ValidateSet('Enable', 'Restore')][string]$Action
    )
    $entries = @(Get-FastSearchShellEntries $LauncherPath)
    $backupPath = Join-Path $StateDirectory 'ExplorerRegistrationBackup.clixml'
    $owned = $false
    foreach ($entry in $entries) {
        $key = $ClassesRoot.OpenSubKey($entry.Path)
        try {
            $current = Read-FastSearchRegistryValue $key ''
            if ($current.Present -and $current.Value -ieq $entry.Command) { $owned = $true }
        } finally { if ($key) { $key.Dispose() } }
    }
    if ($Action -eq 'Restore') {
        if (-not $owned) { return }
        $backup = Import-Clixml -LiteralPath $backupPath
        Restore-FastSearchShellEntries $ClassesRoot $LauncherPath $backup
        return
    }
    if ($owned) {
        # Keep the original baseline when an enabled registration is repaired.
        $backup = Import-Clixml -LiteralPath $backupPath
        if ($backup.Version -ne 1 -or @($backup.Entries).Count -ne $entries.Count) { throw 'Invalid Explorer registration backup.' }
    } else {
        $snapshots = foreach ($entry in $entries) {
            $key = $ClassesRoot.OpenSubKey($entry.Path)
            try {
                @{ Default = (Read-FastSearchRegistryValue $key ''); Delegate = (Read-FastSearchRegistryValue $key 'DelegateExecute') }
            } finally { if ($key) { $key.Dispose() } }
        }
        $backup = @{ Version = 1; Entries = @($snapshots) }
        $history = Join-Path $StateDirectory 'Backups'
        New-Item -ItemType Directory -Path $history -Force | Out-Null
        $archive = Join-Path $history ((Get-Date -Format 'yyyyMMdd-HHmmss-ffff') + '-' + [guid]::NewGuid().ToString('N') + '.clixml')
        $backup | Export-Clixml -LiteralPath $archive -Depth 8 -NoClobber
        Copy-Item -LiteralPath $archive -Destination $backupPath -Force
    }
    try {
        foreach ($entry in $entries) {
            $key = $ClassesRoot.CreateSubKey($entry.Path)
            try {
                $key.SetValue('', $entry.Command, [Microsoft.Win32.RegistryValueKind]::String)
                $key.SetValue('DelegateExecute', '', [Microsoft.Win32.RegistryValueKind]::String)
            } finally { $key.Dispose() }
        }
    } catch {
        Restore-FastSearchShellEntries $ClassesRoot $LauncherPath $backup
        throw
    }
}

if ($Action) {
    $mutex = [Threading.Mutex]::new($false, 'Local\FilesFastSearch.ExplorerReplacement')
    $acquired = $false
    try {
        try { $acquired = $mutex.WaitOne(15000) } catch [Threading.AbandonedMutexException] { $acquired = $true }
        if (-not $acquired) { throw 'Another Explorer registration operation is running.' }
        $classes = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Classes')
        try {
            Set-FastSearchShellIntegration -ClassesRoot $classes -LauncherPath (Join-Path $PSScriptRoot 'Files.FastSearch.Launcher.exe') -StateDirectory $PSScriptRoot -Action $Action
        } finally { $classes.Dispose() }
        exit 0
    } catch {
        Write-Error $_
        exit 1
    } finally {
        if ($acquired) { $mutex.ReleaseMutex() }
        $mutex.Dispose()
    }
}
