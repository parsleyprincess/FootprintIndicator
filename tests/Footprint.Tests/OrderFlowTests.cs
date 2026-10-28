using FootprintIndicator.Core;

public static class OrderFlowTests
{
    private static readonly long Start = new DateTime(2026, 10, 27, 12, 0, 0).Ticks;
    private static TradeRecord T(int ms, long price, long volume, TradeSide side, long sequence, int epoch = 0)
        => new(Start + ms * TimeSpan.TicksPerMillisecond, price, volume, side, sequence, 0, epoch);
    private static EngineSnapshot S(params TradeRecord[] trades) => new()
    {
        TimeTicks = trades.Length == 0 ? Start : trades[^1].TimeTicks, SessionStartTicks = Start,
        TickSize = .00005, Trades = trades, TradeEvents = trades.Length
    };
    private static OrderFlowSettings Options() => new()
    {
        AbsorptionVolume = 10, AbsorptionWindowMilliseconds = 100,
        SweepVolume = 10, SweepWindowMilliseconds = 100, SweepMinimumLevels = 3,
        LargePrintVolume = 10
    };

    public static void Run(Action<bool, string> assert)
    {
        var first = T(0, -21000, 6, TradeSide.Sell, 1);
        var second = T(50, -21000, 4, TradeSide.Sell, 2);
        assert(OrderFlowStudies.Build(S(first), Options()).Markers.Length == 0, "Absorption cannot appear before its volume threshold.");
        var confirmed = OrderFlowStudies.Build(S(first, second), Options()).Markers.Single();
        assert(confirmed.Kind == FlowMarkerKind.Absorption && confirmed.Side == TradeSide.Buy, "Sell executions must label passive BUY absorption.");
        assert(confirmed.TimeTicks == second.TimeTicks && confirmed.Volume == 10 && confirmed.PriceTick == -21000,
            "Absorption confirms at the threshold execution, at the actual fine/negative tick.");
        var consumed = OrderFlowStudies.Build(S(first, second, T(60, -21000, 1, TradeSide.Sell, 3)), Options());
        assert(consumed.Markers.Length == 1, "Consumed absorption evidence must not generate repeated marks.");
        var expired = OrderFlowStudies.Build(S(first, T(101, -21000, 4, TradeSide.Sell, 2)), Options());
        assert(expired.Markers.Length == 0, "Expired prints cannot satisfy absorption.");
        assert(OrderFlowStudies.Build(S(first, T(100, -21000, 4, TradeSide.Sell, 2)), Options()).Markers.Length == 1,
            "The documented rolling window includes its exact lower boundary.");
        assert(OrderFlowStudies.Build(S(first, T(50, -21000, 4, TradeSide.Sell, 2, 1)), Options()).Markers.Length == 0,
            "Absorption must not cross a reset epoch without depth frames.");
        var newSession = S(first, second); newSession.SessionStartTicks = second.TimeTicks;
        assert(OrderFlowStudies.Build(newSession, Options()).Markers.Length == 0, "Absorption must not reuse the previous session.");

        var sweepTrades = new[] { T(0, 100, 4, TradeSide.Buy, 1), T(30, 102, 3, TradeSide.Buy, 2), T(60, 101, 3, TradeSide.Buy, 3) };
        var sweep = OrderFlowStudies.Build(S(sweepTrades), Options()).Markers.Single();
        assert(sweep.Kind == FlowMarkerKind.Sweep && sweep.Levels == 3 && sweep.Volume == 10 && sweep.Side == TradeSide.Buy,
            "Sweep uses same-side distinct prices; monotonic ordering is not an invented requirement.");
        assert(sweep.TimeTicks == sweepTrades[2].TimeTicks, "Sweep marker cannot be backdated to the first print.");
        var twoLevels = sweepTrades.Select(t => new TradeRecord(t.TimeTicks, Math.Min(101, t.PriceTick), t.Volume, t.Side, t.Sequence, 0)).ToArray();
        assert(OrderFlowStudies.Build(S(twoLevels), Options()).Markers.Length == 0, "Repeated prices do not satisfy distinct sweep levels.");
        var opposite = sweepTrades.ToArray(); opposite[2] = T(60, 101, 3, TradeSide.Sell, 3);
        assert(OrderFlowStudies.Build(S(opposite), Options()).Markers.Length == 0, "Opposite aggression is not combined into a sweep.");
        var gap = sweepTrades.ToArray(); gap[2] = T(60, 101, 3, TradeSide.Buy, 3, 1);
        assert(OrderFlowStudies.Build(S(gap), Options()).Markers.Length == 0, "Sweeps cannot bridge a provider reset.");

        var large = OrderFlowStudies.Build(S(T(0, 100, 20, TradeSide.Unknown, 1)), Options()).Markers;
        assert(large.Length == 1 && large[0].Kind == FlowMarkerKind.LargePrint && large[0].Side == TradeSide.Unknown,
            "Unknown aggression can be a large print but cannot become a directional absorption or sweep.");
        var both = OrderFlowStudies.Build(S(T(0, 100, 20, TradeSide.Buy, 1)), Options()).Markers;
        assert(both.Length == 2 && both.Any(m => m.Kind == FlowMarkerKind.Absorption && m.Side == TradeSide.Sell),
            "Independent studies can describe the same print without changing the underlying ledger.");
        var longValues = Options(); longValues.AbsorptionVolume = long.MaxValue; longValues.LargePrintVolume = long.MaxValue;
        var huge = OrderFlowStudies.Build(S(T(0, 100, long.MaxValue - 1, TradeSide.Buy, 1), T(1, 100, 1, TradeSide.Buy, 2)), longValues);
        assert(huge.Markers.Single().Volume == long.MaxValue, "Threshold quantities retain full Int64 precision.");
        Throws<OverflowException>(() => OrderFlowStudies.Build(S(T(0, 100, long.MaxValue - 1, TradeSide.Buy, 1), T(1, 100, 2, TradeSide.Buy, 2)), longValues), assert,
            "Overflowing same-price volume must fail explicitly.");

        // Future prints may add marks, but never change a previously confirmed prefix.
        var stream = Enumerable.Range(0, 90).Select(i => T(i * 10, 100 + i % 3, 4, i % 2 == 0 ? TradeSide.Buy : TradeSide.Sell, i + 1)).ToArray();
        var complete = OrderFlowStudies.Build(S(stream), Options());
        for (int count = 1; count < stream.Length; count++)
        {
            var prefix = OrderFlowStudies.Build(S(stream.Take(count).ToArray()), Options());
            var expected = complete.Markers.Where(m => m.TimeTicks <= stream[count - 1].TimeTicks).ToArray();
            assert(prefix.Markers.Length == expected.Length, "Later prints changed historical marker count.");
            for (int i = 0; i < expected.Length; i++)
                assert(Signature(prefix.Markers[i]) == Signature(expected[i]), "Later prints changed an already confirmed marker.");
        }

        var bounded = Options(); bounded.MaxMarkers = 3;
        var trimmed = OrderFlowStudies.Build(S(stream), bounded);
        assert(trimmed.Truncated && trimmed.Markers.Length == 3, "Marker history must report bounded truncation.");
        assert(trimmed.Markers.Select(Signature).SequenceEqual(complete.Markers.TakeLast(3).Select(Signature)), "Trimming retains the latest exact markers.");
        // Multiple executions and study kinds can share one feed timestamp. A
        // bounded build must retain the same suffix regardless of trim boundaries.
        var simultaneous = Enumerable.Range(0, 90).Select(i => T(0, 100 + i % 4, 10 + i % 3,
            i % 3 == 0 ? TradeSide.Unknown : TradeSide.Buy, i + 1)).ToArray();
        var allSimultaneous = OrderFlowStudies.Build(S(simultaneous), Options());
        foreach (int maximum in new[] { 1, 2, 3, 7, 13 })
        {
            bounded.MaxMarkers = maximum;
            var sameTime = OrderFlowStudies.Build(S(simultaneous), bounded);
            assert(sameTime.Truncated && sameTime.Markers.Select(Signature).SequenceEqual(allSimultaneous.Markers.TakeLast(maximum).Select(Signature)),
                "Equal-time markers, including unknown prints and unequal volumes, must retain the exact sorted suffix after every trim.");
        }
        var disabled = Options(); disabled.EnableAbsorption = disabled.EnableSweeps = disabled.EnableLargePrints = false;
        assert(OrderFlowStudies.Build(S(stream), disabled).Markers.Length == 0, "Disabled studies must emit no execution markers.");

        var liquidity = S(T(150, 100, 60, TradeSide.Sell, 1)); liquidity.TimeTicks = Start + 300 * TimeSpan.TicksPerMillisecond;
        liquidity.Frames = new[] { Frame(0, 100, 100), Frame(100, 200, 100), Frame(200, 300, 100) };
        var depthOptions = Options(); depthOptions.EnableAbsorption = depthOptions.EnableSweeps = depthOptions.EnableLargePrints = false;
        depthOptions.EnableReplenishment = true; depthOptions.ReplenishmentVolume = 50;
        var refill = OrderFlowStudies.Build(liquidity, depthOptions).Markers.Single();
        assert(refill.Kind == FlowMarkerKind.ReplenishmentCandidate && refill.Side == TradeSide.Buy && refill.Volume == 60 && refill.Quantity == 60,
            "Stable observed bid quantity despite matched sell executions should yield only a coarse refill candidate.");
        assert(refill.TimeTicks == liquidity.Frames[1].ToTicks, "Coarse refill marker belongs at the completed frame endpoint.");
        liquidity.Frames = new[] { Frame(0, 100, 100), Frame(100, 200, 40), Frame(200, 300, 40) };
        assert(OrderFlowStudies.Build(liquidity, depthOptions).Markers.Length == 0, "Natural depletion is not a refill.");
        liquidity.Frames = new[] { Frame(0, 100, 100), Frame(100, 200, 100, 1), Frame(200, 300, 100, 1) };
        assert(OrderFlowStudies.Build(liquidity, depthOptions).Markers.Length == 0, "Coarse studies cannot bridge depth epochs.");
        liquidity.Frames = new[] { Frame(0, 100, 100), Frame(100, 200, 150), Frame(200, 300, 150) };
        depthOptions.EnableReplenishment = false; depthOptions.EnableLiquidityChanges = true; depthOptions.LiquidityChangeQuantity = 50;
        assert(OrderFlowStudies.Build(liquidity, depthOptions).Markers.Single().Kind == FlowMarkerKind.LiquidityAdded, "Observed level increase threshold should emit addition.");
        liquidity.Frames = new[] { Frame(0, 100, 150), Frame(100, 200, 100), Frame(200, 300, 100) };
        assert(OrderFlowStudies.Build(liquidity, depthOptions).Markers.Single().Kind == FlowMarkerKind.LiquidityRemoved, "Observed decrease is a quantity reduction, not a labeled cancellation.");
        liquidity.Frames = liquidity.Frames.Take(2).ToArray();
        assert(OrderFlowStudies.Build(liquidity, depthOptions).Markers.Length == 0, "Potentially partial final frame must not issue changing markers.");
        var sharedTime = S(Enumerable.Range(0, 30).Select(i => T(200, 100 + i % 3, 10, TradeSide.Unknown, i + 1)).ToArray());
        sharedTime.TimeTicks = Start + 300 * TimeSpan.TicksPerMillisecond;
        sharedTime.Frames = Enumerable.Range(0, 3).Select(i => new HeatFrame(Start + i * 100 * TimeSpan.TicksPerMillisecond,
            Start + (i + 1) * 100 * TimeSpan.TicksPerMillisecond, 0,
            Enumerable.Range(0, 30).Select(j => new HeatCell(130 - j, BookSide.Bid, i == 0 ? 100 : 200, 0)).ToArray(), 130, 131)).ToArray();
        var sharedOptions = Options(); sharedOptions.EnableLiquidityChanges = true; sharedOptions.LiquidityChangeQuantity = 50;
        var allShared = OrderFlowStudies.Build(sharedTime, sharedOptions);
        sharedOptions.MaxMarkers = 3;
        var limitedShared = OrderFlowStudies.Build(sharedTime, sharedOptions);
        assert(limitedShared.Truncated && limitedShared.Markers.Select(Signature).SequenceEqual(allShared.Markers.TakeLast(3).Select(Signature)),
            "A dense completed depth frame must merge with already trimmed execution markers in the same deterministic order.");
        var before = stream.Select(t => t.Volume).ToArray(); OrderFlowStudies.Build(S(stream), Options());
        assert(before.SequenceEqual(stream.Select(t => t.Volume)), "Studies cannot mutate source execution accounting.");
        var invalid = Options(); invalid.SweepMinimumLevels = 1;
        Throws<ArgumentException>(() => OrderFlowStudies.Build(S(), invalid), assert, "Invalid settings must fail.");
        invalid = Options(); invalid.MaxTrades = 1;
        Throws<InvalidOperationException>(() => OrderFlowStudies.Build(S(first, second), invalid), assert, "Exceeded input budget must fail explicitly.");
    }

    private static HeatFrame Frame(int from, int to, double q, int epoch = 0) => new(Start + from * TimeSpan.TicksPerMillisecond,
        Start + to * TimeSpan.TicksPerMillisecond, epoch, new[] { new HeatCell(100, BookSide.Bid, q, q) }, 100, 101);
    private static string Signature(FlowMarker m) => $"{m.TimeTicks}:{m.PriceTick}:{m.Kind}:{m.Side}:{m.Volume}:{m.Levels}:{m.Quantity}";
    private static void Throws<T>(Action action, Action<bool, string> assert, string text) where T : Exception
    {
        try { action(); } catch (T) { assert(true, text); return; }
        throw new InvalidOperationException(text);
    }
}
