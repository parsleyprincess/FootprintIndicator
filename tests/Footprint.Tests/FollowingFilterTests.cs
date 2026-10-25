using FootprintIndicator.Core;

public static class FollowingFilterTests
{
    private static readonly long Origin = new DateTime(2026, 10, 23, 14, 30, 0).Ticks;

    public static void Run(Action<bool, string> assert)
    {
        var settings = Settings();
        var frames = Enumerable.Range(0, 9).Select(i => Frame(i, 100 + i)).ToArray();
        var mask = LiquidityNoiseFilter.BuildMask(frames, settings);
        for (int i = 0; i < 4; i++)
            assert(!mask.Contains(Moving(frames[i])), "Following filter must not backdate its three-move confirmation.");
        for (int i = 4; i < frames.Length; i++)
            assert(mask.Contains(Moving(frames[i])), "Coherent above-background following band should be suppressed after warm-up.");
        foreach (var frame in frames)
            assert(!mask.Contains(frame.Cells.Single(c => c.Tick == 80)), "Fixed-price wall must remain visible.");
        assert(mask.Count == 5, "Only confirmed following cells should be hidden.");

        var prefix = LiquidityNoiseFilter.BuildMask(frames.Take(6).ToArray(), settings);
        foreach (var frame in frames.Take(6))
            foreach (var cell in frame.Cells)
                assert(prefix.Contains(cell) == mask.Contains(cell), "Later frames cannot change historical prefix decisions.");
        assert(LiquidityNoiseFilter.BuildMask(Enumerable.Range(0, 20).Select(i => Frame(i, 100)).ToArray(), settings).Count == 0,
            "Repeated samples at unchanged best price must not count as BBO moves.");

        var changedSize = Enumerable.Range(0, 9).Select(i => Frame(i, 100 + i, movingSize: i % 2 == 0 ? 100 : 300)).ToArray();
        assert(LiquidityNoiseFilter.BuildMask(changedSize, settings).Count == 0,
            "Incoherent size changes must not be called a stable following pattern.");

        var epoch = Enumerable.Range(0, 9).Select(i => Frame(i, 100 + i, epoch: i < 4 ? 0 : 1)).ToArray();
        var epochMask = LiquidityNoiseFilter.BuildMask(epoch, settings);
        for (int i = 4; i < 8; i++) assert(!epochMask.Contains(Moving(epoch[i])), "Epoch change must restart warm-up.");
        assert(epochMask.Contains(Moving(epoch[8])), "Fresh evidence after epoch reset may qualify again.");

        var missing = Enumerable.Range(0, 9).Select(i => Frame(i, 100 + i, movingSize: i == 3 ? 10 : 100)).ToArray();
        var missingMask = LiquidityNoiseFilter.BuildMask(missing, settings);
        for (int i = 4; i < 8; i++) assert(!missingMask.Contains(Moving(missing[i])), "Missing large offset must break continuity.");
        assert(missingMask.Contains(Moving(missing[8])), "Offset can warm up again after missing evidence.");

        var gap = Enumerable.Range(0, 9).Select(i => Frame(i, 100 + i, gapMilliseconds: i < 4 ? 0 : 2000)).ToArray();
        var gapMask = LiquidityNoiseFilter.BuildMask(gap, settings);
        for (int i = 4; i < 8; i++) assert(!gapMask.Contains(Moving(gap[i])), "Observation gap must restart warm-up even without epoch change.");
        assert(gapMask.Contains(Moving(gap[8])), "Fresh post-gap observations may qualify again.");

        var stale = Enumerable.Range(0, 50).Select(i => Frame(i, 100 + Math.Min(i, 3))).ToArray();
        assert(!LiquidityNoiseFilter.BuildMask(stale, settings).Contains(Moving(stale[49])),
            "Old movements must expire; a now-stationary level cannot be hidden forever.");

        var asks = Enumerable.Range(0, 9).Select(i => AskFrame(i, 100 + i)).ToArray();
        var askMask = LiquidityNoiseFilter.BuildMask(asks, settings);
        assert(askMask.Contains(asks[8].Cells.Single(c => c.RawQuantity == 100)), "Ask-side following uses positive distance correctly.");
        assert(!askMask.Contains(asks[8].Cells.Single(c => c.Tick == 140)), "Fixed ask wall remains visible.");

        var disabled = Settings(); disabled.SuppressFollowingLiquidity = false;
        assert(LiquidityNoiseFilter.BuildMask(frames, disabled).Count == 0, "Disabled filter leaves all observations available.");
        var invalid = Settings(); invalid.FollowingMinimumMoves = 0;
        bool threw = false;
        try { LiquidityNoiseFilter.BuildMask(frames, invalid); } catch (ArgumentOutOfRangeException) { threw = true; }
        assert(threw, "Enabled invalid settings must fail explicitly.");

        var snapshot = new EngineSnapshot
        {
            Frames = frames,
            Trades = new[] {
                new TradeRecord(Origin + 1, 100, 5, TradeSide.Buy, 1, 5),
                new TradeRecord(Origin + 2, 100, 3, TradeSide.Sell, 2, 2),
                new TradeRecord(Origin + 3, 100, 2, TradeSide.Unknown, 3, 2) }
        };
        var viewport = new Viewport(Origin, frames.Last().ToTicks, 70, 125, 800, 400);
        var raw = Visualization.Build(snapshot, viewport, disabled);
        var filtered = Visualization.Build(snapshot, viewport, settings);
        double rawExposure = raw.Heat.Sum(c => c.Quantity * c.Width * c.Height);
        double filteredExposure = filtered.Heat.Sum(c => c.Quantity * c.Width * c.Height);
        assert(filteredExposure < rawExposure, "Visualization must apply the following mask before heatmap construction.");
        assert(raw.VisibleProfile.Sum(p => p.Total) == 10 && filtered.VisibleProfile.Sum(p => p.Total) == 10,
            "Following filter must not change executed-volume profiles.");
        assert(raw.Bubbles.Sum(b => b.Total) == 10 && filtered.Bubbles.Sum(b => b.Total) == 10,
            "Following filter must not change execution bubbles.");
        assert(snapshot.Frames.Sum(f => f.Cells.Count(c => c.RawQuantity == 100)) == 9,
            "Rendering must not mutate stored raw quantities.");

        // View starts after the detector warm-up. It must still use earlier retained frames.
        var lateView = new Viewport(frames[4].FromTicks, frames.Last().ToTicks, 70, 125, 800, 400);
        var lateFiltered = Visualization.Build(snapshot, lateView, settings);
        var lateRaw = Visualization.Build(snapshot, lateView, disabled);
        assert(lateFiltered.Heat.Sum(c => c.Quantity * c.Width * c.Height) < lateRaw.Heat.Sum(c => c.Quantity * c.Width * c.Height),
            "Panning cannot reset detector evidence to the visible left edge.");
    }

    private static DisplaySettings Settings() => new()
    {
        SuppressFollowingLiquidity = true, FollowingMinimumMoves = 3,
        FollowingWindowMilliseconds = 10000, FollowingSizeRatio = 3, FollowingSizeTolerance = .35,
        UsePersistence = false, AutomaticContrast = false, ManualMaximum = 200
    };

    private static HeatFrame Frame(int index, long best, int epoch = 0, double movingSize = 100, int gapMilliseconds = 0)
    {
        var cells = Enumerable.Range(0, 13).Select(i => new HeatCell(best - i, BookSide.Bid, i == 4 ? movingSize : 10, i == 4 ? movingSize : 10)).ToList();
        cells.Add(new HeatCell(80, BookSide.Bid, 200, 200));
        long from = Origin + (index * 250L + gapMilliseconds) * TimeSpan.TicksPerMillisecond;
        return new HeatFrame(from, from + 250 * TimeSpan.TicksPerMillisecond, epoch, cells.ToArray(), best, best + 1);
    }

    private static HeatCell Moving(HeatFrame frame) => frame.Cells.Single(c => c.Tick == frame.BestBidTick!.Value - 4);

    private static HeatFrame AskFrame(int index, long best)
    {
        var cells = Enumerable.Range(0, 13).Select(i => new HeatCell(best + i, BookSide.Ask, i == 4 ? 100 : 10, i == 4 ? 100 : 10)).ToList();
        cells.Add(new HeatCell(140, BookSide.Ask, 200, 200));
        long from = Origin + index * 250L * TimeSpan.TicksPerMillisecond;
        return new HeatFrame(from, from + 250 * TimeSpan.TicksPerMillisecond, 0, cells.ToArray(), best - 1, best);
    }
}
