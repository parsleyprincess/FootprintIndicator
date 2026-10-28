using System;
using System.Collections.Generic;

namespace FootprintIndicator.Core
{
    /// <summary>
    /// Deterministic market-by-price engine. Input DateTime ticks are preserved: callers must
    /// supply one consistent clock/time zone and recreate the engine when replay seeks backward.
    /// Depth quantities are replacements, and positions are positions in each side's ladder.
    /// This engine deliberately makes no claim about individual order identity or cancellation.
    /// </summary>
    public sealed class MarketEngine
    {
        private readonly object gate = new object();
        private readonly double tickSize;
        private readonly long frameTicks, persistenceTicks, retentionTicks;
        private readonly int maxFrames, maxTrades, maxDepthLevels, maxProfileLevels, maxCohorts, maxFrameCells;
        private readonly List<DepthLevel> bids = new List<DepthLevel>();
        private readonly List<DepthLevel> asks = new List<DepthLevel>();
        private readonly Dictionary<LevelKey, LevelState> levels = new Dictionary<LevelKey, LevelState>();
        private readonly Dictionary<LevelKey, Integral> integrals = new Dictionary<LevelKey, Integral>();
        private readonly Queue<HeatFrame> frames = new Queue<HeatFrame>();
        private readonly Queue<TradeRecord> trades = new Queue<TradeRecord>();
        private readonly Dictionary<long, ProfileCounter> profile = new Dictionary<long, ProfileCounter>();
        private bool clockStarted, connected = true, bidTailUnknown, askTailUnknown;
        private long timeTicks, frameStart, nextBoundary, sessionStart;
        private long version, sequence, depthEvents, tradeEvents, invalidEvents, lateEvents, droppedFrames, droppedTrades;
        private long sessionVolume, cumulativeDelta;
        private double sessionVwap;
        private int epoch;
        private string status = "Waiting for depth";

        public MarketEngine(EngineSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (!Finite(settings.TickSize) || settings.TickSize <= 0) throw new ArgumentOutOfRangeException(nameof(settings.TickSize));
            if (settings.FrameMilliseconds < 1 || settings.FrameMilliseconds > 60000) throw new ArgumentOutOfRangeException(nameof(settings.FrameMilliseconds));
            if (settings.PersistenceMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(settings.PersistenceMilliseconds));
            if (settings.RetentionMinutes < 1 || settings.RetentionMinutes > 1440) throw new ArgumentOutOfRangeException(nameof(settings.RetentionMinutes));
            if (settings.MaxFrames < 1 || settings.MaxTrades < 1 || settings.MaxDepthLevels < 1 ||
                settings.MaxProfileLevels < 1 || settings.MaxCohortsPerLevel < 1)
                throw new ArgumentOutOfRangeException(nameof(settings), "All capacity limits must be positive.");
            // Copy values, not the mutable settings object supplied by the caller.
            tickSize = settings.TickSize;
            frameTicks = (long)settings.FrameMilliseconds * TimeSpan.TicksPerMillisecond;
            persistenceTicks = (long)settings.PersistenceMilliseconds * TimeSpan.TicksPerMillisecond;
            retentionTicks = (long)settings.RetentionMinutes * TimeSpan.TicksPerMinute;
            maxFrames = settings.MaxFrames;
            maxTrades = settings.MaxTrades;
            maxDepthLevels = settings.MaxDepthLevels;
            maxProfileLevels = settings.MaxProfileLevels;
            maxCohorts = settings.MaxCohortsPerLevel;
            maxFrameCells = (int)Math.Min(int.MaxValue, Math.Max(1024L, (long)maxDepthLevels * 8));
        }

        /// <summary>Round a finite feed price to the nearest instrument tick, away from zero at ties.</summary>
        public static long PriceToTick(double price, double tickSize)
        {
            if (!Finite(price) || !Finite(tickSize) || tickSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(price), "Price and tick size must be finite; tick size must be positive.");
            double scaled = price / tickSize;
            double rounded = Math.Round(scaled, MidpointRounding.AwayFromZero);
            // double(long.MaxValue) rounds to 2^63, which is already outside Int64.
            if (!Finite(rounded) || rounded >= 9223372036854775808d || rounded < -9223372036854775808d)
                throw new ArgumentOutOfRangeException(nameof(price), "Price does not fit an Int64 tick index.");
            return (long)rounded;
        }

        public void Depth(DateTime time, BookSide side, DepthMutation mutation, int position, double price, long volume)
        {
            lock (gate)
            {
                if (!connected) { Invalid("Disconnected; depth ignored"); return; }
                if ((side != BookSide.Bid && side != BookSide.Ask) ||
                    (mutation != DepthMutation.Insert && mutation != DepthMutation.Update && mutation != DepthMutation.Remove))
                { Invalid("Invalid depth side or operation"); return; }
                long tick;
                if (volume < 0 || !TryPrice(price, out tick))
                { InvalidateSide(side, "Invalid depth price or quantity"); return; }
                if (position < 0)
                { InvalidateSide(side, "Negative depth position; waiting for rebuild"); return; }

                // Malformed values must not move the market clock: a bad timestamp paired
                // with an invalid event must not fabricate decades of resting liquidity.
                long now = NormalizeTime(time.Ticks);
                AdvanceLocked(now);

                List<DepthLevel> source = side == BookSide.Bid ? bids : asks;
                if (position >= maxDepthLevels)
                {
                    // A feed may publish more levels than the configured capture limit.
                    // Such a mutation cannot change earlier positions. Remember that a
                    // tail exists without treating its unavailable contents as known.
                    if (side == BookSide.Bid) bidTailUnknown = true; else askTailUnknown = true;
                    CountDepthEvent();
                    return;
                }
                bool tailUnknown = side == BookSide.Bid ? bidTailUnknown : askTailUnknown;
                if (position > source.Count ||
                    (mutation == DepthMutation.Remove && position == source.Count))
                {
                    // Removing a captured row can expose an unobserved tail. Noncontiguous
                    // tail events do not invalidate the still-known prefix, but neither may
                    // they invent the intervening rows. A contiguous update can refill it.
                    if (tailUnknown) { CountDepthEvent(); return; }
                    InvalidateSide(side, "Depth position gap; waiting for contiguous rebuild");
                    return;
                }

                // Work on a candidate so malformed input cannot leave a partly mutated ladder.
                List<DepthLevel> candidate = new List<DepthLevel>(source);
                if (mutation == DepthMutation.Remove)
                {
                    if (candidate[position].Tick != tick)
                    { InvalidateSide(side, "Depth removal price mismatch; waiting for rebuild"); return; }
                    candidate.RemoveAt(position);
                }
                else if (mutation == DepthMutation.Insert)
                    candidate.Insert(position, new DepthLevel(tick, volume));
                else if (position == candidate.Count)
                {
                    // Some providers build the initial snapshot through contiguous Update events.
                    candidate.Add(new DepthLevel(tick, volume));
                }
                else
                {
                    // This is a positional provider stream: only Remove shifts rows.
                    // A zero-size Update retains its slot until a later explicit Remove.
                    candidate[position] = new DepthLevel(tick, volume);
                }

                if (!Ordered(candidate, side))
                { InvalidateSide(side, "Depth ordering or duplicate price mismatch; waiting for rebuild"); return; }
                // Tail levels beyond our configured capacity become unobserved. Never keep stale
                // levels around after the provider's rolling visible ladder has shifted.
                if (candidate.Count > maxDepthLevels)
                {
                    if (side == BookSide.Bid) bidTailUnknown = true; else askTailUnknown = true;
                    candidate.RemoveRange(maxDepthLevels, candidate.Count - maxDepthLevels);
                }
                ApplyLadder(side, candidate, now);
                source.Clear();
                source.AddRange(candidate);
                CountDepthEvent();
                LimitPartialFrame();
            }
        }

        /// <summary>
        /// Ingest an authoritative, complete observed depth snapshot in best-to-far
        /// order. This recovers a subscription joined in the middle of positional
        /// updates without inventing omitted rows. Inputs use absolute tick keys;
        /// duplicate prices must be aggregated by the provider adapter first.
        /// Unchanged price/quantity observations keep their persistence cohorts.
        /// Rejected snapshots leave the clock and previous book unchanged.
        /// </summary>
        public bool DepthSnapshot(DateTime time, DepthLevel[] bidLevels, DepthLevel[] askLevels)
        {
            lock (gate)
            {
                if (!connected) { Invalid("Disconnected; depth snapshot ignored"); return false; }
                List<DepthLevel> nextBids, nextAsks;
                bool bidTruncated, askTruncated;
                string error;
                if (!TrySnapshotLadder(bidLevels, BookSide.Bid, out nextBids, out bidTruncated, out error) ||
                    !TrySnapshotLadder(askLevels, BookSide.Ask, out nextAsks, out askTruncated, out error))
                {
                    Invalid(error);
                    return false;
                }

                long now = NormalizeTime(time.Ticks);
                AdvanceLocked(now);
                ApplyLadder(BookSide.Bid, nextBids, now);
                ApplyLadder(BookSide.Ask, nextAsks, now);
                bids.Clear(); bids.AddRange(nextBids);
                asks.Clear(); asks.AddRange(nextAsks);
                bidTailUnknown = bidTruncated; askTailUnknown = askTruncated;
                CountDepthEvent();
                LimitPartialFrame();
                return true;
            }
        }

        private bool TrySnapshotLadder(DepthLevel[] input, BookSide side, out List<DepthLevel> result,
            out bool truncated, out string error)
        {
            result = null; truncated = false; error = null;
            if (input == null || input.Length > 200000)
            { error = "Depth snapshot missing or exceeds input budget"; return false; }
            result = new List<DepthLevel>(Math.Min(input.Length, maxDepthLevels));
            long previous = 0;
            for (int i = 0; i < input.Length; i++)
            {
                DepthLevel row = input[i];
                if (row == null || row.Quantity < 0 || !Finite(row.Tick * tickSize))
                { error = "Depth snapshot contains invalid price or quantity"; return false; }
                if (i > 0 && (side == BookSide.Bid ? previous <= row.Tick : previous >= row.Tick))
                { error = "Depth snapshot must contain unique prices in best-to-far order"; return false; }
                previous = row.Tick;
                if (i < maxDepthLevels) result.Add(new DepthLevel(row.Tick, row.Quantity));
            }
            truncated = input.Length > maxDepthLevels;
            return true;
        }

        public void Trade(DateTime time, double price, long volume, double bid, double ask)
        {
            lock (gate)
            {
                if (!connected) { Invalid("Disconnected; trade ignored"); return; }
                long tick;
                if (volume <= 0 || !TryPrice(price, out tick)) { Invalid("Invalid trade price or quantity"); return; }
                ProfileCounter current;
                bool existed = profile.TryGetValue(tick, out current);
                if (!existed && profile.Count >= maxProfileLevels)
                { Invalid("Session profile capacity reached; increase MaxProfileLevels"); return; }
                if (!existed) current = new ProfileCounter();

                TradeSide side = TradeSide.Unknown;
                // A zero quote conventionally means unavailable in the NT feed API. Negative
                // futures prices remain supported. A locked/crossed/missing quote stays unknown.
                if (Finite(bid) && Finite(ask) && bid != 0 && ask != 0 && bid < ask)
                {
                    if (price >= ask) side = TradeSide.Buy;
                    else if (price <= bid) side = TradeSide.Sell;
                }
                long nextVolume, nextDelta, nextSequence, buy = current.Buy, sell = current.Sell, unknown = current.Unknown;
                try
                {
                    checked
                    {
                        nextVolume = sessionVolume + volume;
                        nextDelta = cumulativeDelta + (side == TradeSide.Buy ? volume : side == TradeSide.Sell ? -volume : 0);
                        nextSequence = sequence + 1;
                        if (side == TradeSide.Buy) buy += volume;
                        else if (side == TradeSide.Sell) sell += volume;
                        else unknown += volume;
                        long total = buy + sell + unknown;
                        if (total < 0) throw new OverflowException();
                    }
                }
                catch (OverflowException) { Invalid("Trade volume capacity overflow; event rejected atomically"); return; }
                double weight = (double)volume / nextVolume;
                double nextVwap = sessionVolume == 0 ? price : sessionVwap * (1 - weight) + price * weight;
                if (!Finite(nextVwap)) { Invalid("Non-finite trade average; event rejected"); return; }

                // Only an accepted print advances time. Rejected input must not mature
                // resting quantities, trim valid history, or poison later timestamps.
                long now = NormalizeTime(time.Ticks);
                AdvanceLocked(now);
                current.Buy = buy; current.Sell = sell; current.Unknown = unknown;
                if (!existed) profile.Add(tick, current);
                sessionVolume = nextVolume; cumulativeDelta = nextDelta; sequence = nextSequence; sessionVwap = nextVwap;
                trades.Enqueue(new TradeRecord(now, tick, volume, side, sequence, cumulativeDelta, epoch));
                tradeEvents = Increment(tradeEvents);
                version = Increment(version);
                Trim(now);
            }
        }

        /// <summary>
        /// Advance only with the feed/replay clock. Calling this with wall-clock time while
        /// replay is paused would fabricate history and must be avoided by the platform adapter.
        /// </summary>
        public void Advance(DateTime time)
        {
            lock (gate)
            {
                AdvanceLocked(NormalizeTime(time.Ticks));
            }
        }

        /// <summary>End current depth continuity, retaining already observed history and trades.</summary>
        public void ResetDepth(DateTime time, string reason)
        {
            lock (gate)
            {
                AdvanceLocked(NormalizeTime(time.Ticks));
                ClosePartialFrame();
                bids.Clear(); asks.Clear(); levels.Clear(); integrals.Clear();
                bidTailUnknown = askTailUnknown = false;
                epoch = epoch == int.MaxValue ? 0 : epoch + 1;
                status = string.IsNullOrWhiteSpace(reason) ? "Depth reset; waiting for rebuild" : reason;
                version = Increment(version);
            }
        }

        public void SetConnected(DateTime time, bool isConnected)
        {
            lock (gate)
            {
                AdvanceLocked(NormalizeTime(time.Ticks));
                if (connected == isConnected) return;
                ClosePartialFrame();
                bids.Clear(); asks.Clear(); levels.Clear(); integrals.Clear();
                bidTailUnknown = askTailUnknown = false;
                connected = isConnected;
                epoch = epoch == int.MaxValue ? 0 : epoch + 1;
                status = isConnected ? "Connected; waiting for depth" : "Disconnected";
                version = Increment(version);
            }
        }

        /// <summary>
        /// Reset session totals at an adapter-supplied trading-hours boundary; retained historical
        /// events remain intact. CVD values in old TradeRecords still describe their own session.
        /// </summary>
        public void StartSession(DateTime time)
        {
            lock (gate)
            {
                AdvanceLocked(NormalizeTime(time.Ticks));
                profile.Clear(); sessionVolume = 0; cumulativeDelta = 0; sessionVwap = 0;
                sessionStart = timeTicks;
                version = Increment(version);
            }
        }

        /// <summary>
        /// Load previously captured history into a fresh engine. Historical values
        /// are validated and copied transactionally; live depth and persistence ages
        /// are never restored. The next connection/observation begins a new epoch.
        /// Session totals can outlive retained trades, but must match the full saved
        /// session profile. The adapter decides whether that saved session is still
        /// the current Trading Hours session before accepting new executions.
        /// </summary>
        public void RestoreHistory(EngineSnapshot history)
        {
            if (history == null) throw new ArgumentNullException("history");
            lock (gate)
            {
                if (clockStarted || depthEvents != 0 || tradeEvents != 0 || levels.Count != 0)
                    throw new InvalidOperationException("Recorded history can only be restored into a fresh engine.");
                if (history.TickSize != tickSize || history.TimeTicks < 0 || history.TimeTicks > DateTime.MaxValue.Ticks ||
                    history.SessionStartTicks < 0 || history.SessionStartTicks > history.TimeTicks || history.Epoch < 0 ||
                    history.Version < 0 || history.DepthEvents < 0 || history.TradeEvents < 0 || history.InvalidEvents < 0 ||
                    history.LateEvents < 0 || history.DroppedFrames < 0 || history.DroppedTrades < 0 || history.SessionVolume < 0 ||
                    (history.SessionVolume > 0 && !Finite(history.SessionVwap)))
                    throw new ArgumentException("Recorded history metadata or instrument tick size is invalid.", "history");
                HeatFrame[] sourceFrames = history.Frames ?? new HeatFrame[0];
                TradeRecord[] sourceTrades = history.Trades ?? new TradeRecord[0];
                ProfileLevel[] sourceProfile = history.Profile ?? new ProfileLevel[0];
                if (sourceFrames.Length > 200000 || sourceTrades.Length > 2000000 || sourceProfile.Length > maxProfileLevels)
                    throw new ArgumentException("Recorded history exceeds configured restore budgets.", "history");

                var newProfile = new Dictionary<long, ProfileCounter>();
                long profileVolume = 0, profileDelta = 0;
                foreach (ProfileLevel row in sourceProfile)
                {
                    if (row == null || row.Buy < 0 || row.Sell < 0 || row.Unknown < 0 ||
                        !Finite(row.Tick * tickSize) || newProfile.ContainsKey(row.Tick))
                        throw new ArgumentException("Recorded history contains an invalid session profile.", "history");
                    checked
                    {
                        profileVolume += row.Buy + row.Sell + row.Unknown;
                        profileDelta += row.Buy - row.Sell;
                    }
                    newProfile.Add(row.Tick, new ProfileCounter { Buy = row.Buy, Sell = row.Sell, Unknown = row.Unknown });
                }
                if (profileVolume != history.SessionVolume || profileDelta != history.CumulativeDelta)
                    throw new ArgumentException("Recorded session profile does not match volume or delta totals.", "history");

                long cutoff = history.TimeTicks - Math.Min(history.TimeTicks, retentionTicks);
                long frameEnd = 0, cellVisits = 0;
                long trimmedFrames = 0, trimmedTrades = 0, previousSequence = 0, previousTradeTime = 0;
                var newFrames = new Queue<HeatFrame>();
                foreach (HeatFrame frame in sourceFrames)
                {
                    if (frame == null || frame.FromTicks < frameEnd || frame.FromTicks < 0 || frame.ToTicks <= frame.FromTicks ||
                        frame.ToTicks > history.TimeTicks || frame.Epoch < 0 || frame.Cells == null)
                        throw new ArgumentException("Recorded heat frames overlap or contain invalid time metadata.", "history");
                    frameEnd = frame.ToTicks;
                    cellVisits += frame.Cells.Length;
                    if (cellVisits > 12000000)
                        throw new ArgumentException("Recorded heat cells exceed restore input budget.", "history");
                    var unique = new HashSet<LevelKey>();
                    foreach (HeatCell cell in frame.Cells)
                    {
                        if (cell == null || (cell.Side != BookSide.Bid && cell.Side != BookSide.Ask) ||
                            !Finite(cell.Tick * tickSize) || !Finite(cell.RawQuantity) || !Finite(cell.PersistentQuantity) ||
                            cell.RawQuantity < 0 || cell.PersistentQuantity < 0 || cell.PersistentQuantity > cell.RawQuantity ||
                            !unique.Add(new LevelKey(cell.Side, cell.Tick)))
                            throw new ArgumentException("Recorded heat frame contains invalid or duplicate price cells.", "history");
                    }
                    if (frame.ToTicks <= cutoff) { trimmedFrames++; continue; }
                    newFrames.Enqueue(CopyFrame(frame));
                    if (newFrames.Count > maxFrames) { newFrames.Dequeue(); trimmedFrames++; }
                }
                var newTrades = new Queue<TradeRecord>();
                foreach (TradeRecord trade in sourceTrades)
                {
                    if (trade == null || trade.TimeTicks < previousTradeTime || trade.TimeTicks < 0 || trade.TimeTicks > history.TimeTicks ||
                        trade.Sequence <= previousSequence || trade.Sequence > history.TradeEvents || trade.Volume <= 0 || trade.Epoch < 0 ||
                        !Finite(trade.PriceTick * tickSize) ||
                        (trade.Side != TradeSide.Buy && trade.Side != TradeSide.Sell && trade.Side != TradeSide.Unknown))
                        throw new ArgumentException("Recorded trades have invalid values, time order, or sequence.", "history");
                    previousSequence = trade.Sequence; previousTradeTime = trade.TimeTicks;
                    if (trade.TimeTicks < cutoff) { trimmedTrades++; continue; }
                    newTrades.Enqueue(new TradeRecord(trade.TimeTicks, trade.PriceTick, trade.Volume,
                        trade.Side, trade.Sequence, trade.CumulativeDelta, trade.Epoch));
                    if (newTrades.Count > maxTrades) { newTrades.Dequeue(); trimmedTrades++; }
                }

                // Commit only after every value has passed validation.
                foreach (HeatFrame frame in newFrames) frames.Enqueue(frame);
                foreach (TradeRecord trade in newTrades) trades.Enqueue(trade);
                foreach (KeyValuePair<long, ProfileCounter> row in newProfile) profile.Add(row.Key, row.Value);
                clockStarted = true; connected = false;
                timeTicks = frameStart = history.TimeTicks;
                nextBoundary = timeTicks - timeTicks % frameTicks + frameTicks;
                sessionStart = history.SessionStartTicks;
                sessionVolume = history.SessionVolume; cumulativeDelta = history.CumulativeDelta;
                sessionVwap = sessionVolume == 0 ? 0 : history.SessionVwap;
                depthEvents = history.DepthEvents; tradeEvents = history.TradeEvents; sequence = history.TradeEvents;
                invalidEvents = history.InvalidEvents; lateEvents = history.LateEvents;
                droppedFrames = AddCount(history.DroppedFrames, trimmedFrames);
                droppedTrades = AddCount(history.DroppedTrades, trimmedTrades);
                epoch = history.Epoch == int.MaxValue ? 0 : history.Epoch + 1;
                version = Increment(history.Version);
                status = "Recorded history restored; waiting for a fresh instrument connection and depth";
            }
        }

        /// <summary>A fully detached view. Taking a snapshot never advances the market clock.</summary>
        public EngineSnapshot Snapshot()
        {
            lock (gate)
            {
                HeatFrame partial = MakeFrame(timeTicks);
                int totalFrames = frames.Count + (partial == null ? 0 : 1);
                int skip = Math.Max(0, totalFrames - maxFrames);
                List<HeatFrame> copies = new List<HeatFrame>(Math.Min(totalFrames, maxFrames));
                foreach (HeatFrame frame in frames)
                {
                    if (skip > 0) { skip--; continue; }
                    copies.Add(CopyFrame(frame));
                }
                if (partial != null) copies.Add(partial);
                List<ProfileLevel> profileCopy = new List<ProfileLevel>(profile.Count);
                foreach (KeyValuePair<long, ProfileCounter> pair in profile)
                    profileCopy.Add(new ProfileLevel(pair.Key, pair.Value.Buy, pair.Value.Sell, pair.Value.Unknown));
                profileCopy.Sort((a, b) => a.Tick.CompareTo(b.Tick));
                return new EngineSnapshot
                {
                    Version = version, TimeTicks = timeTicks, SessionStartTicks = sessionStart,
                    DepthEvents = depthEvents, TradeEvents = tradeEvents, InvalidEvents = invalidEvents,
                    LateEvents = lateEvents, DroppedFrames = droppedFrames, DroppedTrades = droppedTrades,
                    CumulativeDelta = cumulativeDelta, SessionVolume = sessionVolume, TickSize = tickSize,
                    SessionVwap = sessionVolume == 0 ? double.NaN : sessionVwap, Epoch = epoch,
                    Connected = connected, HasDepth = BestPositiveTick(bids).HasValue || BestPositiveTick(asks).HasValue, Status = status,
                    Frames = copies.ToArray(), Trades = trades.ToArray(), Bids = bids.ToArray(),
                    Asks = asks.ToArray(), Profile = profileCopy.ToArray()
                };
            }
        }

        private long NormalizeTime(long requested)
        {
            if (!clockStarted)
            {
                clockStarted = true;
                timeTicks = requested; frameStart = requested; sessionStart = requested;
                nextBoundary = requested - requested % frameTicks + frameTicks;
                return requested;
            }
            if (requested < timeTicks)
            {
                lateEvents = Increment(lateEvents);
                return timeTicks;
            }
            return requested;
        }

        private void AdvanceLocked(long target)
        {
            if (target <= timeTicks) return;
            // A disconnected/empty book does not produce manufactured zero-liquidity frames.
            // Flush any previously observed partial interval, then jump over the unknown gap.
            if (levels.Count == 0 && integrals.Count == 0)
            {
                timeTicks = target;
                frameStart = target;
                nextBoundary = target - target % frameTicks + frameTicks;
                Trim(target);
                version = Increment(version);
                return;
            }

            // Bound a large clock jump to work that can still be retained. This retains the
            // standing book's age while avoiding billions of iterations after a long pause.
            long due = target >= nextBoundary ? (target - nextBoundary) / frameTicks + 1 : 0;
            long windowFrames = Math.Min((long)maxFrames, retentionTicks / frameTicks + 1);
            if (due > windowFrames)
            {
                long discarded = due - windowFrames;
                long jump = nextBoundary + (discarded - 1) * frameTicks;
                droppedFrames = AddCount(droppedFrames, discarded);
                integrals.Clear();
                timeTicks = jump; frameStart = jump; nextBoundary = jump + frameTicks;
            }
            while (target >= nextBoundary)
            {
                Integrate(nextBoundary);
                EnqueueFrame(MakeFrame(nextBoundary));
                integrals.Clear();
                frameStart = nextBoundary;
                nextBoundary += frameTicks;
                if (levels.Count == 0)
                {
                    // The final removed level has now closed its last sampled interval.
                    frameStart = target; timeTicks = target;
                    nextBoundary = target - target % frameTicks + frameTicks;
                    break;
                }
            }
            Integrate(target);
            Trim(target);
            version = Increment(version);
            LimitPartialFrame();
        }

        private void Integrate(long to)
        {
            if (to <= timeTicks) return;
            long from = timeTicks;
            foreach (KeyValuePair<LevelKey, LevelState> pair in levels)
            {
                LevelState level = pair.Value;
                Integral sum;
                if (!integrals.TryGetValue(pair.Key, out sum))
                {
                    sum = new Integral(); integrals.Add(pair.Key, sum);
                }
                sum.Raw += (double)level.Quantity * (to - from);
                double persistentIntegral = 0;
                foreach (Cohort cohort in level.Cohorts)
                {
                    long mature = cohort.Since + persistenceTicks;
                    long eligibleStart = Math.Max(from, mature);
                    if (to > eligibleStart) persistentIntegral += (double)cohort.Quantity * (to - eligibleStart);
                }
                sum.Persistent += persistentIntegral;
            }
            timeTicks = to;
        }

        private HeatFrame MakeFrame(long to)
        {
            if (to <= frameStart || integrals.Count == 0) return null;
            double duration = to - frameStart;
            List<HeatCell> cells = new List<HeatCell>(integrals.Count);
            foreach (KeyValuePair<LevelKey, Integral> pair in integrals)
            {
                if (pair.Value.Raw <= 0) continue;
                double raw = pair.Value.Raw / duration;
                double persistent = Math.Min(raw, Math.Max(0, pair.Value.Persistent / duration));
                cells.Add(new HeatCell(pair.Key.Tick, pair.Key.Side, raw, persistent));
            }
            if (cells.Count == 0) return null;
            cells.Sort((a, b) => a.Side == b.Side ? a.Tick.CompareTo(b.Tick) : a.Side.CompareTo(b.Side));
            return new HeatFrame(frameStart, to, epoch, cells.ToArray(),
                BestPositiveTick(bids), BestPositiveTick(asks));
        }

        private void ClosePartialFrame()
        {
            EnqueueFrame(MakeFrame(timeTicks));
            integrals.Clear();
            frameStart = timeTicks;
            nextBoundary = timeTicks - timeTicks % frameTicks + frameTicks;
        }

        private void LimitPartialFrame()
        {
            // A violently moving book can visit many price levels within one nominal time bin.
            // Split the bin early instead of allowing an unbounded accumulator dictionary.
            if (integrals.Count > maxFrameCells) ClosePartialFrame();
        }

        private void EnqueueFrame(HeatFrame frame)
        {
            if (frame == null) return;
            frames.Enqueue(frame);
            while (frames.Count > maxFrames)
            { frames.Dequeue(); droppedFrames = Increment(droppedFrames); }
        }

        private void ApplyLadder(BookSide side, List<DepthLevel> candidate, long now)
        {
            HashSet<long> present = new HashSet<long>();
            foreach (DepthLevel next in candidate)
            {
                if (next.Quantity == 0) continue;
                present.Add(next.Tick);
                LevelKey key = new LevelKey(side, next.Tick);
                LevelState state;
                if (!levels.TryGetValue(key, out state))
                {
                    state = new LevelState(); levels.Add(key, state);
                }
                if (next.Quantity > state.Quantity)
                {
                    long added = next.Quantity - state.Quantity;
                    if (state.Cohorts.Count > 0 && state.Cohorts[state.Cohorts.Count - 1].Since == now)
                        state.Cohorts[state.Cohorts.Count - 1].Quantity += added;
                    else state.Cohorts.Add(new Cohort(now, added));
                }
                else if (next.Quantity < state.Quantity)
                {
                    long reduction = state.Quantity - next.Quantity;
                    // Youngest-first removal computes an aggregate lower envelope, not order age.
                    while (reduction > 0)
                    {
                        int last = state.Cohorts.Count - 1;
                        Cohort youngest = state.Cohorts[last];
                        long removed = Math.Min(reduction, youngest.Quantity);
                        youngest.Quantity -= removed; reduction -= removed;
                        if (youngest.Quantity == 0) state.Cohorts.RemoveAt(last);
                    }
                }
                state.Quantity = next.Quantity;
                // Merge fully matured cohorts without changing any present/future persistence.
                while (state.Cohorts.Count > 1 && state.Cohorts[1].Since + persistenceTicks <= now)
                {
                    state.Cohorts[1].Quantity += state.Cohorts[0].Quantity;
                    state.Cohorts.RemoveAt(0);
                }
                // If capacity is exceeded, conservatively postpone the oldest cohort to the
                // next cohort's timestamp. This can understate persistence, never invent age.
                while (state.Cohorts.Count > maxCohorts)
                {
                    state.Cohorts[1].Quantity += state.Cohorts[0].Quantity;
                    state.Cohorts.RemoveAt(0);
                }
            }
            List<LevelKey> removedKeys = new List<LevelKey>();
            foreach (LevelKey key in levels.Keys)
                if (key.Side == side && !present.Contains(key.Tick)) removedKeys.Add(key);
            foreach (LevelKey key in removedKeys) levels.Remove(key);
        }

        private void InvalidateSide(BookSide side, string reason)
        {
            ClosePartialFrame();
            List<LevelKey> removed = new List<LevelKey>();
            foreach (LevelKey key in levels.Keys) if (key.Side == side) removed.Add(key);
            foreach (LevelKey key in removed) levels.Remove(key);
            if (side == BookSide.Bid) { bids.Clear(); bidTailUnknown = false; }
            else { asks.Clear(); askTailUnknown = false; }
            epoch = epoch == int.MaxValue ? 0 : epoch + 1;
            Invalid(reason);
        }

        private void Trim(long now)
        {
            long cutoff = now - Math.Min(now, retentionTicks);
            while (frames.Count > 0 && frames.Peek().ToTicks <= cutoff)
            { frames.Dequeue(); droppedFrames = Increment(droppedFrames); }
            while (trades.Count > maxTrades || (trades.Count > 0 && trades.Peek().TimeTicks < cutoff))
            { trades.Dequeue(); droppedTrades = Increment(droppedTrades); }
        }

        private void SetLiveStatus()
        {
            long? bestBid = BestPositiveTick(bids), bestAsk = BestPositiveTick(asks);
            if (!bestBid.HasValue && !bestAsk.HasValue) status = "Waiting for depth";
            else if (!bestBid.HasValue || !bestAsk.HasValue) status = "Receiving one-sided depth";
            else if (bestBid.Value >= bestAsk.Value) status = "Locked/crossed depth; waiting for next update";
            else status = "Receiving depth";
            if (bidTailUnknown || askTailUnknown) status += "; deeper rows are unobserved";
        }

        private void CountDepthEvent()
        {
            depthEvents = Increment(depthEvents);
            version = Increment(version);
            SetLiveStatus();
        }

        private void Invalid(string message)
        {
            invalidEvents = Increment(invalidEvents); version = Increment(version); status = message;
        }

        private bool TryPrice(double price, out long tick)
        {
            try { tick = PriceToTick(price, tickSize); return true; }
            catch (ArgumentOutOfRangeException) { tick = 0; return false; }
        }

        private static bool Ordered(List<DepthLevel> ladder, BookSide side)
        {
            for (int i = 1; i < ladder.Count; i++)
                if (side == BookSide.Bid ? ladder[i - 1].Tick <= ladder[i].Tick : ladder[i - 1].Tick >= ladder[i].Tick)
                    return false;
            return true;
        }
        private static long? BestPositiveTick(List<DepthLevel> ladder)
        {
            foreach (DepthLevel row in ladder) if (row.Quantity > 0) return row.Tick;
            return null;
        }
        private static HeatFrame CopyFrame(HeatFrame frame)
        {
            HeatCell[] cells = new HeatCell[frame.Cells.Length];
            Array.Copy(frame.Cells, cells, cells.Length);
            return new HeatFrame(frame.FromTicks, frame.ToTicks, frame.Epoch, cells, frame.BestBidTick, frame.BestAskTick);
        }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static long Increment(long value) { return value == long.MaxValue ? value : value + 1; }
        private static long AddCount(long value, long added) { return value > long.MaxValue - added ? long.MaxValue : value + added; }

        private struct LevelKey : IEquatable<LevelKey>
        {
            public readonly BookSide Side;
            public readonly long Tick;
            public LevelKey(BookSide side, long tick) { Side = side; Tick = tick; }
            public bool Equals(LevelKey other) { return Side == other.Side && Tick == other.Tick; }
            public override bool Equals(object obj) { return obj is LevelKey && Equals((LevelKey)obj); }
            public override int GetHashCode() { return unchecked((Tick.GetHashCode() * 397) ^ (int)Side); }
        }
        private sealed class Cohort
        {
            public readonly long Since;
            public long Quantity;
            public Cohort(long since, long quantity) { Since = since; Quantity = quantity; }
        }
        private sealed class LevelState
        {
            public long Quantity;
            public readonly List<Cohort> Cohorts = new List<Cohort>();
        }
        private sealed class Integral { public double Raw, Persistent; }
        private sealed class ProfileCounter { public long Buy, Sell, Unknown; }
    }
}
