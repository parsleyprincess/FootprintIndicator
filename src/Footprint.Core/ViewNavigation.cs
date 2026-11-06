using System;
using System.Collections.Generic;

namespace FootprintIndicator.Core
{
    /// <summary>A display interval, not a claim that market data exists throughout it.</summary>
    public sealed class TimeRange
    {
        public readonly long FromTicks, ToTicks;
        public long SpanTicks { get { return ToTicks - FromTicks; } }
        public TimeRange(long fromTicks, long toTicks)
        {
            if (fromTicks < 0 || toTicks <= fromTicks || toTicks > DateTime.MaxValue.Ticks)
                throw new ArgumentOutOfRangeException("fromTicks", "A valid increasing DateTime interval is required.");
            FromTicks = fromTicks; ToTicks = toTicks;
        }
    }

    /// <summary>
    /// View navigation only. Paused history stores absolute bounds, so receiving
    /// more market data cannot move it. Optional smooth time adds a bounded visual
    /// lead; it never advances the market engine, extends heat frames or creates trades.
    /// Call on the chart/UI thread or under the adapter's render lock.
    /// </summary>
    public sealed class TimeNavigation
    {
        public const double MinimumWindowSeconds = 0.001;
        public const double MaximumWindowSeconds = 86400;
        private const long MinimumWindowTicks = TimeSpan.TicksPerMillisecond;
        private const long MaximumWindowTicks = TimeSpan.TicksPerDay;
        private long windowTicks, lastObservedTicks, lastLiveEnd;
        private bool hasLiveEnd;
        private TimeRange lastRange;
        public bool IsLive { get; private set; }
        public bool HasRange { get { return lastRange != null; } }
        public long WindowTicks { get { return windowTicks; } }
        public TimeRange LastRange { get { return lastRange; } }
        public TimeRange FrozenRange { get { return IsLive ? null : lastRange; } }

        public TimeNavigation(double windowSeconds = 120)
        {
            IsLive = true;
            SetWindowSeconds(windowSeconds);
        }

        public void SetWindowSeconds(double seconds)
        {
            if (!Finite(seconds) || seconds < MinimumWindowSeconds || seconds > MaximumWindowSeconds)
                throw new ArgumentOutOfRangeException("seconds");
            windowTicks = (long)Math.Round(seconds * TimeSpan.TicksPerSecond);
            if (lastRange != null) lastRange = FitEnd(lastRange.ToTicks, windowTicks);
            hasLiveEnd = false;
        }

        public TimeRange Range(long observedTicks, double elapsedSinceEventMilliseconds = 0,
            bool smooth = false, double maximumLeadMilliseconds = 500)
        {
            if (observedTicks < 0 || observedTicks > DateTime.MaxValue.Ticks)
                throw new ArgumentOutOfRangeException("observedTicks");
            if (!Finite(elapsedSinceEventMilliseconds) || elapsedSinceEventMilliseconds < 0 ||
                !Finite(maximumLeadMilliseconds) || maximumLeadMilliseconds < 0 || maximumLeadMilliseconds > 60000)
                throw new ArgumentOutOfRangeException("elapsedSinceEventMilliseconds");
            if (!IsLive && lastRange != null) return lastRange;

            long lead = smooth ? (long)Math.Round(Math.Min(elapsedSinceEventMilliseconds,
                maximumLeadMilliseconds) * TimeSpan.TicksPerMillisecond) : 0;
            // Keep the current observation a little left of the book. This
            // empty display padding is never an observed liquidity interval.
            long padding = Math.Max(1, windowTicks / 25);
            long end = AddBounded(AddBounded(observedTicks, lead), padding);
            // New timestamps can arrive behind the previous bounded prediction.
            // Do not make the visual clock reverse between normal live events.
            // An actual replay rewind deliberately starts a new visual timeline.
            if (smooth && hasLiveEnd && observedTicks >= lastObservedTicks)
            {
                long maximumLead = (long)Math.Round(maximumLeadMilliseconds * TimeSpan.TicksPerMillisecond);
                long latestAllowed = AddBounded(AddBounded(observedTicks, maximumLead), padding);
                end = Math.Min(latestAllowed, Math.Max(end, lastLiveEnd));
            }
            lastObservedTicks = observedTicks;
            lastRange = FitEnd(end, windowTicks);
            lastLiveEnd = lastRange.ToTicks;
            hasLiveEnd = smooth;
            return lastRange;
        }

        public void Pause()
        {
            // Before the first observation, remember the pause intent without
            // inventing a time range. The first Range call captures it once.
            IsLive = false;
        }

        public void ResumeLive()
        {
            IsLive = true;
            hasLiveEnd = false;
        }

        public void SetFrozenRange(long fromTicks, long toTicks)
        {
            var range = new TimeRange(fromTicks, toTicks);
            if (range.SpanTicks < MinimumWindowTicks || range.SpanTicks > MaximumWindowTicks)
                throw new ArgumentOutOfRangeException("toTicks", "The supported time window is one millisecond through one day.");
            windowTicks = range.SpanTicks;
            lastRange = range;
            IsLive = false;
            hasLiveEnd = false;
        }

        /// <summary>Positive deltas move toward later times. Any pan freezes the display.</summary>
        public void PanSeconds(double seconds)
        {
            if (!Finite(seconds) || Math.Abs(seconds) > DateTime.MaxValue.Ticks / (double)TimeSpan.TicksPerSecond)
                throw new ArgumentOutOfRangeException("seconds");
            double scaled = seconds * TimeSpan.TicksPerSecond;
            // The supported DateTime magnitude is safely below Int64.MaxValue.
            PanTicks((long)Math.Round(scaled));
        }

        public void PanTicks(long ticks)
        {
            if (lastRange == null) return;
            long from = AddBounded(lastRange.FromTicks, ticks);
            from = Math.Min(from, DateTime.MaxValue.Ticks - windowTicks);
            lastRange = new TimeRange(from, from + windowTicks);
            IsLive = false;
            hasLiveEnd = false;
        }

        /// <summary>
        /// Changes scale around the pointer's [0,1] left-to-right fraction and
        /// freezes the resulting absolute interval. ResumeLive is explicit.
        /// A factor below one zooms in. Pointer fractions clamp at the plot edges;
        /// time endpoints clamp only at DateTime limits.
        /// </summary>
        public void Zoom(double factor, double anchorFraction)
        {
            if (!Finite(factor) || factor <= 0) throw new ArgumentOutOfRangeException("factor");
            if (!Finite(anchorFraction)) throw new ArgumentOutOfRangeException("anchorFraction");
            anchorFraction = Math.Max(0, Math.Min(1, anchorFraction));
            long nextSpan = (long)Math.Round(Math.Max(MinimumWindowTicks,
                Math.Min(MaximumWindowTicks, windowTicks * factor)));
            if (lastRange == null) { windowTicks = nextSpan; return; }
            // Work in relative integer ticks. Converting an absolute DateTime to
            // double would lose precision in a very close zoom.
            long anchor = lastRange.FromTicks + (long)Math.Round(windowTicks * anchorFraction);
            long before = (long)Math.Round(nextSpan * anchorFraction);
            long from = Math.Max(0, Math.Min(DateTime.MaxValue.Ticks - nextSpan, anchor - before));
            windowTicks = nextSpan;
            lastRange = new TimeRange(from, from + nextSpan);
            IsLive = false;
            hasLiveEnd = false;
        }

        private static TimeRange FitEnd(long end, long span)
        {
            end = Math.Max(span, Math.Min(DateTime.MaxValue.Ticks, end));
            return new TimeRange(end - span, end);
        }

        private static long AddBounded(long ticks, long delta)
        {
            if (delta > DateTime.MaxValue.Ticks - ticks) return DateTime.MaxValue.Ticks;
            if (delta < -ticks) return 0;
            return ticks + delta;
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }

    public sealed class TimeGridResult
    {
        public readonly long IntervalTicks;
        public readonly long[] Ticks;
        internal TimeGridResult(long interval, long[] ticks) { IntervalTicks = interval; Ticks = ticks; }
    }

    /// <summary>Absolute, legible intervals that stay attached to clock time while scrolling.</summary>
    public static class NiceTimeGrid
    {
        public const int MaximumLabels = 10000;
        private static readonly long[] Intervals =
        {
            10000, 20000, 50000, 100000, 200000, 500000, 1000000, 2000000, 5000000,
            TimeSpan.TicksPerSecond, 2L * TimeSpan.TicksPerSecond, 5L * TimeSpan.TicksPerSecond,
            10L * TimeSpan.TicksPerSecond, 15L * TimeSpan.TicksPerSecond, 20L * TimeSpan.TicksPerSecond,
            30L * TimeSpan.TicksPerSecond, TimeSpan.TicksPerMinute, 2L * TimeSpan.TicksPerMinute,
            5L * TimeSpan.TicksPerMinute, 10L * TimeSpan.TicksPerMinute, 15L * TimeSpan.TicksPerMinute,
            20L * TimeSpan.TicksPerMinute, 30L * TimeSpan.TicksPerMinute, TimeSpan.TicksPerHour,
            2L * TimeSpan.TicksPerHour, 3L * TimeSpan.TicksPerHour, 6L * TimeSpan.TicksPerHour,
            12L * TimeSpan.TicksPerHour, TimeSpan.TicksPerDay, 2L * TimeSpan.TicksPerDay,
            5L * TimeSpan.TicksPerDay, 10L * TimeSpan.TicksPerDay, 30L * TimeSpan.TicksPerDay,
            90L * TimeSpan.TicksPerDay, 365L * TimeSpan.TicksPerDay
        };

        public static TimeGridResult Build(long fromTicks, long toTicks, double width, double minLabelPixels = 100)
        {
            var range = new TimeRange(fromTicks, toTicks);
            if (!Finite(width) || width <= 0 || width > 1000000 || !Finite(minLabelPixels) || minLabelPixels < 1)
                throw new ArgumentOutOfRangeException("width");
            double target = range.SpanTicks * Math.Min(1, Math.Max(1d / MaximumLabels, minLabelPixels / width));
            long interval = Intervals[Intervals.Length - 1];
            foreach (long candidate in Intervals)
                if (candidate >= target) { interval = candidate; break; }
            // Very broad caller-supplied ranges still get a bounded grid.
            if (interval < target) interval = (long)Math.Ceiling(target);
            long remainder = fromTicks % interval;
            long first = fromTicks;
            if (remainder != 0)
            {
                long advance = interval - remainder;
                if (advance > DateTime.MaxValue.Ticks - first) return new TimeGridResult(interval, new long[0]);
                first += advance;
            }
            var ticks = new List<long>();
            for (long tick = first; tick < toTicks;)
            {
                if (ticks.Count >= MaximumLabels) break;
                ticks.Add(tick);
                if (interval > DateTime.MaxValue.Ticks - tick) break;
                tick += interval;
            }
            return new TimeGridResult(interval, ticks.ToArray());
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }

    /// <summary>
    /// Tick-index price range over an explicit usable plot rectangle. Extrapolation
    /// is supported so an adapter can derive full-panel bounds while keeping the
    /// current market centered below its header and above lower-panel studies.
    /// </summary>
    public sealed class PriceRange
    {
        public readonly double MinTick, MaxTick;
        public double SpanTicks { get { return MaxTick - MinTick; } }
        public double CenterTick { get { return MinTick + SpanTicks / 2; } }
        public PriceRange(double minTick, double maxTick)
        {
            if (!Finite(minTick) || !Finite(maxTick) || maxTick <= minTick || !Finite(maxTick - minTick))
                throw new ArgumentOutOfRangeException("minTick");
            MinTick = minTick; MaxTick = maxTick;
        }

        public double GetY(double tick, double usableTop, double usableBottom)
        {
            ValidateMapping(tick, usableTop, usableBottom);
            double result = usableTop + (MaxTick - tick) / SpanTicks * (usableBottom - usableTop);
            if (!Finite(result)) throw new OverflowException("Price-to-pixel mapping overflowed.");
            return result;
        }

        public double GetTick(double y, double usableTop, double usableBottom)
        {
            ValidateMapping(y, usableTop, usableBottom);
            double result = MaxTick - (y - usableTop) / (usableBottom - usableTop) * SpanTicks;
            if (!Finite(result)) throw new OverflowException("Pixel-to-price mapping overflowed.");
            return result;
        }

        private static void ValidateMapping(double value, double top, double bottom)
        {
            if (!Finite(value) || !Finite(top) || !Finite(bottom) || bottom <= top || !Finite(bottom - top))
                throw new ArgumentOutOfRangeException("usableTop");
        }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }

    /// <summary>Price pan/zoom in instrument tick indices, independent of host candle ranges.</summary>
    public sealed class PriceNavigation
    {
        public const double MinimumSpanTicks = 2;
        public const double MaximumSpanTicks = 1000000000;
        private PriceRange range;
        public PriceRange Range { get { return range; } }

        public PriceNavigation(double centerTick, double spanTicks)
        {
            range = Around(centerTick, spanTicks);
        }

        public void CenterOn(double centerTick) { range = Around(centerTick, range.SpanTicks); }
        public void SetSpanTicks(double spanTicks) { range = Around(range.CenterTick, spanTicks); }
        public void PanTicks(double delta) { range = Around(range.CenterTick + delta, range.SpanTicks); }

        /// <summary>Preserves the price at a top-to-bottom pointer fraction, clamped to [0,1].</summary>
        public void Zoom(double factor, double anchorFromTop)
        {
            if (!Finite(factor) || factor <= 0) throw new ArgumentOutOfRangeException("factor");
            if (!Finite(anchorFromTop)) throw new ArgumentOutOfRangeException("anchorFromTop");
            anchorFromTop = Math.Max(0, Math.Min(1, anchorFromTop));
            double span = Math.Max(MinimumSpanTicks, Math.Min(MaximumSpanTicks, range.SpanTicks * factor));
            double anchor = range.MaxTick - range.SpanTicks * anchorFromTop;
            double center = anchor + span * (anchorFromTop - 0.5);
            range = Around(center, span);
        }

        private static PriceRange Around(double center, double span)
        {
            if (!Finite(center) || !Finite(span) || span < MinimumSpanTicks || span > MaximumSpanTicks)
                throw new ArgumentOutOfRangeException("span");
            return new PriceRange(center - span / 2, center + span / 2);
        }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
