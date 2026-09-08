[CmdletBinding()]
param(
    [Parameter()]
    [string] $Version = "0.1.0"
)

$ErrorActionPreference = "Stop"
$Version = $Version -replace '^v', ''
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must have three numeric components, for example 0.1.0."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$publishDir = Join-Path $repoRoot "artifacts/publish"
$installerDir = Join-Path $repoRoot "artifacts/installer"
$releaseDir = Join-Path $repoRoot "artifacts/release"

New-Item -ItemType Directory -Path $publishDir, $installerDir, $releaseDir -Force | Out-Null

Push-Location $repoRoot
try {
    & dotnet test VoltronicHidExporter.sln --configuration Release
    if ($LASTEXITCODE -ne 0) { throw "Tests failed." }

    & dotnet publish src/VoltronicHidExporter/VoltronicHidExporter.csproj `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -p:Version=$Version `
        --output $publishDir
    if ($LASTEXITCODE -ne 0) { throw "Windows executable publish failed." }

    & dotnet build installer/VoltronicHidExporter.Installer.wixproj `
        --configuration Release `
        -p:ProductVersion=$Version `
        -p:PublishDir=$publishDir `
        --output $installerDir
    if ($LASTEXITCODE -ne 0) { throw "MSI build failed." }

    $publishedExe = Join-Path $publishDir "voltronic-hid-exporter.exe"
    $builtMsi = Join-Path $installerDir "voltronic-hid-exporter-$Version-win-x64.msi"
    $releaseExe = Join-Path $releaseDir "voltronic-hid-exporter-$Version-win-x64.exe"
    $releaseMsi = Join-Path $releaseDir "voltronic-hid-exporter-$Version-win-x64.msi"
    $checksums = Join-Path $releaseDir "SHA256SUMS"

    Copy-Item $publishedExe $releaseExe -Force
    Copy-Item $builtMsi $releaseMsi -Force

    $checksumLines = foreach ($file in @($releaseMsi, $releaseExe)) {
        $hash = (Get-FileHash -Algorithm SHA256 $file).Hash.ToLowerInvariant()
        "$hash  $(Split-Path $file -Leaf)"
    }
    [System.IO.File]::WriteAllLines(
        $checksums,
        $checksumLines,
        [System.Text.UTF8Encoding]::new($false))

    Write-Host "Release artifacts written to $releaseDir"
}
finally {
    Pop-Location
}
