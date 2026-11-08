# Publishes a self-contained x64 build and compiles the Inno Setup installer.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root 'artifacts\publish'
$installer = Join-Path $root 'artifacts\installer'

dotnet publish (Join-Path $root 'CorePilot.App\CorePilot.App.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishReadyToRun=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publish

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
if (-not (Test-Path $iscc)) {
    $iscc = Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'
}
if (-not (Test-Path $iscc)) {
    $iscc = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
}
if (-not (Test-Path $iscc)) {
    throw 'Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup'
}

New-Item -ItemType Directory -Force -Path $installer | Out-Null
& $iscc (Join-Path $PSScriptRoot 'CorePilot.iss')

if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE" }

Get-ChildItem $installer -Filter '*.exe' | Select-Object FullName, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
