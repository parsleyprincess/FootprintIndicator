using FootprintIndicator.Core;

public static class ColumnAggregationTests
{
    public static void Run(Action<bool, string> assert)
    {
        assert(ColumnAggregation.ChooseTicksPerRow(14) == 1 && ColumnAggregation.ChooseTicksPerRow(7) == 2 &&
            ColumnAggregation.ChooseTicksPerRow(3) == 8, "Unreadable price rows become stable power-of-two tick bins.");
        var bids = new[] { new DepthLevel(101, 5), new DepthLevel(100, 7), new DepthLevel(99, 11), new DepthLevel(98, 13) };
        var asks = new[] { new DepthLevel(102, 17), new DepthLevel(103, 19), new DepthLevel(104, 23) };
        var grouped = ColumnAggregation.Book(bids, asks, 0, 0, 4, false);
        assert(grouped.BidTotal == 36 && grouped.AskTotal == 59 && grouped.Rows.Sum(r => r.Total) == 95,
            "Grouping conserves all supplied depth without synthesizing missing prices.");
        var mixed = grouped.Rows.Single(r => r.FirstTick == 100);
        assert(mixed.Bid == 12 && mixed.Ask == 36 && mixed.BidLevels == 2 && mixed.AskLevels == 2 && mixed.TickCount == 4,
            "A bin straddling the spread keeps bid and ask quantities separately rather than painting over either side.");
        assert(grouped.Maximum == 48 && grouped.Rows[0].FirstTick == 96 && bids[0].Quantity == 5,
            "Combined bars scale to the actual grouped total without mutating input rows.");
        var limited = ColumnAggregation.Book(bids, asks, 2, 1, 4, false);
        assert(limited.BidTotal == 12 && limited.AskTotal == 17 && limited.Rows.Sum(r => r.Total) == 29,
            "Side row limits apply to nearest supplied levels before grouping.");
        var cumulative = ColumnAggregation.Book(bids, asks, 0, 0, 2, true);
        assert(cumulative.Rows.Single(r => r.FirstTick == 98).Bid == 36 && cumulative.Rows.Single(r => r.FirstTick == 100).Bid == 12 &&
            cumulative.Rows.Single(r => r.FirstTick == 102).Ask == 36 && cumulative.Rows.Single(r => r.FirstTick == 104).Ask == 59,
            "Cumulative columns sum outward independently on each side, including full grouped rows.");
        assert(cumulative.BidTotal == 36 && cumulative.AskTotal == 59 && cumulative.Rows.Single(r => r.FirstTick == 102).Bid == 0,
            "Cumulative rendering neither changes total denominators nor extends a bid into unobserved ask rows.");

        var profile = new[] { new ProfileLevel(-1, 7, 3, 2), new ProfileLevel(-2, 11, 5, 1), new ProfileLevel(0, 2, 9, 4) };
        var p = ColumnAggregation.Profile(profile, 2);
        assert(p.Length == 2 && p[0].FirstTick == -2 && p[0].Buy == 18 && p[0].Sell == 8 && p[0].Unknown == 3 && p[0].Delta == 10,
            "Negative-price grouping uses mathematical floor and preserves unknown volume.");
        assert(p.Sum(r => r.Total) == 44 && p[1].FirstTick == 0 && p[1].Delta == -7,
            "Profile totals and signed delta are independent of display bin size.");
        var large = ColumnAggregation.Profile(new[] { new ProfileLevel(1, long.MaxValue, long.MaxValue, long.MaxValue) }, 1);
        assert(large[0].Total == (decimal)long.MaxValue * 3, "Display sums cannot overflow Int64 or silently wrap.");
        assert(ColumnAggregation.Book(new DepthLevel[0], new DepthLevel[0], 0, 0, 1, false).Rows.Length == 0,
            "Empty book remains empty, not a zero-filled ladder.");
        assert(ColumnAggregation.ChooseTicksPerRow(double.Epsilon) == 1048576, "Extreme zoom is bounded.");
        Throws(() => ColumnAggregation.ChooseTicksPerRow(double.NaN), assert);
        Throws(() => ColumnAggregation.Profile(profile, 0), assert);
        Throws(() => ColumnAggregation.Book(bids.Reverse().ToArray(), asks, 0, 0, 1, false), assert);
        Throws(() => ColumnAggregation.Profile(new[] { new ProfileLevel(long.MinValue, 1, 0, 0) }, 3), assert);
        Throws(() => ColumnAggregation.Profile(new[] { new ProfileLevel(1, -1, 0, 0) }, 1), assert);
        SignedBookAndLimits(assert);
        SparseAndLargeBook(assert);
        AbsoluteProfileBins(assert);
        InvalidInputs(assert);
    }

    private static void SignedBookAndLimits(Action<bool, string> assert)
    {
        var bids = new[] { new DepthLevel(2, 5), new DepthLevel(1, 7), new DepthLevel(-1, 11),
            new DepthLevel(-2, 13), new DepthLevel(-5, 17) };
        var asks = new[] { new DepthLevel(3, 19), new DepthLevel(4, 23), new DepthLevel(7, 29), new DepthLevel(8, 31) };
        var book = ColumnAggregation.Book(bids, asks, 0, 0, 4, false);
        assert(book.Rows.Select(row => row.FirstTick).SequenceEqual(new long[] { -8, -4, 0, 4, 8 }) &&
            book.Rows.Single(row => row.FirstTick == -4).Bid == 24,
            "Books crossing zero retain mathematical-floor bins and ascending output prices.");
        assert(book.BidTotal == 53 && book.AskTotal == 102 && book.Maximum == 52,
            "Grouped bid and ask denominators conserve all quantities across negative and positive prices.");
        var mixed = book.Rows.Single(row => row.FirstTick == 0);
        assert(mixed.Bid == 12 && mixed.Ask == 19 && mixed.BidLevels == 2 && mixed.AskLevels == 1,
            "The same absolute price bin retains both sides of a narrow spread.");

        var limited = ColumnAggregation.Book(bids, asks, 3, 2, 4, true);
        assert(limited.BidTotal == 23 && limited.AskTotal == 42 && limited.Rows.Length == 3 &&
            limited.Rows.Single(row => row.FirstTick == -4).Bid == 23 &&
            limited.Rows.Single(row => row.FirstTick == 4).Ask == 42,
            "Cumulative depth uses the nearest requested levels on each side before grouping and summing outward.");
        var cumulative = ColumnAggregation.Book(bids, asks, 0, 0, 4, true);
        assert(cumulative.Rows.Single(row => row.FirstTick == -8).Bid == 53 &&
            cumulative.Rows.Single(row => row.FirstTick == -4).Bid == 36 &&
            cumulative.Rows.Single(row => row.FirstTick == 0).Total == 31 &&
            cumulative.Rows.Single(row => row.FirstTick == 4).Ask == 71 && cumulative.Maximum == 102,
            "Mixed-side cumulative bins retain independent bid and ask sums and a truthful bar maximum.");
        var unlimited = ColumnAggregation.Book(bids, asks, int.MaxValue, int.MaxValue, 4, false);
        assert(unlimited.BidTotal == book.BidTotal && unlimited.AskTotal == book.AskTotal &&
            bids[4].Tick == -5 && bids[4].Quantity == 17 && asks[3].Quantity == 31,
            "Limits above the supplied depth preserve quantities and do not mutate feed rows.");
    }

    private static void SparseAndLargeBook(Action<bool, string> assert)
    {
        var sparse = ColumnAggregation.Book(new[] { new DepthLevel(100, 5), new DepthLevel(96, 0), new DepthLevel(92, 7) },
            new[] { new DepthLevel(101, 11), new DepthLevel(109, 13) }, 0, 0, 4, true);
        assert(sparse.Rows.Select(row => row.FirstTick).SequenceEqual(new long[] { 92, 100, 108 }) &&
            sparse.Rows[0].Bid == 12 && sparse.Rows[0].Ask == 0 &&
            sparse.Rows[2].Bid == 0 && sparse.Rows[2].Ask == 24,
            "Cumulative bars preserve gaps and never invent levels or extend either side into the opposite book.");
        var limited = ColumnAggregation.Book(new[] { new DepthLevel(100, 0), new DepthLevel(99, 7) },
            Array.Empty<DepthLevel>(), 1, 0, 4, false);
        assert(limited.Rows.Length == 0 && limited.BidTotal == 0,
            "A feed row limit counts supplied levels even when the nearest level has no resting quantity.");
        var huge = ColumnAggregation.Book(new[] { new DepthLevel(1, long.MaxValue), new DepthLevel(0, long.MaxValue) },
            new[] { new DepthLevel(2, long.MaxValue), new DepthLevel(3, long.MaxValue) }, 0, 0, 4, false);
        assert(huge.Rows.Length == 1 && huge.BidTotal == (decimal)long.MaxValue * 2 &&
            huge.AskTotal == (decimal)long.MaxValue * 2 && huge.Maximum == (decimal)long.MaxValue * 4,
            "Mixed depth bins keep exact decimal totals beyond Int64 on both sides and the combined bar.");
    }

    private static void AbsoluteProfileBins(Action<bool, string> assert)
    {
        var profile = new[] { new ProfileLevel(-5, 3, 7, 11), new ProfileLevel(-4, 13, 17, 19),
            new ProfileLevel(-1, 23, 29, 31), new ProfileLevel(0, 37, 41, 43), new ProfileLevel(4, 47, 53, 59) };
        foreach (int binSize in new[] { 1, 2, 4, 8, 16 })
        {
            var rows = ColumnAggregation.Profile(profile, binSize);
            assert(rows.Sum(row => row.Buy) == 123 && rows.Sum(row => row.Sell) == 147 &&
                rows.Sum(row => row.Unknown) == 163 && rows.Sum(row => row.Delta) == -24 &&
                rows.Sum(row => row.Levels) == profile.Length,
                "Changing profile scale preserves executed buy, sell, unknown, delta and source-level counts.");
        }
        var all = ColumnAggregation.Profile(profile, 4);
        var subset = ColumnAggregation.Profile(profile.Skip(1).Take(3).Reverse().ToArray(), 4);
        assert(subset.Length == 2 && subset[0].FirstTick == -4 && subset[1].FirstTick == 0 &&
            subset.All(row => all.Single(full => full.FirstTick == row.FirstTick).Total == row.Total),
            "Profile bin membership remains attached to absolute prices as visible inputs and input order change.");
        var extreme = ColumnAggregation.Profile(new[] { new ProfileLevel(long.MinValue, 1, 2, 3),
            new ProfileLevel(long.MaxValue, 4, 5, 6) }, 1048576);
        assert(extreme.Length == 2 && extreme[0].FirstTick == long.MinValue &&
            extreme[1].FirstTick == long.MaxValue - 1048575 && extreme.Sum(row => row.Total) == 21,
            "Representable bins at both Int64 price limits retain their exact boundaries and quantities.");
    }

    private static void InvalidInputs(Action<bool, string> assert)
    {
        var empty = Array.Empty<DepthLevel>();
        Throws(() => ColumnAggregation.Book(empty, empty, -1, 0, 1, false), assert);
        Throws(() => ColumnAggregation.Book(empty, empty, 0, -1, 1, false), assert);
        Throws(() => ColumnAggregation.Book(empty, empty, 0, 0, 1048577, false), assert);
        Throws(() => ColumnAggregation.Book(new[] { new DepthLevel(2, 1), new DepthLevel(2, 2) }, empty, 0, 0, 1, false), assert);
        Throws(() => ColumnAggregation.Book(empty, new[] { new DepthLevel(3, 1), new DepthLevel(2, 2) }, 0, 0, 1, false), assert);
        Throws(() => ColumnAggregation.Book(new[] { new DepthLevel(2, -1) }, empty, 0, 0, 1, false), assert);
        Throws(() => ColumnAggregation.Profile(new[] { new ProfileLevel(long.MaxValue, 1, 0, 0) }, 3), assert);
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0, -1 })
        {
            Throws(() => ColumnAggregation.ChooseTicksPerRow(invalid), assert);
            Throws(() => ColumnAggregation.ChooseTicksPerRow(1, invalid), assert);
        }
    }

    private static void Throws(Action action, Action<bool, string> assert)
    {
        try { action(); }
        catch (ArgumentException) { assert(true, "Invalid column input fails explicitly."); return; }
        assert(false, "Invalid column input must fail explicitly.");
    }
}
