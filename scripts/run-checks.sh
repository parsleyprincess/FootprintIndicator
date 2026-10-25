#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
export DOTNET_ROOT="${FOOTPRINT_DOTNET_ROOT:-/workspace/.tools/dotnet}"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-/workspace/.tools/dotnet-home}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-/workspace/.tools/nuget}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
mkdir -p "$DOTNET_CLI_HOME" "$NUGET_PACKAGES" artifacts
dotnet build src/Footprint.Core/Footprint.Core.csproj -c Release --nologo
dotnet run --project tests/Footprint.Tests/Footprint.Tests.csproj -c Release
dotnet run --project tools/Footprint.SyntaxCheck/Footprint.SyntaxCheck.csproj -c Release -- src/Footprint.Core/*.cs src/NinjaTrader/FootprintMap.cs
dotnet run --project tools/Footprint.Replay/Footprint.Replay.csproj -c Release -- artifacts/replay-data.json
python3 scripts/build-preview.py
