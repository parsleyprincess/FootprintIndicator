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
focused_states = 0
inspection_examples = 0
try:
    with sync_playwright() as browser_driver:
        chromium = shutil.which('chromium') or shutil.which('chromium-browser')
        browser = browser_driver.chromium.launch(executable_path=chromium, headless=True)
        page = browser.new_page(viewport={'width': 1520, 'height': 1100}, device_scale_factor=1)
        page.on('pageerror', lambda error: errors.append(str(error)))
        page.goto(f'http://127.0.0.1:{server.server_port}/footprint-preview.html', wait_until='load')
        try:
            page.wait_for_function('window.previewReady === true')
        except Exception as error:
            raise RuntimeError('Preview did not initialize: ' + ('; '.join(errors) or str(error))) from error
        page.select_option('#contrast', 'normal')
        page.select_option('#inspection', 'none')
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
        # The browser consumes palettes and lower-panel series exported by C#.
        # Validate their contracts separately from the baseline display matrix.
        fixture_errors = page.evaluate('''() => {
            const errors = [];
            for (const view of data.views) {
                for (const [name, appearance] of [['normal', view], ['focused', view.focused]]) {
                    for (const palette of ['Classic', 'Grayscale', 'Ice']) {
                        if (appearance.heatColors[palette].length !== appearance.scene.Heat.length)
                            errors.push(`${view.key}/${name}: incomplete ${palette} colors`);
                    }
                    if (!appearance.scene.Heat.every((tile, i) =>
                        tile.Color.Hex === appearance.heatColors.Classic[i]))
                        errors.push(`${view.key}/${name}: Classic colors differ from C# scene`);
                    if (!appearance.hoverExamples.some(sample => sample.key === 'bubble') ||
                        !appearance.hoverExamples.some(sample => sample.key === 'wall'))
                        errors.push(`${view.key}/${name}: missing inspection examples`);
                }
                if (view.focused.display.Gamma !== 2 || view.focused.display.HeatFloorPercent !== 20)
                    errors.push(`${view.key}: unexpected focused contrast settings`);
                if (view.focused.scene.Heat.length >= view.scene.Heat.length)
                    errors.push(`${view.key}: focused contrast did not suppress weak heat`);
                if (view.display.BubbleReferenceVolume !== view.focused.display.BubbleReferenceVolume)
                    errors.push(`${view.key}: contrast changed bubble volume reference`);
                const lower = view.lowerPanel, scale = view.lowerScale;
                if (!lower.VolumeBars.length || !lower.CvdPoints.length ||
                    !lower.VolumeBars.some(bar => bar.Unknown > 0))
                    errors.push(`${view.key}: lower panel omitted execution data`);
                if (lower.VolumeBars.some(bar => bar.Total !== bar.Buy + bar.Sell + bar.Unknown))
                    errors.push(`${view.key}: lower panel volume accounting differs`);
                if (scale.MinDelta > lower.MinDelta || scale.MaxDelta < lower.MaxDelta ||
                    scale.MaxVolume < lower.MaxVolume)
                    errors.push(`${view.key}: lower panel scale clips exported data`);
                if (view.key.startsWith('120:') &&
                    lower.VolumeBars.reduce((total, bar) => total + bar.Total, 0) !== data.SessionVolume)
                    errors.push(`${view.key}: full replay volume differs from session accounting`);
            }
            return errors;
        }''')
        if fixture_errors:
            raise AssertionError('\n'.join(fixture_errors))
        page.select_option('#study', 'none')
        for time_range, liquidity, cluster in itertools.product(
                ['120', '30'], ['False', 'True', 'Following'], ['Smart', 'Pixel']):
            for selector, value in [('#range', time_range), ('#filter', liquidity), ('#cluster', cluster)]:
                page.select_option(selector, value)
            for contrast in ['normal', 'focused']:
                page.select_option('#contrast', contrast)
                if '1,872 trades' not in page.locator('#status').inner_text():
                    raise AssertionError('Contrast changed source execution accounting.')
                if contrast == 'focused':
                    focused_states += 1
                samples = page.evaluate('activeView().hoverExamples')
                for sample in samples:
                    page.select_option('#inspection', sample['key'])
                    card = page.locator('#inspection-card')
                    if card.get_attribute('data-sample-key') != sample['key']:
                        raise AssertionError('The inspection card retained a stale sample.')
                    if card.get_attribute('data-view-key') != page.evaluate('activeView().key'):
                        raise AssertionError('The inspection card retained a stale view.')
                    displayed = json.loads(page.locator('#inspection-output').text_content())
                    if displayed != sample['result']:
                        raise AssertionError('The inspection card changed the exported C# result.')
                    result = sample['result']
                    text = card.inner_text()
                    if result['Bubble'] is not None:
                        for label in ['Buy', 'Sell', 'Unknown', 'Total']:
                            if label not in text:
                                raise AssertionError(f'Execution inspection omitted {label}.')
                    if result['Liquidity']['HasObservation'] and result['Liquidity']['HasLevel']:
                        for label in ['Raw frame average', 'Persistent frame average']:
                            if label not in text:
                                raise AssertionError(f'Liquidity inspection omitted {label}.')
                    if sample['key'] == 'gap' and result['Liquidity']['HasObservation']:
                        raise AssertionError('The disconnect gap acquired a depth observation.')
                    if sample['key'] == 'unknown' and result['Bubble']['Unknown'] <= 0:
                        raise AssertionError('The unknown-volume example has no unknown executions.')
                    inspection_examples += 1
        page.select_option('#inspection', 'none')
        page.select_option('#contrast', 'normal')
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
        page.select_option('#study', 'none')
        page.select_option('#contrast', 'focused')
        page.select_option('#style', 'pie')
        page.screenshot(path=str(artifacts / 'preview-focused.png'), full_page=True)
        page.select_option('#inspection', 'bubble')
        page.screenshot(path=str(artifacts / 'preview-hover.png'), full_page=True)
        page.select_option('#inspection', 'wall')
        page.screenshot(path=str(artifacts / 'preview-liquidity.png'), full_page=True)
        browser.close()
finally:
    server.shutdown()
    server.server_close()
if errors:
    raise RuntimeError('\n'.join(errors))
report = {'states_rendered': states, 'focused_states_checked': focused_states,
          'inspection_examples_checked': inspection_examples, 'javascript_errors': errors,
          'scope': 'Standalone preview interactions; not NinjaTrader or pixel-equivalence validation.'}
(artifacts / 'preview-checks.json').write_text(json.dumps(report, indent=2) + '\n')
print(f'Browser preview: {states} baseline control combinations, {focused_states} focused views, '
      f'{inspection_examples} C# inspection examples; zero JavaScript errors.')
