# Bookmap feature and settings catalogue

Research refresh: **October 26, 2026**. This is a research catalogue, not a claim that every item below is implemented in FootprintMap. See [the implementation coverage](../FEATURE-COVERAGE.md) for delivered code and [the validation report](../validation/REPORT.md) for actual tests.

The current official [knowledge-base sitemap](https://bookmap.com/knowledgebase/sitemap.xml) contained **128 URLs**. We retrieved all **48** pages selected by the current add-on, indicator, and relevant chart/settings URL families, plus the separately named current `Addon-CVD` page: **49 primary pages**, all HTTP 200. Relevant descriptions, configuration sections, limitations and version notes were reviewed. The raw pages, extracted text, image references and retrieval results are retained at `/workspace/research-assets/expansion/`, with `sources.json` as the retrieval manifest. Existing [visual research](bookmap-visuals.md) and [liquidity/bubble research](liquidity-and-bubbles.md) cover the original chart screenshots and additional main-chart settings.

The public marketplace changes and includes third-party products. This dated inventory covers the official documentation collection inspected here; it is not a guarantee that every third-party plug-in or future release is represented. The research records native Bookmap terminology; independent implementations must identify their own algorithms and unsupported settings.

## 1. Data requirements come before display options

| Data class | What it supplies | Features it can support | What it does not establish |
| --- | --- | --- | --- |
| Executions with price, time, quantity and reliable aggressor side | Actual trading, classified by aggressor | Dots, profiles, CVD, absorption-style volume/time marks, sweeps, footprint, tape pace, VWAP | Hidden resting size, participant identity, native iceberg or stop-order flags |
| MBP: market by price | Aggregate displayed quantity at each bid/ask price | Heatmap, COB, weighted liquidity, book imbalance, price-level persistence, liquidity-change/replenishment heuristics | Number/size/age of individual constituent orders; exact cancellations; same-order repricing |
| MBO: market by order, with exchange/provider semantics | Individual IDs and sequenced order events, potentially linked execution identifiers | Individual-order filtering/age/tracking, reconstructed aggressive tapes, some exchange-specific iceberg detection and trade-impact classifications | A universal exchange-independent stop/iceberg classifier; unidentified implied orders; total unexecuted hidden size |
| External market feeds | Options positions, liquidation events, additional instruments | Options Strike, crypto liquidation tools, Cross BBO, correlations, Multibook | These facts cannot be derived solely from the selected futures instrument's L2 feed |
| Account/order/execution events | The user's working orders, fills, positions, fees | Breakeven, DOM order actions, execution management, trade statistics/journal | These are account/trading functions, separate from a passive market indicator |

Changing from a NinjaTrader trial connection to Rithmic does not automatically make order IDs available to a NinjaScript indicator. The actual adapter/API must deliver them. Standard `OnMarketDepth` is a positional aggregate-price stream. An MBO feature must remain explicitly unavailable until a real, compatible source is connected; it must not silently treat aggregate levels as individual orders.

Bookmap itself has exchange-specific restrictions: its native Stops & Icebergs product is documented for **nonaggregated CME MBO via Rithmic or BookmapData**; B3 has a separate **Cedro** product. Liquidity Tracker's individual-order size filtering explicitly excludes Eurex in its current documentation. DAX/Eurex compatibility for generic MBP studies therefore does not imply identical native iceberg capability.

## 2. Main heatmap, trade visualization and chart controls

Sources: [Heatmap settings](https://bookmap.com/knowledgebase/docs/KB-SettingUpAndOperating-HeatmapSettings), [Traded Volume Visualization](https://bookmap.com/knowledgebase/docs/KB-SettingUpAndOperating-HeatmapTradedVolumeVisualization), [Studies configuration](https://bookmap.com/knowledgebase/docs/KB-SettingUpAndOperating-HeatmapStudiesConfiguration), and the additional main-chart/colour/supporting-feature sources in [the visual report](bookmap-visuals.md).

| Component | Documented controls and behavior | Implementation distinction |
| --- | --- | --- |
| Heat palette | Colormap families, legacy grayscale maps, global scheme, dimming, toolbar controls; classic family black/blue through pale/yellow/orange/red | Palette intensity describes resting quantity, not bid/ask side; hottest is red in the documented classic family |
| Heat thresholds | Lower and upper cutoffs in absolute quantity or percentiles; contrast/brightness and automatic contrast adjustments | A minimum display cutoff is different from an upper saturation ceiling and different from order-quality filtering |
| Vertical smoothing | Auto/manual/none; neighboring prices visually averaged | Smoothing does not identify real liquidity; it can make price-following bands broader |
| Aggregate market depth | Cumulative depth rather than individual price-level quantities | Changes the statistic; must be labeled instead of silently replacing normal level size |
| Extended depth | Previously observed levels retained outside currently supplied depth, with stale/reset policy | Estimated/stale coverage is distinct from currently observed depth; cannot invent missing levels |
| Execution dots/bubbles | 2D/3D; total volume/delta; size/transparency; min displayed cluster volume; minimum individual execution; Gradient/Solid/Pie; buyer/seller colors | Resting-depth filters must not remove executions from accounting |
| Grouping | Pixel-time base aggregation; Smart; time; volume; price runs; price-and-aggressor runs; vertical in-pixel aggregation; apply/inherit bar settings | Smart and normalization formulas are proprietary; zoom changes grouping but must conserve source volume |
| Volume bars | Bottom strip, buyer/seller coloring, width and panel height, clustering shared with or distinct from dots | Show traded flow, not standing size; native default width is one pixel, configurable to fifteen in the inspected guide |
| Best bid/ask | Stair-step traces, last-value or min/max-per-pixel behavior; automatic hide threshold and hysteresis when spread is too small | Last traded price is a separate trace; filtered BBO is not necessarily executable BBO |
| Chart interaction | Horizontal/vertical pan, axis zoom, rectangle zoom, wheel anchor behavior, undo zoom, recenter by BBO/trades/last/none, chart synchronization | NinjaTrader supplies its own host navigation; identical Bookmap shell behavior is a separate implementation task |
| Crosshair/tooltips | Time/date/timezone, price, quantity, dot VWAP/volume; selected-period event list and filtering | Aggregated/filtered/stale values need truthful labels and correct hit-testing |
| Layout and recording | Component visibility, candles, subcharts/widgets, refresh rate, recording/replay and historical-data options | Repainting frequency is not event-sampling frequency; historical OHLC does not create historical L2 |

The default-family palette is not one immutable RGB table: users can change maps, thresholds, brightness and element colors. Reproducing every screenshot identically requires its source events, viewport, version, DPI and settings, not only matching a few colors.

## 3. Columns: COB, SVP, CVP and related options

Source: [Columns](https://bookmap.com/knowledgebase/docs/KB-SettingUpAndOperating-Columns). The user called the book column “COP”; Bookmap calls it **COB**, Current Order Book.

| Column or option | Native behavior and settings | Data needed / caution |
| --- | --- | --- |
| COB | Current pending bid/ask size at each price; bars/numbers/both; quantity or visible-book percentage; aggregate cumulative and extended-depth options | MBP; percentages need an explicit denominator; stale extended levels must not appear as fresh depth |
| SVP | Session/subscription executed-volume distribution | Captured or supplied executions; a partial recorded session is not complete history |
| CVP | Executed volume within visible chart range | Retained executions; panning/zooming changes range, independently of bubble grouping |
| CTC/STC | Chart/session trade-event count at each price | Counts of executions, not contract size or reconstructed parent-order count |
| Quotes Counter | Count quote-size updates by price over defined range | Raw quote events; a sampled heat frame cannot recover the number of underlying updates |
| Quotes Delta | Positive/negative liquidity change by price; resets associated with BBO updates in the native description | A reduction may be a trade, cancel, correction or coverage change; “canceled orders” needs stronger evidence |
| Time & Sales | Tape display; side and min/max-size filters; detach; export of up to 10,000 raw events | Reconstructed tape identity and individual fills must not be conflated |
| Trading DOM | Working-order display and order placement/modification/cancel actions | Account/execution integration, beyond merely drawing a book histogram |
| Custom/Cloud Notes | Level annotations and related alert/workflow features | Manual or external note data |
| Shared styling | Add/hide/reorder/resize columns, left/right placement, combined/split histograms, side visibility, colors, alignment/inversion, delta, labels | Treat each column's quantity and scale separately |
| Reset and reference | Manual/scheduled resets; conditional price-inactivity reset; optional double-click reset; VWAP and supported session POC lines | Define clock, session and reset scope; do not reset unrelated raw market history |

Native book/profile columns can follow the **visible right edge** during historical navigation. A live COB drawn alongside an old viewport must say it is live; a right-edge historical book needs retained historical book state. “Session profile” and “visible profile” are not interchangeable.

## 4. Execution-pattern and liquidity indicators

### Absorption

Source: [Absorption Indicator](https://bookmap.com/knowledgebase/docs/KB-Indicators-Absorption).

- Detection is **same-side executed volume at one price within a configurable time**, compared with a minimum size. Buyers and sellers are calculated separately. The passive absorbing side is opposite the execution aggressor.
- Calculation settings are per instrument: time, size, automatic mode, SD interval in minutes and SD multiplier. Automatic mode replaces manual size entry. The public page does not fully specify statistical sampling/window-boundary details.
- Visual settings are shared across instruments: show icons/volume, icon size and offset, dotted offset-line width, passive-buyer/seller icon/text/background colors, horizontal/vertical pixel aggregation, show trade dots, dot size and circle/rectangle shape.
- Nearby same-side marks merge visually, and zoom can separate them. Dragging an icon changes global icon offset; right-click opens settings.
- Sound and Telegram alerts have enable/disable, custom text templates, reset/test and threshold-related settings. External messaging is a separate integration, not a prerequisite for calculation.
- The vendor supports instruments with suitable executions, including futures/stocks/crypto, but warns that aggregated backfill can differ from live detections. This is not proof of a native iceberg or guaranteed reversal.

### Sweeps

Source: [Sweeps Indicator](https://bookmap.com/knowledgebase/docs/KB-Indicators-Sweeps).

- Same-side executed volume across **at least a configured number of distinct traded prices** within the configured time, compared with a size threshold. The public description does not impose an additional universally specified monotonic-price condition.
- Time, size, minimum price-level count, automatic SD interval/multiplier, alerts and visual settings mirror Absorption, but marker side represents the **aggressor**.
- A price span of five ticks is not automatically five traded price levels: gaps must not satisfy a distinct-level count.
- Trade clustering, screen aggregation and detection windows are separate operations. A visually merged icon is not necessarily one original market order.

### Large Trades Alert

Source: [Large Trades Alert](https://bookmap.com/knowledgebase/docs/Addons-Large-Trades-Alert).

The default reconstructs a tape by **aggressor ID**. A 10-contract aggressive order filled as 5+3+2 is one 10-contract tape. With **Individual trade events** enabled, none of those fills individually reaches a 10-contract threshold. The threshold is inclusive (`>=`); alert types are sound, popup or both. An ordinary NinjaTrader implementation without aggressor IDs can provide individual large-execution marks/alerts, but cannot claim the default reconstructed-order behavior.

### Strength Level / replenishment

Source: [Strength Level Indicator](https://bookmap.com/knowledgebase/docs/KB-Indicators-Strength-Level-Indicator).

- **Underflow:** compare executed size with observed pending quantity; executions exceeding it suggest hidden/replenished liquidity.
- **Resistance:** measure size added shortly after executions, with configurable delay/minimum size. Automatic SD interval/multiplier, buyer/seller colors and voice/size-filtered alerts are documented.
- These are heuristics. The vendor explicitly describes coincidental unrelated size increases as false positives, dependence on event ordering/data quality, and inability to reveal still-unexecuted hidden resting size.
- The page discusses **5 ms**, and potentially **0.5 ms**, replenishment delays. A detector using 250 ms time-averaged heat frames must be labeled a coarser approximation; those frames do not preserve the microsecond sequence needed for such timing.

### Liquidity Marker

Source: [Liquidity Marker](https://bookmap.com/knowledgebase/docs/Addons-Liquidity-Marker).

Two modes: (1) significant level-size change over **five seconds**, (2) maximum-liquidity price levels. Controls include display threshold, auto calculation, top-N markers/levels, near-BBO exclusion, increases/decreases visibility and separate foreground/background colors. Auto threshold uses the active timeline, with a documented fallback up to the last thirty minutes. Overlapping markers combine quantity/count and split when zoomed in. The vendor warns that finite-depth range edges can create false changes and explicitly excludes extended-depth mode.

### Liquidity Tracker (LT Free / LT Pro)

Source: [Liquidity Tracker](https://bookmap.com/knowledgebase/docs/Addons-LT-Pro). Despite the URL, the current page title is Liquidity Tracker. It is distinct from **Large Lot Tracker**.

LT sums each side's depth with exponentially decreasing **price-distance** weights from BBO. For distance `d` in tick levels and half-life `h`, the documented weighting corresponds to `2^(-d/h)`. Controls include market-depth/half-life, optional MBO, individual-order min/max size when supported, compute in money units, average per nonempty price level, smooth lines and bid/ask/imbalance colors. The page's introductory wording mentions recent/historical weighting, but its mathematical/technical explanation specifically defines distance from the best price; it should not be implemented as an unrelated time decay.

MBP liquidity calculations support general depth instruments. The individual-order filter is documented only for **CME through Rithmic/BookmapData**, explicitly excluding Eurex and CQG for that feature. Neither a level-size filter nor an average over anonymous levels is an individual-order filter.

## 5. Delta, imbalance, anchored values and profiles

| Study | Documented settings / behavior | Source |
| --- | --- | --- |
| CVD | Buyer-aggressor minus seller-aggressor volume; Symmetrical/Include 0/Compact scale; multiple indications; min/max individual size; chart/session range; reference/reset times/frequencies; reset now; split buyers/sellers and colors | [Current CVD add-on](https://bookmap.com/knowledgebase/docs/Addon-CVD), compared with the [legacy guide](https://bookmap.com/knowledgebase/docs/KB-Indicators-CVD) |
| Orderbook Imbalance | `(bid−ask)/(bid+ask)`; selected absolute/percentage/all depth range; exponentially lower weight farther from BBO; sound threshold; visible-right-edge book | [Orderbook Imbalance](https://bookmap.com/knowledgebase/docs/Addons-Orderbook-Imbalance) |
| Volume Imbalance | `(buy-aggressor−sell-aggressor)/(sum)` over visible trading; calculation range, smoothing and sound threshold | [Volume Imbalance](https://bookmap.com/knowledgebase/docs/Addons-Volume-Imbalance) |
| AVWAP | Multiple named anchors at current time or session; editable/additional anchors; standard/custom symmetric deviation bands; show/hide, color, width; right-click interaction | [AVWAP](https://bookmap.com/knowledgebase/docs/Addons-AVWAP) |
| Price Levels | High/low/mid plus arbitrary percentage positions; enable/color/name/delete; scheduled AT and periodic EVERY resets; configurable period | [Price Levels](https://bookmap.com/knowledgebase/docs/Addons-Price-Levels) |
| Footprint | Time bars 30 seconds–30 minutes, reversal/range/volume bars; adjustable bar width; sum/delta/diagonal-delta/buy-sell display; histogram and text foreground/background; automatic price/time scaling and bar statistics | [Footprint](https://bookmap.com/knowledgebase/docs/Addons-Footprint) |
| Market Profile | TPO letters per thirty-minute block at traded prices; main RTH, secondary ETH and tertiary sessions; price aggregation, POC/VAH/VAL, corresponding volume profile and initial-balance periods | [Market Profile](https://bookmap.com/knowledgebase/docs/Addons-Market-Profile) |
| Outside Liquidity | Off-screen quantities at left/right viewport times; bid/ask, difference or both; maximum levels; include visible levels; include price zero; farther-level weighting and tick half-life | [Outside Liquidity](https://bookmap.com/knowledgebase/docs/Addons-Outside-Liquidity) |

Zero denominators and unknown aggressor volume need explicit treatment. Unknown-side trades can contribute to total volume and VWAP while remaining excluded from signed imbalance. Missing depth is not zero liquidity. TPO counts time blocks visiting a level; it is not traded volume, and a single print does not prove all intervening prices traded.

AVWAP's page documents a native timezone issue. An independent implementation should keep anchors and event clocks consistent rather than reproduce the bug. A rolling-window VWAP, session VWAP and arbitrary anchored VWAP must be identified separately.

## 6. MBO-dependent tools and native iceberg distinctions

### Stops & Icebergs

Sources: [Stops & Icebergs main chart](https://bookmap.com/knowledgebase/docs/Addons-Stops-And-Icebergs-On-Chart-Indicator), [Stops & Icebergs subchart](https://bookmap.com/knowledgebase/docs/Addons-SIT).

The current guide distinguishes production **SI On-Chart** and **SI Sub-Chart**, plus an **alpha combined SI** for Bookmap 7.9. The combined alpha includes Smart/Manual configuration; it is not proof that the production UI has the same options.

On-chart iceberg events use **D** detection, **T** trade, **E** fully executed and **C** cancellation. A line tracks the order's active price history; an active icon stays at its current price. Numbers distinguish cumulative traded volume from the current event's bracketed volume. A cancellation label reports already traded volume, **not unknown hidden remainder**. Stop chains combine triggered executions and show the swept price range; their side/orientation differs from passive iceberg marks.

Settings include show/hide stops and icebergs separately; individual traded-size threshold; aggregated-icon threshold; applying thresholds to active orders; event-type visibility; offsets, icon/line/text colors and sizes; horizontal/vertical visual aggregation; separate alert thresholds and time windows; text/voice/Telegram configuration. Alpha Smart mode adapts individual thresholds; Manual mode may align or separate icons and subchart configuration.

Subchart accumulation modes are **SUM**, **EXPONENTIAL** with time half-life, **RESET** every T, **SLIDING** window T, and **SAME_SIDE_SUM** until opposite-side events. Order-size filters and alert thresholds are distinct; thresholding an iceberg's evolving known size can make native on-chart and subchart totals differ. The documentation explicitly warns about packet/event aggregation, missing MBO, reset timestamps and version differences. CME implied orders lack MBO in the described feed.

A generic MBO ledger can track IDs and observed replenishment candidates. Calling them exchange-native icebergs requires verified exchange/provider event semantics. Stop orders often enter the visible stream only after triggering; sweeping behavior alone does not prove a stop order. Neither feature should be inferred from a green/red bubble's appearance.

### B3 Icebergs

Sources: [B3 On-Chart](https://bookmap.com/knowledgebase/docs/Addons-B3-Icebergs-On-Chart-Indicator), [B3 Sub-Chart](https://bookmap.com/knowledgebase/docs/Addons-B3-Icebergs-Sub-Chart-Indicator).

These are separate **Cedro real-time** tools with their own data-adapter requirements, not an automatic consequence of CME support. They provide iceberg lifecycle marks, active-order tracking, individual/aggregate thresholds and visual settings; subchart accumulation/filter/alerts are analogous to the documented iceberg family. They do not turn arbitrary futures depth into universally identified icebergs.

### Large Lot Tracker

Source: [Large Lot Tracker](https://bookmap.com/knowledgebase/docs/KB-Indicators-LLT). This tracks large individual orders and their histories/indications. Treat order-size, lifecycle and queue claims as requiring actual order identity. It is distinct from a large aggregate level, a large execution, Liquidity Tracker, and a native iceberg.

### Trade Impact

Source: [Trade Impact](https://bookmap.com/knowledgebase/docs/Addons-Trade-Impact).

This reconstructs all fills of one aggressive order into one tape and uses MBO to identify passive counterparties. Four classes are documented: **Fragmented**, **Concentrated**, **Large order survived**, and **Large order consumed**. Large-order classes take priority. Hover details include tape volume/fills and, when known, passive order size, filled/remaining size and age.

Controls include Automatic/Manual thresholds, tape threshold, large-order threshold with optional linkage, training period (documented default sixty minutes), and fragmented threshold (documented default twenty orders). The page's manual default examples are 150 contracts for tape and 25 for a large order; these are native defaults for that page, **not universal thresholds recommended for ES, currencies or DAX**. Visual controls include show volume/remaining size, buyer/seller colors, offset lines and horizontal/vertical icon aggregation. Merged marks choose a representative glyph by documented priority; zoom separates marks. Anonymous MBP cannot supply this counterparty classification.

## 7. Market Pulse: a family of algorithms, not one indicator

Sources: [Market Pulse Desktop](https://bookmap.com/knowledgebase/docs/Addons-Market-Pulse), [Market Pulse Web](https://bookmap.com/knowledgebase/docs/Addons-Market-Pulse-Web).

Widgets may use a subscribed instrument or an external source. Common controls include algorithm/instrument selection, name, widget size, start/stop, mute, threshold, color, bar inversion, audio files, sensitivity/coefficient/volume, panel grouping/reorder and subchart visibility. Some settings are algorithm-specific: time half-life, training period, percentage-versus-absolute priority, historical loading and price-distance half-life.

| Algorithm family | Documented input and interpretation | Distinction |
| --- | --- | --- |
| Volume Pressure / Imbalance | Buyer/seller execution volume decaying with time half-life; signed comparison | A pressure accumulation is not raw session CVD |
| Price Change | Last-price deviation from average over training interval, normalized relative to observed deviation | General execution prices; percentage is learned relative scale |
| Spread Change | Spread change and variability, with configured range/normalization | Requires actual bid/ask; last-price movement alone is insufficient |
| Liquidation Pressure | Reported liquidation events, time decay and normalized display | External crypto liquidation data |
| Orderbook Pressure / Imbalance | Changes in bid/ask liquidity, with distance-to-BBO weighting and time decay | Different from weighted **current** book size; finite-depth transitions need coverage handling |
| Absorption Pressure / Imbalance | Events from Absorption, time decay; passive-side pressure | Detector settings affect downstream pressure |
| Sweeps Pressure / Imbalance | Events from Sweeps, time decay; aggressive-side pressure | Separate passive/active sign convention |
| Stops Pressure | Executed-stop events from compatible SI engine | Requires supported source; not general sweep volume |
| Icebergs Pressure | Detected iceberg executions from compatible SI/B3 engine | Requires appropriate MBO and event semantics |
| Orderbook Imbalance | Selected depth-range bid/ask ratio | A current-stock measure, distinct from change-pressure |
| Pace of Tape / Imbalance | Activity of tape/trade events with individual-trade option | Parent tape count versus individual-fill count differs without IDs |
| Multi Algo | Combines percentage values from running widgets | Depends on chosen inputs and normalization; not one universal predictive formula |

For volume pressure, the documented time half-life reduces old event contribution by half every T. The training period controls how current activity compares with the observed maximum in that period; it is not interchangeable with an arbitrary z-score. The native full interaction/normalization behavior is not entirely disclosed. Market Pulse Web adds browser workspaces, delayed/live access distinctions, audio/browser restrictions and a heatmap widget; a desktop NinjaTrader indicator cannot reproduce its remote service merely by calculating local values.

## 8. Other data overlays and technical studies

| Tool | Available controls / purpose | Requirement and scope |
| --- | --- | --- |
| [Cross BBO](https://bookmap.com/knowledgebase/docs/Addons-Cross-BBO) | Overlay another subscribed instrument's bid/ask and last; select sources, line visibility and colors | Additional subscriptions; explicit price/unit conversion where necessary |
| [Correlation Tracker](https://bookmap.com/knowledgebase/docs/KB-Indicators-CorrelationTracker) | Multiple price overlays; mid-price or last; reference resets, color/line shape | Synchronized multi-instrument data; overlay is not itself a statistically specified correlation coefficient |
| [Multibook Customizer](https://bookmap.com/knowledgebase/docs/Addons-Multibook) | Up to five constituent instruments/exchanges; tick size and size granularity; custom synthetic instrument | Normalize price, quantity and clocks; independent feeds needed |
| [Instrument Copy](https://bookmap.com/knowledgebase/docs/Addons-Instrument-Copy) | Independently configured views of the same subscribed source, copies named `@Copy`, history constraints | Source remains subscribed; current docs document replay/add-on restrictions |
| [Liquidation Indicator](https://bookmap.com/knowledgebase/docs/Addons-Liquidation-Indicator) | On-chart/subchart, base/quote currency, threshold, source selection, aggregation, offsets/colors, split sides, accumulation and alerts | Only supported crypto venues/instruments with actual liquidation events; a futures sell burst is not proof of liquidation |
| [Options Strike](https://bookmap.com/knowledgebase/docs/Addons-Options-Strike) | SPX market-maker gamma exposure; ES/MES conversion Auto/Manual offset/ratio; 0DTE/all expirations; Candle profile/simple bars; calls/puts/net; body/wick/open/prior markers; colors/thickness/opacity; square-root scale, auto thickness, labels/hover; gamma-flip cluster settings and alerts | External OptionsDepth data, documented one-minute updates/delivery delay; specifically ES/MES, not an all-instruments L2 feature |
| [TA Indicators](https://bookmap.com/knowledgebase/docs/Addons-TA-Indicators) | ADX, MACD, OBV, RSI, Williams %R; bar duration/synchronization/count and indicator-specific periods | Price/volume technical studies; many have native NinjaTrader counterparts rather than requiring duplication inside the heatmap |
| [CATS](https://bookmap.com/knowledgebase/docs/Addons-CATS) | Current fetched page is titled Customizable Advanced Times & Sales but provides no substantive settings text | Insufficient public specification on that page; do not invent a detailed feature contract |

Options Strike is a useful example of why “all Bookmap features on every instrument” cannot be satisfied merely by generalizing tick size. Its data is SPX options exposure, converted for ES/MES; pretending the same data were DAX or currency-futures gamma would be incorrect. Its bar colors describe long/short gamma or option types, not standard trade aggression.

## 9. Trading, account and workflow products

These are part of Bookmap's ecosystem, but they are not passive order-flow calculations. Researching them does not authorize sending live orders or messages.

| Product | Documented capabilities/settings | Additional integration |
| --- | --- | --- |
| [DOM Pro](https://bookmap.com/knowledgebase/docs/Addons-DOM-Pro) | Price ladder, recent trades/LTQ and split profiles; visible columns, resets, graphics, LMT offset, shortcuts; order entry/move/resize/cancel | Broker/account actions and independent execution testing |
| [Execution Pro](https://bookmap.com/knowledgebase/docs/Addons-Execution-Pro) | Bracket behavior on open/flat, resize after partial fills, breakeven/trailing stops, order resizing, payup/move limits, shortcuts | Strategy/order-management lifecycle, not chart-only code |
| [Multibrackets](https://bookmap.com/knowledgebase/docs/Addons-Multibrackets) | Multiple TP/SL tiers, quantity/tick offsets, presets; overrides TCP bracket configuration | Native docs restrict live connections; simulated and live modes differ |
| [Multi-Account Trading](https://bookmap.com/knowledgebase/docs/Addons-Multi-Account-Trading) | Primary/subaccount replication, bots, order/position monitoring, sizing and cross-trading constraints | Documented live Rithmic multiaccount access; requires deliberate account selection |
| [Breakeven Point](https://bookmap.com/knowledgebase/docs/Addons-Breakeven-Point-Indicator) | Position/session break-even, reset on flat, long/short colors/width; commissions per-contract round-turn/one-side, per-order or percent; ticks/quote currency | User fills/position/fee schedule, not market-wide executions |
| [Trading Statistics](https://bookmap.com/knowledgebase/docs/Addons-Trading-Statistics) | Global/position stats, P&L, entry/exit, filled/canceled counts, post-exit analysis; columns/visual settings; journal integration | Account trading records and optional external storage |
| [Trading Journal](https://bookmap.com/knowledgebase/docs/Addons-Trading-Journal) | Journals, notes, trade records, date/instrument filters, P&L analytics and sharing | Remote application/account, distinct from local indicator history |
| [Trading Simulator](https://bookmap.com/knowledgebase/docs/Addons-Trading-Simulator) | Simulated order types/operations, brackets/OCO, reverse, balance/reset, commissions and journal | Current guide routes through BookmapData adapter; fills require an execution model |
| [Setups Checklist](https://bookmap.com/knowledgebase/docs/Addons-Setups-Checklist) | Manual patterns, enable and 0–10 scores, aggregate score, CSV import/export/edit, reset, always-on-top/collapse | User workflow rather than automatic market detection |
| [Python API](https://bookmap.com/knowledgebase/docs/Addons-Python-API) | Developer-authored studies/automation; supported API and provider limitations | Bookmap runtime integration, not a NinjaTrader-language setting |

## 10. Instrument independence and practical acceptance criteria

General studies should use the instrument's **actual TickSize, quantity units and trading-hours template**, never branch on a small hardcoded symbol list. ES/NQ/RTY/YM/GC are initial examples, not the only permitted instruments. Currency futures commonly appear as **6E** and **6B**; a user or provider may use another alias. DAX family contracts have their own symbol mappings/tick values, and mini/micro variants can differ. Runtime metadata should be authoritative; this research does not substitute guessed contract specifications for the exchange/provider instrument definition.

Compatibility has distinct levels:

1. Numerical support: tick normalization and labels handle small FX increments, ordinary index increments and valid zero/negative prices where the product/feed permits them.
2. Data support: chosen account/exchange actually provides depth/trades, enough levels and suitable timestamps; entitled CME data does not imply Eurex data.
3. Calculation support: no one fixed contract-size threshold is suitable for all symbols; provide per-instance minimums, time windows, depth range and optional well-defined adaptive references.
4. Host support: NinjaTrader callbacks, chart transforms, reset behavior and rendering are verified on the real installed version.
5. Specialized semantics: actual MBO/order IDs, native iceberg/stop information or external datasets are validated for the particular exchange and adapter.

A useful comparison should preserve the same raw events across views. Changing heatmap filters should change heat tiles, not accepted trades/profile/CVD totals. Marker aggregation should be view-only. Zoom must reproject all overlays and allow aggregated markers to separate. Historical navigation must state whether each column is historical-right-edge, visible-range, session-to-edge or current-live.

## 11. Visual and motion evidence boundary

The earlier research actually inspected official chart/settings screenshots, including Bot Remover's raw/filtered comparison and millisecond-scale bubbles. Those observations are recorded in [the visual report](bookmap-visuals.md) and [the liquidity report](liquidity-and-bubbles.md). This catalogue additionally downloaded the current pages containing image/video references. **Retrieving a page with an embedded video is not the same as watching that video.** Any later demonstration review must record the exact accessible asset and what was actually viewed, distinguishing a screenshot, sampled video frames, playback and audio.

Expected dynamic comparisons include: a stable wall remaining at a fixed price when BBO moves; quote updates changing future heat only; confirmed filtering after a warm-up rather than retrospective perfect knowledge; dots merging/splitting with zoom; passive absorption versus aggressive sweep markers; columns changing with their documented reset/range; and genuine coverage gaps remaining blank after reconnect. A synthetic demonstration tests those mechanisms but cannot prove identical Bookmap output on a real feed.

## 12. Source inconsistencies and undisclosed details

- Legacy/current documentation can show different menu locations. CVD and imbalance migration from Studies to add-ons is documented; screenshots from different versions must not be combined into an invented single current UI.
- LT Pro's current title is Liquidity Tracker, distinct from Large Lot Tracker. Its precise half-life explanation is price-distance based despite less precise introductory prose.
- Public descriptions do not fully specify Smart bubble clustering, Bot Remover, auto-SD sampling, all native thresholds, or native iceberg/stop classifiers. Independent approximations should expose their own deterministic definitions.
- Absorption, Strength Level, and SI are different evidence classes. Strong executions at one price or rapid replenishment are useful observations; neither alone establishes the same identified native iceberg.
- Advertised real-time/performance capabilities are vendor claims. They are not measurements of this NinjaTrader implementation.
- Some official pages provide only partial specification, notably CATS. Missing detail should remain marked as unknown, rather than filled with plausible-looking invented controls.

This catalogue establishes a concrete inventory for implementation and review. Actual delivered features, supported settings, deferred data adapters and completed host tests belong in the implementation matrix and validation report, rather than being inferred from this research list.
