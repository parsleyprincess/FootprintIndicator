using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FootprintIndicator.Core;

public static class MboTests
{
    private static readonly DateTime Origin = new DateTime(2026, 10, 26, 12, 0, 0);

    public static void Run(Action<bool, string> assert)
    {
        var unavailable = New();
        assert(!unavailable.Snapshot().Synchronized, "MBO requires an actual authoritative snapshot before any capture.");
        assert(!unavailable.Apply(Event(1, 0, MboOperation.Add, "a", BookSide.Bid, 10, 2)), "A provider label or add event cannot fabricate initial MBO synchronization.");
        assert(unavailable.Snapshot().Orders.Length == 0, "Unavailable MBO cannot expose reconstructed orders.");

        var engine = New(large: 40);
        engine.ResetSnapshot(10, Origin, new[] { Seed("b1", BookSide.Bid, -10, 20), Seed("b2", BookSide.Bid, -10, 40),
            Seed("b3", BookSide.Bid, -11, 10), Seed("a1", BookSide.Ask, -9, 50), Seed("a2", BookSide.Ask, -8, 15) }, true);
        var snapshot = engine.Snapshot();
        assert(snapshot.Synchronized && snapshot.ExchangeOrderIdsConfirmed, "An authoritative exchange-ID snapshot establishes the declared capability.");
        assert(snapshot.RawBidQuantity == 70 && snapshot.RawAskQuantity == 65, "Initial quantities are aggregated independently by side.");
        assert(snapshot.Orders.Select(o => o.OrderId).SequenceEqual(new[] { "b1", "b2", "b3", "a1", "a2" }), "Rows sort bid descending, ask ascending, then stable observation time/ID.");
        assert(snapshot.Levels[0].Quantity == 60 && snapshot.Levels[0].OrderCount == 2 && snapshot.Levels[0].AverageOrderQuantity == 30,
            "MBO exposes real order count and mean order size at a price.");
        assert(snapshot.Levels[0].MinimumOrderQuantity == 20 && snapshot.Levels[0].MaximumOrderQuantity == 40, "MBO size distribution uses individual sizes.");
        assert(snapshot.LargeOrders.Length == 2, "Large-lot view is based on individual order quantities.");
        var filtered = engine.Snapshot(new MboFilter { MinimumQuantity = 30, MaximumQuantity = 45 });
        assert(filtered.Orders.Length == 1 && filtered.Orders[0].OrderId == "b2" && filtered.Levels[0].Quantity == 40,
            "Individual-size filtering differs from an aggregate-level cutoff.");
        assert(filtered.RawBidQuantity == 70 && filtered.RawAskQuantity == 65, "MBO view filters preserve raw totals.");
        assert(engine.Snapshot(new MboFilter { KeepLargestPercent = 20 }).Orders.Single().OrderId == "a1", "Largest-percent filter selects individual orders.");
        snapshot.Orders[0] = snapshot.Orders[1];
        snapshot.Levels[0] = snapshot.Levels[1];
        assert(engine.Snapshot().Orders[0].OrderId == "b1" && engine.Snapshot().Levels[0].Quantity == 60, "Snapshots do not expose mutable engine arrays.");

        assert(engine.Apply(Event(11, 2000, MboOperation.Add, "young", BookSide.Ask, 0, 4)), "A new order at a negative-price book boundary is valid.");
        assert(engine.Snapshot(new MboFilter { MinimumAgeMilliseconds = 1000 }).Orders.Length == 5, "Observed age does not age newly added orders with older snapshot identities.");
        assert(engine.Snapshot(new MboFilter { MaximumAgeMilliseconds = 1000 }).Orders.Single().OrderId == "young", "Maximum age excludes older observed identities.");
        assert(engine.Apply(Event(12, 2100, MboOperation.Replace, "b1", BookSide.Bid, -12, 25)), "Replace changes absolute price and remaining size.");
        var changed = engine.Snapshot(new MboFilter { Changes = MboChangeFilter.PriceChanged });
        assert(changed.Orders.Single().OrderId == "b1" && changed.Orders[0].PriceSinceTicks == Origin.AddMilliseconds(2100).Ticks,
            "A price change keeps identity but restarts observed price continuity.");
        assert(changed.Orders[0].FirstObservedTicks == Origin.Ticks && changed.Orders[0].SizeChanged, "First observation and changed-price time have separate meanings.");
        assert(engine.Snapshot(new MboFilter { Changes = MboChangeFilter.Unchanged }).Orders.Length == 5, "Change filter is independent of current size.");
        assert(engine.Apply(Event(13, 2200, MboOperation.Execute, "b2", BookSide.Bid, -10, 4)), "Matched execution reduces displayed remaining size.");
        assert(!engine.Snapshot().Orders.Single(o => o.OrderId == "b2").SizeChanged, "Execution alone is not labeled an order-replacement size change.");

        var ties = New();
        ties.ResetSnapshot(0, Origin, new[] { Seed("a", BookSide.Bid, 1, 10), Seed("b", BookSide.Bid, 1, 10), Seed("c", BookSide.Bid, 1, 1) }, true);
        assert(ties.Snapshot(new MboFilter { KeepLargestPercent = 20 }).Orders.Length == 2, "Percentile boundary retains all equal-sized ties deterministically.");

        var refill = New(large: 10, trace: 3, candidates: 1);
        refill.ResetSnapshot(0, Origin, new[] { Seed("native-id", BookSide.Ask, 101, 10) }, true);
        assert(refill.Apply(Event(1, 1, MboOperation.Execute, "native-id", BookSide.Ask, 101, 4)), "First same-ID fill accepted.");
        assert(refill.Apply(Event(2, 2, MboOperation.Execute, "native-id", BookSide.Ask, 101, 6)), "Fully executed displayed slice accepted.");
        assert(refill.Snapshot().Orders.Length == 0 && refill.Snapshot().RetainedOrderIdentities == 1,
            "Depleted identity is retained for an explicit refresh/cancel, but not drawn as displayed liquidity.");
        assert(refill.Apply(Event(3, 3, MboOperation.Replace, "native-id", BookSide.Ask, 101, 10)), "Same exchange identity can refresh a depleted slice.");
        var candidate = refill.Snapshot().RefillCandidates.Single();
        assert(candidate.MatchedExecutionQuantity == 10 && candidate.AddedVisibleQuantity == 10 && candidate.VisibleQuantityAfter == 10,
            "Refill records exact observed executions and visible increase, not inferred hidden size.");
        assert(candidate.TotalObservedExecutedQuantity == 10 && candidate.RefillNumber == 1 && candidate.TimeTicks == Origin.AddMilliseconds(3).Ticks,
            "Refill timing is causal at replacement and lifetime execution accounting is retained.");
        assert(refill.Apply(Event(4, 4, MboOperation.Replace, "native-id", BookSide.Ask, 101, 20)), "A size increase without a new fill is a normal replacement.");
        assert(refill.Snapshot().RefillCandidates.Length == 1, "The same execution block cannot be counted again on another increase.");
        assert(refill.Apply(Event(5, 5, MboOperation.Execute, "native-id", BookSide.Ask, 101, 5)), "New matched executions permit a new observation.");
        assert(refill.Apply(Event(6, 6, MboOperation.Replace, "native-id", BookSide.Ask, 101, 20)), "Second refill accepted.");
        var bounded = refill.Snapshot();
        assert(bounded.RefillCandidates.Length == 1 && bounded.DroppedCandidates == 1 && bounded.RefillCandidates[0].RefillNumber == 2,
            "Candidate history has an explicit bounded retention counter.");
        assert(bounded.LargeLotTrace.Length == 3 && bounded.DroppedTracePoints == 3, "Large-lot trace history is bounded independently.");
        assert(bounded.ObservedExecutedQuantity == 15 && bounded.RawAskQuantity == 20, "Trace/candidate trimming cannot alter executed or displayed quantities.");
        assert(refill.Apply(Event(7, 7, MboOperation.Cancel, "native-id", BookSide.Ask, 101, 0)), "Cancel removes the tracked identity.");
        assert(refill.Snapshot().LargeLotTrace.Last().Operation == MboOperation.Cancel && refill.Snapshot().LargeLotTrace.Last().RemainingQuantity == 0,
            "Large-lot history includes the final cancellation.");
        assert(refill.Apply(Event(8, 8, MboOperation.Add, "native-id", BookSide.Ask, 101, 10)), "An explicitly re-added ID has a new lifetime.");
        assert(refill.Snapshot().Orders.Single().ObservedExecutedQuantity == 0 && refill.Snapshot().Orders.Single().RefillCount == 0,
            "ID reuse after cancel cannot inherit an old order's execution evidence.");

        var unconfirmed = One(false);
        unconfirmed.Apply(Event(1, 1, MboOperation.Execute, "o", BookSide.Bid, 5, 3));
        unconfirmed.Apply(Event(2, 2, MboOperation.Replace, "o", BookSide.Bid, 5, 10));
        assert(unconfirmed.Snapshot().RefillCandidates.Length == 0, "Unconfirmed/synthetic IDs never enable exchange-ID refill detection.");
        var moved = One(true);
        moved.Apply(Event(1, 1, MboOperation.Execute, "o", BookSide.Bid, 5, 3));
        moved.Apply(Event(2, 2, MboOperation.Replace, "o", BookSide.Bid, 4, 10));
        assert(moved.Snapshot().RefillCandidates.Length == 0, "An order moving price does not constitute refill at its prior level.");
        var expired = One(true);
        expired.Apply(Event(1, 1, MboOperation.Execute, "o", BookSide.Bid, 5, 3));
        expired.Apply(Event(2, 6000, MboOperation.Replace, "o", BookSide.Bid, 5, 10));
        assert(expired.Snapshot().RefillCandidates.Length == 0, "Expired execution evidence is never promoted by a late increase.");
        var oldBlock = One(true);
        oldBlock.Apply(Event(1, 1, MboOperation.Execute, "o", BookSide.Bid, 5, 3));
        oldBlock.Apply(Event(2, 4999, MboOperation.Execute, "o", BookSide.Bid, 5, 3));
        oldBlock.Apply(Event(3, 5002, MboOperation.Replace, "o", BookSide.Bid, 5, 10));
        assert(oldBlock.Snapshot().RefillCandidates.Length == 0, "A recent fill cannot keep an expired execution block eligible indefinitely.");
        assert(oldBlock.Snapshot().ObservedExecutedQuantity == 6, "Conservative refill-window expiration never erases real executions.");

        var duplicate = One(true);
        assert(duplicate.Apply(Event(1, 1, MboOperation.Execute, "o", BookSide.Bid, 5, 2)), "Expected next sequence applies once.");
        assert(!duplicate.Apply(Event(1, 1, MboOperation.Execute, "o", BookSide.Bid, 5, 2)), "Duplicate sequence is not applied twice.");
        assert(duplicate.Snapshot().Synchronized && duplicate.Snapshot().ObservedExecutedQuantity == 2 && duplicate.Snapshot().DuplicateEvents == 1,
            "An ignored old sequence leaves current state intact.");
        assert(!duplicate.Apply(Event(3, 3, MboOperation.Execute, "o", BookSide.Bid, 5, 1)), "A missing sequence makes the book uncertain.");
        assert(!duplicate.Snapshot().Synchronized && duplicate.Snapshot().Orders.Length == 0 && duplicate.Snapshot().RawBidQuantity == 0,
            "Sequence loss withdraws live book output instead of extending stale orders.");
        assert(!duplicate.Apply(Event(2, 2, MboOperation.Execute, "o", BookSide.Bid, 5, 1)), "Late missing events cannot silently resynchronize a failed stream.");
        duplicate.ResetSnapshot(90, Origin.AddSeconds(1), new[] { Seed("fresh", BookSide.Ask, 4, 8) }, true);
        assert(duplicate.Snapshot().Synchronized && duplicate.Snapshot().Sequence == 90 && duplicate.Snapshot().ObservedExecutedQuantity == 0,
            "Only an authoritative fresh snapshot reestablishes continuity and clears earlier epoch accounting.");

        var malformed = new[] {
            Event(1,-1,MboOperation.Execute,"o",BookSide.Bid,5,1),
            Event(1,1,MboOperation.Execute,"unknown",BookSide.Bid,5,1),
            Event(1,1,MboOperation.Execute,"o",BookSide.Ask,5,1),
            Event(1,1,MboOperation.Execute,"o",BookSide.Bid,6,1),
            Event(1,1,MboOperation.Execute,"o",BookSide.Bid,5,11),
            Event(1,1,MboOperation.Execute,"o",BookSide.Bid,5,0),
            Event(1,1,MboOperation.Cancel,"o",BookSide.Bid,5,1),
            Event(1,1,MboOperation.Add,"o",BookSide.Bid,5,10),
            Event(1,1,MboOperation.Add,"empty",BookSide.Bid,5,0),
            Event(1,1,MboOperation.Replace,"o",BookSide.Bid,5,-1),
            Event(1,1,(MboOperation)99,"o",BookSide.Bid,5,1),
            Event(1,1,MboOperation.Add," ",BookSide.Bid,5,1) };
        foreach (var value in malformed)
        {
            var bad = One(true);
            assert(!bad.Apply(value) && !bad.Snapshot().Synchronized, "Malformed/out-of-order event must fail closed: " + value.Operation);
            assert(bad.Snapshot().ObservedExecutedQuantity == 0 && bad.Snapshot().AcceptedEvents == 0,
                "Rejected events cannot partially increment financial or accepted-event counters.");
        }
        var capacity = New(maxOrders: 1);
        capacity.ResetSnapshot(0, Origin, new[] { Seed("o", BookSide.Bid, 1, 1) }, true);
        capacity.Apply(Event(1, 1, MboOperation.Execute, "o", BookSide.Bid, 1, 1));
        assert(!capacity.Apply(Event(2, 2, MboOperation.Add, "extra", BookSide.Bid, 2, 1)), "Depleted identities count against the budget until cancel/reset.");
        assert(!capacity.Snapshot().Synchronized, "Capacity exhaustion is observable, not silently truncated order identity.");

        var overflow = New();
        overflow.ResetSnapshot(0, Origin, new[] { Seed("huge", BookSide.Bid, long.MinValue, long.MaxValue) }, true);
        assert(!overflow.Apply(Event(1, 1, MboOperation.Add, "extra", BookSide.Bid, long.MaxValue, 1)), "Cross-price side total overflow is rejected atomically.");
        assert(overflow.Snapshot().AcceptedEvents == 0 && overflow.Snapshot().ObservedExecutedQuantity == 0, "Overflow cannot partially update totals.");
        overflow.ResetSnapshot(0, Origin, new[] { Seed("huge", BookSide.Bid, 1, long.MaxValue) }, true);
        assert(overflow.Apply(Event(1, 1, MboOperation.Execute, "huge", BookSide.Bid, 1, long.MaxValue)), "Exact Int64 maximum execution is retained.");
        assert(overflow.Apply(Event(2, 2, MboOperation.Replace, "huge", BookSide.Bid, 1, 1)), "One unit can refill after a huge displayed slice.");
        assert(!overflow.Apply(Event(3, 3, MboOperation.Execute, "huge", BookSide.Bid, 1, 1)), "Cumulative execution overflow rejects the whole event.");
        assert(overflow.Snapshot().ObservedExecutedQuantity == long.MaxValue, "Cumulative financial volume never wraps.");

        var reset = One(true);
        bool threw = false;
        try { reset.ResetSnapshot(20, Origin, new[] { Seed("x", BookSide.Bid, 1, 1), Seed("x", BookSide.Ask, 2, 1) }, true); }
        catch (ArgumentException) { threw = true; }
        assert(threw && reset.Snapshot().Synchronized && reset.Snapshot().Orders.Single().OrderId == "o", "Malformed full snapshots preserve the last valid state atomically.");
        threw = false;
        try { reset.ResetSnapshot(20, Origin, new[] { Seed("x", BookSide.Bid, 1, long.MaxValue), Seed("y", BookSide.Bid, 2, 1) }, true); }
        catch (OverflowException) { threw = true; }
        assert(threw && reset.Snapshot().RawBidQuantity == 10, "Overflowing snapshot replacement is transactional.");
        reset.Invalidate("Provider disconnected");
        assert(!reset.Snapshot().Synchronized && reset.Snapshot().Orders.Length == 0 && reset.Snapshot().Status.Contains("snapshot required"),
            "Provider invalidation withdraws live depth and states the resynchronization prerequisite.");
        foreach (var filter in new[] { new MboFilter { MinimumQuantity = 0 }, new MboFilter { KeepLargestPercent = double.NaN },
            new MboFilter { MinimumAgeMilliseconds = 5, MaximumAgeMilliseconds = 1 }, new MboFilter { Changes = (MboChangeFilter)9 } })
        {
            threw = false; try { reset.Snapshot(filter); } catch (ArgumentOutOfRangeException) { threw = true; }
            assert(threw, "Invalid MBO view controls fail explicitly.");
        }
        var options = new MboSettings { MaximumOrders = 1 };
        var copied = new MboEngine(options); options.MaximumOrders = 100;
        copied.ResetSnapshot(0, Origin, new[] { Seed("x", BookSide.Ask, 1, 1) }, true);
        assert(!copied.Apply(Event(1, 1, MboOperation.Add, "y", BookSide.Ask, 2, 1)), "Settings are copied defensively at construction.");
        RandomLadder(assert);
        ConcurrentSnapshots(assert);
    }

    private static void RandomLadder(Action<bool, string> assert)
    {
        var engine = New();
        engine.ResetSnapshot(0, Origin, Array.Empty<MboOrderSeed>(), false);
        var expected = new Dictionary<string, (BookSide Side, long Tick, long Quantity)>();
        var random = new Random(7821);
        long volume = 0; int id = 0;
        for (int i = 1; i <= 400; ++i)
        {
            MboEvent value;
            if (expected.Count == 0 || random.Next(4) == 0)
            {
                string key = (++id).ToString(); var side = random.Next(2) == 0 ? BookSide.Bid : BookSide.Ask;
                long tick = random.Next(-100, 100), quantity = random.Next(1, 100);
                expected.Add(key, (side, tick, quantity));
                value = Event(i, i, MboOperation.Add, key, side, tick, quantity);
            }
            else
            {
                string key = expected.Keys.ElementAt(random.Next(expected.Count));
                var row = expected[key]; int choice = random.Next(3);
                if (choice == 0)
                {
                    expected.Remove(key); value = Event(i, i, MboOperation.Cancel, key, row.Side, row.Tick, 0);
                }
                else if (choice == 1 || row.Quantity == 0)
                {
                    long tick = random.Next(-100, 100), quantity = random.Next(0, 100);
                    expected[key] = (row.Side, tick, quantity); value = Event(i, i, MboOperation.Replace, key, row.Side, tick, quantity);
                }
                else
                {
                    long quantity = random.Next(1, (int)row.Quantity + 1);
                    volume += quantity; expected[key] = (row.Side, row.Tick, row.Quantity - quantity);
                    value = Event(i, i, MboOperation.Execute, key, row.Side, row.Tick, quantity);
                }
            }
            assert(engine.Apply(value), "Valid randomized order-ID stream must remain synchronized.");
            var actual = engine.Snapshot();
            assert(actual.Orders.Length == expected.Count(p => p.Value.Quantity > 0) && actual.RetainedOrderIdentities == expected.Count,
                "Randomized active/depleted identities agree with an independent dictionary model.");
            assert(actual.RawBidQuantity == expected.Values.Where(v => v.Side == BookSide.Bid).Sum(v => v.Quantity) &&
                actual.RawAskQuantity == expected.Values.Where(v => v.Side == BookSide.Ask).Sum(v => v.Quantity) && actual.ObservedExecutedQuantity == volume,
                "Randomized order mutations conserve raw book and execution totals.");
            assert(actual.Orders.All(o => expected[o.OrderId] == (o.Side, o.PriceTick, o.Quantity)), "Every randomized displayed row agrees with independent state.");
        }
    }

    private static void ConcurrentSnapshots(Action<bool, string> assert)
    {
        var engine = New(); engine.ResetSnapshot(0, Origin, Array.Empty<MboOrderSeed>(), true);
        int errors = 0;
        var writer = Task.Run(() =>
        {
            for (int i = 1; i <= 300; ++i)
                if (!engine.Apply(Event(i, i, MboOperation.Add, i.ToString(), BookSide.Bid, i, i))) Interlocked.Increment(ref errors);
        });
        var reader = Task.Run(() =>
        {
            for (int i = 0; i < 300; ++i)
            {
                var snapshot = engine.Snapshot();
                if (snapshot.RawBidQuantity != snapshot.Orders.Sum(o => o.Quantity) || snapshot.Sequence != snapshot.Orders.Length)
                    Interlocked.Increment(ref errors);
            }
        });
        Task.WaitAll(writer, reader);
        assert(errors == 0 && engine.Snapshot().Orders.Length == 300, "Concurrent snapshots observe coherent committed order state.");
    }

    private static MboEngine New(long large = 100, int maxOrders = 10000, int trace = 10000, int candidates = 10000)
    { return new MboEngine(new MboSettings { LargeLotMinimumQuantity = large, MaximumOrders = maxOrders, MaximumTracePoints = trace, MaximumRefillCandidates = candidates }); }
    private static MboEngine One(bool confirmed)
    { var result = New(); result.ResetSnapshot(0, Origin, new[] { Seed("o", BookSide.Bid, 5, 10) }, confirmed); return result; }
    private static MboOrderSeed Seed(string id, BookSide side, long tick, long quantity) { return new MboOrderSeed(id, side, tick, quantity); }
    private static MboEvent Event(long sequence, int milliseconds, MboOperation operation, string id, BookSide side, long tick, long quantity)
    { return new MboEvent(sequence, Origin.AddMilliseconds(milliseconds), operation, id, side, tick, quantity); }
}
