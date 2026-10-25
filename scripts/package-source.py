#!/usr/bin/env python3
"""Build a source distribution, not an invented NinjaTrader import archive."""
from pathlib import Path
import hashlib
import zipfile

root = Path(__file__).resolve().parents[1]
required = [root / 'src/NinjaTrader/FootprintMap.cs', root / 'docs/INSTALL-NINJATRADER.md', root / 'README.md']
for path in required:
    if not path.is_file():
        raise SystemExit(f'Missing required distribution file: {path.relative_to(root)}')
paths = [root / 'README.md', root / 'global.json', root / 'Directory.Build.props']
for name in ['src', 'tests', 'tools', 'scripts', 'docs']:
    paths.extend(p for p in (root / name).rglob('*') if p.is_file()
                 and not any(part in {'bin', 'obj', '__pycache__'} for part in p.relative_to(root).parts))
target = root / 'artifacts/FootprintMap-source.zip'
target.parent.mkdir(exist_ok=True)
with zipfile.ZipFile(target, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
    for path in sorted(set(paths)):
        archive.write(path, 'FootprintIndicator/' + path.relative_to(root).as_posix())
digest = hashlib.sha256(target.read_bytes()).hexdigest()
(target.parent / 'FootprintMap-source.zip.sha256').write_text(digest + '  FootprintMap-source.zip\n')
print(f'{target}: {target.stat().st_size:,} bytes; SHA-256 {digest}')
