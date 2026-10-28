using System;
using System.Collections.Generic;

namespace FootprintIndicator.Core
{
    public enum FlowMarkerKind { Absorption, Sweep, LargePrint, LiquidityAdded, LiquidityRemoved, ReplenishmentCandidate }

    public sealed class OrderFlowSettings
    {
        public bool EnableAbsorption = true, EnableSweeps = true, EnableLargePrints = true;
        public bool EnableLiquidityChanges, EnableReplenishment;
        public long AbsorptionVolume = 100, SweepVolume = 150, LargePrintVolume = 100, ReplenishmentVolume = 50;
        public int AbsorptionWindowMilliseconds = 1000, SweepWindowMilliseconds = 500, SweepMinimumLevels = 3;
        public double LiquidityChangeQuantity = 100, ReplenishmentMinimumFraction = 0.8;
        public int MaxMarkers = 5000, MaxTrades = 1000000, MaxFrames = 100000, MaxCells = 4000000;
    }

    public sealed class FlowMarker
    {
        public long TimeTicks, PriceTick, Volume;
        // Passive side for absorption/liquidity/refill; aggressor side for sweeps/prints.
        public TradeSide Side;
        public FlowMarkerKind Kind;
        public int Levels;
        public double Quantity;
    }

    public sealed class OrderFlowResult
    {
        public FlowMarker[] Markers = new FlowMarker[0];
        public string Coverage;
        public bool Truncated;
        public int ProcessedTrades, ProcessedFrames;
    }

    /// <summary>
    /// Original causal execution studies. Absorption is fixed-price aggressive
    /// volume/time with the opposite passive-side label, not a proven iceberg.
    /// Sweeps count distinct executed prices, not identified parent market orders.
    /// Frame-based liquidity/refill markers are coarse aggregate estimates only.
    /// </summary>
    public static class OrderFlowStudies
    {
        private struct Key : IEquatable<Key>
        {
            internal long Tick;
            internal TradeSide Side;
            internal Key(long tick, TradeSide side) { Tick = tick; Side = side; }
            public bool Equals(Key other) { return Tick == other.Tick && Side == other.Side; }
            public override bool Equals(object other) { return other is Key && Equals((Key)other); }
            public override int GetHashCode() { unchecked { return Tick.GetHashCode() * 397 ^ (int)Side; } }
        }

        private sealed class Window
        {
            internal readonly Queue<TradeRecord> Trades = new Queue<TradeRecord>();
            internal readonly Dictionary<long, int> Prices = new Dictionary<long, int>();
            internal long Volume;
            internal void Expire(long cutoff)
            {
                while (Trades.Count > 0 && Trades.Peek().TimeTicks < cutoff)
                {
                    TradeRecord trade = Trades.Dequeue();
                    Volume -= trade.Volume;
                    int count = Prices[trade.PriceTick] - 1;
                    if (count == 0) Prices.Remove(trade.PriceTick); else Prices[trade.PriceTick] = count;
                }
            }
            internal void Add(TradeRecord trade)
            {
                Volume = checked(Volume + trade.Volume);
                int count; Prices.TryGetValue(trade.PriceTick, out count);
                Prices[trade.PriceTick] = checked(count + 1);
                Trades.Enqueue(trade);
            }
            internal void Clear() { Trades.Clear(); Prices.Clear(); Volume = 0; }
        }

        public static OrderFlowResult Build(EngineSnapshot snapshot, OrderFlowSettings settings)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            if (settings == null) throw new ArgumentNullException("settings");
            Validate(settings);
            TradeRecord[] trades = snapshot.Trades ?? new TradeRecord[0];
            HeatFrame[] frames = snapshot.Frames ?? new HeatFrame[0];
            if (trades.Length > settings.MaxTrades || frames.Length > settings.MaxFrames)
                throw new InvalidOperationException("Order-flow input budget exceeded; reduce retained history.");
            var result = new OrderFlowResult { Coverage = "Captured current-session executions; no parent order IDs. Liquidity/refill markers use completed depth-frame averages, not native iceberg detection." };
            if (snapshot.DroppedTrades > 0 || snapshot.DroppedFrames > 0)
                result.Coverage += " Earlier retained input was trimmed; historical study coverage is partial.";
            var markers = new List<FlowMarker>();
            var absorption = new Dictionary<Key, Window>();
            Window[] sweep = { new Window(), new Window() };
            long previousTime = -1, previousSequence = -1;
            int epoch = -1;
            foreach (TradeRecord trade in trades)
            {
                if (trade == null || trade.TimeTicks < 0 || trade.TimeTicks > snapshot.TimeTicks || trade.Volume <= 0 ||
                    trade.Epoch < 0 || (trade.Side != TradeSide.Buy && trade.Side != TradeSide.Sell && trade.Side != TradeSide.Unknown) ||
                    trade.TimeTicks < previousTime || (trade.TimeTicks == previousTime && trade.Sequence <= previousSequence))
                    throw new ArgumentException("Invalid or out-of-order study execution.", "snapshot");
                previousTime = trade.TimeTicks; previousSequence = trade.Sequence;
                if (trade.TimeTicks < snapshot.SessionStartTicks) continue;
                result.ProcessedTrades++;
                if (trade.Epoch != epoch)
                { absorption.Clear(); sweep[0].Clear(); sweep[1].Clear(); epoch = trade.Epoch; }
                if (settings.EnableLargePrints && trade.Volume >= settings.LargePrintVolume)
                    AddMarker(markers, Marker(trade, FlowMarkerKind.LargePrint, trade.Side, trade.Volume, 1), settings, result);
                if (trade.Side == TradeSide.Unknown) continue;
                if (settings.EnableAbsorption)
                {
                    var key = new Key(trade.PriceTick, trade.Side);
                    Window window;
                    if (!absorption.TryGetValue(key, out window)) { window = new Window(); absorption.Add(key, window); }
                    window.Expire(trade.TimeTicks - (long)settings.AbsorptionWindowMilliseconds * TimeSpan.TicksPerMillisecond);
                    window.Add(trade);
                    if (window.Volume >= settings.AbsorptionVolume)
                    {
                        AddMarker(markers, Marker(trade, FlowMarkerKind.Absorption, Opposite(trade.Side), window.Volume, 1), settings, result);
                        window.Clear(); // Evidence is consumed; the same prints cannot repeatedly trigger this tool.
                    }
                }
                if (settings.EnableSweeps)
                {
                    Window window = sweep[(int)trade.Side];
                    window.Expire(trade.TimeTicks - (long)settings.SweepWindowMilliseconds * TimeSpan.TicksPerMillisecond);
                    window.Add(trade);
                    if (window.Volume >= settings.SweepVolume && window.Prices.Count >= settings.SweepMinimumLevels)
                    {
                        AddMarker(markers, Marker(trade, FlowMarkerKind.Sweep, trade.Side, window.Volume, window.Prices.Count), settings, result);
                        window.Clear();
                    }
                }
            }
            if (settings.EnableLiquidityChanges || settings.EnableReplenishment)
                BuildLiquidity(snapshot, settings, frames, trades, markers, result);
            markers.Sort(CompareMarkers);
            TrimMarkers(markers, settings.MaxMarkers, result);
            result.Markers = markers.ToArray();
            return result;
        }

        private static FlowMarker Marker(TradeRecord trade, FlowMarkerKind kind, TradeSide side, long volume, int levels)
        {
            return new FlowMarker { TimeTicks = trade.TimeTicks, PriceTick = trade.PriceTick,
                Volume = volume, Quantity = volume, Levels = levels, Kind = kind, Side = side };
        }

        private static void BuildLiquidity(EngineSnapshot snapshot, OrderFlowSettings settings, HeatFrame[] frames,
            TradeRecord[] trades, List<FlowMarker> markers, OrderFlowResult result)
        {
            Dictionary<Key, double> previous = null;
            HeatFrame prior = null;
            long visits = 0;
            int tradeIndex = 0;
            // The final frame may be a changing partial frame. Wait for its successor;
            // this adds one frame of latency instead of rewriting a marker later.
            for (int index = 0; index < frames.Length - 1; index++)
            {
                HeatFrame frame = frames[index];
                if (frame == null || frame.FromTicks < 0 || frame.ToTicks <= frame.FromTicks || frame.ToTicks > snapshot.TimeTicks ||
                    (prior != null && frame.FromTicks < prior.ToTicks))
                    throw new ArgumentException("Invalid study depth-frame interval.", "snapshot");
                var current = new Dictionary<Key, double>();
                foreach (HeatCell cell in frame.Cells ?? new HeatCell[0])
                {
                    if (++visits > settings.MaxCells) throw new InvalidOperationException("Order-flow depth-cell budget exceeded.");
                    if (cell == null || !Finite(cell.RawQuantity) || cell.RawQuantity < 0 ||
                        (cell.Side != BookSide.Bid && cell.Side != BookSide.Ask))
                        throw new ArgumentException("Invalid study liquidity cell.", "snapshot");
                    var key = new Key(cell.Tick, cell.Side == BookSide.Bid ? TradeSide.Buy : TradeSide.Sell);
                    if (current.ContainsKey(key)) throw new ArgumentException("Duplicate study liquidity cell.", "snapshot");
                    current.Add(key, cell.RawQuantity);
                }
                var executed = new Dictionary<Key, long>();
                while (tradeIndex < trades.Length && trades[tradeIndex].TimeTicks < frame.FromTicks) tradeIndex++;
                while (tradeIndex < trades.Length && trades[tradeIndex].TimeTicks < frame.ToTicks)
                {
                    TradeRecord trade = trades[tradeIndex++];
                    if (trade.Side == TradeSide.Unknown || trade.Epoch != frame.Epoch) continue;
                    var key = new Key(trade.PriceTick, Opposite(trade.Side));
                    long amount; executed.TryGetValue(key, out amount);
                    executed[key] = checked(amount + trade.Volume);
                }
                if (prior != null && prior.ToTicks == frame.FromTicks && prior.Epoch == frame.Epoch &&
                    prior.FromTicks >= snapshot.SessionStartTicks)
                {
                    foreach (KeyValuePair<Key, double> row in current)
                    {
                        double before;
                        // Missing levels can mean provider depth-window movement. Do
                        // not turn an unobserved boundary into an add/cancel signal.
                        if (!previous.TryGetValue(row.Key, out before)) continue;
                        double change = row.Value - before;
                        if (settings.EnableLiquidityChanges && Math.Abs(change) >= settings.LiquidityChangeQuantity)
                            AddMarker(markers, new FlowMarker { TimeTicks = frame.ToTicks, PriceTick = row.Key.Tick,
                                Side = row.Key.Side, Kind = change > 0 ? FlowMarkerKind.LiquidityAdded : FlowMarkerKind.LiquidityRemoved,
                                Quantity = Math.Abs(change), Levels = 1 }, settings, result);
                        long fills;
                        if (settings.EnableReplenishment && executed.TryGetValue(row.Key, out fills) && fills >= settings.ReplenishmentVolume &&
                            before > 0 && row.Value >= before * settings.ReplenishmentMinimumFraction && row.Value + fills > before)
                            AddMarker(markers, new FlowMarker { TimeTicks = frame.ToTicks, PriceTick = row.Key.Tick, Side = row.Key.Side,
                                Kind = FlowMarkerKind.ReplenishmentCandidate, Volume = fills,
                                Quantity = row.Value + fills - before, Levels = 1 }, settings, result);
                    }
                }
                previous = current; prior = frame; result.ProcessedFrames++;
            }
        }

        private static void AddMarker(List<FlowMarker> markers, FlowMarker marker, OrderFlowSettings settings, OrderFlowResult result)
        {
            markers.Add(marker);
            // Bound every producer, including unknown-side prints and a single
            // large depth frame. Equal-time marks require the same ordering as
            // final output before any evidence is discarded.
            if (markers.Count > settings.MaxMarkers * 2)
            { markers.Sort(CompareMarkers); TrimMarkers(markers, settings.MaxMarkers, result); }
        }

        private static int CompareMarkers(FlowMarker a, FlowMarker b)
        {
            int c = a.TimeTicks.CompareTo(b.TimeTicks);
            if (c == 0) c = a.Kind.CompareTo(b.Kind);
            if (c == 0) c = a.PriceTick.CompareTo(b.PriceTick);
            if (c == 0) c = a.Side.CompareTo(b.Side);
            if (c == 0) c = a.Volume.CompareTo(b.Volume);
            if (c == 0) c = a.Levels.CompareTo(b.Levels);
            return c == 0 ? a.Quantity.CompareTo(b.Quantity) : c;
        }
        private static void TrimMarkers(List<FlowMarker> markers, int maximum, OrderFlowResult result)
        {
            if (markers.Count <= maximum) return;
            markers.RemoveRange(0, markers.Count - maximum); result.Truncated = true;
        }
        private static TradeSide Opposite(TradeSide side) { return side == TradeSide.Buy ? TradeSide.Sell : TradeSide.Buy; }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static void Validate(OrderFlowSettings value)
        {
            if (value.AbsorptionVolume < 1 || value.SweepVolume < 1 || value.LargePrintVolume < 1 || value.ReplenishmentVolume < 1 ||
                value.AbsorptionWindowMilliseconds < 1 || value.AbsorptionWindowMilliseconds > 3600000 ||
                value.SweepWindowMilliseconds < 1 || value.SweepWindowMilliseconds > 3600000 || value.SweepMinimumLevels < 2 || value.SweepMinimumLevels > 10000 ||
                !Finite(value.LiquidityChangeQuantity) || value.LiquidityChangeQuantity <= 0 ||
                !Finite(value.ReplenishmentMinimumFraction) || value.ReplenishmentMinimumFraction < 0 || value.ReplenishmentMinimumFraction > 1 ||
                value.MaxMarkers < 1 || value.MaxMarkers > 1000000 || value.MaxTrades < 1 || value.MaxTrades > 2000000 ||
                value.MaxFrames < 1 || value.MaxFrames > 200000 || value.MaxCells < 1 || value.MaxCells > 12000000)
                throw new ArgumentOutOfRangeException("settings", "Invalid order-flow threshold, window, or processing budget.");
        }
    }
}
