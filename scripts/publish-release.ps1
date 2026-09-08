[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Version
)

$ErrorActionPreference = "Stop"
$Version = $Version -replace '^v', ''
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must have three numeric components, for example 0.1.0."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$releaseDir = Join-Path $repoRoot "artifacts/release"
$tag = "v$Version"
$assets = @(
    (Join-Path $releaseDir "voltronic-hid-exporter-$Version-win-x64.msi"),
    (Join-Path $releaseDir "voltronic-hid-exporter-$Version-win-x64.exe"),
    (Join-Path $releaseDir "SHA256SUMS")
)

foreach ($asset in $assets) {
    if (-not (Test-Path -LiteralPath $asset -PathType Leaf)) {
        throw "Release asset does not exist: $asset"
    }
}

Push-Location $repoRoot
try {
    & gh release create $tag @assets `
        --generate-notes `
        --title "Voltronic HID Exporter $Version"
    if ($LASTEXITCODE -ne 0) { throw "GitHub release creation failed." }
}
finally {
    Pop-Location
}
