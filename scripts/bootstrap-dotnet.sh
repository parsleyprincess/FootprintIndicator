#!/usr/bin/env bash
# Cloud research/test tooling. This does not install NinjaTrader Desktop.
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."

FOOTPRINT_DOTNET_ROOT="${FOOTPRINT_DOTNET_ROOT:-/workspace/.tools/dotnet}"
export FOOTPRINT_DOTNET_ROOT
if [ -x "$FOOTPRINT_DOTNET_ROOT/dotnet" ] && \
   "$FOOTPRINT_DOTNET_ROOT/dotnet" --list-sdks | grep -q '^8\.0\.426 '; then
    "$FOOTPRINT_DOTNET_ROOT/dotnet" --version
    exit 0
fi

python3 - <<'PY'
import hashlib
import os
import platform
import tarfile
import tempfile
import urllib.request
from pathlib import Path

if platform.system() != "Linux" or platform.machine() != "x86_64":
    raise SystemExit("This cloud bootstrap targets Linux x86_64 only.")

# Published Microsoft release metadata:
# https://builds.dotnet.microsoft.com/dotnet/release-metadata/8.0/releases.json
version = "8.0.426"
url = f"https://builds.dotnet.microsoft.com/dotnet/Sdk/{version}/dotnet-sdk-{version}-linux-x64.tar.gz"
expected = "8141d01bdc53f604aed3bb661b604707a64209f8a947cd81bf95b4f518951593d77864069191f54ea5e84362a409da6b0f69074bf893b283d7e0cf93e2bf20a8"
destination = Path(os.environ["FOOTPRINT_DOTNET_ROOT"])
destination.mkdir(parents=True, exist_ok=True)
with tempfile.TemporaryDirectory(prefix="footprint-sdk-") as scratch:
    archive = Path(scratch) / "sdk.tar.gz"
    with urllib.request.urlopen(url, timeout=60) as source, archive.open("wb") as output:
        while chunk := source.read(1024 * 1024):
            output.write(chunk)
    with archive.open("rb") as source:
        actual = hashlib.file_digest(source, "sha512").hexdigest()
    if actual != expected:
        raise SystemExit("Microsoft SDK SHA-512 verification failed; archive was not installed.")
    with tarfile.open(archive) as package:
        package.extractall(destination, filter="data")
print(f"Installed verified .NET SDK {version} at {destination}")
PY
"$FOOTPRINT_DOTNET_ROOT/dotnet" --version
