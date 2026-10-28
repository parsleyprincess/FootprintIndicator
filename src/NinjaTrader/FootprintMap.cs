#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using System.Threading;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using FootprintIndicator.Core;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
using DxBrush = SharpDX.Direct2D1.SolidColorBrush;
using Viewport = FootprintIndicator.Core.Viewport;
#endregion

// Original implementation. No Bookmap or NinjaTrader proprietary binaries are included.
// Copy alongside all src/Footprint.Core/*.cs files; see docs/INSTALL-NINJATRADER.md.
namespace NinjaTrader.NinjaScript.Indicators
{
    public class FootprintMap : Indicator
    {
        private readonly object engineGate = new object();
        private readonly object renderGate = new object();
        private readonly object historyGate = new object();
        private readonly Dictionary<uint, DxBrush> brushes = new Dictionary<uint, DxBrush>();
        private MarketEngine engine;
        private EngineSnapshot cachedSnapshot;
        private RenderScene cachedScene;
        private Viewport cachedViewport;
        private long snapshotClock, refreshClock, lastEventTicks, lastDepthClock, lastTradeClock;
        private volatile bool terminated;
        private bool dataConnected = true, sessionActive;
        private SessionIterator sessionIterator;
        private long sessionBeginTicks, sessionEndTicks, sessionLookupRetryTicks, engineGeneration, sceneGeneration = -1;
        private long[] timeAnchorTicks = new long[0];
        private float[] timeAnchorX = new float[0];
        private DxBrush overflowBrush;
        private bool profileMarginWarning;
        private string connectionNote = "Waiting for instrument data";
        private string sessionWarning;
        private string renderError;
        private DispatcherTimer repaintTimer;
        private TextFormat textFormat, smallFormat;
        private RadialGradientBrush sphereHighlight;
        private GradientStopCollection sphereStops;
        private double instrumentTickSize;
        private double latestBid = double.NaN, latestAsk = double.NaN;
        private long restoredSessionStart;
        private string historyPath, historyKey, historyNote;
        private System.Threading.Timer historyTimer;
        private ChartControl inputChart;
        private double zoomSeconds, scrollSeconds;
        private ProfileResult cachedStudies;
        private OrderFlowResult cachedFlow;
        private string studyError;
        private long flowClock, flowGeneration = -1;

        public override string DisplayName { get { return "FootprintMap"; } }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Recorded Level II liquidity heatmap, executed-volume bubbles, order book and profiles. Requires Level II; no backfilled historical depth.";
                Name = "FootprintMap";
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                DisplayInDataBox = false;
                DrawOnPricePanel = true;
                IsAutoScale = true;
                PaintPriceMarkers = false;
                IsSuspendedWhileInactive = false;
                BarsRequiredToPlot = 0;
                SetDefaults();
            }
            else if (State == State.DataLoaded)
            {
                lock (engineGate)
                {
                    instrumentTickSize = Instrument.MasterInstrument.TickSize;
                    engine = NewEngine();
                    engineGeneration++;
                    sessionIterator = Bars == null ? null : new SessionIterator(Bars);
                    sessionBeginTicks = sessionEndTicks = sessionLookupRetryTicks = 0;
                    sessionWarning = null;
                    sessionActive = false;
                    terminated = false;
                    lastEventTicks = 0;
                    lastDepthClock = lastTradeClock = 0;
                    cachedSnapshot = null;
                    zoomSeconds = VisibleSeconds;
                    scrollSeconds = HistoryOffsetSeconds;
                    latestBid = latestAsk = double.NaN;
                    dataConnected = true;
                    RestoreCapture();
                }
                if (SaveCapturedHistory)
                    historyTimer = new System.Threading.Timer(SaveCaptureTick, null, 30000, 30000);
            }
            else if (State == State.Historical)
            {
                SetZOrder(CoverPriceBars ? int.MaxValue : -1);
                StartRepaintTimer();
            }
            else if (State == State.Terminated)
            {
                if (historyTimer != null) { historyTimer.Dispose(); historyTimer = null; }
                EngineSnapshot finalSnapshot;
                lock (engineGate) { terminated = true; finalSnapshot = engine == null ? null : engine.Snapshot(); engine = null; cachedSnapshot = null; }
                if (SaveCapturedHistory && finalSnapshot != null) WriteCapture(finalSnapshot);
                StopRepaintTimer();
                lock (renderGate) { cachedScene = null; cachedViewport = null; DisposeRenderResources(); }
            }
        }

        private MarketEngine NewEngine()
        {
            return new MarketEngine(new EngineSettings
            {
                TickSize = instrumentTickSize,
                FrameMilliseconds = FrameMilliseconds,
                PersistenceMilliseconds = PersistenceMilliseconds,
                RetentionMinutes = RetentionMinutes,
                MaxFrames = MaximumFrames,
                MaxTrades = MaximumTrades,
                MaxDepthLevels = MaximumDepthLevels,
                MaxProfileLevels = MaximumProfileLevels
            });
        }

        private void RestoreCapture()
        {
            restoredSessionStart = 0;
            if (!SaveCapturedHistory) { historyNote = "Capture is not saved"; return; }
            try
            {
                historyKey = Instrument.FullName + "|" + instrumentTickSize.ToString("R", CultureInfo.InvariantCulture) + "|" +
                    (Bars == null || Bars.TradingHours == null ? "" : Bars.TradingHours.Name + "|" + Bars.TradingHours.TimeZoneInfo.Id) + "|" +
                    FrameMilliseconds + "|" + PersistenceMilliseconds;
                historyPath = HistoryStore.PathFor(Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "FootprintMapHistory"), historyKey);
                if (!File.Exists(historyPath)) { historyNote = "Capture autosave enabled"; return; }
                EngineSnapshot saved = HistoryStore.Read(historyPath, historyKey, instrumentTickSize);
                engine.RestoreHistory(saved);
                lastEventTicks = saved.TimeTicks;
                restoredSessionStart = saved.SessionStartTicks;
                dataConnected = false;
                historyNote = "Restored captured history; fresh depth required";
            }
            catch (Exception ex) { historyNote = "History restore failed: " + ex.Message; Print("FootprintMap " + historyNote); }
        }

        private void SaveCaptureTick(object state)
        {
            // Serialize snapshots and disk IO on a worker, never in a market callback.
            lock (historyGate)
            {
                EngineSnapshot snapshot;
                lock (engineGate) { if (terminated || engine == null) return; snapshot = engine.Snapshot(); }
                WriteCapture(snapshot);
            }
        }

        private void WriteCapture(EngineSnapshot snapshot)
        {
            if (snapshot.TimeTicks <= 0 || string.IsNullOrEmpty(historyPath)) return;
            lock (historyGate)
            {
                try
                {
                    bool written = HistoryStore.Write(historyPath, historyKey, snapshot);
                    historyNote = written ? "Saved " + new DateTime(snapshot.TimeTicks).ToString("HH:mm:ss", CultureInfo.InvariantCulture) : "Newer saved capture retained";
                }
                catch (Exception ex) { historyNote = "History save failed: " + ex.Message; }
            }
        }

        // Time is NinjaTrader's chart/display time throughout. Do not convert only one stream to UTC.
        private bool PrepareEvent(DateTime time)
        {
            if (engine == null || terminated || time == DateTime.MinValue) return false;
            long ticks = time.Ticks;
            if (lastEventTicks > 0 && ticks < lastEventTicks - TimeSpan.TicksPerSecond * ReplayRewindSeconds)
            {
                // Playback seeking must not retain profile/CVD/trades from the abandoned future.
                engine = NewEngine();
                engineGeneration++;
                cachedSnapshot = null;
                lastEventTicks = 0;
                restoredSessionStart = 0;
                latestBid = latestAsk = double.NaN;
                dataConnected = true;
                sessionIterator = Bars == null ? null : new SessionIterator(Bars);
                sessionBeginTicks = sessionEndTicks = sessionLookupRetryTicks = 0;
                sessionWarning = null;
                sessionActive = false;
                connectionNote = "Event clock rewound: recording restarted";
            }
            if (!dataConnected && ticks >= lastEventTicks)
            {
                engine.SetConnected(time, true);
                dataConnected = true;
                connectionNote = "Instrument data resumed; rebuilding depth";
            }
            if (ResetAtSession && ticks >= lastEventTicks && !PrepareSession(time)) return false;
            lastEventTicks = Math.Max(lastEventTicks, ticks);
            return true;
        }

        protected override void OnMarketDepth(MarketDepthEventArgs e)
        {
            if (State != State.Realtime || e == null) return;
            DepthLevel[] bids = null, asks = null;
            // NinjaTrader owns the authoritative ladder. Attaching mid-stream need not
            // deliver Add(0), Add(1)... before Update(n), so do not reconstruct positions.
            // Release its lock before taking ours, including on reset callbacks.
            if (!e.IsReset && e.Instrument != null)
            {
                var bidRows = new SortedDictionary<long, long>();
                var askRows = new SortedDictionary<long, long>();
                lock (e.Instrument.SyncMarketDepth)
                {
                    foreach (var row in e.Instrument.MarketDepth.Bids) AddDepthRow(bidRows, row.Price, row.Volume);
                    foreach (var row in e.Instrument.MarketDepth.Asks) AddDepthRow(askRows, row.Price, row.Volume);
                }
                bids = DepthRows(bidRows, true);
                asks = DepthRows(askRows, false);
            }
            lock (engineGate)
            {
                if (engine == null || terminated) return;
                if (e.IsReset)
                {
                    long resetTicks = Math.Max(lastEventTicks, e.Time.Ticks);
                    engine.ResetDepth(new DateTime(resetTicks), "Instrument depth reset; waiting for fresh depth");
                    lastDepthClock = 0;
                    latestBid = latestAsk = double.NaN;
                    cachedSnapshot = null;
                    return;
                }
                if (!PrepareEvent(e.Time)) return;
                if (bids != null && asks != null)
                {
                    if (engine.DepthSnapshot(e.Time, bids, asks)) lastDepthClock = Stopwatch.GetTimestamp();
                }
            }
            RequestRepaint();
        }

        private void AddDepthRow(SortedDictionary<long, long> rows, double price, long volume)
        {
            if (volume <= 0 || double.IsNaN(price) || double.IsInfinity(price) || instrumentTickSize <= 0) return;
            double ticks = price / instrumentTickSize;
            if (ticks <= long.MinValue || ticks >= long.MaxValue) return;
            long tick = (long)Math.Round(ticks, MidpointRounding.AwayFromZero), total;
            rows.TryGetValue(tick, out total);
            rows[tick] = volume > long.MaxValue - total ? long.MaxValue : total + volume;
        }

        private DepthLevel[] DepthRows(SortedDictionary<long, long> rows, bool descending)
        {
            var result = new List<DepthLevel>(rows.Count);
            foreach (KeyValuePair<long, long> row in rows) result.Add(new DepthLevel(row.Key, row.Value));
            if (descending) result.Reverse();
            if (result.Count > MaximumDepthLevels) result.RemoveRange(MaximumDepthLevels, result.Count - MaximumDepthLevels);
            return result.ToArray();
        }

        protected override void OnMarketData(MarketDataEventArgs e)
        {
            // Tick Replay is not historical Level II. Begin capture only in real-time/Market Replay.
            if (State != State.Realtime || e == null) return;
            lock (engineGate)
            {
                if (engine == null || terminated) return;
                if (e.IsReset)
                {
                    long resetTicks = Math.Max(lastEventTicks, e.Time.Ticks);
                    engine.ResetDepth(new DateTime(resetTicks), "Instrument market-data reset");
                    lastDepthClock = 0;
                    latestBid = latestAsk = double.NaN;
                    cachedSnapshot = null;
                    return;
                }
                if (!PrepareEvent(e.Time)) return;
                if (e.MarketDataType == MarketDataType.Last)
                {
                    engine.Trade(e.Time, e.Price, e.Volume, e.Bid, e.Ask);
                    latestBid = QuotePrice(e.Bid);
                    latestAsk = QuotePrice(e.Ask);
                    lastTradeClock = Stopwatch.GetTimestamp();
                }
                else if (e.MarketDataType == MarketDataType.Bid || e.MarketDataType == MarketDataType.Ask)
                {
                    if (e.MarketDataType == MarketDataType.Bid) latestBid = QuotePrice(e.Price);
                    else latestAsk = QuotePrice(e.Price);
                    engine.Advance(e.Time);
                }
            }
            RequestRepaint();
        }

        // Session detection runs on market timestamps, before the first execution is counted.
        // OnBarUpdate/OnMarketData ordering cannot erase the first trade of a new session.
        protected override void OnBarUpdate() { }

        private static double QuotePrice(double value)
        {
            // NinjaTrader uses zero for an unavailable quote; negative futures
            // prices are valid and must not leave an older positive quote on-screen.
            return value == 0 || double.IsNaN(value) || double.IsInfinity(value) ? double.NaN : value;
        }

        public override void OnCalculateMinMax()
        {
            MinValue = double.MaxValue;
            MaxValue = double.MinValue;
            if (terminated || !ShowCurrentPrice || instrumentTickSize <= 0) return;
            EngineSnapshot snapshot;
            double bid, ask;
            lock (renderGate)
            {
                // A historical inspection must not pull its scale toward today's
                // price. Manual fixed scaling remains under the chart's control.
                if (ContinuousTimeView && scrollSeconds > 0) return;
                lock (engineGate)
                {
                    if (engine == null) return;
                    snapshot = SnapshotLocked(Stopwatch.GetTimestamp());
                    bid = latestBid; ask = latestAsk;
                }
            }
            double minimum = double.MaxValue, maximum = double.MinValue;
            if (snapshot.Connected)
            {
                if (snapshot.Bids.Length > 0) bid = snapshot.Bids[0].Tick * instrumentTickSize;
                if (snapshot.Asks.Length > 0) ask = snapshot.Asks[0].Tick * instrumentTickSize;
                IncludePrice(bid, ref minimum, ref maximum);
                IncludePrice(ask, ref minimum, ref maximum);
            }
            if (snapshot.Trades.Length > 0)
                IncludePrice(snapshot.Trades[snapshot.Trades.Length - 1].PriceTick * instrumentTickSize, ref minimum, ref maximum);
            if (minimum == double.MaxValue) return;
            double padding = Math.Max(instrumentTickSize * 4, (maximum - minimum) * 0.1);
            MinValue = minimum - padding;
            MaxValue = maximum + padding;
        }

        private static void IncludePrice(double value, ref double minimum, ref double maximum)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return;
            minimum = Math.Min(minimum, value);
            maximum = Math.Max(maximum, value);
        }

        // engineGate protects both the engine and this detached render cache.
        private EngineSnapshot SnapshotLocked(long now)
        {
            if (cachedSnapshot == null || (now - snapshotClock) * 1000.0 / Stopwatch.Frequency >= RefreshMilliseconds)
            {
                cachedSnapshot = engine.Snapshot();
                snapshotClock = now;
            }
            return cachedSnapshot;
        }

        private bool PrepareSession(DateTime time)
        {
            if (sessionIterator == null) return true;
            if (sessionEndTicks > 0 && time.Ticks >= sessionBeginTicks && time.Ticks < sessionEndTicks)
            {
                if (!sessionActive) { engine.StartSession(time); sessionActive = true; }
                return true;
            }
            // Trading Hours controls counter resets, not feed subscription. Continue
            // reconstructing actual events outside the template: the provider need not
            // resend a full positional snapshot just because a chart session has ended.
            // Cache the next session instead of repeating expensive calendar lookups.
            if (sessionEndTicks > 0 && time.Ticks < sessionBeginTicks)
            {
                connectionNote = "Capturing outside chart session; counters reset at next session";
                return true;
            }
            sessionActive = false;
            if (time.Ticks < sessionLookupRetryTicks) return true;
            if (!sessionIterator.GetNextSession(time, false))
            {
                // Calendar failure cannot discard a positional book update: a later
                // update may depend on this one. Retry slowly while capture continues.
                sessionWarning = "Session calendar unavailable; capture continues without a counter reset";
                sessionLookupRetryTicks = time.Ticks + Math.Min(TimeSpan.TicksPerMinute, DateTime.MaxValue.Ticks - time.Ticks);
                return true;
            }
            sessionLookupRetryTicks = 0;
            sessionWarning = null;
            long begin = sessionIterator.ActualSessionBegin.Ticks;
            long end = sessionIterator.ActualSessionEnd.Ticks;
            sessionBeginTicks = begin;
            sessionEndTicks = end;
            if (time.Ticks < begin || time.Ticks >= end)
            {
                connectionNote = "Capturing outside chart session; counters reset at next session";
                return true;
            }
            // Restoring within the same chart Trading Hours session must retain
            // the saved profile/CVD. A first event in another session resets them.
            if (restoredSessionStart < begin || restoredSessionStart >= end) engine.StartSession(time);
            restoredSessionStart = 0;
            sessionActive = true;
            return true;
        }

        protected override void OnConnectionStatusUpdate(ConnectionStatusEventArgs e)
        {
            if (e == null || e.Connection == null || State != State.Realtime) return;
            if (!string.IsNullOrWhiteSpace(DataConnectionName) &&
                !string.Equals(DataConnectionName.Trim(), e.Connection.Options.Name, StringComparison.OrdinalIgnoreCase)) return;
            // Order-system status is deliberately ignored. Blank name watches all price connections conservatively.
            if (e.PriceStatus == e.PreviousPriceStatus) return;
            lock (engineGate)
            {
                if (engine == null || terminated) return;
                DateTime eventTime = new DateTime(Math.Max(DateTime.MinValue.Ticks, lastEventTicks));
                if (e.PriceStatus != ConnectionStatus.Connected)
                {
                    engine.SetConnected(eventTime, false);
                    dataConnected = false;
                    lastDepthClock = 0;
                    latestBid = latestAsk = double.NaN;
                    connectionNote = "Price feed lost: " + e.Connection.Options.Name;
                }
                else
                {
                    // A global connection notification alone does not prove this instrument is receiving data.
                    connectionNote = "Price connection available; waiting for instrument events";
                }
                cachedSnapshot = null;
            }
            RequestRepaint();
        }

        private void StartRepaintTimer()
        {
            ChartControl chart = ChartControl;
            if (chart == null) return;
            chart.Dispatcher.InvokeAsync(() =>
            {
                lock (renderGate)
                {
                    if (terminated || repaintTimer != null) return;
                    repaintTimer = new DispatcherTimer(DispatcherPriority.Background, chart.Dispatcher);
                    repaintTimer.Interval = TimeSpan.FromMilliseconds(250);
                    repaintTimer.Tick += RepaintTick;
                    repaintTimer.Start();
                    inputChart = chart;
                    inputChart.PreviewMouseWheel += ChartMouseWheel;
                }
            });
        }

        private void RepaintTick(object sender, EventArgs e)
        {
            // UI refresh only. Wall time must never extend recorded heat during a playback pause or outage.
            if (!terminated) ForceRefresh();
        }

        private void StopRepaintTimer()
        {
            DispatcherTimer timer;
            ChartControl chart;
            lock (renderGate)
            {
                timer = repaintTimer; repaintTimer = null;
                chart = inputChart; inputChart = null;
            }
            if (timer == null) return;
            Action stop = () =>
            {
                timer.Stop(); timer.Tick -= RepaintTick;
                if (chart != null) chart.PreviewMouseWheel -= ChartMouseWheel;
            };
            if (timer.Dispatcher.CheckAccess()) stop();
            else timer.Dispatcher.InvokeAsync(stop);
        }

        private void ChartMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!ContinuousTimeView || terminated || ChartPanel == null || e.Handled || e.Delta == 0) return;
            System.Windows.Point position = e.GetPosition(ChartPanel);
            if (position.X < 0 || position.Y < 0 || position.X >= ChartPanel.ActualWidth || position.Y >= ChartPanel.ActualHeight) return;
            ModifierKeys keys = Keyboard.Modifiers;
            lock (renderGate)
            {
                if ((keys & ModifierKeys.Control) != 0)
                    zoomSeconds = Math.Max(5, Math.Min(86400, zoomSeconds * (e.Delta > 0 ? 0.8 : 1.25)));
                else if ((keys & ModifierKeys.Shift) != 0)
                    scrollSeconds = Math.Max(0, Math.Min(RetentionMinutes * 60, scrollSeconds + (e.Delta > 0 ? 1 : -1) * zoomSeconds / 4));
                else return;
                cachedScene = null;
            }
            e.Handled = true;
            ForceRefresh();
        }

        private void RequestRepaint()
        {
            long now = Stopwatch.GetTimestamp();
            long previous = Interlocked.Read(ref refreshClock);
            if ((now - previous) * 1000.0 / Stopwatch.Frequency < RefreshMilliseconds) return;
            if (Interlocked.CompareExchange(ref refreshClock, now, previous) != previous) return;
            if (!terminated) ForceRefresh();
        }

        private DisplaySettings DisplayOptions()
        {
            return new DisplaySettings
            {
                UsePersistence = FilterPersistentLiquidity,
                SuppressFollowingLiquidity = SuppressFollowingLiquidity,
                FollowingMinimumMoves = FollowingMinimumMoves,
                FollowingWindowMilliseconds = FollowingWindowMilliseconds,
                FollowingSizeRatio = FollowingSizeRatio,
                FollowingSizeTolerance = FollowingSizeTolerance,
                MinimumLiquidity = MinimumLiquidity,
                TopLiquidityPercent = TopLiquidityPercent,
                AutomaticContrast = AutomaticContrast,
                ContrastPercentile = ContrastPercentile,
                ManualMaximum = ManualHeatMaximum,
                Gamma = HeatGamma,
                Palette = Palette,
                HeatColumnPixels = HeatColumnPixels,
                Clustering = Clustering,
                ClusterMilliseconds = ClusterMilliseconds,
                ClusterVolume = ClusterVolume,
                SmartPixels = SmartClusterPixels,
                MinimumTradeSize = MinimumTradeSize,
                MinimumBubbleVolume = MinimumBubbleVolume,
                DeltaBubbles = DeltaBubbles,
                BubbleScale = BubbleScale,
                MaxBubbleRadius = MaximumBubbleRadius
            };
        }

        private ProfileSettings StudyOptions()
        {
            return new ProfileSettings
            {
                Scope = StudyScope, ValueAreaPercent = ValueAreaPercent,
                BandStandardDeviations = VwapBandDeviations, BookLevels = ImbalanceBookLevels,
                BookDistanceDecay = BookDistanceDecay, LargeLiquidityMinimum = LargeLiquidityMinimum,
                BuildFootprint = ShowFootprint, FootprintMilliseconds = FootprintSeconds * 1000,
                FootprintPriceGroupTicks = FootprintGroupTicks, DiagonalImbalanceRatio = FootprintImbalanceRatio,
                DiagonalMinimumVolume = FootprintMinimumVolume, BuildTpo = ShowTpoProfile,
                TpoMinutes = TpoBracketMinutes, TpoPriceGroupTicks = TpoGroupTicks
            };
        }

        private bool FlowStudiesEnabled
        {
            get { return ShowAbsorptionMarkers || ShowSweepMarkers || ShowLargePrintMarkers || ShowLiquidityChanges || ShowReplenishmentCandidates; }
        }

        private OrderFlowSettings FlowOptions()
        {
            return new OrderFlowSettings
            {
                EnableAbsorption = ShowAbsorptionMarkers, EnableSweeps = ShowSweepMarkers,
                EnableLargePrints = ShowLargePrintMarkers, EnableLiquidityChanges = ShowLiquidityChanges,
                EnableReplenishment = ShowReplenishmentCandidates,
                AbsorptionVolume = AbsorptionMinimumVolume, AbsorptionWindowMilliseconds = AbsorptionWindowMilliseconds,
                SweepVolume = SweepMinimumVolume, SweepWindowMilliseconds = SweepWindowMilliseconds,
                SweepMinimumLevels = SweepMinimumLevels, LargePrintVolume = LargePrintMinimumVolume,
                LiquidityChangeQuantity = LiquidityChangeMinimum, ReplenishmentVolume = ReplenishmentMinimumVolume,
                ReplenishmentMinimumFraction = ReplenishmentMinimumFraction, MaxMarkers = MaximumFlowMarkers,
                MaxTrades = MaximumTrades, MaxFrames = MaximumFrames, MaxCells = 12000000
            };
        }

        public override void OnRenderTargetChanged()
        {
            lock (renderGate) DisposeRenderResources();
        }

        private void DisposeRenderResources()
        {
            foreach (DxBrush brush in brushes.Values) brush.Dispose();
            brushes.Clear();
            if (overflowBrush != null) { overflowBrush.Dispose(); overflowBrush = null; }
            if (sphereHighlight != null) { sphereHighlight.Dispose(); sphereHighlight = null; }
            if (sphereStops != null) { sphereStops.Dispose(); sphereStops = null; }
            if (textFormat != null) { textFormat.Dispose(); textFormat = null; }
            if (smallFormat != null) { smallFormat.Dispose(); smallFormat = null; }
        }

        private void EnsureRenderResources()
        {
            if (textFormat == null) textFormat = new TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Segoe UI", 12f);
            if (smallFormat == null) smallFormat = new TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Segoe UI", 10f);
            if (sphereStops == null)
            {
                sphereStops = new GradientStopCollection(RenderTarget, new[]
                {
                    new GradientStop { Position = 0f, Color = new Color4(1f, 1f, 1f, 0.62f) },
                    new GradientStop { Position = 0.6f, Color = new Color4(1f, 1f, 1f, 0.10f) },
                    new GradientStop { Position = 1f, Color = new Color4(1f, 1f, 1f, 0f) }
                });
            }
            if (sphereHighlight == null)
                sphereHighlight = new RadialGradientBrush(RenderTarget,
                    new RadialGradientBrushProperties { Center = new Vector2(0, 0), RadiusX = 1, RadiusY = 1 }, sphereStops);
        }

        private DxBrush Brush(Rgb color, double opacity = 1)
        {
            byte alpha = (byte)Math.Max(0, Math.Min(255, Math.Round(opacity * 255)));
            uint key = ((uint)alpha << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
            DxBrush result;
            if (!brushes.TryGetValue(key, out result))
            {
                Color4 value = new Color4(color.R / 255f, color.G / 255f, color.B / 255f, alpha / 255f);
                if (brushes.Count >= 4096)
                {
                    // Draw calls consume this brush immediately; never retain it across another Brush call.
                    if (overflowBrush == null) overflowBrush = new DxBrush(RenderTarget, value);
                    else overflowBrush.Color = value;
                    return overflowBrush;
                }
                result = new DxBrush(RenderTarget, value);
                brushes.Add(key, result);
            }
            return result;
        }

        private static Rgb ParseColor(string value, Rgb fallback)
        {
            if (value == null) return fallback;
            string text = value.Trim().TrimStart('#');
            uint n;
            if (text.Length != 6 || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out n)) return fallback;
            return new Rgb((byte)(n >> 16), (byte)(n >> 8), (byte)n);
        }

        private static bool SameViewport(Viewport a, Viewport b)
        {
            return a != null && a.FromTicks == b.FromTicks && a.ToTicks == b.ToTicks && a.Width == b.Width &&
                a.Height == b.Height && a.MinPriceTick == b.MinPriceTick && a.MaxPriceTick == b.MaxPriceTick;
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            lock (renderGate)
            {
                try { RenderLocked(chartControl, chartScale); }
                catch (Exception ex) { ReportRenderError(ex); }
            }
        }

        private void ReportRenderError(Exception ex)
        {
            string error = ex.GetType().Name + ": " + ex.Message;
            if (renderError != error) { renderError = error; Print("FootprintMap rendering: " + error); }
        }

        private void RenderLocked(ChartControl chartControl, ChartScale chartScale)
        {
            if (terminated || RenderTarget == null || chartControl == null || chartScale == null || ChartPanel == null || ChartBars == null) return;
            if (instrumentTickSize <= 0 || double.IsNaN(chartScale.MinValue) || double.IsNaN(chartScale.MaxValue) ||
                double.IsInfinity(chartScale.MinValue) || double.IsInfinity(chartScale.MaxValue)) return;
            float left = ChartPanel.X, top = ChartPanel.Y, bottom = ChartPanel.Y + ChartPanel.H;
            int columns = (ShowCurrentBook ? 1 : 0) + (ShowSessionProfile ? 1 : 0) + (ShowVisibleProfile ? 1 : 0) + (ShowTpoProfile ? 1 : 0);
            float right = ChartPanel.X + ChartPanel.W - columns * ProfileWidth;
            if (right - left < 80 || bottom - top < 80) return;
            EngineSnapshot snapshot;
            long generation;
            long now = Stopwatch.GetTimestamp();
            lock (engineGate)
            {
                if (engine == null) return;
                snapshot = SnapshotLocked(now);
                generation = engineGeneration;
            }
            DateTime from, to;
            if (!(ContinuousTimeView ? BuildContinuousTimeMap(snapshot, left, right, out from, out to) : BuildTimeMap(chartControl, left, right, out from, out to)))
            {
                EnsureRenderResources();
                Text(ContinuousTimeView ? "Waiting for the first captured market event or saved history." : "Native time mode requires a minute/second chart with at least two visible time bars.",
                    left + 8, top + 8, right - left - 16, 42, Brush(new Rgb(246, 195, 120)), false);
                return;
            }
            if (from.Ticks <= 0 || to <= from || chartScale.MaxValue <= chartScale.MinValue) return;
            Viewport viewport = new Viewport(from.Ticks, to.Ticks,
                chartScale.MinValue / instrumentTickSize, chartScale.MaxValue / instrumentTickSize,
                (int)(right - left), (int)(bottom - top));
            try
            {
                EnsureRenderResources();
                if (cachedScene == null || !SameViewport(cachedViewport, viewport) || sceneVersion != snapshot.Version || sceneGeneration != generation)
                {
                    cachedScene = Visualization.Build(snapshot, viewport, DisplayOptions());
                    studyError = null;
                    try { cachedStudies = ProfileStudies.Build(snapshot, StudyOptions(), from.Ticks, to.Ticks); }
                    catch (Exception ex) { cachedStudies = null; studyError = "Profile study: " + ex.Message; }
                    if (!FlowStudiesEnabled) cachedFlow = null;
                    else if (cachedFlow == null || flowGeneration != generation || (now - flowClock) / (double)Stopwatch.Frequency >= 0.5)
                    {
                        flowClock = now; flowGeneration = generation;
                        try { cachedFlow = OrderFlowStudies.Build(snapshot, FlowOptions()); }
                        catch (Exception ex) { cachedFlow = null; studyError = "Flow study: " + ex.Message; }
                    }
                    cachedViewport = viewport;
                    sceneVersion = snapshot.Version;
                    sceneGeneration = generation;
                }
                profileMarginWarning = !ContinuousTimeView && columns > 0 && snapshot.TimeTicks > 0 && TimeX(snapshot.TimeTicks) > right;
                Rgb background = ParseColor(BackgroundHex, new Rgb(30, 36, 47));
                Rgb buy = ParseColor(BuyColorHex, new Rgb(111, 219, 157));
                Rgb sell = ParseColor(SellColorHex, new Rgb(238, 100, 116));
                Rgb unknown = ParseColor(UnknownColorHex, new Rgb(170, 179, 190));
                AntialiasMode priorMode = RenderTarget.AntialiasMode;
                RenderTarget.PushAxisAlignedClip(new RectangleF(left, top, ChartPanel.W, ChartPanel.H), AntialiasMode.Aliased);
                try
                {
                    if (OpaqueBackground) RenderTarget.FillRectangle(new RectangleF(left, top, ChartPanel.W, ChartPanel.H), Brush(background));
                    RenderTarget.PushAxisAlignedClip(new RectangleF(left, top, right - left, bottom - top), AntialiasMode.Aliased);
                    try
                    {
                        if (ShowHeatmap) DrawHeat(chartControl, chartScale, viewport, cachedScene, left);
                        if (ShowPriceGrid) DrawPriceGrid(chartScale, left, right, top, bottom);
                        if (ShowBestBidAsk) DrawBestQuotes(chartControl, chartScale, snapshot, from.Ticks, to.Ticks, buy, sell);
                        if (ShowCurrentDepthProjection) DrawCurrentProjection(chartScale, snapshot, cachedScene, left, right, top, bottom);
                        DrawStudyLines(chartScale, cachedStudies, left, right);
                        if (ShowFootprint) DrawFootprints(chartScale, cachedStudies, from.Ticks, to.Ticks, buy, sell);
                        if (cachedFlow != null) DrawFlowMarkers(chartScale, cachedFlow, from.Ticks, to.Ticks, buy, sell);
                        if (ShowBubbles) DrawBubbles(chartControl, chartScale, cachedScene.Bubbles, buy, sell, unknown);
                        if (ShowCumulativeDelta || ShowVolumeBars) DrawLowerPanel(chartControl, snapshot, from.Ticks, to.Ticks, left, right, bottom - (ContinuousTimeView ? 23 : 0), buy, sell, background);
                        if (ShowCurrentPrice) DrawCurrentPrices(chartScale, snapshot, left, right, top, bottom, buy, sell);
                        if (ContinuousTimeView) DrawTimeAxis(from, to, left, right, bottom, background);
                    }
                    finally { RenderTarget.PopAxisAlignedClip(); }
                    DrawProfiles(chartScale, snapshot, cachedScene, right, top, bottom, buy, sell, unknown, background);
                    if (ShowLegend) DrawLegend(snapshot, cachedScene, left, top, right, buy, sell);
                    if (ShowStatus) DrawStatus(snapshot, left, top, right, now);
                    if (ShowImbalance) DrawStudySummary(cachedStudies, left, top, right, buy, sell);
                    if (studyError != null) Text(studyError, left + 7, top + 123, right - left - 14, 30, Brush(new Rgb(246, 195, 120)), true);
                }
                finally
                {
                    RenderTarget.PopAxisAlignedClip();
                    RenderTarget.AntialiasMode = priorMode;
                }
                renderError = null;
            }
            catch (Exception ex)
            {
                // Surface the exact rendering failure once; do not silently claim a blank chart is healthy.
                ReportRenderError(ex);
            }
        }

        private bool BuildContinuousTimeMap(EngineSnapshot snapshot, float left, float right, out DateTime from, out DateTime to)
        {
            from = to = DateTime.MinValue;
            if (snapshot.TimeTicks <= 0) return false;
            long span = (long)(Math.Max(5, zoomSeconds) * TimeSpan.TicksPerSecond);
            long end = snapshot.TimeTicks - (long)(scrollSeconds * TimeSpan.TicksPerSecond);
            long future = scrollSeconds == 0 ? Math.Max(TimeSpan.TicksPerSecond, span / 25) : 0;
            end = Math.Max(1, Math.Min(DateTime.MaxValue.Ticks - future, end)) + future;
            long begin = Math.Max(1, end - span);
            if (end <= begin) return false;
            from = new DateTime(begin); to = new DateTime(end);
            timeAnchorTicks = new[] { begin, end };
            timeAnchorX = new[] { left, right };
            return true;
        }

        private void DrawTimeAxis(DateTime from, DateTime to, float left, float right, float bottom, Rgb background)
        {
            RenderTarget.FillRectangle(new RectangleF(left, bottom - 22, right - left, 22), Brush(background));
            RenderTarget.DrawLine(new Vector2(left, bottom - 22), new Vector2(right, bottom - 22), Brush(new Rgb(80, 96, 107)), 1);
            int labels = Math.Max(2, (int)((right - left) / 115));
            for (int i = 0; i <= labels; i++)
            {
                long ticks = from.Ticks + (long)((to.Ticks - from.Ticks) * (i / (double)labels));
                float x = left + (right - left) * i / labels;
                string label = new DateTime(ticks).ToString(zoomSeconds < 20 ? "HH:mm:ss.fff" : "HH:mm:ss", CultureInfo.InvariantCulture);
                Text(label, Math.Max(left, Math.Min(right - 80, x - 38)), bottom - 19, 80, 17, Brush(new Rgb(196, 209, 219)), true);
            }
        }

        private bool BuildTimeMap(ChartControl chart, float left, float right, out DateTime from, out DateTime to)
        {
            from = to = DateTime.MinValue;
            if (Bars == null || BarsPeriod == null ||
                (BarsPeriod.BarsPeriodType != BarsPeriodType.Minute && BarsPeriod.BarsPeriodType != BarsPeriodType.Second)) return false;
            int first = Math.Max(0, ChartBars.FromIndex - 1);
            int last = Math.Min(ChartBars.Count - 1, ChartBars.ToIndex + 1);
            if (last <= first) return false;
            var times = new List<long>(last - first + 1);
            var xs = new List<float>(last - first + 1);
            for (int index = first; index <= last; index++)
            {
                long tick = ChartBars.GetTimeByBarIdx(chart, index).Ticks;
                float x = chart.GetXByBarIndex(ChartBars, index);
                if (times.Count > 0 && (tick <= times[times.Count - 1] || x <= xs[xs.Count - 1])) return false;
                times.Add(tick); xs.Add(x);
            }
            timeAnchorTicks = times.ToArray(); timeAnchorX = xs.ToArray();
            long a = TimeAtX(left), b = TimeAtX(right);
            if (a < DateTime.MinValue.Ticks || b > DateTime.MaxValue.Ticks || b <= a) return false;
            from = new DateTime(a); to = new DateTime(b);
            return true;
        }

        private float TimeX(long ticks)
        {
            int index = Array.BinarySearch(timeAnchorTicks, ticks);
            if (index >= 0) return timeAnchorX[index];
            index = Math.Max(0, Math.Min(timeAnchorTicks.Length - 2, ~index - 1));
            double fraction = (ticks - timeAnchorTicks[index]) / (double)(timeAnchorTicks[index + 1] - timeAnchorTicks[index]);
            return timeAnchorX[index] + (float)(fraction * (timeAnchorX[index + 1] - timeAnchorX[index]));
        }

        private long TimeAtX(float x)
        {
            int index = Array.BinarySearch(timeAnchorX, x);
            if (index >= 0) return timeAnchorTicks[index];
            index = Math.Max(0, Math.Min(timeAnchorX.Length - 2, ~index - 1));
            double fraction = (x - timeAnchorX[index]) / (timeAnchorX[index + 1] - timeAnchorX[index]);
            double offset = fraction * (timeAnchorTicks[index + 1] - timeAnchorTicks[index]);
            if (offset > DateTime.MaxValue.Ticks - timeAnchorTicks[index]) return DateTime.MaxValue.Ticks;
            if (offset < -timeAnchorTicks[index]) return DateTime.MinValue.Ticks;
            return timeAnchorTicks[index] + (long)offset;
        }

        private long sceneVersion = -1;

        private void DrawHeat(ChartControl chart, ChartScale scale, Viewport view, RenderScene scene, float left)
        {
            RenderTarget.AntialiasMode = AntialiasMode.Aliased;
            double duration = view.ToTicks - view.FromTicks, range = view.MaxPriceTick - view.MinPriceTick;
            foreach (HeatTile tile in scene.Heat)
            {
                // Convert the core's linear viewport coordinates back to data coordinates, then use native chart transforms.
                long t0 = view.FromTicks + (long)(tile.X / view.Width * duration);
                long t1 = view.FromTicks + (long)((tile.X + tile.Width) / view.Width * duration);
                double high = view.MaxPriceTick - tile.Y / view.Height * range;
                double low = view.MaxPriceTick - (tile.Y + tile.Height) / view.Height * range;
                float x0 = TimeX(Math.Max(view.FromTicks, t0));
                float x1 = TimeX(Math.Min(view.ToTicks, t1));
                float y0 = scale.GetYByValue(high * instrumentTickSize), y1 = scale.GetYByValue(low * instrumentTickSize);
                float width = Math.Max(0, x1 - x0), height = Math.Max(0, y1 - y0);
                if (width <= 0 || height <= 0) continue;
                RenderTarget.FillRectangle(new RectangleF(x0, y0, width, height), Brush(tile.Color, HeatOpacity));
            }
        }

        private void DrawPriceGrid(ChartScale scale, float left, float right, float top, float bottom)
        {
            double span = (scale.MaxValue - scale.MinValue) / instrumentTickSize;
            long step = Math.Max(1, (long)Math.Ceiling(span / Math.Max(2, (bottom - top) / 45)));
            long first = (long)Math.Ceiling(scale.MinValue / instrumentTickSize / step) * step;
            for (long tick = first; tick * instrumentTickSize <= scale.MaxValue; tick += step)
            {
                float y = scale.GetYByValue(tick * instrumentTickSize);
                RenderTarget.DrawLine(new Vector2(left, y), new Vector2(right, y), Brush(new Rgb(190, 201, 216), 0.10), 1f);
            }
        }

        private void DrawCurrentProjection(ChartScale scale, EngineSnapshot data, RenderScene scene, float left, float right, float top, float bottom)
        {
            if (!data.Connected || !data.HasDepth || data.TimeTicks < cachedViewport.FromTicks || data.TimeTicks >= cachedViewport.ToTicks) return;
            float x = Math.Max(left, TimeX(data.TimeTicks));
            if (right <= x) return;
            double maximum = scene.HeatMaximum;
            if (maximum <= 0)
            {
                foreach (DepthLevel row in data.Bids) maximum = Math.Max(maximum, row.Quantity);
                foreach (DepthLevel row in data.Asks) maximum = Math.Max(maximum, row.Quantity);
            }
            DrawProjectedRows(scale, data.Bids, x, right, Math.Max(1, maximum));
            DrawProjectedRows(scale, data.Asks, x, right, Math.Max(1, maximum));
            RenderTarget.DrawLine(new Vector2(x, top + 115), new Vector2(x, bottom - 24), Brush(new Rgb(205, 222, 230), 0.65), 1);
            Text("NOW", x - 27, top + 98, 54, 15, Brush(new Rgb(220, 232, 238)), true, true);
            // Forward projection is deliberately translucent and never saved as history.
        }

        private void DrawProjectedRows(ChartScale scale, DepthLevel[] levels, float from, float to, double maximum)
        {
            foreach (DepthLevel row in levels)
            {
                float y0 = scale.GetYByValue((row.Tick + 0.5) * instrumentTickSize);
                float y1 = scale.GetYByValue((row.Tick - 0.5) * instrumentTickSize);
                double strength = Math.Pow(Math.Min(1, row.Quantity / maximum), HeatGamma);
                RenderTarget.FillRectangle(new RectangleF(from, y0, to - from, Math.Max(1, y1 - y0)),
                    Brush(Visualization.Color(strength, Palette), HeatOpacity * 0.45));
            }
        }

        private string PriceText(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) ? "—" : Instrument.MasterInstrument.FormatPrice(value);
        }

        private void DrawCurrentPrices(ChartScale scale, EngineSnapshot data, float left, float right, float top, float bottom, Rgb buy, Rgb sell)
        {
            double bid, ask;
            lock (engineGate) { bid = latestBid; ask = latestAsk; }
            if (!data.Connected) bid = ask = double.NaN;
            if (data.Bids.Length > 0) bid = data.Bids[0].Tick * instrumentTickSize;
            if (data.Asks.Length > 0) ask = data.Asks[0].Tick * instrumentTickSize;
            TradeRecord last = data.Trades.Length == 0 ? null : data.Trades[data.Trades.Length - 1];
            string lastLabel = Interlocked.Read(ref lastTradeClock) == 0 || !data.Connected ? "RECORDED LAST" : "LAST";
            string line = lastLabel + " " + (last == null ? "—" : PriceText(last.PriceTick * instrumentTickSize)) +
                "    BID " + PriceText(bid) + "    ASK " + PriceText(ask);
            Text(line, left + 7, top + 49, right - left - 14, 18, Brush(new Rgb(230, 237, 241)), false);
            if (ShowBestBidAsk)
            {
                if (!double.IsNaN(bid)) PriceGuide(scale, bid, null, left, right, top, bottom, buy);
                if (!double.IsNaN(ask)) PriceGuide(scale, ask, null, left, right, top, bottom, sell);
            }
            if (last != null)
                PriceGuide(scale, last.PriceTick * instrumentTickSize, PriceText(last.PriceTick * instrumentTickSize), left, right, top, bottom, new Rgb(255, 222, 115));
        }

        private void PriceGuide(ChartScale scale, double price, string label, float left, float right, float top, float bottom, Rgb color)
        {
            float actual = scale.GetYByValue(price);
            float limit = bottom - (ShowCumulativeDelta || ShowVolumeBars ? Math.Min(LowerPanelHeight, ChartPanel.H * 0.35f) : 0) - 26;
            if (actual >= top && actual <= limit)
                RenderTarget.DrawLine(new Vector2(left, actual), new Vector2(right, actual), Brush(color, label == null ? 0.35 : 0.75), label == null ? 0.7f : 1.1f);
            if (label == null) return;
            float y = Math.Max(top + 118, Math.Min(limit - 18, actual - 9));
            if (actual < top + 118) label = "↑ " + label;
            else if (actual > limit) label = "↓ " + label;
            float width = Math.Min(130, right - left);
            RenderTarget.FillRectangle(new RectangleF(right - width, y, width, 19), Brush(new Rgb(19, 27, 31), 0.96));
            RenderTarget.DrawRectangle(new RectangleF(right - width, y, width, 19), Brush(color), 1);
            Text(label, right - width + 4, y + 1, width - 8, 17, Brush(color), false, true);
        }

        private void DrawStudyLines(ChartScale scale, ProfileResult studies, float left, float right)
        {
            if (studies == null) return;
            if (ShowPoc && studies.PocTick.HasValue) StudyLine(scale, studies.PocTick.Value * instrumentTickSize, "POC", left, right, new Rgb(249, 180, 84));
            if (ShowValueArea)
            {
                if (studies.ValueAreaHighTick.HasValue) StudyLine(scale, studies.ValueAreaHighTick.Value * instrumentTickSize, "VAH", left, right, new Rgb(159, 171, 232));
                if (studies.ValueAreaLowTick.HasValue) StudyLine(scale, studies.ValueAreaLowTick.Value * instrumentTickSize, "VAL", left, right, new Rgb(159, 171, 232));
            }
            if (ShowVwap && !double.IsNaN(studies.Vwap)) StudyLine(scale, studies.Vwap, "VWAP", left, right, new Rgb(235, 223, 166));
            if (ShowVwapBands)
            {
                if (!double.IsNaN(studies.UpperBand)) StudyLine(scale, studies.UpperBand, "VWAP +σ", left, right, new Rgb(179, 188, 149));
                if (!double.IsNaN(studies.LowerBand)) StudyLine(scale, studies.LowerBand, "VWAP −σ", left, right, new Rgb(179, 188, 149));
            }
            if (ShowLargeLiquidity)
            {
                foreach (DepthLevel row in studies.LargeBids) StudyLine(scale, row.Tick * instrumentTickSize, "B " + row.Quantity, Math.Max(left, right - 160), right, new Rgb(86, 213, 157));
                foreach (DepthLevel row in studies.LargeAsks) StudyLine(scale, row.Tick * instrumentTickSize, "A " + row.Quantity, Math.Max(left, right - 160), right, new Rgb(241, 130, 120));
            }
        }

        private void StudyLine(ChartScale scale, double price, string label, float left, float right, Rgb color)
        {
            float y = scale.GetYByValue(price);
            if (y < ChartPanel.Y || y > ChartPanel.Y + ChartPanel.H) return;
            RenderTarget.DrawLine(new Vector2(left, y), new Vector2(right, y), Brush(color, 0.65), 0.8f);
            Text(label, left + 5, y - 14, 100, 14, Brush(color), true);
        }

        private void DrawStudySummary(ProfileResult studies, float left, float top, float right, Rgb buy, Rgb sell)
        {
            if (studies == null) return;
            string text = "Book " + studies.BookImbalance.ToString("+0%;-0%;0%", CultureInfo.InvariantCulture) +
                "   Volume " + studies.VolumeImbalance.ToString("+0%;-0%;0%", CultureInfo.InvariantCulture) +
                "   " + StudyScope + " " + studies.TotalVolume.ToString("N0", CultureInfo.InvariantCulture);
            if (studies.PartialTradeHistory) text += "   retained prints partial";
            if (cachedFlow != null && cachedFlow.Truncated) text += "   flow scan capped";
            Text(text, left + 7, top + 103, right - left - 65, 18, Brush(studies.BookImbalance >= 0 ? buy : sell), true);
        }

        private void DrawFootprints(ChartScale scale, ProfileResult studies, long from, long to, Rgb buy, Rgb sell)
        {
            if (studies == null) return;
            foreach (FootprintBar bar in studies.Footprints)
            {
                if (bar.ToTicks <= from || bar.FromTicks >= to) continue;
                float x = TimeX(Math.Max(from, bar.FromTicks)), x1 = TimeX(Math.Min(to, bar.ToTicks));
                if (x1 - x < 5) continue;
                foreach (FootprintCell cell in bar.Levels)
                {
                    float y = scale.GetYByValue((cell.Tick + FootprintGroupTicks - 0.5) * instrumentTickSize);
                    float y1 = scale.GetYByValue((cell.Tick - 0.5) * instrumentTickSize);
                    if (y1 < ChartPanel.Y || y > ChartPanel.Y + ChartPanel.H) continue;
                    Rgb color = cell.BuyImbalance ? buy : cell.SellImbalance ? sell : new Rgb(207, 218, 228);
                    RenderTarget.FillRectangle(new RectangleF(x + 1, y, Math.Max(1, x1 - x - 2), Math.Max(1, y1 - y)), Brush(color, cell.BuyImbalance || cell.SellImbalance ? 0.25 : 0.06));
                    if (x1 - x >= 48 && y1 - y >= 12)
                        Text(cell.Sell + " × " + cell.Buy + (cell.Unknown > 0 ? " ?" + cell.Unknown : ""), x + 2, y, x1 - x - 4, y1 - y, Brush(color), true, true);
                }
            }
        }

        private void DrawFlowMarkers(ChartScale scale, OrderFlowResult flow, long from, long to, Rgb buy, Rgb sell)
        {
            RenderTarget.AntialiasMode = AntialiasMode.PerPrimitive;
            foreach (FlowMarker marker in flow.Markers)
            {
                if (marker.TimeTicks < from || marker.TimeTicks >= to) continue;
                float x = TimeX(marker.TimeTicks), y = scale.GetYByValue(marker.PriceTick * instrumentTickSize);
                if (y < ChartPanel.Y || y > ChartPanel.Y + ChartPanel.H) continue;
                Rgb color = marker.Side == TradeSide.Buy ? buy : marker.Side == TradeSide.Sell ? sell : new Rgb(194, 198, 207);
                string label;
                switch (marker.Kind)
                {
                    case FlowMarkerKind.Absorption: label = "AB"; break;
                    case FlowMarkerKind.Sweep: label = "SW"; break;
                    case FlowMarkerKind.LargePrint: label = "LP"; break;
                    case FlowMarkerKind.LiquidityAdded: label = "+L"; break;
                    case FlowMarkerKind.LiquidityRemoved: label = "−L"; break;
                    default: label = "RP?"; break;
                }
                RenderTarget.DrawRectangle(new RectangleF(x - 5, y - 5, 10, 10), Brush(color), 1.5f);
                if (FlowMarkerLabels)
                {
                    string quantity = marker.Volume > 0 ? marker.Volume.ToString(CultureInfo.InvariantCulture) : marker.Quantity.ToString("0.#", CultureInfo.InvariantCulture);
                    Text(label + " " + quantity, x + 7, y - 8, 90, 16, Brush(color), true);
                }
            }
        }

        private void DrawBestQuotes(ChartControl chart, ChartScale scale, EngineSnapshot data, long from, long to, Rgb buy, Rgb sell)
        {
            HeatFrame previous = null;
            foreach (HeatFrame frame in data.Frames)
            {
                if (frame.ToTicks < from || frame.FromTicks > to) { previous = frame; continue; }
                float x0 = TimeX(Math.Max(from, frame.FromTicks));
                float x1 = TimeX(Math.Min(to, frame.ToTicks));
                bool join = previous != null && previous.Epoch == frame.Epoch && previous.ToTicks == frame.FromTicks;
                DrawQuoteStep(scale, x0, x1, frame.BestBidTick, join ? previous.BestBidTick : null, buy);
                DrawQuoteStep(scale, x0, x1, frame.BestAskTick, join ? previous.BestAskTick : null, sell);
                previous = frame;
            }
        }

        private void DrawQuoteStep(ChartScale scale, float x0, float x1, long? tick, long? oldTick, Rgb color)
        {
            if (!tick.HasValue) return;
            float y = scale.GetYByValue(tick.Value * instrumentTickSize);
            DxBrush brush = Brush(color, 0.95);
            if (oldTick.HasValue) RenderTarget.DrawLine(new Vector2(x0, scale.GetYByValue(oldTick.Value * instrumentTickSize)), new Vector2(x0, y), brush, QuoteLineWidth);
            RenderTarget.DrawLine(new Vector2(x0, y), new Vector2(x1, y), brush, QuoteLineWidth);
        }

        private void DrawBubbles(ChartControl chart, ChartScale scale, Bubble[] bubbles, Rgb buy, Rgb sell, Rgb unknown)
        {
            RenderTarget.AntialiasMode = AntialiasMode.PerPrimitive;
            // Largest first keeps smaller overlapping executions visible. The data and profiles remain unchanged.
            Bubble[] ordered = (Bubble[])bubbles.Clone();
            Array.Sort(ordered, (a, b) => b.Radius.CompareTo(a.Radius));
            int count = Math.Min(ordered.Length, MaximumRenderedBubbles);
            for (int i = 0; i < count; i++)
            {
                Bubble b = ordered[i];
                if (b.Radius <= 0 || b.Total <= 0) continue;
                float x = TimeX(b.TimeTicks);
                float y = scale.GetYByValue(b.PriceTick * instrumentTickSize), radius = (float)b.Radius;
                Ellipse ellipse = new Ellipse(new Vector2(x, y), radius, radius);
                if (BubbleColors == BubbleColorMode.Gradient)
                {
                    double total = b.Total;
                    Rgb mixed = new Rgb((byte)((buy.R * (double)b.Buy + sell.R * (double)b.Sell + unknown.R * (double)b.Unknown) / total),
                        (byte)((buy.G * (double)b.Buy + sell.G * (double)b.Sell + unknown.G * (double)b.Unknown) / total),
                        (byte)((buy.B * (double)b.Buy + sell.B * (double)b.Sell + unknown.B * (double)b.Unknown) / total));
                    RenderTarget.FillEllipse(ellipse, Brush(mixed, BubbleOpacity));
                }
                else if (BubbleColors == BubbleColorMode.Split)
                {
                    double firstFraction = b.Sell / (double)b.Total, secondFraction = (b.Sell + (double)b.Buy) / b.Total;
                    float first = x + radius * (float)CircleCut(firstFraction), second = x + radius * (float)CircleCut(secondFraction);
                    FillEllipseSlice(ellipse, x - radius, first, sell);
                    FillEllipseSlice(ellipse, first, second, buy);
                    FillEllipseSlice(ellipse, second, x + radius, unknown);
                }
                else
                {
                    double angle = -Math.PI / 2;
                    angle = FillSector(x, y, radius, angle, b.Buy / (double)b.Total, buy);
                    angle = FillSector(x, y, radius, angle, b.Sell / (double)b.Total, sell);
                    FillSector(x, y, radius, angle, b.Unknown / (double)b.Total, unknown);
                }
                if (ThreeDimensionalBubbles && radius >= 3)
                {
                    sphereHighlight.Center = new Vector2(x - radius * 0.32f, y - radius * 0.36f);
                    sphereHighlight.RadiusX = radius * 1.2f;
                    sphereHighlight.RadiusY = radius * 1.2f;
                    RenderTarget.FillEllipse(ellipse, sphereHighlight);
                }
                if (BubbleOutline) RenderTarget.DrawEllipse(ellipse, Brush(new Rgb(9, 15, 22), 0.8), 0.8f);
                if (BubbleLabels && radius >= 11)
                {
                    string label = (DeltaBubbles ? Math.Abs(b.Delta) : b.Total).ToString(CultureInfo.InvariantCulture);
                    Text(label, x - radius, y - 7, radius * 2, 16, Brush(new Rgb(240, 247, 250)), true, true);
                }
            }
        }

        private static double CircleCut(double fraction)
        {
            // Exact area split: a linear width fraction would misrepresent the volume in a circle.
            if (fraction <= 0) return -1;
            if (fraction >= 1) return 1;
            double low = -1, high = 1;
            for (int i = 0; i < 24; i++)
            {
                double x = (low + high) / 2;
                double area = (x * Math.Sqrt(Math.Max(0, 1 - x * x)) + Math.Asin(x) + Math.PI / 2) / Math.PI;
                if (area < fraction) low = x; else high = x;
            }
            return (low + high) / 2;
        }

        private void FillEllipseSlice(Ellipse ellipse, float start, float end, Rgb color)
        {
            if (end <= start) return;
            RenderTarget.PushAxisAlignedClip(new RectangleF(start, ellipse.Point.Y - ellipse.RadiusY, end - start, ellipse.RadiusY * 2), AntialiasMode.Aliased);
            try { RenderTarget.FillEllipse(ellipse, Brush(color, BubbleOpacity)); }
            finally { RenderTarget.PopAxisAlignedClip(); }
        }

        private double FillSector(float x, float y, float radius, double angle, double fraction, Rgb color)
        {
            if (fraction <= 0) return angle;
            double end = angle + fraction * Math.PI * 2;
            if (fraction >= 0.999999)
            {
                RenderTarget.FillEllipse(new Ellipse(new Vector2(x, y), radius, radius), Brush(color, BubbleOpacity));
                return end;
            }
            using (PathGeometry geometry = new PathGeometry(NinjaTrader.Core.Globals.D2DFactory))
            {
                using (GeometrySink sink = geometry.Open())
                {
                    sink.BeginFigure(new Vector2(x, y), FigureBegin.Filled);
                    int segments = Math.Max(2, (int)Math.Ceiling(fraction * 64));
                    for (int step = 0; step <= segments; step++)
                    {
                        double a = angle + (end - angle) * step / segments;
                        sink.AddLine(new Vector2(x + radius * (float)Math.Cos(a), y + radius * (float)Math.Sin(a)));
                    }
                    sink.EndFigure(FigureEnd.Closed);
                    sink.Close();
                }
                RenderTarget.FillGeometry(geometry, Brush(color, BubbleOpacity));
            }
            return end;
        }

        private void DrawProfiles(ChartScale scale, EngineSnapshot data, RenderScene scene, float x, float top, float bottom, Rgb buy, Rgb sell, Rgb unknown, Rgb background)
        {
            if (ShowCurrentBook)
            {
                PanelHeader(CobCumulative ? "COB · cumulative" : "COB", x, top, bottom, background);
                int bidCount = CobBids ? BookRowCount(data.Bids, CobBidRows) : 0;
                int askCount = CobAsks ? BookRowCount(data.Asks, CobAskRows) : 0;
                decimal maximum = 1;
                decimal bidTotal = BookSideTotal(data.Bids, bidCount, ref maximum);
                decimal askTotal = BookSideTotal(data.Asks, askCount, ref maximum);
                if (CobCumulative) maximum = Math.Max(1, Math.Max(bidTotal, askTotal));
                DrawBookSide(scale, data.Bids, bidCount, bidTotal, maximum, x, top, bottom, buy);
                DrawBookSide(scale, data.Asks, askCount, askTotal, maximum, x, top, bottom, sell);
                x += ProfileWidth;
            }
            if (ShowSessionProfile)
            {
                PanelHeader("SVP · recorded", x, top, bottom, background);
                ProfileBars(scale, data.Profile, x, top, bottom, buy, sell, unknown);
                x += ProfileWidth;
            }
            if (ShowVisibleProfile)
            {
                PanelHeader("CVP · visible", x, top, bottom, background);
                ProfileBars(scale, scene.VisibleProfile, x, top, bottom, buy, sell, unknown);
                x += ProfileWidth;
            }
            if (ShowTpoProfile && cachedStudies != null)
            {
                PanelHeader("TPO · prints", x, top, bottom, background);
                int maximum = 1;
                foreach (TpoLevel row in cachedStudies.TpoLevels) maximum = Math.Max(maximum, row.Count);
                foreach (TpoLevel row in cachedStudies.TpoLevels)
                {
                    float y, height;
                    if (!ColumnRow(scale, row.Tick, TpoGroupTicks, top, bottom, out y, out height)) continue;
                    bool poc = cachedStudies.TpoPocTick.HasValue && row.Tick == cachedStudies.TpoPocTick.Value;
                    bool inArea = cachedStudies.TpoValueAreaLowTick.HasValue && cachedStudies.TpoValueAreaHighTick.HasValue &&
                        row.Tick >= cachedStudies.TpoValueAreaLowTick.Value && row.Tick <= cachedStudies.TpoValueAreaHighTick.Value;
                    Rgb color = poc ? new Rgb(249, 180, 84) : new Rgb(157, 165, 215);
                    float width = (ProfileWidth - 8) * row.Count / (float)maximum;
                    if (ShowProfileBars)
                        RenderTarget.FillRectangle(new RectangleF(ColumnBarX(x, width, ProfileAlignRight), y, width, height), Brush(color, inArea ? 0.85 : 0.4));
                    if (ProfileLabels && height >= 11)
                        Text(row.Count.ToString(CultureInfo.InvariantCulture), x + 4, y, ProfileWidth - 8, height, Brush(new Rgb(235, 241, 245)), true, false, ProfileAlignRight);
                }
            }
        }

        private void PanelHeader(string label, float x, float top, float bottom, Rgb background)
        {
            RenderTarget.FillRectangle(new RectangleF(x, top, ProfileWidth, bottom - top), Brush(background));
            RenderTarget.DrawLine(new Vector2(x, top), new Vector2(x, bottom), Brush(new Rgb(82, 92, 106)), 1);
            Text(label, x + 4, top + 4, ProfileWidth - 8, 20, Brush(new Rgb(213, 221, 231)), true);
        }

        private static int BookRowCount(DepthLevel[] rows, int limit)
        {
            return limit <= 0 ? rows.Length : Math.Min(rows.Length, limit);
        }

        private static decimal BookSideTotal(DepthLevel[] rows, int count, ref decimal maximum)
        {
            // Decimal keeps sums of bounded Int64 provider quantities exact.
            decimal total = 0;
            for (int i = 0; i < count; i++)
            {
                total += rows[i].Quantity;
                maximum = Math.Max(maximum, rows[i].Quantity);
            }
            return total;
        }

        private void DrawBookSide(ChartScale scale, DepthLevel[] rows, int count, decimal sideTotal, decimal maximum,
            float x, float top, float bottom, Rgb color)
        {
            decimal cumulative = 0;
            for (int i = 0; i < count; i++)
            {
                DepthLevel level = rows[i];
                cumulative += level.Quantity;
                decimal quantity = CobCumulative ? cumulative : level.Quantity;
                float y, height;
                if (!ColumnRow(scale, level.Tick, 1, top, bottom, out y, out height)) continue;
                float width = (float)(quantity / maximum * (ProfileWidth - 8));
                if (ShowCobBars)
                    RenderTarget.FillRectangle(new RectangleF(ColumnBarX(x, width, CobAlignRight), y, width, height), Brush(color, 0.8));
                if (CobLabels && height >= 11)
                {
                    string label = CobPercentages && sideTotal > 0
                        ? (100 * quantity / sideTotal).ToString("0.0", CultureInfo.InvariantCulture) + "%"
                        : quantity.ToString("0", CultureInfo.InvariantCulture);
                    Text(label, x + 4, y, ProfileWidth - 8, height, Brush(new Rgb(235, 241, 245)), true, false, CobAlignRight);
                }
            }
        }

        private float ColumnBarX(float x, float width, bool alignRight)
        {
            return x + 3 + (alignRight ? ProfileWidth - 8 - width : 0);
        }

        private bool ColumnRow(ChartScale scale, double firstTick, int tickCount, float top, float bottom, out float y, out float height)
        {
            y = scale.GetYByValue((firstTick + tickCount - 0.5) * instrumentTickSize);
            float end = scale.GetYByValue((firstTick - 0.5) * instrumentTickSize);
            height = 0;
            if (float.IsNaN(y) || float.IsInfinity(y) || float.IsNaN(end) || float.IsInfinity(end) || end <= top + 26 || y >= bottom) return false;
            float rowHeight = Math.Max(1, end - y - 0.4f);
            float rowBottom = Math.Min(bottom, y + rowHeight);
            y = Math.Max(top + 26, y);
            height = rowBottom - y;
            return height > 0;
        }

        private void ProfileBars(ChartScale scale, ProfileLevel[] profile, float x, float top, float bottom, Rgb buy, Rgb sell, Rgb unknown)
        {
            double max = 1;
            foreach (ProfileLevel level in profile) max = Math.Max(max, ProfileDelta ? Math.Abs((double)(level.Buy - level.Sell)) : level.Total);
            foreach (ProfileLevel level in profile)
            {
                float y0, height;
                if (!ColumnRow(scale, level.Tick, 1, top, bottom, out y0, out height)) continue;
                double quantity = ProfileDelta ? Math.Abs((double)(level.Buy - level.Sell)) : level.Total;
                float cursor = ColumnBarX(x, (float)(quantity / max * (ProfileWidth - 8)), ProfileAlignRight);
                if (ShowProfileBars && ProfileDelta)
                {
                    double delta = level.Buy - level.Sell;
                    RenderTarget.FillRectangle(new RectangleF(cursor, y0, (float)(Math.Abs(delta) / max * (ProfileWidth - 8)), height), Brush(delta >= 0 ? buy : sell, 0.8));
                }
                else if (ShowProfileBars)
                {
                    float sellWidth = (float)(level.Sell / max * (ProfileWidth - 8));
                    float buyWidth = (float)(level.Buy / max * (ProfileWidth - 8));
                    float unknownWidth = (float)(level.Unknown / max * (ProfileWidth - 8));
                    RenderTarget.FillRectangle(new RectangleF(cursor, y0, sellWidth, height), Brush(sell, 0.8)); cursor += sellWidth;
                    RenderTarget.FillRectangle(new RectangleF(cursor, y0, buyWidth, height), Brush(buy, 0.8)); cursor += buyWidth;
                    RenderTarget.FillRectangle(new RectangleF(cursor, y0, unknownWidth, height), Brush(unknown, 0.8));
                }
                if (ProfileLabels && height >= 11)
                    Text((ProfileDelta ? level.Buy - level.Sell : level.Total).ToString(CultureInfo.InvariantCulture), x + 4, y0, ProfileWidth - 8, height, Brush(new Rgb(235, 241, 245)), true, false, ProfileAlignRight);
            }
        }

        private void DrawLowerPanel(ChartControl chart, EngineSnapshot data, long from, long to, float left, float right, float bottom, Rgb buy, Rgb sell, Rgb background)
        {
            float height = Math.Min(LowerPanelHeight, ChartPanel.H * 0.35f), top = bottom - height;
            RenderTarget.FillRectangle(new RectangleF(left, top, right - left, height), Brush(background, 0.94));
            RenderTarget.DrawLine(new Vector2(left, top), new Vector2(right, top), Brush(new Rgb(83, 93, 107)), 1);
            long min = 0, max = 0;
            Dictionary<int, double[]> volume = new Dictionary<int, double[]>();
            foreach (TradeRecord trade in data.Trades)
            {
                if (trade.TimeTicks < from || trade.TimeTicks >= to || trade.TimeTicks < data.SessionStartTicks) continue;
                min = Math.Min(min, trade.CumulativeDelta); max = Math.Max(max, trade.CumulativeDelta);
                int bucket = (int)((TimeX(trade.TimeTicks) - left) / 5);
                double[] values;
                if (!volume.TryGetValue(bucket, out values)) { values = new double[3]; volume.Add(bucket, values); }
                values[trade.Side == TradeSide.Buy ? 0 : trade.Side == TradeSide.Sell ? 1 : 2] += trade.Volume;
            }
            double maxVolume = 1;
            foreach (double[] values in volume.Values) maxVolume = Math.Max(maxVolume, values[0] + values[1] + values[2]);
            if (ShowVolumeBars)
                foreach (KeyValuePair<int, double[]> pair in volume)
                {
                    float x = left + pair.Key * 5, y = bottom - 3;
                    for (int side = 0; side < 3; side++)
                    {
                        float h = (float)(pair.Value[side] / maxVolume * (height - 25));
                        y -= h;
                        RenderTarget.FillRectangle(new RectangleF(x, y, 4, h), Brush(side == 0 ? buy : side == 1 ? sell : new Rgb(163, 173, 186), 0.30));
                    }
                }
            if (ShowCumulativeDelta)
            {
                double span = Math.Max(1, (double)max - min);
                Vector2? last = null;
                foreach (TradeRecord trade in data.Trades)
                {
                    if (trade.TimeTicks < from || trade.TimeTicks >= to || trade.TimeTicks < data.SessionStartTicks) continue;
                    Vector2 point = new Vector2(TimeX(trade.TimeTicks), top + 20 + (float)((max - (double)trade.CumulativeDelta) / span * (height - 25)));
                    if (last.HasValue) RenderTarget.DrawLine(last.Value, point, Brush(new Rgb(226, 229, 232)), 1.1f);
                    last = point;
                }
            }
            string title = (ShowCumulativeDelta ? "CVD " + data.CumulativeDelta.ToString("N0", CultureInfo.InvariantCulture) : "") +
                (ShowVolumeBars ? "  executed volume" : "");
            Text(title, left + 5, top + 3, right - left - 10, 18, Brush(new Rgb(218, 225, 233)), true);
        }

        private void DrawLegend(EngineSnapshot data, RenderScene scene, float left, float top, float right, Rgb buy, Rgb sell)
        {
            const int steps = 32;
            float width = Math.Min(120, (right - left) / 4), x = right - width - 8;
            for (int i = 0; i < steps; i++)
                RenderTarget.FillRectangle(new RectangleF(x + width * i / steps, top + 9, width / steps + 0.3f, 7), Brush(Visualization.Color(i / (double)(steps - 1), Palette)));
            string label = (FilterPersistentLiquidity ? "persistent" : "raw") +
                (SuppressFollowingLiquidity ? " + following filter" : "") + "  0–" + scene.HeatMaximum.ToString("0.#", CultureInfo.InvariantCulture);
            Text(label, x - 35, top + 19, width + 35, 16, Brush(new Rgb(213, 221, 231)), true);
            string title = "FootprintMap" + (ContinuousTimeView ? " · " + zoomSeconds.ToString("0.#", CultureInfo.InvariantCulture) + "s · Ctrl+wheel zoom · Shift+wheel history" : " · native time");
            Text(title, left + 7, top + 5, Math.Max(100, right - left - width - 60), 19, Brush(new Rgb(229, 235, 241)), false);
            if (scene.Bubbles.Length > MaximumRenderedBubbles)
                Text("Bubble render cap: " + MaximumRenderedBubbles + "/" + scene.Bubbles.Length + " (totals preserved)", left + 7, top + 123, right - left - 14, 16, Brush(new Rgb(246, 195, 120)), true);
        }

        private void DrawStatus(EngineSnapshot data, float left, float top, float right, long now)
        {
            long depthClock = Interlocked.Read(ref lastDepthClock);
            string note, calendarWarning;
            lock (engineGate) { note = connectionNote; calendarWarning = sessionWarning; }
            double age = depthClock == 0 ? double.PositiveInfinity : (now - depthClock) / (double)Stopwatch.Frequency;
            string health = !data.Connected ? "DISCONNECTED" : !data.HasDepth ? "WAITING FOR LEVEL II" :
                age > StaleDepthSeconds ? "No recent depth updates / replay may be paused" : "Depth captured";
            string details = health + " · " + data.Bids.Length + "/" + data.Asks.Length + " levels · " +
                data.TradeEvents.ToString("N0", CultureInfo.InvariantCulture) + " trades";
            if (data.InvalidEvents > 0 || data.LateEvents > 0) details += " · invalid " + data.InvalidEvents + ", late " + data.LateEvents;
            if (data.DroppedFrames > 0 || data.DroppedTrades > 0) details += " · retained history trimmed";
            Text(details, left + 7, top + 29, right - left - 14, 18,
                Brush(data.HasDepth && data.Connected ? new Rgb(184, 201, 212) : new Rgb(246, 195, 120)), true);
            if (profileMarginWarning)
                Text("Increase chart right margin to at least " + (((ShowCurrentBook ? 1 : 0) + (ShowSessionProfile ? 1 : 0) + (ShowVisibleProfile ? 1 : 0)) * ProfileWidth + 30) + " px so profiles do not obscure the live edge.",
                    left + 7, top + 67, right - left - 14, 32, Brush(new Rgb(246, 195, 120)), true);
            else if (!data.HasDepth || !data.Connected)
                Text(data.Status + " · " + note, left + 7, top + 67, right - left - 14, 34, Brush(new Rgb(246, 195, 120)), true);
            else if (calendarWarning != null)
                Text(calendarWarning, left + 7, top + 67, right - left - 14, 34, Brush(new Rgb(246, 195, 120)), true);
            else
            {
                string coverage = data.Frames.Length == 0 ? "Depth recording started" :
                    "Captured from " + new DateTime(data.Frames[0].FromTicks).ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                coverage += " · " + historyNote;
                if (ShowCurrentDepthProjection) coverage += " · shaded right of NOW = current book";
                Text(coverage, left + 7, top + 68, right - left - 14, 30, Brush(new Rgb(159, 179, 193)), true);
            }
        }

        private void Text(string value, float x, float y, float width, float height, DxBrush brush, bool small, bool centered = false, bool alignRight = false)
        {
            if (width <= 0 || height <= 0) return;
            TextFormat format = small ? smallFormat : textFormat;
            using (TextLayout layout = new TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, value ?? "", format, width, height))
            {
                if (centered) layout.TextAlignment = TextAlignment.Center;
                else if (alignRight) layout.TextAlignment = TextAlignment.Trailing;
                RenderTarget.DrawTextLayout(new Vector2(x, y), layout, brush, DrawTextOptions.Clip);
            }
        }

        private void SetDefaults()
        {
            FrameMilliseconds = 250;
            PersistenceMilliseconds = 1250;
            RetentionMinutes = 30;
            MaximumFrames = 7200;
            MaximumTrades = 200000;
            MaximumDepthLevels = 200;
            MaximumProfileLevels = 20000;
            RefreshMilliseconds = 150;
            ReplayRewindSeconds = 5;
            ResetAtSession = true;
            DataConnectionName = "";
            StaleDepthSeconds = 30;
            ShowHeatmap = true;
            FilterPersistentLiquidity = false;
            SuppressFollowingLiquidity = false;
            FollowingMinimumMoves = 3;
            FollowingWindowMilliseconds = 10000;
            FollowingSizeRatio = 3;
            FollowingSizeTolerance = 0.35;
            MinimumLiquidity = 1;
            TopLiquidityPercent = 100;
            AutomaticContrast = true;
            ContrastPercentile = 97;
            ManualHeatMaximum = 200;
            HeatGamma = 1;
            Palette = HeatPalette.Classic;
            HeatColumnPixels = 2;
            HeatOpacity = 0.95;
            OpaqueBackground = true;
            CoverPriceBars = true;
            ShowPriceGrid = true;
            ShowBestBidAsk = true;
            ShowVwap = true;
            BackgroundHex = "#1B292F";
            BuyColorHex = "#2ACB8B";
            SellColorHex = "#E15348";
            UnknownColorHex = "#AEB8C4";
            QuoteLineWidth = 1.2f;
            ShowBubbles = true;
            Clustering = ClusterMode.Smart;
            ClusterMilliseconds = 100;
            ClusterVolume = 100;
            SmartClusterPixels = 8;
            MinimumTradeSize = 1;
            MinimumBubbleVolume = 1;
            DeltaBubbles = false;
            BubbleScale = 5;
            MaximumBubbleRadius = 35;
            BubbleColors = BubbleColorMode.Gradient;
            BubbleOpacity = 0.8;
            ThreeDimensionalBubbles = true;
            BubbleOutline = true;
            BubbleLabels = false;
            MaximumRenderedBubbles = 10000;
            ShowCurrentBook = true;
            ShowCobBars = true;
            CobLabels = true;
            CobBids = true;
            CobAsks = true;
            CobCumulative = false;
            CobPercentages = false;
            CobBidRows = 0;
            CobAskRows = 0;
            CobAlignRight = false;
            ShowSessionProfile = true;
            ShowVisibleProfile = false;
            ProfileWidth = 100;
            ShowProfileBars = true;
            ProfileLabels = true;
            ProfileDelta = false;
            ProfileAlignRight = false;
            ShowCumulativeDelta = true;
            ShowVolumeBars = true;
            LowerPanelHeight = 80;
            ShowLegend = true;
            ShowStatus = true;
            SaveCapturedHistory = true;
            ContinuousTimeView = true;
            VisibleSeconds = 120;
            HistoryOffsetSeconds = 0;
            ShowCurrentPrice = true;
            ShowCurrentDepthProjection = true;
            StudyScope = ProfileScope.Session;
            ShowPoc = true;
            ShowValueArea = true;
            ValueAreaPercent = 70;
            ShowVwapBands = false;
            VwapBandDeviations = 1;
            ShowImbalance = true;
            ImbalanceBookLevels = 10;
            BookDistanceDecay = 0;
            ShowLargeLiquidity = false;
            LargeLiquidityMinimum = 100;
            ShowFootprint = false;
            FootprintSeconds = 10;
            FootprintGroupTicks = 1;
            FootprintImbalanceRatio = 3;
            FootprintMinimumVolume = 10;
            ShowTpoProfile = false;
            TpoBracketMinutes = 30;
            TpoGroupTicks = 1;
            ShowAbsorptionMarkers = true;
            AbsorptionMinimumVolume = 100;
            AbsorptionWindowMilliseconds = 2000;
            ShowSweepMarkers = true;
            SweepMinimumVolume = 50;
            SweepWindowMilliseconds = 500;
            SweepMinimumLevels = 3;
            ShowLargePrintMarkers = true;
            LargePrintMinimumVolume = 50;
            ShowLiquidityChanges = false;
            LiquidityChangeMinimum = 100;
            ShowReplenishmentCandidates = false;
            ReplenishmentMinimumVolume = 100;
            ReplenishmentMinimumFraction = 0.75;
            MaximumFlowMarkers = 500;
            FlowMarkerLabels = true;
        }

        #region User settings
        [NinjaScriptProperty]
        [Range(25, 5000)]
        [Display(Name = "Heat frame duration (ms)", Description = "Duration-weighted sampling resolution. Lower values increase memory and processing.", GroupName = "1. Capture", Order = 1)]
        public int FrameMilliseconds { get; set; }

        [NinjaScriptProperty]
        [Range(0, 60000)]
        [Display(Name = "Level persistence (ms)", Description = "Aggregate quantity must remain at a fixed price for this time. This is not individual-order age.", GroupName = "1. Capture", Order = 2)]
        public int PersistenceMilliseconds { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1440)]
        [Display(Name = "History retention (minutes)", Description = "Recorded heat/trades only; earlier unrecorded Level II cannot be recovered.", GroupName = "1. Capture", Order = 3)]
        public int RetentionMinutes { get; set; }

        [NinjaScriptProperty]
        [Range(10, 20000)]
        [Display(Name = "Maximum heat frames", Description = "Count cap can shorten configured time retention.", GroupName = "1. Capture", Order = 4)]
        public int MaximumFrames { get; set; }

        [NinjaScriptProperty]
        [Range(100, 1000000)]
        [Display(Name = "Maximum retained trades", Description = "Count cap affects historical dots/CVP, not accepted session totals.", GroupName = "1. Capture", Order = 5)]
        public int MaximumTrades { get; set; }

        [NinjaScriptProperty]
        [Range(1, 2000)]
        [Display(Name = "Maximum depth levels per side", Description = "Uses only the depth actually supplied by the connection.", GroupName = "1. Capture", Order = 6)]
        public int MaximumDepthLevels { get; set; }

        [NinjaScriptProperty]
        [Range(100, 100000)]
        [Display(Name = "Maximum session profile prices", Description = "New prices are rejected with an explicit status if this budget is exhausted.", GroupName = "1. Capture", Order = 7)]
        public int MaximumProfileLevels { get; set; }

        [NinjaScriptProperty]
        [Range(50, 5000)]
        [Display(Name = "Refresh interval (ms)", Description = "Market data ingestion is independent of visual refresh.", GroupName = "1. Capture", Order = 8)]
        public int RefreshMilliseconds { get; set; }

        [NinjaScriptProperty]
        [Range(1, 300)]
        [Display(Name = "Clock rewind reset threshold (seconds)", Description = "A larger backwards event-time jump resets recorded state; shorter late arrivals are counted.", GroupName = "1. Capture", Order = 9)]
        public int ReplayRewindSeconds { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Reset profile and CVD each session", Description = "Uses the chart Trading Hours template and event timestamps, before recording the first execution.", GroupName = "1. Capture", Order = 10)]
        public bool ResetAtSession { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Data connection name (optional)", Description = "Blank watches all price connections conservatively; enter the exact configured name to isolate one feed.", GroupName = "1. Capture", Order = 11)]
        public string DataConnectionName { get; set; }

        [NinjaScriptProperty]
        [Range(1, 3600)]
        [Display(Name = "Depth inactivity warning (seconds)", Description = "An inactivity warning does not declare a disconnected feed or invent new heat.", GroupName = "1. Capture", Order = 12)]
        public int StaleDepthSeconds { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show heatmap", Description = "Recorded resting liquidity, distinct from executed volume.", GroupName = "2. Heatmap", Order = 1)]
        public bool ShowHeatmap { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Filter by level persistence", Description = "Independent MBP approximation. Switch off to compare the raw observations.", GroupName = "2. Heatmap", Order = 2)]
        public bool FilterPersistentLiquidity { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000000000)]
        [Display(Name = "Minimum displayed level quantity", Description = "Absolute price-level quantity cutoff, not an individual-order size filter.", GroupName = "2. Heatmap", Order = 3)]
        public double MinimumLiquidity { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 100)]
        [Display(Name = "Keep top liquidity percent", Description = "Percentile selection over visible heat cells. 100 keeps all cells above the minimum.", GroupName = "2. Heatmap", Order = 4)]
        public double TopLiquidityPercent { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Automatic contrast", Description = "Uses a robust visible-window percentile; panning may change the color scale.", GroupName = "2. Heatmap", Order = 5)]
        public bool AutomaticContrast { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Contrast percentile", Description = "Upper color reference among the selected heat cells.", GroupName = "2. Heatmap", Order = 6)]
        public double ContrastPercentile { get; set; }

        [NinjaScriptProperty]
        [Range(0.001, 1000000000)]
        [Display(Name = "Manual heat maximum", Description = "Quantity corresponding to the hottest color when automatic contrast is disabled.", GroupName = "2. Heatmap", Order = 7)]
        public double ManualHeatMaximum { get; set; }

        [NinjaScriptProperty]
        [Range(0.05, 20)]
        [Display(Name = "Heat contrast exponent", Description = "A value above one suppresses low intensities.", GroupName = "2. Heatmap", Order = 8)]
        public double HeatGamma { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Heat palette", Description = "Classic follows dark/blue/pale/yellow/orange/red; RGB stops are an independent approximation.", GroupName = "2. Heatmap", Order = 9)]
        public HeatPalette Palette { get; set; }

        [NinjaScriptProperty]
        [Range(1, 32)]
        [Display(Name = "Heat column width (pixels)", Description = "Rendering aggregation width; retained data is unchanged.", GroupName = "2. Heatmap", Order = 10)]
        public int HeatColumnPixels { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1)]
        [Display(Name = "Heat opacity", Description = "Opacity from zero to one.", GroupName = "2. Heatmap", Order = 11)]
        public double HeatOpacity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Suppress price-following liquidity", Description = "Optional aggregate price-level heuristic. It cannot identify individual bots or reproduce TraderMap Pro's proprietary MBO filter.", GroupName = "2b. Liquidity following", Order = 1)]
        public bool SuppressFollowingLiquidity { get; set; }

        [NinjaScriptProperty]
        [Range(2, 50)]
        [Display(Name = "Following minimum quote moves", Description = "Requires repeated changes in the best price; repeated snapshots at the same best price do not count as moves.", GroupName = "2b. Liquidity following", Order = 2)]
        public int FollowingMinimumMoves { get; set; }

        [NinjaScriptProperty]
        [Range(1000, 600000)]
        [Display(Name = "Following observation window (ms)", Description = "Past observations used by the independent following-pattern heuristic.", GroupName = "2b. Liquidity following", Order = 3)]
        public int FollowingWindowMilliseconds { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Following relative size ratio", Description = "Candidate quantity must be large relative to ordinary observed side liquidity.", GroupName = "2b. Liquidity following", Order = 4)]
        public double FollowingSizeRatio { get; set; }

        [NinjaScriptProperty]
        [Range(0, 0.95)]
        [Display(Name = "Following quantity tolerance", Description = "Allowed relative quantity variation at the same distance from the best quote.", GroupName = "2b. Liquidity following", Order = 5)]
        public double FollowingSizeTolerance { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Fill chart background", Description = "Dark backdrop improves heatmap readability.", GroupName = "3. Chart", Order = 1)]
        public bool OpaqueBackground { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Draw above price bars", Description = "Covers bars through indicator z-order; does not edit chart style or template.", GroupName = "3. Chart", Order = 2)]
        public bool CoverPriceBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show price grid", Description = "Subtle horizontal price guides.", GroupName = "3. Chart", Order = 3)]
        public bool ShowPriceGrid { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show raw best bid and ask", Description = "Raw observed BBO remains separate from liquidity filtering.", GroupName = "3. Chart", Order = 4)]
        public bool ShowBestBidAsk { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show captured VWAP", Description = "Uses the selected study scope and accepted captured executions.", GroupName = "3. Chart", Order = 5)]
        public bool ShowVwap { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Background RGB", Description = "Six-digit hexadecimal RGB.", GroupName = "3. Chart", Order = 6)]
        public string BackgroundHex { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Buyer / bid RGB", Description = "Six-digit hexadecimal RGB.", GroupName = "3. Chart", Order = 7)]
        public string BuyColorHex { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Seller / ask RGB", Description = "Six-digit hexadecimal RGB.", GroupName = "3. Chart", Order = 8)]
        public string SellColorHex { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Unknown aggressor RGB", Description = "Inside-spread, unavailable, locked or crossed quotes retain unknown classification.", GroupName = "3. Chart", Order = 9)]
        public string UnknownColorHex { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 5)]
        [Display(Name = "Best quote line width", Description = "Line width in device-independent chart pixels.", GroupName = "3. Chart", Order = 10)]
        public float QuoteLineWidth { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show trade bubbles", Description = "Executed volume only; depth reductions never create trade bubbles.", GroupName = "4. Trade bubbles", Order = 1)]
        public bool ShowBubbles { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Bubble clustering", Description = "Smart is an original pixel-grid approximation; volume mode retains whole closing trades.", GroupName = "4. Trade bubbles", Order = 2)]
        public ClusterMode Clustering { get; set; }

        [NinjaScriptProperty]
        [Range(1, 60000)]
        [Display(Name = "Time cluster duration (ms)", Description = "Used by Time clustering.", GroupName = "4. Trade bubbles", Order = 3)]
        public int ClusterMilliseconds { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000000000)]
        [Display(Name = "Volume cluster size", Description = "Used by Volume clustering; the complete closing trade may exceed the threshold.", GroupName = "4. Trade bubbles", Order = 4)]
        public int ClusterVolume { get; set; }

        [NinjaScriptProperty]
        [Range(1, 128)]
        [Display(Name = "Smart cluster footprint (pixels)", Description = "Groups neighboring time/price display cells. Not Bookmap's proprietary Smart algorithm.", GroupName = "4. Trade bubbles", Order = 5)]
        public int SmartClusterPixels { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000000000)]
        [Display(Name = "Minimum individual execution size", Description = "Applied to original trades before bubble grouping; profiles/CVD retain all accepted trades.", GroupName = "4. Trade bubbles", Order = 6)]
        public long MinimumTradeSize { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000000000)]
        [Display(Name = "Minimum displayed cluster volume", Description = "Applied after grouping; distinct from individual execution size.", GroupName = "4. Trade bubbles", Order = 7)]
        public long MinimumBubbleVolume { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Size bubbles by absolute delta", Description = "Buy minus sell determines magnitude; zero delta is not zero total traded volume.", GroupName = "4. Trade bubbles", Order = 8)]
        public bool DeltaBubbles { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 50)]
        [Display(Name = "Bubble size scale", Description = "Independent square-root area scaling against average cluster volume.", GroupName = "4. Trade bubbles", Order = 9)]
        public double BubbleScale { get; set; }

        [NinjaScriptProperty]
        [Range(1, 150)]
        [Display(Name = "Maximum bubble radius", Description = "Radius cap sacrifices proportionality above the cap.", GroupName = "4. Trade bubbles", Order = 10)]
        public double MaximumBubbleRadius { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Bubble color mode", Description = "Gradient, area-correct split, or buy/sell/unknown pie.", GroupName = "4. Trade bubbles", Order = 11)]
        public BubbleColorMode BubbleColors { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1)]
        [Display(Name = "Bubble opacity", Description = "Opacity from zero to one.", GroupName = "4. Trade bubbles", Order = 12)]
        public double BubbleOpacity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "3D bubble highlight", Description = "Decorative shading; volume radius is unchanged.", GroupName = "4. Trade bubbles", Order = 13)]
        public bool ThreeDimensionalBubbles { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Bubble outline", Description = "Thin outline improves overlap readability.", GroupName = "4. Trade bubbles", Order = 14)]
        public bool BubbleOutline { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Bubble quantity labels", Description = "Only displayed when the bubble has enough space.", GroupName = "4. Trade bubbles", Order = 15)]
        public bool BubbleLabels { get; set; }

        [NinjaScriptProperty]
        [Range(100, 100000)]
        [Display(Name = "Maximum rendered bubbles", Description = "Largest clusters rendered first; a visible warning identifies omitted display marks, totals preserved.", GroupName = "4. Trade bubbles", Order = 16)]
        public int MaximumRenderedBubbles { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show current order book", Description = "Raw current quantities; filtered heatmap does not alter actual COB.", GroupName = "5. Profiles", Order = 1)]
        public bool ShowCurrentBook { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show recorded-session profile", Description = "Accepted executions since capture/session reset.", GroupName = "5. Profiles", Order = 2)]
        public bool ShowSessionProfile { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show visible-range profile", Description = "All retained accepted trades in the visible time/price range, independent of bubble filters.", GroupName = "5. Profiles", Order = 3)]
        public bool ShowVisibleProfile { get; set; }

        [NinjaScriptProperty]
        [Range(45, 250)]
        [Display(Name = "Profile column width", Description = "Reserve at least total enabled profile width plus 30 pixels in chart right margin.", GroupName = "5. Profiles", Order = 4)]
        public int ProfileWidth { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Profile quantity labels", Description = "Shows numbers in session, visible and TPO profiles when rows have enough height. Independent of COB numbers and profile bars.", GroupName = "5. Profiles", Order = 5)]
        public bool ProfileLabels { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Profile buy minus sell delta", Description = "Does not change the raw current order-book column.", GroupName = "5. Profiles", Order = 6)]
        public bool ProfileDelta { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show profile bars", Description = "Shows bars in session, visible and TPO profiles. Quantity labels can remain visible without bars.", GroupName = "5. Profiles", Order = 7)]
        public bool ShowProfileBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Align profiles to right", Description = "Anchors profile bars and quantity labels at the right edge of each column. Off uses the left edge.", GroupName = "5. Profiles", Order = 8)]
        public bool ProfileAlignRight { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show COB bars", Description = "Shows current order-book bars. COB numbers can remain visible without bars.", GroupName = "5a. Current order book", Order = 1)]
        public bool ShowCobBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show COB numbers", Description = "Shows quantities or percentages when price rows have enough height. Independent of profile labels and COB bars.", GroupName = "5a. Current order book", Order = 2)]
        public bool CobLabels { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show COB bids", Description = "Shows supplied bid rows in the current order-book column.", GroupName = "5a. Current order book", Order = 3)]
        public bool CobBids { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show COB asks", Description = "Shows supplied ask rows in the current order-book column.", GroupName = "5a. Current order book", Order = 4)]
        public bool CobAsks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Cumulative COB quantities", Description = "Adds observed quantities from the best quote outward, separately for each side. Only selected supplied rows contribute.", GroupName = "5a. Current order book", Order = 5)]
        public bool CobCumulative { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "COB percentage labels", Description = "Replaces quantity labels with each row's share of its side's selected total. With cumulative quantities, shows the cumulative share. Bar widths still represent quantities.", GroupName = "5a. Current order book", Order = 6)]
        public bool CobPercentages { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2000)]
        [Display(Name = "Maximum COB bid rows", Description = "Nearest supplied bid rows shown; zero includes all captured rows. Does not change capture, heat, or provider depth limits.", GroupName = "5a. Current order book", Order = 7)]
        public int CobBidRows { get; set; }

        [NinjaScriptProperty]
        [Range(0, 2000)]
        [Display(Name = "Maximum COB ask rows", Description = "Nearest supplied ask rows shown; zero includes all captured rows. Does not change capture, heat, or provider depth limits.", GroupName = "5a. Current order book", Order = 8)]
        public int CobAskRows { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Align COB to right", Description = "Anchors current order-book bars and numbers at the column's right edge. Off uses the left edge.", GroupName = "5a. Current order book", Order = 9)]
        public bool CobAlignRight { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show cumulative delta", Description = "Accepted classified buy volume minus sell volume; unknown volume contributes zero delta.", GroupName = "6. Overlays", Order = 1)]
        public bool ShowCumulativeDelta { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show executed volume bars", Description = "Includes buy, sell and unknown prints; independent of bubble filters.", GroupName = "6. Overlays", Order = 2)]
        public bool ShowVolumeBars { get; set; }

        [NinjaScriptProperty]
        [Range(30, 300)]
        [Display(Name = "Lower overlay height", Description = "Overlay inside the price panel; does not create a separate NinjaTrader panel.", GroupName = "6. Overlays", Order = 3)]
        public int LowerPanelHeight { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show quantity legend", Description = "Shows the current heat scaling and active filter.", GroupName = "6. Overlays", Order = 4)]
        public bool ShowLegend { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show data health", Description = "Shows missing Level II, invalid events, trimmed history and feed status.", GroupName = "6. Overlays", Order = 5)]
        public bool ShowStatus { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Save and restore captured history", Description = "Writes retained captured heat, trades and session totals every 30 seconds and on removal; stored in UserDataDir/FootprintMapHistory.", GroupName = "1a. Saved capture", Order = 1)]
        public bool SaveCapturedHistory { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Continuous seconds viewport", Description = "Uses its own labeled time axis inside the panel. Native price coordinates remain in use. Ctrl+wheel zooms; Shift+wheel scrolls.", GroupName = "3a. Time and current price", Order = 1)]
        public bool ContinuousTimeView { get; set; }

        [NinjaScriptProperty]
        [Range(5, 86400)]
        [Display(Name = "Visible time window (seconds)", Description = "Initial continuous window. Set 30, 60, 120, or 300 seconds; Ctrl+wheel adjusts while viewing.", GroupName = "3a. Time and current price", Order = 2)]
        public int VisibleSeconds { get; set; }

        [NinjaScriptProperty]
        [Range(0, 86400)]
        [Display(Name = "History offset (seconds)", Description = "Zero follows the latest actual captured event. Positive values show earlier saved capture. Shift+wheel adjusts this offset.", GroupName = "3a. Time and current price", Order = 3)]
        public int HistoryOffsetSeconds { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show last price and current quotes", Description = "Always displays last execution, bid and ask; off-scale last price receives an arrow badge. Recorded last is labeled after restore/disconnect.", GroupName = "3a. Time and current price", Order = 4)]
        public bool ShowCurrentPrice { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Project current depth beyond NOW", Description = "Translucent raw current quantities from the last captured event toward COB; display only, never historical backfill. Stops on disconnect.", GroupName = "3a. Time and current price", Order = 5)]
        public bool ShowCurrentDepthProjection { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Profile study scope", Description = "Session uses the captured session profile; Visible uses retained trades in the visible time window.", GroupName = "7. Profile studies", Order = 1)]
        public ProfileScope StudyScope { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show point of control", Description = "Highest executed-volume price for the selected study scope.", GroupName = "7. Profile studies", Order = 2)]
        public bool ShowPoc { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show value-area high and low", Description = "Contiguous executed-volume value area grown from the point of control.", GroupName = "7. Profile studies", Order = 3)]
        public bool ShowValueArea { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Value-area percent", Description = "Target fraction of captured executed volume.", GroupName = "7. Profile studies", Order = 4)]
        public double ValueAreaPercent { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show VWAP deviation bands", Description = "Captured volume-weighted price standard deviation; uses profile study scope.", GroupName = "7. Profile studies", Order = 5)]
        public bool ShowVwapBands { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10)]
        [Display(Name = "VWAP band standard deviations", Description = "Multiplier for volume-weighted price standard deviation.", GroupName = "7. Profile studies", Order = 6)]
        public double VwapBandDeviations { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show book and volume imbalance", Description = "Signed bid-versus-ask depth and buy-versus-sell executed volume. Unknown aggressors excluded from volume imbalance.", GroupName = "7. Profile studies", Order = 7)]
        public bool ShowImbalance { get; set; }

        [NinjaScriptProperty]
        [Range(1, 2000)]
        [Display(Name = "Book imbalance levels per side", Description = "Uses only actual supplied levels nearest the market.", GroupName = "7. Profile studies", Order = 8)]
        public int ImbalanceBookLevels { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Book distance decay", Description = "Zero weights levels equally; larger values reduce weight with distance in ticks from best quote.", GroupName = "7. Profile studies", Order = 9)]
        public double BookDistanceDecay { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Highlight large current levels", Description = "Shows large displayed MBP quantities; this does not identify individual orders.", GroupName = "7. Profile studies", Order = 10)]
        public bool ShowLargeLiquidity { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000000000)]
        [Display(Name = "Large current level quantity", Description = "Absolute displayed quantity threshold; tune to contract and feed.", GroupName = "7. Profile studies", Order = 11)]
        public long LargeLiquidityMinimum { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show bid x ask footprint", Description = "Aggregates captured executions into fixed time/price cells; sell x buy with unknown volume shown separately. Labels need sufficient zoom.", GroupName = "8. Footprint and TPO", Order = 1)]
        public bool ShowFootprint { get; set; }

        [NinjaScriptProperty]
        [Range(1, 3600)]
        [Display(Name = "Footprint interval (seconds)", Description = "Independent aggregation period for footprint cells; works with the continuous seconds viewport.", GroupName = "8. Footprint and TPO", Order = 2)]
        public int FootprintSeconds { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000000)]
        [Display(Name = "Footprint ticks per row", Description = "Groups actual instrument ticks into footprint price rows.", GroupName = "8. Footprint and TPO", Order = 3)]
        public int FootprintGroupTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000000)]
        [Display(Name = "Diagonal imbalance ratio", Description = "Compares aggressive buy volume to adjacent lower sell volume, and sell to adjacent higher buy volume.", GroupName = "8. Footprint and TPO", Order = 4)]
        public double FootprintImbalanceRatio { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000000000)]
        [Display(Name = "Diagonal imbalance minimum volume", Description = "Minimum executed volume on the qualifying side.", GroupName = "8. Footprint and TPO", Order = 5)]
        public long FootprintMinimumVolume { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show captured TPO column", Description = "Counts time brackets containing actual captured prints at each grouped price. Untraded prices between prints are not inferred.", GroupName = "8. Footprint and TPO", Order = 6)]
        public bool ShowTpoProfile { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1440)]
        [Display(Name = "TPO time bracket (minutes)", Description = "Fixed event-time bracket duration. Only retained visible executions contribute.", GroupName = "8. Footprint and TPO", Order = 7)]
        public int TpoBracketMinutes { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000000)]
        [Display(Name = "TPO ticks per row", Description = "Groups actual instrument ticks into TPO price rows.", GroupName = "8. Footprint and TPO", Order = 8)]
        public int TpoGroupTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show absorption candidates (AB)", Description = "Original repeated same-price aggressive execution heuristic. Passive side color; does not establish hidden liquidity.", GroupName = "9. Order flow studies", Order = 1)]
        public bool ShowAbsorptionMarkers { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000000000)]
        [Display(Name = "Absorption minimum volume", Description = "Executed quantity threshold inside the observation window.", GroupName = "9. Order flow studies", Order = 2)]
        public long AbsorptionMinimumVolume { get; set; }

        [NinjaScriptProperty]
        [Range(1, 600000)]
        [Display(Name = "Absorption window (ms)", Description = "Observation window for the original absorption heuristic.", GroupName = "9. Order flow studies", Order = 3)]
        public int AbsorptionWindowMilliseconds { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show sweep candidates (SW)", Description = "Original short-window sequence of aggressive prints across multiple prices; not identified stop orders.", GroupName = "9. Order flow studies", Order = 4)]
        public bool ShowSweepMarkers { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000000000)]
        [Display(Name = "Sweep minimum volume", Description = "Total aggressive volume required for a multi-price candidate.", GroupName = "9. Order flow studies", Order = 5)]
        public long SweepMinimumVolume { get; set; }

        [NinjaScriptProperty]
        [Range(1, 600000)]
        [Display(Name = "Sweep window (ms)", Description = "Maximum observation window for a candidate aggressive multi-price move.", GroupName = "9. Order flow studies", Order = 6)]
        public int SweepWindowMilliseconds { get; set; }

        [NinjaScriptProperty]
        [Range(2, 10000)]
        [Display(Name = "Sweep minimum price levels", Description = "Required distinct observed execution levels.", GroupName = "9. Order flow studies", Order = 7)]
        public int SweepMinimumLevels { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show large prints (LP)", Description = "Marks individual captured executions at or above the threshold. Independent of displayed bubble grouping.", GroupName = "9. Order flow studies", Order = 8)]
        public bool ShowLargePrintMarkers { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000000000)]
        [Display(Name = "Large print minimum volume", Description = "Absolute executed quantity threshold; tune to the selected contract.", GroupName = "9. Order flow studies", Order = 9)]
        public long LargePrintMinimumVolume { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show sampled liquidity changes (+L/-L)", Description = "Changes between captured duration-weighted depth frames; aggregate observations cannot distinguish cancels from executions or identify orders.", GroupName = "9. Order flow studies", Order = 10)]
        public bool ShowLiquidityChanges { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000000000)]
        [Display(Name = "Liquidity change quantity", Description = "Minimum absolute frame-to-frame quantity change for a marker.", GroupName = "9. Order flow studies", Order = 11)]
        public double LiquidityChangeMinimum { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show MBP replenishment candidates (RP?)", Description = "Executed volume with substantial sampled displayed liquidity remaining. Original aggregate heuristic, not native iceberg or MBO detection.", GroupName = "9. Order flow studies", Order = 12)]
        public bool ShowReplenishmentCandidates { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000000000)]
        [Display(Name = "Replenishment minimum executed volume", Description = "Captured aggressive volume needed for a same-price replenishment candidate.", GroupName = "9. Order flow studies", Order = 13)]
        public long ReplenishmentMinimumVolume { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 1)]
        [Display(Name = "Replenishment displayed fraction", Description = "Minimum remaining displayed quantity relative to the prior sampled frame.", GroupName = "9. Order flow studies", Order = 14)]
        public double ReplenishmentMinimumFraction { get; set; }

        [NinjaScriptProperty]
        [Range(10, 10000)]
        [Display(Name = "Maximum flow markers", Description = "Bounds study output; truncation appears in the study status.", GroupName = "9. Order flow studies", Order = 15)]
        public int MaximumFlowMarkers { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show flow marker labels", Description = "AB absorption candidate, SW sweep candidate, LP large print, +L/-L sampled depth change, RP? replenishment candidate.", GroupName = "9. Order flow studies", Order = 16)]
        public bool FlowMarkerLabels { get; set; }

        #endregion
    }
}
