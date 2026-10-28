using FootprintIndicator.Core;

public static class InstrumentCoverageTests
{
    public static void Run(Action<bool, string> assert)
    {
        // Metadata-driven examples, not a hardcoded symbol-to-tick table.
        // Fine FX, fractional, equity-index and negative-price cases share one engine.
        double[] tickSizes = { 1, .5, .25, .1, .005, .0001, .00005, .0000005, .03125 };
        var start = new DateTime(2026, 10, 26, 12, 0, 0);
        foreach (double size in tickSizes)
        foreach (long origin in new[] { 23001L, -23001L })
        {
            var engine = new MarketEngine(new EngineSettings { TickSize = size, FrameMilliseconds = 100, PersistenceMilliseconds = 0 });
            double bid = origin * size, ask = (origin + 1) * size;
            assert(MarketEngine.PriceToTick(bid, size) == origin, "Fine/fractional tick round trip failed.");
            assert(MarketEngine.PriceToTick(ask, size) == origin + 1, "Adjacent contract price collapsed onto same tick.");
            engine.Depth(start, BookSide.Bid, DepthMutation.Insert, 0, bid, 50);
            engine.Depth(start, BookSide.Ask, DepthMutation.Insert, 0, ask, 60);
            engine.Trade(start.AddMilliseconds(10), ask, 6, bid, ask);
            engine.Trade(start.AddMilliseconds(20), bid, 4, bid, ask);
            engine.Advance(start.AddMilliseconds(100));
            var snapshot = engine.Snapshot();
            assert(snapshot.InvalidEvents == 0 && snapshot.LateEvents == 0, "Valid instrument metadata caused rejected events.");
            assert(snapshot.Bids[0].Tick == origin && snapshot.Asks[0].Tick == origin + 1, "Order-book tick identity was lost.");
            assert(snapshot.SessionVolume == 10 && snapshot.CumulativeDelta == 2, "Classification or quantity depended on instrument price scale.");
            var scene = Visualization.Build(snapshot, new Viewport(start.Ticks, start.AddMilliseconds(100).Ticks, origin - 3, origin + 4, 800, 400),
                new DisplaySettings { UsePersistence = false, Clustering = ClusterMode.Time, ClusterMilliseconds = 100 });
            assert(scene.Bubbles.Sum(b => b.Total) == 10 && scene.Heat.Length > 0, "Heat/bubble rendering failed for non-index tick sizes.");
            assert(scene.Bubbles.All(b => double.IsFinite(b.X) && double.IsFinite(b.Y)), "Instrument produced nonfinite display coordinates.");
        }

        var continuity = new MarketEngine(new EngineSettings { TickSize = .00005 });
        continuity.Trade(start, 1.08005, 10, 1.08, 1.08005);
        continuity.ResetDepth(start.AddMilliseconds(5), "Provider reset without a completed heat frame");
        continuity.Trade(start.AddMilliseconds(10), 1.08005, 10, 1.08, 1.08005);
        var trades = continuity.Snapshot().Trades;
        assert(trades[0].Epoch != trades[1].Epoch, "Trade-only studies must see resets even when there are no depth frames.");
        assert(continuity.Snapshot().SessionVolume == 20, "Depth epoch change must not erase executed volume.");
    }
}
