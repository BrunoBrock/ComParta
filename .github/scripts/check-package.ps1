# Installs the test MSIX on this Windows machine, checks what it declares, starts it once and
# removes it again (epic 5 story 2). Needs an administrator, to trust the test certificate.
#   .github\scripts\check-package.ps1 publish\package
param([Parameter(Mandatory)][string]$PackageDir)

$ErrorActionPreference = 'Stop'
$package = Get-ChildItem $PackageDir -Filter *.msix | Select-Object -First 1
if (-not $package) { throw "No .msix in $PackageDir." }

Write-Host "== Trust the test certificate" -ForegroundColor Cyan
Import-Certificate -FilePath (Join-Path $PackageDir 'ComParta-test.cer') -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null

Write-Host "== Install $($package.Name)" -ForegroundColor Cyan
Add-AppxPackage -Path $package.FullName
$installed = Get-AppxPackage | Where-Object { $_.Publisher -eq 'CN=ComParta' } | Select-Object -First 1
if (-not $installed) { throw 'The package did not install.' }
Write-Host "Installed $($installed.PackageFullName), version $($installed.Version)"

try {
    Write-Host "== Check what the package declares" -ForegroundColor Cyan
    [xml]$manifest = Get-Content (Join-Path $installed.InstallLocation 'AppxManifest.xml')
    $text = $manifest.OuterXml
    $wanted = [ordered]@{
        'the comparta: links'     = 'Protocol Name="comparta"'
        'the firewall rule'       = 'windows.firewallRules'
        'the start-up task'       = 'TaskId="ComPartaStartup"'
        'the publisher'           = 'Publisher="CN=ComParta"'
    }
    foreach ($item in $wanted.GetEnumerator()) {
        if (-not $text.Contains($item.Value)) { throw "The manifest does not declare $($item.Key)." }
        Write-Host "ok: $($item.Key)"
    }
    if (-not (Test-Path (Join-Path $installed.InstallLocation 'ComParta.exe'))) { throw 'ComParta.exe is not at the top of the package.' }

    Write-Host "== Start it" -ForegroundColor Cyan
    Start-Process "shell:AppsFolder\$($installed.PackageFamilyName)!App"
    $until = (Get-Date).AddSeconds(60)
    $running = $null
    while (-not $running -and (Get-Date) -lt $until) {
        Start-Sleep -Seconds 2
        $running = Get-Process ComParta -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -and $_.Path.StartsWith($installed.InstallLocation, [StringComparison]::OrdinalIgnoreCase) }
    }
    if (-not $running) { throw 'ComParta did not start from the package.' }
    # Still running after a few seconds: it did not fail at start.
    Start-Sleep -Seconds 10
    if ($running | ForEach-Object { $_.Refresh(); $_.HasExited } | Where-Object { $_ }) { throw 'ComParta stopped soon after starting.' }
    Write-Host 'ok: ComParta started from the package and kept running'
    $running | Stop-Process -Force
}
finally {
    Remove-AppxPackage -Package $installed.PackageFullName
}
