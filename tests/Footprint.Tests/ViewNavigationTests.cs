using FootprintIndicator.Core;

public static class ViewNavigationTests
{
    private static readonly long Origin = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc).Ticks;
    private static long Seconds(double seconds) => (long)Math.Round(seconds * TimeSpan.TicksPerSecond);

    public static void Run(Action<bool, string> assert)
    {
        FrozenHistory(assert);
        EmptyNavigation(assert);
        TimeAnchorsAndBounds(assert);
        VisualClock(assert);
        AbsoluteGrid(assert);
        PriceScale(assert);
        InvalidInputs(assert);
    }

    private static void FrozenHistory(Action<bool, string> assert)
    {
        var navigation = new TimeNavigation(120);
        var live = navigation.Range(Origin);
        assert(navigation.IsLive && navigation.HasRange && navigation.FrozenRange == null &&
            live.SpanTicks == Seconds(120), "An observed live viewport has the configured time span.");
        navigation.Pause();
        var paused = navigation.FrozenRange;
        foreach (long observation in new[] { Origin + Seconds(1), Origin + Seconds(86400), Origin - Seconds(1) })
            assert(ReferenceEquals(paused, navigation.Range(observation, 500, true)),
                "Forward feed, retention advancement and replay rewinds cannot move absolute paused history.");
        navigation.PanSeconds(-30);
        assert(!navigation.IsLive && navigation.LastRange.FromTicks == live.FromTicks - Seconds(30) &&
            navigation.LastRange.ToTicks == live.ToTicks - Seconds(30), "Time pan translates and freezes both absolute bounds.");
        navigation.SetWindowSeconds(60);
        assert(!navigation.IsLive && navigation.LastRange.ToTicks == live.ToTicks - Seconds(30) &&
            navigation.LastRange.SpanTicks == Seconds(60), "Changing a paused window keeps its absolute right edge.");
        navigation.ResumeLive();
        var resumed = navigation.Range(Origin + Seconds(600));
        assert(navigation.IsLive && navigation.FrozenRange == null && resumed.ToTicks > live.ToTicks + Seconds(500) &&
            resumed.SpanTicks == Seconds(60), "Explicit resume follows the current observation and preserves the chosen scale.");
        navigation.SetFrozenRange(Origin + 3, Origin + Seconds(7) + 3);
        assert(!navigation.IsLive && navigation.WindowTicks == Seconds(7) && navigation.FrozenRange?.FromTicks == Origin + 3,
            "Explicit frozen intervals preserve single-tick bounds and their exact scale.");
    }

    private static void EmptyNavigation(Action<bool, string> assert)
    {
        var navigation = new TimeNavigation(2);
        navigation.PanTicks(long.MaxValue);
        navigation.Zoom(.5, .5);
        assert(!navigation.HasRange && navigation.LastRange == null && navigation.WindowTicks == Seconds(1),
            "Pan and zoom before data configure a view without inventing a market timestamp.");
        navigation.Pause();
        assert(!navigation.IsLive && !navigation.HasRange && navigation.FrozenRange == null,
            "Pause before data remembers the user's intent without creating an interval.");
        var first = navigation.Range(Origin);
        assert(ReferenceEquals(first, navigation.Range(Origin + Seconds(20))),
            "The first observation after an empty pause is captured once and remains frozen.");
        navigation.ResumeLive();
        assert(navigation.Range(Origin + Seconds(20)).ToTicks > first.ToTicks,
            "Resume after an empty pause returns to advancing time.");
    }

    private static void TimeAnchorsAndBounds(Action<bool, string> assert)
    {
        foreach (double anchor in new[] { 0d, .125, .5, .875, 1 })
        {
            var navigation = new TimeNavigation();
            navigation.SetFrozenRange(Origin + 3, Origin + 100003);
            long originalAnchor = navigation.LastRange.FromTicks + (long)Math.Round(navigation.WindowTicks * anchor);
            navigation.Zoom(.25, anchor);
            long zoomedAnchor = navigation.LastRange.FromTicks + (long)Math.Round(navigation.WindowTicks * anchor);
            assert(originalAnchor == zoomedAnchor && navigation.WindowTicks == 25000 && !navigation.IsLive,
                "Pointer time anchors retain DateTime-tick precision at close zoom.");
            navigation.Zoom(4, anchor);
            assert(navigation.LastRange.FromTicks == Origin + 3 && navigation.LastRange.ToTicks == Origin + 100003,
                "Reciprocal time zooms preserve the original absolute interval.");
        }
        foreach (double outside in new[] { -100d, 100d })
        {
            var clamped = new TimeNavigation(); var edge = new TimeNavigation();
            clamped.SetFrozenRange(Origin, Origin + Seconds(10)); edge.SetFrozenRange(Origin, Origin + Seconds(10));
            clamped.Zoom(.5, outside); edge.Zoom(.5, outside < 0 ? 0 : 1);
            assert(clamped.LastRange.FromTicks == edge.LastRange.FromTicks && clamped.LastRange.ToTicks == edge.LastRange.ToTicks,
                "Time pointer anchors outside the plot clamp to its nearest edge.");
        }
        var bounds = new TimeNavigation(1);
        var firstDate = bounds.Range(DateTime.MinValue.Ticks, double.MaxValue, true);
        assert(firstDate.FromTicks == 0 && firstDate.ToTicks == Seconds(1), "DateTime minimum retains a valid full window.");
        var lastDate = bounds.Range(DateTime.MaxValue.Ticks, double.MaxValue, true);
        assert(lastDate.ToTicks == DateTime.MaxValue.Ticks && lastDate.SpanTicks == Seconds(1),
            "Prediction and padding saturate safely at DateTime maximum.");
        bounds.PanTicks(long.MinValue);
        assert(bounds.LastRange.FromTicks == 0 && bounds.LastRange.SpanTicks == Seconds(1),
            "A minimum-Int64 pan clamps without integer overflow or changing scale.");
        bounds.Zoom(double.MaxValue, 1);
        assert(bounds.LastRange.FromTicks == 0 && bounds.WindowTicks == TimeSpan.TicksPerDay,
            "Very large zoom factors clamp to one day while retaining a valid earliest interval.");
        bounds.PanTicks(long.MaxValue);
        assert(bounds.LastRange.ToTicks == DateTime.MaxValue.Ticks && bounds.WindowTicks == TimeSpan.TicksPerDay,
            "A maximum-Int64 pan clamps at the final representable date.");
        bounds.Zoom(double.Epsilon, 1);
        assert(bounds.LastRange.ToTicks == DateTime.MaxValue.Ticks && bounds.WindowTicks == TimeSpan.TicksPerMillisecond,
            "Very small zoom factors retain the exact right endpoint at the minimum supported span.");
    }

    private static void VisualClock(Action<bool, string> assert)
    {
        var navigation = new TimeNavigation(2);
        var observed = navigation.Range(Origin);
        assert(navigation.Range(Origin, double.MaxValue).ToTicks == observed.ToTicks,
            "Optional smoothing is disabled by default even after a long quiet period.");
        var predicted = navigation.Range(Origin, 125, true, 500);
        assert(predicted.ToTicks - observed.ToTicks == TimeSpan.TicksPerMillisecond * 125,
            "The visual clock advances by elapsed time within its lead allowance.");
        var capped = navigation.Range(Origin, double.MaxValue, true, 500);
        assert(capped.ToTicks - observed.ToTicks == TimeSpan.TicksPerMillisecond * 500,
            "Quiet-market visual lead stops at its configured ceiling.");
        assert(navigation.Range(Origin + Seconds(.1), 0, true, 500).ToTicks == capped.ToTicks,
            "A new event behind the current prediction does not reverse the visual clock.");
        var reduced = navigation.Range(Origin + Seconds(.1), 0, true, 10);
        assert(reduced.ToTicks == observed.ToTicks + Seconds(.11),
            "Reducing the lead allowance caps previously retained visual progress immediately.");
        var rewound = navigation.Range(Origin - Seconds(10), 0, true, 500);
        assert(rewound.ToTicks == observed.ToTicks - Seconds(10),
            "A replay rewind starts a new visual timeline instead of retaining future predictions.");
        navigation.Pause();
        assert(ReferenceEquals(rewound, navigation.Range(Origin, 500, true)),
            "The visual clock cannot move paused history.");

        long eventEnd = Origin + Seconds(1);
        var snapshot = new EngineSnapshot
        {
            TickSize = .25,
            Trades = new[] { new TradeRecord(Origin + Seconds(.5), 400, 7, TradeSide.Buy, 1, 0, 0) },
            Frames = new[] { new HeatFrame(Origin, eventEnd, 0, new[] { new HeatCell(400, BookSide.Bid, 10, 10) }, 400, 401) }
        };
        navigation.ResumeLive();
        var range = navigation.Range(eventEnd, 500, true);
        var scene = Visualization.Build(snapshot, new Viewport(range.FromTicks, range.ToTicks, 399, 402, 1000, 300),
            new DisplaySettings { UsePersistence = false, MinimumLiquidity = 0, AutomaticContrast = false, ManualMaximum = 10 });
        double observedX = (eventEnd - range.FromTicks) * 1000d / range.SpanTicks;
        assert(scene.Heat.Length > 0 && scene.Heat.All(tile => tile.X + tile.Width <= observedX + .001),
            "A smoothly advancing viewport leaves unobserved future space empty rather than extending heat frames.");
        assert(scene.Bubbles.Sum(bubble => bubble.Total) == 7 && snapshot.Trades.Length == 1 && snapshot.Frames[0].ToTicks == eventEnd,
            "Visual clock movement neither creates executions nor changes recorded liquidity intervals.");
    }

    private static void AbsoluteGrid(Action<bool, string> assert)
    {
        var grid = NiceTimeGrid.Build(Origin + Seconds(3), Origin + Seconds(123), 600);
        var panned = NiceTimeGrid.Build(Origin + Seconds(8), Origin + Seconds(128), 600);
        assert(grid.IntervalTicks == Seconds(20) && grid.Ticks.SequenceEqual(panned.Ticks) &&
            grid.Ticks.All(tick => tick % grid.IntervalTicks == 0),
            "Time grid labels use absolute clock boundaries and stay attached while scrolling.");
        var exact = NiceTimeGrid.Build(Origin, Origin + Seconds(120), 600);
        assert(exact.Ticks[0] == Origin && exact.Ticks.Last() < Origin + Seconds(120),
            "Time grid uses a half-open interval and includes an aligned left edge once.");
        var subsecond = NiceTimeGrid.Build(Origin + 3, Origin + Seconds(1) + 3, 1000);
        assert(subsecond.IntervalTicks == TimeSpan.TicksPerMillisecond * 100 && subsecond.Ticks.All(tick => tick % subsecond.IntervalTicks == 0),
            "Close time zoom selects readable subsecond intervals without losing absolute alignment.");
        var dense = NiceTimeGrid.Build(0, TimeSpan.TicksPerDay, 1000000, 1);
        assert(dense.Ticks.Length <= NiceTimeGrid.MaximumLabels && dense.IntervalTicks == Seconds(10),
            "Wide plots select a coarser grid within the bounded label budget.");
        var maximum = NiceTimeGrid.Build(DateTime.MaxValue.Ticks - Seconds(60), DateTime.MaxValue.Ticks, 600);
        assert(maximum.Ticks.Length > 0 && maximum.Ticks.All(tick => tick < DateTime.MaxValue.Ticks && tick % maximum.IntervalTicks == 0),
            "Time grid alignment at the final date cannot overflow.");
        var tiny = NiceTimeGrid.Build(DateTime.MaxValue.Ticks - 1, DateTime.MaxValue.Ticks, 100);
        assert(tiny.Ticks.Length == 0, "A sub-grid interval at DateTime maximum can be empty without overflowing its next boundary.");
        var full = NiceTimeGrid.Build(0, DateTime.MaxValue.Ticks, 1);
        assert(full.Ticks.Length == 1 && full.Ticks[0] == 0, "An extreme full-DateTime interval still produces a bounded grid.");
    }

    private static void PriceScale(Action<bool, string> assert)
    {
        var navigation = new PriceNavigation(-100, 80);
        var range = navigation.Range;
        assert(range.MinTick == -140 && range.MaxTick == -60 && range.GetY(-100, 40, 340) == 190 &&
            range.GetY(-60, 40, 340) == 40 && range.GetY(-140, 40, 340) == 340,
            "Tick scale centers negative prices in the usable plot between the header and lower studies.");
        foreach (double tick in new[] { -200d, -140, -100.25, -60, 100 })
            assert(Math.Abs(range.GetTick(range.GetY(tick, 40, 340), 40, 340) - tick) < 1e-10,
                "Price/pixel mapping round-trips fractional ticks and extrapolates beyond the usable rectangle.");
        var fullPanel = new PriceRange(range.GetTick(400, 40, 340), range.GetTick(0, 40, 340));
        assert(Math.Abs(fullPanel.GetY(-100, 0, 400) - 190) < 1e-10,
            "Extrapolated host-panel bounds retain the market center in the usable plot.");
        navigation.PanTicks(23.5);
        assert(navigation.Range.CenterTick == -76.5 && navigation.Range.SpanTicks == 80,
            "Manual price pan preserves the tick span independently of market price.");
        navigation.CenterOn(-110);
        assert(navigation.Range.CenterTick == -110 && navigation.Range.SpanTicks == 80,
            "Explicit market centering preserves the manually selected price scale.");
        foreach (double anchor in new[] { 0d, .125, .5, .875, 1 })
        {
            var zoom = new PriceNavigation(-100, 80);
            double before = zoom.Range.GetTick(40 + anchor * 300, 40, 340);
            zoom.Zoom(.5, anchor);
            assert(zoom.Range.GetTick(40 + anchor * 300, 40, 340) == before && zoom.Range.SpanTicks == 40,
                "Price zoom keeps the pointer's tick fixed within the usable plot.");
        }
        var top = new PriceNavigation(100, 80); top.Zoom(.5, -10);
        var bottom = new PriceNavigation(100, 80); bottom.Zoom(.5, 10);
        assert(top.Range.MaxTick == 140 && bottom.Range.MinTick == 60, "Price pointer anchors outside the plot clamp to its edges.");
        navigation.Zoom(double.MaxValue, .5);
        assert(navigation.Range.SpanTicks == PriceNavigation.MaximumSpanTicks, "Price zoom saturates at its maximum supported span.");
        navigation.Zoom(double.Epsilon, .5);
        assert(navigation.Range.SpanTicks == PriceNavigation.MinimumSpanTicks, "Price zoom saturates at a usable two-tick span.");
        navigation.SetSpanTicks(101);
        assert(navigation.Range.CenterTick == -110 && navigation.Range.SpanTicks == 101,
            "Setting an odd tick span preserves the market center with fractional edge ticks.");
    }

    private static void InvalidInputs(Action<bool, string> assert)
    {
        var time = new TimeNavigation(1); time.Range(Origin);
        var price = new PriceNavigation(100, 80);
        foreach (double value in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity })
        {
            Invalid(() => time.Zoom(value, .5), assert, "Nonfinite time zoom factor");
            Invalid(() => time.Zoom(1, value), assert, "Nonfinite time anchor");
            Invalid(() => time.PanSeconds(value), assert, "Nonfinite time pan");
            Invalid(() => time.SetWindowSeconds(value), assert, "Nonfinite time window");
            Invalid(() => time.Range(Origin, value), assert, "Nonfinite elapsed clock time");
            Invalid(() => time.Range(Origin, 0, true, value), assert, "Nonfinite clock lead limit");
            Invalid(() => price.PanTicks(value), assert, "Nonfinite price pan");
            Invalid(() => price.CenterOn(value), assert, "Nonfinite market center");
            Invalid(() => price.Zoom(1, value), assert, "Nonfinite price anchor");
            Invalid(() => price.Range.GetY(100, value, 300), assert, "Nonfinite usable plot edge");
        }
        Invalid(() => new TimeRange(0, 0), assert, "Empty time range");
        Invalid(() => time.Range(-1), assert, "Negative DateTime ticks");
        Invalid(() => time.Range(DateTime.MaxValue.Ticks + 1), assert, "DateTime ticks above maximum");
        Invalid(() => time.Range(Origin, -1), assert, "Negative elapsed time");
        Invalid(() => time.Range(Origin, 0, true, 60001), assert, "Unbounded visual lead");
        Invalid(() => time.SetFrozenRange(Origin, Origin + 1), assert, "Frozen span below one millisecond");
        Invalid(() => time.SetFrozenRange(Origin, Origin + TimeSpan.TicksPerDay + 1), assert, "Frozen span above one day");
        Invalid(() => time.SetWindowSeconds(0), assert, "Zero time window");
        Invalid(() => time.Zoom(0, .5), assert, "Zero time zoom factor");
        Invalid(() => NiceTimeGrid.Build(0, Seconds(1), 0), assert, "Zero-width grid");
        Invalid(() => NiceTimeGrid.Build(0, Seconds(1), 100, .5), assert, "Subpixel label separation");
        Invalid(() => new PriceRange(1, 1), assert, "Empty price range");
        Invalid(() => new PriceRange(-double.MaxValue, double.MaxValue), assert, "Overflowing price range");
        Invalid(() => price.SetSpanTicks(1), assert, "Price span below minimum");
        Invalid(() => price.Zoom(-1, .5), assert, "Negative price zoom factor");
        Invalid(() => price.Range.GetTick(0, 100, 100), assert, "Empty usable plot");
        assert(time.IsLive && time.WindowTicks == Seconds(1) && price.Range.CenterTick == 100 && price.Range.SpanTicks == 80,
            "Rejected inputs leave navigation state unchanged.");
    }

    private static void Invalid(Action action, Action<bool, string> assert, string description)
    {
        try { action(); }
        catch (ArgumentOutOfRangeException) { assert(true, description + " was rejected."); return; }
        assert(false, description + " must be rejected.");
    }
}
