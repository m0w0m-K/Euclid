param(
    [string]$SourcePath = (Join-Path $PSScriptRoot '..')
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path $SourcePath).Path
$failures = New-Object System.Collections.Generic.List[string]

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { $script:failures.Add($Message) }
}

$infoPath = Join-Path $root 'Info.json'
$projectPath = Join-Path $root 'Euclid.csproj'

try {
    $info = Get-Content -LiteralPath $infoPath -Raw | ConvertFrom-Json
} catch {
    $failures.Add("Info.json is not valid JSON: $($_.Exception.Message)")
    $info = $null
}

try {
    [xml]$projectXml = Get-Content -LiteralPath $projectPath -Raw
} catch {
    $failures.Add("Euclid.csproj is not valid XML: $($_.Exception.Message)")
    $projectXml = $null
}

if ($info -ne $null) {
    Assert-True ($info.Id -eq 'Euclid') 'Info.json Id must be Euclid.'
    Assert-True ($info.AssemblyName -eq 'Euclid.dll') 'Info.json AssemblyName must be Euclid.dll.'
    Assert-True ($info.EntryMethod -eq 'Euclid.Startup.Load') 'Info.json EntryMethod must be Euclid.Startup.Load.'
}

$projectText = Get-Content -LiteralPath $projectPath -Raw
$versionMatch = [regex]::Match($projectText, '<Version>([^<]+)</Version>')
Assert-True $versionMatch.Success 'Euclid.csproj must define a <Version>.'
if ($info -ne $null -and $versionMatch.Success) {
    Assert-True ($info.Version -eq $versionMatch.Groups[1].Value) 'Info.json Version and csproj Version must match.'
}

Assert-True (-not ($projectText -match '<PackageReference\b')) 'Standalone Euclid should not require NuGet PackageReference entries.'
Assert-True (-not ($projectText -match 'EditorTabLib')) 'EditorTabLib must not be a project dependency.'
Assert-True (-not ($projectText -match '\bLocalizations\b')) 'The old external Localizations dependency must not be referenced.'
Assert-True ($projectText -match '<Private>false</Private>') 'Game/Unity references should remain non-copy-local.'

$sourceFiles = Get-ChildItem -LiteralPath $root -Filter '*.cs' -File
$sourceText = ($sourceFiles | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join "`n"

Assert-True ($sourceText -match 'label\.text\s*=\s*"Å"') 'Current Euclid tab icon must be Å.'
Assert-True (-not ($sourceText -match 'KsADOFAIEditorTool|KsEditorTool|TileMeasure')) 'Old project branding remains in C# source.'
Assert-True (-not (Test-Path (Join-Path $root 'AdofaiColorFieldBridge.cs'))) 'Unused AdofaiColorFieldBridge.cs should stay removed.'
Assert-True (-not (Test-Path (Join-Path $root 'CameraFrameGui.cs'))) 'Unused CameraFrameGui.cs should stay removed.'
Assert-True (-not (Test-Path (Join-Path $root 'SnapshotGui.cs'))) 'Unused SnapshotGui.cs should stay removed.'
Assert-True (Test-Path (Join-Path $root 'BUILD_RELEASE.cmd')) 'BUILD_RELEASE.cmd is missing.'

$localizationDir = Join-Path $root 'Localization'
$englishPath = Join-Path $localizationDir 'en.lang'

function Get-LangKeys {
    param([string]$Path)
    $keys = @()
    foreach ($line in Get-Content -LiteralPath $Path -Encoding UTF8) {
        if ([string]::IsNullOrWhiteSpace($line) -or $line.TrimStart().StartsWith('#')) { continue }
        $parts = $line -split "`t", 2
        if ($parts.Length -lt 2 -or [string]::IsNullOrWhiteSpace($parts[0])) {
            $script:failures.Add("Invalid localization line in $([IO.Path]::GetFileName($Path)): $line")
            continue
        }
        $keys += $parts[0]
    }
    return $keys
}

if (Test-Path $englishPath) {
    $englishKeys = @(Get-LangKeys $englishPath | Sort-Object -Unique)
    foreach ($lang in Get-ChildItem -LiteralPath $localizationDir -Filter '*.lang' -File) {
        $keys = @(Get-LangKeys $lang.FullName | Sort-Object -Unique)
        $missing = @($englishKeys | Where-Object { $_ -notin $keys })
        $extra = @($keys | Where-Object { $_ -notin $englishKeys })
        if ($missing.Count -gt 0) {
            $failures.Add("$($lang.Name) is missing keys: $($missing -join ', ')")
        }
        if ($extra.Count -gt 0) {
            $failures.Add("$($lang.Name) has extra keys: $($extra -join ', ')")
        }
    }
} else {
    $failures.Add('Localization/en.lang is missing.')
}

if ($failures.Count -gt 0) {
    Write-Host 'Project checks failed:' -ForegroundColor Red
    foreach ($failure in $failures) {
        Write-Host " - $failure" -ForegroundColor Red
    }
    exit 1
}

Write-Host 'Project checks passed.' -ForegroundColor Green
