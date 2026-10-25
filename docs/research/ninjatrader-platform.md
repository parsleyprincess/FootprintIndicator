# NinjaTrader platform research and verification boundary

Research dates: 2026-10-22–23. This report distinguishes directly inspected evidence, established engineering constraints that still need current vendor confirmation, and proposed implementation choices. No NinjaTrader indicator has been compiled or run as part of this research.

## Current release: not yet verified

The latest NinjaTrader Desktop version **cannot currently be stated responsibly**. Initially, official release notes, developer documentation, and support requests failed at the environment network proxy with `Tunnel connection failed: 403 Forbidden`. After the environment network changed, those hosts became reachable but returned an actual HTTP 403 body `error code: 1010`, a Cloudflare response. This is a separate vendor-side access problem; repeating the same allowlist edit is not the remedy. A date in this task is not evidence of a particular release. Older versions in sample projects are not the current release.

Required official checks after environment networking is applied:

- Release notes index: <https://ninjatrader.com/support/helpguides/nt8/release_notes.htm>
- Desktop developer documentation: <https://developer.ninjatrader.com/docs/desktop>
- Vendor support: <https://support.ninjatrader.com/>
- Official download/account entry point: <https://ninjatrader.com/>

Record the actual release number, publication date, installer download location, supported Windows versions, .NET requirement, and compiler language level from these sources. Do not infer an installer URL by guessing a version number. The root agent saved allowlist additions. The subsequent vendor response establishes improved network reachability, but the actual pages remain unavailable. A secondary documentation mirror contains release notes through 8.1.8.2 (2026-08-11); this is useful evidence about an observed version, not proof of the latest vendor release on 2026-10-23. See the research update below.

## Available evidence

The following public GitHub projects were accessible and inspected. They are **third-party corroboration**, not vendor documentation and not proof of latest-release compatibility. No source from them has been copied into the product.

1. Free Order Flow, inspected commit `7b5f2005c587734e9900e7f3ca74c67c93be0f0d`:
   - [Project and reference dependencies](https://github.com/trading-code/ninjatrader-freeorderflow/blob/7b5f2005c587734e9900e7f3ca74c67c93be0f0d/FreeOrderFlow.csproj)
   - [Market-depth indicator example](https://github.com/trading-code/ninjatrader-freeorderflow/blob/7b5f2005c587734e9900e7f3ca74c67c93be0f0d/Indicators/FreeOrderFlow/FofMarketDepth.cs)
   - [SharpDX chart-coordinate example](https://github.com/trading-code/ninjatrader-freeorderflow/blob/7b5f2005c587734e9900e7f3ca74c67c93be0f0d/DrawingTools/FreeOrderFlow/FofAnchoredVwap.cs)
   - [Windows CI build](https://github.com/trading-code/ninjatrader-freeorderflow/blob/7b5f2005c587734e9900e7f3ca74c67c93be0f0d/.github/workflows/build-and-release.yml)
   - [Export metadata](https://github.com/trading-code/ninjatrader-freeorderflow/blob/7b5f2005c587734e9900e7f3ca74c67c93be0f0d/publish/info.xml)
2. MicroTrends NinjaTrader8, inspected commit `a7de4d9541f9f0f5de75f1eee64683c545c4e394`:
   - [Repository](https://github.com/MicroTrendsLtd/NinjaTrader8/tree/a7de4d9541f9f0f5de75f1eee64683c545c4e394)
   - Its `ATSQuadroStrategyBase/NinjaTrader 8/bin/Custom/NinjaTrader.ATS.Custom.csproj` targets .NET Framework 4.8, language version 5, references NinjaTrader binaries from the Windows installation, and imports `Microsoft.CSharp.targets`.

The Free Order Flow project targets `net48`, selects C# 8 for its external build, uses WPF, and references NinjaTrader.Core, NinjaTrader.Gui, NinjaTrader.Custom, SharpDX, and SharpDX.Direct2D1. Its build workflow uses `windows-latest`. Its export metadata contains version `8.1.1.0`, while passive PE/.NET metadata inspection of its bundled Core and Gui binaries reports assembly version `8.0.28.0`. This mismatch demonstrates why those artifacts must not be treated as the latest official SDK.

Downloaded examples are outside the application repository under `/workspace/research-assets/ninjatrader`. No bundled proprietary binary is being committed, redistributed, loaded, or used as a trusted compiler reference. Passive metadata inspection is evidence about those files only; it does not establish vendor authenticity or license permission.

## Target language and architecture

The appropriate integration language is C# NinjaScript for NinjaTrader Desktop 8. The known NT8 architecture uses .NET Framework and Windows WPF plus SharpDX/Direct2D rendering. A .NET 8 SDK installed on Linux is useful for a portable C# data engine and its tests; it does not turn NinjaTrader into a Linux application or validate the actual WPF host.

Use conservative C# syntax in code shared with NinjaScript: ordinary classes/structs, explicit using blocks, conventional constructors, no records, nullable-reference annotations, collection expressions, or modern runtime-only APIs. Keep engine dependencies within the eventual Framework target's BCL. A sample's external `LangVersion=8.0` does not prove the built-in NinjaScript editor supports exactly that language version. A subsequently inspected secondary mirror reports C# 13 support introduced in NinjaTrader 8.1.6; see the update below. Conservative source syntax remains an engineering compatibility choice, not a claim that current NinjaTrader only supports C# 5 or 8.

Recommended separation, once research is complete:

- A deterministic, platform-neutral engine for tick-size normalization, order-book reconstruction, liquidity history, filtering, aggressor classification, trade aggregation, volume profile, and cumulative delta.
- A thin NinjaTrader adapter for lifecycle, market events, time conversion, chart coordinate mapping, settings, and rendering.
- A rendering model whose values can be tested independently from SharpDX, with a genuine Windows host test for the final drawing implementation.

## Market-data interface and consequences

The following APIs are corroborated by inspected source and/or passive metadata of the older bundled assemblies. Confirm them against current documentation and genuine current installation references before declaring compatibility.

| API / field | Observed shape | Integration consequence |
|---|---|---|
| `OnMarketDepth` | `protected override void OnMarketDepth(MarketDepthEventArgs e)` | Consume Level II updates separately from trades. |
| Depth event | `MarketDataType`, `Operation`, `Position` (`int`), `Price` (`double`), `Volume` (`long`), `Time` (`DateTime`), `IsReset` (`bool`), `MarketMaker` (`string`) | Preserve side, positional semantics, timestamps, and resets. |
| Depth operation | `Add`, `Update`, `Remove` in inspected source | Insertion/removal can shift ladder positions; do not naively treat every position as a permanent price key. |
| `OnMarketData` | `protected override void OnMarketData(MarketDataEventArgs e)` | Consume Last/Bid/Ask events; count executions only on Last. |
| Market-data event | `Ask`, `Bid`, `Price` (`double`), `Time`, `Volume` (`long`), `IsReset`, `MarketDataType` | Prefer contemporaneous bid/ask supplied with executions where valid. |
| Connection callback | `OnConnectionStatusUpdate(ConnectionStatusEventArgs e)` appears in examples; subsequently inspected mirrored documentation explicitly includes indicators | Use `PriceStatus` for price-feed connectivity; order-system `Status` is different. Confirm on the target installation. |

Correctness requirements for the proposed engine:

- Normalize prices to integer tick indices with a well-defined rounding rule; do not use unrounded floating-point prices as dictionary identities.
- Apply bid and ask updates independently. Position lists must handle inserts, updates, removes, resynchronization, and provider-specific out-of-sequence events. A mismatch should trigger an explicit degraded/resync state rather than silently retain ghost levels.
- Clear or invalidate active depth on a reset/disconnect. Mirrored documentation says `IsReset` is only relevant to columns after manual disconnect; an indicator must not rely exclusively on receiving it. Handle price-feed connection lifecycle independently. Keep historical heatmap separate from the active book, and visibly mark gaps rather than painting liquidity through missing data.
- Treat `Volume` on a depth update as the supplied level size rather than automatically adding it as an increment. Validate the exact provider contract.
- Do not assume an order count or individual order IDs exist. These Level II fields generally describe aggregated price levels. Filters needing order identities or exchange MBO semantics require additional data the standard adapter may not expose.
- Do not label every removed quantity as cancellation: an execution and a depth decrement can overlap, and quote/trade callbacks may arrive in different orders.
- Classify a trade at/above a valid ask as buyer-aggressive and at/below a valid bid as seller-aggressive. Crossed/locked quotes, missing quote context, and inside-spread trades need an explicit fallback policy and unknown classification. A tick-rule fallback is inference, not exchange-certified aggressor data.
- Use 64-bit volume counters and guard aggregation against overflow and non-finite prices.
- Retain actual event time plus a monotonic processing sequence. Replay, equal timestamps, time zones, and local clock changes must not break ordering or the historical axis.

### Historical-data limitations to verify against vendor docs

Standard historical OHLCV bars cannot reconstruct the order book or recover individual trade clusters. Tick Replay and historical tick data must not be represented as historical Level II. A genuine historical liquidity view requires previously recorded depth or an appropriate replay/provider data source containing it. Current depth starts when the indicator is subscribed. Replay should be tested with an actual Market Replay recording containing Level II, and a visible “depth history begins here” boundary should distinguish missing history from zero resting liquidity.

Exchange/feed entitlements determine whether depth is available and how many levels are delivered. A connected market-data feed and a moving last price do not prove Level II is present. Readiness should report depth event count, last depth timestamp, active level count, trade count, and connection/reset status without requiring a trading order.

## Rendering and chart interaction

Inspected source corroborates:

```csharp
protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
public override void OnRenderTargetChanged()
chartScale.GetYByValue(price)
chartControl.GetXByBarIndex(ChartBars, index)
```

Passive older-assembly metadata additionally corroborates `ChartControl.GetXByTime(DateTime) -> int`, `GetTimeByX(int) -> DateTime`, `CanvasLeft/CanvasRight -> int`, `ChartBars.FromIndex/ToIndex -> int`, and `ChartPanel.X/Y -> int`.

For a heatmap, explicit time coordinates are preferable to attaching every depth event to the nearest bar. However, NinjaTrader charts use their own time/bar spacing and session gaps; verify `GetXByTime` under time, tick, volume, and range bars before promising a Bookmap-like continuous wall-clock axis. A supported time-based chart may be the initial requirement. Never silently stretch unknown intervals into historical liquidity.

Proposed renderer requirements:

- Compute visible time and price bounds once per frame; draw only visible heatmap cells and bubbles.
- Aggregate historical samples to screen columns and price rows based on zoom, retaining underlying data so zooming back in recovers detail.
- Preserve trade volume when regrouping bubbles. Radius proportional to the square root of volume gives area proportional to volume; document any min/max radius clamps.
- Show whether bubble coloring represents total side, delta, or a buy/sell split; do not confuse resting bid/ask size with executed buy/sell volume.
- Cache color brushes by palette and render target. Create/dispose render-target-bound resources in `OnRenderTargetChanged`; release all remaining resources at termination. Dispose text formats, layouts, geometries, and temporary brushes.
- Handle `RenderTarget`, `ChartControl`, `ChartBars`, and `ChartPanel` being unavailable during initialization, reconfiguration, export, or termination.
- Save and restore renderer state such as antialias mode when changing it.
- Snapshot engine data under a short lock, then render outside that lock. Never enumerate a live mutable collection from the UI callback while market-data callbacks modify it.
- Throttle repaint requests; a refresh for every depth event can saturate the UI. Rendering cadence and data ingestion must be independent.
- Bound history and memory explicitly, expose retention settings, and retain summarized history only with clear resolution metadata.

A depth heatmap can be an overlay behind price; the inspected example uses `SetZOrder(-1)` in `State.Historical`. The final design must determine whether to hide candles or provide a separate display mode without unexpectedly changing the user's chart template.

## Authentic compilation and runtime validation

### What is possible in the current Linux machine

The root agent installed the SHA512-verified Microsoft .NET SDK 8.0.426 under `/workspace/.tools/dotnet`. This supports compiling a portable engine and running deterministic behavior, property, replay, and performance tests. Those tests can verify reconstruction and rendering-model calculations but cannot establish NinjaTrader import, callbacks, Direct2D drawing, or current-version compatibility.

A fake NinjaTrader API shim could make arbitrary code compile while concealing wrong API signatures. It must not be presented as platform validation. This project should not use such a result as evidence of readiness.

### Preferred Windows verification path

1. Obtain the current Desktop installer through NinjaTrader's official download/account flow. Record vendor version and installer provenance; preserve TLS and signature verification.
2. Install it on a supported Windows machine using its normal installation procedure. Check the installed version in the application and signed executable metadata.
3. Create/import the indicator source using the supported NinjaScript Editor workflow, or reference the genuine installed assemblies in a Windows MSBuild project. Use the installed Framework reference assemblies and the actual `NinjaTrader.Core.dll`, `NinjaTrader.Gui.dll`, `NinjaTrader.Custom.dll`, `SharpDX.dll`, and `SharpDX.Direct2D1.dll` as required by the implementation. Resolve exact paths from the actual installation, not assumptions.
4. Compile in NinjaScript Editor so its generated wrappers, source conventions, reference resolution, and import behavior are exercised. An external DLL build is supplementary.
5. Add the indicator to a chart with real Level II or a known Market Replay recording; verify callbacks and visual behavior, reset/reconnect, multiple chart instances, resizing, zooming, scrolling, template save/reload, and disposal.
6. Record compile output and runtime log. Capture screenshots at prescribed zoom levels and compare the intended visual specification; do not claim pixel identity from a standalone mockup.

A CI Windows build can be added if licensed installation/reference access is available. Proprietary assemblies should not be committed to the repository. A locally configurable path to an existing official installation is preferable. There is currently no verified standalone public NinjaTrader SDK package or Linux server API that provides the Desktop indicator host. An order-entry/account HTTP API would not validate chart rendering even if available.

Wine is not a substitute for supported Windows validation of WPF, Direct2D, feed connectivity, and NinjaScript lifecycle. Installing it would not resolve the present lack of an authenticated official installer or current platform documentation.

### Import packaging

The inspected third-party binary export uses an archive with `info.xml`, its own DLL, and a license file. This is evidence of one packaging pattern, not sufficient evidence for current source-import packaging. Prefer providing plain source with explicit placement instructions first, then create and validate the source ZIP through the actual NinjaTrader export/import flow. Do not invent export schema/version metadata or include proprietary vendor DLLs.

## Validation plan and exit criteria

| Layer | Meaningful checks | Present status |
|---|---|---|
| Current release | Official release notes and installed version agree | Vendor HTTP403/Cloudflare1010; mirror observes 8.1.8.2 but latest remains unverified |
| Portable engine | Book inserts/removes/resets, unknown quotes, volume conservation, threshold boundaries, history retention, zoom aggregation, replay determinism | Toolchain prepared; implementation intentionally deferred until research completes |
| Performance | High-rate event replay with bounded memory and measured snapshot/render cost | Not run |
| NinjaScript compile | Genuine current installed vendor assemblies plus editor compilation | No Windows host/official installation available |
| Runtime | Real/replay depth events, reset/reconnect behavior, lifecycle cleanup | Not run |
| Visual parity | Actual indicator screenshots and specified reference images at multiple zoom levels | Not run |
| Packaging | Clean-machine import and dependency resolution | Not run |

The current external blocker is vendor denial on official NinjaTrader research/download hosts after network allowlisting. Portable engine work and conservative NT8 adapter development can proceed using the corroborated API surface, while latest-release compatibility remains unverified. Final platform readiness additionally requires a supported Windows installation with market-depth or replay data. These are separate requirements: network access alone does not create a Windows validation host.

## User's planned feed and instruments

The user plans to begin with NinjaTrader's two-week trial feed and may use Rithmic later. Target futures are NQ, ES, GC, YM, and RTY. The baseline should use NinjaTrader's normal Level I/Level II callbacks and remain provider-independent; a provider-specific connection API or MBO entitlement is not a prerequisite for the baseline.

The trial's actual depth entitlement must be checked in the connected application. Do not assume that a trial includes every exchange, unlimited depth, historical Level II, Market Replay downloads, or the same depth on all five products. For each selected contract, record whether Last/Bid/Ask events and bid/ask depth updates arrive, how many levels are observed, the instrument's supplied tick size, and whether reset/reconnect rebuilds succeed. A lack of depth should produce a clear status message rather than an empty chart that appears broken.

Read tick size from NinjaTrader's instrument metadata, and keep prices in ticks internally. NQ, ES, GC, YM, and RTY have different liquidity/volume distributions; a single absolute quantity threshold is unsuitable as the only filter. Provide per-instrument presets or a configurable relative/adaptive threshold, while preserving explicit manual controls. Keep volume units in contracts and make any notional conversion explicit. Contract rollover is an instrument change; do not mix price-level histories across contracts.

When Rithmic is later selected, repeat the callback/entitlement/depth tests through NinjaTrader. Provider exchange permissions, depth limits, update aggregation, timestamps, and replay availability may differ. Do not promise that NinjaTrader's aggregated depth callback exposes Rithmic order IDs, queue position, or MBO even when the provider itself offers those data in another API. Trial or feed credentials belong in NinjaTrader's supported login workflow and are unnecessary for portable engine tests.


## Research update: public documentation mirror, 2026-10-23

An independently accessible public repository, [HalfTick/ninjatrader-docs](https://github.com/HalfTick/ninjatrader-docs/tree/8efc2529070841438006bd1abb854024e7e42119), contains a documentation copy at commit `8efc2529070841438006bd1abb854024e7e42119`. This is a third-party mirror, not an official publication. Its pages are secondary evidence that must be labeled as such; neither freshness nor unchanged text is guaranteed. The mirror's release index itself is stale while individual release pages are newer. No executable included in that repository was run or accepted as a vendor artifact.

Relevant observations and permalinks:

- [8.1.8 release page](https://github.com/HalfTick/ninjatrader-docs/blob/8efc2529070841438006bd1abb854024e7e42119/docs/release_notes/8_1_8.md) lists **8.1.8.0 — July 21, 2026; 8.1.8.1 — July 24, 2026; 8.1.8.2 — August 11, 2026**. It is accurate to call 8.1.8.2 the newest version observed in this mirror. It is not accurate to claim it is the latest currently available NinjaTrader release without checking the vendor.
- [8.1.6 release page](https://github.com/HalfTick/ninjatrader-docs/blob/8efc2529070841438006bd1abb854024e7e42119/docs/release_notes/8_1_6.md) lists Feature 30437: NinjaScript Editor **C# 13 support**. Earlier copied release pages describe C# 9 in 8.1.4 and Roslyn/C# 8 in 8.1.2. Compiler language support and runtime API availability are separate: modern syntax does not make .NET 8/9 BCL APIs available inside a Framework 4.8 host.
- [System requirements](https://github.com/HalfTick/ninjatrader-docs/blob/8efc2529070841438006bd1abb854024e7e42119/docs/getting_started/minimum_system_requirements.md) list 64-bit Windows 10/11 or Windows Server 2016+, **Microsoft .NET Framework 4.8**, and recommend DirectX 10 graphics. These are mirrored requirements, still requiring confirmation against the chosen current installer.
- [OnMarketDepth](https://github.com/HalfTick/ninjatrader-docs/blob/8efc2529070841438006bd1abb854024e7e42119/docs/language_reference/onmarketdepth.md) says depth changes are delivered in their correct sequence, are real-time, and do not occur in historical backtests. It explicitly says **there is no guaranteed ordering between OnMarketDepth and OnMarketData**. Preserve within-stream sequence and do not infer exact cancels versus fills from assumed cross-stream ordering.
- [MarketDepthEventArgs](https://github.com/HalfTick/ninjatrader-docs/blob/8efc2529070841438006bd1abb854024e7e42119/docs/language_reference/marketdeptheventargs.md) documents `Position` as a zero-based ladder position, `Volume` as `long`, and Add/Update/Remove operations. It labels `IsReset` as a UI reset after manual disconnect, relevant to columns. An indicator needs explicit connection-loss handling in addition to defensive reset-event handling.
- [OnMarketData](https://github.com/HalfTick/ninjatrader-docs/blob/8efc2529070841438006bd1abb854024e7e42119/docs/language_reference/onmarketdata.md) says Tick Replay replays **Last events and best inside bid/ask prices at each trade**, not historical bid/ask size events. It cannot supply historical market depth.
- [OnConnectionStatusUpdate](https://github.com/HalfTick/ninjatrader-docs/blob/8efc2529070841438006bd1abb854024e7e42119/docs/language_reference/onconnectionstatusupdate.md) explicitly documents the override for indicators. `PriceStatus` describes price-feed state; `Status` may describe the adapter's order system. Use the correct connection/instrument and avoid clearing a healthy instrument because an unrelated account connection changed.

### Current access diagnosis

Ordinary verified HTTPS requests to `ninjatrader.com`, `www.ninjatrader.com`, `developer.ninjatrader.com`, and `support.ninjatrader.com` returned HTTP403 with `error code: 1010`. Main help pages, developer documentation, support landing pages, and a public download entry point were checked. There was no successful official installer discovery/download. `account.ninjatrader.com` separately remained blocked at CONNECT, so account-gated download access has not been established. No credentials were requested or extracted, TLS verification was not disabled, and no security-control workaround was used.

An ordinary Chromium attempt exposed an additional certificate trust problem in the browser. Attempting to trust the already-installed environment proxy CA through NSS was approved, but it did not produce a reliably working browser session. This does not change the independently observed vendor HTTP403 from Python's verified HTTPS client.

### Cross-compilation nuance

Linux **could** compile the adapter's C# against .NET Framework reference assemblies plus genuine vendor assembly metadata without running NinjaTrader, if the official binaries become available through the user's installation or an authenticated official installer. `Microsoft.NETFramework.ReferenceAssemblies.net48` supplies Framework reference assemblies; a C#-only project can explicitly reference the needed Windows/WPF assemblies without claiming a Linux WPF runtime. Avoid WPF XAML-generation build steps if there is no XAML. A Windows installer archive might be extractable with a standard MSI tool, but no current official installer was acquired, so this route is prospective rather than verified.

Even a successful cross-compile would validate type/API linkage only. It would not validate NinjaScript-generated wrappers, editor/import behavior, actual market-data callback ordering, SharpDX resources, chart drawing, or feed entitlements. Those still require genuine Windows/NinjaTrader runtime checks.

Additional mirrored implementation references:

- [OnRender](https://github.com/HalfTick/ninjatrader-docs/blob/8efc2529070841438006bd1abb854024e7e42119/docs/language_reference/onrender.md) documents a render cadence buffered to at least 250 ms, custom SharpDX drawing, and the need for absolute series indices rather than assuming `barsAgo` is synchronized with callbacks. Do not promise a higher frame rate by calling `ForceRefresh` for every market event. Call `base.OnRender` if standard plots are also needed.
- [OnRenderTargetChanged](https://github.com/HalfTick/ninjatrader-docs/blob/8efc2529070841438006bd1abb854024e7e42119/docs/language_reference/onrendertargetchanged.md) warns that resize and hit testing can use different render targets. Brushes must belong to the current target or Direct2D reports `D2DERR_WRONG_RESOURCE_DOMAIN`. Its sample accesses panel bounds as `ChartPanel.X`, `.Y`, `.W`, `.H` and creates `SharpDX.RectangleF` from those values.
- [Installation guide](https://github.com/HalfTick/ninjatrader-docs/blob/8efc2529070841438006bd1abb854024e7e42119/docs/getting_started/installation_guide.md) directs users to log into `account.ninjatrader.com`, choose Download in the account menu, and install the platform. This is the discovered supported acquisition route; it does not provide a public unauthenticated installer URL.
