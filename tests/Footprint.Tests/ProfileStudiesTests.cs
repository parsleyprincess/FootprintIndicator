using FootprintIndicator.Core;

public static class ProfileStudiesTests
{
    private static readonly long Origin = new DateTime(2026, 10, 26, 12, 0, 0).Ticks;

    public static void Run(Action<bool, string> assert)
    {
        void Near(double actual, double expected, string message, double tolerance = 1e-9)
            => assert(Math.Abs(actual - expected) <= tolerance, message + $" ({actual} vs {expected})");
        void Fails(Action action, string message)
        {
            bool rejected = false;
            try { action(); } catch (ArgumentException) { rejected = true; } catch (InvalidOperationException) { rejected = true; } catch (OverflowException) { rejected = true; }
            assert(rejected, message);
        }

        var empty = Build(Snapshot(), new ProfileSettings());
        assert(empty.TotalVolume == 0 && !empty.PocTick.HasValue, "Empty profile has no fabricated POC.");
        assert(double.IsNaN(empty.Vwap) && double.IsNaN(empty.LowerBand), "Empty price statistics remain unavailable.");
        Near(empty.VolumeImbalance, 0, "Empty classified-volume imbalance is neutral.");
        Near(empty.BookImbalance, 0, "Empty book imbalance is neutral.");

        var snapshot = Snapshot(new[] { Trade(0, 100, 1, TradeSide.Buy), Trade(100, 101, 2, TradeSide.Sell), Trade(200, 102, 1, TradeSide.Unknown) },
            new[] { new ProfileLevel(102, 0, 0, 1), new ProfileLevel(100, 1, 0, 0), new ProfileLevel(101, 0, 2, 0) });
        var settings = new ProfileSettings { ValueAreaPercent = 70, BandStandardDeviations = 2 };
        var profile = Build(snapshot, settings);
        assert(profile.TotalVolume == 4 && profile.Buy == 1 && profile.Sell == 2 && profile.Unknown == 1,
            "Profile conserves buy, sell and unknown volume.");
        assert(profile.PocTick == 101 && profile.ValueAreaLowTick == 100 && profile.ValueAreaHighTick == 101,
            "Known value area chooses lower adjacent side on equal volume.");
        Near(profile.Vwap, 25.25, "VWAP includes unknown trades and returns price units.");
        Near(profile.StandardDeviation, Math.Sqrt(.5) * .25, "Volume-weighted population deviation uses the full profile.");
        Near(profile.UpperBand, 25.25 + 2 * Math.Sqrt(.5) * .25, "Upper band uses configured multiplier.");
        Near(profile.LowerBand, 25.25 - 2 * Math.Sqrt(.5) * .25, "Lower band uses configured multiplier.");
        Near(profile.VolumeImbalance, -1.0 / 3, "Volume imbalance excludes unknown from its denominator.");
        assert(profile.ExecutionCount == 3, "Retained execution count is separate from traded contracts.");
        assert(snapshot.Profile[0].Tick == 102, "Profile calculation must not reorder snapshot arrays.");

        settings.ValueAreaPercent = 100;
        var all = Build(snapshot, settings);
        assert(all.ValueAreaLowTick == 100 && all.ValueAreaHighTick == 102, "100 percent value area includes all volume.");
        settings.ValueAreaPercent = 1;
        var small = Build(snapshot, settings);
        assert(small.ValueAreaLowTick == 101 && small.ValueAreaHighTick == 101, "A tiny target fits entirely within the POC.");
        settings.BandStandardDeviations = 0;
        var noBands = Build(snapshot, settings);
        Near(noBands.UpperBand, noBands.Vwap, "Zero band multiplier collapses onto VWAP.");

        var tied = Build(Snapshot(profile: new[] { new ProfileLevel(12, 5, 0, 0), new ProfileLevel(-8, 0, 5, 0) }), new ProfileSettings());
        assert(tied.PocTick == -8, "POC ties deterministically choose the lower price even for sparse negative ticks.");
        assert(tied.ValueAreaLowTick == -8 && tied.ValueAreaHighTick == 12, "Sparse price gaps remain inside the value-area interval without invented volume.");

        var visibleSettings = new ProfileSettings { Scope = ProfileScope.Visible, BuildFootprint = true, BuildTpo = true };
        var visible = ProfileStudies.Build(snapshot, visibleSettings, Origin + 100 * TimeSpan.TicksPerMillisecond, Origin + 200 * TimeSpan.TicksPerMillisecond);
        assert(visible.TotalVolume == 2 && visible.Sell == 2 && visible.ExecutionCount == 1,
            "Visible profile uses the half-open viewport and includes its left boundary only.");
        assert(visible.Levels.Length == 1 && visible.PocTick == 101, "Visible profile is independent of the full session profile.");
        assert(visible.Footprints.Single().TotalVolume == 2 && visible.TpoLevels.Single().Tick == 101,
            "Footprint and TPO use the same selected executions.");
        var sessionAtNarrowView = ProfileStudies.Build(snapshot, new ProfileSettings { BuildFootprint = true },
            Origin + 100 * TimeSpan.TicksPerMillisecond, Origin + 200 * TimeSpan.TicksPerMillisecond);
        assert(sessionAtNarrowView.TotalVolume == 4 && sessionAtNarrowView.Footprints.Single().TotalVolume == 2,
            "Session profile retains full captured volume while footprints obey the viewport.");

        snapshot.DroppedTrades = 10;
        assert(Build(snapshot, new ProfileSettings()).PartialTradeHistory == false,
            "Dropped earlier history does not mark a complete current session as partial.");
        snapshot.SessionStartTicks = Origin - 1;
        assert(Build(snapshot, new ProfileSettings()).PartialTradeHistory,
            "Session retained-execution coverage is marked partial when its start precedes the retained tape.");
        assert(!ProfileStudies.Build(snapshot, visibleSettings, Origin, Origin + TimeSpan.TicksPerMinute).PartialTradeHistory,
            "A visible range wholly inside retained coverage is not marked partial.");
        assert(Build(snapshot, new ProfileSettings()).TotalVolume == 4,
            "Trade retention does not discard independently maintained session-profile totals.");

        var bookSnapshot = Snapshot();
        bookSnapshot.Bids = new[] { new DepthLevel(98, 80), new DepthLevel(100, 100), new DepthLevel(99, 40) };
        bookSnapshot.Asks = new[] { new DepthLevel(103, 200), new DepthLevel(101, 100) };
        var bookSettings = new ProfileSettings { BookLevels = 2, BookDistanceDecay = 1, LargeLiquidityMinimum = 80 };
        var book = Build(bookSnapshot, bookSettings);
        Near(book.WeightedBidDepth, 120, "Book weighting uses tick distance from the actual best bid.");
        Near(book.WeightedAskDepth, 100 + 200.0 / 3, "Ask weighting uses positive price distance.");
        Near(book.BookImbalance, (120 - (100 + 200.0 / 3)) / (120 + 100 + 200.0 / 3), "Book imbalance uses the selected weighted depths.");
        assert(book.LargeBids.Select(b => b.Tick).SequenceEqual(new long[] { 100, 98 }),
            "Large current bid walls include qualifying observed levels beyond the imbalance depth window.");
        assert(book.LargeAsks.Length == 2 && bookSnapshot.Bids[0].Tick == 98, "Large walls and book sorting do not mutate input.");
        bookSettings.BookDistanceDecay = 0;
        Near(Build(bookSnapshot, bookSettings).WeightedBidDepth, 140, "Zero decay uses ordinary top-N depth sums.");

        var tape = new[] {
            Trade(0, 100, 5, TradeSide.Sell), Trade(10, 101, 20, TradeSide.Buy),
            Trade(20, 100, 2, TradeSide.Buy), Trade(30, 101, 9, TradeSide.Sell), Trade(40, 100, 7, TradeSide.Unknown),
            Trade(60000, 100, 3, TradeSide.Buy), Trade(60010, 100, 11, TradeSide.Sell)
        };
        var footprintSettings = new ProfileSettings { Scope = ProfileScope.Visible, BuildFootprint = true,
            DiagonalImbalanceRatio = 3, DiagonalMinimumVolume = 9 };
        var footprints = Build(Snapshot(tape), footprintSettings);
        assert(footprints.Footprints.Length == 2, "Footprint splits at its fixed time boundary.");
        assert(footprints.Footprints[0].FromTicks == Origin && footprints.Footprints[0].ToTicks == Origin + TimeSpan.TicksPerMinute,
            "Fixed footprint boundaries do not depend on chart pan.");
        assert(footprints.Footprints.Sum(b => b.TotalVolume) == tape.Sum(t => t.Volume), "Footprint bars conserve every selected contract.");
        assert(footprints.Footprints.Sum(b => b.Unknown) == 7, "Unknown aggressor has its own footprint bucket.");
        var level100 = footprints.Footprints[0].Levels.Single(c => c.Tick == 100);
        var level101 = footprints.Footprints[0].Levels.Single(c => c.Tick == 101);
        assert(level101.BuyImbalance, "Buy imbalance compares buying to sells one price group lower.");
        assert(!level100.SellImbalance, "Five sells versus twenty buys above is not a sell imbalance.");
        assert(!level101.SellImbalance && !level100.BuyImbalance, "Missing diagonal prices do not create division-by-zero imbalance flags.");
        footprintSettings.DiagonalMinimumVolume = 21;
        assert(!Build(Snapshot(tape), footprintSettings).Footprints[0].Levels.Single(c => c.Tick == 101).BuyImbalance,
            "The diagonal minimum-volume setting is enforced independently of the ratio.");
        var sellDiagonal = Build(Snapshot(new[] { Trade(0, 100, 30, TradeSide.Sell), Trade(1, 101, 10, TradeSide.Buy) }),
            new ProfileSettings { Scope = ProfileScope.Visible, BuildFootprint = true });
        assert(sellDiagonal.Footprints[0].Levels[0].SellImbalance, "Sell diagonal ratio qualifies at exact equality.");

        var negative = Build(Snapshot(new[] { Trade(0, -1, 2, TradeSide.Buy), Trade(1, -2, 3, TradeSide.Unknown), Trade(2, 0, 4, TradeSide.Sell) }),
            new ProfileSettings { Scope = ProfileScope.Visible, BuildFootprint = true, BuildTpo = true, FootprintPriceGroupTicks = 2, TpoPriceGroupTicks = 2 });
        assert(negative.Footprints.Single().Levels.Select(c => c.Tick).SequenceEqual(new long[] { -2, 0 }),
            "Negative futures prices use mathematical-floor grouping, not truncation toward zero.");
        assert(negative.Footprints.Single().Levels[0].Total == 5 && negative.TpoLevels[0].Count == 1,
            "Negative grouped price retains side volume and counts each TPO bracket only once.");

        var tpoTape = new[] { Trade(0, 100, 1, TradeSide.Buy), Trade(100, 100, 1000, TradeSide.Sell),
            Trade(1000, 101, 1, TradeSide.Unknown), Trade(1800000, 100, 1, TradeSide.Buy), Trade(1800100, 102, 1, TradeSide.Sell) };
        var tpo = Build(Snapshot(tpoTape), new ProfileSettings { Scope = ProfileScope.Visible, BuildTpo = true, ValueAreaPercent = 70 });
        assert(tpo.TpoLevels.Select(l => l.Count).SequenceEqual(new[] { 2, 1, 1 }),
            "TPO counts distinct brackets with actual prints, independent of traded volume and repeated executions.");
        assert(tpo.TpoPocTick == 100 && tpo.TpoValueAreaLowTick == 100 && tpo.TpoValueAreaHighTick == 101,
            "TPO value area uses time-price opportunity counts rather than execution volume.");
        assert(tpo.Footprints.Length == 0, "TPO can be enabled without footprint bars.");

        foreach (double tickSize in new[] { .25, .1, 1.0, .00005, .0001, .0000005, .5, .01 })
        {
            var generic = Snapshot(profile: new[] { new ProfileLevel(10000, 1, 0, 0), new ProfileLevel(10002, 0, 1, 0) });
            generic.TickSize = tickSize;
            var value = Build(generic, new ProfileSettings());
            Near(value.Vwap, 10001 * tickSize, "Instrument tick size, not a symbol whitelist, determines price units.", 1e-8);
            Near(value.StandardDeviation, tickSize, "Profile dispersion works for fine currency, index and metal ticks.", 1e-8);
        }

        long half = long.MaxValue / 2;
        var huge = Build(Snapshot(profile: new[] { new ProfileLevel(100, half + 1, half, 0) }), new ProfileSettings());
        assert(huge.TotalVolume == long.MaxValue && huge.VolumeImbalance > 0, "One-contract delta survives huge balanced side totals.");
        Near(huge.VolumeImbalance, 1.0 / long.MaxValue, "Huge-volume imbalance retains the signed exact numerator.", 1e-30);
        Fails(() => Build(Snapshot(profile: new[] { new ProfileLevel(1, long.MaxValue, 1, 0) }), new ProfileSettings()),
            "Overflow inside a profile row must fail explicitly.");
        Fails(() => Build(Snapshot(profile: new[] { new ProfileLevel(1, long.MaxValue, 0, 0), new ProfileLevel(2, 1, 0, 0) }), new ProfileSettings()),
            "Overflow across profile prices must fail explicitly.");

        Fails(() => Build(Snapshot(), new ProfileSettings { ValueAreaPercent = 0 }), "Reject zero value-area percentage.");
        Fails(() => Build(Snapshot(), new ProfileSettings { BookDistanceDecay = double.NaN }), "Reject nonfinite depth decay.");
        Fails(() => Build(Snapshot(), new ProfileSettings { FootprintMilliseconds = 0 }), "Reject zero time grouping.");
        Fails(() => Build(Snapshot(), new ProfileSettings { TpoMinutes = 0 }), "Reject zero TPO duration.");
        Fails(() => Build(Snapshot(), new ProfileSettings { DiagonalImbalanceRatio = .9 }), "Reject invalid diagonal ratio.");
        Fails(() => ProfileStudies.Build(Snapshot(), new ProfileSettings(), Origin, Origin), "Reject empty time range.");
        Fails(() => Build(Snapshot(profile: new[] { new ProfileLevel(1, 1, 0, 0), new ProfileLevel(1, 2, 0, 0) }), new ProfileSettings()),
            "Duplicate session prices cannot double count silently.");
        var invalidBook = Snapshot(); invalidBook.Bids = new[] { new DepthLevel(1, 1), new DepthLevel(1, 2) };
        Fails(() => Build(invalidBook, new ProfileSettings()), "Duplicate current-book rows must fail explicitly.");
        Fails(() => Build(Snapshot(new[] { Trade(0, 1, -1, TradeSide.Buy) }), visibleSettings), "Reject negative executed size.");
        Fails(() => Build(Snapshot(tape), new ProfileSettings { Scope = ProfileScope.Visible, BuildFootprint = true, MaximumFootprintBars = 1 }),
            "Footprint bar capacity bounds memory and reports incomplete output instead of silently dropping rows.");
        Fails(() => Build(Snapshot(tape), new ProfileSettings { Scope = ProfileScope.Visible, BuildFootprint = true, MaximumFootprintCells = 1 }),
            "Footprint cell capacity is enforced.");
        Fails(() => Build(Snapshot(tpoTape), new ProfileSettings { Scope = ProfileScope.Visible, BuildTpo = true, MaximumFootprintBars = 1 }),
            "TPO bracket capacity is enforced.");

        // Independent arithmetic oracle over random volume-weighted price distributions.
        var random = new Random(78616);
        for (int test = 0; test < 60; test++)
        {
            var trades = Enumerable.Range(0, 80).Select(i => Trade(i * 10, random.Next(-30, 31), random.Next(1, 40), (TradeSide)random.Next(3))).ToArray();
            var actual = Build(Snapshot(trades), new ProfileSettings { Scope = ProfileScope.Visible, BuildFootprint = true });
            decimal total = trades.Sum(t => (decimal)t.Volume);
            decimal mean = trades.Sum(t => (decimal)t.PriceTick * t.Volume) / total;
            decimal variance = trades.Sum(t => ((decimal)t.PriceTick - mean) * ((decimal)t.PriceTick - mean) * t.Volume) / total;
            assert(actual.TotalVolume == (long)total, "Randomized profile total agrees with independent tape sum.");
            Near(actual.Vwap, (double)mean * .25, "Randomized VWAP agrees with direct decimal weighted sum.");
            Near(actual.StandardDeviation, Math.Sqrt((double)variance) * .25, "Randomized deviation agrees with direct decimal second moment.");
            var expectedPoc = trades.GroupBy(t => t.PriceTick).Select(g => new { Tick = g.Key, Volume = g.Sum(t => t.Volume) })
                .OrderByDescending(g => g.Volume).ThenBy(g => g.Tick).First().Tick;
            assert(actual.PocTick == expectedPoc, "Randomized POC agrees with independent grouped tape.");
            long inArea = trades.Where(t => t.PriceTick >= actual.ValueAreaLowTick && t.PriceTick <= actual.ValueAreaHighTick).Sum(t => t.Volume);
            assert((decimal)inArea >= total * .7m, "Randomized contiguous value area covers at least its target volume.");
            assert(actual.Footprints.Sum(b => b.TotalVolume) == actual.TotalVolume, "Randomized footprint total matches unfiltered visible profile.");
        }
    }

    private static TradeRecord Trade(long milliseconds, long tick, long quantity, TradeSide side)
        => new(Origin + milliseconds * TimeSpan.TicksPerMillisecond, tick, quantity, side, milliseconds, 0);

    private static EngineSnapshot Snapshot(TradeRecord[]? trades = null, ProfileLevel[]? profile = null)
        => new() { TickSize = .25, TimeTicks = Origin + TimeSpan.TicksPerHour, SessionStartTicks = Origin,
            Trades = trades ?? Array.Empty<TradeRecord>(), Profile = profile ?? Array.Empty<ProfileLevel>() };

    private static ProfileResult Build(EngineSnapshot snapshot, ProfileSettings settings)
        => ProfileStudies.Build(snapshot, settings, Origin, Origin + TimeSpan.TicksPerHour);
}
