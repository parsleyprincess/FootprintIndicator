# Installing and checking FootprintMap in NinjaTrader Desktop

FootprintMap is an original C# NinjaScript indicator with a Bookmap-style heatmap, execution bubbles, current order book, volume profiles and delta overlays. It requires a Windows installation of NinjaTrader Desktop 8 and actual Level II data for the selected contract.

**Current validation boundary:** the portable C# engine and replay workflow have been tested in the cloud. The user compiled and ran an earlier adapter version, then reported the depth/display problems addressed in this revision. The latest revised adapter has not yet been compiled or run in the genuine Windows chart host here. Treat this update as an integration test. A browser replay image is not a NinjaTrader screenshot.

Official NinjaTrader release/download pages were inaccessible from the research environment. A secondary documentation mirror lists 8.1.8.2, dated August 11, 2026; that does not establish the latest release. Obtain the current supported Desktop release through your existing NinjaTrader account and record its version from the application when validating. The adapter uses conservative C# 7.3 syntax and established NT8 APIs; the portable test runner's .NET 8 runtime is separate from NinjaTrader's .NET Framework host.

## 1. Prepare the Windows installation

1. Open NinjaTrader once and confirm its ordinary chart and data connection work, then close NinjaTrader before copying source files.
2. Locate the actual NinjaTrader user-data folder. It is normally **Documents\NinjaTrader 8**, but Windows or OneDrive can redirect Documents. Use the folder containing your existing `bin\Custom` directory.
3. Preserve any existing files named `FootprintMap.cs` or an existing `FootprintMapCore` folder before replacing them. Keep backups **outside** `bin\Custom`: duplicate `.cs` files inside Custom can create duplicate-type compilation errors.
4. Extract the project's source package to an ordinary local folder. Do not place the entire repository or generated build directories in NinjaTrader's Custom folder.

NinjaTrader's trial feed may have different exchange/depth entitlements from a later Rithmic connection. Verify the exact NQ, ES, GC, YM or RTY contract you select. A moving last price alone does not establish Level II availability.

## 2. Copy the source files

The source layout in your NinjaTrader user-data folder should be:

```text
NinjaTrader 8\
  bin\Custom\Indicators\
    FootprintMap.cs
    FootprintMapCore\
      Model.cs
      MarketEngine.cs
      Visualization.cs
      LiquidityNoiseFilter.cs
      HistoryStore.cs
      ProfileStudies.cs
      OrderFlowStudies.cs
      MboEngine.cs
      HoverInspection.cs
      LowerPanel.cs
```

Copy **all ten `.cs` files** from `src\Footprint.Core` into `bin\Custom\Indicators\FootprintMapCore`, including the new `HoverInspection.cs` and `LowerPanel.cs`. Copy `src\NinjaTrader\FootprintMap.cs` into `bin\Custom\Indicators`. This revision has **eleven source files** to install and compile together; updating only the adapter leaves required types missing.

Do not copy `.csproj`, `bin`, `obj`, tests, browser preview files, or vendor DLLs into Custom. The engine source and indicator must compile together in NinjaTrader's Custom assembly; the source installer does not require a separately deployed `Footprint.Core.dll`.

An optional PowerShell helper is included:

```powershell
# Run from the extracted project directory, with NinjaTrader closed.
.\scripts\install-ninjatrader.ps1
```

Read the script's parameters if your user-data folder is redirected. It refuses to overwrite existing project files by default. Its explicit replacement option creates a backup outside Custom before replacement. The helper has not been executed on Windows in this cloud session; manual copying remains available. Follow your Windows policy for locally reviewed scripts rather than weakening system-wide execution policy.

## 3. Compile in NinjaTrader

1. Reopen NinjaTrader.
2. Open **New → NinjaScript Editor** from Control Center.
3. Confirm the copied source files are visible. Compile from the editor, normally with **F5**.
4. Resolve every compiler error before adding the indicator. Keep the full error text, file name and line number if reporting a problem. NinjaTrader compiles other Custom scripts too, so distinguish pre-existing errors from errors in these files.

Do not infer successful platform validation merely because the portable `Footprint.Core` project builds. NinjaScript compilation also exercises NinjaTrader's own base classes, generated wrappers, references and source conventions.

For an additional external build against your **genuine installed assemblies**, the repository includes `scripts/validate-ninjatrader.ps1` and a validation project. Supply the actual installation and Custom paths requested by that script. This requires the corresponding Windows build tooling. It supplements the NinjaScript Editor check and does not replace chart runtime testing. Proprietary NinjaTrader binaries are not supplied with this project.

This optional external check uses the .NET SDK pinned in `global.json` (8.0.426); install that SDK on Windows if using the script from the project directory. The ordinary NinjaScript Editor installation does not require this separate SDK. For example, replacing the path with your actual installation:

```powershell
.\scripts\validate-ninjatrader.ps1 -NinjaTraderBin 'C:\Program Files\NinjaTrader 8\bin'
```

## 4. Start with a seconds view

1. Connect the intended data feed and choose one specific futures contract.
2. Open the chart's **Data Series** dialog. For native seconds bars choose **Type: Second**, then **Value: 1**, **5** or **10**. Do not enter a fractional minute value. A one-minute chart can also host the indicator's independent seconds viewport.
3. Add **FootprintMap**. Its default **Continuous seconds viewport** shows the latest **120 seconds** of actual capture on its own labeled axis inside the panel. It advances with market timestamps and stays still during a replay pause. This makes recent data visible immediately instead of compressing a few seconds into an hour of native candles.
4. Hold **Ctrl** while using the mouse wheel over the indicator's chart panel to zoom the continuous time window; hold **Shift** while scrolling up to inspect earlier retained capture and scroll down to return toward the live edge. The initial window and offset are also available under **3a. Time and current price**. The display sidebar has its own slider interactions; wheel behavior elsewhere belongs to NinjaTrader unless these time-navigation modifiers are held over the indicator panel.
5. Keep **Show last price and current quotes**, **Show current order book**, and **Project current depth beyond NOW** enabled. The yellow last-price guide and badge remain visible even with no Level II. The badge has its own 90-pixel strip; disable **Show separate last-price badge** if you prefer the host price-axis marker and header. An arrow badge means that last price is outside the current visible price range. The translucent strip after **NOW** represents the current observed book; it is never saved as historical heat.
6. Let the feed run. The status should show the current provider bid/ask level counts and increasing trades. Raw depth is the default; persistence and following suppression are opt-in.

The right-side price scale remains NinjaTrader's native scale. With **Auto scale** and **Show last price and current quotes** enabled, the indicator contributes the latest observed price/quotes with a small tick margin to the live scale. Scrolling the continuous view into history stops that contribution; a manually fixed price scale remains under your control. Drag/zoom that price scale to bring prices near the current market into view. A native scale spanning a large historical move can compress each price level to a fraction of a pixel even when depth is being captured correctly. Use an appropriate visible price range, particularly after switching contracts or loading a chart with distant historical bars. The indicator does not change the instrument's tick size or invent prices to fill the chart.

**Continuous time uses its own horizontal coordinates.** Read the indicator's inner `HH:mm:ss` axis, rather than the native chart's bottom time labels. Keep the opaque background and covering z-order enabled when using this view; underlying native bars use a different horizontal map. For exact alignment with native bars and chart scroll/zoom, disable **Continuous seconds viewport**. That native mode requires a minute/second chart with at least two visible bars and enough right margin for the profiles, price strip and sidebar: approximately **enabled columns × Profile column width + Last-price strip width + sidebar width + 30 pixels**. With default COB/SVP, the 90-pixel price strip and expanded 220-pixel sidebar this is about **540 pixels**; the collapsed sidebar uses 32 pixels, and disabled elements reserve no width. A warning reports when profiles obscure the native live edge. The continuous view reserves this space automatically.

The indicator reads `Instrument.MasterInstrument.TickSize` and formats prices through the platform. This includes NQ/ES, GC/RTY, YM, 6E, 6B and DAX-family instruments using the actual instrument definitions. Futures contract availability and the number of supplied depth rows depend on the connection. Liquidity and execution thresholds are absolute quantities and should be tuned separately for each contract.

When updating an older workspace, copy **all ten** core files and the adapter, compile, and open the indicator settings. Existing templates preserve saved values, including the old heat exponent, bubble colors and size scale; replacing source does not reset them. To apply this revision's presentation defaults explicitly:

- Set **Heat contrast floor (%) = 20**, **Heat contrast exponent = 2**, **Filter by level persistence = false**, and **Suppress price-following liquidity = false**. Keep **Automatic contrast = true** initially; use a fixed manual maximum when comparing absolute brightness.
- Set **Bubble color mode = Pie**, **3D bubble highlight = false**, **Bubble size scale = 12**, **Bubble reference volume = 100**, and **Volume cluster anchor (seconds) = 60**. The anchor applies to Volume clustering.
- Enable **Show hover details**, **Hover crosshair**, **Dark header background**, and **Stable CVD and volume scale**. Set **Volume bar interval (ms) = 1000**, **Fixed CVD half-range = 0**, and **Fixed volume maximum = 0** for the default automatic scales.
- Enable **Show on-chart display controls** and **Expand display controls** under **3c. Quick controls** to expose the sliders. Small panels collapse the sidebar automatically; resize the chart or use the indicator settings when space is limited.
- Enable **Continuous seconds viewport** with a **120-second** window and no history offset. Keep a suitable native vertical price range. Enable **Show separate last-price badge** with **Last-price strip width = 90**, or disable that badge to use the host marker and header.

Hover over a bubble to inspect its executed total, buy, sell, unknown and delta quantities. Hover over heat to inspect raw/persistent bid/ask frame averages at that time and price; **Rendered bin** is a separate filtered/aggregated display quantity. The card also shows that recorded frame's best bid/ask, which can differ from the hovered price; a nearby historical quote segment is highlighted. A missing recorded row does not prove that market liquidity was zero. All these quantities are contracts/units; ordinary NinjaTrader depth does not provide individual order counts.

Use the sidebar's **+ / −** button to expand/collapse its reserved area. Drag a slider or use the wheel over its track to adjust **Brightness** (gamma), **Black cutoff**, **Heat opacity**, **Min. level quantity**, **Min. bubble volume**, **Bubble size**, or **Heat ceiling**. Select **Ceiling: MANUAL** before changing the ceiling slider; in Auto it displays the computed ceiling. These controls affect display, including paused Playback, without changing accepted executions or saved raw depth. The black cutoff is a percentage of the current ceiling: a 20% floor with a ceiling of 100 suppresses quantities at or below 20. It is not a claim to reproduce Bookmap's percentile cutoff. Raising the floor/minimum or reducing brightness/opacity can help isolate stronger liquidity; use a fixed ceiling when comparing absolute quantities across zoom levels.

**Show run and last execution volumes** adds an explicitly named readout such as `run 27 (last 9)`: 27 contracts/units in the consecutive retained execution sequence at the last execution price, of which the latest print was 9. A different execution price, capture epoch or session starts a new run. Buy and sell prints can both contribute; the last print's color describes its inferred aggressor, not the whole run. A `≥` prefix marks a possibly truncated run at the retention boundary. These are executed quantities, not resting order counts or iceberg estimates. The similar-looking native `N (n)` label in the reference video remains unverified; see the [frame-by-frame evidence](research/bookmap-oct29-video-review.md#parenthesized-right-edge-numbers).

## 5. Understand the main settings

| Setting | Meaning |
| --- | --- |
| Heat frame duration | Duration-weighted resting quantity per time bucket; smaller buckets require more processing and history storage. Default 250 ms. |
| Maximum depth levels per side | Capture budget, default 200 and maximum 2,000. A larger budget cannot add rows that the provider or account entitlement does not supply. Compare with a native Level II window. |
| Filter by level persistence | Default off. Uses quantity continuously observed at a fixed price. Newly added quantity starts young. This is aggregate-level persistence, not the age of identifiable orders. |
| Level persistence | Required persistence window, default 1,250 ms. Turning the filter off exposes the raw recorded book. |
| Suppress price-following liquidity | Optional heuristic for repeated large quantities at similar distances from a moving best quote. Default off. It can remove ordinary liquidity and is not TraderMap Pro's proprietary Bot Remover. |
| Following observation window / moves / size ratio / tolerance | Controls the evidence needed by that heuristic. Test the effect against raw depth; price-following behavior does not establish intent or individual order identity. |
| Minimum displayed level quantity / Keep top liquidity percent | Selects aggregate heat cells, not individual MBO orders. |
| Automatic contrast / Contrast percentile | Sets intensity from visible selected quantities. Panning or filtering can change the color reference. Use Manual heat maximum for a stable numerical reference. |
| Heat contrast floor (%) / Heat contrast exponent | Defaults 20 and 2. Quantities at/below 20% of the active ceiling stay dark; remaining strength is remapped through the exponent. This floor is a fraction of the ceiling, not a percentile of orders. Display only: raw depth, hover observations, accepted trades and profile/CVD accounting are preserved. |
| Show on-chart display controls / Expand display controls | Reserve a right sidebar for seven display sliders and the Auto/Manual ceiling button. Both default on; the +/− button collapses it, and insufficient panel space forces a compact layout. The settings dialog remains available. |
| Show run and last execution volumes | Defaults on. Shows captured consecutive volume at the latest execution price and the last print's size; price/epoch/session changes end the run, and `≥` identifies possible truncation. This is an independently defined statistic, not verified native Bookmap edge-label semantics. |
| Classic palette | Dark/blue/pale/yellow/orange/red intensity family; the hottest end is red. Exact RGB values and contrast adaptation are original approximations. |
| Minimum individual execution size | Filters original prints before drawing bubbles. It does not remove those executions from profile/CVD totals. |
| Minimum displayed cluster volume | Filters after bubble aggregation. Many small trades can create a qualifying cluster. |
| Bubble clustering / Volume cluster anchor (seconds) | Pixel, Smart, Time, Volume, Price, PriceAndSide, or Individual. Individual draws each supplied print separately, including same-time/same-price prints; overlapping marks still need sufficient zoom and provider-aggregated prints cannot be split into underlying exchange orders. Pixel/Smart/Time use absolute event-time bins; Smart also uses absolute price bins. Volume mode preserves the entire closing trade, may exceed its threshold, and starts new groups at configurable absolute 60-second boundaries to limit retention-related regrouping. |
| Delta bubbles | Sizes by the absolute buy-minus-sell difference. Equal buy/sell volume can produce zero delta despite substantial trading. |
| Bubble size scale / Bubble reference volume | Defaults 12 and 100. Radius is scale × square root of represented volume/reference volume, subject to radius limits. The reference is fixed, so unrelated new large prints cannot resize old bubbles. Delta mode uses absolute buy-minus-sell volume. |
| Bubble opacity / color mode / 3D bubble highlight | Flat Pie sectors by default, with separate buy/sell/unknown colors. Gradient/Split and decorative 3D remain available; unknown volume is neutral. Outline and quantity labels are optional. |
| Show hover details / Hover crosshair | Inspect executed bubble quantities or recorded raw/persistent bid/ask frame averages and the separate rendered bin. Both controls default on. Hover clears when leaving the plot; quantities are not counts of individual orders. |
| Show separate last-price badge / Last-price strip width / Dark header background | Optional separate badge strip, default 90 pixels, prevents the label from covering executions. Disable the badge to use the host price marker and header. The dark header keeps quote/status text readable over heat. |
| Stable CVD and volume scale | Default on. Bounds expand with headroom when a newly encountered live or historical value exceeds them; browsing smaller values cannot shrink the scale. Session/recording resets restart it. Turning this off restores viewport autoscaling. |
| Fixed CVD half-range / Fixed volume maximum | Zero selects automatic scaling. Positive values override it: CVD spans minus/plus the half-range, and volume uses the selected maximum. Values beyond fixed limits clip. Use these for an exact unchanging scale. |
| Volume bar interval (ms) | Default 1,000 ms. Volume bars use absolute event-time buckets, independent of horizontal pixels or bubble filters. An unfinished bucket can still grow; retention can leave the oldest bucket incomplete. |
| COB | Raw currently observed bid/ask quantities. Heatmap filtering does not change the actual displayed order-book column. |
| SVP / CVP | Accepted executed volume since capture/session reset, or within the retained visible range. Neither includes resting depth. |
| Session reset | Uses the chart Trading Hours template and market-event timestamps. It resets profile/CVD before the first accepted execution in the new observed session. Actual feed capture continues outside the chart session; those executions remain in the previous observed profile until the next session begins. A chart calendar boundary does not pretend that the data connection disconnected. |
| Data connection name | Blank conservatively responds to any price-feed disconnect. Set the exact configured name when unrelated data connections would otherwise create unnecessary gaps. |
| History/count budgets | Cap heat frames and trade retention. Session totals remain separate; old dots and the visible profile cannot recover trades discarded by the retention cap. |
| Save and restore captured history | Default on. Saves every 30 seconds in the background and flushes on orderly removal/shutdown. Restores captured heat, executions and session totals; the live book waits for fresh provider data. |
| Profile study scope / POC / value area | Captured session or retained visible executions; default value area is 70%. POC/VA lines default on. These are executed-volume statistics. |
| VWAP / deviation bands | Captured volume-weighted price and weighted price standard deviation under the selected profile scope; VWAP defaults on, bands off. |
| Book / volume imbalance | Signed bid versus ask displayed depth over the selected nearest levels; signed buy versus sell executed volume excludes unknown aggressors. Optional distance decay weights depth by ticks from best. |
| Large current levels | Optional absolute displayed MBP quantity highlight; no individual order identity is inferred. |
| Bid × ask footprint | Optional fixed time/price execution cells, default 10-second buckets. Labels show sell × buy, with unknown volume separately, when zoom provides enough room. Diagonal imbalance coloring uses configurable ratio/minimum volume. |
| Captured TPO | Optional right column counting time brackets with an actual print at each grouped price. It does not fill unobserved prices between executions; it uses retained visible prints and the selected study scope. |
| AB / SW / LP | Original absorption and sweep candidates plus actual large individual prints. Default thresholds are starting values, not instrument recommendations. AB color represents the passive side; SW/LP represent the aggressor. |
| +L / −L / RP? | Optional sampled depth additions/removals and MBP replenishment candidates. These compare duration-weighted frames, not every individual order event. RP? does not prove an iceberg; −L does not prove a cancellation or stop execution. |
| Show COB bars / Show COB numbers | Separate current-book visibility controls under **5a. Current order book**; both default on. Numbers require enough vertical space. Profile label settings do not affect COB numbers. |
| Show COB bids / asks / Maximum COB bid or ask rows | Show either side and limit its nearest supplied rows. Zero means all captured rows; the feed and **Maximum depth levels per side** capture budget still limit availability. These display settings do not change heat, capture or imbalance calculations. |
| Cumulative COB quantities / COB percentage labels | Cumulative sums run from the best quote outward on each side. Percentages replace numeric quantities and divide by the selected row total on that same side, including selected rows outside the visible price range. Cumulative percentages therefore reach 100% at that side's last selected row. Bars always scale quantities against the largest displayed quantity across enabled sides. Both settings default off. |
| Show profile bars / Profile quantity labels | Independent bars and numbers for session, visible and TPO profiles; both default on. Turning bars off leaves numbers visible wherever rows have enough height. |
| Align COB to right / Align profiles to right | Independently anchor the bars and numbers at each column's right edge. Both default off for the existing left alignment. |

In native time mode, the adapter interpolates between native time-bar anchors to preserve sub-bar event positions while respecting chart scroll and zoom. In continuous mode it maps captured event times across the selected seconds window. Completed, fully retained bubble groups inside the viewport stay stable during panning at the same time/price resolution. Zoom or resizing can change Pixel/Smart resolution; changing aggregation settings intentionally regroups executions. Unfinished live groups can grow, and groups clipped by the viewport or retention boundary can be partial. These display changes do not alter accepted executions or profile/CVD accounting. The formulas and marker heuristics are independent implementations, not Bookmap proprietary algorithms.

## 6. Validate actual feed behavior

Perform these checks with a live feed or a known **Market Replay recording containing Level II**:

1. Compare the COB quantities against NinjaTrader's Level II window for the same contract.
2. Switch raw/persistent/following-filter views and confirm that the execution count and profile/CVD accounting stay unchanged.
3. First keep the time/price resolution unchanged while the live edge advances: completed, fully retained interior bubble quantities/radii should remain stable as they move left. Then zoom in/out; grouping can change while recorded executions remain accounted for. A bubble can disappear if its cluster threshold no longer qualifies. Distinguish unfinished live groups and groups cut by viewport/retention boundaries.
4. Compare total-volume and delta bubbles on mixed buy/sell trading. They intentionally represent different quantities.
5. Disconnect the price feed and reconnect. Depth should clear, the outage should remain a historical gap, and new depth should rebuild without extending old liquidity across the outage.
6. Resize the chart, change the visible price range, Ctrl-wheel zoom, Shift-wheel scroll history, save/reload the workspace, and remove/re-add the indicator. Previously saved capture should remain visible; COB must wait for fresh depth. Check the NinjaScript Output and Log windows for errors.
7. Check a trading-hours boundary and a backward Market Replay seek. The configured clock-rewind threshold resets recorded state to avoid carrying future data into the earlier replay.
8. Attach the indicator after the native Level II window has already populated; the current book should fill from the platform's authoritative ladder without waiting for positional Add(0) events. Compare both sides after reconnect and instrument reset.
9. Verify same-session restore retains profile/CVD totals, while the first observed event in a new chart Trading Hours session resets them when session reset is enabled.
10. Enable footprint/TPO and each desired flow study individually, inspect thresholds, and compare its labels with recorded prints and raw depth. Repeat basic depth/price/volume checks for 6E, 6B and the selected DAX-family contract as well as the other target contracts and after changing providers.
11. Check COB bars and numbers independently, hide each side, limit bid/ask rows separately, and compare cumulative sums and percentages to those selected raw rows. Check profile bars/numbers and left/right alignment in session, visible and TPO columns. Confirm these controls leave captured heat, execution totals and imbalance calculations unchanged.
12. Hover over known bubbles and heat cells, including filtered-out rows, capture gaps and chart edges. Compare executed total/buy/sell/unknown quantities and raw/persistent frame averages with the retained data; confirm rendered-bin values are labeled separately and hover clears on exit.
13. Pan and zoom the lower panel. Check the CVD numeric range and zero line, fixed-time volume buckets, and retained CVD values. Stable scales may expand for a newly encountered extreme but must not shrink on smaller views. Repeat with positive fixed CVD/volume limits and confirm excess clips. Check the price badge/header at your Windows DPI and toggle the separate badge alongside the host marker.
14. Drag each display slider, toggle Auto/Manual, and expand/collapse the sidebar, including while Playback is paused. Verify the chart repaints without a new market event, manual ceiling is inactive in Auto, mouse capture releases after dragging outside the panel, and a removed/re-added indicator has no duplicate input handlers. Repeat at your Windows DPI and after resizing. Check that profile columns, badge and sidebar occupy separate space.

Record the NinjaTrader version, provider, contract, Trading Hours template, chart period, settings and any errors with the validation result. Actual Windows rendering, provider compatibility and NinjaScript import remain unverified until these checks are completed.

## Saved capture and troubleshooting

Captured data is stored under **NinjaTrader user-data folder\FootprintMapHistory**. Each archive is keyed by full instrument/contract name, tick size, Trading Hours name and template timezone, heat-frame duration, and persistence duration. Changing the chart from minutes to seconds preserves that capture key. Changing those capture definitions selects a separate archive because stored frame averages/persistence cannot be recalculated exactly under a new definition. Historical event timestamps are stored in NinjaTrader's chart/display time; archive or remove the corresponding saved files before changing the platform's display timezone.

Archives contain bounded retained heat and executions plus the captured session profile/CVD/VWAP. They do not contain a live book or saved order ages. A reload starts a new depth epoch; the gap before fresh feed data remains a gap. A same-session reload continues captured session counters. A new observed Trading Hours session resets them when configured. Saves run every 30 seconds and on orderly termination, so a crash can lose the unsaved interval. Two chart instances share the key; an older capture does not overwrite a newer saved event time, and this is not a multi-instance merge service. A backwards replay may therefore leave a later saved capture intact.

For isolated Playback experiments, disable **Save and restore captured history** before starting the indicator. The archive key does not distinguish live and Playback connections, so this prevents replay experiments from sharing the live capture archive. Keep live capture enabled for ordinary reloads and minute/second chart changes.

- **Waiting for Level II:** confirm depth entitlement and the selected contract in a native Level II window. A Level I-only connection still supplies last-price/bubble data but cannot populate the heatmap.
- **Depth position gap after installing the older version:** replace all sources and recompile. This adapter now copies both provider ladder sides under `Instrument.SyncMarketDepth` and reconciles aggregate prices; it does not apply incomplete positional callbacks to an empty local book.
- **No earlier heatmap:** only actual captured/saved depth or a Market Replay stream containing Level II can supply it. OHLC candles and Tick Replay cannot recreate unobserved resting orders. Check the status for saved-capture or restore errors.
- **Recent heat is a thin strip:** use the continuous 120-second view or a native Second chart and zoom in. Also tighten NinjaTrader's native price scale around the current market. The top LAST/BID/ASK readout identifies where that market is.
- **Last price outside the view:** the badge shows an up/down arrow. Move the native price scale toward that value. Continuous horizontal zoom deliberately preserves native price coordinates.
- **Duplicate price labels:** disable **Show separate last-price badge** to keep the host price-axis marker and indicator header. If enabled, the indicator badge occupies a reserved strip rather than covering bubbles.
- **Old pale heat or blended bubbles after updating:** saved templates retain earlier settings. Apply the explicit contrast floor/exponent, Pie color, flat appearance and fixed bubble reference settings in section 4.
- **Lower-panel size changes while browsing:** the automatic stable scale can expand when it encounters a larger historical or live extreme. Set positive **Fixed CVD half-range** and **Fixed volume maximum** to hold exact limits; zero selects automatic scaling.
- **No recent depth updates:** this can mean an inactive market or paused replay. The message alone is not a disconnect diagnosis; wall-clock time never extends recorded heat. The translucent current-book projection represents the last observation only.
- **Profiles cover the live edge:** continuous time reserves their space automatically. In native time mode increase chart right margin or disable a column.
- **Footprint numbers are absent:** enable the footprint, zoom time until buckets are at least about 48 pixels wide, and expand the price scale until rows are at least about 12 pixels high. Increase ticks per row if appropriate. Smaller cells still show imbalance shading.
- **Unsupported native chart message:** use a minute/second chart with at least two visible time bars, or enable the independent continuous seconds viewport.
- **Unexpected gaps with multiple connections:** set Data connection name to the intended price feed's exact name.
- **Blank or failed rendering:** check NinjaScript Output for the `FootprintMap rendering:` error. Optional study failures appear on-chart while the base heatmap remains available. Do not treat a blank chart as a passing runtime test.
- **Capacity warnings:** inspect invalid-event and trimmed-history counts; retained-trade studies and archive history remain limited by the configured capture budgets. Optional flow-marker caps report truncation. Increase budgets only with measured resource headroom.
- **Session calendar unavailable:** feed capture continues; profile/CVD resets wait for a successful calendar lookup. Check the Trading Hours template. Failed lookups retry at most once per minute of market-event time.

The portable MBO engine in the source package accepts explicit order events in replay/tests. NinjaTrader's normal `OnMarketDepth` adapter supplies aggregated depth and is **not connected to that MBO engine**. It does not obtain exchange order IDs, broker IDs or true native iceberg/stop identities. The enabled absorption, sweep and replenishment studies retain their explicit candidate/heuristic labels.
