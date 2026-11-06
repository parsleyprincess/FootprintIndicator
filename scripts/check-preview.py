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
balanced_states = 0
focused_states = 0
dominant_states = 0
inspection_examples = 0
fixture_checks = {}
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
        if page.locator('#contrast').input_value() != 'balanced' or page.locator('#style').input_value() != 'dominant':
            raise AssertionError('The preview did not start with balanced contrast and dominant-side bubbles.')
        page.screenshot(path=str(artifacts / 'preview-balanced.png'), full_page=True)
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
        fixture_checks = page.evaluate('''() => {
            const errors = [];
            let bubbleColorsChecked = 0, groupedRowsChecked = 0, gridTicksChecked = 0;
            const sum = (rows, field) => rows.reduce((total, row) => total + row[field], 0);
            const dominant = b => b.Buy > b.Sell && b.Buy > b.Unknown ? '#2acb8b' :
                b.Sell > b.Buy && b.Sell > b.Unknown ? '#e15348' : '#aeb8c4';
            if (data.views.length !== 12 || new Set(data.views.map(view => view.key)).size !== 12)
                errors.push('Expected twelve distinct top-level replay views');
            for (const view of data.views) {
                for (const [name, appearance] of [['normal', view], ['balanced', view.balanced], ['focused', view.focused]]) {
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
                    for (const mode of ['dominant', 'gradient'])
                        if (appearance.bubbleColors[mode].length !== appearance.scene.Bubbles.length ||
                            appearance.bubbleColors[mode].some(color => !/^#[0-9a-f]{6}$/i.test(color)))
                            errors.push(`${view.key}/${name}: incomplete ${mode} bubble colors`);
                    if (!appearance.scene.Bubbles.every((bubble, index) =>
                        appearance.bubbleColors.dominant[index].toLowerCase() === dominant(bubble)))
                        errors.push(`${view.key}/${name}: dominant colors differ from execution quantities`);
                    bubbleColorsChecked += appearance.scene.Bubbles.length;
                    if (appearance.scene.Bubbles.length !== view.scene.Bubbles.length ||
                        appearance.display.BubbleReferenceVolume !== view.display.BubbleReferenceVolume ||
                        JSON.stringify(appearance.scene.Bubbles) !== JSON.stringify(view.scene.Bubbles))
                        errors.push(`${view.key}/${name}: contrast changed execution bubbles`);
                }
                if (view.display.Gamma !== 1 || view.display.HeatFloorPercent !== 0 || view.display.ContrastPercentile !== 100)
                    errors.push(`${view.key}: unexpected normal contrast settings`);
                if (view.balanced.display.Gamma !== 1.4 || view.balanced.display.HeatFloorPercent !== 10 ||
                    view.balanced.display.ContrastPercentile !== 100)
                    errors.push(`${view.key}: unexpected balanced contrast settings`);
                if (view.balanced.scene.Heat.length >= view.scene.Heat.length ||
                    view.balanced.scene.HeatMaximum !== view.scene.HeatMaximum)
                    errors.push(`${view.key}: balanced contrast did not suppress weak heat with a shared maximum`);
                if (view.focused.display.Gamma !== 2 || view.focused.display.HeatFloorPercent !== 20)
                    errors.push(`${view.key}: unexpected focused contrast settings`);
                if (view.focused.scene.Heat.length >= view.scene.Heat.length)
                    errors.push(`${view.key}: focused contrast did not suppress weak heat`);
                if (view.display.BubbleReferenceVolume !== view.focused.display.BubbleReferenceVolume)
                    errors.push(`${view.key}: contrast changed bubble volume reference`);
                const columns = view.columns, group = columns.ticksPerRow;
                if (group < 2 || group * columns.pixelsPerTick < columns.minimumRowPixels ||
                    columns.bookLabel !== `COB Σ${group}t` || columns.profileLabel !== `SVP Σ${group}t`)
                    errors.push(`${view.key}: grouped column labels or row height differ`);
                for (const [kind, rows] of [['book', columns.book.Rows], ['profile', columns.profile]]) {
                    if (!rows.length || rows.some((row, i) => row.TickCount !== group || row.FirstTick % group !== 0 ||
                        i > 0 && row.FirstTick <= rows[i - 1].FirstTick))
                        errors.push(`${view.key}: ${kind} groups do not have ordered absolute price intervals`);
                    for (const row of rows) {
                        const inside = level => level.Tick >= row.FirstTick && level.Tick < row.FirstTick + row.TickCount;
                        if (kind === 'book') {
                            const bids = data.Bids.filter(inside), asks = data.Asks.filter(inside);
                            if (row.Bid !== sum(bids, 'Quantity') || row.Ask !== sum(asks, 'Quantity') ||
                                row.Total !== row.Bid + row.Ask || row.BidLevels !== bids.length || row.AskLevels !== asks.length)
                                errors.push(`${view.key}: book bin ${row.FirstTick} does not conserve source quantities`);
                        } else {
                            const source = data.Profile.filter(inside);
                            if (row.Buy !== sum(source, 'Buy') || row.Sell !== sum(source, 'Sell') ||
                                row.Unknown !== sum(source, 'Unknown') || row.Levels !== source.length ||
                                row.Total !== row.Buy + row.Sell + row.Unknown || row.Delta !== row.Buy - row.Sell)
                                errors.push(`${view.key}: profile bin ${row.FirstTick} does not conserve source quantities`);
                        }
                        groupedRowsChecked++;
                    }
                }
                if (sum(columns.book.Rows, 'Bid') !== sum(data.Bids, 'Quantity') ||
                    sum(columns.book.Rows, 'Ask') !== sum(data.Asks, 'Quantity') ||
                    columns.book.BidTotal !== sum(data.Bids, 'Quantity') ||
                    columns.book.AskTotal !== sum(data.Asks, 'Quantity') ||
                    sum(columns.profile, 'Total') !== data.SessionVolume ||
                    columns.book.Maximum !== Math.max(1, ...columns.book.Rows.map(row => row.Total)) ||
                    columns.profileMaximum !== Math.max(1, ...columns.profile.map(row => row.Total)))
                    errors.push(`${view.key}: grouped column totals or maxima differ from captured quantities`);
                const grid = view.timeGrid, interval = BigInt(grid.IntervalTicks),
                    from = BigInt(view.viewport.FromTicks), to = BigInt(view.viewport.ToTicks);
                if (!grid.Ticks.length || grid.labels.length !== grid.Ticks.length ||
                    Number(interval) / Number(to - from) * view.viewport.Width < 110)
                    errors.push(`${view.key}: time grid omits labels or crowds them`);
                for (let i = 0; i < grid.Ticks.length; i++) {
                    const tick = BigInt(grid.Ticks[i]);
                    if (typeof grid.Ticks[i] !== 'string' || tick % interval !== 0n || tick < from || tick >= to ||
                        i > 0 && tick - BigInt(grid.Ticks[i - 1]) !== interval ||
                        grid.labels[i] !== timeLabel(Number(tick)))
                        errors.push(`${view.key}: grid tick ${i} lost its absolute clock anchor`);
                    gridTicksChecked++;
                }
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
            const expectedColors = {buy:'#2acb8b',sell:'#e15348',unknown:'#aeb8c4',tie:'#aeb8c4',
                'unknown-tie':'#aeb8c4','sell-unknown-tie':'#aeb8c4','three-way-tie':'#aeb8c4',empty:'#aeb8c4'};
            if (data.bubbleColorExamples.length !== Object.keys(expectedColors).length)
                errors.push('Missing dominant-side tie/unknown fixtures');
            for (const sample of data.bubbleColorExamples)
                if (sample.color.toLowerCase() !== expectedColors[sample.key] ||
                    sample.color.toLowerCase() !== dominant(sample.bubble))
                    errors.push(`Dominant-side example ${sample.key} differs`);
            const navigation = Object.fromEntries(data.navigationExamples.map(sample => [sample.key, sample]));
            const bounds = sample => [BigInt(sample.fromTicks), BigInt(sample.toTicks)];
            for (const sample of data.navigationExamples) {
                const [from, to] = bounds(sample);
                if (!Number.isFinite(sample.seconds) || sample.seconds <= 0 || to <= from ||
                    Number(to - from) / 10000000 !== sample.seconds ||
                    !/^\\d{2}:\\d{2}:\\d{2}\\.\\d{3}$/.test(sample.from) ||
                    !/^\\d{2}:\\d{2}:\\d{2}\\.\\d{3}$/.test(sample.to))
                    errors.push(`Navigation example ${sample.key} has invalid bounds`);
                const rendered = document.querySelector(`[data-case-key="${sample.key}"]`);
                if (!rendered || !rendered.textContent.includes(sample.from) || !rendered.textContent.includes(sample.to))
                    errors.push(`Navigation example ${sample.key} is absent from its readout`);
            }
            if (data.navigationExamples.length !== 6 || !navigation.live.IsLive || !navigation.resume.IsLive ||
                ['paused','advanced','pan','zoom'].some(key => navigation[key].IsLive))
                errors.push('Navigation examples omit live/paused states');
            for (const key of ['paused', 'advanced'])
                if (navigation[key].fromTicks !== navigation.live.fromTicks || navigation[key].toTicks !== navigation.live.toTicks)
                    errors.push(`Navigation ${key} moved its frozen absolute interval`);
            const [panFrom, panTo] = bounds(navigation.pan), [zoomFrom, zoomTo] = bounds(navigation.zoom);
            if (panFrom !== BigInt(navigation.paused.fromTicks) - 300000000n ||
                panTo !== BigInt(navigation.paused.toTicks) - 300000000n)
                errors.push('Navigation pan changed the requested translation');
            if (zoomTo - zoomFrom !== (panTo - panFrom) / 2n ||
                panFrom + (panTo - panFrom) * 3n / 4n !== zoomFrom + (zoomTo - zoomFrom) * 3n / 4n)
                errors.push('Navigation zoom changed its pointer anchor');
            const [resumeFrom, resumeTo] = bounds(navigation.resume);
            if (resumeTo - resumeFrom !== zoomTo - zoomFrom ||
                resumeTo !== BigInt(data.TimeTicks) + 600000000n + (zoomTo - zoomFrom) / 25n)
                errors.push('Navigation resume did not follow the current observation with display padding');
            return {errors, bubbleColorsChecked, groupedRowsChecked, gridTicksChecked,
                dominantExamplesChecked: data.bubbleColorExamples.length,
                navigationExamplesChecked: data.navigationExamples.length};
        }''')
        if fixture_checks['errors']:
            raise AssertionError('\n'.join(fixture_checks['errors']))
        page.select_option('#study', 'none')
        for time_range, liquidity, cluster in itertools.product(
                ['120', '30'], ['False', 'True', 'Following'], ['Smart', 'Pixel']):
            for selector, value in [('#range', time_range), ('#filter', liquidity), ('#cluster', cluster)]:
                page.select_option(selector, value)
            for contrast in ['normal', 'balanced', 'focused']:
                page.select_option('#contrast', contrast)
                if '1,872 trades' not in page.locator('#status').inner_text():
                    raise AssertionError('Contrast changed source execution accounting.')
                if contrast == 'balanced':
                    balanced_states += 1
                if contrast == 'focused':
                    focused_states += 1
                page.select_option('#style', 'dominant')
                rendered_errors = page.evaluate('''() => {
                    const v = activeView(), fills = [], labels = [], fill = g.fill, fillText = g.fillText;
                    g.fill = function(...args) { fills.push(this.fillStyle.toLowerCase()); return fill.apply(this, args); };
                    g.fillText = function(text, ...args) { labels.push(String(text)); return fillText.call(this, text, ...args); };
                    try { draw(); } finally { g.fill = fill; g.fillText = fillText; }
                    const expected = v.scene.Bubbles.map((bubble, index) => ({bubble, index}))
                        .sort((a, b) => b.bubble.Radius - a.bubble.Radius).filter(item => item.bubble.Radius > 0)
                        .map(item => v.bubbleColors.dominant[item.index].toLowerCase());
                    const errors = [];
                    if (JSON.stringify(fills) !== JSON.stringify(expected))
                        errors.push('Canvas did not use the exported dominant color for every bubble');
                    if (![v.columns.bookLabel, v.columns.profileLabel, ...v.timeGrid.labels].every(label => labels.includes(label)))
                        errors.push('Canvas omitted grouped-column or absolute-clock labels');
                    for (const row of [...v.columns.book.Rows, ...v.columns.profile]) {
                        const yy = (v.viewport.MaxPriceTick - row.FirstTick - row.TickCount + .5) * v.columns.pixelsPerTick;
                        if (yy + row.TickCount * v.columns.pixelsPerTick >= 0 && yy <= v.viewport.Height &&
                            !labels.includes(String(row.Total))) errors.push('Canvas omitted an in-view grouped quantity');
                    }
                    if (!ctrl('column-summary').textContent.includes(v.columns.bookLabel) ||
                        !ctrl('grid-summary').textContent.includes(String(v.timeGrid.IntervalTicks / 10000000)))
                        errors.push('Grouped-column or grid summary is stale');
                    return errors;
                }''')
                if rendered_errors:
                    raise AssertionError('\n'.join(rendered_errors))
                dominant_states += 1
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
          'balanced_states_checked': balanced_states, 'dominant_states_checked': dominant_states,
          'inspection_examples_checked': inspection_examples, 'javascript_errors': errors,
          'dominant_bubble_colors_checked': fixture_checks['bubbleColorsChecked'],
          'dominant_examples_checked': fixture_checks['dominantExamplesChecked'],
          'grouped_rows_checked': fixture_checks['groupedRowsChecked'],
          'absolute_grid_ticks_checked': fixture_checks['gridTicksChecked'],
          'navigation_examples_checked': fixture_checks['navigationExamplesChecked'],
          'scope': 'Standalone preview interactions; not NinjaTrader or pixel-equivalence validation.'}
(artifacts / 'preview-checks.json').write_text(json.dumps(report, indent=2) + '\n')
print(f'Browser preview: {states} baseline control combinations, {balanced_states} balanced views, '
      f'{focused_states} focused views, {dominant_states} dominant-side views, '
      f'{inspection_examples} C# inspection examples; zero JavaScript errors.')
print(f'Export contracts: {fixture_checks["bubbleColorsChecked"]} bubble colors, '
      f'{fixture_checks["dominantExamplesChecked"]} tie/unknown examples, '
      f'{fixture_checks["groupedRowsChecked"]} conserved grouped rows, '
      f'{fixture_checks["gridTicksChecked"]} absolute grid ticks, '
      f'{fixture_checks["navigationExamplesChecked"]} finite navigation examples.')
