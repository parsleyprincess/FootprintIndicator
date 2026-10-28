#!/usr/bin/env python3
"""Check the standalone browser preview; no external sites or user-facing server."""
import functools
import http.server
import itertools
import json
from pathlib import Path
import shutil
import threading
from playwright.sync_api import sync_playwright

root = Path(__file__).resolve().parents[1]
artifacts = root / 'artifacts'

class QuietHandler(http.server.SimpleHTTPRequestHandler):
    def log_message(self, *_):
        pass

handler = functools.partial(QuietHandler, directory=str(artifacts))
server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), handler)
thread = threading.Thread(target=server.serve_forever, daemon=True)
thread.start()
errors = []
states = 0
try:
    with sync_playwright() as browser_driver:
        chromium = shutil.which('chromium') or shutil.which('chromium-browser')
        browser = browser_driver.chromium.launch(executable_path=chromium, headless=True)
        page = browser.new_page(viewport={'width': 1520, 'height': 1100}, device_scale_factor=1)
        page.on('pageerror', lambda error: errors.append(str(error)))
        page.goto(f'http://127.0.0.1:{server.server_port}/footprint-preview.html', wait_until='load')
        page.wait_for_function('window.previewReady === true')
        for time_range, liquidity, cluster, palette, style in itertools.product(
            ['120', '30'], ['False', 'True', 'Following'], ['Smart', 'Pixel'],
            ['Classic', 'Grayscale', 'Ice'], ['3d', '2d', 'pie']):
            for selector, value in [('#range', time_range), ('#filter', liquidity), ('#cluster', cluster), ('#palette', palette), ('#style', style)]:
                page.select_option(selector, value)
            if '1,872 trades' not in page.locator('#status').inner_text():
                raise AssertionError('Display controls changed the accepted trade count.')
            states += 1
        for time_range, study in itertools.product(['120', '30'], ['none', 'absorption', 'sweeps', 'large', 'refill', 'levels', 'footprint', 'tpo']):
            page.select_option('#range', time_range)
            page.select_option('#study', study)
            if '1,872 trades' not in page.locator('#status').inner_text():
                raise AssertionError('Study display changed source execution accounting.')
            states += 1
        for kind in [0, 1, 2, 5]:
            if not page.evaluate('(kind) => data.flow.Markers.some(m => m.Kind === kind)', kind):
                raise AssertionError(f'The replay did not exercise study marker kind {kind}.')
        page.select_option('#study', 'none')
        for selector, value in [('#range', '120'), ('#cluster', 'Smart'), ('#palette', 'Classic'), ('#style', '3d')]:
            page.select_option(selector, value)
        for value, name in [('False', 'raw'), ('True', 'persistent'), ('Following', 'filtered')]:
            page.select_option('#filter', value)
            page.screenshot(path=str(artifacts / f'preview-{name}.png'), full_page=True)
        page.select_option('#range', '30')
        page.select_option('#cluster', 'Pixel')
        page.select_option('#style', 'pie')
        page.screenshot(path=str(artifacts / 'preview-detail.png'), full_page=True)
        page.select_option('#range', '120')
        page.select_option('#cluster', 'Smart')
        page.select_option('#style', '3d')
        page.select_option('#study', 'absorption')
        page.screenshot(path=str(artifacts / 'preview-studies.png'), full_page=True)
        page.select_option('#study', 'levels')
        page.screenshot(path=str(artifacts / 'preview-profile.png'), full_page=True)
        browser.close()
finally:
    server.shutdown()
    server.server_close()
if errors:
    raise RuntimeError('\n'.join(errors))
report = {'states_rendered': states, 'javascript_errors': errors,
          'scope': 'Standalone preview interactions; not NinjaTrader or pixel-equivalence validation.'}
(artifacts / 'preview-checks.json').write_text(json.dumps(report, indent=2) + '\n')
print(f'Browser preview: {states} control combinations rendered, zero JavaScript errors.')
