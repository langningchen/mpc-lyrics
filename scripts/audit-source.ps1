param(
    [Parameter(Mandatory = $true)]
    [string]$SourceProject
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

# Windows PowerShell 5.1 treats UTF-8 files without a BOM as the current ANSI
# code page when Get-Content is used without -Encoding. That corrupts Chinese
# XAML before the XML parser sees it. Read every source file explicitly as
# strict UTF-8 so the audit behaves identically on every Windows locale.
$script:Utf8Strict = New-Object System.Text.UTF8Encoding -ArgumentList $false, $true
$Utf8Console = New-Object System.Text.UTF8Encoding -ArgumentList $false
[Console]::OutputEncoding = $Utf8Console
$OutputEncoding = $Utf8Console

function Read-Utf8Source {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    return [System.IO.File]::ReadAllText($Path, $script:Utf8Strict)
}

function Test-WellFormedXml {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Text
    )

    $Document = New-Object System.Xml.XmlDocument
    $Document.PreserveWhitespace = $true
    $Document.LoadXml($Text)
}

$SourceProject = [System.IO.Path]::GetFullPath($SourceProject).TrimEnd('\')
$SourcePrefix = $SourceProject + '\'
$Errors = @()

$CsFiles = Get-ChildItem -LiteralPath $SourceProject -Filter '*.cs' -Recurse -File
$XamlFiles = Get-ChildItem -LiteralPath $SourceProject -Filter '*.xaml' -Recurse -File
if (-not $CsFiles) { $Errors += "No C# files found under $SourceProject" }
if (-not $XamlFiles) { $Errors += "No XAML files found under $SourceProject" }

foreach ($File in $CsFiles) {
    $Relative = if ($File.FullName.StartsWith($SourcePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        $File.FullName.Substring($SourcePrefix.Length)
    } else {
        $File.FullName
    }

    try {
        $Text = Read-Utf8Source -Path $File.FullName
    } catch {
        $Errors += "$Relative is not valid UTF-8: $($_.Exception.Message)"
        continue
    }

    if ($Text.Contains([char]0xFFFD)) {
        $Errors += "$Relative contains the Unicode replacement character U+FFFD."
    }

    # WinRT XAML controls such as Border, StackPanel, Grid, and Canvas are sealed.
    if ($Text -match '(?m)^\s*(?:public|internal|private|protected)?\s*(?:sealed\s+)?class\s+\w+\s*:\s*(Border|StackPanel|Grid|Canvas)\b') {
        $Errors += "$Relative derives from sealed WinUI control '$($Matches[1])'."
    }

    # The rewritten settings UI must stay declarative. Runtime-created framework
    # controls would bypass XAML compilation and reintroduce the old failure mode.
    if ($Relative -like 'UI\SettingsWindow*' -and $Text -match '\bnew\s+(NavigationView|NavigationViewItem|ColorPicker|ContentDialog|Flyout|Popup)\b') {
        $Errors += "$Relative dynamically creates '$($Matches[1])'; use the XAML-declared control instead."
    }

    if ($Text -match 'ColorSourceMode\.Inverse|CreateBlurredBackdrop|InvertOpaqueBitmap|CaptureBackdrop|\bBitBlt\s*\(') {
        $Errors += "$Relative reintroduces removed software inverse, blur, or backdrop capture code."
    }
}

foreach ($File in $XamlFiles) {
    $Relative = if ($File.FullName.StartsWith($SourcePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        $File.FullName.Substring($SourcePrefix.Length)
    } else {
        $File.FullName
    }

    try {
        $Text = Read-Utf8Source -Path $File.FullName
    } catch {
        $Errors += "$Relative is not valid UTF-8: $($_.Exception.Message)"
        continue
    }

    if ($Text.Contains([char]0xFFFD)) {
        $Errors += "$Relative contains the Unicode replacement character U+FFFD."
    }

    try {
        Test-WellFormedXml -Text $Text
    } catch {
        $Errors += "$Relative is not well-formed XML: $($_.Exception.Message)"
    }
}

$SettingsXamlPath = Join-Path $SourceProject 'UI\SettingsWindow.xaml'
$SettingsCodePath = Join-Path $SourceProject 'UI\SettingsWindow.xaml.cs'
$TextStyleXamlPath = Join-Path $SourceProject 'UI\TextStyleEditor.xaml'
$AppXamlPath = Join-Path $SourceProject 'App.xaml'

if (-not (Test-Path -LiteralPath $SettingsXamlPath)) {
    $Errors += 'UI\SettingsWindow.xaml is missing.'
}
if (-not (Test-Path -LiteralPath $SettingsCodePath)) {
    $Errors += 'UI\SettingsWindow.xaml.cs is missing.'
}

if ((Test-Path -LiteralPath $SettingsXamlPath) -and (Test-Path -LiteralPath $SettingsCodePath)) {
    try {
        $XamlText = Read-Utf8Source -Path $SettingsXamlPath
        $CodeText = Read-Utf8Source -Path $SettingsCodePath
    } catch {
        $Errors += "Unable to read SettingsWindow sources as UTF-8: $($_.Exception.Message)"
        $XamlText = ''
        $CodeText = ''
    }

    if ($XamlText -match '<NavigationView\b') {
        $Errors += 'SettingsWindow.xaml must remain a compact settings window without NavigationView.'
    }
    $ColorPickerCount = ([regex]::Matches($XamlText, '<ColorPicker\b')).Count
    if ($ColorPickerCount -ne 1) {
        $Errors += "SettingsWindow.xaml must declare one background ColorPicker; found $ColorPickerCount."
    }
    if ($XamlText -notmatch 'x:Name="BackgroundColorModeCombo"' -or
        $XamlText -notmatch 'x:Name="AcrylicToggle"') {
        $Errors += 'SettingsWindow.xaml must retain dynamic background colors and system acrylic.'
    }
    if ($XamlText -match '反色|AcrylicBlurSlider') {
        $Errors += 'SettingsWindow.xaml must not expose removed software inverse or blur controls.'
    }
    $FlyoutCount = ([regex]::Matches($XamlText, '<Flyout\b')).Count
    if ($FlyoutCount -ne 9) {
        $Errors += "SettingsWindow.xaml must declare five category flyouts and four text-style flyouts; found $FlyoutCount."
    }
    if ($CodeText -notmatch 'presenter\.SetBorderAndTitleBar\s*\(\s*true\s*,\s*true\s*\)') {
        $Errors += 'SettingsWindow.xaml.cs must use the standard Windows border and title bar.'
    }
    if (($CodeText -notmatch 'presenter\.IsResizable\s*=\s*false') -or
        ($CodeText -notmatch 'presenter\.IsMaximizable\s*=\s*false')) {
        $Errors += 'SettingsWindow.xaml.cs must disable resizing and maximizing.'
    }
    if ($CodeText -notmatch 'WS_MAXIMIZEBOX' -or
        $CodeText -notmatch 'SWP_FRAMECHANGED' -or
        $CodeText -notmatch 'ExerciseFixedWindowCapabilitiesForSmokeTest') {
        $Errors += 'SettingsWindow.xaml.cs must remove native maximize styles and test the resulting window behavior.'
    }
    if ($CodeText -match 'ExtendsContentIntoTitleBar\s*=\s*true') {
        $Errors += 'SettingsWindow.xaml.cs must leave title-bar dragging to Windows.'
    }
    if ($XamlText -match 'IsSelected="True"') {
        $Errors += 'NavigationView selection must not run during XAML parsing.'
    }
    if ($XamlText -match '<ContentDialog\b|<Popup\b|<Expander\b') {
        $Errors += 'SettingsWindow.xaml must use anchored Flyout editors, without dialogs, raw popups, or expanders.'
    }
    if ($CodeText -notmatch 'public\s+sealed\s+partial\s+class\s+SettingsWindow\s*:\s*Window') {
        $Errors += 'SettingsWindow.xaml.cs must declare a sealed partial Window class.'
    }
    if ($CodeText -notmatch '\bInitializeComponent\s*\(') {
        $Errors += 'SettingsWindow.xaml.cs does not call InitializeComponent().'
    }

    $Names = [regex]::Matches($XamlText, 'x:Name="([A-Za-z_][A-Za-z0-9_]*)"') |
        ForEach-Object { $_.Groups[1].Value }
    $DuplicateNames = $Names | Group-Object | Where-Object Count -gt 1
    foreach ($Duplicate in $DuplicateNames) {
        $Errors += "SettingsWindow.xaml contains duplicate x:Name '$($Duplicate.Name)'."
    }

    $EventPattern = '\b(?:Loaded|SizeChanged|ItemInvoked|Toggled|TextChanged|ValueChanged|SelectionChanged|ColorChanged|Click|Opening|Opened)="([A-Za-z_][A-Za-z0-9_]*)"'
    $Handlers = [regex]::Matches($XamlText, $EventPattern) |
        ForEach-Object { $_.Groups[1].Value } |
        Sort-Object -Unique
    foreach ($Handler in $Handlers) {
        if ($CodeText -notmatch "\b$([regex]::Escape($Handler))\s*\(") {
            $Errors += "XAML event handler '$Handler' is missing from SettingsWindow.xaml.cs."
        }
    }

    # StaticResource failures are reported only at XAML load time. Ensure every
    # app-defined reference in this window has a key in the same XAML file.
    $DefinedKeys = [regex]::Matches($XamlText, 'x:Key="([^"]+)"') |
        ForEach-Object { $_.Groups[1].Value } |
        Sort-Object -Unique
    $StaticKeys = [regex]::Matches($XamlText, '\{StaticResource\s+([^}]+)\}') |
        ForEach-Object { $_.Groups[1].Value.Trim() } |
        Sort-Object -Unique
    foreach ($StaticKey in $StaticKeys) {
        if ($DefinedKeys -notcontains $StaticKey) {
            $Errors += "SettingsWindow.xaml references unresolved local StaticResource '$StaticKey'."
        }
    }
}

if (-not (Test-Path -LiteralPath $TextStyleXamlPath)) {
    $Errors += 'UI\TextStyleEditor.xaml is missing.'
} else {
    try {
        $TextStyleXaml = Read-Utf8Source -Path $TextStyleXamlPath
        $TextStyleColorPickerCount = ([regex]::Matches($TextStyleXaml, '<ColorPicker\b')).Count
        if ($TextStyleColorPickerCount -ne 2) {
            $Errors += "TextStyleEditor.xaml must declare outline and fill ColorPickers; found $TextStyleColorPickerCount."
        }
        $ColorModeOptionCount = ([regex]::Matches($TextStyleXaml, '<ComboBoxItem\b')).Count
        if ($TextStyleXaml -notmatch 'x:Name="OutlineColorModeCombo"' -or
            $TextStyleXaml -notmatch 'x:Name="FillColorModeCombo"' -or
            $ColorModeOptionCount -ne 4) {
            $Errors += 'TextStyleEditor.xaml must expose custom and system-accent modes for both colors.'
        }
        if ($TextStyleXaml -match 'IsAlphaEnabled="True"') {
            $Errors += 'TextStyleEditor special colors must use the independent opacity sliders.'
        }
    } catch {
        $Errors += "Unable to audit TextStyleEditor.xaml: $($_.Exception.Message)"
    }
}

if (Test-Path -LiteralPath $AppXamlPath) {
    try {
        $AppXamlText = Read-Utf8Source -Path $AppXamlPath
        if ($AppXamlText -notmatch '<XamlControlsResources\b') {
            $Errors += 'App.xaml must merge XamlControlsResources for native WinUI styles.'
        }
    } catch {
        $Errors += "App.xaml is not valid UTF-8: $($_.Exception.Message)"
    }
} else {
    $Errors += 'App.xaml is missing.'
}

$ProgramPath = Join-Path $SourceProject 'Program.cs'
if (Test-Path -LiteralPath $ProgramPath) {
    $Errors += 'Program.cs must not replace the WinUI-generated entry point.'
}

$ProjectPath = Join-Path $SourceProject 'MpcLyrics.csproj'
if (-not (Test-Path -LiteralPath $ProjectPath)) {
    $Errors += 'MpcLyrics.csproj is missing.'
} else {
    try {
        $ProjectText = Read-Utf8Source -Path $ProjectPath
        Test-WellFormedXml -Text $ProjectText
        if ($ProjectText -match 'DISABLE_XAML_GENERATED_MAIN') {
            $Errors += 'MpcLyrics.csproj must use the standard WinUI-generated entry point.'
        }
        if ($ProjectText -notmatch '<MicrosoftWindowsAppSDKVersion>1\.8\.260710003</MicrosoftWindowsAppSDKVersion>' -and
            $ProjectText -notmatch 'PackageReference\s+Include="Microsoft\.WindowsAppSDK"\s+Version="1\.8\.260710003"') {
            $Errors += 'MpcLyrics.csproj must pin Microsoft.WindowsAppSDK 1.8.260710003.'
        }
        if ($ProjectText -notmatch 'PackageReference\s+Include="Microsoft\.Windows\.SDK\.BuildTools"\s+Version="10\.0\.26100\.7705"') {
            $Errors += 'MpcLyrics.csproj must pin Microsoft.Windows.SDK.BuildTools 10.0.26100.7705.'
        }
        if ($ProjectText -notmatch '<TargetPlatformMinVersion>10\.0\.19041\.0</TargetPlatformMinVersion>') {
            $Errors += 'MpcLyrics.csproj must target Windows 10 2004 or later.'
        }
        if ($ProjectText -notmatch '<WindowsAppSdkUndockedRegFreeWinRTInitialize>true</WindowsAppSdkUndockedRegFreeWinRTInitialize>') {
            $Errors += 'MpcLyrics.csproj must enable reg-free WinRT activation for its self-contained unpackaged runtime.'
        }
        if ($ProjectText -notmatch '<PackageLicenseExpression>AGPL-3\.0-only</PackageLicenseExpression>') {
            $Errors += 'MpcLyrics.csproj must declare the AGPL-3.0-only license expression.'
        }
    } catch {
        $Errors += "MpcLyrics.csproj is not valid UTF-8 XML: $($_.Exception.Message)"
    }
}

$RepositoryRoot = Split-Path -Parent (Split-Path -Parent $SourceProject)
$LicensePath = Join-Path $RepositoryRoot 'LICENSE'
if (-not (Test-Path -LiteralPath $LicensePath)) {
    $Errors += 'The repository LICENSE file is missing.'
} else {
    try {
        $LicenseText = Read-Utf8Source -Path $LicensePath
        if ($LicenseText -notmatch 'GNU AFFERO GENERAL PUBLIC LICENSE' -or
            $LicenseText -notmatch 'Version 3, 19 November 2007') {
            $Errors += 'LICENSE must contain the GNU Affero General Public License version 3 text.'
        }
    } catch {
        $Errors += "LICENSE is not valid UTF-8: $($_.Exception.Message)"
    }
}

if ($Errors.Count -gt 0) {
    Write-Host 'Source audit failed:' -ForegroundColor Red
    foreach ($ErrorText in $Errors) {
        Write-Host "  - $ErrorText" -ForegroundColor Red
    }
    throw "Source audit found $($Errors.Count) problem(s)."
}

Write-Host "Source audit passed: $($CsFiles.Count) C# files and $($XamlFiles.Count) XAML files checked."
