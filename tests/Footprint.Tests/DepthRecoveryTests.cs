using FootprintIndicator.Core;

public static class DepthRecoveryTests
{
    private static readonly DateTime Origin = new(2026, 10, 27, 12, 0, 0);
    private static DateTime At(int milliseconds) => Origin.AddMilliseconds(milliseconds);
    private static DepthLevel[] Rows(params long[] pairs)
    {
        var rows = new DepthLevel[pairs.Length / 2];
        for (int i = 0; i < rows.Length; i++) rows[i] = new DepthLevel(pairs[i * 2], pairs[i * 2 + 1]);
        return rows;
    }
    private static MarketEngine Engine(int persistence = 100, int depth = 20, int frames = 100, int trades = 100) => new(new EngineSettings
    {
        TickSize = .25, FrameMilliseconds = 100, PersistenceMilliseconds = persistence,
        MaxDepthLevels = depth, MaxFrames = frames, MaxTrades = trades, RetentionMinutes = 30
    });
    private static double Exposure(EngineSnapshot snapshot, long tick, bool persistent) => snapshot.Frames.Sum(f =>
        (f.ToTicks - f.FromTicks) / (double)TimeSpan.TicksPerMillisecond * f.Cells.Where(c => c.Tick == tick)
            .Sum(c => persistent ? c.PersistentQuantity : c.RawQuantity));

    public static void Run(Action<bool, string> assert)
    {
        var engine = Engine();
        engine.Depth(At(0), BookSide.Ask, DepthMutation.Update, 6, 26, 10);
        assert(engine.Snapshot().InvalidEvents == 1, "Missing initial positional prefix should be reported.");
        assert(engine.DepthSnapshot(At(10), Rows(100, 20, 99, 30, 98, 40), Rows(101, 25, 102, 35, 103, 45)),
            "Authoritative state must recover immediately without waiting for every missing positional Add.");
        var recovered = engine.Snapshot();
        assert(recovered.Bids.Length == 3 && recovered.Asks.Length == 3 && recovered.HasDepth,
            "Recovery must rebuild both supplied sides, not leave a one-level prefix.");
        int epoch = recovered.Epoch;
        // Providers may change several rows before dispatching a callback. The complete
        // observed state is independent of callback Position, Remove.Price, or their order.
        assert(engine.DepthSnapshot(At(20), Rows(100, 21, 99, 30, 98, 40), Rows(102, 36, 103, 45, 104, 15)),
            "An authoritative snapshot must replace a shifted ladder without a removal-price dependency.");
        for (int i = 3; i <= 100; i++)
            assert(engine.DepthSnapshot(At(i * 10), Rows(100, 21, 99, 30, 98, 40), Rows(102, 36, 103, 45, 104, 15)),
                "Repeated good snapshots cannot reenter a positional-gap rejection loop.");
        recovered = engine.Snapshot();
        assert(recovered.InvalidEvents == 1 && recovered.DepthEvents == 100, "Snapshot updates must be accepted and counted once.");
        assert(recovered.Epoch == epoch, "Each complete refresh must not create a false disconnect epoch.");
        assert(Exposure(recovered, 99, true) > 26000, "Unchanged levels must mature across refreshes.");
        assert(recovered.Frames.Zip(recovered.Frames.Skip(1)).All(p => p.First.ToTicks == p.Second.FromTicks),
            "Steady observed snapshots should form continuous heat intervals.");

        var exact = Engine();
        exact.DepthSnapshot(At(0), Rows(100, 10), Rows(101, 20));
        exact.DepthSnapshot(At(50), Rows(100, 30), Rows(101, 20));
        exact.DepthSnapshot(At(100), Rows(100, 30), Rows(101, 20));
        exact.Advance(At(200));
        var beforeBad = exact.Snapshot();
        assert(Exposure(beforeBad, 100, false) == 5000, "Snapshot integration must apply old quantity before each replacement.");
        assert(Exposure(beforeBad, 100, true) == 2000, "New quantity must not inherit older persistence on snapshot refresh.");
        foreach (var malformed in new[] { Rows(100, -1), Rows(100, 1, 100, 2), Rows(99, 1, 100, 2), new DepthLevel[] { null! } })
        {
            assert(!exact.DepthSnapshot(Origin.AddYears(20), malformed, Rows(101, 20)), "Malformed future snapshot must be rejected.");
            var afterBad = exact.Snapshot();
            assert(afterBad.TimeTicks == beforeBad.TimeTicks && afterBad.Bids[0].Quantity == 30 && afterBad.Epoch == beforeBad.Epoch,
                "Rejected snapshot must not move the clock, replace the book, or backdate persistence.");
        }
        assert(!exact.DepthSnapshot(At(250), Rows(100, 50), Rows(102, 10, 101, 10)), "Ask ordering must be validated before accepting bids.");
        assert(exact.Snapshot().Bids[0].Quantity == 30, "One invalid side cannot partially commit the other side.");
        assert(!exact.DepthSnapshot(At(250), null!, Rows(101, 10)), "Null snapshot must not mean an observed empty book.");

        var capped = Engine(depth: 2);
        capped.DepthSnapshot(At(0), Rows(100, 10, 99, 20, 98, 30), Rows(101, 15, 102, 25, 103, 35));
        assert(capped.Snapshot().Bids.Length == 2 && capped.Snapshot().Status.Contains("unobserved"),
            "Snapshot capture limit must expose its truncated coverage.");
        capped.DepthSnapshot(At(50), Rows(99, 20, 98, 30, 97, 40), Rows(101, 15, 102, 25));
        capped.Advance(At(100));
        var capView = capped.Snapshot();
        assert(capView.Bids[0].Tick == 99 && capView.Bids[1].Tick == 98, "Shifted authoritative book must refill previously unobserved tail.");
        assert(Exposure(capView, 98, false) == 1500 && Exposure(capView, 98, true) == 0,
            "A newly visible deeper row must not acquire invented pre-observation history or age.");

        var gap = Engine(persistence: 0);
        gap.DepthSnapshot(At(0), Rows(100, 10), Rows(101, 20));
        gap.SetConnected(At(100), false);
        assert(!gap.DepthSnapshot(At(200), Rows(100, 30), Rows(101, 40)), "A snapshot alone must not override a known disconnection.");
        assert(gap.Snapshot().TimeTicks == At(100).Ticks, "Rejected disconnected input cannot extend stale depth.");
        gap.SetConnected(At(300), true);
        gap.DepthSnapshot(At(300), Rows(100, 30), Rows(101, 40));
        gap.Advance(At(400));
        var gapView = gap.Snapshot();
        assert(gapView.Frames.All(f => f.ToTicks <= At(100).Ticks || f.FromTicks >= At(300).Ticks),
            "Rebuilt snapshots cannot bridge a known observation outage.");
        assert(Exposure(gapView, 100, false) == 4000, "Disconnect gap must not count as observed standing quantity.");

        var zeros = Engine(persistence: 0);
        zeros.Depth(At(0), BookSide.Bid, DepthMutation.Insert, 0, 25, 10);
        zeros.Depth(At(0), BookSide.Bid, DepthMutation.Insert, 1, 24.75, 20);
        zeros.Depth(At(10), BookSide.Bid, DepthMutation.Update, 0, 25, 0);
        assert(zeros.Snapshot().Bids.Length == 2 && zeros.Snapshot().Bids[0].Quantity == 0,
            "Zero quantity Update must retain positional row until explicit Remove.");
        zeros.Depth(At(20), BookSide.Bid, DepthMutation.Update, 1, 24.75, 25);
        zeros.Depth(At(30), BookSide.Bid, DepthMutation.Remove, 0, 25, 0);
        zeros.Advance(At(100));
        assert(zeros.Snapshot().InvalidEvents == 0 && zeros.Snapshot().Bids.Length == 1 && zeros.Snapshot().Bids[0].Quantity == 25,
            "An explicit Remove after a zero update must preserve later row positions.");
        var onlyZero = Engine();
        onlyZero.DepthSnapshot(At(0), Rows(100, 0), Rows(101, 0));
        onlyZero.Advance(At(100));
        assert(!onlyZero.Snapshot().HasDepth && onlyZero.Snapshot().Frames.Length == 0,
            "Empty displayed quantities must not fabricate BBO or heat.");

        foreach (double tickSize in new[] { .1, .00005, .0001, .5 })
        {
            var instrument = new MarketEngine(new EngineSettings { TickSize = tickSize, FrameMilliseconds = 100, PersistenceMilliseconds = 0 });
            long tick = MarketEngine.PriceToTick(22001 * tickSize, tickSize);
            instrument.DepthSnapshot(At(0), Rows(tick, 10, tick - 1, 20), Rows(tick + 1, 15, tick + 2, 25));
            instrument.Advance(At(100));
            assert(instrument.Snapshot().HasDepth && Exposure(instrument.Snapshot(), tick, false) == 1000,
                "GC, currency and fractional-index metadata must share exact snapshot integration.");
        }

        RestoreTests(assert);
    }

    private static void RestoreTests(Action<bool, string> assert)
    {
        var live = Engine(persistence: 0);
        live.DepthSnapshot(At(0), Rows(100, 20), Rows(101, 30));
        live.Trade(At(50), 25.25, 5, 25, 25.25);
        live.Trade(At(150), 25, 2, 25, 25.25);
        live.Advance(At(300));
        var archive = live.Snapshot();
        var restored = Engine(persistence: 0, frames: 2, trades: 1);
        restored.RestoreHistory(archive);
        var snapshot = restored.Snapshot();
        assert(!snapshot.Connected && !snapshot.HasDepth && snapshot.Bids.Length == 0 && snapshot.Asks.Length == 0,
            "Restoring capture history must not resurrect old live orders.");
        assert(snapshot.Frames.Length == 2 && snapshot.Trades.Length == 1 && snapshot.DroppedFrames == 1 && snapshot.DroppedTrades == 1,
            "Restore must obey current history budgets and report the trimming.");
        assert(snapshot.SessionVolume == 7 && snapshot.CumulativeDelta == 3 && snapshot.Profile.Sum(p => p.Total) == 7,
            "Trimming restored history must not erase the independent captured session totals.");
        assert(snapshot.Epoch != archive.Epoch && snapshot.Trades[0].Epoch == archive.Trades[1].Epoch,
            "New live epoch must differ while historical trade epoch identity is preserved.");
        archive.Frames[^1].Cells[0] = new HeatCell(1, BookSide.Bid, 999, 999);
        archive.Trades[1] = new TradeRecord(At(150).Ticks, 1, 999, TradeSide.Buy, 2, 999);
        assert(restored.Snapshot().Frames.All(f => f.Cells.All(c => c.Tick != 1)) && restored.Snapshot().Trades[0].Volume == 2,
            "Restore must detach mutable arrays from the supplied archive.");
        restored.SetConnected(At(500), true);
        restored.DepthSnapshot(At(500), Rows(100, 40), Rows(101, 60));
        restored.Trade(At(550), 25.25, 1, 25, 25.25);
        restored.Advance(At(600));
        snapshot = restored.Snapshot();
        assert(snapshot.SessionVolume == 8 && snapshot.CumulativeDelta == 4 && snapshot.Trades[0].Sequence == 3,
            "Resumed capture must continue counters and execution sequence without duplicate IDs.");
        assert(snapshot.Frames.All(f => f.ToTicks <= At(300).Ticks || f.FromTicks >= At(500).Ticks),
            "A saved archive must not extend its last live book through the closed interval.");
        bool threw = false;
        try { restored.RestoreHistory(live.Snapshot()); } catch (InvalidOperationException) { threw = true; }
        assert(threw, "A used live engine must reject replacing itself with an archive.");

        void Reject(Action<EngineSnapshot> corrupt, string message)
        {
            var bad = live.Snapshot(); corrupt(bad); var clean = Engine(); bool rejected = false;
            try { clean.RestoreHistory(bad); } catch (ArgumentException) { rejected = true; } catch (OverflowException) { rejected = true; }
            assert(rejected && clean.Snapshot().DepthEvents == 0 && clean.Snapshot().TradeEvents == 0 && clean.Snapshot().Frames.Length == 0,
                "Invalid archive must leave a fresh engine untouched: " + message);
            clean.RestoreHistory(live.Snapshot());
            assert(clean.Snapshot().SessionVolume == 7, "Valid recovery after rejected archive must remain possible.");
        }
        Reject(h => h.TickSize = .1, "different instrument tick size");
        Reject(h => h.SessionVolume++, "profile/volume mismatch");
        Reject(h => h.CumulativeDelta++, "profile/delta mismatch");
        Reject(h => h.Trades[1] = new TradeRecord(At(150).Ticks, 100, 2, TradeSide.Sell, 1, 3), "duplicate sequence");
        Reject(h => h.Frames[1] = h.Frames[0], "overlapping frames");
        Reject(h => h.Frames[0].Cells[0] = new HeatCell(100, BookSide.Bid, 1, 2), "persistence greater than raw size");
        Reject(h => h.SessionVwap = double.PositiveInfinity, "nonfinite session average");
        Reject(h => h.TimeTicks = At(100).Ticks, "data from beyond the archived clock");
    }
}
