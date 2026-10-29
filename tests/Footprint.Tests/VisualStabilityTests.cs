using FootprintIndicator.Core;

public static class VisualStabilityTests
{
    private static readonly long Start = new DateTime(2026, 10, 28, 12, 0, 0).Ticks;
    private static long At(double milliseconds) => Start + (long)Math.Round(milliseconds * TimeSpan.TicksPerMillisecond);
    private static TradeRecord T(double ms, long price, long volume, TradeSide side, long sequence, int epoch = 0)
        => new(At(ms), price, volume, side, sequence, 0, epoch);
    private static EngineSnapshot Snapshot(params TradeRecord[] trades) => new() { TickSize = .25, Trades = trades };
    private static Viewport View(double from = 0, double to = 1800, double min = -120, double max = -80, int width = 180)
        => new(At(from), At(to), min, max, width, 200);
    private static DisplaySettings Settings(ClusterMode mode) => new()
    {
        Clustering = mode, ClusterVolume = 40, ClusterMilliseconds = 80, SmartPixels = 8,
        BubbleReferenceVolume = 100, BubbleScale = 12, MaxBubbleRadius = 100,
        UsePersistence = false, HeatColumnPixels = 1, MinimumLiquidity = 0
    };

    private static object Identity(Bubble b) => (b.FromTicks, b.ToTicks, b.TimeTicks, b.PriceTick,
        b.Buy, b.Sell, b.Unknown, b.Radius, b.MinPriceTick, b.MaxPriceTick);
    private static object[] Interior(RenderScene scene) => scene.Bubbles
        .Where(b => b.FromTicks >= At(200) && b.ToTicks < At(700)).Select(Identity).ToArray();

    public static void Run(Action<bool, string> assert)
    {
        var simultaneous = Snapshot(T(100, -100, 2, TradeSide.Buy, 1), T(100, -100, 3, TradeSide.Sell, 2));
        var individual = Visualization.Build(simultaneous, View(), Settings(ClusterMode.Individual));
        assert(individual.Bubbles.Length == 2 && individual.Bubbles.Select(b => b.Total).SequenceEqual(new long[] { 2, 3 }),
            "Individual mode preserves separate prints even when time and price are identical.");
        var sameTime = Enumerable.Range(0, 70).Select(i => T(100, -100, i + 1, (TradeSide)(i % 3), i + 1)).ToArray();
        var ordered = Visualization.Build(Snapshot(sameTime.Reverse().ToArray()), View(), Settings(ClusterMode.Individual));
        var extended = Visualization.Build(Snapshot(sameTime.Concat(new[] { T(200, -99, 500, TradeSide.Buy, 71) }).ToArray()),
            View(), Settings(ClusterMode.Individual));
        assert(ordered.Bubbles.Select(b => b.Total).SequenceEqual(Enumerable.Range(1, 70).Select(i => (long)i)) &&
            ordered.Bubbles.Select(Identity).SequenceEqual(extended.Bubbles.Take(70).Select(Identity)),
            "Equal-time/equal-price individual prints retain sequence paint order when unrelated live prints arrive.");
        var history = Enumerable.Range(0, 130)
            .Select(i => T(i * 7, -103 + i / 4 % 7, 3 + i % 5, (TradeSide)(i / 2 % 3), i + 1)).ToArray();
        var tail = Enumerable.Range(0, 50)
            .Select(i => T(1000 + i * 9, -103 + i % 7, 1000 + i, (TradeSide)(i % 3), 131 + i)).ToArray();
        var complete = Snapshot(history.Concat(tail).ToArray());
        foreach (var mode in Enum.GetValues<ClusterMode>())
        {
            var settings = Settings(mode);
            var prefix = Visualization.Build(Snapshot(history), View(), settings);
            var appended = Visualization.Build(complete, View(), settings);
            var panned = Visualization.Build(complete, View(37, 1837), settings);
            var autoscaled = Visualization.Build(complete, View(37, 1837, -120.2, -79.8), settings);
            var expected = Interior(prefix);
            assert(expected.Length > 2, $"{mode}: stability sample must contain several historical groups.");
            assert(expected.SequenceEqual(Interior(appended)), $"{mode}: large future executions changed closed historical groups, radii, or color composition.");
            assert(expected.SequenceEqual(Interior(panned)), $"{mode}: continuous time scrolling regrouped historical executions.");
            assert(expected.SequenceEqual(Interior(autoscaled)), $"{mode}: minor price autoscale regrouped historical executions.");
            assert(appended.BubbleReferenceVolume == 100 && panned.BubbleReferenceVolume == 100,
                $"{mode}: bubble scaling must use the configured absolute reference.");
            foreach (TradeSide side in Enum.GetValues<TradeSide>())
            {
                long actual = panned.Bubbles.Sum(b => side == TradeSide.Buy ? b.Buy : side == TradeSide.Sell ? b.Sell : b.Unknown);
                long expectedVolume = complete.Trades.Where(t => t.TimeTicks >= At(37) && t.TimeTicks < At(1837) && t.Side == side).Sum(t => t.Volume);
                assert(actual == expectedVolume, $"{mode}: panning lost or fabricated {side} executions.");
            }
            assert(panned.VisibleProfile.Sum(p => p.Total) == complete.Trades.Where(t => t.TimeTicks >= At(37)).Sum(t => t.Volume),
                $"{mode}: profile must retain exactly the visible executions.");
        }

        // A zoom change intentionally changes the target grid resolution.
        foreach (var mode in new[] { ClusterMode.Pixel, ClusterMode.Smart })
        {
            var settings = Settings(mode);
            var dense = Snapshot(Enumerable.Range(0, 90).Select(i => T(200 + i, -100, 1, (TradeSide)(i % 3), i + 1)).ToArray());
            var coarse = Visualization.Build(dense, View(width: 18), settings);
            var fine = Visualization.Build(dense, View(width: 1800), settings);
            assert(coarse.Bubbles.Length < fine.Bubbles.Length, $"{mode}: intentional zoom should change grouping resolution.");
            assert(coarse.Bubbles.Sum(b => b.Total) == 90 && fine.Bubbles.Sum(b => b.Total) == 90,
                $"{mode}: zoom changed the execution ledger.");
        }

        var volumeTrades = Enumerable.Range(0, 12).Select(i => T(i * 10, -100, 3, TradeSide.Buy, i + 1)).ToArray();
        var volumeSettings = Settings(ClusterMode.Volume); volumeSettings.ClusterVolume = 10;
        var clipped = Visualization.Build(Snapshot(volumeTrades), View(15, 115), volumeSettings);
        assert(clipped.Bubbles.Select(b => b.Total).SequenceEqual(new long[] { 6, 12, 12 }),
            "Volume grouping must retain offscreen boundary evidence and clip only the first visible group.");
        VolumeRetentionAnchors(assert);
        var priceRun = Snapshot(T(100, -100, 3, TradeSide.Buy, 1), T(200, 500, 10, TradeSide.Buy, 2), T(300, -100, 4, TradeSide.Buy, 3));
        foreach (var mode in new[] { ClusterMode.Price, ClusterMode.PriceAndSide })
        {
            var scene = Visualization.Build(priceRun, View(), Settings(mode));
            assert(scene.Bubbles.Select(b => b.Total).SequenceEqual(new long[] { 3, 4 }),
                $"{mode}: an offscreen price run still separates two visible runs.");
        }

        foreach (var mode in Enum.GetValues<ClusterMode>())
        {
            var resets = Snapshot(T(100, -100, 3, TradeSide.Unknown, 1), T(100, -100, 4, TradeSide.Unknown, 2, 1));
            var scene = Visualization.Build(resets, View(width: 1), Settings(mode));
            assert(scene.Bubbles.Length == 2 && scene.Bubbles.Sum(b => b.Unknown) == 7,
                $"{mode}: clustering must separate reset epochs and preserve unknown aggression.");
        }

        var precision = Snapshot(T(200, -103, 1, TradeSide.Buy, 1), T(200.0003, -99, 3, TradeSide.Sell, 2));
        var preciseSettings = Settings(ClusterMode.Time); preciseSettings.ClusterMilliseconds = 1000;
        var precise = Visualization.Build(precision, View(), preciseSettings).Bubbles.Single();
        assert(precise.TimeTicks == At(200) + 2 && precise.MinPriceTick == -103 && precise.MaxPriceTick == -99,
            "Weighted timestamps retain single-DateTime-tick precision and price extents retain actual execution extremes.");
        var huge = Snapshot(T(100, -100, long.MaxValue, TradeSide.Unknown, 1));
        assert(Visualization.Build(huge, View(), Settings(ClusterMode.Volume)).Bubbles.Single().Unknown == long.MaxValue,
            "Whole-trade boundary evidence must not overflow on one maximum-volume execution.");

        var calibrated = Settings(ClusterMode.Time);
        var calibratedPrints = Snapshot(T(100, -100, 100, TradeSide.Buy, 1), T(300, -100, 400, TradeSide.Sell, 2));
        var radii = Visualization.Build(calibratedPrints, View(), calibrated).Bubbles.Select(b => b.Radius).ToArray();
        assert(radii.SequenceEqual(new[] { 12d, 24d }), "Fixed reference must retain square-root area scaling for absolute quantities.");
        calibrated.BubbleReferenceVolume = 400;
        assert(Visualization.Build(calibratedPrints, View(), calibrated).Bubbles.Select(b => b.Radius).SequenceEqual(new[] { 6d, 12d }),
            "Changing the reference must intentionally recalibrate every bubble consistently.");

        HeatFloor(assert);
        foreach (double invalid in new[] { -1d, 100, double.NaN, double.PositiveInfinity })
        {
            var invalidSettings = Settings(ClusterMode.Time); invalidSettings.HeatFloorPercent = invalid;
            ExpectInvalid(() => Visualization.Build(Snapshot(), View(), invalidSettings), assert, "Invalid heat floor was accepted.");
        }
        foreach (double invalid in new[] { 0d, -1, double.NaN, double.PositiveInfinity })
        {
            var invalidSettings = Settings(ClusterMode.Time); invalidSettings.BubbleReferenceVolume = invalid;
            ExpectInvalid(() => Visualization.Build(Snapshot(), View(), invalidSettings), assert, "Invalid bubble reference was accepted.");
        }
        foreach (int invalid in new[] { 0, -1, 86401 })
        {
            var invalidSettings = Settings(ClusterMode.Volume); invalidSettings.VolumeAnchorSeconds = invalid;
            ExpectInvalid(() => Visualization.Build(Snapshot(), View(), invalidSettings), assert, "Invalid volume anchor duration was accepted.");
        }
    }

    private static void VolumeRetentionAnchors(Action<bool, string> assert)
    {
        var settings = Settings(ClusterMode.Volume); settings.ClusterVolume = 10;
        var early = Enumerable.Range(0, 3).Select(i => T(i * 100, -100, 3, TradeSide.Buy, i + 1));
        var later = Enumerable.Range(0, 12).Select(i => T(60000 + i * 100, -100, 3,
            (TradeSide)(i % 3), i + 4));
        var trades = early.Concat(later).ToArray();
        var complete = Visualization.Build(Snapshot(trades), View(0, 120000), settings);
        var trimmed = Visualization.Build(Snapshot(trades.Skip(1).ToArray()), View(0, 120000), settings);
        var expected = complete.Bubbles.Where(b => b.FromTicks >= At(60000)).Select(Identity).ToArray();
        assert(expected.Length == 3 && expected.SequenceEqual(trimmed.Bubbles.Where(b => b.FromTicks >= At(60000)).Select(Identity)),
            "Trimming a partial prior volume anchor cannot rephase later retained groups or change their sizes and colors.");
        assert(complete.Bubbles[0].Total == 9 && trimmed.Bubbles[0].Total == 6 &&
            trimmed.Bubbles.Sum(b => b.Total) == trades.Skip(1).Sum(t => t.Volume),
            "The partial oldest anchor reports only retained executions and preserves total volume.");

        settings.VolumeAnchorSeconds = 1;
        settings.ClusterVolume = 100;
        var boundary = Visualization.Build(Snapshot(T(999, -100, 3, TradeSide.Buy, 1),
            T(1000, -100, 4, TradeSide.Sell, 2), T(1001, -100, 200, TradeSide.Unknown, 3),
            T(1002, -100, 5, TradeSide.Buy, 4)), View(0, 2000), settings);
        assert(boundary.Bubbles.Select(b => b.Total).SequenceEqual(new long[] { 3, 204, 5 }),
            "Volume runs close at exact configured time anchors or after the whole threshold-crossing execution.");
        assert(boundary.Bubbles[0].ToTicks == At(999) && boundary.Bubbles[1].FromTicks == At(1000) &&
            boundary.Bubbles[1].Unknown == 200,
            "Absolute anchor boundaries preserve real execution timestamps and never split an oversized trade.");
    }

    private static void HeatFloor(Action<bool, string> assert)
    {
        var snapshot = Snapshot(T(100, -100, 7, TradeSide.Unknown, 1), T(200, -101, 13, TradeSide.Sell, 2));
        snapshot.SessionVolume = 20; snapshot.CumulativeDelta = -13;
        snapshot.Frames = new[] { new HeatFrame(At(0), At(1000), 0,
            new[] { new HeatCell(-103, BookSide.Bid, 10, 10), new HeatCell(-102, BookSide.Bid, 20, 20),
                new HeatCell(-101, BookSide.Bid, 60, 60), new HeatCell(-100, BookSide.Ask, 100, 100) }, -101, -100) };
        var settings = Settings(ClusterMode.Time); settings.AutomaticContrast = false; settings.ManualMaximum = 100;
        settings.Gamma = 2;
        var original = Visualization.Build(snapshot, View(), settings);
        settings.HeatFloorPercent = 20;
        var filtered = Visualization.Build(snapshot, View(), settings);
        // Duration-weighted floating arithmetic can differ by a few ULPs.
        assert(original.Heat.Select(t => Math.Round(t.Quantity, 9)).Distinct().Order().SequenceEqual(new[] { 10d, 20d, 60d, 100d }),
            "Zero heat floor must preserve the original heat quantity selection: " + string.Join(", ", original.Heat.Select(t => t.Quantity).Distinct().Order()));
        assert(filtered.Heat.Select(t => Math.Round(t.Quantity, 9)).Distinct().Order().SequenceEqual(new[] { 60d, 100d }),
            "Heat floor must hide low intensities including its exact boundary.");
        var expected = Visualization.Color(.25, settings.Palette).Hex;
        assert(filtered.Heat.Where(t => Math.Abs(t.Quantity - 60) < 1e-9).All(t => t.Color.Hex == expected),
            "Heat floor must rescale intensity before gamma: ((.60-.20)/.80)^2 = .25.");
        assert(filtered.Heat.Where(t => Math.Abs(t.Quantity - 100) < 1e-9).All(t => t.Color.Hex == Visualization.Color(1, settings.Palette).Hex),
            "The contrast ceiling must retain its saturated color after applying the heat floor.");
        assert(original.Bubbles.Select(Identity).SequenceEqual(filtered.Bubbles.Select(Identity)) &&
            original.VisibleProfile.Select(p => (p.Tick, p.Buy, p.Sell, p.Unknown)).SequenceEqual(filtered.VisibleProfile.Select(p => (p.Tick, p.Buy, p.Sell, p.Unknown))),
            "Heat intensity filtering must leave executed bubbles and profile accounting untouched.");
        assert(snapshot.SessionVolume == 20 && snapshot.CumulativeDelta == -13 && snapshot.Trades.Sum(t => t.Volume) == 20 &&
            snapshot.Frames[0].Cells.Sum(c => c.RawQuantity) == 190, "Rendering must not mutate the source ledger or resting book quantities.");
    }

    private static void ExpectInvalid(Action body, Action<bool, string> assert, string message)
    {
        try { body(); }
        catch (ArgumentOutOfRangeException) { assert(true, message); return; }
        assert(false, message);
    }
}
