# Installing and checking FootprintMap in NinjaTrader Desktop

FootprintMap is an original C# NinjaScript indicator with a Bookmap-style heatmap, execution bubbles, current order book, volume profiles and delta overlays. It requires a Windows installation of NinjaTrader Desktop 8 and actual Level II data for the selected contract.

**Current validation boundary:** the portable C# engine and replay workflow have been tested in the cloud. The NinjaTrader adapter has not yet been compiled against a genuine current NinjaTrader installation or run in its chart host. Treat the first Windows installation as an integration test. A browser replay image is not a NinjaTrader screenshot.

Official NinjaTrader release/download pages were inaccessible from the research environment. A secondary documentation mirror lists 8.1.8.2, dated August 11, 2026; that does not establish the latest release. Obtain the current supported Desktop release through your existing NinjaTrader account and record its version from the application when validating. The adapter uses conservative C# 7.3 syntax and established NT8 APIs; the portable test runner's .NET 8 runtime is separate from NinjaTrader's .NET Framework host.

## 1. Prepare the Windows installation

1. Open NinjaTrader once and confirm its ordinary chart and data connection work, then close NinjaTrader before copying source files.
2. Locate the actual NinjaTrader user-data folder. It is normally **Documents\NinjaTrader 8**, but Windows or OneDrive can redirect Documents. Use the folder containing your existing `bin\Custom` directory.
3. Preserve any existing files named `FootprintMap.cs` or an existing `FootprintMapCore` folder before replacing them. Keep backups **outside** `bin\Custom`: duplicate `.cs` files inside Custom can create duplicate-type compilation errors.
4. Extract the project's source package to an ordinary local folder. Do not place the entire repository or generated build directories in NinjaTrader's Custom folder.

NinjaTrader's trial feed may have different exchange/depth entitlements from a later Rithmic connection. Verify the exact NQ, ES, GC, YM or RTY contract you select. A moving last price alone does not establish Level II availability.

## 2. Copy the source files

The source layout in your NinjaTrader user-data folder should be:

```text
NinjaTrader 8\
  bin\Custom\Indicators\
    FootprintMap.cs
    FootprintMapCore\
      Model.cs
      MarketEngine.cs
      Visualization.cs
      LiquidityNoiseFilter.cs
```

Copy **all `.cs` files** from `src\Footprint.Core` into `bin\Custom\Indicators\FootprintMapCore`, including any additional helper source files supplied in the package. Copy `src\NinjaTrader\FootprintMap.cs` into `bin\Custom\Indicators`.

Do not copy `.csproj`, `bin`, `obj`, tests, browser preview files, or vendor DLLs into Custom. The engine source and indicator must compile together in NinjaTrader's Custom assembly; the source installer does not require a separately deployed `Footprint.Core.dll`.

An optional PowerShell helper is included:

```powershell
# Run from the extracted project directory, with NinjaTrader closed.
.\scripts\install-ninjatrader.ps1
```

Read the script's parameters if your user-data folder is redirected. It refuses to overwrite existing project files by default. Its explicit replacement option creates a backup outside Custom before replacement. The helper has not been executed on Windows in this cloud session; manual copying remains available. Follow your Windows policy for locally reviewed scripts rather than weakening system-wide execution policy.

## 3. Compile in NinjaTrader

1. Reopen NinjaTrader.
2. Open **New → NinjaScript Editor** from Control Center.
3. Confirm the copied source files are visible. Compile from the editor, normally with **F5**.
4. Resolve every compiler error before adding the indicator. Keep the full error text, file name and line number if reporting a problem. NinjaTrader compiles other Custom scripts too, so distinguish pre-existing errors from errors in these files.

Do not infer successful platform validation merely because the portable `Footprint.Core` project builds. NinjaScript compilation also exercises NinjaTrader's own base classes, generated wrappers, references and source conventions.

For an additional external build against your **genuine installed assemblies**, the repository includes `scripts/validate-ninjatrader.ps1` and a validation project. Supply the actual installation and Custom paths requested by that script. This requires the corresponding Windows build tooling. It supplements the NinjaScript Editor check and does not replace chart runtime testing. Proprietary NinjaTrader binaries are not supplied with this project.

This optional external check uses the .NET SDK pinned in `global.json` (8.0.426); install that SDK on Windows if using the script from the project directory. The ordinary NinjaScript Editor installation does not require this separate SDK. For example, replacing the path with your actual installation:

```powershell
.\scripts\validate-ninjatrader.ps1 -NinjaTraderBin 'C:\Program Files\NinjaTrader 8\bin'
```

## 4. Add the indicator to a supported chart

1. Connect the intended data feed and choose one specific futures contract.
2. Open a **minute or second chart**, with at least two visible bars. These are the supported chart modes for this version. Tick, volume, range, Renko and other non-time charts are not presented as equivalent to Bookmap's continuous time axis.
3. Add **FootprintMap** using the chart's Indicators dialog.
4. Reserve sufficient right margin for the enabled profile columns: approximately **number of columns × Profile column width + 30 pixels**. The default COB and session-profile columns are 100 pixels each, so start with at least **230 pixels**. A warning appears if the columns cover the recorded live edge.
5. Let the market feed run. The status should progress from waiting for Level II to recorded depth, with bid/ask level counts and an increasing execution count.

The indicator reads the instrument's tick size, including ES/NQ 0.25, GC/RTY 0.1 and YM 1. It does not assume the same liquidity quantity threshold is appropriate for every contract. Begin with the default relative contrast, then inspect the raw view before adjusting minimum quantities.

The main chart uses the native price scale. Expand that scale when you want to see liquidity farther from the market. Opaque background and high indicator z-order provide the heatmap appearance without changing the saved candle style. Toggle those options when you want native bars visible as well.

## 5. Understand the main settings

| Setting | Meaning |
| --- | --- |
| Heat frame duration | Duration-weighted resting quantity per time bucket; smaller buckets require more processing and history storage. Default 250 ms. |
| Filter by level persistence | Uses quantity continuously observed at a fixed price. Newly added quantity starts young. This is aggregate-level persistence, not the age of identifiable orders. |
| Level persistence | Required persistence window, default 1,250 ms. Turning the filter off exposes the raw recorded book. |
| Suppress price-following liquidity | Optional heuristic for repeated large quantities at similar distances from a moving best quote. Default off. It can remove ordinary liquidity and is not TraderMap Pro's proprietary Bot Remover. |
| Following observation window / moves / size ratio / tolerance | Controls the evidence needed by that heuristic. Test the effect against raw depth; price-following behavior does not establish intent or individual order identity. |
| Minimum displayed level quantity / Keep top liquidity percent | Selects aggregate heat cells, not individual MBO orders. |
| Automatic contrast / Contrast percentile | Sets intensity from visible selected quantities. Panning or filtering can change the color reference. Use Manual heat maximum for a stable numerical reference. |
| Classic palette | Dark/blue/pale/yellow/orange/red intensity family; the hottest end is red. Exact RGB values and contrast adaptation are original approximations. |
| Minimum individual execution size | Filters original prints before drawing bubbles. It does not remove those executions from profile/CVD totals. |
| Minimum displayed cluster volume | Filters after bubble aggregation. Many small trades can create a qualifying cluster. |
| Bubble clustering | Pixel, Smart, Time, Volume, Price, or PriceAndSide. Smart uses an original pixel-grid approximation. Volume mode preserves the entire closing trade and may exceed its threshold. |
| Delta bubbles | Sizes by the absolute buy-minus-sell difference. Equal buy/sell volume can produce zero delta despite substantial trading. |
| Bubble size / opacity / color mode | Area-based size approximation, radius cap, Gradient/Split/Pie color, optional 3D highlight, outline and quantity labels. Unknown aggressor volume is neutral. |
| COB | Raw currently observed bid/ask quantities. Heatmap filtering does not change the actual displayed order-book column. |
| SVP / CVP | Accepted executed volume since capture/session reset, or within the retained visible range. Neither includes resting depth. |
| Session reset | Uses the chart Trading Hours template and market-event timestamps. It resets profile/CVD before the first accepted execution in the new observed session. Actual feed capture continues outside the chart session; those executions remain in the previous observed profile until the next session begins. A chart calendar boundary does not pretend that the data connection disconnected. |
| Data connection name | Blank conservatively responds to any price-feed disconnect. Set the exact configured name when unrelated data connections would otherwise create unnecessary gaps. |
| History/count budgets | Cap heat frames and trade retention. Session totals remain separate; old dots and the visible profile cannot recover trades discarded by the retention cap. |

The adapter interpolates between native time-bar anchors to preserve sub-bar event positions while respecting chart scroll and zoom. It does not claim that NinjaTrader's chart layout, session gaps or proprietary Bookmap clustering are identical.

## 6. Validate actual feed behavior

Perform these checks with a live feed or a known **Market Replay recording containing Level II**:

1. Compare the COB quantities against NinjaTrader's Level II window for the same contract.
2. Switch raw/persistent/following-filter views and confirm that the execution count and profile/CVD accounting stay unchanged.
3. Zoom in and out. Bubble grouping should change while recorded executions remain accounted for; a bubble can disappear when its displayed-cluster threshold no longer qualifies at the new zoom.
4. Compare total-volume and delta bubbles on mixed buy/sell trading. They intentionally represent different quantities.
5. Disconnect the price feed and reconnect. Depth should clear, the outage should remain a historical gap, and new depth should rebuild without extending old liquidity across the outage.
6. Resize the chart, change the visible price range, scroll history, save/reload the workspace, and remove/re-add the indicator. Check the NinjaScript Output and Log windows for errors.
7. Check a trading-hours boundary and a backward Market Replay seek. The configured clock-rewind threshold resets recorded state to avoid carrying future data into the earlier replay.
8. Repeat the basic depth/price/volume checks for the other target contracts and after changing providers.

Record the NinjaTrader version, provider, contract, Trading Hours template, chart period, settings and any errors with the validation result. Actual Windows rendering, provider compatibility and NinjaScript import remain unverified until these checks are completed.

## Missing history and troubleshooting

- **Waiting for Level II:** confirm depth entitlement and the selected contract in a native Level II window. A Level I-only connection cannot populate the heatmap.
- **No earlier heatmap:** depth capture begins when this indicator receives data. Historical candles and Tick Replay do not recreate historical Level II. The current implementation retains data in memory and starts fresh after removing/reloading the indicator.
- **No recent depth updates:** this can mean an inactive market or paused replay. The message alone is not a disconnect diagnosis; no new heat is fabricated from wall-clock time.
- **Profiles cover the live edge:** increase the chart right margin or disable a profile column.
- **Unsupported chart message:** use a minute/second chart with at least two visible time bars.
- **Unexpected gaps with multiple connections:** set Data connection name to the intended price feed's exact name.
- **Blank or failed rendering:** check NinjaScript Output for the `FootprintMap rendering:` error. Do not treat a blank chart as a passing runtime test.
- **Capacity warnings:** inspect invalid-event and trimmed-history counts; increase the appropriate budget only when measured resource use permits it.
- **Session calendar unavailable:** feed capture continues to preserve the positional order book; profile/CVD resets wait for a successful calendar lookup. Check the chart Trading Hours template. Failed lookups are retried at most once per minute of market-event time.

This indicator does not implement exchange MBO order IDs, broker IDs, genuine native iceberg/stop identification, Bookmap's entire add-on ecosystem, or its proprietary filter formulas. The research dossier identifies those distinctions explicitly.
