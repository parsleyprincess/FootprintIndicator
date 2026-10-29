using System.Collections.Concurrent;
using System.Diagnostics;
using FootprintIndicator.Core;

internal static class Program
{
    private static readonly DateTime Origin = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);
    private static readonly List<(string Name, Action Body)> Cases = new();
    private static int assertions;

    private static int Main()
    {
        RegisterEngineTests();
        RegisterBoundaryTests();
        RegisterVisualTests();
        Test("Causal following-liquidity heuristic and independent accounting", () => FollowingFilterTests.Run(Assert));
        Test("Generic fine, fractional, negative-price instruments and trade reset epochs", () => InstrumentCoverageTests.Run(Assert));
        Test("Profile value area, VWAP bands, imbalances, footprints and TPO", () => ProfileStudiesTests.Run(Assert));
        Test("Order-ID MBO sequencing, filters, traces and refill candidates", () => MboTests.Run(Assert));
        Test("Authoritative depth recovery and restored-history continuity", () => DepthRecoveryTests.Run(Assert));
        Test("Causal absorption, sweeps, individual prints and coarse liquidity candidates", () => OrderFlowTests.Run(Assert));
        Test("Checksummed capture archives, safe replacement and historical-only restore", () => HistoryStoreTests.Run(Assert));
        Test("Stable historical bubble groups, fixed sizing and heat contrast floor", () => VisualStabilityTests.Run(Assert));
        Test("Historical liquidity and execution hover inspection", () => HoverInspectionTests.Run(Assert));
        Test("Fixed-time volume bins and stable CVD scales", () => LowerPanelTests.Run(Assert));
        var failures = new List<string>();
        var total = Stopwatch.StartNew();
        foreach (var test in Cases)
        {
            var elapsed = Stopwatch.StartNew();
            try
            {
                test.Body();
                Console.WriteLine($"PASS {test.Name} ({elapsed.ElapsedMilliseconds} ms)");
            }
            catch (Exception error)
            {
                failures.Add(test.Name);
                Console.WriteLine($"FAIL {test.Name}: {error.GetType().Name}: {error.Message}");
                Console.WriteLine(error.StackTrace);
            }
        }
        Console.WriteLine($"RESULT {Cases.Count - failures.Count} passed, {failures.Count} failed, 0 skipped; {assertions} assertions; {total.ElapsedMilliseconds} ms");
        return failures.Count == 0 ? 0 : 1;
    }

    private static void Test(string name, Action body) => Cases.Add((name, body));
    private static DateTime At(double milliseconds) => Origin.AddTicks((long)Math.Round(milliseconds * TimeSpan.TicksPerMillisecond));
    private static long Tick(double milliseconds) => At(milliseconds).Ticks;
    private static EngineSettings Settings(int frame = 250, int persistence = 1000) => new()
    {
        TickSize = .25, FrameMilliseconds = frame, PersistenceMilliseconds = persistence,
        MaxFrames = 1000, MaxTrades = 10000, MaxDepthLevels = 200, RetentionMinutes = 30
    };
    private static MarketEngine Engine(int frame = 250, int persistence = 1000) => new(Settings(frame, persistence));
    private static void Assert(bool value, string message)
    {
        Interlocked.Increment(ref assertions);
        if (!value) throw new InvalidOperationException(message);
    }
    private static void Equal<T>(T expected, T actual, string label) where T : IEquatable<T>
        => Assert(expected.Equals(actual), $"{label}: expected {expected}, got {actual}");
    private static void Near(double expected, double actual, string label, double tolerance = 1e-7)
        => Assert(double.IsFinite(actual) && Math.Abs(expected - actual) <= tolerance * Math.Max(1, Math.Abs(expected)), $"{label}: expected {expected:R}, got {actual:R}");
    private static void Throws<T>(Action body, string label) where T : Exception
    {
        try { body(); }
        catch (T) { Assert(true, label); return; }
        throw new InvalidOperationException($"{label}: expected {typeof(T).Name}");
    }
    private static double Exposure(EngineSnapshot snap, long priceTick, bool persistent, double fromMs = 0, double toMs = double.PositiveInfinity)
    {
        var from = Tick(fromMs);
        var to = double.IsPositiveInfinity(toMs) ? long.MaxValue : Tick(toMs);
        return snap.Frames.Sum(f => Math.Max(0, Math.Min(f.ToTicks, to) - Math.Max(f.FromTicks, from)) / (double)TimeSpan.TicksPerMillisecond *
            f.Cells.Where(c => c.Tick == priceTick).Sum(c => persistent ? c.PersistentQuantity : c.RawQuantity));
    }
    private static HeatFrame Frame(EngineSnapshot snapshot, double from, double to)
        => snapshot.Frames.Single(f => f.FromTicks == Tick(from) && f.ToTicks == Tick(to));

    private static void RegisterEngineTests()
    {
        Test("Price normalization for NQ ES GC YM RTY tick sizes", () =>
        {
            foreach (var item in new[] { ("NQ", .25, 21000.25, 84001L), ("ES", .25, 6000.75, 24003L), ("GC", .1, 2700.1, 27001L), ("YM", 1d, 43001d, 43001L), ("RTY", .1, 2200.3, 22003L) })
            {
                Equal(item.Item4, MarketEngine.PriceToTick(item.Item3, item.Item2), item.Item1);
                Equal(item.Item4, MarketEngine.PriceToTick(item.Item3 + item.Item2 * 1e-8, item.Item2), item.Item1 + " roundoff");
            }
            Throws<ArgumentException>(() => MarketEngine.PriceToTick(double.NaN, .25), "NaN price");
            Throws<ArgumentException>(() => MarketEngine.PriceToTick(10, 0), "zero tick size");
        });
        Test("Settings validation and defensive copy", () =>
        {
            var bad = Settings(); bad.TickSize = double.NaN;
            Throws<ArgumentException>(() => new MarketEngine(bad), "invalid tick size");
            bad = Settings(); bad.FrameMilliseconds = 0;
            Throws<ArgumentException>(() => new MarketEngine(bad), "zero frame duration");
            var original = Settings(); var engine = new MarketEngine(original); original.TickSize = 10;
            engine.Depth(At(0), BookSide.Bid, DepthMutation.Insert, 0, 100.25, 10);
            Equal(401L, engine.Snapshot().Bids[0].Tick, "settings cloned");
        });
        Test("Bid positional insert shift update remove", () =>
        {
            var engine = Engine();
            engine.Depth(At(0), BookSide.Bid, DepthMutation.Insert, 0, 100, 100);
            engine.Depth(At(1), BookSide.Bid, DepthMutation.Insert, 1, 99.75, 50);
            engine.Depth(At(2), BookSide.Bid, DepthMutation.Insert, 0, 100.25, 10);
            var snap = engine.Snapshot();
            Equal(3, snap.Bids.Length, "insert count"); Equal(400L, snap.Bids[1].Tick, "old best shifted");
            engine.Depth(At(3), BookSide.Bid, DepthMutation.Update, 1, 100, 120);
            Equal(120L, engine.Snapshot().Bids[1].Quantity, "replacement not additive");
            engine.Depth(At(4), BookSide.Bid, DepthMutation.Remove, 1, 100, 0);
            snap = engine.Snapshot();
            Equal(2, snap.Bids.Length, "remove count"); Equal(399L, snap.Bids[1].Tick, "lower level shifts up");
        });
        Test("Ask positions have opposite price ordering", () =>
        {
            var engine = Engine();
            engine.Depth(At(0), BookSide.Ask, DepthMutation.Insert, 0, 100.5, 10);
            engine.Depth(At(1), BookSide.Ask, DepthMutation.Insert, 1, 101, 20);
            engine.Depth(At(2), BookSide.Ask, DepthMutation.Insert, 0, 100.25, 30);
            var snap = engine.Snapshot();
            Assert(snap.Asks.Select(l => l.Tick).SequenceEqual(new long[] { 401, 402, 404 }), "asks ascending");
        });
        Test("Contiguous initial updates append while position gaps invalidate side", () =>
        {
            var engine = Engine();
            engine.Depth(At(0), BookSide.Bid, DepthMutation.Update, 0, 100, 10);
            engine.Depth(At(1), BookSide.Bid, DepthMutation.Update, 1, 99.75, 20);
            engine.Depth(At(1), BookSide.Ask, DepthMutation.Insert, 0, 100.25, 30);
            Equal(2, engine.Snapshot().Bids.Length, "initial update ladder");
            engine.Depth(At(2), BookSide.Bid, DepthMutation.Update, 5, 99, 40);
            var snap = engine.Snapshot();
            Equal(0, snap.Bids.Length, "gapped bid ladder cleared"); Equal(1, snap.Asks.Length, "other side preserved");
            Assert(snap.InvalidEvents > 0, "invalid event counted");
            engine.Depth(At(3), BookSide.Bid, DepthMutation.Update, 0, 99.5, 25);
            Equal(398L, engine.Snapshot().Bids.Single().Tick, "rebuild after invalidation");
        });
        Test("Removal price mismatch and duplicate price cannot create ghost depth", () =>
        {
            var engine = Engine();
            engine.Depth(At(0), BookSide.Bid, DepthMutation.Insert, 0, 100, 10);
            engine.Depth(At(1), BookSide.Bid, DepthMutation.Insert, 1, 99.75, 10);
            engine.Depth(At(2), BookSide.Bid, DepthMutation.Remove, 0, 99.75, 0);
            Equal(0, engine.Snapshot().Bids.Length, "mismatched removal clears side");
            engine.Depth(At(3), BookSide.Bid, DepthMutation.Insert, 0, 100, 10);
            engine.Depth(At(4), BookSide.Bid, DepthMutation.Insert, 1, 100, 10);
            var snap = engine.Snapshot();
            Equal(snap.Bids.Length, snap.Bids.Select(l => l.Tick).Distinct().Count(), "no duplicate levels");
            Assert(snap.InvalidEvents >= 2, "both bad events counted");
        });
        Test("Zero quantity update retains its positional slot until explicit removal", () =>
        {
            var engine = Engine();
            engine.Depth(At(0), BookSide.Bid, DepthMutation.Insert, 0, 100, 10);
            engine.Depth(At(1), BookSide.Bid, DepthMutation.Update, 0, 100, 0);
            Equal(1, engine.Snapshot().Bids.Length, "zero update retains provider position");
            Equal(0L, engine.Snapshot().Bids[0].Quantity, "zero displayed quantity");
            engine.Depth(At(2), BookSide.Bid, DepthMutation.Remove, 0, 100, 0);
            Equal(0, engine.Snapshot().Bids.Length, "explicit remove releases provider position");
        });
        Test("Independent randomized ordered ladder reference model 2000 operations", () =>
        {
            var engine = Engine(); var reference = new SortedDictionary<long, long>(); var random = new Random(93711);
            for (int i = 0; i < 2000; i++)
            {
                var prices = reference.Keys.OrderByDescending(x => x).ToArray();
                if (prices.Length == 0 || (prices.Length < 50 && random.Next(3) == 0))
                {
                    long price; do { price = 350 + random.Next(100); } while (reference.ContainsKey(price));
                    long quantity = random.Next(1, 500);
                    int position = prices.Count(p => p > price);
                    engine.Depth(At(i), BookSide.Bid, DepthMutation.Insert, position, price * .25, quantity);
                    reference.Add(price, quantity);
                }
                else
                {
                    int position = random.Next(prices.Length); long price = prices[position];
                    if (random.Next(3) == 0)
                    { engine.Depth(At(i), BookSide.Bid, DepthMutation.Remove, position, price * .25, 0); reference.Remove(price); }
                    else
                    { long quantity = random.Next(1, 500); engine.Depth(At(i), BookSide.Bid, DepthMutation.Update, position, price * .25, quantity); reference[price] = quantity; }
                }
                var actual = engine.Snapshot().Bids;
                Assert(actual.Select(l => (l.Tick, l.Quantity)).SequenceEqual(reference.Reverse().Select(kv => (kv.Key, kv.Value))), $"reference ladder at operation {i}");
            }
        });
        Test("Raw liquidity integrates prior size over exact event durations", () =>
        {
            var engine = Engine(1000, 0);
            engine.Depth(At(0), BookSide.Bid, DepthMutation.Insert, 0, 100, 100);
            engine.Depth(At(400), BookSide.Bid, DepthMutation.Update, 0, 100, 200);
            engine.Advance(At(1000));
            Near(160000, Exposure(engine.Snapshot(), 400, false), "contract milliseconds");
            Near(160, Frame(engine.Snapshot(), 0, 1000).Cells.Single().RawQuantity, "frame weighted mean");
        });
        Test("Partial snapshots do not advance or double count integrals", () =>
        {
            var engine = Engine(250, 0);
            engine.Depth(At(0), BookSide.Bid, DepthMutation.Insert, 0, 100, 100);
            engine.Depth(At(100), BookSide.Bid, DepthMutation.Update, 0, 100, 200);
            engine.Advance(At(150));
            var first = engine.Snapshot(); var repeat = engine.Snapshot();
            Near(20000, Exposure(first, 400, false), "partial exposure");
            Near(Exposure(first, 400, false), Exposure(repeat, 400, false), "snapshot repeat");
            engine.Advance(At(250));
            Near(160, Frame(engine.Snapshot(), 0, 250).Cells.Single().RawQuantity, "whole frame after snapshots");
            Near(20000, Exposure(first, 400, false), "older snapshot immutable");
        });
        Test("Fixed-price persistence does not age newly added size", () =>
        {
            var engine = Engine(250, 1000);
            engine.Depth(At(0), BookSide.Bid, DepthMutation.Insert, 0, 100, 10);
            engine.Depth(At(1000), BookSide.Bid, DepthMutation.Update, 0, 100, 500);
            engine.Advance(At(1250));
            var cell = Frame(engine.Snapshot(), 1000, 1250).Cells.Single();
            Near(500, cell.RawQuantity, "raw quantity"); Near(10, cell.PersistentQuantity, "only old floor matured");
        });
        Test("Persistence maturity inside a frame is integrated, not backdated", () =>
        {
            var engine = Engine(250, 1000);
            engine.Depth(At(125), BookSide.Bid, DepthMutation.Insert, 0, 100, 100);
            engine.Advance(At(1250));
            var cell = Frame(engine.Snapshot(), 1000, 1250).Cells.Single();
            Near(100, cell.RawQuantity, "resting size"); Near(50, cell.PersistentQuantity, "half frame matured");
        });
        Test("Quantity reductions remove young strata before conservative old floor", () =>
        {
            var engine = Engine(100, 500);
            engine.Depth(At(0), BookSide.Bid, DepthMutation.Insert, 0, 100, 10);
            engine.Depth(At(400), BookSide.Bid, DepthMutation.Update, 0, 100, 50);
            engine.Depth(At(600), BookSide.Bid, DepthMutation.Update, 0, 100, 40);
            engine.Advance(At(1000));
            Near(8000, Exposure(engine.Snapshot(), 400, true), "aged lower-envelope exposure");
        });
        Test("Disappearing liquidity loses persistence when it returns", () =>
        {
            var engine = Engine(250, 500);
            engine.Depth(At(0), BookSide.Bid, DepthMutation.Insert, 0, 100, 100);
            engine.Depth(At(1000), BookSide.Bid, DepthMutation.Remove, 0, 100, 0);
            engine.Depth(At(1100), BookSide.Bid, DepthMutation.Insert, 0, 100, 100);
            engine.Advance(At(1500));
            Near(0, Exposure(engine.Snapshot(), 400, true, 1100, 1500), "reappeared size is young");
        });
        Test("Historical prefix cannot acquire persistence from future survival", () =>
        {
            var engine = Engine(250, 1000);
            engine.Depth(At(0), BookSide.Bid, DepthMutation.Insert, 0, 100, 100);
            engine.Advance(At(750)); var before = engine.Snapshot();
            engine.Advance(At(2000)); var after = engine.Snapshot();
            Near(0, Exposure(before, 400, true), "unmatured prefix");
            Near(0, Exposure(after, 400, true, 0, 750), "future does not recolor past");
            Near(100000, Exposure(after, 400, true, 1000, 2000), "mature future");
        });
        Test("Ladder shifts cannot move historical price levels", () =>
        {
            var engine = Engine(250, 0);
            engine.Depth(At(0), BookSide.Bid, DepthMutation.Insert, 0, 100, 50);
            engine.Depth(At(250), BookSide.Bid, DepthMutation.Insert, 0, 100.25, 20);
            engine.Advance(At(500));
            Near(25000, Exposure(engine.Snapshot(), 400, false), "fixed original price");
            Near(5000, Exposure(engine.Snapshot(), 401, false), "new best only since arrival");
        });
        Test("Reset closes heat interval without erasing trades or session", () =>
        {
            var engine = Engine(250, 0);
            engine.Depth(At(0), BookSide.Bid, DepthMutation.Insert, 0, 100, 10);
            engine.Trade(At(100), 100.25, 5, 100, 100.25);
            engine.ResetDepth(At(250), "test reset");
            engine.Advance(At(1000));
            var snap = engine.Snapshot();
            Near(2500, Exposure(snap, 400, false), "known history retained only");
            Equal(0, snap.Bids.Length, "book cleared"); Equal(5L, snap.SessionVolume, "executions retained");
            Assert(snap.Epoch > 0, "reset advances epoch");
        });
        Test("Disconnect gap never extends stale depth and reconnect starts fresh age", () =>
        {
            var engine = Engine(250, 500);
            engine.Depth(At(0), BookSide.Bid, DepthMutation.Insert, 0, 100, 10);
            engine.SetConnected(At(250), false); engine.Advance(At(1000));
            var gap = engine.Snapshot(); Assert(!gap.Connected, "disconnected state"); Equal(0, gap.Bids.Length, "no live stale bids");
            engine.SetConnected(At(1000), true);
            engine.Depth(At(1000), BookSide.Bid, DepthMutation.Insert, 0, 100, 20); engine.Advance(At(1250));
            var snap = engine.Snapshot();
            Near(7500, Exposure(snap, 400, false), "pre/post gap exposures");
            Near(0, Exposure(snap, 400, false, 250, 1000), "gap is not assumed liquidity");
            Near(0, Exposure(snap, 400, true, 1000, 1250), "age reset on reconnect");
        });
        Test("Execution classification includes uncertain locked crossed and missing quotes", () =>
        {
            var engine = Engine();
            engine.Trade(At(0), 101, 10, 100, 101);
            engine.Trade(At(1), 100, 6, 100, 101);
            engine.Trade(At(2), 100.5, 3, 100, 101);
            engine.Trade(At(3), 100, 2, 100, 100);
            engine.Trade(At(4), 100.5, 4, 101, 100);
            engine.Trade(At(5), 100, 5, double.NaN, double.NaN);
            var snap = engine.Snapshot();
            Assert(snap.Trades.Select(t => t.Side).SequenceEqual(new[] { TradeSide.Buy, TradeSide.Sell, TradeSide.Unknown, TradeSide.Unknown, TradeSide.Unknown, TradeSide.Unknown }), "classified sides");
            Equal(30L, snap.SessionVolume, "all prints counted"); Equal(4L, snap.CumulativeDelta, "only classified delta");
            Equal(30L, snap.Profile.Sum(p => p.Total), "profile volume"); Equal(14L, snap.Profile.Sum(p => p.Unknown), "unknown accounting");
            Near((101 * 10d + 100 * 6 + 100.5 * 3 + 100 * 2 + 100.5 * 4 + 100 * 5) / 30, snap.SessionVwap, "VWAP");
        });
        Test("Invalid trade and depth values are rejected without negative or nonfinite state", () =>
        {
            var engine = Engine();
            engine.Trade(At(0), 100, -1, 99, 100); engine.Trade(At(1), double.NaN, 1, 99, 100);
            engine.Trade(At(2), double.PositiveInfinity, 1, 99, 100); engine.Trade(At(3), 100, 0, 99, 100);
            engine.Depth(At(4), BookSide.Bid, DepthMutation.Insert, 0, 100, -1);
            engine.Depth(At(5), BookSide.Bid, DepthMutation.Insert, 0, double.NaN, 1);
            var snap = engine.Snapshot();
            Equal(0L, snap.SessionVolume, "invalid prints not counted"); Equal(0, snap.Bids.Length, "invalid book empty");
            Assert(snap.InvalidEvents >= 6, "invalid counters");
        });
        Test("Overflowing trade is atomically rejected", () =>
        {
            var engine = Engine(); engine.Trade(At(0), 100, 1, 99.75, 100);
            engine.Trade(At(1), 100, long.MaxValue, 99.75, 100);
            var snap = engine.Snapshot();
            Equal(1L, snap.SessionVolume, "volume not wrapped or partially applied"); Equal(1L, snap.CumulativeDelta, "delta not corrupted");
            Equal(1L, snap.Profile.Sum(p => p.Total), "profile not partially applied"); Assert(snap.InvalidEvents > 0, "overflow surfaced");
        });
        Test("Equal timestamps keep sequence and late timestamps do not rewind", () =>
        {
            var engine = Engine();
            engine.Trade(At(100), 100, 1, 99.75, 100); engine.Trade(At(100), 100.25, 2, 100, 100.25);
            engine.Trade(At(50), 100.5, 3, 100.25, 100.5);
            var snap = engine.Snapshot();
            Equal(3, snap.Trades.Length, "none dropped by equal time key");
            Assert(snap.Trades.Zip(snap.Trades.Skip(1), (a, b) => a.Sequence < b.Sequence && a.TimeTicks <= b.TimeTicks).All(x => x), "sequence monotone");
            Assert(snap.LateEvents > 0, "late arrivals counted"); Equal(Tick(100), snap.TimeTicks, "clock does not rewind");
        });
        Test("Session reset clears profile and delta while preserving market history", () =>
        {
            var engine = Engine(); engine.Trade(At(0), 100, 10, 99.75, 100);
            engine.StartSession(At(1000)); var reset = engine.Snapshot();
            Equal(0L, reset.SessionVolume, "new session volume"); Equal(0L, reset.CumulativeDelta, "new session delta"); Equal(0, reset.Profile.Length, "new profile");
            Equal(1, reset.Trades.Length, "retained trade history");
            engine.Trade(At(1100), 100, 3, 100, 100.25); var snap = engine.Snapshot();
            Equal(-3L, snap.CumulativeDelta, "new session sell delta"); Equal(3L, snap.SessionVolume, "new session volume only");
        });
        Test("Count and time retention are bounded without altering session totals", () =>
        {
            var settings = Settings(250, 0); settings.MaxTrades = 3; settings.MaxFrames = 3; settings.RetentionMinutes = 1;
            var engine = new MarketEngine(settings);
            engine.Depth(At(0), BookSide.Bid, DepthMutation.Insert, 0, 100, 10);
            for (int i = 0; i < 10; i++) engine.Trade(At(i * 250), 100, 1, 99.75, 100);
            engine.Advance(At(2500)); var snap = engine.Snapshot();
            Assert(snap.Trades.Length <= 3, "trade cap"); Assert(snap.Frames.Length <= 3, "frame cap");
            Equal(10L, snap.SessionVolume, "session retains total"); Equal(10L, snap.Profile.Sum(p => p.Total), "profile retains total");
            Assert(snap.DroppedTrades >= 7 && snap.DroppedFrames >= 7, "drop counts observable");
            engine.ResetDepth(At(3000), "stop capture"); engine.Advance(At(70000));
            Assert(engine.Snapshot().Trades.All(t => t.TimeTicks >= Tick(10000)), "time-retained trades");
        });
        Test("Snapshots do not expose mutable engine collections", () =>
        {
            var engine = Engine(250, 0); engine.Depth(At(0), BookSide.Bid, DepthMutation.Insert, 0, 100, 10);
            engine.Trade(At(10), 100, 2, 99.75, 100); engine.Advance(At(250)); var first = engine.Snapshot();
            first.Bids[0] = new DepthLevel(999, 999); first.Frames[0].Cells[0] = new HeatCell(999, BookSide.Bid, 999, 999);
            first.Trades[0] = new TradeRecord(Tick(0), 999, 999, TradeSide.Buy, 999, 999);
            var second = engine.Snapshot(); Equal(400L, second.Bids[0].Tick, "book copy"); Equal(400L, second.Frames[0].Cells[0].Tick, "nested frame copy"); Equal(2L, second.Trades[0].Volume, "trade copy");
        });
        Test("Concurrent feed and snapshots remain coherent", () =>
        {
            var engine = Engine(); engine.Depth(At(0), BookSide.Bid, DepthMutation.Insert, 0, 100, 10);
            var errors = new ConcurrentQueue<Exception>();
            var producer = Task.Run(() =>
            {
                try { for (int i = 1; i <= 4000; i++) { engine.Depth(At(i), BookSide.Bid, DepthMutation.Update, 0, 100, i % 200 + 1); engine.Trade(At(i), 100.25, 1, 100, 100.25); } }
                catch (Exception e) { errors.Enqueue(e); }
            });
            // Independent reader loops run while the producer advances; completion is joined below.
            var workers = new[] {
                Task.Run(() => { try { for (int i = 0; i < 100; i++) { var s = engine.Snapshot(); Assert(s.Bids.Length == 1 && s.Bids[0].Quantity > 0, "coherent live side"); Assert(s.SessionVolume >= 0, "nonnegative total"); } } catch (Exception e) { errors.Enqueue(e); } }),
                Task.Run(() => { try { for (int i = 0; i < 100; i++) { var s = engine.Snapshot(); Assert(s.Frames.All(f => f.Cells.All(c => c.PersistentQuantity <= c.RawQuantity + 1e-6)), "persistent bounded by raw"); } } catch (Exception e) { errors.Enqueue(e); } })
            };
            Task.WaitAll(workers.Append(producer).ToArray());
            Equal(4000L, engine.Snapshot().SessionVolume, "all concurrent executions counted");
            Assert(errors.IsEmpty, errors.TryPeek(out var first) ? first.Message : "concurrent readers");
        });
        Test("Burst replay performance and memory-budget smoke", () =>
        {
            var settings = Settings(100, 500); settings.MaxTrades = 1000; settings.MaxFrames = 100;
            var engine = new MarketEngine(settings); engine.Depth(At(0), BookSide.Bid, DepthMutation.Insert, 0, 100, 1);
            var elapsed = Stopwatch.StartNew();
            for (int i = 1; i <= 10000; i++) { engine.Depth(At(i), BookSide.Bid, DepthMutation.Update, 0, 100, i % 200 + 1); engine.Trade(At(i), 100.25, 1, 100, 100.25); }
            var snap = engine.Snapshot();
            Equal(10000L, snap.SessionVolume, "burst executes all trades"); Assert(snap.Trades.Length <= 1000 && snap.Frames.Length <= 100, "burst bounded retention");
            Console.WriteLine($"  PERF 20000 market events: {elapsed.ElapsedMilliseconds} ms; retained {snap.Trades.Length} trades/{snap.Frames.Length} heat frames (informational, no platform benchmark claim)");
        });
    }

    private static void RegisterBoundaryTests()
    {
        Test("Provider depth beyond retained prefix cannot invalidate the near book", () =>
        {
            var settings = Settings(100, 500); settings.MaxDepthLevels = 2;
            var engine = new MarketEngine(settings);
            for (int i = 0; i < 8; ++i)
                engine.Depth(At(i), BookSide.Bid, DepthMutation.Insert, i, 100 - i * .25, 10 + i);
            engine.Depth(At(10), BookSide.Bid, DepthMutation.Update, 6, 98.5, 900);
            engine.Depth(At(11), BookSide.Bid, DepthMutation.Remove, 5, 98.75, 0);
            var snapshot = engine.Snapshot();
            Assert(snapshot.Bids.Select(l => (l.Tick, l.Quantity)).SequenceEqual(new[] { (400L, 10L), (399L, 11L) }),
                "normal deeper-feed activity leaves the retained top two unchanged");
            Equal(0L, snapshot.InvalidEvents, "valid out-of-window provider operations are not corrupt depth");
            engine.Advance(At(1000));
            Assert(engine.Snapshot().Frames.SelectMany(f => f.Cells).All(c => c.Tick == 400 || c.Tick == 399),
                "omitted tail quantities are never painted as observed rows");
        });
        Test("Truncated depth reentry waits for an observed row and starts a fresh age", () =>
        {
            var settings = Settings(100, 500); settings.MaxDepthLevels = 2;
            var engine = new MarketEngine(settings);
            engine.Depth(At(0), BookSide.Bid, DepthMutation.Insert, 0, 100, 10);
            engine.Depth(At(0), BookSide.Bid, DepthMutation.Insert, 1, 99.75, 20);
            engine.Advance(At(1000));
            engine.Depth(At(1000), BookSide.Bid, DepthMutation.Insert, 0, 100.25, 5);
            Assert(engine.Snapshot().Bids.All(l => l.Tick != 399), "former tail is no longer known");
            engine.Depth(At(1100), BookSide.Bid, DepthMutation.Remove, 0, 100.25, 0);
            Equal(1, engine.Snapshot().Bids.Length, "removal does not invent the missing tail");
            engine.Depth(At(1101), BookSide.Bid, DepthMutation.Update, 5, 98.5, 400);
            Equal(400L, engine.Snapshot().Bids.Single().Tick, "outside-window update does not clear remaining prefix");
            engine.Depth(At(1200), BookSide.Bid, DepthMutation.Update, 1, 99.75, 20);
            engine.Advance(At(1600));
            Near(0, Exposure(engine.Snapshot(), 399, false, 1000, 1200), "unobserved interval is not backfilled");
            Near(8000, Exposure(engine.Snapshot(), 399, false, 1200, 1600), "returned level raw exposure");
            Near(0, Exposure(engine.Snapshot(), 399, true, 1200, 1600), "returned level does not inherit old persistence");
            Equal(0L, engine.Snapshot().InvalidEvents, "normal truncation lifecycle is valid");
        });
        Test("Ask depth cap preserves the same prefix guarantees with ascending prices", () =>
        {
            var settings = Settings(); settings.MaxDepthLevels = 2;
            var engine = new MarketEngine(settings);
            for (int i = 0; i < 6; ++i)
                engine.Depth(At(i), BookSide.Ask, DepthMutation.Update, i, 100.25 + i * .25, 20 + i);
            engine.Depth(At(10), BookSide.Ask, DepthMutation.Remove, 4, 101.25, 0);
            var snapshot = engine.Snapshot();
            Assert(snapshot.Asks.Select(l => l.Tick).SequenceEqual(new[] { 401L, 402L }), "ascending retained ask prefix");
            Equal(0L, snapshot.InvalidEvents, "valid omitted ask rows are not corruption");
        });
        Test("Known truncated prefix survives a noncontiguous update before tail refill", () =>
        {
            var settings = Settings(); settings.MaxDepthLevels = 3;
            var engine = new MarketEngine(settings);
            for (int i = 0; i < 6; ++i)
                engine.Depth(At(i), BookSide.Bid, DepthMutation.Insert, i, 100 - i * .25, 10 + i);
            engine.Depth(At(100), BookSide.Bid, DepthMutation.Remove, 0, 100, 0);
            engine.Depth(At(110), BookSide.Bid, DepthMutation.Remove, 0, 99.75, 0);
            engine.Depth(At(120), BookSide.Bid, DepthMutation.Update, 2, 99, 66);
            Equal(398L, engine.Snapshot().Bids.Single().Tick, "unknown second row cannot erase known first row");
            engine.Depth(At(130), BookSide.Bid, DepthMutation.Update, 1, 99.25, 40);
            engine.Depth(At(140), BookSide.Bid, DepthMutation.Update, 2, 99, 66);
            Assert(engine.Snapshot().Bids.Select(l => (l.Tick, l.Quantity)).SequenceEqual(new[] { (398L, 12L), (397L, 40L), (396L, 66L) }),
                "only supplied contiguous rows restore the retained prefix");
            Equal(0L, engine.Snapshot().InvalidEvents, "known truncation does not become a feed-corruption alarm");
        });
        Test("Cohort capacity conservatively delays persistence without changing raw depth", () =>
        {
            var boundedSettings = Settings(100, 1000); boundedSettings.MaxCohortsPerLevel = 2;
            var exactSettings = Settings(100, 1000); exactSettings.MaxCohortsPerLevel = 128;
            var bounded = new MarketEngine(boundedSettings); var exact = new MarketEngine(exactSettings);
            for (int step = 0; step <= 20; ++step)
            {
                if (step <= 4)
                {
                    var operation = step == 0 ? DepthMutation.Insert : DepthMutation.Update;
                    bounded.Depth(At(step * 100), BookSide.Bid, operation, 0, 100, (step + 1) * 10);
                    exact.Depth(At(step * 100), BookSide.Bid, operation, 0, 100, (step + 1) * 10);
                }
                else { bounded.Advance(At(step * 100)); exact.Advance(At(step * 100)); }
                var actual = bounded.Snapshot(); var reference = exact.Snapshot();
                Near(Exposure(reference, 400, false), Exposure(actual, 400, false), "raw exposure unchanged by cohort budget");
                Assert(Exposure(actual, 400, true) <= Exposure(reference, 400, true) + 1e-6,
                    "bounded representation never invents older persistent quantity");
            }
            Near(50, Frame(bounded.Snapshot(), 1900, 2000).Cells.Single().PersistentQuantity,
                "all remaining quantity matures after the last cohort's threshold");
        });
        Test("Profile capacity rejects an unseen price atomically and still accepts known prices", () =>
        {
            var settings = Settings(); settings.MaxProfileLevels = 1;
            var engine = new MarketEngine(settings);
            engine.Trade(At(0), 100, 10, 99.75, 100);
            engine.Trade(At(1), 101, 5, 100.75, 101);
            var rejected = engine.Snapshot();
            Equal(10L, rejected.SessionVolume, "profile overflow leaves total unchanged");
            Equal(10L, rejected.CumulativeDelta, "profile overflow leaves delta unchanged");
            Equal(1L, rejected.TradeEvents, "rejected execution is not claimed accepted");
            Equal(1, rejected.Profile.Length, "profile remains bounded"); Equal(1, rejected.Trades.Length, "trade tape remains consistent");
            Near(100, rejected.SessionVwap, "VWAP unchanged"); Assert(rejected.InvalidEvents > 0, "capacity failure observable");
            engine.Trade(At(2), 100, 3, 100, 100.25);
            var continued = engine.Snapshot();
            Equal(13L, continued.SessionVolume, "known price remains usable"); Equal(7L, continued.CumulativeDelta, "subsequent sell counted");
            Equal(13L, continued.Profile.Single().Total, "tape and profile agree after capacity rejection");
        });
        Test("Session volume overflow across different prices is transactional", () =>
        {
            var engine = Engine();
            engine.Trade(At(0), 100, long.MaxValue - 5, 99.75, 100);
            engine.Trade(At(1), 101, 10, 101, 101.25);
            var rejected = engine.Snapshot();
            Equal(long.MaxValue - 5, rejected.SessionVolume, "cross-profile overflow rejected");
            Equal(long.MaxValue - 5, rejected.CumulativeDelta, "rejected sell cannot partially reduce delta");
            Equal(1, rejected.Profile.Length, "no empty/partial profile key inserted"); Equal(1L, rejected.TradeEvents, "accepted count preserved");
            Near(100, rejected.SessionVwap, "VWAP not partially updated");
            engine.Trade(At(2), 101, 5, double.NaN, double.NaN);
            var full = engine.Snapshot();
            Equal(long.MaxValue, full.SessionVolume, "exact Int64 maximum is representable");
            Equal(5L, full.Profile.Single(p => p.Tick == 404).Unknown, "remaining capacity accepted as unknown");
            engine.Trade(At(3), 102, 1, 101.75, 102);
            var final = engine.Snapshot();
            Equal(long.MaxValue, final.SessionVolume, "next unit cannot wrap total"); Equal(2, final.Profile.Length, "overflow cannot create a third profile level");
            Equal(2L, final.TradeEvents, "only successful executions counted");
        });
        Test("Large feed-clock jumps perform only bounded retained-frame work", () =>
        {
            var settings = Settings(1, 1000); settings.MaxFrames = 7; settings.RetentionMinutes = 1;
            var engine = new MarketEngine(settings);
            engine.Depth(Origin, BookSide.Bid, DepthMutation.Insert, 0, 100, 17);
            var target = Origin.AddYears(50);
            var watch = Stopwatch.StartNew(); engine.Advance(target); var snapshot = engine.Snapshot();
            Assert(snapshot.Frames.Length > 0 && snapshot.Frames.Length <= 7, "retained count bounds fifty-year clock jump");
            Assert(snapshot.Frames.All(f => f.FromTicks >= target.AddMilliseconds(-7).Ticks && f.ToTicks <= target.Ticks),
                "only final retained intervals are materialized");
            Assert(snapshot.Frames.SelectMany(f => f.Cells).All(c => Math.Abs(c.RawQuantity - 17) < 1e-9 && Math.Abs(c.PersistentQuantity - 17) < 1e-9),
                "standing quantities and causal maturity survive skipped expired history");
            Assert(snapshot.DroppedFrames > 1_000_000, "discarded history is reported");
            Console.WriteLine($"  PERF fifty-year feed clock jump: {watch.ElapsedMilliseconds} ms; {snapshot.Frames.Length} retained frames (informational)");
        });
        Test("DateTime maximum remains a valid endpoint without wrapped frames", () =>
        {
            var engine = Engine(100, 500);
            var from = DateTime.MaxValue.AddMilliseconds(-100);
            engine.Depth(from, BookSide.Bid, DepthMutation.Insert, 0, 100, 12);
            engine.Advance(DateTime.MaxValue);
            engine.Trade(DateTime.MaxValue, 100.25, 3, 100, 100.25);
            var snapshot = engine.Snapshot();
            Equal(DateTime.MaxValue.Ticks, snapshot.TimeTicks, "endpoint clock");
            Assert(snapshot.Frames.All(f => f.FromTicks >= from.Ticks && f.ToTicks <= DateTime.MaxValue.Ticks && f.ToTicks > f.FromTicks), "no frame wraps through zero or exceeds DateTime");
            Near(12 * 100d, snapshot.Frames.Sum(f => (f.ToTicks - f.FromTicks) / (double)TimeSpan.TicksPerMillisecond * f.Cells.Sum(c => c.RawQuantity)), "endpoint exposure");
            Assert(snapshot.Frames.SelectMany(f => f.Cells).All(c => c.PersistentQuantity == 0), "unmatured endpoint liquidity");
            Equal(3L, snapshot.SessionVolume, "endpoint trade counted");
            engine.ResetDepth(DateTime.MaxValue, "endpoint reset"); engine.Advance(DateTime.MaxValue);
            Equal(0, engine.Snapshot().Bids.Length, "endpoint reset remains safe");
        });
        Test("Malformed future events cannot advance history or mature liquidity", () =>
        {
            var engine = Engine(100, 500);
            engine.Depth(At(0), BookSide.Bid, DepthMutation.Insert, 0, 100, 10);
            engine.Trade(At(100), 100.25, 2, 100, 100.25);
            engine.Trade(DateTime.MaxValue, 100.25, 0, 100, 100.25);
            engine.Trade(DateTime.MaxValue, 100.25, long.MaxValue, 100, 100.25);
            engine.Depth(DateTime.MaxValue, BookSide.Bid, DepthMutation.Insert, 0, double.NaN, 1);
            var snapshot = engine.Snapshot();
            Equal(Tick(100), snapshot.TimeTicks, "invalid future timestamps do not advance accepted clock");
            Equal(2L, snapshot.SessionVolume, "invalid future executions do not alter volume");
            Assert(snapshot.Frames.All(f => f.ToTicks <= Tick(100)), "no invented far-future frame");
            Near(0, Exposure(snapshot, 400, true), "invalid input cannot mature old depth");
            Assert(snapshot.InvalidEvents >= 3, "each malformed/overflow event diagnosed");
        });
        Test("Invalidating one book side preserves the other side's observed persistence", () =>
        {
            var engine = Engine(100, 500);
            engine.Depth(At(0), BookSide.Bid, DepthMutation.Insert, 0, 100, 10);
            engine.Depth(At(0), BookSide.Ask, DepthMutation.Insert, 0, 100.25, 20);
            engine.Depth(At(500), BookSide.Bid, DepthMutation.Update, 5, 99, 9);
            engine.Advance(At(1000)); var snapshot = engine.Snapshot();
            Equal(0, snapshot.Bids.Length, "invalid side cleared"); Equal(1, snapshot.Asks.Length, "opposite side retained");
            Near(20000, Exposure(snapshot, 401, false), "valid ask remains observed through side reset");
            Near(10000, Exposure(snapshot, 401, true), "valid ask age not reset by unrelated bid corruption");
            Near(0, Exposure(snapshot, 400, false, 500, 1000), "invalid bid never extends after reset");
        });
    }

    private static EngineSnapshot VisualSnapshot(params TradeRecord[] trades) => new() { TickSize = .25, Trades = trades };
    private static TradeRecord Print(double milliseconds, long priceTick, long volume, TradeSide side, long sequence = 0)
        => new(Tick(milliseconds), priceTick, volume, side, sequence, 0);
    private static Viewport View(int width = 1000, double from = 0, double to = 1000, double min = 390, double max = 420)
        => new(Tick(from), Tick(to), min, max, width, 600);
    private static DisplaySettings Display(ClusterMode mode = ClusterMode.Pixel) => new() { Clustering = mode, UsePersistence = false, MinimumLiquidity = 0, MinimumTradeSize = 1, MinimumBubbleVolume = 1, HeatColumnPixels = 1 };

    private static void RegisterVisualTests()
    {
        Test("All clustering modes conserve executed buy sell unknown totals at multiple zooms", () =>
        {
            var random = new Random(191); var trades = Enumerable.Range(0, 300).Select(i => Print(i * 3, 400 + random.Next(6), random.Next(1, 20), (TradeSide)(i % 3), i)).ToArray();
            var snapshot = VisualSnapshot(trades);
            foreach (var mode in Enum.GetValues<ClusterMode>()) foreach (var width in new[] { 1, 80, 1500 })
            {
                var settings = Display(mode); settings.ClusterVolume = 100; settings.ClusterMilliseconds = 100;
                var scene = Visualization.Build(snapshot, View(width), settings);
                Equal(trades.Sum(t => t.Volume), scene.Bubbles.Sum(b => b.Total), $"{mode} width{width} total");
                Equal(trades.Where(t => t.Side == TradeSide.Buy).Sum(t => t.Volume), scene.Bubbles.Sum(b => b.Buy), "buy conservation");
                Equal(trades.Where(t => t.Side == TradeSide.Sell).Sum(t => t.Volume), scene.Bubbles.Sum(b => b.Sell), "sell conservation");
                Equal(trades.Where(t => t.Side == TradeSide.Unknown).Sum(t => t.Volume), scene.Bubbles.Sum(b => b.Unknown), "unknown conservation");
                Equal(trades.Sum(t => t.Volume), scene.VisibleProfile.Sum(p => p.Total), "profile unaffected by zoom");
                Assert(scene.Bubbles.All(b => double.IsFinite(b.X) && double.IsFinite(b.Y) && double.IsFinite(b.Radius) && b.Radius >= 0), "finite bubbles");
            }
        });
        Test("Single pixel dot uses volume-weighted time and price without timestamp precision loss", () =>
        {
            var snapshot = VisualSnapshot(Print(100, 400, 2, TradeSide.Buy), Print(300, 408, 6, TradeSide.Sell, 1));
            var scene = Visualization.Build(snapshot, View(1), Display()); var bubble = scene.Bubbles.Single();
            Equal(8L, bubble.Total, "total"); Equal(2L, bubble.Buy, "buy"); Equal(6L, bubble.Sell, "sell");
            Near(406, bubble.PriceTick, "VWAP tick"); Assert(Math.Abs(bubble.TimeTicks - Tick(250)) <= 1, "weighted timestamp within one .NET tick");
        });
        Test("Zoom changes grouping while preserving all underlying prints", () =>
        {
            var snapshot = VisualSnapshot(Enumerable.Range(0, 10).Select(i => Print(20 + i * 50, 400, 1, TradeSide.Buy, i)).ToArray());
            var settings = Display(); settings.MinimumBubbleVolume = 10;
            Equal(1, Visualization.Build(snapshot, View(1), settings).Bubbles.Length, "zoomed out aggregate meets threshold");
            Equal(0, Visualization.Build(snapshot, View(1000), settings).Bubbles.Length, "zoomed in individual dots below threshold");
            Equal(10, snapshot.Trades.Length, "raw event collection retained");
        });
        Test("Minimum individual trade size differs from aggregate displayed volume", () =>
        {
            var snapshot = VisualSnapshot(Enumerable.Range(0, 10).Select(i => Print(20 + i, 400, 1, TradeSide.Buy, i)).ToArray());
            var settings = Display(); settings.MinimumBubbleVolume = 10;
            var aggregate = Visualization.Build(snapshot, View(1), settings); Equal(10L, aggregate.Bubbles.Sum(b => b.Total), "aggregate threshold");
            settings.MinimumTradeSize = 2; var individual = Visualization.Build(snapshot, View(1), settings);
            Equal(0, individual.Bubbles.Length, "individual prints excluded"); Equal(10L, individual.VisibleProfile.Sum(p => p.Total), "profile keeps all executed volume");
        });
        Test("Zero delta is retained as nonzero total with zero delta radius", () =>
        {
            var snapshot = VisualSnapshot(Print(100, 400, 4, TradeSide.Buy), Print(101, 400, 4, TradeSide.Sell, 1));
            var settings = Display(); settings.DeltaBubbles = true;
            var scene = Visualization.Build(snapshot, View(1), settings); var bubble = scene.Bubbles.Single();
            Equal(8L, bubble.Total, "executed volume"); Equal(0L, bubble.Delta, "balanced delta"); Near(0, bubble.Radius, "zero delta not plotted as positive-size delta");
            Equal(8L, scene.VisibleProfile.Sum(p => p.Total), "profile kept");
        });
        Test("One-contract delta survives very large balanced buy and sell totals", () =>
        {
            long small = long.MaxValue / 2, large = small + 1;
            foreach (bool buyLarger in new[] { false, true })
            {
                var snapshot = VisualSnapshot(Print(100, 400, buyLarger ? large : small, TradeSide.Buy),
                    Print(101, 400, buyLarger ? small : large, TradeSide.Sell, 1));
                var settings = Display(); settings.DeltaBubbles = true;
                var bubble = Visualization.Build(snapshot, View(1), settings).Bubbles.Single();
                Equal(long.MaxValue, bubble.Total, "full volume still represented exactly");
                Equal(buyLarger ? 1L : -1L, bubble.Delta, "one-contract signed difference");
                Assert(double.IsFinite(bubble.Radius) && bubble.Radius > 0, "integer delta must not disappear through double rounding");
            }
        });
        Test("Whole-trade volume clustering documents and conserves overshoot", () =>
        {
            var snapshot = VisualSnapshot(new[] { 20L, 15, 35, 270, 10 }.Select((q, i) => Print(100 + i, 400, q, TradeSide.Buy, i)).ToArray());
            var settings = Display(ClusterMode.Volume); settings.ClusterVolume = 100;
            var scene = Visualization.Build(snapshot, View(), settings);
            Assert(scene.Bubbles.Select(b => b.Total).SequenceEqual(new long[] { 340, 10 }), "closing trade remains whole; explicit difference from Bookmap splitting");
        });
        Test("Price and aggressor clustering respect contiguous runs", () =>
        {
            var snapshot = VisualSnapshot(Print(100, 400, 10, TradeSide.Buy), Print(101, 400, 5, TradeSide.Buy, 1), Print(102, 400, 4, TradeSide.Sell, 2), Print(103, 401, 6, TradeSide.Sell, 3), Print(104, 400, 8, TradeSide.Sell, 4));
            var byPrice = Visualization.Build(snapshot, View(), Display(ClusterMode.Price));
            Assert(byPrice.Bubbles.Select(b => b.Total).SequenceEqual(new long[] { 19, 6, 8 }), "price runs");
            var bySide = Visualization.Build(snapshot, View(), Display(ClusterMode.PriceAndSide));
            Assert(bySide.Bubbles.Select(b => b.Total).SequenceEqual(new long[] { 15, 4, 6, 8 }), "price+side runs");
        });
        Test("Half-open time viewport counts each boundary trade exactly once", () =>
        {
            var snapshot = VisualSnapshot(Print(0, 400, 1, TradeSide.Buy), Print(500, 400, 2, TradeSide.Buy, 1), Print(1000, 400, 4, TradeSide.Buy, 2));
            var left = Visualization.Build(snapshot, View(100, 0, 500), Display()); var right = Visualization.Build(snapshot, View(100, 500, 1000), Display());
            Equal(1L, left.Bubbles.Sum(b => b.Total), "left boundary inclusion"); Equal(2L, right.Bubbles.Sum(b => b.Total), "right half excludes1000");
        });
        Test("All same-timestamp prices survive pixel aggregation and visible profile", () =>
        {
            var snapshot = VisualSnapshot(Print(100, 400, 1, TradeSide.Buy), Print(100, 401, 2, TradeSide.Sell, 1), Print(100, 402, 3, TradeSide.Unknown, 2));
            var scene = Visualization.Build(snapshot, View(), Display());
            Equal(6L, scene.Bubbles.Sum(b => b.Total), "no timestamp-key overwrite"); Equal(3, scene.VisibleProfile.Length, "profile price distinctions");
        });
        Test("Heatmap duration weighting conserves resting quantity units", () =>
        {
            var snapshot = VisualSnapshot(); snapshot.Frames = new[] {
                new HeatFrame(Tick(0), Tick(100), 0, new[] { new HeatCell(400, BookSide.Bid, 10, 5) }, 400, 401),
                new HeatFrame(Tick(100), Tick(400), 0, new[] { new HeatCell(400, BookSide.Bid, 30, 15) }, 400, 401)
            };
            var scene = Visualization.Build(snapshot, View(1, 0, 400), Display());
            Assert(scene.Heat.Length > 0, "heat rendered");
            var area = scene.Heat.Sum(t => t.Width * t.Height);
            Near(25, scene.Heat.Sum(t => t.Quantity * t.Width * t.Height) / area, "weighted resting quantity, not summed snapshots");
        });
        Test("Raw and level-persistence modes share data but show distinct quantities", () =>
        {
            var snapshot = VisualSnapshot(); snapshot.Frames = new[] { new HeatFrame(Tick(0), Tick(1000), 0, new[] { new HeatCell(400, BookSide.Bid, 200, 10) }, 400, 401) };
            var rawSettings = Display(); var persistentSettings = Display(); persistentSettings.UsePersistence = true;
            var raw = Visualization.Build(snapshot, View(10), rawSettings); var filtered = Visualization.Build(snapshot, View(10), persistentSettings);
            Assert(raw.Heat.All(t => Math.Abs(t.Quantity - 200) < 1e-8), "raw200"); Assert(filtered.Heat.All(t => Math.Abs(t.Quantity - 10) < 1e-8), "persistent10");
            Equal(200d, snapshot.Frames[0].Cells[0].RawQuantity, "snapshot unchanged");
        });
        Test("Disconnected heat gap survives even when intervals share one pixel", () =>
        {
            var snapshot = VisualSnapshot(); snapshot.Frames = new[] {
                new HeatFrame(Tick(0), Tick(250), 0, new[] { new HeatCell(400, BookSide.Bid, 10, 10) }, 400, 401),
                new HeatFrame(Tick(750), Tick(1000), 1, new[] { new HeatCell(400, BookSide.Bid, 10, 10) }, 400, 401)
            };
            foreach (var width in new[] { 1, 100 })
            {
                var scene = Visualization.Build(snapshot, View(width), Display());
                Assert(scene.Heat.Length > 0, "known intervals render");
                Assert(scene.Heat.All(t => !(t.X < width * .5 && t.X + t.Width > width * .5)), "no tile paints across gap midpoint");
            }
        });
        Test("Heat tiles stay finite and clipped at viewport edges", () =>
        {
            var snapshot = VisualSnapshot(); snapshot.Frames = new[] { new HeatFrame(Tick(-100), Tick(1500), 0, new[] { new HeatCell(390, BookSide.Bid, 10, 10), new HeatCell(400, BookSide.Bid, 20, 20), new HeatCell(420, BookSide.Ask, 30, 30) }, 400, 401) };
            var scene = Visualization.Build(snapshot, View(123), Display());
            Assert(scene.Heat.Length > 0, "edge heat present");
            Assert(scene.Heat.All(t => float.IsFinite(t.X) && float.IsFinite(t.Y) && t.Width > 0 && t.Height > 0 && t.X >= -1e-5 && t.Y >= -1e-5 && t.X + t.Width <= 123.001 && t.Y + t.Height <= 600.001), "finite clipped tile extents");
        });
        Test("Classic heatmap saturates red and grayscale channels agree", () =>
        {
            var hot = Visualization.Color(1, HeatPalette.Classic); var cold = Visualization.Color(0, HeatPalette.Classic);
            Assert(hot.R > hot.G && hot.R > hot.B && hot.R > 200, "hottest red, not white");
            Assert(cold.R + cold.G + cold.B < 150, "dark low end");
            foreach (var level in new[] { 0d, .1, .5, .9, 1d }) { var c = Visualization.Color(level, HeatPalette.Grayscale); Equal(c.R, c.G, "gray RG"); Equal(c.G, c.B, "gray GB"); }
        });
        Test("Invalid render dimensions and settings fail explicitly", () =>
        {
            var snapshot = VisualSnapshot();
            Throws<ArgumentException>(() => Visualization.Build(snapshot, View(0), Display()), "zero width");
            Throws<ArgumentException>(() => Visualization.Build(snapshot, View(100, 100, 100), Display()), "zero time span");
            Throws<ArgumentException>(() => Visualization.Build(snapshot, View(100, 0, 1000, double.NaN, 420), Display()), "NaN price scale");
            var bad = Display(); bad.Gamma = double.NaN;
            Throws<ArgumentException>(() => Visualization.Build(snapshot, View(), bad), "NaN gamma");
        });
        Test("Rendering is deterministic and does not mutate input or totals", () =>
        {
            var snapshot = VisualSnapshot(Print(100, 400, 7, TradeSide.Buy), Print(200, 401, 3, TradeSide.Sell, 1));
            var first = Visualization.Build(snapshot, View(), Display(ClusterMode.Smart)); var second = Visualization.Build(snapshot, View(), Display(ClusterMode.Smart));
            Assert(first.Bubbles.Select(b => (b.TimeTicks, b.PriceTick, b.Buy, b.Sell, b.Unknown, b.Radius)).SequenceEqual(second.Bubbles.Select(b => (b.TimeTicks, b.PriceTick, b.Buy, b.Sell, b.Unknown, b.Radius))), "stable scene");
            Equal(2, snapshot.Trades.Length, "input retained"); Equal(10L, snapshot.Trades.Sum(t => t.Volume), "input totals");
        });
    }
}
