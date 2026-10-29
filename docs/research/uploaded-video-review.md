# Uploaded video review — October 28, 2026

Both user-provided MP4 files were available locally, decoded successfully, and visually inspected. This supersedes the earlier statement that the requested Bookmap video could not be viewed because YouTube requests returned HTTP tunnel errors. The earlier network attempts still failed; the upload supplied the source independently.

## Sources and review method

| Recording | Local source | Decoded metadata |
| --- | --- | --- |
| NinjaTrader bug recording | `/workspace/attachments/3ad8bffd-4268-4cfc-83fc-bb7fba137a6e/20261028_065630.mp4` | 2032×1096; reported video frame rate 293/12; duration 80.064000 seconds; 4,044,332 bytes. |
| Requested Bookmap introduction | `/workspace/attachments/4d767120-d6f0-4f2f-9acd-e1add9d40cf0/YTDown.com_YouTube_Media_z_3M-dz_KCo_Bookmap-Introduction-and-Key-Features_001_1080p.mp4` | 1920×1080; 30000/1001 frames/second; duration 92.067120 seconds; 17,901,733 bytes. The supplied filename identifies [YouTube `z_3M-dz_KCo`](https://www.youtube.com/watch?v=z_3M-dz_KCo). |

`ffprobe` verified the local media metadata. The visual review used chronological contact sheets at four-second intervals and full-resolution frames at relevant timestamps. Additional reference samples at approximately one-second intervals helped locate interactions; exact seeks at 28.0 and 28.5 seconds establish the scale comparison. The one-second reference contact-sheet labels explicitly mark approximate offsets because of the frame-rate filter's sampling phase. Timestamps in the observation tables are clip offsets, not the chart's displayed market time.

This is a decoded-frame review, not a claim that every encoded frame was watched. **No audio was listened to or transcribed.** The files contain audio streams, but no finding below relies on narration.

Original uploads and extracted images remain outside the checkout. Research assets are in `/workspace/research-assets/uploaded-oct28/`; `upload-provenance.json` records source sizes and SHA-256 digests. Their hashes are:

- NinjaTrader: `4e84d008375c20908253cfc89ea6ab6e5950bb65720fd64c21747054ed2f3395`.
- Bookmap: `7ed20cb29328d88477c86e71b05fd2206dd9c781cbf37e253ddaa3f3d00672ee`.

## NinjaTrader recording: directly visible issues

The recording shows `ES DEC26` with the FootprintMap overlay. Its right side contains `COB` and `SVP · recorded`, and its lower lane combines a white CVD trace with executed-volume bars.

| Clip time | Direct visual observation | What it supports |
| --- | --- | --- |
| **00:00–00:28**, sampled every four seconds | The header remains at **1117.6s**, while the recorded history slowly shifts left. A comparison compensating for approximately 1.54 pixels/second of that movement shows old bubbles changing radius and overlap. The red disk beneath the early peak near **7760.75** is larger in the 00:08 crop than at 00:00; older clusters near **7759.50/7759.00** alternate between larger disks and smaller overlapping disks/rings. These regions are well behind the current-price edge. | The visual instability occurs during ordinary live scrolling at the same displayed time-window width. It is stronger evidence than simply comparing two zoom levels. Pixels do not identify exact execution IDs or prove which internal grouping rule caused it. |
| **00:00, 00:08, 00:28** | Many bubbles have muted olive, gray or pale red interiors. Dense neighboring circles overlap on top of pale cyan/yellow liquidity bands. | Aggressor colors and the boundaries of individual groups are hard to distinguish in this presentation. The clip alone does not reveal whether a muted disk is an intended blended group, overlapping translucent shapes, or background compositing. |
| **00:00 and 00:28** | A wide dark, gold-outlined price rectangle is drawn near the chart's right edge, while the host price axis shows another black price badge at the same level. At 00:00 both read **7759.75**; at 00:28 both read **7759.50**. | Duplicate presentation of the current price is directly visible. A compact badge near the price scale should preserve readability and avoid competing labels. |
| **00:28 → 00:32 → 00:36** | The displayed time window changes from **1117.6s** to **234.4s** to **90s**. Earlier history leaves the viewport; remaining heat bands and trades spread horizontally. The vertical price ladder remains approximately **7752–7767**. | This is horizontal time zoom. Changing projected distances is expected; stored executions and completed groups should remain identifiable through the transition. |
| **00:32 and 00:36** | Both frames label the lower lane **CVD -25** while showing different time ranges. The same retained interval is stretched horizontally, and its white trajectory occupies much of the shallow lane. The lane does not show a numeric vertical CVD scale or an explicit zero reference. | The current delta and the visual scale need to be distinguishable. A zoomed curve can look very different without the current CVD value changing; these frames do not establish a mutation of stored cumulative delta. |
| **00:36–01:08** | In the narrow view, the white CVD rise around chart time **06:56:15** sits near the upper portion of the lower lane. When broad history returns at 01:08, the same later interval sits lower while an older peak around **06:46** occupies the lane's upper portion. Intermediate labels also change as new executions arrive. | The clip is consistent with viewport-dependent CVD placement/rescaling. Because the lane has no vertical numerical labels and new trades arrive, the images alone cannot prove an exact scaling formula or separate every data update from a scale update. |
| **01:00 → 01:08 → 01:12** | Broader history returns, eventually leaving blank space before the first captured depth. The main path, liquidity history and lower plots remain tied to time; COB/SVP remain at the right edge. | A missing-history region and the current-book region should remain distinguishable from captured historical liquidity while navigating. |

The strongest old-bubble comparison is `nt-old-trades-aligned-4s.jpg`. It uses the same-size crops from 00:00 through 00:28, shifting only the crop origin to compensate for normal leftward scrolling. No images were stretched to force alignment. The source frames remain available as `nt-000s.jpg` through `nt-076s.jpg`. `nt-cvd-zoom-sequence.jpg` preserves the lower lane at native vertical resolution for 00:28, 00:32, 00:36, 00:40, 01:00 and 01:08.

## Requested Bookmap reference: directly visible behavior

The uploaded reference is an edited introduction with several chart scenes, replay controls, and large white presentation annotations. Those annotations should not be mistaken for native interactive tooltips.

| Clip time | Direct visual observation | Implication and limit |
| --- | --- | --- |
| **00:08** | Liquidity uses blue, pale yellow, orange and red bands against a dark background. Small and large red/green execution bubbles remain distinguishable from thin price paths. Separate **COB**, **CVP** and **SVP** columns share the price scale. White boxes and the words “Larger Player Liquidity” highlight selected regions. | Strong levels stand out while weak regions remain comparatively dark. The white highlights are presentation annotations; they do not establish a selection or hover interaction. |
| **00:20–00:24** | Replay advances. Historical heat, candles and bubbles shift left together; current depth quantities change. COB shows red above and green below the current-price area. CVP and SVP show separate red/green volume distributions and quantities. | Depth and volume columns need distinct statistics and displays on a shared price coordinate. This sequence does not reveal their exact accumulation/reset settings. |
| **00:28.0 → 00:28.5** | A vertical double-arrow cursor is visible on the price axis. The visible CL range contracts from approximately **63.64–62.16** to **63.51–62.28**. Heat, execution path and side-column distributions scale together. | The reference visibly supports coordinated vertical scaling. The frames do not establish the exact input gesture, an aggregation rule, or invariant bubble pixel radii under zoom. |
| **00:40** | Strong horizontal bands stand out against sparse dark regions. “Transition From Buyer To Sellers” points toward lower red/green volume bars. | Trade-volume bars and liquidity are separate layers. The annotation is explanatory video content. |
| **00:48–00:49** | Large and small red/green bubbles coexist with visible stepped bid/ask paths. Compact price and quantity labels sit near the current-price ladder. Blue/orange lower plots and gauges appear in an **Icebergs/Stops** context. | The display keeps execution size, quote path and current-price information readable. These lower plots are not evidence of CVD scaling behavior. |
| **00:50–00:56** | Large “Key Liquidity Levels,” “Stop Runs,” and “Evolution Of Iceberg Order” annotations overlay the chart. Smaller native labels include `T(33)`, `T(4)`, `E:99` and event quantities. | Event annotations have a separate visual vocabulary. They are not observed bubble or heatmap hover cards. Their presence does not reveal a private detection algorithm. |
| **01:00** | A gray **“Faster”** tooltip appears above the replay control beneath the cursor. Replay speed reads **X4**. At exact **01:02**, the same control area has no visible tooltip. | A genuine toolbar hover tooltip is directly observed. The inspected samples do not establish chart-cell or bubble data hover cards. |

The best exact vertical-zoom comparison is `reference-zoom-28-28p5.jpg`, which includes native price-axis crops. Full frames include `reference-exact-28.5.jpg` and the four-second `bookmap-*.jpg` sequence. Denser approximate samples are under `reference-1s/`.

The reference's heatmap is visibly darker and more selective than the NinjaTrader recording's nearly continuous pale/yellow field. The recordings use different data, market conditions, zoom and settings; this is a useful readability comparison, not proof that identical quantities should receive identical colors. No clearly resolved mixed-sector bubble was established from this specific upload. Earlier official animations provide separate evidence of mixed red/green sectors in [the broader motion dossier](bookmap-motion-evidence.md).

## Implementation and validation consequences

These observations motivate the following checks. They are requirements derived from the recordings and the requested changes, not claims that the new build has already passed a genuine NinjaTrader replay comparison:

1. Hold the time-window width steady while advancing the right edge. Completed historical bubble membership, volume and side composition should remain unchanged; only their projected horizontal positions should move.
2. Append new executions outside a completed group and confirm its radius does not change through a new viewport-wide normalization. Explicit zoom/aggregation settings may change detail, but ordinary scrolling should not repeatedly split and recombine old groups.
3. Render buy/sell composition so it remains legible over strong heat and near neighboring bubbles. Avoid inferring side from a muddy composite color alone.
4. Show the CVD scale and zero reference, and use a documented scale policy that prevents viewport changes from creating unexplained apparent jumps. Verify stored cumulative values independently of their projected screen positions.
5. Keep the chart's current-price presentation compact and readable alongside host labels. Inspect a true chart host at the user's DPI; a browser mockup cannot validate host-axis badge interaction.
6. Keep heat, executions and side columns aligned under vertical zoom. Use distinct current-depth and volume statistics.
7. Verify hover content against the retained data actually under the cursor, and clear it when the pointer leaves or changes target. Chart-data hover is a requested capability; this video establishes only the toolbar tooltip described above.

The uploaded bug recording supplies evidence of the old behavior. The uploaded introduction supplies concrete visual references. A final host acceptance check still requires the updated indicator running against a known data recording in NinjaTrader; these videos alone cannot validate the replacement build.
