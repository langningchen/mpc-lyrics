param(
    [Parameter(Mandatory = $true)]
    [string]$ExePath
)

$ErrorActionPreference = 'Stop'
$ExePath = [System.IO.Path]::GetFullPath($ExePath)
if (-not (Test-Path -LiteralPath $ExePath)) {
    throw "MpcLyrics.exe not found: $ExePath"
}

$logDir = Join-Path $env:LOCALAPPDATA 'mpc-lyrics'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$report = Join-Path $logDir 'diagnostic.txt'
$start = Get-Date

"Diagnostic start: $($start.ToString('O'))" | Set-Content -LiteralPath $report -Encoding UTF8
"Executable: $ExePath" | Add-Content -LiteralPath $report -Encoding UTF8

$process = Start-Process -FilePath $ExePath -ArgumentList '--settings' -PassThru
"PID: $($process.Id)" | Add-Content -LiteralPath $report -Encoding UTF8
Start-Sleep -Seconds 8
$process.Refresh()

if ($process.HasExited) {
    "Exited: true" | Add-Content -LiteralPath $report -Encoding UTF8
    # PowerShell 5 treats a direct negative Int32-to-UInt32 conversion as an
    # overflow. Reinterpret the same four bytes so native/CLR failure codes
    # (for example 0xE0434352) do not abort the diagnostic itself.
    $exitCodeHex = [BitConverter]::ToUInt32(
        [BitConverter]::GetBytes([int32]$process.ExitCode),
        0)
    "Exit code: $($process.ExitCode) (0x$('{0:X8}' -f $exitCodeHex))" | Add-Content -LiteralPath $report -Encoding UTF8
} else {
    "Exited: false (still running after 8 seconds)" | Add-Content -LiteralPath $report -Encoding UTF8
}

"" | Add-Content -LiteralPath $report -Encoding UTF8
"Relevant Application event log entries:" | Add-Content -LiteralPath $report -Encoding UTF8
try {
    Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $start.AddSeconds(-2) } -ErrorAction Stop |
        Where-Object {
            $_.ProviderName -in @('.NET Runtime', 'Application Error', 'Windows Error Reporting', 'Microsoft-Windows-AppModel-Runtime') -and
            ($_.Message -match 'MpcLyrics' -or $_.Message -match [string]$process.Id)
        } |
        Select-Object TimeCreated, ProviderName, Id, LevelDisplayName, Message |
        Format-List |
        Out-String -Width 240 |
        Add-Content -LiteralPath $report -Encoding UTF8
} catch {
    "Unable to read Application event log: $($_.Exception.Message)" | Add-Content -LiteralPath $report -Encoding UTF8
}

if (-not $process.HasExited) {
    try {
        $process.CloseMainWindow() | Out-Null
        if (-not $process.WaitForExit(2000)) {
            $process.Kill()
            $process.WaitForExit()
        }
        "Diagnostic process stopped after sampling." | Add-Content -LiteralPath $report -Encoding UTF8
    } catch {
        "Unable to stop diagnostic process: $($_.Exception.Message)" | Add-Content -LiteralPath $report -Encoding UTF8
    }
}

Write-Host "Diagnostic report: $report"
Get-Content -LiteralPath $report
