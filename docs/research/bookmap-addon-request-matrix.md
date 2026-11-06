# Requested Bookmap add-ons, labels and data boundaries

Reviewed October 30, 2026, against the user's complete Advanced Add-ons screenshot and revision `4cbf041`; display-snapshot descriptions updated for the November 6 changes. This inventory distinguishes native Bookmap products from the independent FootprintMap features. It is not a claim that the screenshot's entire product catalogue has been implemented. The maintained [feature coverage](../FEATURE-COVERAGE.md) records the delivered source as subsequent revisions change it.

The screenshot contains chart studies, order-ID tools, external-data products, trading/account tools and workflow applications. Some can share captured futures executions and price-level depth; others require inputs that this NinjaTrader indicator does not receive. Implementing a control with the same name would not provide the missing data or reproduce a native algorithm.

## Every item in the screenshot

“Partial” means there is useful related functionality, with the actual differences named below. “Not implemented” does not necessarily mean impossible; it identifies further work or a missing input rather than claiming completion.

| Requested product | FootprintMap implementation at review | Remaining behavior or input |
| --- | --- | --- |
| [Cumulative Volume Delta (CVD)](https://bookmap.com/knowledgebase/docs/Addon-CVD) | Captured-session buyer-aggressive minus seller-aggressive volume; historical curve, zero reference, stable or fixed scale, volume bars | No arbitrary multiple CVD instances, every native reset mode, individual-size-filtered CVD or separate buyer/seller accumulation UI. Unknown-side volume contributes no signed delta. |
| [Large Trades Alert](https://bookmap.com/knowledgebase/docs/Addons-Large-Trades-Alert) | Individual large-print markers, labeled `LP`, with a size threshold | No sound/popup alert workflow or reconstructed parent-order tape. Bookmap's default tape grouping by aggressor ID differs from the optional individual-trade-event mode; our MBP input cannot supply missing IDs. |
| [Liquidity Marker](https://bookmap.com/knowledgebase/docs/Addons-Liquidity-Marker) | Large aggregate-level highlights and optional `+L`/`−L` changes between adjacent completed depth frames | Not the complete native five-second/change and maximum-level modes, automatic threshold, top-N aggregation or icon settings. Finite-depth boundaries are excluded from inferred additions/removals. |
| [Market Pulse Price Change](https://bookmap.com/knowledgebase/docs/Addons-Market-Pulse) | Not implemented | A separately defined rolling price-deviation/normalization study is feasible from captured trades. The native trained widget, audio and normalization behavior have not been reproduced. |
| [Setups Checklist](https://bookmap.com/knowledgebase/docs/Addons-Setups-Checklist) | Not implemented | Manual setup names, scores, CSV import/export and workflow UI; it is not an automatic order-flow detector. |
| [Technical Analysis: %R, RSI, OBV, MACD, ADX](https://bookmap.com/knowledgebase/docs/Addons-TA-Indicators) | Not implemented inside FootprintMap | NinjaTrader has conventional technical indicators that can be added separately. A combined implementation would still need explicit bar construction/period settings and alignment with the independent time viewport. Existing native indicators are not claimed as this project's code. |
| [Cross BBO](https://bookmap.com/knowledgebase/docs/Addons-Cross-BBO) | Current instrument BBO is implemented; cross-instrument overlay is not | Additional instrument subscriptions, synchronized quote histories and explicit price/unit conversion. |
| [Instrument Copy](https://bookmap.com/knowledgebase/docs/Addons-Instrument-Copy) | Separate NinjaTrader charts can host independent FootprintMap display settings | No Bookmap-style shared subscription/copy manager. Instances with the same capture identity use the same archive path; this is not an archival recording for each chart copy. |
| [Outside Liquidity](https://bookmap.com/knowledgebase/docs/Addons-Outside-Liquidity) | Not implemented as an outside-viewport summary | A sum of actually supplied off-screen rows is feasible. It must disclose coverage and selected time; unavailable depth is not zero, and current rows cannot reconstruct an old book. |
| Point of Control Indicator | Session or visible-scope volume POC, plus contiguous value-area levels | No claim of every native POC reset/alert/multi-instance option. POC ties choose the lower price under the independent documented convention. |
| [AVWAP](https://bookmap.com/knowledgebase/docs/Addons-AVWAP) | Captured-session or visible-scope VWAP and deviation bands | Arbitrary named anchors, multiple anchor paths, edit/delete and native interaction are not implemented. Session VWAP is not equivalent to arbitrary AVWAP. |
| [Price Levels](https://bookmap.com/knowledgebase/docs/Addons-Price-Levels) | POC/VAH/VAL/VWAP reference levels exist | No generic high/low/mid and arbitrary percentages with scheduled AT/EVERY resets or user-created named price levels. |
| [Trading Journal](https://bookmap.com/knowledgebase/docs/Addons-Trading-Journal) | Not implemented | User fills, notes, account/application storage and analytics. The saved market capture is not a trading journal. |
| [Trading Statistics](https://bookmap.com/knowledgebase/docs/Addons-Trading-Statistics) | Captured market-volume/imbalance statistics only | No account fills, strategy P&L, entries/exits, canceled-order statistics or post-exit trade analysis. Market executions are not the user's trades. |
| [Liquidation Indicator](https://bookmap.com/knowledgebase/docs/Addons-Liquidation-Indicator) | Not implemented | Requires actual liquidation-event data from supported venues. A fast futures sweep or sell burst cannot be relabeled as a liquidation. |
| [Order Book](https://bookmap.com/knowledgebase/docs/Addons-Orderbook-Imbalance) & [Volume Imbalance](https://bookmap.com/knowledgebase/docs/Addons-Volume-Imbalance) | Displayed-snapshot book imbalance with selected depth/distance weighting; captured session/visible execution imbalance | No complete native independent subcharts, smoothing or threshold alerts. Pausing freezes the book snapshot alongside other panels; panning earlier does not reconstruct a different book snapshot at that historical right edge. |
| [Large Lot Tracker](https://bookmap.com/knowledgebase/docs/KB-Indicators-LLT) | Standalone MBO engine can retain qualifying order-ID traces | Not connected to the NinjaTrader chart or a live MBO source. A large MBP level is the sum of orders and is not an identified individual large lot. |
| [Strength Indicator](https://bookmap.com/knowledgebase/docs/KB-Indicators-Strength-Level-Indicator) | Optional coarse `RP?` replenishment candidate from sampled MBP and executions | No native millisecond underflow/resistance analysis, automatic training, voice alerts or reliable order-ID evidence. The native documentation itself warns about unrelated additions and false positives. |
| [Absorption Indicator](https://bookmap.com/knowledgebase/docs/KB-Indicators-Absorption) | Same-aggressor volume at a fixed price in a configurable interval; `AB` marker uses the opposite, passive side | Independent heuristic, not proven hidden liquidity. No complete native auto-SD tuning, icon clustering/dragging, alert integrations or every appearance setting. |
| [Break Even Point](https://bookmap.com/knowledgebase/docs/Addons-Breakeven-Point-Indicator) | Not implemented | Requires the user's position, execution prices and commissions. The market VWAP cannot substitute for an account break-even price. |
| [Correlation Tracker](https://bookmap.com/knowledgebase/docs/KB-Indicators-CorrelationTracker) | Not implemented | Synchronized additional instruments, reference resets and normalization. A multi-price overlay is not automatically a statistically specified correlation coefficient. |
| [DOM Pro](https://bookmap.com/knowledgebase/docs/Addons-DOM-Pro) | Read-only COB/profile columns | No order entry/move/cancel, account selection, shortcuts or full trading ladder. These require a separate account/execution integration and execution testing. |
| [Execution Pro](https://bookmap.com/knowledgebase/docs/Addons-Execution-Pro) | Not implemented | Brackets, partial fills, stop movement, pay-up and automated order management are outside the current chart-only adapter. |
| [Footprint](https://bookmap.com/knowledgebase/docs/Addons-Footprint) | Captured fixed-time bid × ask cells, unknown volume, price grouping and diagonal imbalances | No complete native range/reversal/volume bars, all display/statistics variants or independently resizable footprint shell. |
| [Multi-Account Trading](https://bookmap.com/knowledgebase/docs/Addons-Multi-Account-Trading) | Not implemented | Deliberate account mapping, sizing, broker support, position/order replication and failure recovery. Installing this passive indicator does not connect or trade accounts. |
| [Multibrackets](https://bookmap.com/knowledgebase/docs/Addons-Multibrackets) | Not implemented | Multi-tier TP/SL order lifecycle and account integration. No orders are submitted by FootprintMap. |
| [Multibook Customizer](https://bookmap.com/knowledgebase/docs/Addons-Multibook) | Not implemented | Multiple live books with compatible prices, quantity units, clocks and source identifiers; separate subscriptions are needed. |
| [Sweep Indicator](https://bookmap.com/knowledgebase/docs/KB-Indicators-Sweeps) | Same-aggressor executions across at least a configured number of distinct prices in a short window; `SW` marker | Independent detector; not a reconstructed parent market order or stop classification. No complete native auto-SD/alert/icon aggregation suite. |
| MBO Bundle: [Stops & Icebergs](https://bookmap.com/knowledgebase/docs/Addons-Stops-And-Icebergs-On-Chart-Indicator), [Liquidity Tracker Pro](https://bookmap.com/knowledgebase/docs/Addons-LT-Pro), TraderMap Pro, full Market Pulse algorithms | Standalone sequenced MBO ledger, order-size/age filtering, traces and same-ID refill candidates; separate MBP persistence/following filters and current weighted-book statistics | No live MBO bridge, native stop/iceberg classifier, full Tracker chart, proprietary Bot Remover or complete Market Pulse family. Native product data/connection restrictions apply even within Bookmap. |

The [expanded native catalogue](bookmap-feature-catalogue.md) records additional settings and source differences. A separate UI or proprietary algorithm is not considered completed because an approximate calculation has a related name.

## What the chart labels mean

| Label | Quantity and interpretation | What it does not prove |
| --- | --- | --- |
| `SW 51` or `SW 356` | 51 or 356 captured contracts/units of one aggressor side across enough distinct executed prices within the sweep window | Not 51/356 individual orders; not necessarily a single parent order, institutional trader or stop run |
| `AB 100` | 100 captured contracts/units of one aggressor side executing at one price within the absorption window; marker side describes the passive absorber | Not 100 hidden contracts or a confirmed iceberg |
| `LP 75` | One supplied execution event of 75 contracts/units | Not necessarily a complete parent market order; a provider may split or aggregate its executions |
| `+L 120` / `−L 120` | An increase/decrease of 120 in adjacent completed sampled level quantities | Not a verified order addition/cancellation; fills, replacements and event timing can contribute |
| `RP? 150` | Coarse replenishment candidate with 150 captured executions and substantial sampled displayed quantity remaining at that price | The question mark is intentional; not native iceberg detection or hidden reserve size |
| `POC`, `VAH`, `VAL` | Highest-volume profile price and configured contiguous value-area high/low for the selected scope | Not guaranteed support/resistance, and a partial recorded session is not full exchange history |
| `VWAP`, deviation bands | Captured volume-weighted average price and selected standard-deviation multiples | Not an account entry/break-even price or an arbitrary anchored VWAP |
| `CVD` | Captured buyer-aggressive volume minus seller-aggressive volume for the active session | Does not include unknown-side executions in signed delta; cannot recover uncaptured historical trades |
| `COB` | Supplied bid/ask resting quantities at the displayed snapshot, frozen when paused, subject to configured display rows/cumulative/percentage options | Not individual order count or every unseen exchange level |
| `SVP · recorded` / visible profile | Accepted executed volume by price, accumulated over the recorded session / retained visible range respectively | Resting liquidity is not executed volume; “recorded” discloses that prior unrecorded session volume is unavailable |
| `run 27 (last 9)` | 27 consecutive captured contracts/units at the latest execution price; 9 in the last supplied print | Not the unresolved native Bookmap bracketed label, an order count, resting liquidity or iceberg size |

At revision `4cbf041`, sweep defaults were **50 contracts, 500 ms, three distinct executed price levels**. Absorption defaults were **100 contracts over 2,000 ms at one price**; large-print threshold was **50**. These are adjustable starting thresholds, not appropriate universal values for every futures contract. A `SW 356` can exceed the threshold because whole executions are counted; an execution is never split merely to stop the marker at exactly 50. A bare number without its nearby prefix should be interpreted from its layer/hover context, not guessed to be a sweep.

Absorption and sweep markers are independent of bubble clustering. Showing a bubble and a marker near the same trade does not double-count the underlying session volume. A marker being covered is a rendering/readability problem, not evidence that the event vanished. Disable a study's marker visibility without changing recorded executions when a clearer chart is preferred.

## Why two different quantities can look equally red

The color scale has a ceiling. Under the earlier automatic **97th-percentile** setting, a small minority of large quantities is intentionally clipped to the hottest color. If that calculated ceiling is 47, both 47 and 127 map to the top color. That follows from saturation; it does not imply equal raw quantities. The legend's maximum and hover quantities are needed to interpret it.

The current COB is a present snapshot; heatmap history contains time-weighted frame averages and may combine time or price bins at a wider zoom. Therefore a COB quantity of 127 is not necessarily the historical heat-bin quantity directly to its left. Persistence/following filters can further affect the displayed heat while leaving COB raw.

Gamma changes the distribution of normalized heat intensity. A larger exponent darkens midrange values, while a smaller exponent brings them toward warmer palette colors. This can look like “more blue / less blue” rather than turning the whole image uniformly brighter. Opacity/dimming controls overall visibility against the background. Black cutoff removes values below a fraction of the selected ceiling; an absolute quantity threshold is a different filter. The October 30 rendering changes should name these controls according to what they actually do and preserve optional percentile/manual modes.

For comparisons of absolute size, use a fixed manual ceiling covering the meaningful quantities, or a maximum-based automatic scale. A percentile mode is useful when the objective is to emphasize the upper part of a distribution, but its saturated colors cannot distinguish every larger value. There is no single best absolute contract threshold for ES, NQ, GC, currency futures and DAX; defaults should adapt to the instrument's observed distribution without inventing hidden depth.

## Recording, disk use and reopening

The source records **captured market data**, not screen video. With **Save and restore captured history** enabled, a background worker writes a retained snapshot every 30 seconds and on removal/termination. The default folder is the actual NinjaTrader user-data directory plus `FootprintMapHistory`, commonly `Documents\NinjaTrader 8\FootprintMapHistory`. A custom/OneDrive user-data location can differ.

Each capture filename is a SHA-256 key plus `.fph`. The key includes the full instrument/contract name, tick size, Trading Hours name/time zone, frame duration and persistence duration. Repeated saves for the same key **replace** that file atomically; they do not append an unlimited new file every 30 seconds. A temporary replacement and a small lock file can coexist during writing. Different contracts or capture-defining settings create different archive files. There is currently **no automatic total-folder cleanup**, so old contracts/configurations can accumulate until the owner removes their archives.

| Budget at the reviewed revision | Default / hard boundary | Practical consequence |
| --- | --- | --- |
| Heat frame duration | 250 ms | Four averaged depth samples per second, not a full raw message log |
| Time retention | 30 minutes | Old heat/individual trades are trimmed; session totals/profile are maintained separately |
| Retained heat frames | 7,200 | At 250 ms this is 30 minutes of continuous frames; changing frame duration changes the count's time coverage |
| Retained individual executions | 200,000 | Busy contracts can reach the count cap before the time cap |
| Captured depth | 200 rows per side by default; adapter setting up to 2,000 | Larger settings only retain rows actually supplied by the connection |
| One archive | 128 MiB maximum; at most four million heat cells; additional frame/trade/profile count validation | An oversized snapshot fails with visible save status; it is not silently converted into an unlimited recording |

Encoded heat cells use 25 bytes each, plus frame metadata; retained executions use 45 bytes each and profile levels 32 bytes each. For illustration, 7,200 frames with **60 observed rows total** contribute about **10.8 MB** of heat cells; 200,000 executions add about **9 MB**, plus metadata/profile. These are decimal-byte illustrations of encoded contents, not a measurement of the user's file. More observed price rows, price changes within a frame and additional capture identities increase storage. Memory objects use additional overhead; the on-disk size is not a RAM estimate.

Reopening with a matching capture key restores validated retained history, but **clears the live book until fresh depth arrives**. It never paints through the period while the indicator was closed. Same Trading Hours session can retain compatible session totals; a new session resets session profile/CVD while retained heat/trades remain subject to budgets. Rewinding playback restarts the in-memory recording to avoid mixing abandoned future events into the past, and older replay does not overwrite a valid newer saved capture.

Disabling saving stops future archive writes and restore for that instance; it does not delete existing files. To reclaim old recording space, close the indicator/NinjaTrader first, then remove only unwanted `.fph` captures in the dedicated folder. Removing them removes that locally saved history; it does not affect broker or exchange data. The file-size and total-folder distinctions should stay visible in the installation guide.

## Trial data, Rithmic and how much Level II can appear

The supplied recordings show that the trial supplies some depth, but they do **not** establish a contractual trial-specific maximum. Several independent limits can explain the visible result:

1. The account must be entitled to the selected exchange/product's depth, not just last price or top of book.
2. The provider/connection adapter determines which depth levels and update semantics reach NinjaTrader.
3. FootprintMap applies its **Maximum depth levels per side** capture budget and optional COB row limits.
4. The viewport, price-bin aggregation, minimum size and contrast filters determine which supplied levels are visible and legible.

NinjaTrader's documented Market Depth Map setting explicitly states that the available number of levels is limited by what the data provider supplies. Its sample default of ten levels is an indicator setting, **not evidence of a universal NinjaTrader limit or this user's trial entitlement**. The cached documentation mirror is secondary evidence of that text; the current official source is [Order Flow Market Depth Map](https://ninjatrader.com/support/helpguides/nt8/order_flow_market_depth_map.htm).

A practical diagnosis compares FootprintMap's captured bid/ask row counts with **New → Level II** for the identical contract and connection. If both show the same finite depth, increasing the indicator budget cannot reveal rows NinjaTrader does not receive. If native Level II has more rows, first check FootprintMap's capture/display limits and then its adapter behavior. A wide zoom or hidden labels is a display issue even when the rows are captured correctly.

Rithmic may expose different depth according to account/exchange entitlement and the chosen connection, but **“paid Rithmic means unlimited depth and MBO in this indicator” is not a supported promise**. Standard NinjaScript `OnMarketDepth` remains price-level data for this adapter. A live order-ID feed bridge and validated sequence/event semantics are required for the standalone MBO engine. CME entitlements do not automatically include Eurex/DAX.

Fresh official Bookmap sources retrieved October 30 confirm narrower native requirements:

- [Stops & Icebergs On-Chart](https://bookmap.com/knowledgebase/docs/Addons-Stops-And-Icebergs-On-Chart-Indicator) says it works with **CME MBO, Rithmic or BookmapData, real-time non-aggregated mode**. The same guide notes CME does not provide MBO for implied orders and does not permit native icebergs in some instruments.
- [Liquidity Tracker](https://bookmap.com/knowledgebase/docs/Addons-LT-Pro) supports MBP calculations broadly, but its **individual-order-size filtering** is documented for CME through Rithmic/BookmapData. It explicitly excludes non-CME such as Eurex and CME through CQG for that feature.

Those are Bookmap's supported product paths, not proof that NinjaTrader forwards the same fields through its standard indicator API. Direct Rithmic documentation was not fetched in this review because that domain was not in the selected environment's network policy; no Rithmic package entitlement or exact level count is invented here.

## Further features feasible from current captured inputs

These are implementation candidates, **not completed features in this document**:

- A captured-time AVWAP anchor with a named reset and explicit partial-history coverage; additional anchors and historical paths need bounded storage/UI.
- High/low/mid and percentage price levels over an explicitly defined retained interval, plus user-named manual levels.
- Individual large-print local alerts with deduplication across reconnect/restore and no false claim of reconstructed aggressor tape.
- An outside-viewport depth summary using supplied rows only and clearly labeled current/history scope.
- A separately defined price-change or volume-pressure study with explicit window/decay math; calling it Market Pulse equivalence would need further evidence.
- A more detailed MBP refill/underflow study only if event timing and retained input resolution support it; sampled heat cannot recover a lost millisecond sequence.

Account trading tools, cross-instrument products, liquidation data and native MBO identity cannot be completed from the current single-instrument MBP snapshot by adding display settings alone.

## Evidence and reproduction boundary

The source audit inspected `FootprintMap.cs`, `HistoryStore.cs`, `OrderFlowStudies.cs` and the existing feature catalogue. Current official Bookmap pages for Sweeps, Liquidity Tracker and Stops & Icebergs returned HTTP 200 on October 30; fetched HTML/text and access results are retained outside the source package at `/workspace/research-assets/oct30-catalogue/`. The main catalogue supplies the remaining native product links and its original access dates. The five new recordings have a separate [visual review](bookmap-oct30-user-video-review.md); this inventory does not claim audio review or native Windows validation.
