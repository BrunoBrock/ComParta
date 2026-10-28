# Unpacks the zip for people without the Store (epic 5 stories 4 and 6), checks what it carries,
# starts the ComParta.exe inside it with no .NET to lean on, and drives it through its own screen:
# the version About shows, sharing a file picked in the real file dialog, and quitting.
#   .github\scripts\check-zip.ps1 publish\ComParta-1.2.3-win-x64.zip -Version 1.2.3
# Needs the screen's packages (src\ui\node_modules), which build.ps1 installs.
param(
    [Parameter(Mandatory)][string]$Zip,
    [Parameter(Mandatory)][string]$Version
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$work = Join-Path ([IO.Path]::GetTempPath()) "comparta-zip-$([guid]::NewGuid().ToString('N'))"
$folder = Join-Path $work 'app'
$home_ = Join-Path $work 'home'
New-Item -ItemType Directory -Force $folder, $home_ | Out-Null

Write-Host "== Unpack $(Split-Path -Leaf $Zip)" -ForegroundColor Cyan
Expand-Archive -Path $Zip -DestinationPath $folder
$exe = Join-Path $folder 'ComParta.exe'

Write-Host "== Check what it carries" -ForegroundColor Cyan
$wanted = [ordered]@{
    'ComParta.exe'                 = 'ComParta.exe'
    'the license'                  = 'LICENSE.txt'
    # Self-contained: .NET and the Windows App SDK come in the folder, nothing to install first.
    'its own .NET'                 = 'hostfxr.dll'
    'its own .NET runtime'         = 'coreclr.dll'
    'its own Windows App SDK'      = 'Microsoft.WindowsAppRuntime.dll'
    'the screen'                   = 'wwwroot\index.html'
}
# The third-party notices (epic 5 story 1) ship whenever the repository has them.
if (Test-Path (Join-Path $root 'THIRD-PARTY-NOTICES.txt')) { $wanted['the third-party notices'] = 'THIRD-PARTY-NOTICES.txt' }
foreach ($item in $wanted.GetEnumerator()) {
    if (-not (Test-Path (Join-Path $folder $item.Value))) {
        Get-ChildItem $folder | ForEach-Object { Write-Host $_.Name }
        throw "The zip does not carry $($item.Key) ($($item.Value))."
    }
    Write-Host "ok: $($item.Key)"
}

# No secret, account or personal path in the files people download.
$text = Get-ChildItem $folder -File -Include *.txt, *.json, *.xml, *.config, *.html -Recurse | ForEach-Object { Get-Content -Raw $_.FullName }
foreach ($pattern in @('C:\Users\', 'D:\a\', 'runneradmin', 'ghp_', 'github_pat_')) {
    if ("$text".Contains($pattern)) { throw "The zip's files mention $pattern." }
}
Write-Host 'ok: no personal paths or keys in its text files'

# Drives the app through its screen; see drive-app.mjs.
function Invoke-Drive([string[]]$Arguments) {
    $output = & node (Join-Path $PSScriptRoot 'drive-app.mjs') $port @Arguments
    if ($LASTEXITCODE -ne 0) { throw "The screen step '$($Arguments -join ' ')' failed: $output" }
    Write-Host $output
    return $output
}

# A port free on this machine; Windows reserves some ranges, so try a few.
$port = $null
foreach ($candidate in Get-Random -Minimum 20000 -Maximum 40000 -Count 20) {
    try {
        $probe = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $candidate)
        $probe.Start(); $probe.Stop()
        $port = $candidate
        break
    }
    catch { }
}
if (-not $port) { throw 'No free port to reach the screen.' }
$saved = @{}
$environment = @{
    # Data under a test folder, never the runner's own (and side by side with any other copy).
    COMPARTA_HOME                         = $home_
    # Lets this script reach the screen inside the app's web view.
    WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS = "--remote-debugging-port=$port"
    # Points away from the .NET installed on the runner, as on a PC without it.
    DOTNET_ROOT                           = (Join-Path $work 'no-dotnet')
    DOTNET_ROOT_X64                       = (Join-Path $work 'no-dotnet')
    DOTNET_MULTILEVEL_LOOKUP              = '0'
}
foreach ($name in $environment.Keys) {
    $saved[$name] = [Environment]::GetEnvironmentVariable($name)
    [Environment]::SetEnvironmentVariable($name, $environment[$name])
}

$app = $null
try {
    Write-Host "== Start it" -ForegroundColor Cyan
    $app = Start-Process -FilePath $exe -WorkingDirectory $folder -PassThru
    Start-Sleep -Seconds 5
    if ($app.HasExited) { throw "ComParta stopped at start, code $($app.ExitCode)." }

    Write-Host "== The version About shows" -ForegroundColor Cyan
    $shown = "$(Invoke-Drive @('version'))".Trim()
    if (-not ($shown -eq $Version -or $shown.StartsWith("$Version."))) { throw "About shows version $shown, not $Version." }
    Write-Host "ok: version $shown"

    Write-Host "== Share a file picked in the file dialog" -ForegroundColor Cyan
    $file = Join-Path $work 'family photos.bin'
    [IO.File]::WriteAllBytes($file, [byte[]]((1..255) * 400))
    Invoke-Drive @('start-share') | Out-Null
    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
    $automation = [Windows.Automation.AutomationElement]
    $byProcess = [Windows.Automation.PropertyCondition]::new($automation::ProcessIdProperty, $app.Id)
    $until = (Get-Date).AddSeconds(60)
    $dialog = $null
    while (-not $dialog -and (Get-Date) -lt $until) {
        Start-Sleep -Seconds 1
        $dialog = $automation::RootElement.FindAll([Windows.Automation.TreeScope]::Children, $byProcess) |
            Where-Object { $_.Current.ClassName -eq '#32770' } | Select-Object -First 1
    }
    if (-not $dialog) { throw 'The file dialog did not open.' }
    $byId = { param($id) [Windows.Automation.PropertyCondition]::new($automation::AutomationIdProperty, $id) }
    $isEdit = [Windows.Automation.PropertyCondition]::new($automation::ControlTypeProperty, [Windows.Automation.ControlType]::Edit)
    $nameBox = $dialog.FindFirst([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.AndCondition]::new((& $byId '1148'), $isEdit))
    if (-not $nameBox) { throw 'The file dialog has no file name box.' }
    $nameBox.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($file)
    $open = $dialog.FindFirst([Windows.Automation.TreeScope]::Children, (& $byId '1'))
    if (-not $open) { throw 'The file dialog has no Open button.' }
    $open.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Invoke-Drive @('shared', (Split-Path -Leaf $file)) | Out-Null
    Write-Host 'ok: the file is shared and the screen heard about it'

    Write-Host "== Quit" -ForegroundColor Cyan
    Invoke-Drive @('quit') | Out-Null
    if (-not $app.WaitForExit(30000)) { throw 'ComParta did not quit.' }
    Write-Host 'ok: ComParta quit'
}
catch {
    Write-Host "== What was running" -ForegroundColor Yellow
    if ($app) { Write-Host "ComParta exited: $($app.HasExited)" }
    Get-CimInstance Win32_Process -Filter "Name = 'msedgewebview2.exe' OR Name = 'ComParta.exe'" |
        ForEach-Object { Write-Host "$($_.ProcessId) $($_.ParentProcessId) $($_.CommandLine)" }
    Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object LocalPort -eq $port |
        ForEach-Object { Write-Host "listening on ${port}: process $($_.OwningProcess)" }
    Get-ChildItem -Recurse $home_ -ErrorAction SilentlyContinue | ForEach-Object { Write-Host $_.FullName }
    Get-ChildItem (Join-Path $home_ 'logs') -File -ErrorAction SilentlyContinue | Get-Content | Write-Host
    throw
}
finally {
    if ($app -and -not $app.HasExited) { $app | Stop-Process -Force }
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
}
