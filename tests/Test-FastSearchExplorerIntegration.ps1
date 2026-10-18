# Copyright (c) Files FastSearch contributors. Licensed under the MIT License.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\src\Files.App\Assets\FilesOpenDialog\RestoreFastSearchExplorer.ps1')
$id = [guid]::NewGuid().ToString('N')
$testPath = 'Software\FilesFastSearchTests\' + $id
$root = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($testPath)
$state = Join-Path ([IO.Path]::GetTempPath()) ('FilesFastSearchTests-' + $id)
$launcher = Join-Path $state 'Files.FastSearch.Launcher.exe'
function Assert-Equal($Expected, $Actual, [string]$Message) {
    if ($Expected -cne $Actual) { throw "$Message (expected '$Expected', actual '$Actual')" }
}
try {
    $entries = @(Get-FastSearchShellEntries $launcher)
    Set-FastSearchShellIntegration $root $launcher $state Enable
    $baseline = (Get-FileHash (Join-Path $state 'ExplorerRegistrationBackup.clixml')).Hash
    Set-FastSearchShellIntegration $root $launcher $state Enable
    Assert-Equal $baseline (Get-FileHash (Join-Path $state 'ExplorerRegistrationBackup.clixml')).Hash 'Repeated enable must keep the original baseline'
    foreach ($entry in $entries) {
        $key = $root.OpenSubKey($entry.Path)
        try { Assert-Equal $entry.Command $key.GetValue('') 'Enable must register each handler' } finally { $key.Dispose() }
    }
    Set-FastSearchShellIntegration $root $launcher $state Restore
    foreach ($entry in $entries) {
        $key = $root.OpenSubKey($entry.Path)
        try { Assert-Equal 0 $key.GetValueNames().Count 'Absent original values must remain absent' } finally { $key.Dispose() }
    }
    'PASS: enable, repeated enable, and exact restoration of absent values'

    $key = $root.OpenSubKey($entries[0].Path, $true)
    try {
        $key.SetValue('', '%LOCALAPPDATA%\Other Files\Launcher.exe "%1"', [Microsoft.Win32.RegistryValueKind]::ExpandString)
        $key.SetValue('DelegateExecute', '{11111111-1111-1111-1111-111111111111}')
        $key.SetValue('Unrelated', 'preserve me')
    } finally { $key.Dispose() }
    $key = $root.OpenSubKey($entries[1].Path, $true)
    try { $key.SetValue('', [byte[]]@(1,2,3), [Microsoft.Win32.RegistryValueKind]::Binary) } finally { $key.Dispose() }
    Set-FastSearchShellIntegration $root $launcher $state Enable
    Set-FastSearchShellIntegration $root $launcher $state Restore
    $key = $root.OpenSubKey($entries[0].Path)
    try {
        Assert-Equal 'ExpandString' $key.GetValueKind('').ToString() 'Preserve registry value type'
        Assert-Equal '%LOCALAPPDATA%\Other Files\Launcher.exe "%1"' $key.GetValue('', $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) 'Preserve unexpanded environment variables'
        Assert-Equal '{11111111-1111-1111-1111-111111111111}' $key.GetValue('DelegateExecute') 'Restore previous delegate'
        Assert-Equal 'preserve me' $key.GetValue('Unrelated') 'Preserve unrelated values'
    } finally { $key.Dispose() }
    $key = $root.OpenSubKey($entries[1].Path)
    try { Assert-Equal '1,2,3' ($key.GetValue('') -join ',') 'Preserve binary value data' } finally { $key.Dispose() }
    'PASS: previous handler, environment strings, binary data, delegate and unrelated values preserved'

    Set-FastSearchShellIntegration $root $launcher $state Enable
    $key = $root.OpenSubKey($entries[0].Path, $true)
    try { $key.SetValue('', 'new manager'); $key.SetValue('DelegateExecute', 'new delegate') } finally { $key.Dispose() }
    $key = $root.OpenSubKey($entries[1].Path, $true)
    try { $key.SetValue('DelegateExecute', 'changed delegate') } finally { $key.Dispose() }
    Set-FastSearchShellIntegration $root $launcher $state Restore
    $key = $root.OpenSubKey($entries[0].Path)
    try {
        Assert-Equal 'new manager' $key.GetValue('') 'Do not replace another manager'
        Assert-Equal 'new delegate' $key.GetValue('DelegateExecute') 'Do not replace another delegate'
    } finally { $key.Dispose() }
    $key = $root.OpenSubKey($entries[1].Path)
    try { Assert-Equal 'changed delegate' $key.GetValue('DelegateExecute') 'Do not overwrite a delegate changed after enabling' } finally { $key.Dispose() }
    'PASS: partial restore leaves newer file-manager registrations untouched'

    Set-FastSearchShellIntegration $root $launcher $state Enable
    $key = $root.OpenSubKey($entries[0].Path, $true)
    try { $key.SetValue('', 'newer manager') } finally { $key.Dispose() }
    Set-FastSearchShellIntegration $root $launcher $state Enable
    Set-FastSearchShellIntegration $root $launcher $state Restore
    $key = $root.OpenSubKey($entries[0].Path)
    try { Assert-Equal 'new manager' $key.GetValue('') 'Repair must retain the original baseline of this activation' } finally { $key.Dispose() }
    'PASS: repair preserves baseline and history backups'
} finally {
    $root.Dispose()
    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($testPath, $false)
    # Retain temporary backup files as test evidence.
}
