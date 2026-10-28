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
    # Lets this script reach the screen inside the app's web view (MainPage.OpenScreenForChecks).
    COMPARTA_SCREEN_CHECK_PORT            = "$port"
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
    if ($shown -notmatch "^$([regex]::Escape($Version))([.+]|$)") { throw "About shows version $shown, not $Version." }
    Write-Host "ok: version $shown"

    Write-Host "== Share a file picked in the file dialog" -ForegroundColor Cyan
    $file = Join-Path $work 'family photos.bin'
    [IO.File]::WriteAllBytes($file, [byte[]]((1..255) * 400))
    Invoke-Drive @('start-share') | Out-Null
    # The file dialog is a plain Windows dialog: fill its file name box and press Open with window
    # messages, which do not depend on the desktop having focus as typing would.
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class CheckFileDialog
{
    delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int size);
    [DllImport("user32.dll")] static extern int GetDlgCtrlID(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetDlgItem(IntPtr dialog, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, StringBuilder lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    static string ClassOf(IntPtr hwnd) { var name = new StringBuilder(256); GetClassName(hwnd, name, 256); return name.ToString(); }

    static uint ProcessOf(IntPtr hwnd) { uint pid; GetWindowThreadProcessId(hwnd, out pid); return pid; }

    // The dialog belongs to the app, or (when Windows shows it from a helper process) to a window of it.
    public static IntPtr Find(int processId)
    {
        var found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || ClassOf(hwnd) != "#32770") return true;
            var owner = GetWindow(hwnd, 4 /* GW_OWNER */);
            if (ProcessOf(hwnd) == processId || (owner != IntPtr.Zero && ProcessOf(owner) == processId)) { found = hwnd; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static string DescribeAll()
    {
        var text = new StringBuilder();
        EnumWindows((hwnd, _) =>
        {
            if (IsWindowVisible(hwnd))
                text.AppendLine("  window " + ClassOf(hwnd) + " process " + ProcessOf(hwnd) + " owner process " + ProcessOf(GetWindow(hwnd, 4)));
            return true;
        }, IntPtr.Zero);
        return text.ToString();
    }

    static List<IntPtr> Children(IntPtr parent)
    {
        var all = new List<IntPtr>();
        EnumChildWindows(parent, (hwnd, _) => { all.Add(hwnd); return true; }, IntPtr.Zero);
        return all;
    }

    // The file name box: an Edit with id 1148, or the Edit inside the combo box with that id.
    public static IntPtr NameBox(IntPtr dialog)
    {
        foreach (var hwnd in Children(dialog))
        {
            if (GetDlgCtrlID(hwnd) != 1148) continue;
            if (ClassOf(hwnd) == "Edit") return hwnd;
            foreach (var inner in Children(hwnd)) if (ClassOf(inner) == "Edit") return inner;
        }
        return IntPtr.Zero;
    }

    public static string Describe(IntPtr dialog)
    {
        var text = new StringBuilder();
        foreach (var hwnd in Children(dialog)) text.AppendLine("  " + ClassOf(hwnd) + " " + GetDlgCtrlID(hwnd));
        return text.ToString();
    }

    public static string Choose(IntPtr dialog, IntPtr nameBox, string path)
    {
        IntPtr result;
        if (SendMessageTimeout(nameBox, 0x000C /* WM_SETTEXT */, IntPtr.Zero, path, 0x0002, 10000, out result) == IntPtr.Zero)
            throw new TimeoutException("The file name box did not answer.");
        var shown = new StringBuilder(1024);
        SendMessageTimeout(nameBox, 0x000D /* WM_GETTEXT */, (IntPtr)1024, shown, 0x0002, 10000, out result);
        // Open is the dialog's OK command (id 1).
        PostMessage(dialog, 0x0111 /* WM_COMMAND */, (IntPtr)1, GetDlgItem(dialog, 1));
        return shown.ToString();
    }
}
'@
    $until = (Get-Date).AddSeconds(60)
    $dialog = [IntPtr]::Zero
    $nameBox = [IntPtr]::Zero
    while ($nameBox -eq [IntPtr]::Zero -and (Get-Date) -lt $until) {
        Start-Sleep -Seconds 1
        $dialog = [CheckFileDialog]::Find($app.Id)
        if ($dialog -ne [IntPtr]::Zero) { $nameBox = [CheckFileDialog]::NameBox($dialog) }
    }
    if ($dialog -eq [IntPtr]::Zero) {
        Write-Host "ComParta is process $($app.Id); the windows showing:"
        Write-Host ([CheckFileDialog]::DescribeAll())
        throw 'The file dialog did not open.'
    }
    if ($nameBox -eq [IntPtr]::Zero) {
        Write-Host ([CheckFileDialog]::Describe($dialog))
        throw 'The file dialog has no file name box.'
    }
    $typed = [CheckFileDialog]::Choose($dialog, $nameBox, $file)
    Write-Host "picked: $typed"
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
