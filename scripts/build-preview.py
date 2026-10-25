#!/usr/bin/env python3
"""Bundle actual C# replay scenes into a self-contained, explicitly labeled preview."""
import json
from pathlib import Path

root = Path(__file__).resolve().parents[1]
source = root / "artifacts/replay-data.json"
data = json.loads(source.read_text())
if len(data.get("views", [])) != 12:
    raise SystemExit("Expected twelve completed C# replay scenes.")
template = (root / "tools/Footprint.Replay/preview.html").read_text()
# Prevent script-tag termination if a future fixture includes free-form metadata.
payload = json.dumps(data, separators=(",", ":")).replace("<", "\\u003c")
target = root / "artifacts/footprint-preview.html"
target.write_text(template.replace("__REPLAY_JSON__", payload))
print(f"Wrote standalone preview: {target} ({target.stat().st_size:,} bytes)")
