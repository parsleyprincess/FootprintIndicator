# Research ledger

Research performed October 22–29, 2026; each report records its own access dates and evidence.

Primary Bookmap product documentation, TraderMap Pro documentation/blog, and actual official screenshots were retrieved and inspected. The initial network restriction was resolved. The documents below separate sourced facts, direct visual observations, independent algorithms, and unavailable proprietary details.

- [Bookmap visuals and settings](bookmap-visuals.md)
- [Liquidity filtering, TraderMap Pro, and bubbles](liquidity-and-bubbles.md)
- [NinjaTrader release/API/compilation investigation](ninjatrader-platform.md)
- [Original engineering plan](implementation-plan.md)
- [Actual implemented coverage](../FEATURE-COVERAGE.md)
- [Current validation report](../validation/REPORT.md)
- [Expanded Bookmap indicator/settings catalogue](bookmap-feature-catalogue.md)
- [Screenshots and decoded demonstration frames](bookmap-motion-evidence.md)
- [Uploaded NinjaTrader and requested Bookmap video review](uploaded-video-review.md)
- [October 29 Bookmap interaction video: zoom, sliders, hover and parenthesized numbers](bookmap-oct29-video-review.md)
- [Future MBO integration contract](mbo-integration-contract.md)

The selected workflow starts with NinjaTrader's trial feed and possible later Rithmic. NQ, ES, GC, YM, RTY, 6E, 6B and DAX-family futures use instrument metadata rather than a symbol whitelist. Standard price-level depth is the baseline; actual depth entitlements must be checked per contract/account. A different provider alone does not connect the standalone MBO engine.

Bookmap’s classic chart reference uses dark surfaces, scalar liquidity intensity from dark/blue through pale/yellow/orange/red, green/red BBO, shaded trade bubbles, and aligned right-side book/profile columns. Source links and image observations are in the visual report. Native proprietary algorithms are not disclosed by those sources.

NinjaTrader’s official release/help pages returned vendor HTTP403 during research. A third-party documentation mirror records 8.1.8.2 dated August 11, 2026; the vendor’s latest release remains unconfirmed. The user compiled and ran an earlier indicator revision; the latest repaired adapter still needs Windows/NinjaTrader compilation and runtime validation. Portable builds/tests and the generated preview must not be represented as that validation.

Several official Bookmap video/animation frame sequences and settings screenshots were inspected directly. The user's uploaded NinjaTrader recording and exact requested Bookmap introduction were also decoded and visually inspected; the [uploaded-video review](uploaded-video-review.md) records timestamps, observations and limits. The upload made that reference available despite the earlier YouTube retrieval failures. This was a decoded-frame review; no audio was listened to or transcribed. The recordings show the earlier behavior and the visual reference, not a validation of the revised Windows indicator.

The later [October 29 recording](bookmap-oct29-video-review.md) supplies direct views of heat/bubble hover cards, highlighted historical bid/ask paths, time zoom, the native **Contrast black cut-off** and **Volume dots size** sliders, and independent heat dimming. Its `N (n)` right-edge numbers are visible, but the available evidence does not establish their precise semantics. The implementation's explicitly labeled **run / last** statistic is independently defined; it is not claimed as a verified reproduction of that native overlay. The heat floor likewise uses a fraction of the current ceiling rather than claiming Bookmap's percentile/normalization formula.

Research assets remain outside the application checkout at `/workspace/research-assets/`; source URLs and source revisions are recorded in the reports. No proprietary vendor DLLs or third-party chart images are redistributed in the source package.
