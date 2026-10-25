# Liquidity filtering, executed-volume bubbles, and order-flow semantics

Research checked **2026-10-23**. Official Bookmap product documentation, the relevant TraderMap Pro blog article, and actual official screenshots were retrieved and inspected. This document distinguishes documented product behavior, observations from screenshots, and independent implementation proposals. The initial network restriction has been resolved. Proprietary formulas remain unknown where the vendor does not publish them.

## 1. Evidence and limits

The following sources were actually read:

1. [Bookmap official API demo repository README](https://github.com/BookmapAPI/DemoStrategies/blob/6368c4b6131dc4822b2ab68019a3fee5e35cf5e5/README.md): data flow, historical aggregation, coordinate systems, and rendering/performance architecture.
2. [Official last-trade icon example](https://github.com/BookmapAPI/DemoStrategies/blob/6368c4b6131dc4822b2ab68019a3fee5e35cf5e5/Strategies/src/main/java/velox/api/layer1/simplified/demo/LastTradeDemoIconsNoHistory.java): side-colored trade markers and an explicit warning that an icon for every trade becomes expensive when zooming out.
3. [Official market-by-order visualizer](https://github.com/BookmapAPI/DemoStrategies/blob/6368c4b6131dc4822b2ab68019a3fee5e35cf5e5/Strategies/src/main/java/velox/api/layer1/simplified/demo/MboVisualizerNoHistory.java): separate per-order send, replace, and cancel operations, with aggregation into price levels.
4. [Official data-editor example](https://github.com/BookmapAPI/DemoStrategies/blob/6368c4b6131dc4822b2ab68019a3fee5e35cf5e5/Strategies/src/main/java/velox/api/layer1/simpledemo/dataeditor/DataEditorBasicExample.java): independent depth and trade updates, replacement quantities, price/size transforms, and restoration of the original book on unloading.
5. [Official Bookmap Simplified API reference](https://github.com/BookmapAPI/ai-skills/blob/main/references/simplified-api.md): depth, trade, BBO, MBO, and time listener distinctions; raw Bookmap prices are tick/level units; history must be requested rather than assumed.
6. [Official Core API reference](https://github.com/BookmapAPI/ai-skills/blob/main/references/core-api.md): data versus screen coordinates, historical recalculation, settings, and data editor placement.

Additional official primary sources read after website access was enabled:

7. [TraderMap Pro](https://bookmap.com/knowledgebase/docs/Addons-Tradermap-Pro), updated June 22, 2026: actual filters, transformations, history behavior, and limitations.
8. [TraderMap Lite](https://bookmap.com/knowledgebase/docs/Addons-Tradermap-Lite), updated June 22, 2026: the specific market-following stacker pattern, sensitivity settings, and supported instrument/connection combinations.
9. [Traded Volume Visualization](https://bookmap.com/knowledgebase/docs/KB-SettingUpAndOperating-HeatmapTradedVolumeVisualization), updated August 26, 2026: pixel aggregation, dot positioning, drawing styles, clustering, and volume bars. Its settings screenshot was also inspected, which resolves an imprecise sentence about delta in the prose.
10. [Heatmap settings](https://bookmap.com/knowledgebase/docs/KB-SettingUpAndOperating-HeatmapSettings), updated June 22, 2026: palette, percentiles/absolute cutoffs, contrast, smoothing, accumulated depth, and stale extended-depth handling.
11. [4 Ways to Trade with Tradermap Pro](https://bookmap.com/blog/4-ways-to-trade-with-tradermap-pro/), guest post by nybammer, January 12, 2023 (WordPress metadata modified April 27, 2023). Retrieved through the official [WordPress search endpoint](https://bookmap.com/wp-json/wp/v2/posts?search=tradermap&per_page=100), including its ACF article body. This is an actual relevant article, not a guessed URL.
12. [Absorption Indicator](https://bookmap.com/knowledgebase/docs/KB-Indicators-Absorption) and [Sweeps Indicator](https://bookmap.com/knowledgebase/docs/KB-Indicators-Sweeps).
13. [Large Trades Alert](https://bookmap.com/knowledgebase/docs/Addons-Large-Trades-Alert) and [Large Lot Tracker](https://bookmap.com/knowledgebase/docs/KB-Indicators-LLT).
14. [Stops & Icebergs](https://bookmap.com/knowledgebase/docs/Addons-Stops-And-Icebergs-On-Chart-Indicator) and [Iceberg Orders Tracker technical explanation](https://bookmap.com/knowledgebase/docs/KB-Bookmap-Wiki-Iceberg-Orders-Tracker).

The public sources specify meaningful behavior but do not publish the Bot Remover classifier, Smart clustering algorithm, exact average-volume scaling formula, or native renderer source. Marketing claims about removing bots do not establish the intent of every filtered order.

## 2. What the data represents

A limit-order book and a trade tape are different streams:

- **Resting liquidity:** displayed quantity currently offered at a price, on the bid or ask side. Its history produces horizontal time/price bands.
- **Executed volume:** quantity that actually traded at a price and time. Its history produces volume dots/bubbles, a traded-volume profile, and delta calculations.
- **Best bid/offer:** the closest displayed prices on each side. It outlines the spread; it is neither a trade nor the complete book.
- **MBO (market by order):** order identifiers plus their additions, replacements, and cancellations. The official `MboVisualizerNoHistory` demonstrates these explicitly.
- **MBP (market by price):** total displayed quantity at each price. The ordinary depth callback is of this type. It does not reveal the constituent order identities.

The official Bookmap `onDepth` examples update the quantity at a price. They do not add that quantity repeatedly. Treating each quote refresh as incremental volume inflates the book disastrously. A zero quantity removes that level; a trade callback separately records executed quantity.

The Bookmap API's historical `DepthAggregationEvent`, per its README, contains the last change at each changed price over an interval, not every intermediate change and not a full standalone snapshot. The README explains that an initial aggregation before the requested range can be used as a snapshot, followed by interval updates. This is evidence about the API aggregation contract, **not** proof of the heatmap's exact pixel statistic.

For a NinjaTrader adapter, do not copy Bookmap's numerical price units accidentally: normalize NinjaTrader prices using the instrument tick size into integer price keys, and keep conversion at the boundary. Bookmap's callbacks already use tick/level units.

## 2a. User-selected feeds and instruments

The first deployment is intended for the NinjaTrader two-week trial data connection, potentially followed by Rithmic, on NQ, ES, GC, YM, and RTY. The portable baseline must therefore use the actual MBP depth and trade events delivered by NinjaTrader. Do not assume that a trial includes every exchange entitlement, full depth, historical depth, exchange-native aggressor flags, or MBO access. Verify these capabilities on the connected account and each selected instrument. A Rithmic connection does not by itself establish that NinjaScript exposes Rithmic MBO identifiers.

Use each contract's `TickSize` and feed quantity units at runtime. Avoid hardcoding prices or substituting a micro contract for the requested standard contract. Liquidity distributions differ substantially across NQ, ES, GC, YM, and RTY, so the default filter should combine an adaptive relative reference with optional per-instrument absolute overrides. Proposed numeric presets must be calibrated on recorded data and identified as starting points, not Bookmap defaults or proven optimal thresholds. A raw-depth mode, available-level counts, and an explicit waiting/partial-depth state will make missing trial entitlements or limited depth observable.

## 3. Why liquidity can appear to hug price

Several different effects can create a moving band of color near price:

1. Genuine market makers continuously replace quotes close to best bid/offer.
2. Many small, short-lived quotes dominate a low-contrast visualization.
3. The feed provides only the nearest N levels. The edge of the observed data follows the market, even when deeper liquidity exists outside the feed.
4. Per-frame normalization continually promotes ordinary near-touch quantities to the brightest colors.
5. A rendering bug keys history to **ladder position** rather than absolute price, making a level appear to travel when the ladder shifts.
6. Displaying repeated snapshots as new additive quantities creates an artificial intensity ridge.
7. An incomplete recovery after a reconnect leaves stale liquidity behind or fills a coverage gap with invented data.

These explanations must be separated before applying a filter. A better filter cannot recover unobserved deeper levels. Suppressing the nearest few ticks is a user-selected view preference; it is not a detector of irrelevant orders. Near-touch liquidity is often economically important.

A readable replica needs a raw mode for comparison and a clearly labeled filtered mode. Both should be derived from the same reconstructed raw book. Filtering must not corrupt the raw book or silently remove quantities from a current-order-book panel that purports to show actual depth. Bookmap Pro intentionally exposes a synthetic filtered book; a compatible UI must make that choice explicit.

## 3a. What TraderMap Pro actually does

TraderMap Pro makes a **synthetic copy** of a subscribed instrument with selected filters applied. A name gets `@TM.Pro`; multiple copies can use different filters, but copies cannot be made from other Pro copies. The original must remain subscribed. This is deeper than simply changing a color cutoff: filtered orders are removed from the synthetic book, and downstream columns/add-ons see the transformed data.

### The exact “hugging price” problem

The Lite FAQ describes a stacker algorithm that appears around 8 a.m. EDT, shadows the market on three high-liquidity levels about 3–4 points from best bid/offer, and seeks queue position to get filled when conditions become favorable. The documentation explicitly says the unfiltered Bookmap heatmap is functioning correctly and displays the true market. This is a source-specific example, not a universal description of all hugging bands or a timeless market rule.

The Pro **Bot Remover** is described as excluding market-maker algorithms which tend to remain at a set distance from price and usually do not execute. Its sensitivity control has low, standard, and high descriptions: higher sensitivity removes more suspected bot activity and may also remove ordinary orders. The settings screenshot shows a continuous min/max sensitivity slider with a reset button. The docs mention preselected sensitivity levels specifically for ES, NQ, MES, and MNQ. They do not disclose the numerical detection algorithm.

TraderMap Lite is the automatic simplified version for ES, NQ, MES, and MNQ through Rithmic, BookmapData, and IQFeed. **GC, YM, and RTY are not listed as Lite-supported instruments.** Pro is the documented alternative for more flexible filtering. These Bookmap connection claims do not establish equivalent data exposure in NinjaTrader.

### Verified Pro filters and transformations

| Control | Documented behavior | Data/implementation implication |
|---|---|---|
| Bot Remover | Suppress market-following algorithmic order patterns, configurable sensitivity | Proprietary classifier; cannot claim exact reproduction |
| Order size | Keep individual orders within a configured size range; example 5–10 excludes 1–4 and 11+ | Explicitly requires order-by-order/MBO data; filtering a 100-lot MBP level is not the same |
| Order size % | Retain selected percentage of largest orders, calculating a size threshold | Uses individual-order distribution; option excludes outlying low-price orders from threshold calculation |
| Order change | Show orders changed/unchanged in price, size, or either | Six combinations; optional exclusion of trade-induced changes; requires identity/history |
| Order ID pattern | Include/exclude IDs using contains, starts-with, ends-with, or regex | Unavailable without order IDs |
| Broker ID | Select one or more brokers and their orders and/or trades | Documented only for Cedro |
| Order duration | An order appears only after resting for configured time; example 60 s | Causal delayed appearance, true order identity matters |
| Number of orders per price | Replace total quantity with count of individual orders | Heatmap, COB, and add-ons inherit changed units |
| Distribution by order size | Vertical axis becomes order size; intensity is count of orders | Not a price heatmap; optional mirrored negative bid side, positive ask side |
| Average order size | Mean size of constituent orders at each price | Cannot derive from total size without count/identity |
| Canceled book | Show canceled quantities for chosen lifetime, summed by price, expiring individually | Requires identified canceled events; not all MBP reductions qualify |
| Price inversion | Either negate price or use reciprocal price | Orders/trades reverse sides; axis semantics change |

Canceled Book's documented example is precise: a cancellation of 10 at price 100 persists for 5 seconds; a cancellation of 5 one second later makes 15; four seconds later only 5 remains; one further second later zero remains. This is an expiring **event-flow** transformation, not standing liquidity.

Order-size filtering occurs before number-of-orders transformation. The documented overall precedence is canceled-book/order-duration processing, then data filters, then transformations. Only one of number-of-orders/distribution transformations can be selected together; the page also disallows order-size filter plus distribution-by-size. Do not blindly assume all combinations are supported.

### Important effects and limitations

- Pro recalculates BBO **after** filtering. If the original best quote is removed, its displayed BBO moves away; trades may appear inside that synthetic spread. It is no longer the actual tradable BBO. A NinjaTrader implementation should default to the real BBO and label any filtered-book boundary separately.
- Trades are transferred from the source instrument, including available history. Depth history is limited to MBO accumulated live by the original. The official example has 90 minutes of trades but only 30 minutes of depth after downloading one hour of trade history and collecting 30 minutes live.
- Pro's page says replay mode is unsupported. Its v2.57 changelog says history aggregation was reduced to 100 ms. Neither statement authorizes inventing tick-precise historical MBO.
- Size filter and percentage filter are order-level filters, while heatmap contrast cutoffs are price-level color controls. These must not share misleading UI labels.
- A source-page disclaimer lists known add-on bugs, reinforcing that visual similarity cannot establish equivalent correctness.

### What the actual blog contributes

The 2023 guest article emphasizes filtering by **individual order size**, with index-specific thresholds. Its author's suggested starting rule is 100 contracts for ES and 50 for NQ/RTY. It discusses swing targets, intraday targets, news-driven moves, and movement/cancellation of large orders. These numbers are dated discretionary examples, not Bookmap defaults, tested universal settings, or MBP-level thresholds. The article itself cautions that large displayed levels do not determine breakout direction and may be canceled before price reaches them. Modern Bot Remover/duration features should be sourced from current docs, not retroactively attributed to that article.

### Screenshot analysis

The official [Bot Remover comparison](https://bookmap.com/knowledgebase/assets/images/botRemoverExample-0a09ac28245756dbd95695b562b6ba7a.png) was downloaded and visually inspected. The left chart has two conspicuous bright stair-step bands maintaining distance from the price path. The right chart removes those bands while leaving ordinary horizontal liquidity structure. Price/BBO and trade bubbles retain the same path. Fixed-price horizontal bands become much brighter in the filtered chart, consistent with contrast being recalculated after removing dominant quantities; a replica that only paints filtered cells black will not necessarily match that appearance. This is an observed image effect, not proof of the hidden classifier.

Local inspected assets are in `/workspace/research-assets/liquidity/`: `bot-remover-comparison.png`, `bot-remover-settings.png`, `order-size-percent-settings.png`, `order-change-settings.png`, `volume-dot-settings.webp`, and `volume-bubbles.jpg`. The general chart illustration shows translucent 3D red/green spheres, thin red/green stair-step bid/offer lines, horizontal liquidity cells, narrow right-hand COB/profile columns, and a bottom executed-volume strip.

## 4. An honest, causal liquidity filter for aggregated Level II

The following is a proposed independent **MBP-compatible approximation**, not the published MBO Order Duration filter and not a reconstruction of the proprietary Bot Remover. It is useful for a NinjaTrader trial connection whose exposed data lacks order identities. It should be labeled “level persistence” or “liquidity noise filter,” never “identical TraderMap Pro.”

### 4.1 Quantity threshold and robust normalization

Use an absolute minimum size plus an optional relative threshold. An absolute threshold removes small displayed quantities; it must be instrument-specific and expressed in feed quantity units. A relative threshold can be based on a rolling distribution of positive visible quantities, such as a percentile. Avoid one universal contract threshold across ES, NQ, equities, and fractional-quantity instruments.

Normalize heatmap colors against a slowly adapting robust upper reference (for example, a high percentile) or a manual fixed ceiling. Clamp intensities above that ceiling. This keeps a single huge quote from making the rest of the chart invisible and reduces full-screen color pumping. Store raw quantities so a changed color scale can be applied without pretending a new market event occurred.

A lower display cutoff and an upper color ceiling serve different purposes. The cutoff hides low values; the ceiling controls saturation. Expose them separately.

### 4.2 Fixed-price persistence

Persistence belongs to `(side, absoluteTickPrice)`, never to level index. A simple causal filter shows a level only after it has continuously met a quantity threshold for a configurable duration. It delays newly appearing liquidity; that is an unavoidable tradeoff, not a bug.

A more informative aggregate persistence measure is a rolling lower envelope:

`persistentQuantity(t, τ) = min q(s), for s in [t − τ, t]`

This can be interpreted as the amount of **aggregate displayed quantity** continuously observed at that price over the window. It cannot be interpreted as the age of the same individual orders. If the level was unknown or absent anywhere in the window, continuity is broken. For example, quantities 10 → 50 → 40 over the window establish at most 10 units of persistent aggregate quantity, not 40 units aged for the full window.

The lower-envelope approach avoids aging a newly added large quote merely because a one-lot order was already present at that price. It can be implemented with time-window samples or a monotonic queue, with defined retention bounds.

A cheaper alternative is a threshold age timer reset whenever quantity falls below the configured threshold. If used, document its meaning as continuous threshold persistence and avoid an "order age" label.

### 4.3 Churn and quote repricing

At a price, an optional rolling activity measure can estimate additions and reductions:

`added = Σ max(0, q_new − q_old)`

`reduced = Σ max(0, q_old − q_new)`

`churn = (added + reduced) / max(epsilon, timeWeightedMeanQuantity)`

Use a finite time window and an explicit duration normalization if comparing rates. A high ratio indicates rapidly changing displayed depth. It does not establish intent, same-order replacement, or spoofing. Reductions include executions, cancellations, feed corrections, and coverage changes. Do not label every reduction as cancellation.

If a churn filter is implemented, provide it as an optional attenuation alongside persistence, with raw-versus-filtered comparison. A hard filter can hide genuine responsive liquidity. Apparent repricing correlation with best bid/offer can be an exploratory statistic, but MBP data cannot identify the same order migrating across prices.

### 4.4 Avoid future leakage

A live filter must use only observations available at that time. When a quote survives for one second, it is valid to show its filtered signal from the one-second threshold onward. Retroactively coloring its entire prior lifetime makes historical performance look more certain than live operation.

A retrospective analysis view can be offered separately and labeled as such. It must never be mistaken for what a trader could have seen live. The same rule applies to absorption, sweep, and replenishment alerts.

### 4.5 Coverage and reset rules

- Before initial depth arrives: **unknown**, not an empty market.
- Outside a limited feed's observable depth: **unknown**, not zero liquidity. Bookmap optionally retains the last observed quantity as extended depth; that is explicitly documented stale estimation and needs a distinct display/reset policy.
- At disconnect or detected reset: stop extending last-known depth as current fact; mark a gap and clear continuity ages.
- After reconnect: rebuild from the new snapshot/update stream, without bridging the outage.
- At a session or instrument change: reset the relevant state and maintain explicit boundaries.
- At a replay time rewind: clear future state before ingesting the earlier tape.

At the feed boundary, distinction between a true cancellation and removal caused by truncating a provider's finite-depth window may not be available. Report that limitation instead of inventing canceled-volume statistics.

## 5. Time/price heatmap aggregation

Liquidity is a stock (quantity available at a moment), whereas traded volume is a flow (quantity executed during an interval). Their aggregation must differ.

For depth in a horizontal pixel interval, a principled statistic is the duration-weighted mean of standing quantity:

`meanQuantity = Σ(quantity × duration) / observedDuration`

Alternatively, maximum quantity emphasizes short-lived extremes, while last quantity emphasizes the end state. These choices answer different questions and should not be mixed silently. The API's last-update historical aggregation does not establish which statistic Bookmap uses internally for a heatmap pixel.

Integrate the **previous** quantity from its last timestamp up to the next event, then apply the new quantity. A new depth update must not be painted backward before its timestamp. Intervals spanning unknown coverage need coverage metadata so missing data is not treated as zero or stretched into a false continuous band.

When several price ticks fit in one pixel, a renderer must choose a consistent vertical aggregation. Summing quantities across price levels is suitable for a price-bin total, but changes the units relative to a single-tick cell. Taking a maximum preserves individual-wall visibility. State the selection in the settings/help text.

## 6. Trade-side classification and bubbles

### Verified built-in Bookmap behavior

The current [Traded Volume Visualization guide](https://bookmap.com/knowledgebase/docs/KB-SettingUpAndOperating-HeatmapTradedVolumeVisualization) states that each base dot aggregates trades in a **single horizontal pixel's time slice**. Zooming in shortens that interval; zooming out lengthens it. Dot center Y is the volume-weighted average price for the interval; the X coordinate represents its execution time. With Smart clustering, both time and price use weighted averages over merged dots. The guide does not specify the exact base-dot X-time reducer, so choosing a volume-weighted time in our implementation is a documented design choice rather than a proven identical detail.

The built-in size algorithm scales volume at a pixel-time against the instrument's average trade volume and the user's size control. The exact averaging window and radius formula are not disclosed. A square-root area law is an independent sensible approximation; it cannot honestly be called Bookmap's published formula.

Verified settings include:

| Setting | Actual documented meaning |
|---|---|
| 2D Dots / 3D Bubbles | Flat circles or shaded spheres |
| Total volume / Volume delta (buy minus sell) | Sum of aggressive buy/sell volume or their difference |
| Size | Dot-size scaling; optional toolbar slider |
| Transparency | Opacity control |
| Minimal displayed volume / minimum accountable dot volume | Threshold on aggregated dot volume |
| Minimal trade size | Threshold on each individual execution before aggregation |
| Gradient | Blend buy/sell colors in proportion to aggressor volumes |
| Solid | Split the two solid colors to show side proportions |
| Pie | Pie segments show side proportions |
| Clustering | Smart, by time, by volume, by price, by price and aggressor; unclustered option |
| Apply to Bars / Inherit from Bars | Synchronize dot and volume-bar clustering |
| Apply vertical in-pixel aggregation | Merge vertically aligned data points sharing the same horizontal coordinate |

**Delta clarification:** the page prose uses the imprecise phrase “difference between aggressive and passive trade volume.” The inspected settings screenshot explicitly says **“Volume delta (buy minus sell)”**, and the Absorption FAQ explicitly defines it as buyer-aggressor minus seller-aggressor volume. That is the correct mathematical interpretation.

Minimal displayed volume is intentionally zoom-dependent. One hundred one-lot executions may qualify as a 100-lot dot when compressed into one pixel, then disappear when zoomed in across multiple pixels. Minimal trade size of 100 instead excludes all those one-lot prints. These are independent controls and need separate tests.

Clustering details:

- **Smart:** proprietary combination of nearby or overlapping dots; slider controls aggregation strength; merged position uses weighted price and time.
- **Time:** one dot per configured interval.
- **Volume:** create a bubble when configured volume is reached. The documentation has a non-obvious example: threshold 100 and prints 20, 15, 35, 270 yield a bubble of 300 and carry 40. This does not disclose enough about print splitting/carry rules to guarantee an identical implementation. The volume-bar section separately gives 50 + 100 → 150 at threshold 100. Do not silently assume one uniform “exact threshold” rule or discard excess volume; define and test the chosen replica rule.
- **Price:** aggregate consecutive trades at one price until another price trades.
- **Price and aggressor:** same-price, same-aggressor runs; the prose specifically says the aggressor change ends a cluster. A reasonable reading also requires a price change to end it, but this should be verified empirically for exact parity.
- **Vertical in-pixel aggregation:** a separate compactness control, not simply a bigger trade-size filter.

Volume bars are executed volume per horizontal pixel width, 1 pixel by default and configurable to 15. Drawing styles are Gradient/Solid. They have Smart, time, volume, and described price aggregation behavior; the bar area height is draggable and displays the visible maximum at the right. This bottom strip uses the same trades as dots, so changing display clustering must never alter session volume/CVD totals.

### Data-side classification for a NinjaTrader implementation

The public official icon demo colors trade icons using the feed's `tradeInfo.isBidAggressor` field and explicitly cautions against one icon per trade when zoomed far out. This establishes that aggressor metadata exists in Bookmap's feed abstraction, and that zoom-dependent aggregation is important. The file draws example arrows, **not** the built-in commercial volume bubbles; it is not evidence for the commercial bubble-sizing formula.

For a NinjaTrader indicator, an independent defensible classification pipeline is:

1. Prefer trade-time bid/ask values provided with the last-trade event when supported by the API/feed.
2. Trade at or above a valid ask: buy aggressor. Trade at or below a valid bid: sell aggressor.
3. For trades strictly inside the spread, use an explicitly chosen fallback (tick rule or midpoint), or mark unknown.
4. For missing, stale, locked, or crossed quotes, retain an unknown state unless a documented fallback applies.
5. Do not turn all unknown trades into buys or inherit the last side indefinitely without disclosure.

A trade always has both a buyer and seller. Green/red denotes the inferred **aggressing** side, not "there were only buyers" or "there were only sellers".

### 6.1 Bubble content modes

The following describes the quantities an independent renderer can expose. Use the verified Bookmap settings above as the visual target; side-separated and unknown-segment choices below are additional explicit design options:

| Mode | Quantity represented | Color/content |
|---|---|---|
| Side-separated trades | Buy and sell volume in separate clusters | Configurable buy/sell colors |
| Total-volume cluster | `buy + sell + unknown` | Dominant side, split segments, or neutral for unknown |
| Delta cluster | `abs(buy − sell)` | Sign determines side color; zero delta does not mean no trading |
| Split/pie cluster | Total executed volume | Segment proportions show buy/sell/unknown composition |
| Large-trade view | Trades or clusters above a threshold | Threshold stage must be documented |

Do not accidentally implement a delta-only bubble while calling it total volume. A cluster with 1,000 buys and 1,000 sells has 2,000 total volume and zero delta.

### 6.2 Zoom and aggregation

Store market events independently of rendering. At render time, cluster trades into time/price bins based on the current viewport and a configurable pixel footprint. Rebuild clusters when horizontal or vertical scale changes. Zooming out should merge neighboring executions; zooming in should expose the retained detail rather than expand the same permanently merged blob.

Use a stable time origin and tick-aligned price bins. Define whether minimum volume filters apply to each original trade or to the aggregated cluster: ten one-lot trades form a ten-lot cluster, but none is a ten-lot individual execution. These filters must not share an ambiguous label.

A suitable independent visual size law is `radius ∝ sqrt(volume)` so circle **area** represents executed quantity. Apply configurable minimum/maximum radii and a global scale. Clamping sacrifices proportionality beyond the cap and should be documented. Nonlinear/log scaling can improve readability over a wide range but changes interpretation; label it explicitly.

A stable reference volume avoids all circles shrinking when a single huge print arrives. Manual scaling or a slowly adapting reference is more interpretable than recomputing the largest bubble from the current viewport every frame. Whether Bookmap itself uses these exact choices remains unverified.

Mixed-price cluster placement should be explicit: a tick-bin center or volume-weighted price. A volume-weighted time is reasonable for a cluster centroid, but the full time span should be available in a tooltip/export. Do not let the centroid imply an individual execution at a price that never traded.

### 6.3 Rendering details

Suggested visual controls include opacity, outline, size scale, minimum volume, maximum radius, separate buy/sell colors, unknown-side color, grouping time/price width, and optional volume text. Keep outlines and text readable against intense heatmap cells. Render bubbles after the heatmap and reference lines; clip to the chart area so large circles do not overwrite axes or panels.

A bubble's opacity may indicate overlap readability, but it should not silently encode a second unlabelled market statistic. A "3D" highlight is decorative shading; it must not alter the quantitative radius.

## 7. Other order-flow features and what the data can support

### Verified Bookmap study behavior

**Absorption:** Bookmap marks a price when same-side executed volume exceeds configured size within configured time. Buys and sells are calculated separately at each price. Automatic minimum size uses standard deviation over a configurable recent-minute interval and an SD multiplier; the precise binning/estimator is not published. Unlike some trading definitions, the documented trigger does **not** require a subsequent bounce or an explicit price-progress rejection test. Labels should identify the passive side (sell aggressors absorbed by passive buyers, and conversely). Display options include marks/dots, round/rectangle shape, volume labels, icon offset with connecting dotted line, colors, and horizontal/vertical pixel aggregation of same-side marks. Backfilled aggregated data can produce different detections than live granular events.

**Sweeps:** same-side executed volume above a threshold in configured time **across at least a configured number of distinct price levels**. Absorption applies size per price; Sweeps sums across levels. It shares automatic SD thresholding and much of the icon/alert configuration. Neither study alone proves an iceberg or a stop order.

**Large Trades Alert:** by default reconstructs an aggressor tape by aggressor ID. A 10-lot aggressive order executed against passive 5/3/2 orders becomes one 10-lot alert candidate. “Individual trade events” instead tests 5, 3, and 2 separately. Sound and popup are configurable. A NinjaTrader adapter without aggressor IDs cannot honestly promise this tape reconstruction; time/side proximity grouping must be called an approximation.

**Large Lot Tracker:** a white vertical line on a COB bar approximates the largest single pending order. The documented default examples require at least 20% of quantity at its own level and at least 10% of the largest visible order-book bar. These thresholds are configurable. The docs explicitly say its largest-order estimate is an internal algorithm, not exchange-reported order identity, and not 100% accurate. Its dependence on the COB bar panel is part of its UI behavior.

**Stops & Icebergs:** production On-Chart shows detected, traded, fully executed, and canceled iceberg events (D/T/E/C), active iceberg lines, and stop-chain marks. Cancellation labels show already traded size, **not** the remaining hidden quantity. An active iceberg line follows price changes and uses dotted connectors to event labels. Stops show a bold vertical range and total chain volume; a chain may contain several stops. The current guide distinguishes separate production On-Chart/Sub-Chart add-ons from a combined **Bookmap 7.9 alpha** add-on. It requires nonaggregated CME MBO via Rithmic or Bookmap Data; the underlying event sequence matters. This capability is not established by merely selecting Rithmic inside NinjaTrader.

The technical iceberg article explicitly states hidden remaining quantity cannot be detected, identification cannot occur before some execution, and trackers are estimates. Its old underflow algorithm (trade exceeds visible depth) is labeled expired/unreliable. It describes a lower-quality “Resistance” approximation based on very rapid passive replenishment following executions, but reliable sequencing is necessary. Never repurpose simple MBP volume subtraction as “native iceberg detection.”


| Feature | Data required | What can be stated honestly |
|---|---|---|
| Current order book | Current Level II snapshot/updates | Displayed quantity currently observed by the feed |
| Historical heatmap | Recorded Level II updates and coverage | Past displayed quantity since capture began |
| Session/visible-range volume profile | Last-trade stream | Executed volume at price over a stated interval |
| Buy/sell profile and CVD | Trades plus aggressor classification | Classified volume and delta, with unknown volume accounted for |
| Depth imbalance | Bid/ask depth in defined range | Ratio over a specified level count or tick distance |
| Pulling/stacking estimate | Ordered depth changes, preferably execution reconciliation | Added/reduced displayed quantity; cancellation attribution may remain uncertain |
| Absorption candidate | Aggressive executions plus price response | High aggressive volume with limited progress; an inference, not proof of hidden passive interest |
| Replenishment candidate | Repeated executions and replacement displayed liquidity | Replenishment pattern, not proof of one iceberg order |
| Native iceberg detection | Suitable exchange MBO semantics/identifiers | Depends on feed and venue; generic MBP cannot reliably supply the same result |
| Stop-run/sweep candidate | Aggressive execution burst across prices | Pattern; public tape does not identify all trades as stop orders |
| Large resting order tracking | MBO identifiers for true identity | MBP can track a price-level wall, not an individual order |
| Alerts | Any well-defined derived condition | Trigger on causal thresholds with cooldown/hysteresis |

Bookmap's entire add-on ecosystem is not equivalent to its base heatmap. Feature parity should be tracked explicitly, with base visuals, independent heuristics, and feed-dependent advanced features distinguished.

## 8. Test cases needed before claiming correctness

1. A replacement depth update of 100 → 120 results in 120, not 220.
2. A new best price shifts ladder indices but leaves historical levels at their absolute prices.
3. A quantity that appears for 20 ms is hidden by a 500 ms persistence setting and remains visible in raw mode.
4. A one-lot level aged for 10 s that becomes 500 lots does not acquire 500 lots of old persistence instantly.
5. A quantity disappears and reappears: continuity resets; old age is not inherited.
6. A disconnect creates a visible coverage gap and stops persistent-age accrual.
7. A limited-depth window edge is not counted as proven cancellation/spoofing.
8. Executed volume is conserved when zooming and clustering; buy + sell + unknown equals total.
9. Equal buying and selling produces zero delta but nonzero total-volume bubbles.
10. Same-timestamp, multi-price trades are deterministic and never dropped by a timestamp-only key.
11. Unknown/stale-quote trades retain unknown classification under the selected policy.
12. Bubble volume filtering distinguishes individual-print and cluster thresholds.
13. A replay rewind clears later state, profiles, and ages.
14. Tick-size conversion is stable for fractional tick sizes and does not merge adjacent prices by floating-point accident.
15. Heatmap time weighting respects exact update times and partial-bin boundaries.
16. Viewport changes do not mutate stored raw market data or session totals.
17. Memory and render work are bounded during fast-market bursts and extended sessions.
18. Tests distinguish true NinjaTrader integration validation from a portable core harness or API stubs.

## 9. Remaining limits and next comparison work

Primary research and image inspection are now completed for the filters, traded-volume controls, and related studies above. Exact Bot Remover classification, Smart clustering, contrast adaptation formula, and dot-radius algorithm remain proprietary. Their visible behavior can be approximated and tested, but not declared mathematically identical from the public evidence.

Before declaring visual parity, compare the same recorded sequence at multiple zoom levels in the actual NinjaTrader indicator and Bookmap. Validate pixel-time aggregation, re-centering, bubble size/opacity, filtered contrast, real versus synthetic BBO, and stale-depth behavior. The official screenshots provide a concrete visual target; a synthetic demo alone cannot establish identical market behavior. A live feed trial plus actual platform compilation and playback remains necessary for the integration claim.
