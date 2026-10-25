#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
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
#endregion

// Original implementation. No Bookmap or NinjaTrader proprietary binaries are included.
// Copy alongside all src/Footprint.Core/*.cs files; see docs/INSTALL-NINJATRADER.md.
namespace NinjaTrader.NinjaScript.Indicators
{
    public class FootprintMap : Indicator
    {
        private readonly object engineGate = new object();
        private readonly object renderGate = new object();
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
                    cachedSnapshot = null;
                }
            }
            else if (State == State.Historical)
            {
                SetZOrder(CoverPriceBars ? int.MaxValue : -1);
                StartRepaintTimer();
            }
            else if (State == State.Terminated)
            {
                lock (engineGate) { terminated = true; engine = null; cachedSnapshot = null; }
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
            lock (engineGate)
            {
                if (engine == null || terminated) return;
                if (e.IsReset)
                {
                    long resetTicks = Math.Max(lastEventTicks, e.Time.Ticks);
                    engine.ResetDepth(new DateTime(resetTicks), "Instrument depth reset; waiting for fresh depth");
                    lastDepthClock = 0;
                    cachedSnapshot = null;
                    return;
                }
                if (!PrepareEvent(e.Time)) return;
                if (e.MarketDataType == MarketDataType.Bid || e.MarketDataType == MarketDataType.Ask)
                {
                    DepthMutation mutation;
                    if (e.Operation == Operation.Add) mutation = DepthMutation.Insert;
                    else if (e.Operation == Operation.Update) mutation = DepthMutation.Update;
                    else if (e.Operation == Operation.Remove) mutation = DepthMutation.Remove;
                    else return;
                    engine.Depth(e.Time, e.MarketDataType == MarketDataType.Bid ? BookSide.Bid : BookSide.Ask,
                        mutation, e.Position, e.Price, e.Volume);
                    lastDepthClock = Stopwatch.GetTimestamp();
                }
            }
            RequestRepaint();
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
                    cachedSnapshot = null;
                    return;
                }
                if (!PrepareEvent(e.Time)) return;
                if (e.MarketDataType == MarketDataType.Last)
                {
                    engine.Trade(e.Time, e.Price, e.Volume, e.Bid, e.Ask);
                    lastTradeClock = Stopwatch.GetTimestamp();
                }
                else if (e.MarketDataType == MarketDataType.Bid || e.MarketDataType == MarketDataType.Ask)
                    engine.Advance(e.Time);
            }
            RequestRepaint();
        }

        // Session detection runs on market timestamps, before the first execution is counted.
        // OnBarUpdate/OnMarketData ordering cannot erase the first trade of a new session.
        protected override void OnBarUpdate() { }

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
            engine.StartSession(time);
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
            if (ChartControl == null) return;
            ChartControl.Dispatcher.InvokeAsync(() =>
            {
                if (terminated || repaintTimer != null) return;
                repaintTimer = new DispatcherTimer(DispatcherPriority.Background, ChartControl.Dispatcher);
                repaintTimer.Interval = TimeSpan.FromMilliseconds(250);
                repaintTimer.Tick += RepaintTick;
                repaintTimer.Start();
            });
        }

        private void RepaintTick(object sender, EventArgs e)
        {
            // UI refresh only. Wall time must never extend recorded heat during a playback pause or outage.
            if (!terminated) ForceRefresh();
        }

        private void StopRepaintTimer()
        {
            DispatcherTimer timer = repaintTimer;
            repaintTimer = null;
            if (timer == null) return;
            if (timer.Dispatcher.CheckAccess()) { timer.Stop(); timer.Tick -= RepaintTick; }
            else timer.Dispatcher.InvokeAsync(() => { timer.Stop(); timer.Tick -= RepaintTick; });
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
                sphereHighlight = new RadialGradientBrush(RenderTarget,
                    new RadialGradientBrushProperties { Center = new Vector2(0, 0), RadiusX = 1, RadiusY = 1 }, sphereStops);
            }
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
            int columns = (ShowCurrentBook ? 1 : 0) + (ShowSessionProfile ? 1 : 0) + (ShowVisibleProfile ? 1 : 0);
            float right = ChartPanel.X + ChartPanel.W - columns * ProfileWidth;
            if (right - left < 80 || bottom - top < 80) return;
            DateTime from, to;
            if (!BuildTimeMap(chartControl, left, right, out from, out to))
            {
                EnsureRenderResources();
                Text("FootprintMap requires a minute/second chart with at least two visible time bars.",
                    left + 8, top + 8, right - left - 16, 42, Brush(new Rgb(246, 195, 120)), false);
                return;
            }
            if (from.Ticks <= 0 || to <= from || chartScale.MaxValue <= chartScale.MinValue) return;
            Viewport viewport = new Viewport(from.Ticks, to.Ticks,
                chartScale.MinValue / instrumentTickSize, chartScale.MaxValue / instrumentTickSize,
                (int)(right - left), (int)(bottom - top));
            EngineSnapshot snapshot;
            long generation;
            long now = Stopwatch.GetTimestamp();
            lock (engineGate)
            {
                if (engine == null) return;
                if (cachedSnapshot == null || (now - snapshotClock) * 1000.0 / Stopwatch.Frequency >= RefreshMilliseconds)
                {
                    cachedSnapshot = engine.Snapshot();
                    snapshotClock = now;
                }
                snapshot = cachedSnapshot;
                generation = engineGeneration;
            }
            try
            {
                EnsureRenderResources();
                if (cachedScene == null || !SameViewport(cachedViewport, viewport) || sceneVersion != snapshot.Version || sceneGeneration != generation)
                {
                    cachedScene = Visualization.Build(snapshot, viewport, DisplayOptions());
                    cachedViewport = viewport;
                    sceneVersion = snapshot.Version;
                    sceneGeneration = generation;
                }
                profileMarginWarning = columns > 0 && snapshot.TimeTicks > 0 && TimeX(snapshot.TimeTicks) > right;
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
                        if (ShowVwap && snapshot.SessionVolume > 0 && !double.IsNaN(snapshot.SessionVwap))
                        {
                            float y = chartScale.GetYByValue(snapshot.SessionVwap);
                            RenderTarget.DrawLine(new Vector2(left, y), new Vector2(right, y), Brush(new Rgb(235, 223, 166), 0.8), 1f);
                            Text("VWAP", left + 4, y - 15, 60, 14, Brush(new Rgb(235, 223, 166)), true);
                        }
                        if (ShowBubbles) DrawBubbles(chartControl, chartScale, cachedScene.Bubbles, buy, sell, unknown);
                        if (ShowCumulativeDelta || ShowVolumeBars) DrawLowerPanel(chartControl, snapshot, from.Ticks, to.Ticks, left, right, bottom, buy, sell, background);
                    }
                    finally { RenderTarget.PopAxisAlignedClip(); }
                    DrawProfiles(chartScale, snapshot, cachedScene, right, top, bottom, buy, sell, unknown, background);
                    if (ShowLegend) DrawLegend(snapshot, cachedScene, left, top, right, buy, sell);
                    if (ShowStatus) DrawStatus(snapshot, left, top, right, now);
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
                PanelHeader("COB", x, top, bottom, background);
                double max = 1;
                foreach (DepthLevel level in data.Bids) max = Math.Max(max, level.Quantity);
                foreach (DepthLevel level in data.Asks) max = Math.Max(max, level.Quantity);
                foreach (DepthLevel level in data.Bids) DepthBar(scale, level, x, max, buy);
                foreach (DepthLevel level in data.Asks) DepthBar(scale, level, x, max, sell);
                x += ProfileWidth;
            }
            if (ShowSessionProfile)
            {
                PanelHeader("SVP · recorded", x, top, bottom, background);
                ProfileBars(scale, data.Profile, x, buy, sell, unknown);
                x += ProfileWidth;
            }
            if (ShowVisibleProfile)
            {
                PanelHeader("CVP · visible", x, top, bottom, background);
                ProfileBars(scale, scene.VisibleProfile, x, buy, sell, unknown);
            }
        }

        private void PanelHeader(string label, float x, float top, float bottom, Rgb background)
        {
            RenderTarget.FillRectangle(new RectangleF(x, top, ProfileWidth, bottom - top), Brush(background));
            RenderTarget.DrawLine(new Vector2(x, top), new Vector2(x, bottom), Brush(new Rgb(82, 92, 106)), 1);
            Text(label, x + 4, top + 4, ProfileWidth - 8, 20, Brush(new Rgb(213, 221, 231)), true);
        }

        private void DepthBar(ChartScale scale, DepthLevel level, float x, double max, Rgb color)
        {
            float y0 = scale.GetYByValue((level.Tick + 0.5) * instrumentTickSize), y1 = scale.GetYByValue((level.Tick - 0.5) * instrumentTickSize);
            float height = Math.Max(1, y1 - y0 - 0.4f), width = (float)(level.Quantity / max * (ProfileWidth - 8));
            RenderTarget.FillRectangle(new RectangleF(x + 3, y0, width, height), Brush(color, 0.8));
            if (ProfileLabels && height >= 11) Text(level.Quantity.ToString(CultureInfo.InvariantCulture), x + 4, y0, ProfileWidth - 8, height, Brush(new Rgb(235, 241, 245)), true);
        }

        private void ProfileBars(ChartScale scale, ProfileLevel[] profile, float x, Rgb buy, Rgb sell, Rgb unknown)
        {
            double max = 1;
            foreach (ProfileLevel level in profile) max = Math.Max(max, ProfileDelta ? Math.Abs((double)(level.Buy - level.Sell)) : level.Total);
            foreach (ProfileLevel level in profile)
            {
                float y0 = scale.GetYByValue((level.Tick + 0.5) * instrumentTickSize), y1 = scale.GetYByValue((level.Tick - 0.5) * instrumentTickSize);
                float height = Math.Max(1, y1 - y0 - 0.4f), cursor = x + 3;
                if (ProfileDelta)
                {
                    double delta = level.Buy - level.Sell;
                    RenderTarget.FillRectangle(new RectangleF(cursor, y0, (float)(Math.Abs(delta) / max * (ProfileWidth - 8)), height), Brush(delta >= 0 ? buy : sell, 0.8));
                }
                else
                {
                    float sellWidth = (float)(level.Sell / max * (ProfileWidth - 8));
                    float buyWidth = (float)(level.Buy / max * (ProfileWidth - 8));
                    float unknownWidth = (float)(level.Unknown / max * (ProfileWidth - 8));
                    RenderTarget.FillRectangle(new RectangleF(cursor, y0, sellWidth, height), Brush(sell, 0.8)); cursor += sellWidth;
                    RenderTarget.FillRectangle(new RectangleF(cursor, y0, buyWidth, height), Brush(buy, 0.8)); cursor += buyWidth;
                    RenderTarget.FillRectangle(new RectangleF(cursor, y0, unknownWidth, height), Brush(unknown, 0.8));
                }
                if (ProfileLabels && height >= 11)
                    Text((ProfileDelta ? level.Buy - level.Sell : level.Total).ToString(CultureInfo.InvariantCulture), x + 4, y0, ProfileWidth - 8, height, Brush(new Rgb(235, 241, 245)), true);
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
            Text("FootprintMap", left + 7, top + 5, Math.Max(100, right - left - width - 60), 19, Brush(new Rgb(229, 235, 241)), false);
            if (scene.Bubbles.Length > MaximumRenderedBubbles)
                Text("Bubble render cap: " + MaximumRenderedBubbles + "/" + scene.Bubbles.Length + " (totals preserved)", left + 7, top + 49, right - left - 14, 16, Brush(new Rgb(246, 195, 120)), true);
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
        }

        private void Text(string value, float x, float y, float width, float height, DxBrush brush, bool small, bool centered = false)
        {
            if (width <= 0 || height <= 0) return;
            TextFormat format = small ? smallFormat : textFormat;
            using (TextLayout layout = new TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, value ?? "", format, width, height))
            {
                if (centered) layout.TextAlignment = TextAlignment.Center;
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
            FilterPersistentLiquidity = true;
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
            ShowVwap = false;
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
            ShowSessionProfile = true;
            ShowVisibleProfile = false;
            ProfileWidth = 100;
            ProfileLabels = true;
            ProfileDelta = false;
            ShowCumulativeDelta = true;
            ShowVolumeBars = true;
            LowerPanelHeight = 80;
            ShowLegend = true;
            ShowStatus = true;
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
        [Display(Name = "Show recorded-session VWAP", Description = "Based on accepted trades since capture/session reset, not backfilled session history.", GroupName = "3. Chart", Order = 5)]
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
        [Display(Name = "Profile quantity labels", Description = "Labels appear when vertical space permits.", GroupName = "5. Profiles", Order = 5)]
        public bool ProfileLabels { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Profile buy minus sell delta", Description = "Does not change the raw current order-book column.", GroupName = "5. Profiles", Order = 6)]
        public bool ProfileDelta { get; set; }

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

        #endregion
    }
}
