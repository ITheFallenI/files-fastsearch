# Copyright (c) Files FastSearch contributors. Licensed under the MIT License.
param([switch]$ValidateOnly)
$ErrorActionPreference = 'Stop'
try {
    if (-not [Environment]::Is64BitProcess -or [Environment]::OSVersion.Version.Build -lt 22000) {
        throw 'This preview requires Windows 11 x64 (build 22000 or later).'
    }
    $payload = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'payload.json') -Raw | ConvertFrom-Json
    foreach ($file in $payload.files) {
        if ([IO.Path]::GetFileName($file.name) -ne $file.name) { throw 'Invalid payload filename.' }
        $path = Join-Path $PSScriptRoot $file.name
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.sha256) {
            throw "The installer payload is damaged: $($file.name). Download it again from GitHub."
        }
    }
    $certPath = Join-Path $PSScriptRoot 'FilesFastSearch.cer'
    $cert = [Security.Cryptography.X509Certificates.X509Certificate2]::new($certPath)
    if ($cert.HasPrivateKey -or $cert.Thumbprint -ne $payload.certificateThumbprint) {
        throw 'Unexpected signing certificate.'
    }
    if ($ValidateOnly) { Write-Output 'PASS: preview installer payload verified.'; exit 0 }
    Write-Host "Files FastSearch $($payload.version) - unofficial preview" -ForegroundColor Cyan
    Write-Host 'Project: https://github.com/ITheFallenI/files-fastsearch'
    Write-Host 'This installs the app and Microsoft Windows App Runtime for your Windows account.'
    Write-Host 'An existing FastSearch window may close during the update. Its settings are backed up first.'
    Write-Host 'To enable indexed search afterwards, use Settings > Advanced > Fast search in the app.'
    Write-Host 'Registering the indexing service requires a separate Windows administrator prompt.'
    Write-Host ''
    Write-Host 'This experimental build uses a SELF-SIGNED certificate. Windows may show an unknown-publisher warning.' -ForegroundColor Yellow
    Write-Host 'It has not been tested on a clean Windows installation.'
    Write-Host "Publisher: $($cert.Subject)"
    Write-Host "Certificate thumbprint: $($cert.Thumbprint)"
    Write-Host ''
    $trusted = Test-Path -LiteralPath "Cert:\LocalMachine\TrustedPeople\$($cert.Thumbprint)"
    if (-not $trusted) {
        Write-Host 'Continuing adds this public signing certificate to LOCAL COMPUTER > Trusted People.'
        Write-Host 'This trusts software signed with this certificate for ALL USERS. No root certificate is added.'
        Write-Host 'Windows will request administrator approval for that certificate change.'
        Write-Host 'Only continue if you trust this project. Type TRUST AND INSTALL to agree, or press Enter to cancel.'
        if ((Read-Host 'Choice') -cne 'TRUST AND INSTALL') { Write-Host 'Cancelled.'; exit 0 }
    } elseif ((Read-Host 'Type INSTALL to continue, or press Enter to cancel') -cne 'INSTALL') {
        Write-Host 'Cancelled.'; exit 0
    }
    $installed = Get-AppxPackage -Name FilesFastSearch
    if ($installed -and [version]$installed.Version -gt [version]$payload.version) {
        throw 'A newer version is already installed. This installer will not downgrade it.'
    }
    if ($installed) {
        $source = Join-Path $env:LOCALAPPDATA "Packages\$($installed.PackageFamilyName)"
        $backup = Join-Path $env:LOCALAPPDATA ('FilesFastSearch\SettingsBackups\' + (Get-Date -Format 'yyyyMMdd-HHmmss-ffff'))
        foreach ($folder in @('Settings', 'LocalState', 'RoamingState')) {
            $from = Join-Path $source $folder
            if (Test-Path -LiteralPath $from) {
                $to = Join-Path $backup $folder
                & robocopy.exe $from $to /E /R:1 /W:1 /XJ /NFL /NDL /NJH /NJS /NP | Out-Null
                if ($LASTEXITCODE -ge 8) { throw "Settings backup failed. Close Files FastSearch and try again. Backup: $backup" }
            }
        }
        Write-Host "Settings backup: $backup"
    }
    if (-not $trusted) {
        $encodedCert = [Convert]::ToBase64String($cert.RawData)
        $trustCommand = @"
`$ErrorActionPreference = 'Stop'
try {
    `$cert = [Security.Cryptography.X509Certificates.X509Certificate2]::new([Convert]::FromBase64String('$encodedCert'))
    if (`$cert.Thumbprint -ne '$($cert.Thumbprint)' -or `$cert.HasPrivateKey) { exit 1 }
    `$store = [Security.Cryptography.X509Certificates.X509Store]::new('TrustedPeople', 'LocalMachine')
    try { `$store.Open('ReadWrite'); `$store.Add(`$cert) } finally { `$store.Close() }
    exit 0
} catch { exit 1 }
"@
        $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($trustCommand))
        $process = Start-Process -FilePath "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" `
            -Verb RunAs -WindowStyle Hidden -ArgumentList @('-NoProfile', '-NonInteractive', '-EncodedCommand', $encodedCommand) -Wait -PassThru
        if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath "Cert:\LocalMachine\TrustedPeople\$($cert.Thumbprint)")) {
            throw 'Certificate trust was not approved or could not be installed. App installation has stopped.'
        }
    }
    Add-AppxPackage -Path (Join-Path $PSScriptRoot 'FilesFastSearch.msix') `
        -DependencyPath (Join-Path $PSScriptRoot 'Microsoft.WindowsAppRuntime.2.msix') -ForceApplicationShutdown
    $installed = Get-AppxPackage -Name FilesFastSearch
    if (-not $installed -or [version]$installed.Version -ne [version]$payload.version -or $installed.Status -ne 'Ok') {
        throw 'Windows did not confirm successful installation.'
    }
    Write-Host 'Installed. Open Files FastSearch from the Start menu.' -ForegroundColor Green
    Write-Host 'To uninstall, use Windows Settings > Apps. Remove the indexing service in the app first.'
    Write-Host "To remove certificate trust later: certlm.msc > Trusted People > Certificates > $($cert.Subject)."
    [void](Read-Host 'Press Enter to finish')
    exit 0
} catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    if (-not $ValidateOnly) { [void](Read-Host 'Installation stopped. Press Enter to close') }
    exit 1
}
