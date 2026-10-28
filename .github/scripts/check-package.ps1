# Installs the test MSIX on this Windows machine, checks what it declares, starts it once and
# removes it again (epic 5 story 2). Needs an administrator, to trust the test certificate.
#   .github\scripts\check-package.ps1 publish\package
param([Parameter(Mandatory)][string]$PackageDir)

$ErrorActionPreference = 'Stop'
$package = Get-ChildItem $PackageDir -Filter *.msix | Select-Object -First 1
if (-not $package) { throw "No .msix in $PackageDir." }

Write-Host "== Trust the test certificate" -ForegroundColor Cyan
Import-Certificate -FilePath (Join-Path $PackageDir 'ComParta-test.cer') -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null

# What a pilot build left behind: a database from its first version with one share, and the port
# its invites name. The packaged install must start with both (epic 5 story 5).
Write-Host "== Leave pilot data behind" -ForegroundColor Cyan
$pilot = Join-Path $env:LOCALAPPDATA 'ComParta'
New-Item -ItemType Directory -Force (Join-Path $pilot 'engine') | Out-Null
# A port free on this machine, as the pilot would have kept. Below Windows' range for outgoing
# connections, which could take it in the meantime; Windows also reserves some ranges, so try a few.
$pilotPort = $null
foreach ($candidate in Get-Random -Minimum 20000 -Maximum 40000 -Count 20) {
    try {
        $probe = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Any, $candidate)
        $probe.Start(); $probe.Stop()
        $pilotPort = "$candidate"
        break
    }
    catch { }
}
if (-not $pilotPort) { throw 'No free port for the pilot data.' }
[System.IO.File]::WriteAllText((Join-Path $pilot 'engine\port.txt'), $pilotPort)
if (-not (Test-Path (Join-Path $pilot 'engine\port.txt'))) { throw 'Could not write the pilot port.' }
$pilotDatabase = Join-Path $pilot 'comparta.db'
python (Join-Path $PSScriptRoot 'pilot_db.py') make $pilotDatabase
if ($LASTEXITCODE -ne 0) { throw 'Could not write the pilot database.' }

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
        'the download links'      = 'Protocol Name="magnet"'
        'the download files'      = 'FileType>.torrent</'
        'the firewall rule'       = 'windows.firewallRules'
        'the start-up task'       = 'TaskId="ComPartaStartup"'
        'the publisher'           = 'Publisher="CN=ComParta"'
    }
    foreach ($item in $wanted.GetEnumerator()) {
        if (-not $text.Contains($item.Value)) { throw "The manifest does not declare $($item.Key)." }
        Write-Host "ok: $($item.Key)"
    }
    if (-not (Test-Path (Join-Path $installed.InstallLocation 'ComParta.exe'))) { throw 'ComParta.exe is not at the top of the package.' }
    # Settings, About reads it from the package (epic 5 story 1).
    if (-not (Test-Path (Join-Path $installed.InstallLocation 'THIRD-PARTY-NOTICES.txt'))) { throw 'THIRD-PARTY-NOTICES.txt is not in the package.' }
    Write-Host 'ok: the third-party notices'

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

    Write-Host "== Check it brought the pilot data over" -ForegroundColor Cyan
    $data = Join-Path $env:LOCALAPPDATA "Packages\$($installed.PackageFamilyName)\LocalState\ComParta"
    if (-not (Test-Path (Join-Path $data 'imported-from-pilot.txt'))) {
        Get-ChildItem -Recurse $data -ErrorAction SilentlyContinue | ForEach-Object { Write-Host $_.FullName }
        Get-ChildItem (Join-Path $data 'logs') -ErrorAction SilentlyContinue | Get-Content | Write-Host
        throw 'The pilot data was not imported.'
    }
    $port = Get-Content -Raw (Join-Path $data 'engine\port.txt')
    if ($port -ne $pilotPort) {
        Get-ChildItem -Recurse $data | ForEach-Object { Write-Host "$($_.FullName) $($_.LastWriteTimeUtc.ToString('o'))" }
        Get-ChildItem (Join-Path $data 'logs') -ErrorAction SilentlyContinue | Get-Content | Write-Host
        Get-ChildItem -Recurse $pilot | ForEach-Object { Write-Host "pilot: $($_.FullName)" }
        throw "The pilot port $pilotPort did not come over: $port"
    }
    $found = python (Join-Path $PSScriptRoot 'pilot_db.py') read (Join-Path $data 'comparta.db')
    $version, $name = "$found" -split ' ', 2
    if ($LASTEXITCODE -ne 0 -or [int]$version -le 1 -or $name -ne 'pilot.bin') { throw "The pilot share did not come over upgraded: $found" }
    Write-Host 'ok: the pilot share and port came over, and the database was upgraded'
    if ((python (Join-Path $PSScriptRoot 'pilot_db.py') read $pilotDatabase) -ne '1 pilot.bin') { throw 'The pilot database was changed.' }
}
finally {
    Remove-AppxPackage -Package $installed.PackageFullName
}
