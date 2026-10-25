using System;
using System.Collections.Generic;

namespace FootprintIndicator.Core
{
    /// <summary>
    /// Deterministic, platform-independent display aggregation. This is an original
    /// renderer model, not Bookmap's proprietary contrast or Smart-clustering code.
    /// All coordinates are relative to the supplied viewport's top-left corner.
    /// </summary>
    public static class Visualization
    {
        // Geometry budgets are explicit. Price bins widen when necessary; their
        // quantities are sums across prices, not an invented single-level size.
        private const int MaximumColumns = 2048;
        private const int MaximumRows = 512;
        private const int MaximumRegularCells = 262144;
        private const int MaximumHeatSegments = 500000;
        private const long MaximumHeatContributions = 12000000;
        private const int MaximumInputTrades = 1000000;
        private static readonly Rgb[] ClassicColors = {
            new Rgb(11, 24, 32), new Rgb(0, 58, 83), new Rgb(0, 117, 163),
            new Rgb(126, 190, 211), new Rgb(238, 241, 205), new Rgb(255, 231, 0),
            new Rgb(255, 127, 0), new Rgb(255, 20, 0)
        };
        private static readonly double[] ClassicPositions = { 0.0, 0.13, 0.30, 0.46, 0.58, 0.69, 0.84, 1.0 };
        private static readonly Rgb[] IceColors = {
            new Rgb(7, 16, 25), new Rgb(0, 67, 120), new Rgb(0, 167, 202), new Rgb(125, 235, 245), new Rgb(245, 255, 255)
        };
        private static readonly double[] IcePositions = { 0.0, 0.25, 0.55, 0.82, 1.0 };

        private sealed class HeatAccumulator
        {
            internal int Row, Epoch;
            internal double From, To, QuantityTime;
        }

        private sealed class TradeAccumulator
        {
            internal long Buy, Sell, Unknown, Total, First, Last, Price;
            internal TradeSide FirstSide;
            internal double MeanPrice, MeanTimeOffset;
            internal readonly long Origin;

            internal TradeAccumulator(long origin) { Origin = origin; }

            internal void Add(TradeRecord trade)
            {
                long next = checked(Total + trade.Volume);
                if (Total == 0)
                {
                    First = Last = trade.TimeTicks;
                    Price = trade.PriceTick;
                    FirstSide = trade.Side;
                    MeanPrice = trade.PriceTick;
                    MeanTimeOffset = trade.TimeTicks - Origin;
                }
                else
                {
                    double weight = (double)trade.Volume / next;
                    MeanPrice += ((double)trade.PriceTick - MeanPrice) * weight;
                    MeanTimeOffset += ((double)(trade.TimeTicks - Origin) - MeanTimeOffset) * weight;
                    if (trade.TimeTicks < First) First = trade.TimeTicks;
                    if (trade.TimeTicks > Last) Last = trade.TimeTicks;
                }
                switch (trade.Side)
                {
                    case TradeSide.Buy: Buy = checked(Buy + trade.Volume); break;
                    case TradeSide.Sell: Sell = checked(Sell + trade.Volume); break;
                    default: Unknown = checked(Unknown + trade.Volume); break;
                }
                Total = next;
            }
        }

        private sealed class ProfileAccumulator
        {
            internal long Buy, Sell, Unknown;
            internal void Add(TradeRecord trade)
            {
                // Validate the combined quantity as well as individual sides.
                checked { long total = Buy + Sell + Unknown + trade.Volume; if (total < 0) throw new OverflowException(); }
                switch (trade.Side)
                {
                    case TradeSide.Buy: Buy = checked(Buy + trade.Volume); break;
                    case TradeSide.Sell: Sell = checked(Sell + trade.Volume); break;
                    default: Unknown = checked(Unknown + trade.Volume); break;
                }
            }
        }

        private struct ClusterKey : IEquatable<ClusterKey>
        {
            internal readonly long Time;
            internal readonly int Row;
            internal ClusterKey(long time, int row) { Time = time; Row = row; }
            public bool Equals(ClusterKey other) { return Time == other.Time && Row == other.Row; }
            public override bool Equals(object value) { return value is ClusterKey && Equals((ClusterKey)value); }
            public override int GetHashCode() { unchecked { return Time.GetHashCode() * 397 ^ Row; } }
        }

        /// <summary>
        /// Build a new scene without mutating data or settings. Trade intervals are
        /// half-open [FromTicks, ToTicks). Invalid inputs and exceeded safety budgets
        /// throw explicitly instead of returning a deceptively incomplete picture.
        /// </summary>
        public static RenderScene Build(EngineSnapshot snapshot, Viewport viewport, DisplaySettings settings)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            Validate(viewport, settings);
            RenderScene scene = new RenderScene();
            BuildHeat(snapshot, viewport, settings, scene);
            BuildTrades(snapshot, viewport, settings, scene);
            return scene;
        }

        private static void Validate(Viewport view, DisplaySettings settings)
        {
            if (view == null) throw new ArgumentNullException("viewport");
            if (settings == null) throw new ArgumentNullException("settings");
            if (view.FromTicks < 0 || view.ToTicks > DateTime.MaxValue.Ticks || view.ToTicks <= view.FromTicks)
                throw new ArgumentOutOfRangeException("viewport", "The time interval must be a positive DateTime interval.");
            if (!Finite(view.MinPriceTick) || !Finite(view.MaxPriceTick) || view.MaxPriceTick <= view.MinPriceTick ||
                !Finite(view.MaxPriceTick - view.MinPriceTick))
                throw new ArgumentOutOfRangeException("viewport", "The price interval must be finite and increasing.");
            if (view.Width < 1 || view.Height < 1 || view.Width > 32768 || view.Height > 32768)
                throw new ArgumentOutOfRangeException("viewport", "Viewport dimensions must be between 1 and 32768 pixels.");
            if (!Finite(settings.MinimumLiquidity) || settings.MinimumLiquidity < 0 ||
                !Finite(settings.TopLiquidityPercent) || settings.TopLiquidityPercent <= 0 || settings.TopLiquidityPercent > 100 ||
                !Finite(settings.ContrastPercentile) || settings.ContrastPercentile <= 0 || settings.ContrastPercentile > 100 ||
                !Finite(settings.ManualMaximum) || settings.ManualMaximum <= 0 ||
                !Finite(settings.Gamma) || settings.Gamma <= 0 || settings.Gamma > 20 ||
                settings.HeatColumnPixels < 1 || settings.HeatColumnPixels > 32768)
                throw new ArgumentOutOfRangeException("settings", "Invalid heatmap scale, threshold or resolution.");
            if (settings.ClusterMilliseconds < 1 || settings.ClusterVolume < 1 || settings.SmartPixels < 1 ||
                settings.SmartPixels > 32768 || settings.MinimumTradeSize < 1 || settings.MinimumBubbleVolume < 1 ||
                !Finite(settings.BubbleScale) || settings.BubbleScale <= 0 || !Finite(settings.MaxBubbleRadius) ||
                settings.MaxBubbleRadius <= 0 || settings.MaxBubbleRadius > 4096 ||
                !Enum.IsDefined(typeof(ClusterMode), settings.Clustering) || !Enum.IsDefined(typeof(HeatPalette), settings.Palette))
                throw new ArgumentOutOfRangeException("settings", "Invalid bubble aggregation, size or color settings.");
        }

        private static void BuildHeat(EngineSnapshot snapshot, Viewport view, DisplaySettings settings, RenderScene scene)
        {
            HeatFrame[] frames = snapshot.Frames ?? new HeatFrame[0];
            // Build from all retained frames, not only visible frames, so panning
            // does not reset causal pattern evidence. Filtering precedes contrast.
            HashSet<HeatCell> followingMask = settings.SuppressFollowingLiquidity
                ? LiquidityNoiseFilter.BuildMask(frames, settings) : null;
            int columnPixels = Math.Max(settings.HeatColumnPixels, (view.Width + MaximumColumns - 1) / MaximumColumns);
            int columns = (view.Width + columnPixels - 1) / columnPixels;
            int rowBudget = Math.Min(MaximumRows, Math.Max(1, MaximumRegularCells / columns));
            double priceRange = view.MaxPriceTick - view.MinPriceTick;
            // A bin is an integer number of ticks, aligned to absolute prices.
            double ticksPerRow = Math.Max(1, Math.Ceiling((priceRange + 1) / rowBudget));
            double firstTick = Math.Floor((view.MinPriceTick + 0.5) / ticksPerRow) * ticksPerRow;
            double rowBottom = firstTick - 0.5;
            double duration = view.ToTicks - view.FromTicks;
            List<HeatAccumulator> parts = new List<HeatAccumulator>();
            Dictionary<long, HeatAccumulator> last = new Dictionary<long, HeatAccumulator>();
            long contributions = 0;

            HeatFrame[] ordered = frames;
            for (int i = 1; i < frames.Length; i++)
            {
                if (frames[i] != null && frames[i - 1] != null && frames[i].FromTicks < frames[i - 1].FromTicks)
                {
                    ordered = (HeatFrame[])frames.Clone();
                    Array.Sort(ordered, delegate(HeatFrame a, HeatFrame b)
                    { if (a == null) return b == null ? 0 : -1; if (b == null) return 1; return a.FromTicks.CompareTo(b.FromTicks); });
                    break;
                }
            }

            long precedingEnd = 0;
            foreach (HeatFrame frame in ordered)
            {
                if (frame == null || frame.ToTicks <= frame.FromTicks || frame.ToTicks <= view.FromTicks || frame.FromTicks >= view.ToTicks)
                    continue;
                // Valid engine frames do not overlap. Malformed snapshots should
                // fail loudly rather than double-count repeated book snapshots.
                if (frame.FromTicks < precedingEnd)
                    throw new ArgumentException("Heat frames overlap in time.", "snapshot");
                precedingEnd = frame.ToTicks;
                double from = Math.Max(frame.FromTicks, view.FromTicks) - view.FromTicks;
                double to = Math.Min(frame.ToTicks, view.ToTicks) - view.FromTicks;
                if (to <= from) continue;
                Dictionary<int, double> rows = new Dictionary<int, double>();
                foreach (HeatCell cell in frame.Cells ?? new HeatCell[0])
                {
                    if (cell == null || (followingMask != null && followingMask.Contains(cell))) continue;
                    double quantity = settings.UsePersistence ? cell.PersistentQuantity : cell.RawQuantity;
                    if (!Finite(quantity) || quantity <= 0) continue;
                    if ((double)cell.Tick + 0.5 <= view.MinPriceTick || (double)cell.Tick - 0.5 >= view.MaxPriceTick) continue;
                    int row = (int)Math.Floor(((double)cell.Tick - firstTick) / ticksPerRow);
                    if (row < 0 || row > rowBudget + 1) continue;
                    double current;
                    rows.TryGetValue(row, out current);
                    double next = current + quantity;
                    if (!Finite(next)) throw new OverflowException("Heatmap price-bin quantity overflowed.");
                    rows[row] = next;
                }

                int firstColumn = Math.Max(0, Math.Min(columns - 1, (int)Math.Floor(from / duration * view.Width / columnPixels)));
                int finalColumn = Math.Max(firstColumn, Math.Min(columns - 1, (int)Math.Ceiling(to / duration * view.Width / columnPixels) - 1));
                for (int column = firstColumn; column <= finalColumn; column++)
                {
                    double columnFrom = (double)(column * columnPixels) / view.Width * duration;
                    double columnTo = (double)Math.Min(view.Width, (column + 1) * columnPixels) / view.Width * duration;
                    double partFrom = Math.Max(from, columnFrom), partTo = Math.Min(to, columnTo);
                    if (partTo <= partFrom) continue;
                    foreach (KeyValuePair<int, double> row in rows)
                    {
                        if (++contributions > MaximumHeatContributions)
                            throw new InvalidOperationException("Heatmap render budget exceeded; reduce retained history or visible range.");
                        long key = (long)row.Key * columns + column;
                        HeatAccumulator accumulator;
                        if (!last.TryGetValue(key, out accumulator) || accumulator.Epoch != frame.Epoch || accumulator.To != partFrom)
                        {
                            if (parts.Count >= MaximumHeatSegments)
                                throw new InvalidOperationException("Heatmap segment budget exceeded; reduce retained history or visible range.");
                            accumulator = new HeatAccumulator { Row = row.Key, Epoch = frame.Epoch, From = partFrom, To = partFrom };
                            last[key] = accumulator;
                            parts.Add(accumulator);
                        }
                        accumulator.QuantityTime += row.Value * (partTo - partFrom);
                        accumulator.To = partTo;
                    }
                }
            }

            List<double> quantities = new List<double>(parts.Count);
            foreach (HeatAccumulator part in parts)
            {
                double value = part.QuantityTime / (part.To - part.From);
                if (Finite(value) && value >= settings.MinimumLiquidity && value > 0) quantities.Add(value);
            }
            if (quantities.Count == 0)
            {
                scene.HeatMaximum = settings.AutomaticContrast ? 1 : settings.ManualMaximum;
                return;
            }
            quantities.Sort();
            double cutoff = Math.Max(settings.MinimumLiquidity, Percentile(quantities, 1 - settings.TopLiquidityPercent / 100));
            int retainedStart = quantities.BinarySearch(cutoff);
            if (retainedStart < 0) retainedStart = ~retainedStart;
            else while (retainedStart > 0 && quantities[retainedStart - 1] == cutoff) retainedStart--;
            double ceiling = settings.AutomaticContrast
                ? Percentile(quantities, settings.ContrastPercentile / 100, retainedStart)
                : settings.ManualMaximum;
            scene.HeatMaximum = Math.Max(ceiling, double.Epsilon);
            List<HeatTile> tiles = new List<HeatTile>(parts.Count);
            foreach (HeatAccumulator part in parts)
            {
                double value = part.QuantityTime / (part.To - part.From);
                if (!Finite(value) || value < cutoff || value <= 0) continue;
                double low = Math.Max(view.MinPriceTick, rowBottom + part.Row * ticksPerRow);
                double high = Math.Min(view.MaxPriceTick, rowBottom + (part.Row + 1) * ticksPerRow);
                double x = part.From / duration * view.Width;
                double right = part.To / duration * view.Width;
                if (high <= low || right <= x) continue;
                double normalized = Math.Pow(Clamp(value / scene.HeatMaximum, 0, 1), settings.Gamma);
                tiles.Add(new HeatTile {
                    X = (float)x, Y = (float)((view.MaxPriceTick - high) / priceRange * view.Height),
                    Width = (float)(right - x), Height = (float)((high - low) / priceRange * view.Height),
                    Quantity = value, Color = Color(normalized, settings.Palette)
                });
            }
            scene.Heat = tiles.ToArray();
        }

        private static void BuildTrades(EngineSnapshot snapshot, Viewport view, DisplaySettings settings, RenderScene scene)
        {
            TradeRecord[] source = snapshot.Trades ?? new TradeRecord[0];
            if (source.Length > MaximumInputTrades)
                throw new InvalidOperationException("Trade render budget exceeded; reduce retained trade count.");
            List<TradeRecord> trades = new List<TradeRecord>();
            Dictionary<long, ProfileAccumulator> profiles = new Dictionary<long, ProfileAccumulator>();
            double priceRange = view.MaxPriceTick - view.MinPriceTick;
            double pricePadding = settings.MaxBubbleRadius / view.Height * priceRange;
            foreach (TradeRecord trade in source)
            {
                if (trade == null || trade.Volume <= 0 || trade.TimeTicks < view.FromTicks || trade.TimeTicks >= view.ToTicks) continue;
                if (trade.PriceTick >= view.MinPriceTick && trade.PriceTick <= view.MaxPriceTick)
                {
                    ProfileAccumulator profile;
                    if (!profiles.TryGetValue(trade.PriceTick, out profile))
                    { profile = new ProfileAccumulator(); profiles.Add(trade.PriceTick, profile); }
                    profile.Add(trade);
                }
                if (trade.Volume >= settings.MinimumTradeSize && trade.PriceTick >= view.MinPriceTick - pricePadding &&
                    trade.PriceTick <= view.MaxPriceTick + pricePadding) trades.Add(trade);
            }
            List<long> prices = new List<long>(profiles.Keys);
            prices.Sort();
            List<ProfileLevel> profileLevels = new List<ProfileLevel>(prices.Count);
            foreach (long price in prices)
            {
                ProfileAccumulator p = profiles[price];
                profileLevels.Add(new ProfileLevel(price, p.Buy, p.Sell, p.Unknown));
            }
            scene.VisibleProfile = profileLevels.ToArray();
            trades.Sort(delegate(TradeRecord a, TradeRecord b)
            {
                int comparison = a.TimeTicks.CompareTo(b.TimeTicks);
                return comparison != 0 ? comparison : a.Sequence.CompareTo(b.Sequence);
            });
            List<TradeAccumulator> groups = new List<TradeAccumulator>();
            Dictionary<ClusterKey, TradeAccumulator> bins = new Dictionary<ClusterKey, TradeAccumulator>();
            TradeAccumulator current = null;
            double duration = view.ToTicks - view.FromTicks;
            long timeStep = (long)settings.ClusterMilliseconds * TimeSpan.TicksPerMillisecond;
            foreach (TradeRecord trade in trades)
            {
                if (settings.Clustering == ClusterMode.Volume || settings.Clustering == ClusterMode.Price || settings.Clustering == ClusterMode.PriceAndSide)
                {
                    bool boundary = current == null;
                    if (!boundary && settings.Clustering == ClusterMode.Volume) boundary = current.Total >= settings.ClusterVolume;
                    if (!boundary && (settings.Clustering == ClusterMode.Price || settings.Clustering == ClusterMode.PriceAndSide))
                        boundary = current.Price != trade.PriceTick || (settings.Clustering == ClusterMode.PriceAndSide && current.FirstSide != trade.Side);
                    if (boundary) { current = new TradeAccumulator(view.FromTicks); groups.Add(current); }
                    // The volume mode keeps the entire closing trade. It never
                    // synthesizes a split execution to hit an exact threshold.
                    current.Add(trade);
                }
                else
                {
                    long timeBin;
                    int priceBin = 0;
                    if (settings.Clustering == ClusterMode.Time) timeBin = trade.TimeTicks / timeStep;
                    else
                    {
                        double x = (double)(trade.TimeTicks - view.FromTicks) / duration * view.Width;
                        int footprint = settings.Clustering == ClusterMode.Smart ? settings.SmartPixels : 1;
                        timeBin = (long)Math.Floor(x / footprint);
                        if (settings.Clustering == ClusterMode.Smart)
                        {
                            double y = (view.MaxPriceTick - trade.PriceTick) / priceRange * view.Height;
                            priceBin = (int)Math.Floor(y / footprint);
                        }
                    }
                    ClusterKey key = new ClusterKey(timeBin, priceBin);
                    TradeAccumulator group;
                    if (!bins.TryGetValue(key, out group))
                    { group = new TradeAccumulator(view.FromTicks); bins.Add(key, group); groups.Add(group); }
                    group.Add(trade);
                }
            }
            // Independent documented approximation: mean cluster volume before
            // display-threshold filtering, not the vendor's unpublished scaler.
            double reference = 0;
            for (int i = 0; i < groups.Count; i++) reference += (groups[i].Total - reference) / (i + 1);
            scene.BubbleReferenceVolume = Math.Max(1, reference);
            List<Bubble> bubbles = new List<Bubble>(groups.Count);
            foreach (TradeAccumulator group in groups)
            {
                if (group.Total < settings.MinimumBubbleVolume) continue;
                long time = view.FromTicks + (long)Math.Round(group.MeanTimeOffset, MidpointRounding.AwayFromZero);
                time = Math.Max(group.First, Math.Min(group.Last, time));
                // Both sides are nonnegative and their sum fits Int64, so their integer
                // difference is safe. Subtract before converting: double(Buy)-double(Sell)
                // can erase a real one-contract imbalance at large cumulative volumes.
                double represented = settings.DeltaBubbles ? Math.Abs((double)(group.Buy - group.Sell)) : group.Total;
                double radius = represented == 0 ? 0 : Math.Min(settings.MaxBubbleRadius,
                    Math.Max(1, settings.BubbleScale * Math.Sqrt(represented / scene.BubbleReferenceVolume)));
                bubbles.Add(new Bubble {
                    X = group.MeanTimeOffset / duration * view.Width,
                    Y = (view.MaxPriceTick - group.MeanPrice) / priceRange * view.Height,
                    TimeTicks = time, PriceTick = group.MeanPrice, FromTicks = group.First, ToTicks = group.Last,
                    Buy = group.Buy, Sell = group.Sell, Unknown = group.Unknown, Radius = radius
                });
            }
            bubbles.Sort(delegate(Bubble a, Bubble b)
            { int comparison = a.TimeTicks.CompareTo(b.TimeTicks); return comparison != 0 ? comparison : a.PriceTick.CompareTo(b.PriceTick); });
            scene.Bubbles = bubbles.ToArray();
        }

        /// <summary>
        /// Original RGB anchors following the documented classic color family.
        /// The strongest classic liquidity is red; pale blue/white is intermediate.
        /// </summary>
        public static Rgb Color(double normalized, HeatPalette palette)
        {
            if (!Finite(normalized)) throw new ArgumentOutOfRangeException("normalized");
            double value = Clamp(normalized, 0, 1);
            if (palette == HeatPalette.Grayscale)
            {
                byte gray = (byte)Math.Round(12 + 243 * value);
                return new Rgb(gray, gray, gray);
            }
            if (palette == HeatPalette.Ice)
                return Ramp(value, IceColors, IcePositions);
            if (palette != HeatPalette.Classic) throw new ArgumentOutOfRangeException("palette");
            return Ramp(value, ClassicColors, ClassicPositions);
        }

        private static Rgb Ramp(double value, Rgb[] colors, double[] positions)
        {
            int upper = 1;
            while (upper < positions.Length - 1 && value > positions[upper]) upper++;
            double fraction = Clamp((value - positions[upper - 1]) / (positions[upper] - positions[upper - 1]), 0, 1);
            Rgb a = colors[upper - 1], b = colors[upper];
            return new Rgb((byte)Math.Round(a.R + (b.R - a.R) * fraction),
                (byte)Math.Round(a.G + (b.G - a.G) * fraction), (byte)Math.Round(a.B + (b.B - a.B) * fraction));
        }

        private static double Percentile(List<double> sorted, double fraction, int start = 0)
        {
            if (sorted.Count == 0) return 0;
            start = Math.Max(0, Math.Min(sorted.Count - 1, start));
            double index = start + (sorted.Count - 1 - start) * Clamp(fraction, 0, 1);
            int lower = (int)Math.Floor(index), upper = Math.Min(sorted.Count - 1, lower + 1);
            return sorted[lower] + (sorted[upper] - sorted[lower]) * (index - lower);
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static double Clamp(double value, double minimum, double maximum) { return Math.Max(minimum, Math.Min(maximum, value)); }
    }
}
