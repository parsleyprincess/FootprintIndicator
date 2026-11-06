# Five uploaded FootprintMap recordings — October 30, 2026

Visual review completed November 4, 2026. These recordings show the earlier FootprintMap adapter running in NinjaTrader, with `ES DEC26` selected. They establish concrete problems with price positioning, zoom, contrast, column labels and bubble readability. They do **not** validate the later fixes: a fresh Windows/NinjaTrader run is still required.

## Sources and review method

| Uploaded recording | Duration | File size | Principal evidence |
| --- | ---: | ---: | --- |
| `20261030_094957.mp4` | 401.685 s | 17,438,532 bytes | Live price below the usable plot; excessive upper price space; repeated heat-control adjustments |
| `20261030_095840.mp4` | 50.688 s | 3,808,874 bytes | Wide time zoom, empty time before recording, visible heat ceiling around 47.9 |
| `20261030_100232.mp4` | 246.784 s | 11,667,051 bytes | Hover quantity 127 above a 51.9 color ceiling; sweep labels beside trade bubbles |
| `20261030_100927.mp4` | 207.019 s | 11,110,182 bytes | Wide-to-narrow zoom changes ceiling; crowded dots/markers; sparse side-column digits |
| `20261030_102354.mp4` | 84.011 s | 5,339,259 bytes | Repeated time zoom; detailed BBO steps/dots return at 133.7 seconds, while side-column digits remain sparse |

All five are 2032 × 1096 pixels and have audio streams. The review inspected each chronological overview, selected full-resolution frames, the 100927 sequence around 120–190 seconds, and the one-second 102354 sequence around 60–71 seconds. It also inspected the October 29 Bookmap zoom comparison at 48, 50 and 52 seconds. **Audio was not listened to or transcribed; this is not a claim to have watched every encoded frame.**

The videos remain in `/workspace/attachments/`, and extracted frames remain outside Git at `/workspace/research-assets/uploaded-oct30/`. `nov4-review-provenance.json` records current `ffprobe` output, source paths, file sizes and SHA-256 hashes. `frame-149.832.jpg` identifies the existing sample requested at that elapsed offset; `exact-130.jpg` is an independently available exact-seek sample. These are video offsets, not the chart's market timestamps. Seek offsets identify the first decoded frame selected around that position, not every intervening event. No video, vendor image or extracted frame is included in the source package.

## Specific observations

| Recording and elapsed offset | Directly visible evidence | Consequence and limitation |
| --- | --- | --- |
| **094957 — 00:00.000** | Header says `LAST 7677.75`; the chart scale extends to about 7693 at the top. The trade path descends below the heat area into the boundary reserved for CVD/volume. An off-scale arrow badge is visible, while COB/SVP continue farther down the panel. | An off-scale badge helps diagnose the problem but does not make the execution path usable. Main heat, BBO, bubbles and side columns need a shared price transform restricted to the main plot; live centering should keep the active price in that area. The top of the scale is over 15 points above the last trade. |
| **094957 — overview 01:49–06:40** | Repeated adjustments produce mostly dark bands, blue/white bands, and broad yellow heat while the trade layer remains visible. The sidebar calls gamma `Brightness`, separately from `Heat opacity`. | The label conflates nonlinear tone mapping with overall brightness. The controls need distinct names and a clear explanation of floor, gamma, ceiling and opacity. Changing appearance is not a change in recorded contracts. |
| **095840 — 00:22.585** | Header shows `1245.2s`, gamma `2.93`, opacity `95%`, cutoff `20%`, ceiling `47.918`. The captured region starts around 09:47; hovering earlier time reports `No recorded depth at this time.` | Blank time before capture is correct. Zoom should remain anchored to the intended point and disclose available coverage; it must not manufacture missing depth to fill the screen. This frame does not prove a trial-feed limit. |
| **100232 — 01:07.032** | `637.5s`; ceiling `51.862`; gamma `5.09`. Hover at 7684.25 reads raw ask `127`, persistent ask `127`, rendered bin `127`. The corresponding band is saturated red. Several `SW 50`/`SW 66` labels overlap the nearby bubble field. | The heat quantity and ceiling are visibly different. A ceiling around 52 clips 127 and every larger quantity to the palette maximum. Marker overlap is a layout problem; the labels refer to detected volume, not additional trades. |
| **100927 — 02:10.000** | Header shows `5937.4s` and ceiling `126`, gamma `0.61`, cutoff `20%`, opacity `95%`. Captured data occupies only the right part of the roughly 99-minute window. | The time window is much wider than the retained recording. Empty time is expected; the bubble field is heavily compressed. |
| **100927 — 02:29.832** | Header now shows `637.5s`, ceiling `66`, with the same gamma `0.61`, cutoff `20%`, opacity `95%`. Many bands have become pale blue/white/yellow. Trade bubbles and flow labels remain crowded. COB/SVP bars are present but only scattered numeric labels are drawn. | Zoom and a lower automatic ceiling jointly change appearance. This sequence cannot isolate a proprietary grouping formula or prove that a particular bubble split into specific constituent dots. Side-column labels need legible price grouping rather than silently skipping most rows. |
| **102354 — 01:00–01:08, including 01:07.918** | The sequence moves from a broad crowded view to `133.7s`. At 01:07.918, individual red/green quote steps and small trade dots are distinct. Gamma is `0.61`, cutoff `49%`, opacity `95%`, ceiling `63.606`. Hover shows a raw ask quantity of `19` with `Hidden by filter or not rendered.` | Time zoom already reveals more detail; the remaining task is predictable grouping, stable scale and readable overlays. A filtered cell can retain its raw hover value. Column label spacing is a separate vertical issue and remains sparse in this detailed time view. |

The headers repeatedly show `30/30 levels`, large `late` counters, `retained history trimmed` and `retained prints partial`. Those are observations of this run. Pixels alone cannot establish why events were late, whether the trial has a contractual 30-level cap, or whether another provider supplies more rows. Diagnosis requires comparing the same contract with NinjaTrader's native Level II window and reviewing event handling/status. A missing visible price row is not automatically evidence of missing incoming depth.

## Comparison with the user's Bookmap recording

The [October 29 review](bookmap-oct29-video-review.md) records the separate native Bookmap source. The inspected `zoom-048-050-052.jpg` composite shows major time ticks changing from five minutes to thirty seconds to five seconds. Nearby quote steps and dots spread apart as the view narrows. The October 30 FootprintMap sequence also reveals finer detail at narrower time ranges; its problem is the combined behavior of viewport anchoring, price positioning, changing heat ceiling and overlapping bubbles/labels.

The Bookmap source also has separate heat dimming, black cutoff and dot-size controls. It visibly retains understandable quote paths and a usable active-price region. These are behavioral references. The pixels do not establish Bookmap's exact scaling, clustering or color formula, and the native settings used in that source are not completely shown.

The native right-edge `N (n)` label remains semantically unresolved by the October 29 video. FootprintMap deliberately names its independent quantities `run N` and `last n`; resemblance must not be presented as proof of native equivalence. Likewise, a native iceberg annotation with parentheses is a different context and does not identify the right-edge label in this recording.

## What SW 51 and SW 356 mean

`SW` is FootprintMap's **sweep candidate**. The number is the total captured execution volume in that detected same-aggressor sequence: 51 or 356 contracts/units. It is **not an order count**, proof of one parent market order, a stop-order classification, or the size of an iceberg. The starting settings require at least 50 contracts within 500 ms across at least three distinct executed prices. An entire qualifying execution is counted, so a result can exceed 50 substantially.

`AB` is a same-price absorption candidate, `LP` an individual supplied large print, `+L/−L` a sampled depth change, and `RP?` a coarse replenishment candidate. Bubble grouping and these studies are separate displays of captured data. A sweep marker next to a bubble does not add its quantity to the profile a second time. The [complete label and addon matrix](bookmap-addon-request-matrix.md) defines the limits of each.

## Requirements and acceptance checks from these recordings

| User-visible requirement | Implementation direction in this revision | Acceptance in genuine NinjaTrader |
| --- | --- | --- |
| Live price remains visible | Independent main-plot price range with live centering and a Center price action; optional native scale | Let price move substantially in both directions, with CVD and columns enabled; confirm all price layers align and the last trade stays inside the usable plot. |
| Smooth, predictable zoom/scroll | Pointer-anchored time/price zoom, explicit history navigation, pan, Pause and Live | Anchor a recognizable historical point, zoom both ways, then pan; confirm the point does not jump to a new time and Live returns to actual captured data. |
| Pause remains useful while studying the chart | Freeze the displayed snapshot while continuing capture; optional pause on hover | Pause through incoming executions; check heat, bubbles, profiles, COB, last price, imbalance and hover use the frozen snapshot. Resume and confirm new capture appears. |
| Heat quantities remain interpretable | Separate tone, opacity, floor and ceiling; maximum-based default and a ceiling that does not fall during ordinary view changes | Compare hover quantities 47 and 127 at a fixed ceiling of at least 127, then change zoom. Check that values above a deliberately lower manual/percentile ceiling saturate as documented. |
| Bubbles expose useful detail | Deterministic retained-trade grouping, bounded bubble size, independent marker labels and color choices | At fixed resolution, follow completed interior groups as the chart advances. Then narrow the view and verify more detail without changing profile/CVD totals. |
| Column numbers remain readable | Sum nearby price rows into explicitly labeled groups when vertical space is limited | Zoom the price axis and inspect `ΣN ticks` headers. Check summed quantities, independent bars/numbers switches and one-tick detail when space allows. |
| Recorded history and disk usage are clear | Document autosave identity, replacement, retention caps and restore gaps | Close and reopen using the same capture settings; check saved history returns, current depth waits for fresh input, and time while closed remains blank. |
| Trial depth and MBO promises remain accurate | Separate observed MBP rows, indicator limits, provider entitlement and live MBO integration | Compare native Level II, confirm the exchange/contract entitlement with the provider, and do not infer order IDs from an aggregated price row. |
| Every requested Advanced Add-on is accounted for | [29-row coverage matrix](bookmap-addon-request-matrix.md#every-item-in-the-screenshot) with present behavior and explicit gaps | Review each item against its required input; related heuristics are not native-product parity. |

The [user guide](../USER-GUIDE.md) describes the delivered controls. Portable calculations and compatibility compilation can verify important parts of these changes; only a new native recording can establish chart-host behavior, visual alignment and interaction quality after this update.
