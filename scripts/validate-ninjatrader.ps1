[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$NinjaTraderBin,
    [string]$NinjaTraderCustom = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'NinjaTrader 8\bin\Custom')
)
$ErrorActionPreference = 'Stop'
foreach ($Name in @('NinjaTrader.Core.dll', 'NinjaTrader.Gui.dll', 'SharpDX.dll', 'SharpDX.Direct2D1.dll', 'SharpDX.DirectWrite.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $NinjaTraderBin $Name) -PathType Leaf)) { throw "Missing installed reference: $Name" }
}
if (-not (Test-Path -LiteralPath (Join-Path $NinjaTraderCustom 'NinjaTrader.Custom.dll') -PathType Leaf)) {
    throw 'NinjaTrader.Custom.dll is missing. Compile existing NinjaScripts in NinjaTrader first.'
}
$ProjectDirectory = Split-Path -Parent $PSScriptRoot
$CoreVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $NinjaTraderBin 'NinjaTrader.Core.dll')).FileVersion
Write-Host "Installed NinjaTrader.Core file version: $CoreVersion"
$Project = Join-Path $ProjectDirectory 'src\Footprint.NinjaTrader.Validation\Footprint.NinjaTrader.Validation.csproj'
& dotnet build $Project -c Release "-p:NinjaTraderBin=$NinjaTraderBin" "-p:NinjaTraderCustom=$NinjaTraderCustom"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host 'External reference compilation passed. NinjaScript Editor compilation and chart/replay tests are still required.'
