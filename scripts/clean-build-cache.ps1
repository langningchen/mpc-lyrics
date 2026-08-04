$ErrorActionPreference = 'Stop'
$path = Join-Path $env:LOCALAPPDATA 'MpcLyricsCSharpBuild'
if (Test-Path -LiteralPath $path) {
    Remove-Item -LiteralPath $path -Recurse -Force
    Write-Host "Removed $path"
} else {
    Write-Host "Nothing to remove: $path"
}
