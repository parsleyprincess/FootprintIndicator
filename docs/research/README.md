# Research ledger

Research performed October 22–23, 2026; implementation/validation continued afterward.

Primary Bookmap product documentation, TraderMap Pro documentation/blog, and actual official screenshots were retrieved and inspected. The initial network restriction was resolved. The documents below separate sourced facts, direct visual observations, independent algorithms, and unavailable proprietary details.

- [Bookmap visuals and settings](bookmap-visuals.md)
- [Liquidity filtering, TraderMap Pro, and bubbles](liquidity-and-bubbles.md)
- [NinjaTrader release/API/compilation investigation](ninjatrader-platform.md)
- [Original engineering plan](implementation-plan.md)
- [Actual implemented coverage](../FEATURE-COVERAGE.md)
- [Current validation report](../validation/REPORT.md)

The selected workflow targets NQ, ES, GC, YM and RTY with NinjaTrader’s initial trial feed and possible later Rithmic. Standard price-level depth is the baseline. Actual market-depth entitlements must be checked per instrument/account.

Bookmap’s classic chart reference uses dark surfaces, scalar liquidity intensity from dark/blue through pale/yellow/orange/red, green/red BBO, shaded trade bubbles, and aligned right-side book/profile columns. Source links and image observations are in the visual report. Native proprietary algorithms are not disclosed by those sources.

NinjaTrader’s official release/help pages still returned vendor HTTP403 during the latest check. A third-party documentation mirror records 8.1.8.2 dated August 11, 2026; the vendor’s latest release remains unconfirmed. Genuine Windows/NinjaTrader compilation and runtime validation are pending. The completed engine builds/tests and generated preview must not be represented as that validation.

Research assets remain outside the application checkout at `/workspace/research-assets/`; source URLs and source revisions are recorded in the reports. No proprietary vendor DLLs or third-party chart images are redistributed in the source package.
