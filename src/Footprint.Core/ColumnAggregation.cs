using System;
using System.Collections.Generic;

namespace FootprintIndicator.Core
{
    /// <summary>One absolute price bin. Quantities are sums, never per-price estimates.</summary>
    public sealed class BookColumnRow
    {
        public readonly long FirstTick;
        public readonly int TickCount, BidLevels, AskLevels;
        public readonly decimal Bid, Ask;
        public decimal Total { get { return Bid + Ask; } }
        internal BookColumnRow(long tick, int count, decimal bid, decimal ask, int bids, int asks)
        { FirstTick = tick; TickCount = count; Bid = bid; Ask = ask; BidLevels = bids; AskLevels = asks; }
    }

    public sealed class BookColumnResult
    {
        public readonly BookColumnRow[] Rows;
        public readonly decimal BidTotal, AskTotal, Maximum;
        internal BookColumnResult(BookColumnRow[] rows, decimal bid, decimal ask, decimal maximum)
        { Rows = rows; BidTotal = bid; AskTotal = ask; Maximum = maximum; }
    }

    public sealed class ProfileColumnRow
    {
        public readonly long FirstTick;
        public readonly int TickCount, Levels;
        public readonly decimal Buy, Sell, Unknown;
        public decimal Total { get { return Buy + Sell + Unknown; } }
        public decimal Delta { get { return Buy - Sell; } }
        internal ProfileColumnRow(long tick, int count, decimal buy, decimal sell, decimal unknown, int levels)
        { FirstTick = tick; TickCount = count; Buy = buy; Sell = sell; Unknown = unknown; Levels = levels; }
    }

    /// <summary>
    /// Keeps columns readable by summing supplied prices into explicitly labeled tick bins.
    /// Bins are anchored to absolute prices, so panning does not change their membership.
    /// Decimal counters preserve sums larger than Int64 without changing engine accounting.
    /// </summary>
    public static class ColumnAggregation
    {
        private const int MaximumInputRows = 1000000;
        private sealed class Counter
        {
            internal decimal Buy, Sell, Unknown;
            internal int Buys, Sells, Levels;
        }

        public static int ChooseTicksPerRow(double pixelsPerTick, double minimumRowPixels = 14)
        {
            if (!Finite(pixelsPerTick) || pixelsPerTick <= 0 || !Finite(minimumRowPixels) || minimumRowPixels < 1)
                throw new ArgumentOutOfRangeException("pixelsPerTick");
            int count = 1;
            while (count < 1048576 && pixelsPerTick * count < minimumRowPixels) count *= 2;
            return count;
        }

        public static BookColumnResult Book(DepthLevel[] bids, DepthLevel[] asks,
            int bidLimit, int askLimit, int ticksPerRow, bool cumulative)
        {
            ValidateGroup(ticksPerRow);
            if (bids == null || asks == null) throw new ArgumentNullException("bids");
            if (bidLimit < 0 || askLimit < 0) throw new ArgumentOutOfRangeException("bidLimit");
            if (bids.Length > MaximumInputRows || asks.Length > MaximumInputRows)
                throw new InvalidOperationException("Book column input budget exceeded.");
            var bins = new SortedDictionary<long, Counter>();
            decimal bidTotal = AddSide(bids, bidLimit, true, ticksPerRow, bins);
            decimal askTotal = AddSide(asks, askLimit, false, ticksPerRow, bins);
            var keys = new List<long>(bins.Keys);
            if (cumulative)
            {
                decimal sum = 0;
                for (int i = keys.Count - 1; i >= 0; i--)
                {
                    Counter row = bins[keys[i]];
                    if (row.Buys > 0) { sum += row.Buy; row.Buy = sum; }
                }
                sum = 0;
                foreach (long key in keys)
                {
                    Counter row = bins[key];
                    if (row.Sells > 0) { sum += row.Sell; row.Sell = sum; }
                }
            }
            var rows = new BookColumnRow[keys.Count];
            decimal maximum = 1;
            for (int i = 0; i < keys.Count; i++)
            {
                Counter row = bins[keys[i]];
                rows[i] = new BookColumnRow(keys[i], ticksPerRow, row.Buy, row.Sell, row.Buys, row.Sells);
                maximum = Math.Max(maximum, rows[i].Total);
            }
            return new BookColumnResult(rows, bidTotal, askTotal, maximum);
        }

        private static decimal AddSide(DepthLevel[] source, int limit, bool bid, int group,
            SortedDictionary<long, Counter> bins)
        {
            decimal total = 0;
            int count = limit == 0 ? source.Length : Math.Min(limit, source.Length);
            for (int i = 0; i < count; i++)
            {
                DepthLevel level = source[i];
                if (level == null || level.Quantity < 0 || i > 0 &&
                    (bid ? level.Tick >= source[i - 1].Tick : level.Tick <= source[i - 1].Tick))
                    throw new ArgumentException("Book rows must be nonnegative, unique and ordered best quote first.", "source");
                if (level.Quantity == 0) continue;
                Counter row = Get(bins, Bin(level.Tick, group));
                if (bid) { row.Buy += level.Quantity; row.Buys++; }
                else { row.Sell += level.Quantity; row.Sells++; }
                total += level.Quantity;
            }
            return total;
        }

        public static ProfileColumnRow[] Profile(ProfileLevel[] source, int ticksPerRow)
        {
            ValidateGroup(ticksPerRow);
            if (source == null) throw new ArgumentNullException("source");
            if (source.Length > MaximumInputRows) throw new InvalidOperationException("Profile column input budget exceeded.");
            var bins = new SortedDictionary<long, Counter>();
            foreach (ProfileLevel level in source)
            {
                if (level == null || level.Buy < 0 || level.Sell < 0 || level.Unknown < 0)
                    throw new ArgumentException("Profile rows must contain nonnegative quantities.", "source");
                Counter row = Get(bins, Bin(level.Tick, ticksPerRow));
                row.Buy += level.Buy; row.Sell += level.Sell; row.Unknown += level.Unknown; row.Levels++;
            }
            var result = new ProfileColumnRow[bins.Count];
            int index = 0;
            foreach (KeyValuePair<long, Counter> bin in bins)
                result[index++] = new ProfileColumnRow(bin.Key, ticksPerRow,
                    bin.Value.Buy, bin.Value.Sell, bin.Value.Unknown, bin.Value.Levels);
            return result;
        }

        private static Counter Get(SortedDictionary<long, Counter> bins, long tick)
        {
            Counter result;
            if (!bins.TryGetValue(tick, out result)) { result = new Counter(); bins.Add(tick, result); }
            return result;
        }

        private static long Bin(long tick, int group)
        {
            decimal first = decimal.Floor(tick / (decimal)group) * group;
            if (first < long.MinValue || first + group - 1 > long.MaxValue)
                throw new ArgumentOutOfRangeException("tick", "The full grouped tick interval must fit Int64.");
            return (long)first;
        }

        private static void ValidateGroup(int group)
        {
            if (group < 1 || group > 1048576) throw new ArgumentOutOfRangeException("ticksPerRow");
        }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
