param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectRoot
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

$ProjectRoot = [System.IO.Path]::GetFullPath($ProjectRoot)
$CacheRoot = Join-Path $env:LOCALAPPDATA 'MpcLyricsCSharpBuild'
$LegacyDotnet = Join-Path $env:LOCALAPPDATA 'MpcLyricsBuild\dotnet\dotnet.exe'
$DotnetRoot = Join-Path $CacheRoot 'dotnet'
$DotnetExe = Join-Path $DotnetRoot 'dotnet.exe'
$CliHome = Join-Path $CacheRoot 'dotnet-home'
$NugetPackages = Join-Path $CacheRoot 'nuget-packages'
$NugetHttpCache = Join-Path $CacheRoot 'nuget-http-cache'
$NugetPluginsCache = Join-Path $CacheRoot 'nuget-plugins-cache'
$Workspace = Join-Path $CacheRoot 'workspace'
$WindowsRunDir = Join-Path $CacheRoot 'run'
$SourceProject = Join-Path $ProjectRoot 'src\MpcLyrics'
$LicenseFile = Join-Path $ProjectRoot 'LICENSE'
$BuildProject = Join-Path $Workspace 'MpcLyrics'
$PublishDir = Join-Path $Workspace 'publish'
$Destination = Join-Path $ProjectRoot 'dist'

if (-not (Test-Path -LiteralPath $SourceProject)) {
    throw "Source project not found: $SourceProject"
}
if (-not (Test-Path -LiteralPath $LicenseFile)) {
    throw "License file not found: $LicenseFile"
}

$AuditScript = Join-Path $ProjectRoot 'scripts\audit-source.ps1'
if (-not (Test-Path -LiteralPath $AuditScript)) {
    throw "Source audit script not found: $AuditScript"
}
Write-Host '[0/5] Auditing source invariants...'
& $AuditScript -SourceProject $SourceProject

New-Item -ItemType Directory -Force -Path `
    $CacheRoot, $DotnetRoot, $CliHome, $NugetPackages, $NugetHttpCache, $NugetPluginsCache | Out-Null

if (Test-Path -LiteralPath $LegacyDotnet) {
    $DotnetExe = $LegacyDotnet
    $DotnetRoot = Split-Path -Parent $LegacyDotnet
    Write-Host "[1/5] Reusing portable .NET SDK: $DotnetExe"
}
elseif (-not (Test-Path -LiteralPath $DotnetExe)) {
    Write-Host '[1/5] Installing a portable .NET 10 SDK under LocalAppData...'
    $InstallScript = Join-Path $CacheRoot 'dotnet-install.ps1'
    Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -UseBasicParsing -OutFile $InstallScript
    & $InstallScript -Channel '10.0' -Quality 'GA' -Architecture 'x64' -InstallDir $DotnetRoot -NoPath
    if (-not $? -or -not (Test-Path -LiteralPath $DotnetExe)) {
        throw "dotnet-install.ps1 did not produce $DotnetExe"
    }
} else {
    Write-Host "[1/5] Reusing portable .NET SDK: $DotnetExe"
}

$env:DOTNET_ROOT = $DotnetRoot
$env:DOTNET_CLI_HOME = $CliHome
$env:NUGET_PACKAGES = $NugetPackages
$env:NUGET_HTTP_CACHE_PATH = $NugetHttpCache
$env:NUGET_PLUGINS_CACHE_PATH = $NugetPluginsCache
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

& $DotnetExe --version
if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }

Write-Host '[2/5] Staging the project on the Windows filesystem...'
Remove-Item -LiteralPath $Workspace -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $Workspace | Out-Null
Copy-Item -LiteralPath $SourceProject -Destination $BuildProject -Recurse -Force
Copy-Item -LiteralPath $LicenseFile -Destination (Join-Path $BuildProject 'LICENSE') -Force

$Csproj = Join-Path $BuildProject 'MpcLyrics.csproj'
$NugetConfig = Join-Path $BuildProject 'NuGet.Config'
if (-not (Test-Path -LiteralPath $NugetConfig)) {
    throw "NuGet.Config was not staged: $NugetConfig"
}

Write-Host '[3/5] Restoring Windows App SDK packages from nuget.org...'
& $DotnetExe restore $Csproj `
    --runtime win-x64 `
    --configfile $NugetConfig `
    --force `
    --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE" }

Write-Host '[4/5] Publishing the self-contained C# application...'
Remove-Item -LiteralPath $PublishDir -Recurse -Force -ErrorAction SilentlyContinue
& $DotnetExe publish $Csproj `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --no-restore `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    --output $PublishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

# dotnet publish does not copy WinUI page XBF files for this unpackaged SDK
# layout, even though the XAML compiler emits them under bin. LoadComponent
# resolves pages through ms-appx, so preserve their relative paths explicitly.
$XbfRoot = Get-ChildItem -LiteralPath (Join-Path $BuildProject 'bin\Release') `
    -Filter 'App.xbf' -Recurse -File |
    Select-Object -First 1 -ExpandProperty DirectoryName
if ([string]::IsNullOrWhiteSpace($XbfRoot)) {
    throw 'The WinUI compiler did not produce App.xbf.'
}
Get-ChildItem -LiteralPath $XbfRoot -Filter '*.xbf' -Recurse -File | ForEach-Object {
    $RelativeXbf = $_.FullName.Substring($XbfRoot.Length).TrimStart('\')
    $PublishedXbf = Join-Path $PublishDir $RelativeXbf
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $PublishedXbf) | Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $PublishedXbf -Force
}

$AppPri = Join-Path $XbfRoot 'MpcLyrics.pri'
if (-not (Test-Path -LiteralPath $AppPri)) {
    throw "The WinUI resource index is missing: $AppPri"
}
Copy-Item -LiteralPath $AppPri -Destination (Join-Path $PublishDir 'MpcLyrics.pri') -Force

$SettingsXbf = Join-Path $PublishDir 'UI\SettingsWindow.xbf'
if (-not (Test-Path -LiteralPath $SettingsXbf)) {
    throw "Published WinUI page is missing: $SettingsXbf"
}
$PublishedLicense = Join-Path $PublishDir 'LICENSE'
if (-not (Test-Path -LiteralPath $PublishedLicense)) {
    throw "Published AGPL license is missing: $PublishedLicense"
}

Remove-Item -LiteralPath $Destination -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $Destination | Out-Null
Copy-Item -Path (Join-Path $PublishDir '*') -Destination $Destination -Recurse -Force

$DestinationExe = Join-Path $Destination 'MpcLyrics.exe'
if (-not (Test-Path -LiteralPath $DestinationExe)) {
    throw "Publish completed but MpcLyrics.exe was not found in $Destination"
}

# WinUI's registration-free runtime activation is not reliable when an EXE is
# launched directly from WSL's \\wsl.localhost UNC share. Keep dist as the
# portable package, and deploy a byte-for-byte runnable copy to an NTFS path.
Remove-Item -LiteralPath $WindowsRunDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $WindowsRunDir | Out-Null
Copy-Item -Path (Join-Path $PublishDir '*') -Destination $WindowsRunDir -Recurse -Force
$Exe = Join-Path $WindowsRunDir 'MpcLyrics.exe'

Write-Host '[5/5] Running the WinUI settings-window smoke test...'
$StartupLog = Join-Path $env:LOCALAPPDATA 'mpc-lyrics\startup.log'
$CrashLog = Join-Path $env:LOCALAPPDATA 'mpc-lyrics\crash.log'
$StartupBefore = if (Test-Path -LiteralPath $StartupLog) {
    ([System.IO.File]::ReadAllText($StartupLog)).Length
} else {
    0
}
$CrashBefore = if (Test-Path -LiteralPath $CrashLog) {
    ([System.IO.File]::ReadAllText($CrashLog)).Length
} else {
    0
}

$PreviousSmokeValue = $env:MPC_LYRICS_SMOKE_TEST
try {
    $env:MPC_LYRICS_SMOKE_TEST = '1'
    $Process = Start-Process -FilePath $Exe -WorkingDirectory $WindowsRunDir -PassThru
    if (-not $Process.WaitForExit(15000)) {
        try { $Process.Kill() } catch { }
        throw 'The smoke-test process did not exit within 15 seconds.'
    }
    $SmokeExitCode = $Process.ExitCode
} finally {
    if ($null -eq $PreviousSmokeValue) {
        Remove-Item Env:MPC_LYRICS_SMOKE_TEST -ErrorAction SilentlyContinue
    } else {
        $env:MPC_LYRICS_SMOKE_TEST = $PreviousSmokeValue
    }
}

$StartupTail = if (Test-Path -LiteralPath $StartupLog) {
    $All = [System.IO.File]::ReadAllText($StartupLog)
    if ($StartupBefore -lt $All.Length) { $All.Substring([int]$StartupBefore) } else { '' }
} else {
    ''
}
$CrashTail = if (Test-Path -LiteralPath $CrashLog) {
    $All = [System.IO.File]::ReadAllText($CrashLog)
    if ($CrashBefore -lt $All.Length) { $All.Substring([int]$CrashBefore) } else { '' }
} else {
    ''
}

if ($SmokeExitCode -ne 0 -or $StartupTail -notmatch 'SMOKE_TEST: PASS') {
    Write-Host '--- smoke-test startup log ---' -ForegroundColor Yellow
    Write-Host $StartupTail
    if (-not [string]::IsNullOrWhiteSpace($CrashTail)) {
        Write-Host '--- smoke-test crash log ---' -ForegroundColor Yellow
        Write-Host $CrashTail
    }
    throw "WinUI smoke test failed with exit code $SmokeExitCode"
}

Write-Host 'WinUI smoke test passed.' -ForegroundColor Green
Write-Host ''
Write-Host "Portable package: $Destination"
Write-Host "Runnable Windows copy: $Exe"
