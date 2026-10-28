using System;
using System.Collections.Generic;

namespace FootprintIndicator.Core
{
    public enum ProfileScope { Session, Visible }

    public sealed class ProfileSettings
    {
        public ProfileScope Scope = ProfileScope.Session;
        public double ValueAreaPercent = 70, BandStandardDeviations = 1;
        public int BookLevels = 10;
        public double BookDistanceDecay = 0;
        public long LargeLiquidityMinimum = 100;
        public bool BuildFootprint, BuildTpo;
        public int FootprintMilliseconds = 60000, FootprintPriceGroupTicks = 1;
        public double DiagonalImbalanceRatio = 3;
        public long DiagonalMinimumVolume = 10;
        public int MaximumFootprintBars = 1000, MaximumFootprintCells = 100000;
        public int TpoMinutes = 30, TpoPriceGroupTicks = 1;
    }

    public sealed class FootprintCell
    {
        public long Tick, Buy, Sell, Unknown;
        public long Total { get { return checked(Buy + Sell + Unknown); } }
        public bool BuyImbalance, SellImbalance;
    }

    public sealed class FootprintBar
    {
        public long FromTicks, ToTicks, Buy, Sell, Unknown;
        public long TotalVolume { get { return checked(Buy + Sell + Unknown); } }
        public FootprintCell[] Levels = new FootprintCell[0];
    }

    public sealed class TpoLevel
    {
        public long Tick;
        public int Count;
        public TpoLevel(long tick, int count) { Tick = tick; Count = count; }
    }

    public sealed class ProfileResult
    {
        public ProfileLevel[] Levels = new ProfileLevel[0];
        public long TotalVolume, Buy, Sell, Unknown, ExecutionCount;
        public long? PocTick, ValueAreaLowTick, ValueAreaHighTick;
        public double Vwap = double.NaN, StandardDeviation = double.NaN;
        public double UpperBand = double.NaN, LowerBand = double.NaN;
        // Imbalance excludes unknown executions; the value is zero when neither side has volume.
        public double VolumeImbalance, BookImbalance, WeightedBidDepth, WeightedAskDepth;
        // Applies to retained execution statistics, footprints and TPO, not the independently kept session profile.
        public bool PartialTradeHistory;
        public FootprintBar[] Footprints = new FootprintBar[0];
        public TpoLevel[] TpoLevels = new TpoLevel[0];
        public long? TpoPocTick, TpoValueAreaLowTick, TpoValueAreaHighTick;
        public DepthLevel[] LargeBids = new DepthLevel[0], LargeAsks = new DepthLevel[0];
    }

    /// <summary>
    /// Original volume/profile studies from captured MBP and execution data.
    /// Session statistics use the captured session profile, not backfilled exchange history.
    /// Footprints and TPO use only retained trades in the requested half-open viewport.
    /// TPO counts brackets containing an actual print at a grouped price; unobserved prices
    /// between prints are not inferred. No order identity or hidden liquidity is inferred.
    /// </summary>
    public static class ProfileStudies
    {
        private const int MaximumInputTrades = 1000000, MaximumInputLevels = 200000;

        private sealed class Counter
        {
            internal long Buy, Sell, Unknown;
            internal void Add(TradeSide side, long volume)
            {
                checked
                {
                    if (side == TradeSide.Buy) Buy += volume;
                    else if (side == TradeSide.Sell) Sell += volume;
                    else Unknown += volume;
                    long total = Buy + Sell + Unknown;
                    if (total < 0) throw new OverflowException();
                }
            }
        }

        public static ProfileResult Build(EngineSnapshot snapshot, ProfileSettings settings, long fromTicks, long toTicks)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            if (settings == null) throw new ArgumentNullException("settings");
            Validate(settings);
            if (!Finite(snapshot.TickSize) || snapshot.TickSize <= 0)
                throw new ArgumentOutOfRangeException("snapshot", "Instrument tick size must be finite and positive.");
            if (fromTicks < 0 || toTicks > DateTime.MaxValue.Ticks || fromTicks >= toTicks)
                throw new ArgumentOutOfRangeException("fromTicks", "A valid half-open time range is required.");
            TradeRecord[] input = snapshot.Trades ?? new TradeRecord[0];
            if (input.Length > MaximumInputTrades) throw new InvalidOperationException("Profile trade input budget exceeded.");
            var result = new ProfileResult();
            var selected = new List<TradeRecord>();
            var visibleProfile = new SortedDictionary<long, Counter>();
            long earliest = long.MaxValue;
            foreach (TradeRecord trade in input)
            {
                if (trade == null || trade.TimeTicks < 0 || trade.TimeTicks > DateTime.MaxValue.Ticks || trade.Volume <= 0 ||
                    (trade.Side != TradeSide.Buy && trade.Side != TradeSide.Sell && trade.Side != TradeSide.Unknown))
                    throw new ArgumentException("Invalid retained execution.", "snapshot");
                earliest = Math.Min(earliest, trade.TimeTicks);
                bool inSession = trade.TimeTicks >= snapshot.SessionStartTicks;
                bool inView = trade.TimeTicks >= fromTicks && trade.TimeTicks < toTicks;
                if (settings.Scope == ProfileScope.Session ? inSession : inView) result.ExecutionCount++;
                if (!inView || (settings.Scope == ProfileScope.Session && !inSession)) continue;
                selected.Add(trade);
                if (settings.Scope == ProfileScope.Visible) Add(visibleProfile, trade.PriceTick, trade.Side, trade.Volume);
            }
            long requiredStart = settings.Scope == ProfileScope.Session ? snapshot.SessionStartTicks : fromTicks;
            result.PartialTradeHistory = snapshot.DroppedTrades > 0 && (input.Length == 0 || requiredStart < earliest);
            result.Levels = settings.Scope == ProfileScope.Session ? CopyProfile(snapshot.Profile) : ToProfile(visibleProfile);
            ComputeProfile(result, snapshot.TickSize, settings);
            result.WeightedBidDepth = Book(snapshot.Bids, BookSide.Bid, settings, out result.LargeBids);
            result.WeightedAskDepth = Book(snapshot.Asks, BookSide.Ask, settings, out result.LargeAsks);
            double allDepth = result.WeightedBidDepth + result.WeightedAskDepth;
            if (allDepth > 0) result.BookImbalance = (result.WeightedBidDepth - result.WeightedAskDepth) / allDepth;
            // Both studies align brackets to DateTime's tick epoch, giving stable grouping
            // under chart panning. They do not reanchor to the visible left edge.
            if (settings.BuildFootprint) result.Footprints = Footprints(selected, settings);
            if (settings.BuildTpo) ComputeTpo(selected, settings, result);
            return result;
        }

        private static void Validate(ProfileSettings settings)
        {
            if ((settings.Scope != ProfileScope.Session && settings.Scope != ProfileScope.Visible) ||
                !Finite(settings.ValueAreaPercent) || settings.ValueAreaPercent <= 0 || settings.ValueAreaPercent > 100 ||
                !Finite(settings.BandStandardDeviations) || settings.BandStandardDeviations < 0 || settings.BandStandardDeviations > 10 ||
                settings.BookLevels < 1 || settings.BookLevels > MaximumInputLevels ||
                !Finite(settings.BookDistanceDecay) || settings.BookDistanceDecay < 0 || settings.BookDistanceDecay > 1000000 ||
                settings.LargeLiquidityMinimum < 1 || settings.FootprintMilliseconds < 1 || settings.FootprintMilliseconds > 86400000 ||
                settings.FootprintPriceGroupTicks < 1 || settings.FootprintPriceGroupTicks > 1000000 ||
                !Finite(settings.DiagonalImbalanceRatio) || settings.DiagonalImbalanceRatio < 1 || settings.DiagonalImbalanceRatio > 1000000 ||
                settings.DiagonalMinimumVolume < 1 || settings.MaximumFootprintBars < 1 || settings.MaximumFootprintBars > 10000 ||
                settings.MaximumFootprintCells < 1 || settings.MaximumFootprintCells > 500000 ||
                settings.TpoMinutes < 1 || settings.TpoMinutes > 1440 || settings.TpoPriceGroupTicks < 1 || settings.TpoPriceGroupTicks > 1000000)
                throw new ArgumentOutOfRangeException("settings", "Invalid profile, footprint, or TPO settings.");
        }

        private static void Add(SortedDictionary<long, Counter> levels, long tick, TradeSide side, long volume)
        {
            Counter counter;
            if (!levels.TryGetValue(tick, out counter))
            {
                if (levels.Count >= MaximumInputLevels) throw new InvalidOperationException("Profile price-level budget exceeded.");
                counter = new Counter(); levels.Add(tick, counter);
            }
            counter.Add(side, volume);
        }

        private static ProfileLevel[] ToProfile(SortedDictionary<long, Counter> counters)
        {
            var levels = new ProfileLevel[counters.Count];
            int index = 0;
            foreach (KeyValuePair<long, Counter> row in counters)
                levels[index++] = new ProfileLevel(row.Key, row.Value.Buy, row.Value.Sell, row.Value.Unknown);
            return levels;
        }

        private static ProfileLevel[] CopyProfile(ProfileLevel[] input)
        {
            if (input == null) return new ProfileLevel[0];
            if (input.Length > MaximumInputLevels) throw new InvalidOperationException("Session profile budget exceeded.");
            var counters = new SortedDictionary<long, Counter>();
            foreach (ProfileLevel row in input)
            {
                if (row == null || row.Buy < 0 || row.Sell < 0 || row.Unknown < 0)
                    throw new ArgumentException("Invalid session profile row.", "input");
                long total = checked(row.Buy + row.Sell + row.Unknown);
                if (total == 0) continue;
                if (counters.ContainsKey(row.Tick)) throw new ArgumentException("Duplicate session profile price.", "input");
                counters.Add(row.Tick, new Counter { Buy = row.Buy, Sell = row.Sell, Unknown = row.Unknown });
            }
            return ToProfile(counters);
        }

        private static void ComputeProfile(ProfileResult result, double tickSize, ProfileSettings settings)
        {
            foreach (ProfileLevel level in result.Levels)
            {
                checked
                {
                    result.Buy += level.Buy; result.Sell += level.Sell; result.Unknown += level.Unknown;
                    result.TotalVolume += level.Total;
                }
            }
            // Decimal subtraction retains a one-contract delta even above double's integer precision.
            decimal classified = (decimal)result.Buy + result.Sell;
            if (classified > 0) result.VolumeImbalance = (double)((decimal)result.Buy - result.Sell) / (double)classified;
            if (result.TotalVolume == 0) return;
            ValueArea(result.Levels, settings.ValueAreaPercent,
                out result.PocTick, out result.ValueAreaLowTick, out result.ValueAreaHighTick);
            long anchor = result.Levels[0].Tick;
            double mean = 0, weight = 0, m2 = 0;
            foreach (ProfileLevel level in result.Levels)
            {
                double value = (double)((decimal)level.Tick - anchor);
                double nextWeight = weight + level.Total;
                double delta = value - mean;
                mean += delta * (level.Total / nextWeight);
                m2 += level.Total * delta * (value - mean);
                weight = nextWeight;
            }
            result.Vwap = ((double)anchor + mean) * tickSize;
            result.StandardDeviation = Math.Sqrt(Math.Max(0, m2 / weight)) * tickSize;
            result.UpperBand = result.Vwap + result.StandardDeviation * settings.BandStandardDeviations;
            result.LowerBand = result.Vwap - result.StandardDeviation * settings.BandStandardDeviations;
            if (!Finite(result.Vwap) || !Finite(result.StandardDeviation) || !Finite(result.UpperBand) || !Finite(result.LowerBand))
                throw new OverflowException("Profile price statistics exceed finite numeric range.");
        }

        // Select POC by highest total; ties choose the lower price. Expand the contiguous
        // occupied-price range toward the larger adjacent row, lower side first on a tie.
        // Gaps contain no volume but lie within the resulting low/high interval.
        private static void ValueArea(ProfileLevel[] levels, double percent, out long? poc, out long? low, out long? high)
        {
            poc = low = high = null;
            if (levels.Length == 0) return;
            long total = 0;
            int p = 0;
            for (int i = 0; i < levels.Length; i++)
            {
                total = checked(total + levels[i].Total);
                if (levels[i].Total > levels[p].Total) p = i;
            }
            if (total == 0) return;
            decimal target = (decimal)total * (decimal)percent / 100m;
            int a = p, b = p;
            long included = levels[p].Total;
            while ((decimal)included < target && (a > 0 || b + 1 < levels.Length))
            {
                bool takeLow = a > 0 && (b + 1 == levels.Length || levels[a - 1].Total >= levels[b + 1].Total);
                included = checked(included + (takeLow ? levels[--a].Total : levels[++b].Total));
            }
            poc = levels[p].Tick; low = levels[a].Tick; high = levels[b].Tick;
        }

        private static double Book(DepthLevel[] input, BookSide side, ProfileSettings settings, out DepthLevel[] large)
        {
            large = new DepthLevel[0];
            if (input == null || input.Length == 0) return 0;
            if (input.Length > MaximumInputLevels) throw new InvalidOperationException("Current book input budget exceeded.");
            var levels = (DepthLevel[])input.Clone();
            foreach (DepthLevel level in levels)
                if (level == null || level.Quantity < 0) throw new ArgumentException("Invalid current book row.", "input");
            Array.Sort(levels, delegate(DepthLevel a, DepthLevel b)
            { return side == BookSide.Bid ? b.Tick.CompareTo(a.Tick) : a.Tick.CompareTo(b.Tick); });
            double weighted = 0;
            var walls = new List<DepthLevel>();
            long best = levels[0].Tick;
            for (int i = 0; i < levels.Length; i++)
            {
                if (i > 0 && levels[i].Tick == levels[i - 1].Tick)
                    throw new ArgumentException("Duplicate book price.", "input");
                if (levels[i].Quantity >= settings.LargeLiquidityMinimum)
                    walls.Add(new DepthLevel(levels[i].Tick, levels[i].Quantity));
                if (i >= settings.BookLevels) continue;
                double distance = Math.Abs((double)((decimal)levels[i].Tick - best));
                weighted += levels[i].Quantity / (1 + distance * settings.BookDistanceDecay);
            }
            large = walls.ToArray();
            return weighted;
        }

        private static long GroupPrice(long tick, int grouping)
        {
            // Mathematical floor, including negative futures prices. Decimal avoids
            // overflowing when the grouped origin lies just below Int64.MinValue.
            decimal grouped = decimal.Floor((decimal)tick / grouping) * grouping;
            if (grouped < long.MinValue || grouped > long.MaxValue) throw new OverflowException("Grouped tick price exceeds Int64.");
            return (long)grouped;
        }

        private static FootprintBar[] Footprints(List<TradeRecord> trades, ProfileSettings settings)
        {
            long duration = (long)settings.FootprintMilliseconds * TimeSpan.TicksPerMillisecond;
            var bars = new SortedDictionary<long, SortedDictionary<long, Counter>>();
            int cells = 0;
            foreach (TradeRecord trade in trades)
            {
                long start = trade.TimeTicks / duration * duration;
                SortedDictionary<long, Counter> levels;
                if (!bars.TryGetValue(start, out levels))
                {
                    if (bars.Count >= settings.MaximumFootprintBars) throw new InvalidOperationException("Footprint bar budget exceeded; increase bar duration or narrow the view.");
                    levels = new SortedDictionary<long, Counter>(); bars.Add(start, levels);
                }
                long tick = GroupPrice(trade.PriceTick, settings.FootprintPriceGroupTicks);
                if (!levels.ContainsKey(tick) && ++cells > settings.MaximumFootprintCells)
                    throw new InvalidOperationException("Footprint cell budget exceeded; group prices or narrow the view.");
                Add(levels, tick, trade.Side, trade.Volume);
            }
            var result = new List<FootprintBar>();
            foreach (KeyValuePair<long, SortedDictionary<long, Counter>> bar in bars)
            {
                var output = new FootprintBar { FromTicks = bar.Key,
                    ToTicks = bar.Key + Math.Min(duration, DateTime.MaxValue.Ticks - bar.Key) };
                var levels = new List<FootprintCell>();
                foreach (KeyValuePair<long, Counter> level in bar.Value)
                {
                    Counter lower = null, upper = null;
                    if (level.Key >= long.MinValue + settings.FootprintPriceGroupTicks)
                        bar.Value.TryGetValue(level.Key - settings.FootprintPriceGroupTicks, out lower);
                    if (level.Key <= long.MaxValue - settings.FootprintPriceGroupTicks)
                        bar.Value.TryGetValue(level.Key + settings.FootprintPriceGroupTicks, out upper);
                    var cell = new FootprintCell { Tick = level.Key, Buy = level.Value.Buy, Sell = level.Value.Sell, Unknown = level.Value.Unknown,
                        BuyImbalance = Imbalanced(level.Value.Buy, lower == null ? 0 : lower.Sell, settings),
                        SellImbalance = Imbalanced(level.Value.Sell, upper == null ? 0 : upper.Buy, settings) };
                    checked { output.Buy += cell.Buy; output.Sell += cell.Sell; output.Unknown += cell.Unknown; }
                    long total = output.TotalVolume;
                    if (total < 0) throw new OverflowException();
                    levels.Add(cell);
                }
                output.Levels = levels.ToArray(); result.Add(output);
            }
            return result.ToArray();
        }

        private static bool Imbalanced(long numerator, long denominator, ProfileSettings settings)
        {
            // A missing opposite price is not evidence of a zero queue/traded side.
            // Require a positive diagonal denominator; no division-by-zero flags.
            return numerator >= settings.DiagonalMinimumVolume && denominator > 0 &&
                (decimal)numerator >= (decimal)denominator * (decimal)settings.DiagonalImbalanceRatio;
        }

        private static void ComputeTpo(List<TradeRecord> trades, ProfileSettings settings, ProfileResult result)
        {
            long duration = (long)settings.TpoMinutes * TimeSpan.TicksPerMinute;
            var brackets = new Dictionary<long, HashSet<long>>();
            var counts = new SortedDictionary<long, Counter>();
            int cells = 0;
            foreach (TradeRecord trade in trades)
            {
                long bracket = trade.TimeTicks / duration;
                HashSet<long> prices;
                if (!brackets.TryGetValue(bracket, out prices))
                {
                    if (brackets.Count >= settings.MaximumFootprintBars) throw new InvalidOperationException("TPO bracket budget exceeded; increase bracket duration or narrow the view.");
                    prices = new HashSet<long>(); brackets.Add(bracket, prices);
                }
                long price = GroupPrice(trade.PriceTick, settings.TpoPriceGroupTicks);
                if (!prices.Add(price)) continue;
                if (++cells > settings.MaximumFootprintCells) throw new InvalidOperationException("TPO cell budget exceeded; group prices or narrow the view.");
                Add(counts, price, TradeSide.Unknown, 1);
            }
            ProfileLevel[] levels = ToProfile(counts);
            result.TpoLevels = new TpoLevel[levels.Length];
            for (int i = 0; i < levels.Length; i++) result.TpoLevels[i] = new TpoLevel(levels[i].Tick, checked((int)levels[i].Unknown));
            ValueArea(levels, settings.ValueAreaPercent, out result.TpoPocTick, out result.TpoValueAreaLowTick, out result.TpoValueAreaHighTick);
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
