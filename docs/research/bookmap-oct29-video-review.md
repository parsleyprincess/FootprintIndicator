# Uploaded Bookmap interaction review — October 29, 2026

The new recording was decoded and visually inspected. It supplies direct evidence of chart-data hover cards, bid/ask path highlighting, independent heatmap controls, volume-dot sizing, and substantial time zoom. The numerical label beside the current-price badge is visible, but the reviewed frames do not establish what its parenthesized component measures.

## Source and method

- Source: `/workspace/attachments/e77b0a67-26ae-4fe5-b94a-8b7ec58b3d92/20261029_072646.mp4`.
- `ffprobe`: **2036×1108**, H.264 video, **261.355 seconds** container duration, 4,890 video frames, **28,729,915 bytes**. The stream reports a nominal rate of 48 fps and an average of `4005888/214069` (approximately 18.71 fps); this is not a constant-48-fps review.
- SHA-256: `9cb56459cf2a862120e631a0040b7e3767cf25857a69bd88b6ba0f6b89992993`.
- Assets: `/workspace/research-assets/uploaded-oct29/`, outside the checkout. `metadata.json` preserves the probe result; `provenance.json` records source and extraction details.
- A chronological five-second frame sample locates interactions. Important observations were checked using exact requested seeks (`exact-030.jpg` means `ffmpeg -ss 30 -i source -frames:v 1`, selecting the first decoded frame at or after that offset). The coarse `frame-NNN.jpg` sequence and its contact-sheet labels are approximate, and are not the evidence for the exact offsets below.
- The `late/` subdirectory contains independently inspected exact five-second samples and denser sequences from the latter half. Contact-sheet timestamps from a shortened segment are relative to that segment; add its stated start time.
- **No audio was listened to or transcribed.** An AAC stereo stream is present. This document reports visual evidence, not narration or a claim to have watched every encoded frame.

The selected instrument is `ESZ6.CME@BMD`. A red banner says market data is delayed for 15 minutes. The status bar says `Data: Live` and `Trading: Simulated`. The chart's displayed date/time uses **IST**; all recording offsets below are elapsed video time, not market time. The right columns are **COB** and **SVP**.

## Independent heat and bubble controls

| Exact clip offset | Direct evidence | Interpretation and limit |
| --- | --- | --- |
| **01:08.000** | The pointer is on the slider immediately beneath the heat color palette (approximately x618–765, y85). Its numeric popover shows **100**. | This is a separate control from the two smaller sliders near the left of the toolbar. |
| **01:10.000, 01:12.000, 01:14.000, 01:16.000** | The palette slider shows **25**, **17**, **69**, and then returns to the right end. At the low values the palette swatch itself and the entire heatmap become dim; bubbles, bid/ask paths and side-column colors remain readable. Strong bands keep their positions but become less luminous. | Direct visual support for a heat brightness/opacity control. These frames do not establish the exact numerical transfer function or an official tooltip name. |
| **01:20.000** | The upper small toolbar slider near x258–335, y65 shows **15**, with the native tooltip **“Contrast black cut-off”**. | This identifies the control explicitly. It should not be relabeled as an exact resting-contract threshold without a separate mapping. |
| **01:22.000** | The contrast slider shows approximately **60**. Compared with 01:20, many weak blue bands are suppressed while major orange/yellow levels remain strong. The palette slider remains at the right end. | Contrast cut-off and global heat brightness visibly serve different purposes. |
| **01:24.000–01:36.000** | The upper slider is moved repeatedly, including a visible **0** at 01:24 and **80** at 01:36. Weak heat fills in at the low end and recedes at the high end. | The interaction changes retained visible intensity, not the recorded location of the bands. Exact quantity, percentile or normalization rules cannot be recovered from pixels. |
| **01:48.000** | The lower small toolbar slider near x258–335, y85 has the native tooltip **“Volume dots size”**. | This is explicitly a bubble-size control. |
| **01:50.000–01:54.000** | The size slider is moved up and then to **0** at 01:54. Bubbles grow substantially and then shrink to very small marks while the bid/ask paths and heat remain visible. | Size is independent from contrast. The frames do not reveal the configured clustering mode or prove that the slider changes the underlying trades. |

Useful evidence assets: `contrast-tooltip-080.png`, `dots-size-tooltip-108.png`, `toolbar-exact-1.jpg`, `toolbar-exact-2.jpg`, `brightness-068-070-072-076.jpg`, `contrast-080-082-084-096.jpg`, and `bubble-size-108-110-112-114.jpg`. The composite images are presentation crops; their exact full frames remain alongside them.

## Hover fields and bid/ask paths

| Exact clip offset | Readable contents and behavior | What can be replicated |
| --- | --- | --- |
| **00:30.000** | The gray pointer-anchored chart card reads `29-Oct-2026 IST`, `07:26:06.821`, **`ASK ~16 at 7768.75`**, **`Volume: 104 at 7768.83055555`**. The bottom status strip mirrors these fields. | Date/time, the depth side/quantity/price under the cursor, and an extra volume row when a bubble is hit. The volume price has sub-tick precision, so displaying an aggregate price is supported; this card alone does not establish its calculation. |
| **00:45.000** | A chart card reads `29-Oct-2026 IST`, `07:25:44.490`, **`ASK ~25 at 7771.75`**, with no volume row. | Hover content depends on the target. A generic heat/depth card should not invent bubble volume. |
| **00:52.000** | In the detailed time view, a card reads `07:15:43.785`, **`ASK ~32 at 7759.25`**. The red and green historical paths are visibly separate horizontal steps with vertical changes. | Historical paths should preserve step geometry and remain distinct from bubble centers. |
| **00:55.000** | The pointer is below the execution path over heat. The card reads `07:15:43.321`, **`BID ~42 at 7754.75`**. | Depth hover follows historical time and hovered price, rather than always reporting the current best quote. |
| **02:45.000** | The card reads `29-Oct-2026 IST`, `07:29:26.540`, **`BID ~27 at 7770.25`**, **`Best bid: 7770.5`**. The green historical line is emphasized and a green **`Best bid`** label appears at its left side. | Hovering the quote path adds the named best-bid value and highlights the relevant path. The depth sample and best bid are different fields; here their prices differ. |
| **02:50.000** | The card reads `29-Oct-2026 IST`, `07:29:26.036`, **`ASK ~13 at 7770.75`**, **`Best ask: 7770.75`**. The red line is emphasized with a red **`Best ask`** label at its left side. | The ask path has the corresponding behavior and side color. |

The paths continue as **dashed red and green horizontal extensions** through the space to the right of the latest visible data. Historical portions remain stepped and solid. A compact bordered price badge sits on the price scale; the parenthesized numeric label is immediately to its left. These features should be treated as distinct overlays: historical BBO, forward extensions, current-price badge, and numeric trade/quote information whose semantics must be verified.

`hover-volume-030.png` preserves the bubble hover card. The later hover frames are `late/frame-165.png` and `late/frame-170.png`.

## Parenthesized right-edge numbers

The actual punctuation is **parentheses**, not square brackets. The user-visible pattern is `N (n)`: a white outside number and a red or green parenthesized number beside the current-price badge. Examples checked at exact seeks:

| Clip offset | Visible label | Price badge | Nearby displayed COB values |
| --- | --- | --- | --- |
| **00:30.000** | **`27 (9)`**, red parentheses | `7772.25`, red border | Red `25` by 7773; green `30` by 7772. |
| **01:10.000** | **`47 (9)`**, red parentheses | `7771.25`, red border | Red `26` by 7772; green `35` by 7771. |
| **01:20.000** | **`2 (1)`**, green parentheses | `7770.75`, green border | Red `26` above the badge; green `33` by 7770. |
| **02:30.000** | **`3 (3)`**, red parentheses | `7770.5` | See `late/frame-150.png`. |
| **02:55.000** | **`7 (2)`**, green parentheses | See full frame | See `late/frame-175.png`. |
| **03:00.000** | **`16 (4)`**, red parentheses | See full frame | See `late/frame-180.png`. |

The outside number therefore does **not** simply equal the parenthesized number. It also does not directly match the nearby visible COB text in these examples. However, the broad price scale labels whole points while the instrument trades in quarter-points, so the comparison is not proof about an individual best-quote row hidden by the coarse display.

The late sequence **04:11–04:20** includes the pointer beside/below this label without an explanatory tooltip. In the sampled sequence it changes from `29 (1)` to `32 (1)`, `34 (3)`, `38 (7)`, then `11 (5)` in red, later `7 (4)`, `12 (9)`, `13 (10)`, and `14 (11)` in green. The current-price badge also changes. Those transitions are evidence of a live numeric overlay; they do not prove whether its components are accumulated executed volume, recent trade size, aggressor run, order count, resting liquidity, or another statistic. The relative-time contact sheet is `late/brackets-251-260-contact.png` (add **251 seconds** to its captions).

**Do not label either component as best-quote quantity, largest order, or last trade solely from this recording.** Useful enlarged evidence is in `bracket-cob-030.png`, `bracket-cob-070.png`, and `bracket-cob-080.png`.

### Related documentation does not automatically identify this label

The cached official [Main Chart documentation](https://bookmap.com/knowledgebase/docs/KB-SettingUpAndOperating-HeatmapMainChart) defines BBO and last-price lines, but the inspected section does not explain these adjacent numbers.

The [Stops and Icebergs documentation](https://bookmap.com/knowledgebase/docs/Addons-Stops-And-Icebergs-On-Chart-Indicator) does define `17(17)` near an **active iceberg icon**: outside is total executed iceberg volume; parentheses contain the last trade volume. It separately defines bracketed event volume on D/T/E/C annotations. That is a documented **different display context**. This video has not established that the native right-edge `N (n)` label is the iceberg indicator, so those semantics must not be borrowed without corroboration. Cached source: `/workspace/research-assets/liquidity/Addons-Stops-And-Icebergs-On-Chart-Indicator.txt`, lines 120–136.

## Time zoom and bubble detail

The exact sequence **00:48 → 00:50 → 00:52** provides a substantial horizontal zoom comparison. At 00:48, major time ticks are five minutes apart; at 00:50 they are thirty seconds apart; at 00:52 they are five seconds apart. The same neighborhood around chart time **07:15:40** spreads across more horizontal pixels. Individual red/green dots and quote steps become easier to separate. A thin detail line replaces the dense appearance of the wider view, and bubble sizes also change. `zoom-048-050-052.jpg` preserves the execution area and time axis; the full exact frames remain available.

This proves zoom-dependent visual detail. It does **not** identify execution IDs, prove an exact one-to-many mapping of a particular wide-view dot, or reveal the proprietary grouping rule. Nor does it imply that old dots should repeatedly regroup during ordinary scrolling with unchanged zoom. The earlier October 28 bug recording addresses that separate stability requirement.

The official [Traded Volume Visualization documentation](https://bookmap.com/knowledgebase/docs/KB-SettingUpAndOperating-HeatmapTradedVolumeVisualization) supplies the relevant declared behavior: **Smart** clustering aggregates adjacent or overlapping dots and positions the result using weighted averages of trade prices and execution time; other modes group by time, volume, price, or price and aggressor. This supports a deterministic approximation that aggregates retained executions according to an explicit mode. The upload does not show which mode is selected.

## Consequences for implementation and acceptance

1. Keep global heat brightness and contrast/weak-liquidity suppression independently adjustable, and keep bubble size separate. Values visible in the recording are UI settings, not verified numbers of contracts.
2. Retain executions and quote history independently of projected dots. Allow explicit zoom/detail changes to regroup deterministically; keep completed historical data stable under ordinary scrolling.
3. Render bid and ask as distinct historical step paths with separate current extensions. Hover should identify the actual path and highlight it without confusing the depth price with the best quote price.
4. Populate hover content from the target under the pointer: timestamp, depth side/quantity/price, bubble aggregate volume/price where available, and BBO value for a path hit. Preserve unavailable or approximate values honestly; the video uses `~` for depth quantities.
5. Resolve the native parenthesized label from a matching source or explanatory tooltip before asserting or implementing Bookmap-equivalent statistics. An implementation may expose explicitly named useful statistics, but visual resemblance is not evidence of semantic equivalence.
6. Verify the final behavior in NinjaTrader against a known recording at several zoom levels. These reference frames establish requirements; they do not validate the changed indicator or its host interactions.
