using System;
using System.Collections.Generic;

namespace FootprintIndicator.Core
{
    /// <summary>
    /// Optional, original market-by-price display heuristic. A large quantity that
    /// repeatedly occupies the same distance from the best price is considered a
    /// following pattern. This does not identify orders, bots, or trader intent.
    /// It is not Bookmap's proprietary TraderMap Pro filter.
    /// </summary>
    public static class LiquidityNoiseFilter
    {
        private const int MaximumOffsetsPerSide = 4096;
        private const long MaximumCellVisits = 12000000;

        private sealed class Observation
        {
            internal long BestTick, TimeTicks;
            internal double Minimum, Maximum;
        }

        private sealed class Trajectory
        {
            internal readonly List<Observation> Points = new List<Observation>();
        }

        /// <summary>
        /// Return references to engine cells to omit from the filtered heatmap.
        /// Decisions use evidence from earlier, completed frames; the current
        /// frame is recorded only after its decision. Thus confirmation incurs
        /// one frame of latency and never backfills earlier history. An epoch,
        /// observation gap, missing offset, or loss of above-background size
        /// breaks a trajectory. Capture truncation restarts the warm-up.
        /// </summary>
        public static HashSet<HeatCell> BuildMask(HeatFrame[] frames, DisplaySettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            var mask = new HashSet<HeatCell>();
            if (!settings.SuppressFollowingLiquidity) return mask;
            Validate(settings);
            if (frames == null || frames.Length == 0) return mask;

            HeatFrame[] ordered = (HeatFrame[])frames.Clone();
            Array.Sort(ordered, delegate(HeatFrame a, HeatFrame b)
            {
                if (a == null) return b == null ? 0 : -1;
                if (b == null) return 1;
                return a.FromTicks.CompareTo(b.FromTicks);
            });
            var bid = new Dictionary<long, Trajectory>();
            var ask = new Dictionary<long, Trajectory>();
            long previousEnd = -1, visits = 0;
            int previousEpoch = 0;
            long window = (long)settings.FollowingWindowMilliseconds * TimeSpan.TicksPerMillisecond;

            foreach (HeatFrame frame in ordered)
            {
                if (frame == null) continue;
                if (frame.FromTicks < 0 || frame.ToTicks <= frame.FromTicks || frame.ToTicks > DateTime.MaxValue.Ticks)
                    throw new ArgumentException("Following filter requires valid nonempty time frames.", "frames");
                if (previousEnd >= 0 && frame.FromTicks < previousEnd)
                    throw new ArgumentException("Following filter frames overlap in time.", "frames");
                if (previousEnd < 0 || frame.FromTicks != previousEnd || frame.Epoch != previousEpoch)
                { bid.Clear(); ask.Clear(); }

                HeatCell[] cells = frame.Cells ?? new HeatCell[0];
                visits += (long)cells.Length * 2;
                if (visits > MaximumCellVisits)
                    throw new InvalidOperationException("Following filter history budget exceeded; reduce retained history.");
                ProcessSide(frame, cells, BookSide.Bid, frame.BestBidTick, bid, mask, settings, window);
                ProcessSide(frame, cells, BookSide.Ask, frame.BestAskTick, ask, mask, settings, window);
                previousEnd = frame.ToTicks;
                previousEpoch = frame.Epoch;
            }

            // Engine cells are unique to frames. A manually constructed snapshot
            // may reuse one immutable cell object across times; do not let a later
            // decision accidentally mask its earlier occurrence through identity.
            var seenMasked = new HashSet<HeatCell>();
            foreach (HeatFrame frame in ordered)
            {
                if (frame == null || frame.Cells == null) continue;
                foreach (HeatCell cell in frame.Cells)
                    if (cell != null && mask.Contains(cell) && !seenMasked.Add(cell)) mask.Remove(cell);
            }
            return mask;
        }

        private static void Validate(DisplaySettings settings)
        {
            if (settings.FollowingMinimumMoves < 1 || settings.FollowingMinimumMoves > 64 ||
                settings.FollowingWindowMilliseconds < 1 || settings.FollowingWindowMilliseconds > 3600000 ||
                !Finite(settings.FollowingSizeRatio) || settings.FollowingSizeRatio <= 1 || settings.FollowingSizeRatio > 1000000 ||
                !Finite(settings.FollowingSizeTolerance) || settings.FollowingSizeTolerance < 0 || settings.FollowingSizeTolerance >= 1)
                throw new ArgumentOutOfRangeException("settings", "Invalid following-liquidity window, move count, size ratio, or tolerance.");
        }

        private static void ProcessSide(HeatFrame frame, HeatCell[] cells, BookSide side, long? best,
            Dictionary<long, Trajectory> history, HashSet<HeatCell> mask, DisplaySettings settings, long window)
        {
            if (!best.HasValue) { history.Clear(); return; }
            var sizes = new List<double>();
            foreach (HeatCell cell in cells)
                if (cell != null && cell.Side == side && Finite(cell.RawQuantity) && cell.RawQuantity > 0)
                    sizes.Add(cell.RawQuantity);
            if (sizes.Count == 0) { history.Clear(); return; }
            sizes.Sort();
            double median = sizes.Count % 2 == 0
                ? sizes[sizes.Count / 2 - 1] * 0.5 + sizes[sizes.Count / 2] * 0.5 : sizes[sizes.Count / 2];
            double threshold = median * settings.FollowingSizeRatio;
            var observed = new HashSet<long>();
            long cutoff = Math.Max(0, frame.FromTicks - window);

            foreach (HeatCell cell in cells)
            {
                if (cell == null || cell.Side != side || !Finite(cell.RawQuantity) || cell.RawQuantity < threshold) continue;
                long offset;
                try { offset = checked(cell.Tick - best.Value); }
                catch (OverflowException) { continue; }
                // Crossed or obsolete cells from an intra-frame ladder move must
                // not be evidence for a valid same-side displayed quote.
                if ((side == BookSide.Bid && offset > 0) || (side == BookSide.Ask && offset < 0)) continue;
                if (!observed.Add(offset))
                    throw new ArgumentException("Duplicate side/price cells in following filter frame.", "frame");
                if (observed.Count > MaximumOffsetsPerSide)
                    throw new InvalidOperationException("Following filter price-level budget exceeded.");

                Trajectory trajectory;
                if (!history.TryGetValue(offset, out trajectory))
                { trajectory = new Trajectory(); history.Add(offset, trajectory); }
                List<Observation> points = trajectory.Points;
                while (points.Count > 0 && points[0].TimeTicks < cutoff) points.RemoveAt(0);

                if (points.Count >= settings.FollowingMinimumMoves + 1)
                {
                    double minimum = cell.RawQuantity, maximum = cell.RawQuantity;
                    foreach (Observation point in points)
                    { minimum = Math.Min(minimum, point.Minimum); maximum = Math.Max(maximum, point.Maximum); }
                    if (minimum >= maximum * (1 - settings.FollowingSizeTolerance)) mask.Add(cell);
                }

                // Record only after the decision. Repeated frames at the same
                // best price refresh size evidence but never count as a move.
                Observation last = points.Count == 0 ? null : points[points.Count - 1];
                if (last != null && last.BestTick == best.Value)
                {
                    last.TimeTicks = frame.ToTicks;
                    last.Minimum = Math.Min(last.Minimum, cell.RawQuantity);
                    last.Maximum = Math.Max(last.Maximum, cell.RawQuantity);
                }
                else
                {
                    points.Add(new Observation { BestTick = best.Value, TimeTicks = frame.ToTicks,
                        Minimum = cell.RawQuantity, Maximum = cell.RawQuantity });
                    while (points.Count > settings.FollowingMinimumMoves + 1) points.RemoveAt(0);
                }
            }

            // Unobserved, small, or newly out-of-range offsets lose continuity.
            var missing = new List<long>();
            foreach (long offset in history.Keys) if (!observed.Contains(offset)) missing.Add(offset);
            foreach (long offset in missing) history.Remove(offset);
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
