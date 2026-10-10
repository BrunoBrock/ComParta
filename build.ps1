# Builds ComParta for Windows.
#   .\build.ps1            builds the UI and the app, and runs every test
#   .\build.ps1 -Publish   also produces a folder you can copy to another PC: publish\ComParta
param([switch]$Publish, [switch]$SkipTests)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$framework = 'net10.0-windows10.0.19041.0'

function Invoke-Step([string]$Name, [scriptblock]$Step) {
    Write-Host "== $Name" -ForegroundColor Cyan
    & $Step
    if ($LASTEXITCODE -ne 0) { throw "$Name failed." }
}

Push-Location (Join-Path $root 'src\ui')
try {
    if (-not (Test-Path 'node_modules')) { Invoke-Step 'Install UI packages' { npm ci } }
    if (-not $SkipTests) { Invoke-Step 'UI tests' { npm test } }
    # Writes into src\ComParta.App\Resources\Raw\wwwroot, which the app shows in its window.
    Invoke-Step 'Build UI' { npm run build }
}
finally { Pop-Location }

Push-Location $root
try {
    Invoke-Step 'Build app' { dotnet build ComParta.sln -c Release --nologo -v minimal }
    if (-not $SkipTests) { Invoke-Step 'C# tests' { dotnet test ComParta.sln -c Release --no-build --nologo -v minimal } }

    if ($Publish) {
        $out = Join-Path $root 'publish\ComParta'
        if (Test-Path $out) { Remove-Item $out -Recurse -Force }
        Invoke-Step 'Publish' {
            dotnet publish src\ComParta.App\ComParta.App.csproj -c Release -f $framework -r win-x64 `
                --self-contained true -p:WindowsAppSDKSelfContained=true -o $out --nologo -v minimal
        }
        Write-Host "Ready: $out\ComParta.exe" -ForegroundColor Green
    }
}
finally { Pop-Location }
