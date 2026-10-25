# Feature coverage and deliberate limits

The product research inventories more Bookmap features than a single NinjaTrader indicator can reproduce from standard Level II. This table describes the actual delivered source, not a promise that every Bookmap add-on is implemented.

| Capability | Delivered source | Limits / differences |
| --- | --- | --- |
| Historical liquidity heatmap | Duration-weighted raw depth; fixed tick-price history; classic/grayscale/ice palettes; contrast, cutoffs, opacity | Sampled at configurable frame duration, default 250 ms; no missing history invented; exact native palette transfer function is not published |
| Size filtering | Minimum level quantity and top percentage of rendered price-bin quantities | Filters aggregate price levels, not individual MBO order sizes |
| Level persistence | Conservative quantity cohorts at absolute side/price; newly added size starts young | Aggregate continuity, not individual-order age; configurable latency |
| Following-liquidity suppression | Optional large-quantity, relative-offset coherence across prior BBO moves; resets on gaps/epochs | Original heuristic, default off; can hide legitimate responsive liquidity; cannot identify bots or exactly reproduce TraderMap Pro |
| Volume dots | Six grouping modes, VWAP center, buy/sell/unknown accounting, separate print and cluster cutoffs, total/delta, 2D/3D, gradient/split/pie | Smart uses an original pixel-grid approximation; area uses an original average-normalized square-root rule; volume grouping keeps whole closing trades rather than the native documented carry/split example |
| Best bid/ask | Raw observed BBO step traces | Some Bookmap filtered-chart BBOs are recalculated after MBO filters; this indicator keeps the actual observed book |
| Current book | Raw captured bid/ask bars with quantities | Only feed-supplied retained levels; no individual-order queue tracking or estimated outside liquidity |
| Profiles | Session and retained-visible-range executed volume; side split/delta; labels | Visible range is limited by retained trades; session totals start with captured session data, not earlier historical trades |
| CVD, volume bars, VWAP | Classified executions and session resets | Unknown aggressor excluded from delta but included in total/VWAP; not exchange-certified aggressor flags |
| Feed and lifecycle handling | Disconnect/reset gaps, replay rewind restart, status, bounded histories and renderer caches | Blank connection name conservatively observes all price-feed disconnects; configure the actual connection name when needed |
| Chart zoom/scroll | Reaggregation from retained trades and native time-bar anchors | Supported on minute/second charts; not a standalone continuous-time chart platform or non-time-bar equivalent |
| MBO-specific TraderMap Pro filters | Research only | Order-ID, broker-ID, exact order duration/change, average order size/count/distribution, canceled-order identity need data unavailable in the baseline |
| Specialized add-ons | Research inventory only | Native stop/iceberg detection, LLT, Absorption/Sweeps alerts, liquidation feeds, Market Pulse, multi-book, footprint, market profile, trading/execution panels are not implemented |
| UI and persistence | NinjaTrader indicator property settings; in-memory capture | No Bookmap menu clone, disk recorder, workspace-wide replay importer, alert UI, vertical smoothing/cumulative-depth display, or cross-trading |

The goal is a useful, independently implemented visual analogue. Genuine NinjaTrader compilation/runtime checks remain a separate acceptance gate; portable tests and preview images do not certify that gate.
