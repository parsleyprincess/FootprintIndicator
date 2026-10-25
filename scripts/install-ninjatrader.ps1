[CmdletBinding()]
param(
    [string]$CustomDirectory = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'NinjaTrader 8\bin\Custom'),
    [switch]$ReplaceExisting
)
$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'This installer copies source into NinjaTrader Desktop on Windows.'
}
if (Get-Process -Name NinjaTrader -ErrorAction SilentlyContinue) {
    throw 'Close NinjaTrader before installing these source files, then run this script again.'
}
if (-not (Test-Path -LiteralPath $CustomDirectory -PathType Container)) {
    throw "NinjaTrader Custom directory does not exist: $CustomDirectory. Start NinjaTrader once or specify -CustomDirectory with its actual user-data path."
}
$ProjectDirectory = Split-Path -Parent $PSScriptRoot
$CoreDirectory = Join-Path $ProjectDirectory 'src\Footprint.Core'
$IndicatorFile = Join-Path $ProjectDirectory 'src\NinjaTrader\FootprintMap.cs'
$TargetCoreDirectory = Join-Path $CustomDirectory 'Indicators\FootprintMapCore'
$TargetIndicator = Join-Path $CustomDirectory 'Indicators\FootprintMap.cs'
$CoreFiles = @(Get-ChildItem -LiteralPath $CoreDirectory -Filter '*.cs' -File)
if (-not (Test-Path -LiteralPath $IndicatorFile -PathType Leaf)) {
    throw 'The source package is incomplete. Extract the entire source ZIP before installing.'
}
foreach ($RequiredName in @('Model.cs', 'MarketEngine.cs', 'Visualization.cs', 'LiquidityNoiseFilter.cs')) {
    if (-not (Test-Path -LiteralPath (Join-Path $CoreDirectory $RequiredName) -PathType Leaf)) {
        throw "The source package is incomplete: $RequiredName is missing. No files were changed."
    }
}
$Destinations = @($TargetIndicator) + @($CoreFiles | ForEach-Object { Join-Path $TargetCoreDirectory $_.Name })
$Existing = @($Destinations | Where-Object { Test-Path -LiteralPath $_ })
if ($Existing.Count -gt 0 -and -not $ReplaceExisting) {
    throw 'Existing FootprintMap files were found. No files were changed. To back up and replace them, rerun with -ReplaceExisting.'
}
if ($Existing.Count -gt 0) {
    # Backups live outside Custom so NinjaTrader cannot compile duplicate classes.
    $UserDirectory = Split-Path -Parent (Split-Path -Parent $CustomDirectory)
    $BackupDirectory = Join-Path $UserDirectory ('FootprintMap-backups\' + (Get-Date -Format 'yyyyMMdd-HHmmss-ffff'))
    New-Item -ItemType Directory -Path $BackupDirectory | Out-Null
    foreach ($File in $Existing) {
        Copy-Item -LiteralPath $File -Destination (Join-Path $BackupDirectory ([IO.Path]::GetFileName($File)))
    }
    Write-Host "Previous source backed up to $BackupDirectory"
}
New-Item -ItemType Directory -Path $TargetCoreDirectory -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $TargetIndicator) -Force | Out-Null
foreach ($File in $CoreFiles) {
    Copy-Item -LiteralPath $File.FullName -Destination (Join-Path $TargetCoreDirectory $File.Name) -Force
}
Copy-Item -LiteralPath $IndicatorFile -Destination $TargetIndicator -Force
Write-Host 'Source copied. Open NinjaTrader, open the NinjaScript Editor, and compile (F5).'
Write-Host 'Copying source does not prove compilation or runtime compatibility. Follow docs/INSTALL-NINJATRADER.md.'
