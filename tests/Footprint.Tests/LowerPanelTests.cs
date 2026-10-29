using FootprintIndicator.Core;

public static class LowerPanelTests
{
    private static readonly long Origin = new DateTime(2026, 10, 26, 12, 0, 0).Ticks;

    public static void Run(Action<bool, string> assert)
    {
        void Reject(Action action, string message)
        {
            bool failed = false;
            try { action(); }
            catch (ArgumentException) { failed = true; }
            catch (InvalidOperationException) { failed = true; }
            catch (OverflowException) { failed = true; }
            assert(failed, message);
        }

        var empty = Build(Snapshot(), 0, 5000);
        assert(empty.VolumeBars.Length == 0 && empty.CvdPoints.Length == 0,
            "An empty tape produces no invented volume or CVD history.");
        assert(empty.MinDelta == 0 && empty.MaxDelta == 0 && empty.MaxVolume == 0,
            "Empty panel observations have a neutral zero baseline.");

        var first = Trade(50, 10, TradeSide.Buy, 10);
        var second = Trade(550, 4, TradeSide.Sell, 6);
        var unknown = Trade(850, 7, TradeSide.Unknown, 6);
        var next = Trade(1200, 3, TradeSide.Buy, 9);
        var snapshot = Snapshot(first, second, unknown, next);
        var initial = Build(snapshot, 0, 2000);
        assert(initial.VolumeBars.Length == 2 && initial.VolumeBars[0].FromTicks == Origin &&
            initial.VolumeBars[0].ToTicks == At(1000), "Volume buckets use fixed absolute event-time boundaries.");
        assert(initial.VolumeBars[0].Buy == 10 && initial.VolumeBars[0].Sell == 4 &&
            initial.VolumeBars[0].Unknown == 7 && initial.VolumeBars[0].Total == 21,
            "Independent buy, sell and unknown volume sum exactly within each bucket.");
        assert(initial.VolumeBars.Sum(b => b.Total) == 24 && initial.MaxVolume == 21,
            "Volume totals and the observed maximum agree with the captured tape.");
        assert(initial.CvdPoints.Select(p => p.CumulativeDelta).SequenceEqual(new long[] { 10, 6, 6, 9 }),
            "CVD copies captured deltas, including unchanged deltas on unknown-side executions.");
        assert(initial.CvdPoints[0].TimeTicks == first.TimeTicks && initial.MinDelta == 0 && initial.MaxDelta == 10,
            "CVD preserves real event time and includes zero in its observed range.");

        var panned = Build(snapshot, 650, 2200);
        assert(panned.VolumeBars[0].FromTicks == initial.VolumeBars[0].FromTicks &&
            panned.VolumeBars[0].Total == initial.VolumeBars[0].Total,
            "Panning within a completed bucket cannot regroup or reduce its historical volume.");
        assert(panned.CvdPoints[0].TimeTicks == second.TimeTicks && panned.CvdPoints[0].CumulativeDelta == 6,
            "CVD retains the latest actual predecessor so clipping can connect the left edge.");
        assert(panned.CvdPoints.All(p => p.TimeTicks != At(650)), "The left viewport boundary does not fabricate a delta sample.");

        var updated = Snapshot(first, second, unknown, next, Trade(2300, 20, TradeSide.Sell, -11));
        var live = Build(updated, 650, 2500);
        assert(live.VolumeBars[0].Total == initial.VolumeBars[0].Total &&
            live.VolumeBars[1].Total == initial.VolumeBars[1].Total,
            "Appending live executions leaves completed historical volume buckets unchanged.");
        assert(live.CvdPoints.Take(3).Select(p => p.CumulativeDelta).SequenceEqual(panned.CvdPoints.Select(p => p.CumulativeDelta)),
            "Appending live executions leaves every existing CVD value unchanged.");
        var historical = Build(updated, 0, 850);
        assert(historical.VolumeBars.Single().Total == 14 && historical.CvdPoints.Length == 2,
            "A historical right cutoff excludes executions at the boundary and all later data.");
        updated.CumulativeDelta = -999999;
        var noLookahead = Build(updated, 0, 850);
        assert(noLookahead.CvdPoints.Select(p => p.CumulativeDelta).SequenceEqual(new long[] { 10, 6 }),
            "The latest snapshot delta cannot replace earlier captured CVD values.");

        var afterReset = Snapshot(Trade(-100, 40, TradeSide.Buy, 40, 0), Trade(0, 2, TradeSide.Sell, -2, 1),
            Trade(300, 5, TradeSide.Buy, 3, 2));
        var currentSession = Build(afterReset, -500, 1000);
        assert(currentSession.CvdPoints.Length == 2 && currentSession.CvdPoints[0].CumulativeDelta == -2,
            "Earlier retained sessions cannot leak their different delta origins into current-session CVD.");
        assert(currentSession.CvdPoints.Select(p => p.Epoch).SequenceEqual(new[] { 1, 2 }),
            "Captured continuity epochs remain available to break disconnected line segments.");
        assert(currentSession.VolumeBars.Sum(b => b.Total) == 47, "Historical volume remains visible across session resets.");
        assert(Build(afterReset, -500, -1).CvdPoints.Length == 0,
            "A viewport ending before this session cannot show future session CVD.");

        var retained = Snapshot(Trade(5000, 2, TradeSide.Sell, 998), Trade(6000, 3, TradeSide.Buy, 1001));
        retained.DroppedTrades = 100;
        var partial = Build(retained, 0, 7000);
        assert(partial.CvdPoints[0].TimeTicks == At(5000) && partial.CvdPoints[0].CumulativeDelta == 998,
            "Retention does not rebase CVD or backfill uncaptured history with an invented zero.");

        var shuffled = Snapshot(next, unknown, first, second);
        var ordered = Build(shuffled, 0, 2000);
        assert(ordered.CvdPoints.Select(p => p.TimeTicks).SequenceEqual(initial.CvdPoints.Select(p => p.TimeTicks)),
            "Output remains chronological when snapshot execution order differs.");
        assert(ReferenceEquals(shuffled.Trades[0], next), "Building the lower panel cannot reorder snapshot arrays.");
        shuffled.Trades[0] = Trade(1200, 90, TradeSide.Sell, -90);
        assert(ordered.CvdPoints.Last().CumulativeDelta == 9 && ordered.VolumeBars.Last().Total == 3,
            "Built points and buckets remain independent of later snapshot-array replacement.");

        var subsecond = LowerPanel.Build(snapshot, At(90), At(950), 250);
        assert(subsecond.VolumeBars.Select(b => b.FromTicks).SequenceEqual(new[] { At(0), At(500), At(750) }),
            "Configured subsecond buckets remain anchored to absolute time and omit empty bins.");

        var scale = new LowerPanelScale();
        var neutral = scale.Update(Origin, 0, 0, 0);
        assert(neutral.MinDelta < 0 && neutral.MaxDelta > 0 && neutral.MaxVolume > 0,
            "A flat or empty series always has finite nonzero drawable scale spans.");
        var established = scale.Update(Origin, -100, 150, 300);
        assert(established.MinDelta == -200 && established.MaxDelta == 200 && established.MaxVolume == 500,
            "Scale bounds add headroom and round outward to readable 1/2/5 steps.");
        var smaller = scale.Update(Origin, -10, 20, 50);
        assert(smaller.MinDelta == established.MinDelta && smaller.MaxDelta == established.MaxDelta &&
            smaller.MaxVolume == established.MaxVolume, "Panning or retention cannot shrink established session scales.");
        var within = scale.Update(Origin, -199, 199, 499);
        assert(within.MinDelta == established.MinDelta && within.MaxDelta == established.MaxDelta &&
            within.MaxVolume == established.MaxVolume, "New extremes within reserved headroom cannot move historical pixels.");
        var expanded = scale.Update(Origin, -201, 700, 501);
        assert(expanded.MinDelta == -500 && expanded.MaxDelta == 1000 && expanded.MaxVolume == 1000,
            "Crossing a bound expands only the affected range with fresh headroom.");
        assert(established.MinDelta == -200 && established.MaxDelta == 200,
            "Earlier scale snapshots are immutable after subsequent expansions.");
        var newSession = scale.Update(At(86400000), -2, 3, 4);
        assert(newSession.MinDelta == -5 && newSession.MaxDelta == 5 && newSession.MaxVolume == 5,
            "A new session key resets old extremes before sizing its observations.");
        scale.Reset();
        var manualReset = scale.Update(At(86400000), 0, 0, 0);
        assert(manualReset.MinDelta == -1 && manualReset.MaxDelta == 1 && manualReset.MaxVolume == 1,
            "An explicit reset permits fresh sizing within the same session.");
        var hugeScale = scale.Update(Origin, long.MinValue, long.MaxValue, long.MaxValue);
        assert(double.IsFinite(hugeScale.MinDelta) && double.IsFinite(hugeScale.MaxDelta) &&
            hugeScale.MinDelta < long.MinValue && hugeScale.MaxDelta > long.MaxValue && hugeScale.MaxVolume > long.MaxValue,
            "Extreme signed deltas and volumes produce finite outward scales without integer overflow.");

        var huge = Build(Snapshot(Trade(0, long.MaxValue - 1, TradeSide.Buy, 0), Trade(1, 1, TradeSide.Sell, 0)), 0, 1000);
        assert(huge.VolumeBars.Single().Total == long.MaxValue, "Combined bucket volume preserves exact full-range integer counts.");
        Reject(() => Build(Snapshot(Trade(0, long.MaxValue, TradeSide.Buy, 0), Trade(1, 1, TradeSide.Sell, 0)), 0, 1000),
            "Cross-side bucket volume overflow fails explicitly.");
        Reject(() => Build(Snapshot(Trade(0, long.MaxValue, TradeSide.Buy, 0), Trade(1, 1, TradeSide.Buy, 0)), 0, 1000),
            "Single-side bucket volume overflow fails explicitly.");
        Reject(() => Build(Snapshot(Trade(0, 0, TradeSide.Buy, 0)), 0, 1000), "Zero execution volume is invalid input.");
        Reject(() => Build(Snapshot(Trade(0, -1, TradeSide.Buy, 0)), 0, 1000), "Negative execution volume is invalid input.");
        Reject(() => Build(Snapshot(Trade(0, 1, (TradeSide)99, 0)), 0, 1000), "Unknown enum values cannot be counted silently.");
        Reject(() => Build(Snapshot(), 0, 0), "An empty viewport range is rejected.");
        Reject(() => LowerPanel.Build(Snapshot(), Origin, At(1000), 0), "Zero bucket duration is rejected.");
        Reject(() => LowerPanel.Build(Snapshot(), Origin, At(1000), int.MaxValue), "Excessive bucket duration is rejected.");
        Reject(() => scale.Update(Origin, 1, -1, 1), "Reversed observed delta bounds are rejected.");
        Reject(() => scale.Update(Origin, 0, 0, -1), "Negative observed maximum volume is rejected.");
    }

    private static long At(long milliseconds) => Origin + milliseconds * TimeSpan.TicksPerMillisecond;
    private static TradeRecord Trade(long milliseconds, long volume, TradeSide side, long delta, int epoch = 0)
        => new TradeRecord(At(milliseconds), 100, volume, side, milliseconds, delta, epoch);
    private static EngineSnapshot Snapshot(params TradeRecord[] trades)
        => new EngineSnapshot { TimeTicks = At(10000), SessionStartTicks = Origin, Trades = trades };
    private static LowerPanelResult Build(EngineSnapshot snapshot, long fromMilliseconds, long toMilliseconds)
        => LowerPanel.Build(snapshot, At(fromMilliseconds), At(toMilliseconds), 1000);
}
