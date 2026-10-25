# Proposed architecture and validation plan

This is a provisional engineering plan, not a claim about Bookmap's internal implementation. The blocked product and visual research must be completed before choosing final behavior or writing the indicator.

## Data model and event semantics

Maintain bid and ask books independently. Older NinjaTrader examples corroborate positional insert/update/remove operations; confirm their contract against current vendor documentation before implementing the adapter. Convert the resulting book into absolute integer tick-price keys. Bookmap's publicly demonstrated absolute-size callback semantics must not be copied onto NinjaTrader's different event contract without adaptation.

Represent price as an integer tick index within the engine, with validated conversions at the boundary. Use the instrument's actual tick size and contract metadata; do not hard-code one instrument's price precision. Preserve long volume values with explicit overflow handling and reject malformed data rather than silently corrupting the book.

An observation has a time, sequence, connection epoch, instrument, side, price and quantity. Preserve source order where timestamps collide. Keep raw observations separate from filtered display values. A zero quantity, an unobserved level, and a gap caused by disconnection are different states.

Maintain depth coverage explicitly. A price dropping beyond the delivered ladder is not proof of cancellation. On reset/reconnect, clear invalid live state, mark the continuity break, and wait for usable depth. Do not extrapolate stale heat across a disconnected interval or invent history from OHLC bars.

## Proposed display layers

| Layer | Proposed behavior | Critical validation |
| --- | --- | --- |
| Liquidity history | Time-price heatmap of observed resting quantities | Correct side/price, duration, coverage gaps, deterministic scale |
| Filtered liquidity | Optional size and fixed-price persistence/churn filters | Raw view remains available; causal output; no future leakage |
| Trade bubbles | Aggressor-classified executions, aggregated in screen-space or documented time/price bins | Total volume conserved; ambiguous prints explicit; stable zoom behavior |
| Best bid/ask | Step lines from valid book/BBO observations | No line across disconnected epochs; synchronization with trades |
| Current book | Current visible depth histogram and quantities | Clearly distinguished from historical and executed volume |
| Volume profile | Executed buy/sell/unknown volume by tick | Session/visible-range scope explicit; totals conserved |
| Cumulative delta | Classified aggressive buy volume minus sell volume | Session resets, ambiguity, and replay consistency |

This is an initial engineering inventory, not a completed inventory of Bookmap's options. Product documentation will determine which additional settings and layers belong in the implementation. Specialized iceberg/stop identification and order execution features require separate data and API evidence; ordinary aggregated depth cannot prove individual hidden orders or trader intent.

## Liquidity filtering

The user's target problem is low-value liquidity that repeatedly follows the best bid/ask. A proposed filter should evaluate persistence at an **absolute price**, not merely “three levels from the market.” Otherwise a moving ladder can look persistent when the underlying price levels are changing.

Candidate inputs are absolute minimum displayed quantity, relative size among nearby observations, a rolling lower envelope of observed quantity at a fixed price, and displayed-quantity addition/reduction churn. These must be explicitly named as observations and heuristics. They do not identify a trader's intention, prove spoofing, or recreate an undisclosed TraderMap Pro formula.

Use causal calibration: a frame cannot depend on future observations. Do not retroactively show an order as having met a persistence threshold before it did. Make threshold/normalization scope visible and support per-instrument overrides for NQ, ES, GC, YM and RTY. Percentile-based defaults should have a sample warm-up and robust handling of sparse books. Disconnects and unknown coverage invalidate persistence evidence.

## Bubble aggregation and zoom

Separate classification from display aggregation. Prefer an authoritative aggressor flag when actually supplied. When classifying from quotes, document quote timing and crossed/locked-market behavior, and retain an unknown category rather than invent certainty. Any tick-rule fallback must be an explicit, tested option.

Use bucket sums for executed volume. Heatmap liquidity is a resting quantity over time and requires a different aggregation rule; summing repeated snapshots would produce meaningless volume inflation.

Candidate bubble sizing uses area proportional to volume, so radius scales with the square root of volume. Treat this as a proposed visual rule until Bookmap documentation and screenshots establish its settings. Enforce finite minimum/maximum radii and make clipping understandable. If multiple trades merge on zoom-out, preserve total buy/sell/unknown quantities. Do not silently drop small trades before calculating profiles or delta.

Historical rendering must derive from time and price, not current bar indices alone. NinjaTrader's time-based and non-time-based chart spacing can differ; verify the supported chart modes before claiming all modes work identically. Record exact viewport inputs so a rendering test can reproduce a zoom state.

## Rendering and resource lifecycle

Use a market-data engine with no WPF or NinjaTrader dependency and a thin NinjaTrader adapter. Publish consistent immutable render snapshots or use a short, bounded lock; never enumerate collections that a market-data thread mutates. Keep expensive aggregation out of market-data callbacks.

Use SharpDX resources only on the appropriate renderer lifecycle. Dispose target-bound brushes/bitmaps on render-target change and all owned resources at termination. Handle null chart controls, changing DPI, minimized/resized windows, instrument changes, workspace reloads, empty history and invalid scales.

Set explicit history, event, and cache budgets. Decimate data with documented units and preserve gaps. Benchmark sustained depth bursts and trade rates, rendering at several viewport sizes and zoom levels. Do not let rendering backpressure block feed processing.

## Validation matrix

| Area | Necessary cases |
| --- | --- |
| Depth reconstruction | Insert at top/middle/end; update; remove; duplicate price; inconsistent position; reset; crossed book; truncated ladder |
| Integer price/volume | All target tick sizes; roundoff at boundaries; zero/negative invalid sizes; large volumes; overflow |
| Causality | Identical event prefixes yield identical historical output regardless of later events |
| Noise filtering | Stable wall; wall added late; moving ladder; rapid pull/re-add; coverage loss; disconnect; sparse normalization |
| Trade classification | Bid/ask executions; inside spread; stale quotes; locked/crossed book; equal timestamps; unknown side |
| Aggregation | Exact volume conservation across zooms; gap preservation; viewport clipping; stable scale settings |
| Sessions | Correct instrument trading-hours boundaries; overnight sessions; replay seek; reload; session reset |
| Concurrency/resources | Simultaneous feed/render; target recreation; termination; repeated workspace open/close |
| Runtime | Authentic NinjaTrader compilation/import; replay with Level II; live feed; entitlement absent; reconnect |
| Visual comparison | Same scene and viewport criteria for palette ordering, relative bubble sizes, overlap, best bid/ask, profiles and contrast |

Synthetic scenarios validate invariants; they do not prove exchange-feed compatibility or visual identity with Bookmap. Final reporting must distinguish engine tests, adapter compilation, actual NinjaTrader runtime tests, and human-reviewed visual comparison.
