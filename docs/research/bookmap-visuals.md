# Bookmap: visual research, settings inventory and reproduction specification

Research performed 2026-10-22–23 against official Bookmap documentation and official GitHub API sources. The Bookmap website was initially unavailable, then became accessible; the findings below supersede the earlier access-blocked notes. **43 official chart/settings images were downloaded, and representative full charts, millisecond views, smoothing comparisons and settings panels were visually inspected.**

This is a documented independent implementation specification. Public documentation describes what Bookmap displays, but does not disclose all of its proprietary normalization and clustering algorithms. “Visually similar” and “algorithmically identical” are different claims and must be tested separately.

## 1. Version context and source register

At access time, [official release notes](https://bookmap.com/knowledgebase/docs/KB-ReleaseNotes) list **Bookmap 7.9 Production build 11, released 20 October 2026**, and **Bookmap 8.0 Alpha build 5, released 9 October 2026**. Production 7.9 improves vertical smoothing for small sizes. Alpha 8.0 adds high-DPI chart rendering and a modern UI with blue accents, rounded controls, browser-style tabs and darker chart areas; it also offers “Use classic UI style.” Most knowledge-base screenshots show the classic interface. The appropriate visual reference for this project is the documented classic chart rather than an unsupported claim to reproduce the latest alpha shell.

### Official product documentation read

| Ref | Page | Particularly relevant evidence |
|---|---|---|
| V1 | [Main Chart](https://bookmap.com/knowledgebase/docs/KB-SettingUpAndOperating-HeatmapMainChart) | Heatmap meaning, basic layers, BBO min/max-per-pixel modes, BBO hide threshold/hysteresis. |
| V2 | [Heatmap settings](https://bookmap.com/knowledgebase/docs/KB-SettingUpAndOperating-HeatmapSettings) | Native palette families, cutoffs, contrast, dimming, vertical smoothing, cumulative depth, extended depth/reset. |
| V3 | [Traded Volume Visualization](https://bookmap.com/knowledgebase/docs/KB-SettingUpAndOperating-HeatmapTradedVolumeVisualization) | Pixel-time bubbles, average-volume scaling, VWAP positioning, trade filters, clustering, bars, appearance. |
| V4 | [Colour Settings](https://bookmap.com/knowledgebase/docs/KB-SettingUpAndOperating-HeatmapColourSettings) | Independent element colors, profiles/import/restore; screenshot of available color fields. |
| V5 | [Columns](https://bookmap.com/knowledgebase/docs/KB-SettingUpAndOperating-Columns) | COB, CVP/SVP, trade/quote counters, quote delta, time and sales, column formats/resets. |
| V6 | [Supporting Features](https://bookmap.com/knowledgebase/docs/KB-SettingUpAndOperating-HeatmapSupportingFeatures) | Zoom/drag/recenter, tooltips, crosshair, events list, chart sync, recording/replay, 40 FPS default. |
| V7 | [Studies configuration](https://bookmap.com/knowledgebase/docs/KB-SettingUpAndOperating-HeatmapStudiesConfiguration) | Current studies/add-on split and visible component controls. |
| V8 | [VWAP + POC](https://bookmap.com/knowledgebase/docs/KB-SettingUpAndOperating-HeatmapVWAPPOC) | VWAP migration to AVWAP; session POC for volume, trade and quote columns. |
| V9 | [Indicator Panel](https://bookmap.com/knowledgebase/docs/KB-SettingUpAndOperating-HeatmapIndicatorPanel) | Subcharts, widgets and configurable visible indications. |
| V10 | [Information Bar](https://bookmap.com/knowledgebase/docs/KB-SettingUpAndOperating-HeatmapInformationBar) | Cursor data, recording controls and screenshot. |
| V11 | [Candlestick View](https://bookmap.com/knowledgebase/docs/KB-SettingUpAndOperating-HeatmapCandlestickView) | Optional conventional bars/candles over the heatmap. |
| V12 | [Historical Order Flow](https://bookmap.com/knowledgebase/docs/KB-Zoom-Out-Historical-Order-Flow) and [New Zoom-Out + Historical Candlestick Charts](https://bookmap.com/knowledgebase/docs/New-Zoom-Out-Historical-Candlestick-Charts) | Extended history is a separate data capability with provider/instrument/add-on limitations. |
| V13 | [Liquidity Marker](https://bookmap.com/knowledgebase/docs/Addons-Liquidity-Marker) | Five-second liquidity-change markers, near-BBO exclusion, overlap aggregation, limited-depth false detections. |
| V14 | [CVD](https://bookmap.com/knowledgebase/docs/Addon-CVD) | Buy/sell aggressor delta, individual-trade size filters, range/reset and display options. |
| V15 | [Anchored VWAP](https://bookmap.com/knowledgebase/docs/Addons-AVWAP) | Multiple anchors and deviation bands. |
| V16 | [Orderbook Imbalance](https://bookmap.com/knowledgebase/docs/Addons-Orderbook-Imbalance) and [Volume Imbalance](https://bookmap.com/knowledgebase/docs/Addons-Volume-Imbalance) | Formulas, different data scopes, depth weighting, add-on status. |
| V17 | [Footprint](https://bookmap.com/knowledgebase/docs/Addons-Footprint) | Bar types, number/histogram styles, smart scaling, detailed statistics. |
| V18 | [Market Profile](https://bookmap.com/knowledgebase/docs/Addons-Market-Profile) | TPO profiles, sessions, value area and initial balance. |
| V19 | [Outside Liquidity](https://bookmap.com/knowledgebase/docs/Addons-Outside-Liquidity) | Off-screen liquidity, depth limits and half-life weighting. |
| V20 | [Strength Level Indicator](https://bookmap.com/knowledgebase/docs/KB-Indicators-Strength-Level-Indicator) | Hidden-liquidity heuristics and explicit data-quality/false-positive limitations. |
| V21 | [Release Notes](https://bookmap.com/knowledgebase/docs/KB-ReleaseNotes) and [Software Versions](https://bookmap.com/knowledgebase/docs/KB-IntroductionToBookmap-Versions) | Production/alpha distinction and recent rendering changes. |

Native terminology changes over time. V7, updated September 2026, states that CVD, Theoretical Average and Volume Imbalance moved from Studies to add-ons. Separate current documentation also describes Orderbook Imbalance as an add-on. Old screenshots displaying them in Studies are not proof that the newest version has the same menu organization.

### API evidence retained from the initial research

- [Official DemoStrategies README at revision 6368c4b](https://github.com/BookmapAPI/DemoStrategies/blob/6368c4b6131dc4822b2ab68019a3fee5e35cf5e5/README.md): depth replacement semantics, historical aggregation, rendering coordinates, processing queues.
- [Official Core API reference at revision 6699ae1](https://github.com/BookmapAPI/ai-skills/blob/6699ae1dfabd98683c625c14b51e8d784b6a94e3/references/core-api.md): screen-space/data-coordinate anchors and move/zoom handling.
- [Official Python API](https://github.com/BookmapAPI/python-api): integer price/size units; aggregate depth and MBO are separate capabilities.
- [Official Layer0 API](https://github.com/BookmapAPI/Layer0ApiDemo): external live and replay feeds.
- [Official BrAPI demo](https://github.com/BookmapAPI/brapi-demo-consumer): add-on event broadcasting is separate from native chart rendering.

## 2. Screenshot analysis: what actually creates the appearance

Downloaded source images and their exact origin URLs are indexed in `/workspace/research-assets/bookmap/image-manifest.json`. Original image files are retained outside the source checkout. The following images were opened and inspected, rather than inferred only from captions.

| Reference image | Direct observation | Design implication |
|---|---|---|
| [Main chart](https://bookmap.com/knowledgebase/assets/images/SetUp_Heatmap_I-d18876e82d7cb028478f46b1abe86255.webp) | Dense horizontal liquidity bands; dark blue-gray shell; compact right-side COB/SVP; green/red bubbles and price traces; small bars beneath; lower CVD/subchart. | Recreate a layered time/price canvas with a compact side ladder; do not substitute candlesticks for the main visual. |
| [AAPL information-bar chart](https://bookmap.com/knowledgebase/assets/images/Information_bar_and_tool_tip_display-1423dda48ef74def7ff38df5732a4127.png) | About ten minutes of data. One-tick horizontal rows, dashed vertical time grid, thin horizontal grid, pale blue/white/yellow/orange/red bands, shaded translucent bubbles, red/green staircase BBO lines, right price ladder and COB. | Historical resting-size bands are the dominant structure. Overlays must remain subordinate and use the same vertical price mapping. |
| [Volume visualization at millisecond scale](https://bookmap.com/knowledgebase/assets/images/Volume_Bubbles-ec145d0c56c7945ee2e3032649b85d2c.jpg) | The retained local image shows labels from approximately 14:58:49.065 through .071, several distinct spherical bubbles, essentially flat BBO and large stationary horizontal levels. The figure explicitly annotates a 1 millisecond interval. | A trade view can be much finer than chart bars. Zoom behavior and event timestamps cannot be derived from ordinary OHLC bars. See manifest for the verified source URL if this generated asset name changes. |
| `KB-SettingUpAndOperating-HeatmapSettings-2.jpg` | Vertical smoothing spreads intensity across neighboring prices, creating broader blurred regions that can follow the price path. | Smoothing is visual averaging, not order-quality filtering. |
| `KB-SettingUpAndOperating-HeatmapSettings-3.jpg` | Same general AAPL period without smoothing shows distinct, thin, persistent horizontal levels and more granular short updates. | Offer a no-smoothing mode for precise level interpretation and debugging. |
| `KB-SettingUpAndOperating-HeatmapSettings-4.jpg` | Cumulative-depth mode grows brighter away from the BBO and produces broad regions rather than isolated individual-level bands. | Cumulative depth is a different semantic view; clearly label it and keep individual-level mode as the intuitive default. |
| `KB-SettingUpAndOperating-HeatmapSettings-0.png` | Heatmap panel visibly contains colormap, global scheme, legacy colormap, lower/upper percent cutoffs, exact-size fields, vertical Auto/Manual/None, and Aggregate Market Depth. | These are confirmed controls, not speculative settings. |
| `KB-SettingUpAndOperating-HeatmapTradedVolumeVisualization-1.webp` | Dots panel visibly shows 2D/3D, Total/Delta, Size, Transparency, Smart clustering, vertical in-pixel aggregation, Minimal displayed volume and Minimal trade size. | Preserve independent meanings of these controls. |
| `KB-SettingUpAndOperating-HeatmapColourSettings-0.jpg` | Separate colors exist for Buyers/Sellers, trade circles, BBO-related elements, Last Price, price/text axes, orders, candles, drawings and heatmap. | Green/red trade colors need not equal the colors of every other buy/sell element. |
| `KB-SettingUpAndOperating-Columns-0.png` / `-1.png` | CVP and COB have different control sets. Bars/numbers/both, split display, side visibility and alignment are visible; COB adds Extended/Aggregate/percentage mode. | Do not combine current resting depth and historical executions into a single ambiguous histogram. |

The color ramp visibly passes through pale blue/white before yellow in these reference screenshots. V2 describes the default family more briefly as **black → blue → yellow → orange → red**. Therefore the hottest end is red, not white. There is no single immutable RGB palette: users can select maps, alter intensity and customize elements. Public docs do not publish exact default transfer-function control points.

The images also demonstrate that “Bookmap appearance” includes substantial user choice: the right columns may be split or combined, bubbles can be flat or shaded, heatmaps can be grayscale, and studies can be hidden. Reference screenshots should record their chosen configuration before any pixel comparison.

## 3. Heatmap: meaning, controls and subtle failure modes

### 3.1 Meaning and depth history

V1 states that the heatmap represents **current and historical liquidity in the order book**. Brighter/warmer bands represent greater resting quantity; cooler/darker bands represent less. The palette encodes quantity, not buy-versus-sell direction. A large bid can be red on the heatmap even though buy-initiated trade bubbles are green.

Depth updates replace a price level's previous quantity; they do not add to it. A level changing from 200 to 500 has 500 resting, not 700. Retain its earlier 200 interval in history, then begin the 500 interval. A later cancellation ends the level's visible interval without deleting its past.

A depth-history aggregation containing the last update in a time interval is not automatically a full snapshot. Rendering a historical window needs the valid book state at its left boundary and subsequent changes. Official API interval aggregations can retain only the last update at a level within an interval; this does not establish that every native heatmap zoom mode uses last-value sampling.

### 3.2 Colormaps, dimming and global/per-instrument scope [V2]

- The default family runs black through blue, yellow, orange and red; two other color maps are available.
- Enabling legacy maps adds two grayscale choices.
- The map dropdown can be displayed on or removed from the toolbar.
- Dimming darkens the entire heatmap, all the way to black; it is controlled from the toolbar.
- “Apply color scheme globally” applies the map and dimming to all open instruments.
- Heatmap settings otherwise operate per instrument; the most recently applied settings are also used for subsequently subscribed instruments.

Exact names and RGB stops for the two alternative maps are not provided in the retrieved prose. Do not invent them.

### 3.3 Upper and lower cutoffs [V2]

Both cutoffs can be specified as an **exact contract quantity** or as a **percentile**. Values above the upper cutoff saturate at the strongest color; values below the lower cutoff saturate at the weakest color. Between them the map distinguishes relative sizes.

The official example makes the percentile semantics explicit: upper 95% colors the largest 5% with the strongest color; lower 5% colors the smallest 5% with the weakest color. Percentile quantity thresholds change as the book changes; exact quantities remain absolute. Lower cutoff can be placed on the toolbar.

This control is essential for reducing small-size visual clutter, but suppressing low displayed size is not proof that all remaining orders are “real.” A transient large order survives a size cutoff; a persistent small order does not.

### 3.4 Contrast and large-size highlight [V2]

Bookmap's internal algorithm adjusts contrast using maximum order-book size and the distribution of pending quantities. Users can schedule adjustments and enable adjustment on recentering. Advanced settings expose Contrast, Brightness and automatic-adjustment controls.

“Large Size Highlight” is specifically relevant to grayscale maps: it changes the shade range allocated to relatively larger pending orders so that these levels are more distinguishable. It is not a documented MBO order filter.

The exact automatic contrast formula is not public in these sources. An independent robust percentile/logarithmic normalization must be labeled as an approximation, not Bookmap's hidden algorithm. Independent normalization should avoid a single extreme order crushing all useful contrast, and avoid per-frame brightness pumping.

### 3.5 Vertical smoothing [V2]

Confirmed modes are **Auto**, **Manual**, **None**. Auto chooses smoothing based on zoom; Manual exposes strength; None preserves unsmoothed levels. The screenshots establish the visible difference. Production 7.9 release notes specifically mention improved detail at small sizes under smoothing.

Smoothing blends vertically adjacent liquidity. It does not analyze how long an order stayed, whether it moved with the market, whether it was executed, or who placed it. It can make near-market liquidity look like a broad envelope. Keep raw/no-smoothing visibility available when inspecting a potential noise-filter problem.

### 3.6 Aggregate Market Depth [V2]

Normal mode shows quantity at each price separately. Aggregate mode shows cumulative size from the best level through each successive level on that side. For example, level 3 contains sizes of levels 1+2+3. This explains the broad, increasingly bright zones away from price in the cumulative screenshot.

Do not confuse this with averaging adjacent pixels or filtering genuine liquidity. It changes the measured quantity.

### 3.7 Active range, extended depth and stale data [V2]

Some providers transmit only a fixed number of levels around the BBO. Bookmap can retain each out-of-range level's last reported quantity and continue displaying it in heatmap and COB. The docs explicitly call this an estimate or record of the last known size, not a current verified quantity.

Controls include:

- Override the number of active levels with a smaller manually selected depth.
- Reset inactive levels after a configured number of minutes.
- Reset out-of-range levels at session end where the provider supplies the end-of-session indication; the page specifically cites Rithmic.
- Reset Now.
- Leave reset controls unchecked to keep last-known out-of-range values.

The COB marks active range with horizontal boundaries. A reproduction must distinguish live known depth, stale retained depth and unavailable data. Extending a stale level indefinitely without labeling it misrepresents liquidity. Reconnection must invalidate stale book assumptions until the feed rebuilds a valid book.

## 4. Trade bubbles and zoom: documented behavior

### 4.1 The basic unit is a pixel-time slice [V3]

Each bubble can aggregate trades executed during the time represented by one horizontal pixel. When zooming in, a pixel spans less time; when zooming out, it spans more time. Therefore the same underlying executions can become several smaller dots when zoomed in and one larger dot when zoomed out.

The dot center marks its time and the **volume-weighted average execution price** for that interval. In Smart clustering, both time and price are weighted averages of the grouped trades. Do not anchor an aggregate bubble to a random constituent trade or to a bar close.

Bookmap scales bubble size using user settings and an internal algorithm comparing that pixel-time's volume against the instrument's average trade volume. The exact curve, averaging horizon, outlier treatment, cap and smart-cluster distance formula are not specified. An area-proportional rule (`radius ∝ sqrt(volume)`) is a defensible independent choice, but cannot be attributed to Bookmap without additional evidence.

### 4.2 Aggressor semantics and colors [V3, V14]

The detailed docs describe buy and sell aggressor volume. The conventional observed scheme is green for buyer-initiated trades and red for seller-initiated trades, with separate configurable colors. Green buys generally execute against resting asks; red sells generally execute against resting bids. One short sentence in V1 uses “Bid aggressors”/“Ask aggressors” ambiguously; it should not be used to invert the economic interpretation established by the detailed docs.

Mixed clusters retain both buy and sell contributions. Native drawing types:

- **Gradient:** blend the two aggressor colors according to relative buy/sell volume.
- **Solid:** split the dot into two solid colors according to relative volume.
- **Pie:** split as a pie.

There are **2D dots** and **3D bubbles**, size and transparency controls, and total-volume versus **volume delta (buy minus sell)** display. The exact size/appearance convention for zero-delta clusters is not documented and needs an explicit independent rule.

### 4.3 Two different minimum-size filters [V3]

**Minimal trade size** filters individual transactions before considering them as displayed trades. A threshold of 100 permits a 100-lot transaction and rejects a 90-lot transaction.

**Minimal displayed volume** filters the aggregated dot. One hundred individual one-lot trades can qualify as a 100-lot dot if they fall in one pixel-time interval. Zoom in far enough to separate them and that dot can disappear; zoom out and it can reappear. This is intended display behavior, not necessarily a data-loss bug.

The page also uses the older label “Minimum accountable dot volume.” Preserve the semantic distinction rather than treating every threshold as the same field. Visual filtering must not silently alter independent session totals, profiles or CVD unless their own calculation filters explicitly request that.

### 4.4 Clustering modes [V3]

| Mode | Documented behavior | Implementation consequence |
|---|---|---|
| Smart | Merge adjacent/overlapping dots using an internal algorithm; weighted time and price; strength slider. | Screen/zoom-aware grouping; exact proprietary clustering unavailable. |
| By time | A dot every configurable period. | Stable explicit time buckets separate from viewport pixels. |
| By volume | Create a bubble after configured traded volume is reached; actual dots can exceed a threshold. | Define conservation and oversized-trade handling; do not silently lose or double-count remaining volume. |
| By price | Aggregate consecutive trading at the same price until trading moves to another price. | This is run-based grouping, not “all trades at a price for the whole session.” |
| By price and aggressor | Aggregate at a price until aggressor side changes. | Price and side transitions split runs. |

The official by-volume example states threshold 100 and trades 20, 15, 35, 270 can produce a 300-volume dot and carry 40 into the next dot. The volume-bars section gives a different overshoot example (50 then 100 produces a 150 bar). Do not pretend these prose examples fully specify one shared boundary algorithm; document any independent deterministic policy and preserve total volume.

Controls also include “Apply to Bars,” “Inherit from Bars,” and **Apply vertical in-pixel aggregation**, which combines points sharing the same horizontal coordinate. The current screenshot also shows an “Apply smart clustering on top” control; its complete interaction matrix is not explained in the retrieved prose.

### 4.5 Volume bars [V3]

Volume bars appear at the bottom of the heatmap. They aggregate execution volume across their time span, with buy/sell gradient or solid split coloring. Default width is **one pixel**, configurable to **15 pixels**. Bar height can be resized by dragging its top boundary, and the right-side number indicates the maximum volume in view.

Smart, time and volume clustering are documented, along with price aggregation in the current page. Settings can be inherited from or applied to dots. Rendering bars and bubbles from the same raw executions avoids mismatched totals when changing visibility or grouping.

## 5. BBO, price lines, navigation and interaction

### 5.1 BBO and last price [V1]

BBO is a pair of staircase-like traces: best bid and best ask. Last Price is the latest execution price, a different signal. In the inspected screenshots the BBO traces are green and red; they should not be replaced with a smoothed close-price line.

There are two documented per-pixel trace modes: last value only, or the minimum/maximum range inside the pixel. The former is cleaner; the latter preserves brief extremes. Current docs add automatic hiding of BBO when vertical zoom makes the spread smaller than a pixel threshold, displaying Last Price instead. Hysteresis prevents flicker:

`reappear threshold = hide threshold × (1 + hysteresis factor)`.

### 5.2 Navigation [V6]

Bookmap supports time-slice selection, wheel zoom, axis drag zoom, rectangular “Zoom by Drag,” Ctrl+Z zoom reversal, and horizontal/vertical panning. In drag mode wheel zoom anchors at the cursor; toolbar zoom anchors at the viewport midpoint. Arrow keys move a pixel and Shift+arrow moves ten.

Recenter modes include BBO, Trades, Last Price and None. Recenter-in-drag and relative vertical zoom are configurable, with tolerance settings. An embedded NinjaTrader indicator may use NinjaTrader's native pan/zoom rather than reproducing the whole Bookmap shell, but historical data must stay correctly attached to the host price/time axes.

### 5.3 Data tooltip and crosshair [V6, V10]

Tooltip fields include time/date/timezone, side, order quantity and price, plus traded volume for dots. Centering the cursor on a dot yields its VWAP. The crosshair projects labels onto price and time axes. The events list can show raw depth/trade events from selected periods, optional nanoseconds, current-instrument filtering and volume-only filtering.

For an independent indicator, tooltip content should additionally state any aggregation interval, unknown aggressor volume, stale-depth status and filtered/raw quantity when those distinctions matter. These additions should be plain trading information, not internal implementation details.

### 5.4 Refresh and retention [V6, V12]

V6 documents a default chart refresh rate of **40 FPS / 25 ms**, configurable for weaker hardware. Refresh rate is not the market-event sampling rate; skipping a repaint must not skip depth changes or executed volume.

Extended historical data is a separate service feature. V12's older page describes 7.6, BookmapData and a small supported-symbol list; the newer page describes 7.7, all CME and multi-book instruments, variable historical start dates, and unsupported CBOT/COMEX/NYMEX at that documentation revision. Both warn about incomplete add-on/column compatibility. This capability cannot be supplied merely by drawing historical NinjaTrader candles, and it must not be promised without the corresponding historical L2 data.

## 6. Columns: same price axis, different quantities

### 6.1 Column composition and controls [V5]

Columns can be inserted, hidden, resized and reordered; they can be moved to either side of the chart. Available families include Current Order Book, Volume, Trades Counter, Quotes Counter, Quotes Delta, Time & Sales, Trading DOM, Custom Notes and Cloud Notes.

Common display controls include bars/numbers/both, single or split histogram, buy/bid and sell/ask visibility, alignment, inverse display, colors and delta. Keep a reserved column area so current-book bars do not obscure the latest historical events.

### 6.2 COB: pending orders [V5]

COB displays current resting quantity by price, with a number at its top indicating the quantity represented by the full column width. It can use quantities or percentages of the visible order book; displayed percentages sum to 100% and use two decimal places. It offers cumulative aggregate and extended-range modes. Cumulative COB excludes iceberg/large-lot indications from that aggregation.

COB is a snapshot at the relevant right-edge time when navigating history, not always an invitation to display today's live book against an old chart. A historical right edge and a live book must be explicitly distinguished if the host integration cannot reproduce historical COB.

### 6.3 CVP and SVP: executed volume [V5]

- **CVP**: traded volume within the visible chart interval.
- **SVP**: traded volume accumulated since subscription/session start as described by Bookmap.

Both are distributions of executions over price, with combined or separate buy/sell values. Panning can alter chart-range profiles; Bookmap's drag-mode documentation also notes session columns follow the visible right-edge boundary rather than adding later unseen live data to an old view. This boundary behavior needs an explicit design choice in the replica.

### 6.4 Counts, quote delta, resets and POC [V5, V8]

- CTC/STC count executions by price over chart/session ranges; counts are different from traded size.
- Quote counters count size updates at price, not executed contracts.
- Quotes Delta shows liquidity added (+) or pulled (-) at prices; best ask/bid rectangles are red/green, and its delta resets on BBO updates.
- Volume/trade/quote columns offer manual, scheduled, conditional per-price inactivity and optional double-click reset.
- Columns can show a VWAP line for their selected range.
- POC is the price with maximal selected activity. Native chart POC is documented for session-based Volume, Trades Counter and Quotes Counter columns, not chart-range columns.

A decrease in displayed depth may be a cancellation, execution, feed truncation or reset. Aggregate L2 alone does not justify labeling every negative change as a deliberate pull or a known participant action.

### 6.5 Tape and trading features [V5]

Time & Sales reconstructs tape events based on transmission order; it supports side and min/max-size filters, detaching and export of the last 10,000 raw events. Trading DOM places/modifies/cancels orders. Notes can contain level annotations and alerts. These are platform features beyond a passive visualization indicator and should be listed separately from implemented chart studies.

## 7. Order-flow feature inventory beyond the main heatmap

This is a broad inventory of current official documentation, not an assertion that every paid or third-party add-on can be duplicated from generic NinjaTrader L2. Data requirements and algorithm availability vary.

| Feature | Documented behavior/settings | Reproduction considerations |
|---|---|---|
| CVD [V14] | Cumulative buy-minus-sell aggressor volume; Symmetrical/Include 0/Compact scale; multiple indications; min/max individual volume; chart/session scope; reference/reset points; split buyers/sellers; colors. | Straightforward with reliable aggressor classification and clear reset scope; unknown-side trades need explicit handling. |
| AVWAP [V15] | Multiple named VWAPs with time/session anchors, editable reference points, ±standard-deviation/custom bands, color/width/visibility. | Define volume-weighted variance and timezone handling; native docs note a timezone issue, which should not be copied. |
| Orderbook Imbalance [V16] | `(bid size − ask size)/(bid size + ask size)`, selected absolute/percentage/all level range, exponentially lower weight farther away, sound threshold. Uses book at visible right edge. | Aggregate depth is sufficient, but unavailable/stale depth and zero denominator must be handled. |
| Volume Imbalance [V16] | `(buy aggressor volume − sell aggressor volume)/(sum)` over visible data, range/smoothing/sound settings. | Distinct from CVD and book imbalance; display versus calculation filtering must remain clear. |
| Footprint [V17] | Time 30 sec–30 min, reversal, range, volume bars; sum/delta/diagonal delta/buy-sell histograms and text; independent foreground/background; smart tick/time scaling; detailed per-bar stats. | A substantial additional display mode; use the same execution ledger to avoid inconsistencies. |
| Market Profile [V18] | 30-minute letter TPOs, configurable RTH/secondary/tertiary sessions, tick aggregation, POC/VAH/VAL, corresponding volume profile, initial-balance periods. | Time-at-price/TPO is different from executed-volume profile; needs session/calendar rules. |
| Liquidity Marker [V13] | Significant size changes over **5 seconds**, threshold/auto-threshold, maximum top markers, near-BBO exclusion, increasing/decreasing colors; separate maximum-liquidity-level mode; overlap aggregation splits on zoom. | L2 sufficient for a heuristic; limited-depth windows can produce false detections. Native add-on explicitly does not support extended depth. |
| Outside Liquidity [V19] | Sum off-screen bid/ask size at left/right visible times, delta/both modes, level limits, include visible levels, optional price zero; half-life distance weighting. | Requires retained depth outside viewport; it cannot invent levels absent from the feed. |
| Strength Level [V20] | Underflow (executions exceed observed resting size) and Resistance (post-trade replenishment), delay/minimum size, automatic standard-deviation threshold, colors/alerts. | Explicitly heuristic and data-sequence sensitive; false positives are documented. Does not reveal unexecuted hidden resting orders. |
| TraderMap Pro / Lite | Separate filtered views of liquidity, with their own data requirements. | See dedicated liquidity research; do not equate palette cutoff or smoothing to TraderMap's filtering. |
| Large Lot Tracker / LT Pro | Large-order tracking and associated visualization. | Individual-order claims depend on MBO and provider capability; aggregate price size is not individual order size. |
| Absorption / Sweeps | Highlight specific execution/price/liquidity patterns. | Parameters, event ordering and data quality matter; see dedicated research. |
| Stops/Icebergs and B3 Icebergs | Exchange/data-specific detected events and on/subchart displays. | Cannot promise native-order identification from anonymous price-level depth. |
| Market Pulse | Configurable collections of order-flow signals/widgets. | Extensive separate product with many presets; not one universal formula. |
| DOM Pro / Execution Pro / Multi-account / Multibrackets | Order-entry/execution workflows. | Beyond a passive indicator; would require separate trading-system scope and testing. |
| Multibook / Instrument Copy / Cross BBO | Combine, copy or overlay instrument/exchange data. | Require additional feeds and coordinate/size normalization. |
| Trade Impact / Liquidations / Options Strike | Specialized analytics/market-specific event overlays. | Requirements depend on supported source data; generic L2 does not contain every event type. |
| TA Indicators / Price Levels / CATS / Breakeven Point | Conventional technical studies, level overlays and specialized analytics. | Separate add-ons; see their official pages for exact configuration. |
| Journal / Statistics / Setups Checklist / Simulator | Workflow, analysis and simulated execution. | Product ecosystem features rather than heatmap rendering. |

Further source URLs for the named catalog: [DOM Pro](https://bookmap.com/knowledgebase/docs/Addons-DOM-Pro), [Market Pulse](https://bookmap.com/knowledgebase/docs/Addons-Market-Pulse), [Multibook](https://bookmap.com/knowledgebase/docs/Addons-Multibook), [Instrument Copy](https://bookmap.com/knowledgebase/docs/Addons-Instrument-Copy), [Trade Impact](https://bookmap.com/knowledgebase/docs/Addons-Trade-Impact), [TA Indicators](https://bookmap.com/knowledgebase/docs/Addons-TA-Indicators), [Breakeven Point](https://bookmap.com/knowledgebase/docs/Addons-Breakeven-Point-Indicator). The official [knowledge-base sitemap](https://bookmap.com/knowledgebase/sitemap.xml) enumerates the current documentation collection. A dynamic marketplace cannot honestly be described as a permanently exhaustive closed feature list.

## 8. Independent implementation decisions supported by the research

These are recommended engineering choices, not undocumented claims about Bookmap internals.

### Data and calculations

1. Retain the raw valid book and executions separately from display filtering. Use integer tick keys and integer quantities where the market supports them.
2. Preserve level lifetimes, changes, removals and unavailable periods; do not manufacture historical L2 from candles.
3. Retain buy, sell and unknown-side volume independently. Use feed aggressor flags where supplied; otherwise use documented quote/tick classification with an unknown fallback.
4. Render retained event time, never the wall-clock arrival time of a delayed repaint. Synchronize shared data safely and render immutable snapshots.
5. Use explicit session/calendar resets and bounded history. Track the recording start so the UI does not imply a complete session when only a partial session is available.
6. Build raw and filtered liquidity views from the same source; expose exact filter rules and limitations. Size, persistence and relative contrast are separate concepts.

### Visual composition

- Classic dark slate/near-black chart surface.
- Sequential black/navy → blue → pale blue/white → yellow → orange → red heat palette, with alternative palettes and exact/percentile cutoffs. RGB anchors and interpolation are independently selected approximations.
- Thin horizontal tick bands, subtle horizontal grid and dashed vertical time grid.
- Green/red step BBO traces, optional Last Price, correct min/max/last-per-pixel behavior.
- Configurable translucent flat or shaded buy/sell circles with Gradient/Solid/Pie modes and VWAP positioning.
- Pixel-time aggregation plus optional explicit grouping modes; zoom reaggregation must conserve execution totals.
- Bottom volume bars, right aligned price ladder and reserved COB/CVP/SVP columns.
- Optional CVD and imbalance displays without obscuring the heatmap.
- Clear raw/filtered, live/replay, depth-availability and recording-range state.

### Heat color approximation

A deterministic initial transfer can use lower/upper thresholds `L/U`, percentile or absolute selection, logarithmic/linear normalization, contrast and brightness. For logarithmic normalization:

`u = clamp((log(1+q) − log(1+L)) / (log(1+U) − log(1+L)), 0, 1)`.

This is an independent choice. It is not Bookmap's documented internal contrast algorithm. Guard `U <= L`, absent depth, zero/negative invalid quantity and extreme values. Adaptive thresholds should update at a configurable cadence and avoid gratuitous intensity oscillation.

### Pixel and time accuracy

- Bubble and depth coordinates must be derived from the current chart viewport, not cached pixels from a previous zoom.
- Time/price summaries must state whether they represent last, maximum or time-weighted average quantity; repeated snapshots of the same resting order must not be summed as new liquidity.
- Far-zoom price aggregation must not imply individual-level size when it actually combines several levels.
- Smart grouping is a view transformation; it must not mutate the underlying trade ledger or discard volumes from independent metrics.
- Separate a display radius cap from the true cluster volume and expose exact values in tooltips.

## 9. Validation and remaining unknowns

### Data correctness tests

- Insert/update/remove and price shifts; duplicates/zero size; empty sides; feed resets and reconnect.
- Snapshot initialization before historical rendering; an unchanged order stays visible without receiving a new update every frame.
- Fixed-depth range changes do not create false “new giant orders” or “mass cancellation” claims.
- Exact quantity and percentile color cutoffs, all-equal distributions, extremes and manual-scale stability.
- Individual-trade filter versus aggregate-dot filter; zoom split/merge behavior; oversized-volume grouping; VWAP/time centroids.
- Buy/sell/unknown conservation across raw ledger, profiles, CVD, bars and visible clusters, with each metric's explicit filters accounted for.
- Historical right-edge versus live COB; CVP versus session profile; explicit reset boundaries.
- Thread-safe market-event ingestion while rendering and changing parameters.

### Visual comparison tests

Use three reference scales: millisecond detail, several-minute execution flow, and broad/session liquidity. Record viewport size, display DPI, instrument tick size, time/price range, palette/cutoffs, smoothing and bubble settings. The inspected official screenshots support composition/appearance comparisons. Exact event-by-event pixel comparison requires the same underlying feed/replay sequence and settings.

Check horizontal band continuity, tick alignment, bubble centroids, palette ordering, alpha blending, BBO visibility, clipping, column widths, profile alignment, tooltip hit-testing and font readability. Recheck at multiple Windows DPI settings and after device/render-target changes in the native host.

### What remains unavailable from public evidence

- Exact default color-map RGB stops and native automatic contrast formula.
- Native bubble size normalization curve and complete Smart clustering algorithm.
- Full interaction rules among every legacy/current bubble setting and by-volume carry behavior.
- Proprietary filtered-liquidity and individual-order detection internals beyond documented behavior.
- Pixel-identical reproduction of the 8.0 alpha UI from classic knowledge-base screenshots.
- Assurance that every market-data provider sends enough information for every feature.

These are specific limits on equivalence, not reasons to skip building the correctly specified core. A native NinjaTrader implementation can be thoroughly tested and visually close while documenting where its data, algorithms or host UI differ.
