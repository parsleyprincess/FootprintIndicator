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
            internal long Buy, Sell, Unknown, Total, First, Last, MinPrice, MaxPrice;
            internal double MeanPrice, MeanTimeOffset;
            internal readonly long Origin;

            internal TradeAccumulator(long origin) { Origin = origin; }

            internal void Add(TradeRecord trade)
            {
                long next = checked(Total + trade.Volume);
                if (Total == 0)
                {
                    First = Last = trade.TimeTicks;
                    MinPrice = MaxPrice = trade.PriceTick;
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
                    if (trade.PriceTick < MinPrice) MinPrice = trade.PriceTick;
                    if (trade.PriceTick > MaxPrice) MaxPrice = trade.PriceTick;
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
            internal readonly long Time, Row;
            internal readonly int Epoch;
            internal ClusterKey(long time, long row, int epoch) { Time = time; Row = row; Epoch = epoch; }
            public bool Equals(ClusterKey other) { return Time == other.Time && Row == other.Row && Epoch == other.Epoch; }
            public override bool Equals(object value) { return value is ClusterKey && Equals((ClusterKey)value); }
            public override int GetHashCode() { unchecked { return (Time.GetHashCode() * 397 ^ Row.GetHashCode()) * 397 ^ Epoch; } }
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
                !Finite(settings.HeatFloorPercent) || settings.HeatFloorPercent < 0 || settings.HeatFloorPercent > 99 ||
                settings.HeatColumnPixels < 1 || settings.HeatColumnPixels > 32768)
                throw new ArgumentOutOfRangeException("settings", "Invalid heatmap scale, threshold or resolution.");
            if (settings.ClusterMilliseconds < 1 || settings.ClusterVolume < 1 || settings.SmartPixels < 1 ||
                settings.VolumeAnchorSeconds < 1 || settings.VolumeAnchorSeconds > 86400 ||
                settings.SmartPixels > 32768 || settings.MinimumTradeSize < 1 || settings.MinimumBubbleVolume < 1 ||
                !Finite(settings.BubbleScale) || settings.BubbleScale <= 0 || !Finite(settings.MaxBubbleRadius) ||
                !Finite(settings.BubbleReferenceVolume) || settings.BubbleReferenceVolume <= 0 ||
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
                double floor = settings.HeatFloorPercent / 100;
                double strength = Clamp(value / scene.HeatMaximum, 0, 1);
                if (strength <= floor && floor > 0) continue;
                double normalized = Math.Pow(Clamp((strength - floor) / (1 - floor), 0, 1), settings.Gamma);
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
            bool sequential = settings.Clustering == ClusterMode.Volume || settings.Clustering == ClusterMode.Price ||
                settings.Clustering == ClusterMode.PriceAndSide;
            foreach (TradeRecord trade in source)
            {
                if (trade == null || trade.Volume <= 0) continue;
                bool visibleTime = trade.TimeTicks >= view.FromTicks && trade.TimeTicks < view.ToTicks;
                if (visibleTime && trade.PriceTick >= view.MinPriceTick && trade.PriceTick <= view.MaxPriceTick)
                {
                    ProfileAccumulator profile;
                    if (!profiles.TryGetValue(trade.PriceTick, out profile))
                    { profile = new ProfileAccumulator(); profiles.Add(trade.PriceTick, profile); }
                    profile.Add(trade);
                }
                if (trade.Volume >= settings.MinimumTradeSize && (sequential ||
                    (visibleTime && trade.PriceTick >= view.MinPriceTick - pricePadding &&
                    trade.PriceTick <= view.MaxPriceTick + pricePadding))) trades.Add(trade);
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
            TradeRecord previous = null;
            long runVolume = 0;
            double duration = view.ToTicks - view.FromTicks;
            long timeStep = (long)settings.ClusterMilliseconds * TimeSpan.TicksPerMillisecond;
            long volumeAnchorTicks = (long)settings.VolumeAnchorSeconds * TimeSpan.TicksPerSecond;
            if (settings.Clustering == ClusterMode.Pixel)
                timeStep = (long)Math.Max(1, Math.Ceiling(duration / view.Width));
            else if (settings.Clustering == ClusterMode.Smart)
                timeStep = QuantizedStep(duration / view.Width * settings.SmartPixels);
            long priceStep = settings.Clustering == ClusterMode.Smart
                ? QuantizedStep(priceRange / view.Height * settings.SmartPixels) : 1;
            foreach (TradeRecord trade in trades)
            {
                if (settings.Clustering == ClusterMode.Individual)
                {
                    var individual = new TradeAccumulator(trade.TimeTicks);
                    individual.Add(trade);
                    groups.Add(individual);
                    continue;
                }
                if (sequential)
                {
                    // Establish run boundaries from retained chronology, including
                    // offscreen prints. Only the group's visible members are drawn.
                    bool boundary = previous == null || previous.Epoch != trade.Epoch;
                    if (!boundary && settings.Clustering == ClusterMode.Volume)
                        boundary = runVolume >= settings.ClusterVolume ||
                            FloorDivide(previous.TimeTicks, volumeAnchorTicks) != FloorDivide(trade.TimeTicks, volumeAnchorTicks);
                    if (!boundary && (settings.Clustering == ClusterMode.Price || settings.Clustering == ClusterMode.PriceAndSide))
                        boundary = previous.PriceTick != trade.PriceTick ||
                            (settings.Clustering == ClusterMode.PriceAndSide && previous.Side != trade.Side);
                    if (boundary) { current = null; runVolume = 0; }
                    // The volume mode keeps the entire closing trade. It never
                    // synthesizes a split execution to hit an exact threshold.
                    // Absolute-time anchors bound a run's duration and isolate
                    // later anchors from a partially trimmed retention boundary.
                    // Saturating boundary evidence also avoids overflow from an
                    // offscreen large print that will never enter an accumulator.
                    if (runVolume < settings.ClusterVolume)
                        runVolume = trade.Volume >= settings.ClusterVolume - runVolume
                            ? settings.ClusterVolume : runVolume + trade.Volume;
                    previous = trade;
                    if (trade.TimeTicks < view.FromTicks || trade.TimeTicks >= view.ToTicks ||
                        trade.PriceTick < view.MinPriceTick - pricePadding || trade.PriceTick > view.MaxPriceTick + pricePadding) continue;
                    if (current == null) { current = new TradeAccumulator(trade.TimeTicks); groups.Add(current); }
                    current.Add(trade);
                }
                else
                {
                    // Absolute event-time and tick coordinates make grid membership
                    // independent of panning. Smart changes resolution in powers of
                    // two, so a small price autoscale does not continually regroup it.
                    long timeBin = FloorDivide(trade.TimeTicks, timeStep);
                    long priceBin = settings.Clustering == ClusterMode.Smart ? FloorDivide(trade.PriceTick, priceStep) : 0;
                    ClusterKey key = new ClusterKey(timeBin, priceBin, trade.Epoch);
                    TradeAccumulator group;
                    if (!bins.TryGetValue(key, out group))
                    { group = new TradeAccumulator(trade.TimeTicks); bins.Add(key, group); groups.Add(group); }
                    group.Add(trade);
                }
            }
            scene.BubbleReferenceVolume = settings.BubbleReferenceVolume;
            List<Bubble> bubbles = new List<Bubble>(groups.Count);
            foreach (TradeAccumulator group in groups)
            {
                if (group.Total < settings.MinimumBubbleVolume) continue;
                long time = group.Origin + (long)Math.Round(group.MeanTimeOffset, MidpointRounding.AwayFromZero);
                time = Math.Max(group.First, Math.Min(group.Last, time));
                // Both sides are nonnegative and their sum fits Int64, so their integer
                // difference is safe. Subtract before converting: double(Buy)-double(Sell)
                // can erase a real one-contract imbalance at large cumulative volumes.
                double represented = settings.DeltaBubbles ? Math.Abs((double)(group.Buy - group.Sell)) : group.Total;
                double radius = represented == 0 ? 0 : Math.Min(settings.MaxBubbleRadius,
                    Math.Max(1, settings.BubbleScale * Math.Sqrt(represented / scene.BubbleReferenceVolume)));
                bubbles.Add(new Bubble {
                    X = ((double)(group.Origin - view.FromTicks) + group.MeanTimeOffset) / duration * view.Width,
                    Y = (view.MaxPriceTick - group.MeanPrice) / priceRange * view.Height,
                    TimeTicks = time, PriceTick = group.MeanPrice, FromTicks = group.First, ToTicks = group.Last,
                    MinPriceTick = group.MinPrice, MaxPriceTick = group.MaxPrice,
                    Buy = group.Buy, Sell = group.Sell, Unknown = group.Unknown, Radius = radius
                });
            }
            // Equal-time/equal-price prints still have a deterministic order.
            // List.Sort is unstable: appending unrelated prints could otherwise
            // reorder older overlapping circles and change the topmost hover hit.
            int[] paintOrder = new int[bubbles.Count];
            for (int i = 0; i < paintOrder.Length; i++) paintOrder[i] = i;
            Array.Sort(paintOrder, delegate(int first, int second)
            {
                Bubble a = bubbles[first], b = bubbles[second];
                int comparison = a.TimeTicks.CompareTo(b.TimeTicks);
                if (comparison != 0) return comparison;
                comparison = a.PriceTick.CompareTo(b.PriceTick);
                return comparison != 0 ? comparison : first.CompareTo(second);
            });
            scene.Bubbles = new Bubble[bubbles.Count];
            for (int i = 0; i < paintOrder.Length; i++) scene.Bubbles[i] = bubbles[paintOrder[i]];
        }

        private static long FloorDivide(long value, long divisor)
        {
            long quotient = value / divisor;
            return value < 0 && value % divisor != 0 ? quotient - 1 : quotient;
        }

        private static long QuantizedStep(double requested)
        {
            if (requested <= 1) return 1;
            if (requested >= long.MaxValue) return long.MaxValue;
            double step = Math.Pow(2, Math.Round(Math.Log(requested, 2), MidpointRounding.AwayFromZero));
            return step >= long.MaxValue ? long.MaxValue : (long)step;
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
