using System;
using System.Collections.Generic;

namespace FootprintIndicator.Core
{
    public enum MboOperation { Add, Replace, Cancel, Execute }
    public enum MboChangeFilter { Any, Changed, Unchanged, SizeChanged, PriceChanged }

    public sealed class MboSettings
    {
        public int MaximumOrders = 100000;
        public int MaximumRefillCandidates = 10000;
        public int MaximumTracePoints = 20000;
        public int RefillWindowMilliseconds = 5000;
        public long LargeLotMinimumQuantity = 100;
    }

    /// <summary>One live order in an authoritative, complete snapshot.</summary>
    public sealed class MboOrderSeed
    {
        public readonly string OrderId;
        public readonly BookSide Side;
        public readonly long PriceTick, Quantity;
        public MboOrderSeed(string orderId, BookSide side, long priceTick, long quantity)
        { OrderId = orderId; Side = side; PriceTick = priceTick; Quantity = quantity; }
    }

    /// <summary>
    /// Normalized per-instrument sequence, not an unfiltered connection-global sequence.
    /// Replace quantity is absolute; Execute quantity reduces displayed remaining size;
    /// Cancel quantity must be zero. Side and price identify the resting order.
    /// </summary>
    public sealed class MboEvent
    {
        public readonly long Sequence, TimeTicks, PriceTick, Quantity;
        public readonly string OrderId;
        public readonly BookSide Side;
        public readonly MboOperation Operation;
        public MboEvent(long sequence, DateTime time, MboOperation operation, string orderId,
            BookSide side, long priceTick, long quantity)
        {
            Sequence = sequence; TimeTicks = time.Ticks; Operation = operation;
            OrderId = orderId; Side = side; PriceTick = priceTick; Quantity = quantity;
        }
    }

    public sealed class MboFilter
    {
        public long MinimumQuantity = 1, MaximumQuantity = long.MaxValue;
        public int MinimumAgeMilliseconds, MaximumAgeMilliseconds;
        public double KeepLargestPercent = 100;
        public MboChangeFilter Changes = MboChangeFilter.Any;
    }

    public sealed class MboOrderRow
    {
        public readonly string OrderId;
        public readonly BookSide Side;
        public readonly long PriceTick, Quantity, FirstObservedTicks, PriceSinceTicks, LastChangedTicks;
        public readonly long ObservedExecutedQuantity, RefillCount;
        public readonly bool SizeChanged, PriceChanged;
        internal MboOrderRow(OrderState order)
        {
            OrderId = order.Id; Side = order.Side; PriceTick = order.Tick; Quantity = order.Quantity;
            FirstObservedTicks = order.FirstTime; PriceSinceTicks = order.PriceSince;
            LastChangedTicks = order.ChangedTime; ObservedExecutedQuantity = order.Executed;
            RefillCount = order.Refills; SizeChanged = order.SizeChanged; PriceChanged = order.PriceChanged;
        }
    }

    public sealed class MboPriceLevel
    {
        public readonly BookSide Side;
        public readonly long PriceTick, Quantity, MinimumOrderQuantity, MaximumOrderQuantity;
        public readonly int OrderCount;
        public double AverageOrderQuantity { get { return Quantity / (double)OrderCount; } }
        internal MboPriceLevel(BookSide side, long tick, long quantity, int count, long minimum, long maximum)
        { Side = side; PriceTick = tick; Quantity = quantity; OrderCount = count; MinimumOrderQuantity = minimum; MaximumOrderQuantity = maximum; }
    }

    /// <summary>Observed same-ID refill, not proof of hidden size or a native iceberg.</summary>
    public sealed class MboRefillCandidate
    {
        public readonly string OrderId;
        public readonly BookSide Side;
        public readonly long TimeTicks, PriceTick, MatchedExecutionQuantity, AddedVisibleQuantity;
        public readonly long VisibleQuantityAfter, TotalObservedExecutedQuantity, RefillNumber;
        internal MboRefillCandidate(OrderState order, long time, long matched, long added)
        {
            OrderId = order.Id; Side = order.Side; TimeTicks = time; PriceTick = order.Tick;
            MatchedExecutionQuantity = matched; AddedVisibleQuantity = added;
            VisibleQuantityAfter = order.Quantity; TotalObservedExecutedQuantity = order.Executed;
            RefillNumber = order.Refills;
        }
    }

    public sealed class MboLotTracePoint
    {
        public readonly string OrderId;
        public readonly BookSide Side;
        public readonly MboOperation Operation;
        public readonly long TimeTicks, PriceTick, PreviousPriceTick, RemainingQuantity, ExecutedQuantity;
        internal MboLotTracePoint(MboEvent value, OrderState next, long previousPrice)
        {
            OrderId = value.OrderId; Side = value.Side; Operation = value.Operation;
            TimeTicks = value.TimeTicks; PriceTick = value.PriceTick; PreviousPriceTick = previousPrice;
            RemainingQuantity = value.Operation == MboOperation.Cancel ? 0 : next.Quantity;
            ExecutedQuantity = value.Operation == MboOperation.Execute ? value.Quantity : 0;
        }
    }

    public sealed class MboSnapshot
    {
        public bool Synchronized, ExchangeOrderIdsConfirmed;
        public long Sequence, TimeTicks, Epoch, AcceptedEvents, RejectedEvents, DuplicateEvents;
        public long ObservedExecutedQuantity, RawBidQuantity, RawAskQuantity, DroppedTracePoints, DroppedCandidates;
        public int RetainedOrderIdentities, RawDisplayedOrderCount;
        public string Status;
        public MboOrderRow[] Orders = new MboOrderRow[0];
        public MboPriceLevel[] Levels = new MboPriceLevel[0];
        public MboOrderRow[] LargeOrders = new MboOrderRow[0];
        public MboRefillCandidate[] RefillCandidates = new MboRefillCandidate[0];
        public MboLotTracePoint[] LargeLotTrace = new MboLotTracePoint[0];
    }

    // Internal state is never exposed by a snapshot. Events use a fresh copy before committing.
    internal sealed class OrderState
    {
        internal string Id;
        internal BookSide Side;
        internal long Tick, Quantity, FirstTime, PriceSince, ChangedTime, Executed, Refills;
        internal long PendingExecuted, PendingExecutionStart;
        internal bool SizeChanged, PriceChanged, WasLarge;
        internal OrderState Copy() { return (OrderState)MemberwiseClone(); }
    }

    /// <summary>
    /// Optional order-ID engine for a future genuine MBO bridge. Standard NinjaTrader
    /// OnMarketDepth is MBP and cannot populate this engine with real order identities.
    /// It starts unsynchronized and accepts no deltas until a complete snapshot arrives.
    /// </summary>
    public sealed class MboEngine
    {
        private readonly object gate = new object();
        private readonly int maximumOrders, maximumCandidates, maximumTrace;
        private readonly long largeMinimum, refillWindow;
        private Dictionary<string, OrderState> orders = new Dictionary<string, OrderState>(StringComparer.Ordinal);
        private readonly Queue<MboRefillCandidate> candidates = new Queue<MboRefillCandidate>();
        private readonly Queue<MboLotTracePoint> trace = new Queue<MboLotTracePoint>();
        private bool synchronized, confirmedIds;
        private long sequence, time, epoch, accepted, rejected, duplicates, executed;
        private long bidQuantity, askQuantity, droppedTrace, droppedCandidates;
        private string status = "MBO unavailable: waiting for an authoritative order-ID snapshot";

        public MboEngine(MboSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            if (settings.MaximumOrders < 1 || settings.MaximumOrders > 2000000 ||
                settings.MaximumRefillCandidates < 1 || settings.MaximumRefillCandidates > 1000000 ||
                settings.MaximumTracePoints < 1 || settings.MaximumTracePoints > 1000000 ||
                settings.RefillWindowMilliseconds < 1 || settings.RefillWindowMilliseconds > 3600000 ||
                settings.LargeLotMinimumQuantity < 1)
                throw new ArgumentOutOfRangeException("settings", "Invalid MBO memory, quantity or time limits.");
            maximumOrders = settings.MaximumOrders;
            maximumCandidates = settings.MaximumRefillCandidates;
            maximumTrace = settings.MaximumTracePoints;
            largeMinimum = settings.LargeLotMinimumQuantity;
            refillWindow = (long)settings.RefillWindowMilliseconds * TimeSpan.TicksPerMillisecond;
        }

        /// <summary>
        /// Replace the complete captured book at a known sequence/time boundary. This
        /// resets histories and counters; snapshot orders' ages start when observed.
        /// The bridge must buffer subsequent deltas until this operation completes.
        /// Invalid input throws without replacing the prior engine state.
        /// </summary>
        public void ResetSnapshot(long snapshotSequence, DateTime snapshotTime,
            IEnumerable<MboOrderSeed> snapshotOrders, bool exchangeOrderIdsConfirmed)
        {
            if (snapshotSequence < 0) throw new ArgumentOutOfRangeException("snapshotSequence");
            if (snapshotOrders == null) throw new ArgumentNullException("snapshotOrders");
            var replacement = new Dictionary<string, OrderState>(StringComparer.Ordinal);
            long bids = 0, asks = 0;
            foreach (MboOrderSeed seed in snapshotOrders)
            {
                if (seed == null || !ValidId(seed.OrderId) || !ValidSide(seed.Side) || seed.Quantity <= 0)
                    throw new ArgumentException("Snapshot requires valid unique order IDs, sides and positive quantities.", "snapshotOrders");
                if (replacement.Count >= maximumOrders) throw new ArgumentException("MBO snapshot exceeds retained-order budget.", "snapshotOrders");
                if (replacement.ContainsKey(seed.OrderId)) throw new ArgumentException("Duplicate order ID in snapshot.", "snapshotOrders");
                if (seed.Side == BookSide.Bid) bids = checked(bids + seed.Quantity);
                else asks = checked(asks + seed.Quantity);
                replacement.Add(seed.OrderId, new OrderState { Id = seed.OrderId, Side = seed.Side,
                    Tick = seed.PriceTick, Quantity = seed.Quantity, FirstTime = snapshotTime.Ticks,
                    PriceSince = snapshotTime.Ticks, ChangedTime = snapshotTime.Ticks,
                    WasLarge = seed.Quantity >= largeMinimum });
            }
            lock (gate)
            {
                orders = replacement;
                sequence = snapshotSequence; time = snapshotTime.Ticks;
                bidQuantity = bids; askQuantity = asks;
                confirmedIds = exchangeOrderIdsConfirmed; synchronized = true;
                if (epoch < long.MaxValue) epoch++;
                accepted = rejected = duplicates = executed = droppedTrace = droppedCandidates = 0;
                candidates.Clear(); trace.Clear();
                status = confirmedIds ? "Order-ID snapshot synchronized; native iceberg identification remains unproven"
                    : "Order-ID snapshot synchronized; exchange identity not confirmed, refill detection disabled";
            }
        }

        /// <summary>
        /// Apply one normalized delta. Old sequence numbers are ignored. Gaps, late
        /// timestamps, unknown IDs, capacity or arithmetic failures invalidate the
        /// live book until another complete authoritative snapshot is supplied.
        /// </summary>
        public bool Apply(MboEvent value)
        {
            lock (gate)
            {
                if (!synchronized) { Increment(ref rejected); return false; }
                if (value == null) return Reject("Null MBO event");
                if (value.Sequence <= sequence) { Increment(ref duplicates); return false; }
                if (sequence == long.MaxValue || value.Sequence != sequence + 1) return Reject("MBO sequence gap");
                if (value.TimeTicks < time) return Reject("MBO event clock moved backward");
                if (!ValidId(value.OrderId) || !ValidSide(value.Side) || value.Quantity < 0 ||
                    value.Operation < MboOperation.Add || value.Operation > MboOperation.Execute)
                    return Reject("Malformed MBO event");
                OrderState previous;
                bool found = orders.TryGetValue(value.OrderId, out previous);
                if (value.Operation == MboOperation.Add)
                {
                    if (found || value.Quantity == 0) return Reject("Duplicate add or empty new MBO order");
                    if (orders.Count >= maximumOrders) return Reject("MBO order budget exceeded");
                }
                else if (!found || previous.Side != value.Side)
                    return Reject("Unknown MBO order or incompatible side");
                if (value.Operation != MboOperation.Add && value.Operation != MboOperation.Replace && previous.Tick != value.PriceTick)
                    return Reject("MBO event price does not identify the current resting order");
                if (value.Operation == MboOperation.Cancel && value.Quantity != 0) return Reject("Cancel quantity must be zero");
                if (value.Operation == MboOperation.Execute && (value.Quantity == 0 || value.Quantity > previous.Quantity))
                    return Reject("MBO execution exceeds observed displayed remaining quantity");

                try
                {
                    OrderState next = found ? previous.Copy() : new OrderState { Id = value.OrderId, Side = value.Side,
                        Tick = value.PriceTick, FirstTime = value.TimeTicks, PriceSince = value.TimeTicks, ChangedTime = value.TimeTicks };
                    long oldQuantity = found ? previous.Quantity : 0;
                    long newExecuted = executed;
                    MboRefillCandidate candidate = null;
                    if (value.Operation == MboOperation.Add) next.Quantity = value.Quantity;
                    else if (value.Operation == MboOperation.Cancel) next.Quantity = 0;
                    else if (value.Operation == MboOperation.Execute)
                    {
                        next.Quantity = oldQuantity - value.Quantity;
                        next.Executed = checked(next.Executed + value.Quantity);
                        newExecuted = checked(executed + value.Quantity);
                        // Keep a bounded, conservative execution block. Expire the
                        // whole block if its first fill is too old; never count an
                        // expired fill just because later fills keep arriving.
                        if (next.PendingExecuted > 0 && value.TimeTicks - next.PendingExecutionStart > refillWindow)
                            next.PendingExecuted = 0;
                        if (next.PendingExecuted == 0) next.PendingExecutionStart = value.TimeTicks;
                        next.PendingExecuted = checked(next.PendingExecuted + value.Quantity);
                    }
                    else
                    {
                        bool samePrice = next.Tick == value.PriceTick;
                        next.Quantity = value.Quantity;
                        if (confirmedIds && samePrice && value.Quantity > oldQuantity && next.PendingExecuted > 0 &&
                            value.TimeTicks - next.PendingExecutionStart <= refillWindow)
                        {
                            next.Refills = checked(next.Refills + 1);
                            candidate = new MboRefillCandidate(next, value.TimeTicks, next.PendingExecuted, value.Quantity - oldQuantity);
                        }
                        if (!samePrice) { next.PriceChanged = true; next.PriceSince = value.TimeTicks; }
                        if (value.Quantity != oldQuantity) next.SizeChanged = true;
                        if (!samePrice || value.Quantity != oldQuantity) next.ChangedTime = value.TimeTicks;
                        next.Tick = value.PriceTick;
                        // A replacement consumes the observation; later replacements
                        // cannot count the same matched execution again.
                        next.PendingExecuted = 0;
                    }
                    long sideTotal = value.Side == BookSide.Bid ? bidQuantity : askQuantity;
                    long newTotal = checked(checked(sideTotal - oldQuantity) + next.Quantity);
                    next.WasLarge = next.WasLarge || next.Quantity >= largeMinimum;
                    MboLotTracePoint point = next.WasLarge ? new MboLotTracePoint(value, next, found ? previous.Tick : value.PriceTick) : null;

                    // All quantity arithmetic and semantic checks have completed.
                    if (value.Operation == MboOperation.Cancel) orders.Remove(value.OrderId);
                    else orders[value.OrderId] = next;
                    if (value.Side == BookSide.Bid) bidQuantity = newTotal; else askQuantity = newTotal;
                    executed = newExecuted; sequence = value.Sequence; time = value.TimeTicks;
                    Increment(ref accepted);
                    if (candidate != null)
                    {
                        if (candidates.Count >= maximumCandidates) { candidates.Dequeue(); Increment(ref droppedCandidates); }
                        candidates.Enqueue(candidate);
                    }
                    if (point != null)
                    {
                        if (trace.Count >= maximumTrace) { trace.Dequeue(); Increment(ref droppedTrace); }
                        trace.Enqueue(point);
                    }
                    return true;
                }
                catch (OverflowException) { return Reject("MBO quantity accounting overflow"); }
            }
        }

        public void Invalidate(string reason)
        {
            lock (gate) { ClearLiveBook(string.IsNullOrWhiteSpace(reason) ? "MBO stream invalidated" : reason); }
        }

        private bool Reject(string reason)
        {
            Increment(ref rejected);
            ClearLiveBook(reason);
            return false;
        }

        private void ClearLiveBook(string reason)
        {
            synchronized = false;
            orders.Clear(); bidQuantity = askQuantity = 0;
            status = reason + "; authoritative full MBO snapshot required";
        }

        public MboSnapshot Snapshot(MboFilter filter = null)
        {
            filter = CopyFilter(filter ?? new MboFilter());
            lock (gate)
            {
                var rows = new List<MboOrderRow>();
                int rawCount = 0;
                foreach (OrderState order in orders.Values)
                {
                    if (order.Quantity > 0) rawCount++;
                    if (order.Quantity < filter.MinimumQuantity || order.Quantity > filter.MaximumQuantity) continue;
                    long age = time - order.FirstTime;
                    if (age < (long)filter.MinimumAgeMilliseconds * TimeSpan.TicksPerMillisecond ||
                        (filter.MaximumAgeMilliseconds > 0 && age > (long)filter.MaximumAgeMilliseconds * TimeSpan.TicksPerMillisecond)) continue;
                    if (!MatchesChange(order, filter.Changes)) continue;
                    rows.Add(new MboOrderRow(order));
                }
                if (filter.KeepLargestPercent < 100 && rows.Count > 0)
                {
                    var sizes = new List<long>(rows.Count);
                    foreach (MboOrderRow row in rows) sizes.Add(row.Quantity);
                    sizes.Sort((a, b) => b.CompareTo(a));
                    int count = Math.Max(1, (int)Math.Ceiling(rows.Count * filter.KeepLargestPercent / 100));
                    long cutoff = sizes[count - 1];
                    rows.RemoveAll(row => row.Quantity < cutoff);
                }
                rows.Sort(CompareRows);
                var levels = new List<MboPriceLevel>();
                var large = new List<MboOrderRow>();
                for (int first = 0; first < rows.Count;)
                {
                    MboOrderRow start = rows[first];
                    int end = first;
                    long quantity = 0, minimum = long.MaxValue, maximum = 0;
                    while (end < rows.Count && rows[end].Side == start.Side && rows[end].PriceTick == start.PriceTick)
                    {
                        MboOrderRow row = rows[end++];
                        quantity = checked(quantity + row.Quantity);
                        minimum = Math.Min(minimum, row.Quantity); maximum = Math.Max(maximum, row.Quantity);
                        if (row.Quantity >= largeMinimum) large.Add(row);
                    }
                    levels.Add(new MboPriceLevel(start.Side, start.PriceTick, quantity, end - first, minimum, maximum));
                    first = end;
                }
                return new MboSnapshot { Synchronized = synchronized, ExchangeOrderIdsConfirmed = confirmedIds,
                    Sequence = sequence, TimeTicks = time, Epoch = epoch, AcceptedEvents = accepted,
                    RejectedEvents = rejected, DuplicateEvents = duplicates, ObservedExecutedQuantity = executed,
                    RawBidQuantity = bidQuantity, RawAskQuantity = askQuantity, RetainedOrderIdentities = orders.Count,
                    RawDisplayedOrderCount = rawCount, Orders = rows.ToArray(), Levels = levels.ToArray(), LargeOrders = large.ToArray(),
                    RefillCandidates = candidates.ToArray(), LargeLotTrace = trace.ToArray(),
                    DroppedTracePoints = droppedTrace, DroppedCandidates = droppedCandidates, Status = status };
            }
        }

        private static MboFilter CopyFilter(MboFilter filter)
        {
            var copy = new MboFilter { MinimumQuantity = filter.MinimumQuantity, MaximumQuantity = filter.MaximumQuantity,
                MinimumAgeMilliseconds = filter.MinimumAgeMilliseconds, MaximumAgeMilliseconds = filter.MaximumAgeMilliseconds,
                KeepLargestPercent = filter.KeepLargestPercent, Changes = filter.Changes };
            if (copy.MinimumQuantity < 1 || copy.MaximumQuantity < copy.MinimumQuantity || copy.MinimumAgeMilliseconds < 0 ||
                copy.MaximumAgeMilliseconds < 0 || (copy.MaximumAgeMilliseconds > 0 && copy.MaximumAgeMilliseconds < copy.MinimumAgeMilliseconds) ||
                double.IsNaN(copy.KeepLargestPercent) || double.IsInfinity(copy.KeepLargestPercent) || copy.KeepLargestPercent <= 0 || copy.KeepLargestPercent > 100 ||
                copy.Changes < MboChangeFilter.Any || copy.Changes > MboChangeFilter.PriceChanged)
                throw new ArgumentOutOfRangeException("filter", "Invalid order-size, age, percentile or change filter.");
            return copy;
        }

        private static bool MatchesChange(OrderState order, MboChangeFilter filter)
        {
            if (filter == MboChangeFilter.Changed) return order.SizeChanged || order.PriceChanged;
            if (filter == MboChangeFilter.Unchanged) return !order.SizeChanged && !order.PriceChanged;
            if (filter == MboChangeFilter.SizeChanged) return order.SizeChanged;
            if (filter == MboChangeFilter.PriceChanged) return order.PriceChanged;
            return true;
        }

        private static int CompareRows(MboOrderRow a, MboOrderRow b)
        {
            int compare = a.Side.CompareTo(b.Side);
            if (compare != 0) return compare;
            compare = a.Side == BookSide.Bid ? b.PriceTick.CompareTo(a.PriceTick) : a.PriceTick.CompareTo(b.PriceTick);
            if (compare != 0) return compare;
            compare = a.FirstObservedTicks.CompareTo(b.FirstObservedTicks);
            return compare != 0 ? compare : StringComparer.Ordinal.Compare(a.OrderId, b.OrderId);
        }

        private static bool ValidId(string id) { return !string.IsNullOrWhiteSpace(id) && id.Length <= 256; }
        private static bool ValidSide(BookSide side) { return side == BookSide.Bid || side == BookSide.Ask; }
        private static void Increment(ref long value) { if (value < long.MaxValue) value++; }
    }
}
