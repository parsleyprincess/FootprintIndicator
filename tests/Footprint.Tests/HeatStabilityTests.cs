using FootprintIndicator.Core;

public static class HeatStabilityTests
{
    // Align the fixture to a large power of two, while retaining a realistic
    // DateTime magnitude that would expose conversions through double timestamps.
    private static readonly long Start = new DateTime(2026, 11, 4).Ticks / (1L << 20) * (1L << 20);

    public static void Run(Action<bool, string> assert)
    {
        ContrastModes(assert);
        ContrastLifetime(assert);
        HeldContrastAcrossViews(assert);
        PanStability(assert);
        PriceBinsAcrossColumnBoundary(assert);
        ZoomAndGaps(assert);
        UnequalDurationsAndMissingFrames(assert);
        IndependentExecutionLedger(assert);
        DominantBubbleColors(assert);
        DateTimeBounds(assert);
    }

    private static DisplaySettings Settings() => new()
    {
        UsePersistence = false, MinimumLiquidity = 0, HeatColumnPixels = 2,
        AutomaticContrastMinimum = 200
    };

    private static EngineSnapshot Snapshot(params HeatFrame[] frames) => new() { Frames = frames };
    private static HeatCell Cell(long tick, double quantity) => new(tick, BookSide.Bid, quantity, quantity);
    private static HeatFrame Frame(long from, long to, int epoch, params HeatCell[] cells)
        => new(Start + from, Start + to, epoch, cells, null, null);
    private static Viewport View(long from, long to, int width = 100, double min = -105.5, double max = -94.5, int height = 600)
        => new(Start + from, Start + to, min, max, width, height);
    private static double Exposure(RenderScene scene, Viewport view)
        => scene.Heat.Sum(t => t.Quantity * t.Width / view.Width * (view.ToTicks - view.FromTicks));
    private static double ExpectedExposure(EngineSnapshot snapshot, Viewport view)
        => snapshot.Frames.Sum(f => Math.Max(0, Math.Min(f.ToTicks, view.ToTicks) - Math.Max(f.FromTicks, view.FromTicks)) *
            f.Cells.Where(c => c.Tick + .5 > view.MinPriceTick && c.Tick - .5 < view.MaxPriceTick).Sum(c => c.RawQuantity));
    private static void Near(double expected, double actual, Action<bool, string> assert, string message)
        => assert(double.IsFinite(actual) && Math.Abs(expected - actual) <= 2e-6 * Math.Max(1, Math.Abs(expected)),
            $"{message}: expected {expected:R}, got {actual:R}");

    private static (long From, long To, double Low, double High, double Quantity, string Color) Identity(HeatTile tile, Viewport view)
    {
        long duration = view.ToTicks - view.FromTicks;
        long from = view.FromTicks - Start + (long)Math.Round((double)tile.X / view.Width * duration);
        long to = view.FromTicks - Start + (long)Math.Round(((double)tile.X + tile.Width) / view.Width * duration);
        double range = view.MaxPriceTick - view.MinPriceTick;
        double low = view.MaxPriceTick - ((double)tile.Y + tile.Height) / view.Height * range;
        double high = view.MaxPriceTick - (double)tile.Y / view.Height * range;
        return (from, to, Math.Round(low, 3), Math.Round(high, 3), Math.Round(tile.Quantity, 9), tile.Color.Hex);
    }

    private static object[] Interior(RenderScene scene, Viewport view, long from, long to)
        => scene.Heat.Select(t => Identity(t, view)).Where(t => t.From >= from && t.To <= to)
            .OrderBy(t => t.From).ThenBy(t => t.To).ThenBy(t => t.Low).Select(t => (object)t).ToArray();

    private static void ContrastModes(Action<bool, string> assert)
    {
        // A long low-liquidity tail makes a percentile ceiling saturate all three
        // meaningful levels. The default maximum must retain their separation.
        var cells = Enumerable.Range(0, 100).Select(i => Cell(i - 100,
            i == 97 ? 47 : i == 98 ? 72 : i == 99 ? 127 : 10)).ToArray();
        var snapshot = Snapshot(Frame(0, 65536, 0, cells));
        var view = View(0, 65536, 32, -100.5, -.5);
        var settings = new DisplaySettings { UsePersistence = false, MinimumLiquidity = 0 };
        var maximum = Visualization.Build(snapshot, view, settings);
        assert(settings.ContrastPercentile == 100 && maximum.HeatMaximum == 127,
            "Default automatic contrast uses the maximum rather than saturating levels above a low percentile.");
        var colors = new[] { 47d, 72, 127 }.Select(q => maximum.Heat.First(t => t.Quantity == q).Color.Hex).ToArray();
        assert(colors.Distinct().Count() == 3 && colors[2] == Visualization.Color(1, settings.Palette).Hex &&
            colors[0] == Visualization.Color(47d / 127, settings.Palette).Hex &&
            colors[1] == Visualization.Color(72d / 127, settings.Palette).Hex,
            "47, 72, and 127 resting contracts receive three distinct proportional shades; only the maximum saturates.");

        settings.ContrastPercentile = 97;
        var percentile = Visualization.Build(snapshot, view, settings);
        assert(percentile.HeatMaximum < 47 && percentile.Heat.Where(t => t.Quantity >= 47)
            .All(t => t.Color.Hex == Visualization.Color(1, settings.Palette).Hex),
            "An explicitly selected percentile can still clip the upper tail, independently of maximum mode.");
        settings.AutomaticContrast = false;
        settings.ManualMaximum = 200;
        settings.AutomaticContrastMinimum = 500;
        var manual = Visualization.Build(snapshot, view, settings);
        assert(manual.HeatMaximum == 200 && manual.Heat.All(t => t.Color.Hex == Visualization.Color(t.Quantity / 200, settings.Palette).Hex),
            "Manual contrast uses its absolute ceiling and ignores both percentile and held automatic contrast.");
        settings.AutomaticContrast = true;
        settings.ContrastPercentile = 100;
        var held = Visualization.Build(snapshot, view, settings);
        assert(held.HeatMaximum == 500 && held.Heat.All(t => t.Color.Hex == Visualization.Color(t.Quantity / 500, settings.Palette).Hex),
            "A held automatic ceiling keeps the same quantity mapped to the same shade after the prior maximum scrolls away.");
        assert(snapshot.Frames[0].Cells.Sum(c => c.RawQuantity) == 97 * 10 + 47 + 72 + 127,
            "Contrast controls never alter captured resting quantities.");
    }

    private static void ContrastLifetime(Action<bool, string> assert)
    {
        var scale = new HeatContrastScale();
        assert(scale.Update(10, 0) == 0 && scale.Update(10, 127) == 127 && scale.Update(10, 47) == 127 &&
            scale.Update(10, 0) == 127 && scale.Update(10, 200) == 200,
            "Automatic contrast expands for larger observations and never shrinks when maxima leave the viewport.");
        assert(scale.Update(11, 0) == 0 && scale.Update(11, 47) == 47,
            "A new session resets automatic contrast before collecting new observations.");
        scale.Reset();
        assert(scale.Update(11, 0) == 0 && scale.Update(11, 72) == 72,
            "An explicit settings/filter reset recalibrates contrast even within the same session.");
        foreach (double invalid in new[] { -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Invalid(() => scale.Update(12, invalid), assert, "Invalid contrast observations must be rejected.");
            assert(scale.Update(11, 0) == 72, "Rejected observations cannot reset valid held contrast state.");
            var settings = Settings(); settings.AutomaticContrastMinimum = invalid;
            Invalid(() => Visualization.Build(Snapshot(), View(0, 1), settings), assert,
                "Invalid held contrast ceilings must be rejected before rendering.");
        }
        var empty = Visualization.Build(Snapshot(), View(0, 1), Settings());
        assert(empty.Heat.Length == 0 && empty.HeatMaximum == 200,
            "An empty viewport retains the host's held ceiling without fabricating heat.");
    }

    private static void PanStability(Action<bool, string> assert)
    {
        var history = Enumerable.Range(0, 200).Select(i => Frame(i * 7000L, (i + 1) * 7000L, 0,
            Cell(-101, 20 + i % 13 * 4), Cell(-100, 10 + i % 7 * 3), Cell(-99, 47))).ToArray();
        var snapshot = Snapshot(history);
        var originalView = View(0, 1000000);
        var pannedView = View(1370, 1001370);
        var pricePannedView = View(1370, 1001370, min: -105.375, max: -94.375);
        var settings = Settings();
        var original = Visualization.Build(snapshot, originalView, settings);
        var panned = Visualization.Build(snapshot, pannedView, settings);
        var pricePanned = Visualization.Build(snapshot, pricePannedView, settings);
        var extended = Snapshot(history.Concat(new[] { Frame(1400000, 1500000, 0, Cell(-100, 1000)) }).ToArray());
        var appended = Visualization.Build(extended, originalView, settings);
        var expected = Interior(original, originalView, 100000, 800000);
        assert(expected.Length > 10 && expected.SequenceEqual(Interior(panned, pannedView, 100000, 800000)),
            "Subpixel time panning changes heat coordinates without recomputing closed historical quantities or colors.");
        assert(expected.SequenceEqual(Interior(pricePanned, pricePannedView, 100000, 800000)),
            "Fractional price panning preserves the absolute historical price bins.");
        assert(expected.SequenceEqual(Interior(appended, originalView, 100000, 800000)),
            "Newer offscreen depth frames cannot rewrite old heat bins.");
        Near(ExpectedExposure(snapshot, pannedView), Exposure(panned, pannedView), assert,
            "Clipped panning preserves quantity multiplied by observed duration");
    }

    private static void HeldContrastAcrossViews(Action<bool, string> assert)
    {
        var snapshot = Snapshot(Frame(0, 65536, 0, Cell(-100, 47), Cell(-99, 127)),
            Frame(65536, 131072, 0, Cell(-100, 47)));
        var settings = new DisplaySettings { UsePersistence = false, MinimumLiquidity = 0 };
        var scale = new HeatContrastScale();
        settings.AutomaticContrastMinimum = scale.Update(1, 0);
        var first = Visualization.Build(snapshot, View(0, 65536), settings);
        settings.AutomaticContrastMinimum = scale.Update(1, first.HeatMaximum);
        var panned = Visualization.Build(snapshot, View(65536, 131072), settings);
        var originalColor = first.Heat.First(t => t.Quantity == 47).Color.Hex;
        assert(panned.Heat.Length > 0 && panned.HeatMaximum == 127 &&
            panned.Heat.All(t => t.Quantity == 47 && t.Color.Hex == originalColor) &&
            originalColor != Visualization.Color(1, settings.Palette).Hex,
            "After 127 scrolls offscreen, the host's held scale keeps 47 at its original shade rather than turning it hot red.");
        settings.Gamma = 2;
        settings.HeatFloorPercent = 10;
        settings.Palette = HeatPalette.Ice;
        var recolored = Visualization.Build(snapshot, View(65536, 131072), settings);
        assert(recolored.HeatMaximum == 127 && recolored.Heat.All(t => t.Quantity == 47),
            "Changing the transfer curve or palette preserves the held ceiling and recorded quantity.");
        settings.AutomaticContrastMinimum = scale.Update(2, 0);
        var newSession = Visualization.Build(snapshot, View(65536, 131072), settings);
        assert(newSession.HeatMaximum == 47,
            "The first view of a new session recalibrates from its own observed heat after the host resets its scale.");
    }

    private static void PriceBinsAcrossColumnBoundary(Action<bool, string> assert)
    {
        const long duration = 512 * 1024;
        var snapshot = Snapshot(Frame(0, duration + 100, 0,
            Cell(-103, 10), Cell(-102, 20), Cell(-101, 30), Cell(-100, 40)));
        var alignedView = View(0, duration, 1024, -600.5, 421.5, 1022);
        var pannedView = View(1, duration + 1, 1024, -600.5, 421.5, 1022);
        var settings = Settings();
        var aligned = Visualization.Build(snapshot, alignedView, settings);
        var panned = Visualization.Build(snapshot, pannedView, settings);
        assert(Interior(aligned, alignedView, 1024, duration - 1024)
            .SequenceEqual(Interior(panned, pannedView, 1024, duration - 1024)),
            "An extra partially visible time column must not change price-bin width at the geometry budget threshold.");
        assert(aligned.Heat.Select(t => t.Quantity).Distinct().Order().SequenceEqual(new[] { 40d, 60 }),
            "Coarse negative-price bins use floor alignment and sum neighboring levels without crossing zero incorrectly.");
        Near(ExpectedExposure(snapshot, alignedView), Exposure(aligned, alignedView), assert,
            "Coarse price bins conserve summed resting quantity exposure");
    }

    private static void ZoomAndGaps(Action<bool, string> assert)
    {
        var frames = Enumerable.Range(0, 64).Select(i => Frame(i * 1024L, (i + 1) * 1024L, 0,
            Cell(-100, i % 2 == 0 ? 10 : 50))).ToArray();
        var snapshot = Snapshot(frames);
        var coarseView = View(0, 65536, 1);
        var fineView = View(0, 65536, 128);
        var settings = Settings();
        var coarse = Visualization.Build(snapshot, coarseView, settings);
        var fine = Visualization.Build(snapshot, fineView, settings);
        assert(coarse.Heat.Length == 1 && coarse.Heat[0].Quantity == 30 && fine.Heat.Length == 64 &&
            fine.Heat.Select(t => t.Quantity).Distinct().Order().SequenceEqual(new[] { 10d, 50 }),
            "Intentional time zoom changes aggregation resolution and reveals underlying quantities.");
        Near(Exposure(coarse, coarseView), Exposure(fine, fineView), assert,
            "Time zoom preserves quantity multiplied by duration");
        var gaps = Snapshot(Frame(0, 16384, 0, Cell(-100, 20)),
            Frame(16384, 32768, 0, Cell(-100, 0)),
            Frame(32768, 49152, 1, Cell(-100, 40)), Frame(49152, 65536, 2, Cell(-100, 60)));
        var gapped = Visualization.Build(gaps, coarseView, settings);
        assert(gapped.Heat.Length == 3 && gapped.Heat.All(t => !(t.X < .375 && t.X + t.Width > .375)),
            "An absent/zero-liquidity interval remains a real gap even inside one display column.");
        assert(gapped.Heat.Select(t => t.Quantity).SequenceEqual(new[] { 20d, 40, 60 }),
            "Adjacent reset epochs must remain distinct rather than averaging across a feed reset.");
        Near(16384 * 120d, Exposure(gapped, coarseView), assert,
            "Gaps and reset boundaries preserve only actually observed liquidity exposure");
    }

    private static void DateTimeBounds(Action<bool, string> assert)
    {
        foreach (long from in new[] { 0L, DateTime.MaxValue.Ticks - 31 })
        {
            var view = new Viewport(from, from + 31, -1.5, .5, 31, 20);
            var snapshot = Snapshot(new HeatFrame(from, from + 31, 0,
                new[] { Cell(-1, 47), Cell(0, 72) }, null, null));
            var settings = Settings(); settings.HeatColumnPixels = 1;
            var scene = Visualization.Build(snapshot, view, settings);
            assert(scene.Heat.Length == 62 && scene.Heat.All(t => t.Width == 1 && t.X >= 0 && t.X + t.Width <= 31),
                "Single DateTime ticks at both date limits remain distinct, finite, and clipped to the viewport.");
            Near(31 * 119d, Exposure(scene, view), assert, "DateTime-limit heat exposure is exact");
        }
        var full = new Viewport(0, DateTime.MaxValue.Ticks, -1.5, .5, 1, 20);
        var fullSnapshot = Snapshot(new HeatFrame(0, DateTime.MaxValue.Ticks, 0,
            new[] { Cell(-1, 47) }, null, null));
        var entire = Visualization.Build(fullSnapshot, full, Settings());
        assert(entire.Heat.Length == 1 && entire.Heat[0].Width == 1,
            "A full DateTime-range bin must not overflow its end boundary.");
        Near(47, entire.Heat[0].Quantity, assert,
            "Extreme-duration weighting retains quantity within floating-point precision");
        Invalid(() => Visualization.Build(Snapshot(), new Viewport(-1, 1, -1, 1, 1, 1), Settings()), assert,
            "Negative DateTime endpoints are invalid.");
        Invalid(() => Visualization.Build(Snapshot(), new Viewport(0, DateTime.MaxValue.Ticks + 1, -1, 1, 1, 1), Settings()), assert,
            "Times beyond DateTime.MaxValue are invalid.");
    }

    private static void UnequalDurationsAndMissingFrames(Action<bool, string> assert)
    {
        var snapshot = Snapshot(Frame(0, 4096, 0, Cell(-100, 10)),
            Frame(4096, 16384, 0, Cell(-100, 50)),
            Frame(32768, 65536, 0, Cell(-100, 80)));
        var view = View(0, 65536, 1);
        var scene = Visualization.Build(snapshot, view, Settings());
        assert(scene.Heat.Length == 2 && scene.Heat[0].Quantity == 40 && scene.Heat[1].Quantity == 80 &&
            scene.Heat[0].Width == .25 && scene.Heat[1].X == .5 && scene.Heat[1].Width == .5,
            "Time aggregation weights unequal frame durations and preserves a missing-frame gap within the same epoch.");
        Near(ExpectedExposure(snapshot, view), Exposure(scene, view), assert,
            "Duration-weighted aggregation conserves exposure without filling uncaptured time");
        var clippedView = View(2048, 65536, 1);
        var clipped = Visualization.Build(snapshot, clippedView, Settings());
        Near((10 * 2048d + 50 * 12288d) / 14336, clipped.Heat[0].Quantity, assert,
            "A partially visible first frame contributes only its clipped duration");
        Near(ExpectedExposure(snapshot, clippedView), Exposure(clipped, clippedView), assert,
            "Clipping and missing history conserve recorded quantity-duration exposure");
    }

    private static void IndependentExecutionLedger(Action<bool, string> assert)
    {
        var snapshot = Snapshot(Frame(0, 65536, 0, Cell(-101, 47), Cell(-100, 72), Cell(-99, 127)));
        snapshot.Trades = new[] {
            new TradeRecord(Start + 1024, -101, 47, TradeSide.Buy, 1, 0, 0),
            new TradeRecord(Start + 32768, -100, 72, TradeSide.Sell, 2, 0, 0),
            new TradeRecord(Start + 65535, -99, 127, TradeSide.Unknown, 3, 0, 0)
        };
        snapshot.SessionVolume = 246;
        snapshot.CumulativeDelta = -25;
        foreach (var mode in Enum.GetValues<ClusterMode>())
        {
            var settings = Settings(); settings.Clustering = mode;
            foreach (bool automatic in new[] { true, false })
            {
                settings.AutomaticContrast = automatic;
                settings.HeatFloorPercent = automatic ? 0 : 50;
                var scene = Visualization.Build(snapshot, View(0, 65536, 1), settings);
                assert(scene.Bubbles.Sum(b => b.Buy) == 47 && scene.Bubbles.Sum(b => b.Sell) == 72 &&
                    scene.Bubbles.Sum(b => b.Unknown) == 127 && scene.VisibleProfile.Sum(p => p.Total) == 246,
                    $"{mode}: automatic/manual heat contrast and hidden heat levels leave every executed side and profile quantity intact.");
            }
        }
        assert(snapshot.SessionVolume == 246 && snapshot.CumulativeDelta == -25 &&
            snapshot.Trades.Sum(t => t.Volume) == 246 && snapshot.Frames[0].Cells.Sum(c => c.RawQuantity) == 246,
            "Heat rendering and contrast never mutate the source session ledger or resting quantities.");
    }

    private static void DominantBubbleColors(Action<bool, string> assert)
    {
        var buy = new Rgb(20, 200, 40);
        var sell = new Rgb(220, 30, 60);
        var unknown = new Rgb(120, 130, 140);
        foreach (var item in new[] {
            (Buy: 6L, Sell: 4L, Unknown: 3L, Expected: buy),
            (Buy: 4L, Sell: 6L, Unknown: 3L, Expected: sell),
            (Buy: 6L, Sell: 6L, Unknown: 3L, Expected: unknown),
            (Buy: 6L, Sell: 4L, Unknown: 6L, Expected: unknown),
            (Buy: 4L, Sell: 6L, Unknown: 6L, Expected: unknown),
            (Buy: 4L, Sell: 3L, Unknown: 10L, Expected: unknown),
            (Buy: 0L, Sell: 0L, Unknown: 0L, Expected: unknown),
            (Buy: long.MaxValue, Sell: 0L, Unknown: 0L, Expected: buy),
            (Buy: 0L, Sell: long.MaxValue, Unknown: 0L, Expected: sell)
        })
        {
            var bubble = new Bubble { Buy = item.Buy, Sell = item.Sell, Unknown = item.Unknown };
            assert(Visualization.DominantBubbleColor(bubble, buy, sell, unknown).Hex == item.Expected.Hex &&
                bubble.Buy == item.Buy && bubble.Sell == item.Sell && bubble.Unknown == item.Unknown,
                $"Dominant execution color respects strict category dominance without changing {item.Buy}/{item.Sell}/{item.Unknown} quantities.");
        }
        foreach (var bubble in new[] { new Bubble { Buy = -1 }, new Bubble { Sell = -1 }, new Bubble { Unknown = -1 } })
            Invalid(() => Visualization.DominantBubbleColor(bubble, buy, sell, unknown), assert,
                "Dominant execution color rejects negative category quantities.");
        bool rejectedNull = false;
        try { Visualization.DominantBubbleColor(null!, buy, sell, unknown); }
        catch (ArgumentNullException) { rejectedNull = true; }
        assert(rejectedNull, "Dominant execution color rejects a missing bubble explicitly.");
    }

    private static void Invalid(Action action, Action<bool, string> assert, string message)
    {
        try { action(); }
        catch (ArgumentOutOfRangeException) { assert(true, message); return; }
        assert(false, message);
    }
}
