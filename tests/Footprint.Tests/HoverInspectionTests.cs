using FootprintIndicator.Core;

public static class HoverInspectionTests
{
    private static readonly long Origin = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc).Ticks;

    public static void Run(Action<bool, string> assert)
    {
        ExecutionReadout(assert);
        BubbleContentsAndSelection(assert);
        CapturedLiquidityAndGaps(assert);
        HiddenAndAggregatedDisplay(assert);
        BoundariesAndValidation(assert);
    }

    private static void BubbleContentsAndSelection(Action<bool, string> assert)
    {
        var snapshot = new EngineSnapshot
        {
            TickSize = .25,
            Trades = new[] {
                new TradeRecord(At(100), 100, 7, TradeSide.Buy, 1, 7),
                new TradeRecord(At(200), 102, 3, TradeSide.Sell, 2, 4),
                new TradeRecord(At(300), 99, 2, TradeSide.Unknown, 3, 4)
            }
        };
        var view = View(1000);
        var scene = Visualization.Build(snapshot, view, new DisplaySettings { Clustering = ClusterMode.Time, ClusterMilliseconds = 1000 });
        var bubble = scene.Bubbles.Single();
        var hit = HoverInspection.Inspect(snapshot, scene, view, bubble.X, bubble.Y);
        assert(hit != null && hit.Bubble != null, "An executed-volume circle must be inspectable at its actual rendered center.");
        var detail = hit!.Bubble!;
        assert(detail.Total == 12 && detail.Buy == 7 && detail.Sell == 3 && detail.Unknown == 2 && detail.Delta == 4,
            "Hover must preserve total and all three execution sides without confusing total and delta.");
        assert(detail.FromTicks == At(100) && detail.ToTicks == At(300) && detail.MinPriceTick == 99 && detail.MaxPriceTick == 102 && detail.HasPriceRange,
            "Cluster hover must use the actual member time and price extents.");
        assert(Math.Abs(detail.PriceTick - bubble.PriceTick) < 1e-10 && detail.TimeTicks == bubble.TimeTicks,
            "Hover retains the weighted center separately from the member extents.");
        assert(snapshot.Trades.Sum(t => t.Volume) == 12 && scene.Bubbles[0] == bubble,
            "Inspection cannot mutate captured trades or the rendered cluster.");

        var largest = Circle(50, 50, 20, 100);
        var firstSmall = Circle(50, 50, 6, 11);
        var lastSmall = Circle(50, 50, 6, 12);
        var circles = new[] { firstSmall, largest, lastSmall };
        var overlap = new RenderScene { Bubbles = circles };
        var smallView = new Viewport(Origin, At(1000), 90, 110, 100, 100);
        var visible = HoverInspection.SelectVisibleBubbles(circles, 2);
        assert(visible.Length == 2 && visible[0] == largest && visible[1] == firstSmall && circles[0] == firstSmall,
            "Render selection must retain largest N with stable ties and preserve the source order.");
        assert(HoverInspection.Inspect(snapshot, overlap, smallView, 50, 50).Bubble.Total == 12,
            "Equal center distances choose the last painted, topmost circle.");
        assert(HoverInspection.Inspect(snapshot, overlap, smallView, 50, 50, 1).Bubble.Total == 100,
            "A bubble omitted by the rendering cap must never be hit.");
        assert(HoverInspection.Inspect(snapshot, overlap, smallView, 50, 50, 2).Bubble.Total == 11,
            "Equal-radius selection ties must match paint order under a cap.");
        assert(HoverInspection.Inspect(snapshot, overlap, smallView, 56, 50).Bubble.Total == 12,
            "The exact circumference belongs to the visible circle.");
        assert(HoverInspection.Inspect(snapshot, overlap, smallView, 57, 50).Bubble.Total == 100,
            "A point beyond small circles may still inspect a containing larger circle.");
        assert(HoverInspection.Inspect(snapshot, overlap, smallView, 50, 50, 0).Bubble == null,
            "A disabled bubble layer must not produce a bubble hit.");

        lastSmall.X = 52;
        assert(HoverInspection.Inspect(snapshot, overlap, smallView, 50, 50).Bubble.Total == 11,
            "A closer center wins over a farther circle drawn later.");
        firstSmall.Radius = 0; largest.Radius = 0; lastSmall.Radius = 0;
        assert(HoverInspection.Inspect(snapshot, overlap, smallView, 50, 50).Bubble == null,
            "Balanced delta circles with zero radius must not be inspectable as visible bubbles.");
        assert(detail.Total == 12, "Inspection results are detached values rather than mutable scene aliases.");

        var legacy = Circle(50, 50, 5, 1);
        legacy.MinPriceTick = legacy.MaxPriceTick = 0;
        var legacyHit = HoverInspection.Inspect(snapshot, new RenderScene { Bubbles = new[] { legacy } }, smallView, 50, 50);
        assert(!legacyHit.Bubble.HasPriceRange, "Missing external cluster extents must not invent a price range at zero.");
    }

    private static void CapturedLiquidityAndGaps(Action<bool, string> assert)
    {
        var first = new HeatFrame(Origin, At(250), 3, new[] {
            new HeatCell(100, BookSide.Bid, 12.5, 3),
            new HeatCell(100, BookSide.Ask, 7.25, 2),
            new HeatCell(101, BookSide.Ask, 40, 20)
        }, 100, 101);
        var second = new HeatFrame(At(500), At(750), 4, new[] { new HeatCell(100, BookSide.Bid, 30, 10) }, 100, 101);
        var snapshot = new EngineSnapshot { Frames = new[] { second, first } };
        var view = View(1000);
        var scene = new RenderScene();
        var firstHit = HoverInspection.Inspect(snapshot, scene, view, 100, Y(view, 100)).Liquidity;
        assert(firstHit.HasObservation && firstHit.HasLevel && firstHit.RawBid == 12.5 && firstHit.RawAsk == 7.25 &&
            firstHit.PersistentBid == 3 && firstHit.PersistentAsk == 2 && firstHit.RawTotal == 19.75,
            "Captured bid and ask samples at the same price must remain separate and keep fractional quantities.");
        assert(firstHit.FromTicks == Origin && firstHit.ToTicks == At(250) && firstHit.Epoch == 3 && firstHit.FrameMilliseconds == 250,
            "Liquidity must disclose its sampled frame extent and continuity epoch.");
        assert(!firstHit.HasDisplayTile, "Captured quantities must remain available without any display tile.");
        assert(firstHit.BestBidTick == 100 && firstHit.BestAskTick == 101,
            "Hover quote values come from the inspected historical frame, separately from the hovered depth price.");

        var gap = HoverInspection.Inspect(snapshot, scene, view, 375, Y(view, 100)).Liquidity;
        assert(!gap.HasObservation && !gap.HasLevel && gap.RawTotal == 0 && gap.PersistentTotal == 0,
            "A capture gap must never borrow a prior frame or invent a live-book quantity.");
        assert(!gap.BestBidTick.HasValue && !gap.BestAskTick.HasValue,
            "Missing historical observations cannot borrow current or previous best quotes.");
        assert(!HoverInspection.Inspect(snapshot, scene, view, 250, Y(view, 100)).Liquidity.HasObservation,
            "The end of a capture frame is excluded from its half-open observation interval.");
        assert(HoverInspection.Inspect(snapshot, scene, view, 500, Y(view, 100)).Liquidity.RawBid == 30,
            "The start of a later frame is included without bridging the preceding gap.");
        assert(!HoverInspection.Inspect(snapshot, scene, view, 750, Y(view, 100)).Liquidity.HasObservation,
            "The newest recorded frame must not be extended past its observed end.");
        var absentLevel = HoverInspection.Inspect(snapshot, scene, view, 100, Y(view, 105)).Liquidity;
        assert(absentLevel.HasObservation && !absentLevel.HasLevel && absentLevel.RawTotal == 0,
            "An unrecorded price in an observed frame is distinct from a missing time frame.");
        assert(snapshot.Frames[0] == second && snapshot.Frames[1] == first && first.Cells[0].RawQuantity == 12.5,
            "Unsorted frame lookup must leave frame ordering and captured quantities unchanged.");
    }

    private static void HiddenAndAggregatedDisplay(Action<bool, string> assert)
    {
        var snapshot = new EngineSnapshot { Frames = new[] {
            new HeatFrame(Origin, At(250), 0, new[] { new HeatCell(100, BookSide.Bid, 42, 0) }, 100, 101)
        } };
        var view = View(250);
        var persistent = Visualization.Build(snapshot, view, new DisplaySettings { UsePersistence = true });
        var persistentHit = HoverInspection.Inspect(snapshot, persistent, view, 100, Y(view, 100)).Liquidity;
        assert(persistent.Heat.Length == 0 && persistentHit.RawBid == 42 && persistentHit.PersistentBid == 0 && !persistentHit.HasDisplayTile,
            "Persistence-hidden heat still exposes the recorded raw sample independently.");
        var threshold = Visualization.Build(snapshot, view, new DisplaySettings { UsePersistence = false, MinimumLiquidity = 50 });
        assert(HoverInspection.Inspect(snapshot, threshold, view, 100, Y(view, 100)).Liquidity.RawBid == 42,
            "The display threshold cannot erase captured quantities from inspection.");

        var coarseSnapshot = new EngineSnapshot { Frames = new[] {
            new HeatFrame(Origin, At(250), 0, new[] {
                new HeatCell(100, BookSide.Bid, 40, 30), new HeatCell(101, BookSide.Bid, 60, 50)
            }, 101, 102)
        } };
        var coarseView = new Viewport(Origin, At(250), 0, 2000, 100, 2000);
        var coarseScene = Visualization.Build(coarseSnapshot, coarseView, new DisplaySettings { UsePersistence = false, HeatColumnPixels = 100 });
        var coarseHit = HoverInspection.Inspect(coarseSnapshot, coarseScene, coarseView, 50, Y(coarseView, 100)).Liquidity;
        assert(coarseHit.RawBid == 40 && coarseHit.PersistentBid == 30 && coarseHit.HasDisplayTile && coarseHit.DisplayQuantity == 100,
            "A coarse price tile sum must stay separate from the individual captured price quantity.");

        var frames = Enumerable.Range(0, 9).Select(i =>
        {
            long best = 100 + i;
            var cells = Enumerable.Range(0, 13).Select(j => new HeatCell(best - j, BookSide.Bid, j == 4 ? 100 : 10, j == 4 ? 50 : 5)).ToArray();
            return new HeatFrame(At(i * 250), At((i + 1) * 250), 0, cells, best, best + 1);
        }).ToArray();
        var followingSnapshot = new EngineSnapshot { Frames = frames };
        var followingView = new Viewport(Origin, At(2250), 70, 125, 900, 550);
        var followingScene = Visualization.Build(followingSnapshot, followingView, new DisplaySettings {
            SuppressFollowingLiquidity = true, UsePersistence = false,
            FollowingMinimumMoves = 3, FollowingWindowMilliseconds = 10000,
            FollowingSizeRatio = 3, FollowingSizeTolerance = .35
        });
        var followingHit = HoverInspection.Inspect(followingSnapshot, followingScene, followingView, 850, Y(followingView, 104)).Liquidity;
        assert(followingHit.HasObservation && followingHit.HasLevel && followingHit.RawBid == 100 && followingHit.PersistentBid == 50 && !followingHit.HasDisplayTile,
            "Following-liquidity suppression must hide only display heat, leaving raw and persistent capture samples inspectable.");
    }

    private static void BoundariesAndValidation(Action<bool, string> assert)
    {
        var view = new Viewport(Origin, At(1000), 99.5, 101.5, 1000, 200);
        var snapshot = new EngineSnapshot { Frames = new[] {
            new HeatFrame(Origin, At(1000), 0, new[] {
                new HeatCell(100, BookSide.Bid, 10, 5), new HeatCell(101, BookSide.Ask, 20, 10)
            }, 100, 101)
        } };
        var scene = new RenderScene();
        assert(HoverInspection.Inspect(snapshot, scene, view, 0, 100).Liquidity.RawAsk == 20,
            "A half-tick price boundary belongs to the higher tick, matching the rendered price bands.");
        assert(HoverInspection.Inspect(snapshot, scene, view, 0, 100.001).Liquidity.RawBid == 10,
            "A pointer immediately below a half-tick boundary belongs to the lower price cell.");
        assert(HoverInspection.Inspect(snapshot, scene, view, 0, 100).TimeTicks == Origin,
            "The left plot edge must preserve the exact capture start tick.");
        assert(HoverInspection.Inspect(snapshot, scene, view, 999.999999, 100).TimeTicks < view.ToTicks,
            "The last in-bounds pixel must remain inside the half-open time interval.");
        assert(HoverInspection.Inspect(snapshot, scene, view, -1, 100) == null &&
            HoverInspection.Inspect(snapshot, scene, view, 1000, 100) == null &&
            HoverInspection.Inspect(snapshot, scene, view, 100, -1) == null &&
            HoverInspection.Inspect(snapshot, scene, view, 100, 200) == null,
            "Inspection must reject points outside all four plot edges.");

        var fineView = new Viewport(Origin, Origin + 1000, -2.5, -.5, 1000, 200);
        var fineHit = HoverInspection.Inspect(new EngineSnapshot(), scene, fineView, 1, 101);
        assert(fineHit.TimeTicks == Origin + 1 && fineHit.PriceTick == -2,
            "Relative time arithmetic must preserve individual DateTime ticks and negative price levels.");

        Throws<ArgumentNullException>(() => HoverInspection.Inspect(null!, scene, view, 1, 1), assert, "A snapshot is required.");
        Throws<ArgumentNullException>(() => HoverInspection.Inspect(snapshot, null!, view, 1, 1), assert, "A scene is required.");
        Throws<ArgumentNullException>(() => HoverInspection.Inspect(snapshot, scene, null!, 1, 1), assert, "A viewport is required.");
        Throws<ArgumentOutOfRangeException>(() => HoverInspection.Inspect(snapshot, scene, view, double.NaN, 1), assert, "NaN pointer positions must fail explicitly.");
        Throws<ArgumentOutOfRangeException>(() => HoverInspection.Inspect(snapshot, scene, view, 1, double.PositiveInfinity), assert, "Infinite pointer positions must fail explicitly.");
        Throws<ArgumentOutOfRangeException>(() => HoverInspection.Inspect(snapshot, scene, view, 1, 1, -1), assert, "Negative bubble caps must fail explicitly.");
        Throws<ArgumentOutOfRangeException>(() => HoverInspection.Inspect(snapshot, scene, new Viewport(Origin, Origin, 0, 1, 10, 10), 1, 1), assert, "Empty time ranges must fail explicitly.");
        Throws<ArgumentOutOfRangeException>(() => HoverInspection.Inspect(snapshot, scene, new Viewport(Origin, At(1000), 0, double.NaN, 10, 10), 1, 1), assert, "Invalid price ranges must fail explicitly.");
        Throws<ArgumentOutOfRangeException>(() => HoverInspection.Inspect(snapshot, scene, new Viewport(Origin, At(1000), 0, 1, 0, 10), 1, 1), assert, "Empty plot dimensions must fail explicitly.");
        var overlap = new EngineSnapshot { Frames = new[] { snapshot.Frames[0], snapshot.Frames[0] } };
        Throws<ArgumentException>(() => HoverInspection.Inspect(overlap, scene, view, 100, 100), assert, "Overlapping capture frames cannot silently double-count liquidity.");
        var invalid = Circle(50, 50, 5, 1); invalid.Radius = double.NaN;
        Throws<ArgumentException>(() => HoverInspection.SelectVisibleBubbles(new[] { invalid }), assert, "Malformed bubble geometry must fail explicitly.");
        var overflowing = Circle(50, 50, 5, long.MaxValue); overflowing.Unknown = 1;
        Throws<OverflowException>(() => HoverInspection.SelectVisibleBubbles(new[] { overflowing }), assert, "Execution quantities cannot silently wrap around.");
        var invalidQuantity = new EngineSnapshot { Frames = new[] {
            new HeatFrame(Origin, At(1000), 0, new[] { new HeatCell(100, BookSide.Bid, double.NaN, 0) }, 100, 101)
        } };
        Throws<ArgumentException>(() => HoverInspection.Inspect(invalidQuantity, scene, view, 100, 101), assert, "Invalid sampled quantities must fail explicitly.");
        var tooManyTiles = new RenderScene { Heat = new HeatTile[500001] };
        Throws<InvalidOperationException>(() => HoverInspection.Inspect(snapshot, tooManyTiles, view, 100, 100), assert, "Hover must reject an exceeded query budget rather than silently truncate observations.");
    }

    private static void ExecutionReadout(Action<bool, string> assert)
    {
        var trades = new[] {
            new TradeRecord(At(1), 100, 70, TradeSide.Buy, 1, 70),
            new TradeRecord(At(2), 101, 5, TradeSide.Sell, 2, 65),
            new TradeRecord(At(3), 100, 18, TradeSide.Buy, 3, 83),
            new TradeRecord(At(4), 100, 9, TradeSide.Sell, 4, 74)
        };
        var snapshot = new EngineSnapshot { Trades = trades };
        var readout = LastExecutionSummary.Build(snapshot);
        assert(readout.HasTrade && readout.RunVolume == 27 && readout.LastVolume == 9 && readout.Side == TradeSide.Sell && !readout.PartialRun,
            "Price-run readout is 27 (last 9), excluding earlier nonconsecutive prints at the same price.");
        snapshot.Trades = trades.Skip(2).ToArray(); snapshot.DroppedTrades = 2;
        assert(LastExecutionSummary.Build(snapshot).PartialRun, "A trimmed oldest run is explicitly partial.");
        snapshot.Trades = new[] { trades[2], new TradeRecord(At(4), 100, 9, TradeSide.Unknown, 4, 74, 1) };
        readout = LastExecutionSummary.Build(snapshot);
        assert(readout.RunVolume == 9 && !readout.PartialRun && readout.Side == TradeSide.Unknown, "Epoch changes end the run; unknown executions are not assigned a side.");
        snapshot.SessionStartTicks = At(5);
        assert(!LastExecutionSummary.Build(snapshot).HasTrade, "Earlier session prints cannot become the new session's last execution readout.");
        assert(!LastExecutionSummary.Build(new EngineSnapshot()).HasTrade, "No execution means no fabricated price-run quantities.");
        snapshot = new EngineSnapshot { Trades = new[] {
            new TradeRecord(At(1), 100, long.MaxValue, TradeSide.Buy, 1, 0),
            new TradeRecord(At(2), 100, 1, TradeSide.Buy, 2, 0) } };
        Throws<OverflowException>(() => LastExecutionSummary.Build(snapshot), assert, "Price-run sums cannot wrap around.");
    }

    private static Bubble Circle(double x, double y, double radius, long volume)
    {
        return new Bubble { X = x, Y = y, Radius = radius, PriceTick = 100, MinPriceTick = 100, MaxPriceTick = 100,
            TimeTicks = At(500), FromTicks = At(500), ToTicks = At(500), Buy = volume };
    }

    private static Viewport View(int milliseconds) { return new Viewport(Origin, At(milliseconds), 90, 110, 1000, 200); }
    private static long At(int milliseconds) { return Origin + milliseconds * TimeSpan.TicksPerMillisecond; }
    private static double Y(Viewport view, double tick) { return (view.MaxPriceTick - tick) / (view.MaxPriceTick - view.MinPriceTick) * view.Height; }

    private static void Throws<T>(Action action, Action<bool, string> assert, string message) where T : Exception
    {
        try { action(); }
        catch (T) { assert(true, message); return; }
        assert(false, message);
    }
}
