# Feature coverage and limits

This describes the delivered source. The broader [Bookmap catalogue](research/bookmap-feature-catalogue.md) is research, not a list of completed features. The NinjaTrader indicator consumes market-by-price (MBP) depth and executions; the separate MBO engine has no live feed bridge.

An earlier version compiled in the user’s NinjaTrader installation, but reported runtime/display issues prompted subsequent changes. **The latest revision still requires compilation and chart testing on Windows.** The [validation report](validation/REPORT.md) records portable test evidence and the remaining platform checks; neither syntax parsing nor browser screenshots establish NinjaTrader runtime correctness.

## Chart and market-data features

| Capability | Delivered behavior | Limits / differences |
| --- | --- | --- |
| Historical liquidity heatmap | Duration-weighted raw depth; fixed tick-price history; classic/grayscale/ice palettes; contrast, cutoffs, opacity and automatic/manual ceiling | Default 250 ms frames; missing capture is not filled in; native Bookmap palette/normalization formulas are not reproduced |
| Current depth acquisition | Adapter copies the instrument’s authoritative `MarketDepth` bids/asks under NinjaTrader’s depth lock on callbacks; captures current rows when attaching mid-stream | Only rows actually supplied by the provider, up to configured limits; still requires current Windows/feed verification |
| Level-size filtering | Minimum level quantity and largest percentage of rendered price-bin quantities | Filters aggregate price levels, not constituent individual orders |
| Fixed-price persistence | Conservative quantity cohorts at each side/absolute price; newly added size starts young | Default off; aggregate continuity is not order age; maturation delays heat and can hide legitimate short-lived/moving size |
| Following-liquidity suppression | Optional large-quantity coherence at relative BBO offsets across prior moves; resets at gaps/epochs | Original heuristic, default off; can hide legitimate responsive liquidity; cannot identify bots or reproduce TraderMap Pro Bot Remover |
| Execution bubbles | Six grouping modes, VWAP centers, buy/sell/unknown accounting, separate print/cluster cutoffs, total/delta sizing, 2D/3D, gradient/split/pie | Smart uses an original pixel-grid rule; sizing uses an original square-root rule; volume groups retain the complete closing execution and may exceed the target |
| Observed best bid/ask | Raw BBO step traces and current quote guides | Heat filtering does not recalculate an executable filtered BBO; aggressor classification is inferred from supplied quotes, not certified exchange flags |
| Last price and current projection | Last-execution guide/badge, off-scale arrow, and translucent raw-depth strip after NOW | Projection is display-only and stops on disconnect; restored last price is labeled as recorded; no projected heat is saved as history |
| Current order book (COB) | Raw supplied quantities; independent bars/numbers, bid/ask visibility, per-side row limits, cumulative quantities, percentage labels and left/right alignment | Percentages use selected supplied rows on each side, including off-screen rows; current book even while browsing history; no individual queue priority, stale extended depth or estimated outside liquidity |
| Session/visible volume columns | Captured-session and retained-visible-range executed volume, side segments/delta, independent bars/numbers and left/right alignment | Session begins with accepted capture/reset, not earlier exchange history; visible column clips by time and price independently of bubble filters; no centered split/inverse display or independently styled columns |
| CVD and volume bars | Classified buy minus sell executions and total executed volume, with session resets | Unknown aggressor contributes zero delta but contributes total volume; no reconstructed parent aggressive orders |
| Continuous time navigation | Default 120-second independent time axis; Ctrl+wheel zoom; Shift+wheel history scroll; event-time live edge | Uses NinjaTrader’s native vertical price scale, which needs an appropriate range; native candles/bottom time labels use a different horizontal map |
| Native chart alignment | Optional native minute/second time-bar coordinates and retained-trade reaggregation | Requires two visible time bars and right margin for columns; non-time-bar alignment and Bookmap’s full navigation shell are not implemented |
| Instruments | Metadata-based tick normalization and price formatting without a symbol whitelist; whole-unit/contract volume | Broad futures support includes index, metal, currency and DAX-family definitions; feed availability and per-contract thresholds still matter; not a certification of every instrument |
| Feed/lifecycle handling | Reset/disconnect gaps, session counters, replay rewind restart, status, bounded input/output histories and renderer caches | Blank connection name conservatively observes all price-feed disconnects; supply the intended connection name when needed |

Raw heat is the starting view. Persistence and following suppression affect display eligibility; they do not modify accepted executions, raw COB or session accounting. A dark filtered chart can reflect its thresholds or latency. Automatic contrast rescales the displayed quantities, so use a fixed manual maximum to compare absolute brightness across filter settings.

## Profile and order-flow studies

| Capability | Delivered behavior | Limits / differences |
| --- | --- | --- |
| POC and volume value area | Session or visible-time profile; highest-volume POC; contiguous occupied-price expansion to configured volume percentage | POC ties choose lower price; value-area expansion chooses the larger adjacent row, lower side on a tie; own documented rule, not every vendor’s convention |
| VWAP and deviation bands | Volume-weighted price mean and standard deviation, with configurable multiplier for the selected profile scope | Horizontal captured-scope statistics; no arbitrary named anchors, multiple AVWAPs or full historical VWAP path; unknown-side volume is included |
| Volume imbalance | `(buy − sell) / (buy + sell)` for the selected profile scope | Unknown aggressors excluded; zero classified volume yields zero; no automatic threshold alerts or smoothing |
| Book imbalance and large levels | Nearest supplied levels with optional exponential price-distance decay; normalized bid/ask imbalance and large aggregate-level highlights | Uses current observed MBP even in a historical viewport; unavailable depth does not establish absent liquidity; large levels are not identified individual orders |
| Bid × ask footprint | Fixed time/price groups from retained captured executions; sell × buy labels, unknown volume and diagonal imbalance | Visible-time executions only, with a current-session restriction in Session scope; opposite diagonal must have positive volume; no range/reversal/volume bars or all native footprint modes |
| Captured TPO | Counts distinct fixed time brackets with actual executions at each grouped price; histogram with TPO POC/value-area highlighting | Same retained viewport/session restriction as footprint; no inferred visits between prints, letter layout, separate RTH/ETH profiles or initial-balance tool |
| Absorption candidates (AB) | Same-aggressor executions at one price within a time/volume threshold; passive-side color | Original heuristic; evidence is consumed after a trigger; not proven hidden liquidity or an iceberg classification |
| Sweep candidates (SW) | Same-aggressor executions across a minimum count of distinct prices within a time/volume threshold | Original heuristic; distinct prices are counted rather than price span; no parent market-order or stop-order identification |
| Large prints (LP) | Individual accepted executions at or above a configurable threshold | Independent of bubble clustering; no aggressor-ID tape reconstruction or sound/popup alert system |
| Sampled liquidity changes (+L/−L) | Quantity changes between adjacent completed depth-frame averages at prices observed in both frames | Optional coarse aggregate observation; missing boundary rows do not become adds/removals; cannot distinguish cancellation, execution or order replacement |
| MBP replenishment candidates (RP?) | Captured executions plus substantial remaining sampled displayed quantity at the same side/price | Optional coarse heuristic with one-frame completion latency; not millisecond-native refill measurement, same-ID evidence or iceberg detection |

Session POC/value area/VWAP use the independently maintained captured-session profile, which can survive trimming individual trades. Visible-scope statistics, footprint, TPO and execution-pattern studies depend on retained executions. Profile-study Visible scope selects the time window across captured prices; the visible volume column additionally clips to the visible price range. Flow studies scan retained current-session input and respect epoch breaks. Retention, budgets and incomplete capture limit historical coverage; the adapter exposes study/trim status.

## Saved capture and standalone MBO

| Capability | Delivered behavior | Limits / differences |
| --- | --- | --- |
| Disk capture | Versioned, checksummed, bounded snapshots of captured heat/trades/session totals; autosave every 30 seconds and on removal; enabled by default | Stored under the NinjaTrader user-data `FootprintMapHistory` folder; capture identity includes instrument/tick size, Trading Hours/time zone, frame and persistence settings; not an unlimited tick recorder |
| Restore | Validates capture identity, tick size, checksum and budgets; restores historical observations and compatible session accounting | Clears live book/connection state and requires fresh depth; no historical Level II download, synthetic gap filling or vendor replay import |
| MBO reconstruction | Separate portable `MboEngine` accepts normalized Add/Replace/Cancel/Execute plus authoritative order snapshots, sequence continuity and reset handling | **No live NinjaTrader/vendor bridge.** Requires a genuine compatible order-ID source and provider-specific normalization; depth positions must never be used as invented IDs |
| MBO order filtering/statistics | Individual min/max quantity, observed age/change filters, largest percentage, aggregate filtered depth and count/mean/min/max size | Core API only; observed age begins at the snapshot/first observation; display sorting is not exchange queue priority; no broker identity or complete TraderMap settings clone |
| MBO large-lot traces | Bounded history of changes/fills/cancels after an order qualifies, including price moves | Core API only; no native chart overlay or pre-snapshot trace |
| MBO same-ID refill candidates | Explicit matched executions followed by visible size increase on the same confirmed ID/price within a configured window | Core API only; reports observed refill/execution quantities, never hidden reserve or certainty of native iceberg identity |

See the [MBO integration contract](research/mbo-integration-contract.md) for input semantics, synchronization, capacity handling and feed requirements. Switching to Rithmic or acquiring MBO entitlement does not by itself connect this engine to NinjaTrader.

## Remaining gaps

Native stop/iceberg detection, proprietary Smart/Bot Remover formulas, all Bookmap/TraderMap settings, native alert workflows, trading/account/DOM execution actions, reconstructed MBO aggressive tapes, multi-book/cross-instrument tools, liquidation/options feeds, custom notes, arbitrary AVWAP anchors, quote-count columns, full Time & Sales UI, vertical smoothing, extended-depth retention and the Bookmap application shell are not implemented. Some require additional feeds or account integration; others require further independent UI and algorithm work.

The project offers an independently implemented set of market visualizations and studies. Research coverage, portable tests and a previously compiling adapter do not establish full Bookmap parity or validation of the latest Windows revision.
