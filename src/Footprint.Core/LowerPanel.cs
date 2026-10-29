using System;
using System.Collections.Generic;

namespace FootprintIndicator.Core
{
    /// <summary>A fixed event-time volume bucket. Unknown volume remains independently observable.</summary>
    public sealed class LowerPanelVolumeBar
    {
        public readonly long FromTicks, ToTicks, Buy, Sell, Unknown, Total;
        internal LowerPanelVolumeBar(long from, long to, long buy, long sell, long unknown)
        {
            FromTicks = from; ToTicks = to; Buy = buy; Sell = sell; Unknown = unknown;
            Total = checked(buy + sell + unknown);
        }
    }

    /// <summary>A captured session delta at its actual event time; epochs identify continuity breaks.</summary>
    public sealed class LowerPanelCvdPoint
    {
        public readonly long TimeTicks, CumulativeDelta, Sequence;
        public readonly int Epoch;
        internal LowerPanelCvdPoint(TradeRecord trade)
        {
            TimeTicks = trade.TimeTicks; CumulativeDelta = trade.CumulativeDelta;
            Sequence = trade.Sequence; Epoch = trade.Epoch;
        }
    }

    public sealed class LowerPanelResult
    {
        public readonly LowerPanelVolumeBar[] VolumeBars;
        public readonly LowerPanelCvdPoint[] CvdPoints;
        public readonly long MinDelta, MaxDelta, MaxVolume;
        internal LowerPanelResult(LowerPanelVolumeBar[] bars, LowerPanelCvdPoint[] points,
            long minDelta, long maxDelta, long maxVolume)
        {
            VolumeBars = bars; CvdPoints = points;
            MinDelta = minDelta; MaxDelta = maxDelta; MaxVolume = maxVolume;
        }
    }

    /// <summary>
    /// Converts captured executions into stable lower-panel data independently of chart width.
    /// The left edge includes its full intersecting volume bucket. The right edge is a causal
    /// cutoff: executions at or after it are excluded, even when the snapshot has later data.
    /// CVD uses captured values only and includes at most one real predecessor before the view.
    /// </summary>
    public static class LowerPanel
    {
        private const int MaximumInputTrades = 1000000;

        private sealed class Counter
        {
            internal long Buy, Sell, Unknown;
            internal void Add(TradeRecord trade)
            {
                checked
                {
                    if (trade.Side == TradeSide.Buy) Buy += trade.Volume;
                    else if (trade.Side == TradeSide.Sell) Sell += trade.Volume;
                    else Unknown += trade.Volume;
                    // Validate the combined total as well as each individual side.
                    long total = Buy + Sell + Unknown;
                    if (total < 0) throw new OverflowException();
                }
            }
        }

        public static LowerPanelResult Build(EngineSnapshot snapshot, long fromTicks, long toTicks, int bucketMilliseconds)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            if (fromTicks < 0 || toTicks > DateTime.MaxValue.Ticks || fromTicks >= toTicks)
                throw new ArgumentOutOfRangeException("fromTicks", "A valid half-open time range is required.");
            if (bucketMilliseconds < 1 || bucketMilliseconds > 86400000)
                throw new ArgumentOutOfRangeException("bucketMilliseconds", "Volume buckets must be between one millisecond and one day.");
            if (snapshot.SessionStartTicks < 0 || snapshot.SessionStartTicks > DateTime.MaxValue.Ticks)
                throw new ArgumentException("Invalid session start.", "snapshot");
            TradeRecord[] trades = snapshot.Trades ?? new TradeRecord[0];
            if (trades.Length > MaximumInputTrades)
                throw new InvalidOperationException("Lower-panel trade input budget exceeded.");

            long bucketTicks = checked((long)bucketMilliseconds * TimeSpan.TicksPerMillisecond);
            long firstBucket = fromTicks - fromTicks % bucketTicks;
            var counters = new SortedDictionary<long, Counter>();
            var cvd = new List<TradeRecord>();
            TradeRecord predecessor = null;
            foreach (TradeRecord trade in trades)
            {
                if (trade == null || trade.TimeTicks < 0 || trade.TimeTicks > DateTime.MaxValue.Ticks ||
                    trade.Volume <= 0 || trade.Epoch < 0 ||
                    (trade.Side != TradeSide.Buy && trade.Side != TradeSide.Sell && trade.Side != TradeSide.Unknown))
                    throw new ArgumentException("Invalid retained execution.", "snapshot");
                if (trade.TimeTicks >= toTicks) continue;

                if (trade.TimeTicks >= firstBucket)
                {
                    long bucket = trade.TimeTicks - trade.TimeTicks % bucketTicks;
                    Counter counter;
                    if (!counters.TryGetValue(bucket, out counter))
                    {
                        counter = new Counter(); counters.Add(bucket, counter);
                    }
                    counter.Add(trade);
                }
                // Retained records from earlier sessions have different delta origins.
                if (trade.TimeTicks < snapshot.SessionStartTicks) continue;
                if (trade.TimeTicks >= fromTicks) cvd.Add(trade);
                else if (predecessor == null || CompareTrades(predecessor, trade) < 0) predecessor = trade;
            }

            var bars = new LowerPanelVolumeBar[counters.Count];
            long maximumVolume = 0;
            int index = 0;
            foreach (KeyValuePair<long, Counter> pair in counters)
            {
                Counter counter = pair.Value;
                var bar = new LowerPanelVolumeBar(pair.Key, Math.Min(DateTime.MaxValue.Ticks, checked(pair.Key + bucketTicks)),
                    counter.Buy, counter.Sell, counter.Unknown);
                bars[index++] = bar;
                maximumVolume = Math.Max(maximumVolume, bar.Total);
            }
            if (predecessor != null) cvd.Add(predecessor);
            cvd.Sort(CompareTrades);
            var points = new LowerPanelCvdPoint[cvd.Count];
            long minDelta = 0, maxDelta = 0;
            for (int i = 0; i < cvd.Count; i++)
            {
                points[i] = new LowerPanelCvdPoint(cvd[i]);
                minDelta = Math.Min(minDelta, points[i].CumulativeDelta);
                maxDelta = Math.Max(maxDelta, points[i].CumulativeDelta);
            }
            return new LowerPanelResult(bars, points, minDelta, maxDelta, maximumVolume);
        }

        private static int CompareTrades(TradeRecord first, TradeRecord second)
        {
            int comparison = first.TimeTicks.CompareTo(second.TimeTicks);
            if (comparison != 0) return comparison;
            comparison = first.Sequence.CompareTo(second.Sequence);
            return comparison != 0 ? comparison : first.Epoch.CompareTo(second.Epoch);
        }
    }

    public sealed class LowerPanelScaleBounds
    {
        public readonly double MinDelta, MaxDelta, MaxVolume;
        internal LowerPanelScaleBounds(double minDelta, double maxDelta, double maxVolume)
        { MinDelta = minDelta; MaxDelta = maxDelta; MaxVolume = maxVolume; }
    }

    /// <summary>
    /// Expands only when an observation crosses an existing bound, with twenty percent headroom
    /// rounded to 1/2/5 steps. Panning, retention and smaller observations cannot shrink it.
    /// A session change or explicit Reset starts a fresh range. Instances belong to one renderer.
    /// </summary>
    public sealed class LowerPanelScale
    {
        private bool initialized;
        private long session;
        private double minimum = -1, maximum = 1, volumeMaximum = 1;

        public LowerPanelScaleBounds Update(long sessionKey, long minDelta, long maxDelta, long maxVolume)
        {
            if (minDelta > maxDelta) throw new ArgumentException("Delta bounds are reversed.");
            if (maxVolume < 0) throw new ArgumentOutOfRangeException("maxVolume");
            if (!initialized || sessionKey != session)
            {
                Reset(); session = sessionKey; initialized = true;
            }
            if (minDelta < minimum) minimum = -RoundUp(-(double)minDelta * 1.2);
            if (maxDelta > maximum) maximum = RoundUp((double)maxDelta * 1.2);
            if (maxVolume > volumeMaximum) volumeMaximum = RoundUp((double)maxVolume * 1.2);
            return new LowerPanelScaleBounds(minimum, maximum, volumeMaximum);
        }

        public void Reset()
        {
            initialized = false; session = 0;
            minimum = -1; maximum = 1; volumeMaximum = 1;
        }

        private static double RoundUp(double value)
        {
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
            double fraction = value / magnitude;
            double rounded = fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10;
            return rounded * magnitude;
        }
    }
}
