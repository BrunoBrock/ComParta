# Builds, tests and publishes ComParta for Windows.
#   .\build.ps1                   asks what you want to do
#   .\build.ps1 Publish           builds and tests, then creates publish\ComParta: a folder you can run here
#                                 or copy to another PC
#   .\build.ps1 Package           builds and tests, then creates publish\package\ComParta_<version>_x64.msix,
#                                 signed with a test certificate made on this PC
#   .\build.ps1 Build|Test|Check  see $actions below
#   -SkipTests                    leaves the tests out of Publish and Check
#   -Run                          starts the published ComParta.exe once Publish succeeds
#   -Version 1.2.3                the package's version; CI passes the git tag's
# Without a person at the keyboard (CI, piped input) it does Check instead of asking.
param(
    [ValidateSet('Publish', 'Package', 'Build', 'Test', 'Check')]
    [string]$Action,
    [string]$Version = '0.1.0',
    [switch]$SkipTests,
    [switch]$Run
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$framework = 'net10.0-windows10.0.19041.0'
$publishDir = Join-Path $root 'publish\ComParta'
$packageDir = Join-Path $root 'publish\package'
# Must match the Publisher in src\ComParta.App\Platforms\Windows\Package.appxmanifest.
$packagePublisher = 'CN=ComParta'

# What the menu offers, in order. The first one is the default. To add an option: add it here and to
# the ValidateSet above, then give it a branch in the switch at the bottom.
$actions = [ordered]@{
    Publish = 'Build and test, then create publish\ComParta, a folder to run here or copy to another PC'
    Package = 'Build and test, then create an MSIX package in publish\package, signed for testing'
    Build   = 'Build the screen and the app, without running any tests'
    Test    = 'Run every test'
    Check   = 'Build and run every test (what CI does)'
}

function Invoke-Step([string]$Name, [string]$In, [scriptblock]$Step) {
    Write-Host "== $Name" -ForegroundColor Cyan
    Push-Location (Join-Path $root $In)
    try {
        & $Step
        if ($LASTEXITCODE -ne 0) { throw "$Name failed." }
    }
    finally { Pop-Location }
}

# Fails before any work starts, saying what to install.
function Assert-Prerequisites {
    $nodeVersion = [version](& node --version).TrimStart('v')
    $nodeOk = ($nodeVersion.Major -eq 20 -and $nodeVersion -ge [version]'20.19.0') -or
              ($nodeVersion.Major -eq 22 -and $nodeVersion -ge [version]'22.12.0') -or
              ($nodeVersion.Major -gt 22)
    if (-not $nodeOk) {
        throw "Node $nodeVersion is too old. The UI needs Node 20.19+ or 22.12+ (install the LTS from https://nodejs.org)."
    }

    if (-not ((& dotnet workload list) -match 'maui')) {
        throw "The .NET MAUI workload is not installed. Run this in an administrator PowerShell: dotnet workload install maui-windows"
    }
}

# Installs when the packages are missing or older than package-lock.json (a pull that added a package),
# so tests do not fail on an import that is not on disk yet. npm writes node_modules\.package-lock.json
# on every install.
function Install-UiPackages {
    $installed = Join-Path $root 'src\ui\node_modules\.package-lock.json'
    $wanted = Join-Path $root 'src\ui\package-lock.json'
    $upToDate = (Test-Path $installed) -and
                ((Get-Item $installed).LastWriteTimeUtc -ge (Get-Item $wanted).LastWriteTimeUtc)
    if (-not $upToDate) { Invoke-Step 'Install UI packages' 'src\ui' { npm ci } }
}

function Test-Ui {
    Install-UiPackages
    Invoke-Step 'UI tests' 'src\ui' { npm test }
}

# Writes into src\ComParta.App\Resources\Raw\wwwroot, which the app shows in its window.
function Build-Ui {
    Install-UiPackages
    Invoke-Step 'Build UI' 'src\ui' { npm run build }
}

function Build-App { Invoke-Step 'Build app' '.' { dotnet build ComParta.sln -c Release --nologo -v minimal } }

function Test-App { Invoke-Step 'C# tests' '.' { dotnet test ComParta.sln -c Release --nologo -v minimal } }

# Windows will not let the publish folder be replaced while the app is running from it.
function Assert-PublishFolderFree {
    $running = Get-Process ComParta -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($publishDir, [StringComparison]::OrdinalIgnoreCase) }
    if ($running) { throw "ComParta is running from $publishDir. Close it, then publish again." }
}

function Publish-App {
    Assert-PublishFolderFree
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    Invoke-Step 'Publish' '.' {
        dotnet publish src\ComParta.App\ComParta.App.csproj -c Release -f $framework -r win-x64 `
            --self-contained true -p:WindowsAppSDKSelfContained=true -o $publishDir --nologo -v minimal
    }
    Write-Host "Ready: $publishDir\ComParta.exe" -ForegroundColor Green
    if ($Run) {
        Write-Host '== Run' -ForegroundColor Cyan
        Start-Process -FilePath (Join-Path $publishDir 'ComParta.exe') -WorkingDirectory $publishDir
    }
}

# A certificate to sign test packages, made once and kept in this user's store. Windows installs a
# package signed with it only after the certificate is trusted (see .github\scripts\check-package.ps1).
function Get-TestCertificate {
    $existing = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Subject -eq $packagePublisher -and $_.NotAfter -gt (Get-Date).AddDays(1) -and $_.HasPrivateKey } |
        Select-Object -First 1
    if ($existing) { return $existing }
    New-SelfSignedCertificate -Type Custom -Subject $packagePublisher -KeyUsage DigitalSignature `
        -FriendlyName 'ComParta test packages' -CertStoreLocation Cert:\CurrentUser\My `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
}

function Package-App {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "The version must look like 1.2.3, not $Version." }
    $certificate = Get-TestCertificate
    if (Test-Path $packageDir) { Remove-Item $packageDir -Recurse -Force }
    Invoke-Step 'Package' '.' {
        dotnet publish src\ComParta.App\ComParta.App.csproj -c Release -f $framework -r win-x64 --self-contained true `
            -p:WindowsPackageType=MSIX -p:WindowsAppSDKSelfContained=true -p:GenerateAppxPackageOnBuild=true `
            -p:AppxPackageSigningEnabled=true -p:PackageCertificateThumbprint=$($certificate.Thumbprint) `
            -p:ApplicationDisplayVersion=$Version -p:ApplicationVersion=0 `
            "-p:AppxPackageDir=$packageDir\\" --nologo -v minimal
    }
    $package = Get-ChildItem $packageDir -Recurse -Filter *_x64.msix | Select-Object -First 1
    if (-not $package) { throw "No .msix was made in $packageDir." }
    $target = Join-Path $packageDir "ComParta_$($Version)_x64.msix"
    Move-Item $package.FullName $target -Force
    Export-Certificate -Cert $certificate -FilePath (Join-Path $packageDir 'ComParta-test.cer') | Out-Null
    Write-Host "Ready: $target" -ForegroundColor Green
}

function Select-Action {
    $names = @($actions.Keys)
    Write-Host 'What do you want to do?' -ForegroundColor Cyan
    for ($i = 0; $i -lt $names.Count; $i++) {
        Write-Host ('  {0}) {1,-8} {2}' -f ($i + 1), $names[$i], $actions[$names[$i]])
    }
    while ($true) {
        $answer = "$(Read-Host "Choose 1-$($names.Count) [1]")".Trim()
        if ($answer -eq '') { return $names[0] }
        if ($answer -match '^\d+$' -and [int]$answer -ge 1 -and [int]$answer -le $names.Count) { return $names[[int]$answer - 1] }
        $byName = $names | Where-Object { $_ -ieq $answer }
        if ($byName) { return $byName }
        Write-Host "Not an option: $answer" -ForegroundColor Yellow
    }
}

if (-not $Action) {
    $canAsk = [Environment]::UserInteractive -and -not [Console]::IsInputRedirected -and -not $env:CI
    $Action = if ($canAsk) { Select-Action } else { 'Check' }
}
if ($Action -eq 'Test' -and $SkipTests) { throw 'Nothing to do: Test with -SkipTests.' }
if ($Run -and $Action -ne 'Publish') { throw '-Run only works with Publish.' }
Assert-Prerequisites

switch ($Action) {
    'Publish' {
        Assert-PublishFolderFree   # before the tests, so a locked folder does not cost minutes first
        if (-not $SkipTests) { Test-Ui }
        Build-Ui
        if (-not $SkipTests) { Test-App }
        Publish-App   # builds the app itself, so there is no separate Build-App here
    }
    'Package' {
        if (-not $SkipTests) { Test-Ui }
        Build-Ui
        if (-not $SkipTests) { Test-App }
        Package-App
    }
    'Build' { Build-Ui; Build-App }
    'Test' { Test-Ui; Test-App }
    'Check' {
        if (-not $SkipTests) { Test-Ui }
        Build-Ui
        Build-App
        if (-not $SkipTests) { Test-App }
    }
}
