using System;

namespace FootprintIndicator.Core
{
    /// <summary>
    /// Captured consecutive volume at the latest execution price, followed by
    /// the last individual print. This is not an iceberg total or order count.
    /// A changed price, reset epoch or session ends the run.
    /// </summary>
    public sealed class LastExecutionSummary
    {
        public bool HasTrade, PartialRun;
        public long PriceTick, RunVolume, LastVolume;
        public TradeSide Side;

        public static LastExecutionSummary Build(EngineSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            var result = new LastExecutionSummary();
            TradeRecord[] trades = snapshot.Trades ?? new TradeRecord[0];
            if (trades.Length > 1000000) throw new InvalidOperationException("Execution readout input budget exceeded.");
            if (trades.Length == 0) return result;
            TradeRecord last = trades[trades.Length - 1];
            if (last == null || last.Volume <= 0) throw new ArgumentException("Invalid last execution.", "snapshot");
            if (last.TimeTicks < snapshot.SessionStartTicks) return result;
            result.HasTrade = true; result.PriceTick = last.PriceTick;
            result.LastVolume = last.Volume; result.Side = last.Side;
            int i = trades.Length - 1;
            for (; i >= 0; i--)
            {
                TradeRecord trade = trades[i];
                if (trade == null || trade.Volume <= 0) throw new ArgumentException("Invalid execution in price run.", "snapshot");
                if (trade.PriceTick != last.PriceTick || trade.Epoch != last.Epoch || trade.TimeTicks < snapshot.SessionStartTicks) break;
                result.RunVolume = checked(result.RunVolume + trade.Volume);
            }
            result.PartialRun = i < 0 && snapshot.DroppedTrades > 0;
            return result;
        }
    }

    /// <summary>A detached description of a rendered execution cluster.</summary>
    public sealed class HoverBubbleInfo
    {
        public readonly double X, Y, Radius, PriceTick;
        public readonly long TimeTicks, FromTicks, ToTicks, MinPriceTick, MaxPriceTick, Buy, Sell, Unknown;
        public readonly bool HasPriceRange;
        public long Total { get { return checked(Buy + Sell + Unknown); } }
        public long Delta { get { return Buy - Sell; } }

        internal HoverBubbleInfo(Bubble bubble)
        {
            X = bubble.X; Y = bubble.Y; Radius = bubble.Radius; PriceTick = bubble.PriceTick;
            TimeTicks = bubble.TimeTicks; FromTicks = bubble.FromTicks; ToTicks = bubble.ToTicks;
            MinPriceTick = bubble.MinPriceTick; MaxPriceTick = bubble.MaxPriceTick;
            HasPriceRange = MinPriceTick <= PriceTick && MaxPriceTick >= PriceTick;
            Buy = bubble.Buy; Sell = bubble.Sell; Unknown = bubble.Unknown;
        }
    }

    /// <summary>
    /// Captured depth at one time and price cell. Raw and persistent quantities
    /// are time-weighted frame samples in instrument quantity units (contracts
    /// for futures), not individual-order counts or an exact intraframe queue.
    /// HasObservation is false in missing history, including disconnect gaps.
    /// HasLevel distinguishes a captured cell from an unrecorded price; an absent
    /// level must not be described as proof that the entire book was empty.
    /// DisplayQuantity is independently aggregated and filtered display heat.
    /// </summary>
    public sealed class HoverLiquidityInfo
    {
        public readonly bool HasObservation, HasLevel, HasDisplayTile;
        public readonly long TimeTicks, PriceTick, FromTicks, ToTicks;
        public readonly long? BestBidTick, BestAskTick;
        public readonly int Epoch;
        public readonly double RawBid, RawAsk, PersistentBid, PersistentAsk, DisplayQuantity;
        public double RawTotal { get { return RawBid + RawAsk; } }
        public double PersistentTotal { get { return PersistentBid + PersistentAsk; } }
        public double FrameMilliseconds { get { return (ToTicks - FromTicks) / (double)TimeSpan.TicksPerMillisecond; } }

        internal HoverLiquidityInfo(long time, long price, HeatFrame frame, bool hasLevel,
            double rawBid, double rawAsk, double persistentBid, double persistentAsk, HeatTile tile)
        {
            TimeTicks = time; PriceTick = price; HasObservation = frame != null; HasLevel = hasLevel;
            FromTicks = frame == null ? 0 : frame.FromTicks;
            ToTicks = frame == null ? 0 : frame.ToTicks;
            Epoch = frame == null ? 0 : frame.Epoch;
            BestBidTick = frame == null ? null : frame.BestBidTick;
            BestAskTick = frame == null ? null : frame.BestAskTick;
            RawBid = rawBid; RawAsk = rawAsk; PersistentBid = persistentBid; PersistentAsk = persistentAsk;
            HasDisplayTile = tile != null; DisplayQuantity = tile == null ? 0 : tile.Quantity;
        }
    }

    public sealed class HoverInspectionResult
    {
        public readonly double X, Y;
        public readonly long TimeTicks, PriceTick;
        public readonly HoverBubbleInfo Bubble;
        public readonly HoverLiquidityInfo Liquidity;

        internal HoverInspectionResult(double x, double y, long time, long price, HoverBubbleInfo bubble, HoverLiquidityInfo liquidity)
        { X = x; Y = y; TimeTicks = time; PriceTick = price; Bubble = bubble; Liquidity = liquidity; }
    }

    /// <summary>
    /// Read-only, bounded hit testing in viewport-local pixels. The caller must
    /// supply the same scene, geometry and bubble limit used for painting.
    /// </summary>
    public static class HoverInspection
    {
        private const int MaximumBubbles = 1000000;
        private const int MaximumFrames = 100000;
        private const int MaximumFrameCells = 4000000;
        private const int MaximumHeatTiles = 500000;

        /// <summary>
        /// Returns null outside [0,Width) x [0,Height). Frames use half-open
        /// [FromTicks,ToTicks) intervals and price cells [tick-.5,tick+.5).
        /// Among containing circles, the nearest center wins; equal distances
        /// choose the last painted circle. Zero-radius bubbles cannot be hit.
        /// </summary>
        public static HoverInspectionResult Inspect(EngineSnapshot snapshot, RenderScene scene, Viewport view,
            double x, double y, int maximumVisibleBubbles = int.MaxValue)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            if (scene == null) throw new ArgumentNullException("scene");
            ValidateView(view);
            if (!Finite(x) || !Finite(y)) throw new ArgumentOutOfRangeException("x", "Pointer coordinates must be finite.");
            if (maximumVisibleBubbles < 0) throw new ArgumentOutOfRangeException("maximumVisibleBubbles");
            if (x < 0 || y < 0 || x >= view.Width || y >= view.Height) return null;

            long duration = view.ToTicks - view.FromTicks;
            // Add the relative offset as an integer, preserving DateTime tick
            // precision that an absolute double timestamp would lose.
            long offset = Math.Min(duration - 1, (long)(x / view.Width * duration));
            long time = view.FromTicks + offset;
            double price = Math.Floor(view.MaxPriceTick - y / view.Height * (view.MaxPriceTick - view.MinPriceTick) + 0.5);
            if (price < -9223372036854775808.0 || price >= 9223372036854775808.0)
                throw new ArgumentOutOfRangeException("view", "Pointer price is outside the supported tick range.");
            long tick = (long)price;

            Bubble hit = null;
            double nearestSquared = double.PositiveInfinity;
            foreach (Bubble bubble in SelectVisibleBubbles(scene.Bubbles, maximumVisibleBubbles))
            {
                if (bubble.Radius <= 0 || bubble.Total <= 0) continue;
                double dx = x - bubble.X, dy = y - bubble.Y;
                // Bound each axis before squaring to avoid overflowing even
                // when an offscreen point has unusually large coordinates.
                if (Math.Abs(dx) > bubble.Radius || Math.Abs(dy) > bubble.Radius) continue;
                double squared = dx * dx + dy * dy;
                if (squared <= bubble.Radius * bubble.Radius && squared <= nearestSquared)
                { hit = bubble; nearestSquared = squared; }
            }

            HeatFrame frame = FindFrame(snapshot.Frames, time);
            bool hasBid = false, hasAsk = false;
            double rawBid = 0, rawAsk = 0, persistentBid = 0, persistentAsk = 0;
            if (frame != null && frame.Cells != null)
            {
                if (frame.Cells.Length > MaximumFrameCells)
                    throw new InvalidOperationException("Hover heat-cell budget exceeded.");
                foreach (HeatCell cell in frame.Cells)
                {
                    if (cell == null) continue;
                    if (!Finite(cell.RawQuantity) || !Finite(cell.PersistentQuantity) || cell.RawQuantity < 0 ||
                        cell.PersistentQuantity < 0 || cell.PersistentQuantity > cell.RawQuantity ||
                        (cell.Side != BookSide.Bid && cell.Side != BookSide.Ask))
                        throw new ArgumentException("Invalid captured heat cell.", "snapshot");
                    if (cell.Tick != tick) continue;
                    if (cell.Side == BookSide.Bid)
                    {
                        if (hasBid) throw new ArgumentException("Duplicate bid cell at the inspected price.", "snapshot");
                        hasBid = true; rawBid = cell.RawQuantity; persistentBid = cell.PersistentQuantity;
                    }
                    else
                    {
                        if (hasAsk) throw new ArgumentException("Duplicate ask cell at the inspected price.", "snapshot");
                        hasAsk = true; rawAsk = cell.RawQuantity; persistentAsk = cell.PersistentQuantity;
                    }
                }
            }
            if (!Finite(rawBid + rawAsk) || !Finite(persistentBid + persistentAsk))
                throw new OverflowException("Inspected liquidity quantity overflowed.");
            HeatTile tile = FindDisplayTile(scene.Heat, x, y);
            return new HoverInspectionResult(x, y, time, tick, hit == null ? null : new HoverBubbleInfo(hit),
                new HoverLiquidityInfo(time, tick, frame, hasBid || hasAsk, rawBid, rawAsk, persistentBid, persistentAsk, tile));
        }

        /// <summary>
        /// Returns a new array in paint order: largest radius first, original
        /// scene index as the stable tie-breaker, retaining at most the requested
        /// largest N bubbles. Shares bubble objects but never changes them.
        /// The cap applies before zero-radius/zero-volume circles are skipped.
        /// </summary>
        public static Bubble[] SelectVisibleBubbles(Bubble[] bubbles, int maximumVisibleBubbles = int.MaxValue)
        {
            if (maximumVisibleBubbles < 0) throw new ArgumentOutOfRangeException("maximumVisibleBubbles");
            if (bubbles == null || bubbles.Length == 0 || maximumVisibleBubbles == 0) return new Bubble[0];
            if (bubbles.Length > MaximumBubbles) throw new InvalidOperationException("Hover bubble budget exceeded.");
            int[] order = new int[bubbles.Length];
            for (int i = 0; i < bubbles.Length; i++)
            {
                Bubble bubble = bubbles[i];
                if (bubble == null || !Finite(bubble.X) || !Finite(bubble.Y) || !Finite(bubble.Radius) ||
                    bubble.Radius < 0 || bubble.Radius > 4096 || !Finite(bubble.PriceTick) ||
                    bubble.Buy < 0 || bubble.Sell < 0 || bubble.Unknown < 0 ||
                    bubble.FromTicks < 0 || bubble.ToTicks < bubble.FromTicks || bubble.ToTicks > DateTime.MaxValue.Ticks ||
                    bubble.TimeTicks < bubble.FromTicks || bubble.TimeTicks > bubble.ToTicks)
                    throw new ArgumentException("Invalid rendered bubble.", "bubbles");
                checked { long total = bubble.Buy + bubble.Sell + bubble.Unknown; if (total < 0) throw new OverflowException(); }
                order[i] = i;
            }
            Array.Sort(order, delegate(int a, int b)
            {
                int comparison = bubbles[b].Radius.CompareTo(bubbles[a].Radius);
                return comparison != 0 ? comparison : a.CompareTo(b);
            });
            Bubble[] selected = new Bubble[Math.Min(bubbles.Length, maximumVisibleBubbles)];
            for (int i = 0; i < selected.Length; i++) selected[i] = bubbles[order[i]];
            return selected;
        }

        private static HeatFrame FindFrame(HeatFrame[] frames, long time)
        {
            if (frames == null) return null;
            if (frames.Length > MaximumFrames) throw new InvalidOperationException("Hover frame budget exceeded.");
            HeatFrame match = null;
            foreach (HeatFrame frame in frames)
            {
                if (frame == null) continue;
                if (frame.FromTicks < 0 || frame.ToTicks <= frame.FromTicks || frame.ToTicks > DateTime.MaxValue.Ticks)
                    throw new ArgumentException("Invalid heat-frame interval.", "snapshot");
                if (time < frame.FromTicks || time >= frame.ToTicks) continue;
                if (match != null) throw new ArgumentException("Heat frames overlap at the inspected time.", "snapshot");
                match = frame;
            }
            return match;
        }

        private static HeatTile FindDisplayTile(HeatTile[] tiles, double x, double y)
        {
            if (tiles == null) return null;
            if (tiles.Length > MaximumHeatTiles) throw new InvalidOperationException("Hover display-tile budget exceeded.");
            HeatTile match = null;
            foreach (HeatTile tile in tiles)
            {
                if (tile == null) continue;
                if (!Finite(tile.X) || !Finite(tile.Y) || !Finite(tile.Width) || !Finite(tile.Height) ||
                    tile.Width < 0 || tile.Height < 0 || !Finite(tile.Quantity) || tile.Quantity < 0)
                    throw new ArgumentException("Invalid rendered heat tile.", "scene");
                if (x >= tile.X && x < (double)tile.X + tile.Width && y >= tile.Y && y < (double)tile.Y + tile.Height)
                    match = tile;
            }
            return match;
        }

        private static void ValidateView(Viewport view)
        {
            if (view == null) throw new ArgumentNullException("view");
            if (view.FromTicks < 0 || view.ToTicks <= view.FromTicks || view.ToTicks > DateTime.MaxValue.Ticks ||
                !Finite(view.MinPriceTick) || !Finite(view.MaxPriceTick) || view.MaxPriceTick <= view.MinPriceTick ||
                !Finite(view.MaxPriceTick - view.MinPriceTick) || view.Width < 1 || view.Height < 1 ||
                view.Width > 32768 || view.Height > 32768)
                throw new ArgumentOutOfRangeException("view", "Invalid viewport time, price range or dimensions.");
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
