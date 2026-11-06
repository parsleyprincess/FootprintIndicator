# Reading and controlling FootprintMap

This guide describes the revised indicator, including the fixes prompted by the five October 30 recordings. Portable tests and a synthetic preview are separate from validation in the genuine Windows NinjaTrader chart. See [installation](INSTALL-NINJATRADER.md) and [validation](validation/REPORT.md).

## Keep price centered and inspect history

The continuous view has its own **PRICE** axis and time axis. By default it follows the market in the middle of the usable heatmap area, above CVD and below the header. The host chart's outer axes still describe its underlying bars. Read the indicator's axes when using its independent view. **Use native price scale** restores the host's vertical transform if needed.

| Action | Result |
| --- | --- |
| **Pause** | Freezes the displayed snapshot, including heat, bubbles, COB and profiles. Incoming data continues to be captured. |
| **Live** | Returns to fresh captured data. |
| **Center price** | Recenters the independent price window on the displayed market and restores following when live. |
| **Ctrl + mouse wheel** over the main plot | Zooms time around the pointer and freezes the resulting interval for inspection. |
| **Shift + mouse wheel** | Moves through history and freezes it, rather than continually advancing an offset behind the live clock. |
| **Alt + drag** | Pans the time/price view for inspection. |
| **Alt + mouse wheel** | Zooms the independent price range around the pointer. |
| **Pause on hover** setting | Optional temporary freeze while inspecting the main plot; leaving returns to live unless a manual action paused history. Defaults off. |
| **Smooth scroll** setting | Optional bounded interpolation of the display clock. Defaults off, preserving movement with actual event time. It adds no trades or recorded liquidity. |

Pausing is a display action, not a feed or recorder pause. The frozen snapshot remains available while live retention advances, at the cost of holding that additional snapshot in memory. Resume Live to release it. Panning beyond the captured interval shows missing history, not invented depth. The vertical time grid selects readable clock-aligned intervals as you zoom: seconds, tens of seconds, minutes or hours. The interval is a navigation aid, not a different market-data sampling rate.

The initial price span is **80 instrument ticks**, so it respects each contract's tick size. Adjust it with price zoom; a universal number of price points would not suit ES, GC and currencies equally. Native candles are covered by default because their coordinates differ from this independent view.

## Why numbers disappeared, and what grouped numbers mean

The previous renderer suppressed labels when one price tick occupied less than 11 pixels. The revised COB, SVP and CVP can sum adjacent prices into readable, absolute tick bins. A header such as **Σ2 ticks** means each displayed row contains the sum for two price ticks. The sums are not claims about a single price or individual orders. Zoom vertically inward to recover single-tick rows.

- **COB:** supplied resting bid/ask quantity at the displayed snapshot. A group spanning both sides retains separate bid and ask quantities. Cumulative mode sums outward from the best quote on each side.
- **SVP · recorded:** executed volume accumulated during the captured session. It cannot include trades from before capture began.
- **CVP · visible:** retained executed volume within the visible time/price window, independent of the bubble-size filter.
- **TPO · prints:** distinct time brackets containing captured executions at each grouped price; not an inferred visit between executions.

The bars and numeric labels remain independently configurable. When the display is paused, the book and profiles are frozen too; they are not the current tradable quote. The live data continues in the background.

## Heat colors, tone and dimming

The heatmap represents sampled resting quantity. Blue is weaker; pale/yellow/orange/red is stronger relative to the chosen ceiling. **Raw** hover quantities are time-weighted capture-frame averages. The **Rendered bin** may aggregate time/price cells and apply filters. A current COB quantity can differ from historical heat at the same price.

The former default used a 97th-percentile automatic ceiling. If that ceiling was 47, both 47 and 127 saturated at the hottest color. That was clipping, not a claim that the quantities were equal. The new maximum-based default uses percentile **100**, with an automatic ceiling that does not fall merely because the view pans or narrows. New larger observations can raise it. A positive fixed manual ceiling gives exact control; setting it below a quantity deliberately saturates that quantity.

| Control | What changes |
| --- | --- |
| **Tone (gamma)** | Changes the distribution of intensity. Values above 1 darken weak/intermediate levels while leaving the ceiling hot. This is not uniform brightness. |
| **Dimming / opacity** | Changes how strongly the heat colors cover the background. This is the overall dimming control. |
| **Black cutoff** | Suppresses intensities at or below a fraction of the current ceiling, then remaps the remainder. A 10% cutoff with ceiling 200 suppresses quantities at or below 20. |
| **Min. level quantity** | Absolute minimum displayed quantity, independent of the percentage cutoff. |
| **Heat ceiling / Auto–Manual** | Controls which quantity reaches the hottest color. Percentile, maximum-based automatic and manual modes have different clipping behavior. |
| **Level persistence / following filter** | Optional aggregate-depth filters. These can suppress legitimate liquidity and are not individual-order bot identification. |

A balanced starting point is **raw depth**, **gamma 1.4**, **cutoff 10%**, **automatic contrast with percentile 100**, and the default opacity. Leave persistence/following suppression off initially. To isolate stronger levels, increase the cutoff or absolute minimum gradually. To compare 47, 72 and 127 contracts directly, select a manual ceiling at least 127 and keep that ceiling unchanged. These are display starting points, not universally optimal trading thresholds.

Existing NinjaTrader templates preserve old saved settings. Updating source alone may keep gamma 2, cutoff 20% or percentile 97. Apply the values above explicitly or add a fresh instance.

## Bubbles and zoom

Bubbles describe executed volume, not resting orders. Their area relates to volume using a fixed reference, so a new large trade does not resize all completed historical bubbles. Zoom changes how retained prints can be grouped; it does not change profile or CVD accounting. A forming live cluster can grow, and a cluster cut by a view/retention boundary can be partial.

**Pie** shows buy, sell and unknown proportions. **Dominant** uses one solid color for the strictly largest category: green buy, red sell, neutral unknown/ties. Gradient and split styles remain available. Hover always reports the underlying totals even when one color is selected.

Smart/Pixel grouping can combine prints at nearby times and prices. The resulting volume-weighted center can lie between the bid/ask paths or away from either path at its weighted time; that is not an additional execution. Inside-spread/unknown prints can also appear between quotes. Use **PriceAndSide**, **Price**, or **Individual** and zoom inward when you want to see price-specific detail. Individual means each supplied print, not guaranteed exchange order IDs or unbundled fills.

## Chart labels

| Label | Meaning and limitation |
| --- | --- |
| **SW 51 / SW 356** | Sweep candidate containing 51 / 356 captured contracts/units from same-aggressor executions across several prices. Defaults require at least 50 within 500 ms across at least three distinct prices. Not proof of one parent order or a stop. |
| **AB** | Same-price absorption candidate, default at least 100 contracts within 2 seconds. Shows a passive-side inference, not a confirmed iceberg. |
| **LP** | One supplied large execution, default threshold 50. Independent of bubble grouping. |
| **+L / −L** | Change between adjacent completed depth-frame averages. Does not distinguish a cancellation from an execution. |
| **RP?** | Coarse replenishment candidate based on captured executions and remaining aggregate depth. Not native iceberg identification. |
| **POC / VAH / VAL** | Highest-volume price and boundaries of the selected profile's contiguous value area. |
| **VWAP / bands** | Captured-scope volume-weighted average and optional deviation bands. Not multiple arbitrary anchored VWAP studies. |
| **CVD** | Captured session buy-aggressive volume minus sell-aggressive volume. Unknown prints add volume but zero delta. |
| **run 27 / last 9** | 27 consecutive captured contracts at the latest execution price, with 9 in the last supplied print. Price/epoch/session changes end the run; ≥ means the beginning may have been trimmed. |

Study labels have backgrounds and are painted after bubbles, but dense signals can still compete for space. Disable labels or increase the relevant threshold to reduce clutter. A marker beside a bubble does not count its quantity twice. The [29-item addon matrix](research/bookmap-addon-request-matrix.md) lists implemented studies and gaps, including native stops/icebergs, account/trading tools and the absent live MBO bridge.

## Recording and disk space

**Save and restore captured history** records market observations, not screen video. It saves every 30 seconds and on removal in the actual NinjaTrader user-data folder's **FootprintMapHistory** directory. A matching `.fph` file is replaced atomically. It does not create another full archive every 30 seconds.

Defaults retain 30 minutes, at most 7,200 heat frames and 200,000 executions. Count limits can trim history sooner on busy contracts. Session totals/profile are maintained separately from individual retained prints. Each archive has a 128 MiB limit and a four-million-heat-cell limit; excessive settings can fail saving visibly. Files for different contracts or capture-defining settings accumulate: there is no automatic total-folder cleanup. The [recording analysis](research/bookmap-addon-request-matrix.md#recording-disk-use-and-reopening) explains sizes and identity fields.

Reopening compatible settings restores retained history and waits for fresh depth before showing a live book. Time while the indicator was closed remains a gap. A new Trading Hours session resets session counters, while retained history remains subject to limits. Disabling saving does not delete old files. Close the indicator before removing unwanted `.fph` archives; that removes their local history.

## Trial feed, Rithmic and diagnostics

The videos show 30 bid and 30 ask rows, but do not prove a contractual trial-specific cap. Compare the same contract/connection in NinjaTrader's **Level II** window. If it receives the same rows, an indicator cannot create deeper liquidity. FootprintMap defaults to a capture budget of 200 rows per side, configurable up to 2,000; COB display limits and the viewport can further restrict what is shown.

Paid Rithmic may provide different depth according to the exchange entitlement and connection. It does not automatically give this standard NinjaScript adapter exchange order IDs. Native MBO iceberg/stop tools still need a compatible order-ID feed bridge and validation. CME and Eurex/DAX entitlements and data paths must be checked separately.

**Timestamp adjustments** (previously labeled **Late**) counts events whose timestamps arrived behind the engine's current clock. The existing engine accepts valid late executions and their volume, but normalizes their timestamps to keep stored time monotonic. It is not a discarded-trade count. Fine timing/classification remains limited by supplied quotes and provider callback ordering. **Retained prints partial** means the retained per-print history has hit a time/count boundary; session totals can still include older executions.
