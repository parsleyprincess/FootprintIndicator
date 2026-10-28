using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using FootprintIndicator.Core;

public static class HistoryStoreTests
{
    private const string CaptureKey = "GC 12-26|CME|captured Δ|../../escape";
    private static readonly long Origin = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc).Ticks;

    public static void Run(Action<bool, string> assert)
    {
        string directory = Path.Combine(Path.GetTempPath(), "footprint-history-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = HistoryStore.PathFor(directory, CaptureKey);
            assert(Path.GetDirectoryName(path) == directory && Path.GetFileNameWithoutExtension(path).Length == 64,
                "Capture names must remain within the selected directory even with path syntax in their context.");
            assert(path == HistoryStore.PathFor(directory, CaptureKey) && path != HistoryStore.PathFor(directory, CaptureKey + "another"),
                "History paths must be stable and distinct for different instrument/context keys.");
            Expect<ArgumentException>(() => HistoryStore.PathFor(directory, " "), assert, "Empty capture identity must be rejected.");
            Expect<ArgumentException>(() => HistoryStore.Write(Path.Combine(directory, "notes.txt"), CaptureKey, Fixture()), assert,
                "Writing must require a capture extension.");

            var original = Fixture();
            assert(HistoryStore.Write(path, CaptureKey, original), "The first capture must be persisted.");
            Compare(original, HistoryStore.Read(path, CaptureKey, original.TickSize), assert);
            assert(new FileInfo(path).Length == 168 + Encoding.UTF8.GetByteCount(CaptureKey) +
                original.Frames.Sum(f => 26 + (f.BestBidTick.HasValue ? 8 : 0) + (f.BestAskTick.HasValue ? 8 : 0) + 25 * f.Cells.Length) +
                45 * original.Trades.Length + 32 * original.Profile.Length, "The archive-size budget must match the actual binary file.");

            foreach (double tickSize in new[] { .25, .1, .00005, .0001, .5 })
            {
                var instrument = Fixture(); instrument.TickSize = tickSize;
                string instrumentPath = HistoryStore.PathFor(directory, CaptureKey + tickSize.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                HistoryStore.Write(instrumentPath, CaptureKey, instrument);
                Compare(instrument, HistoryStore.Read(instrumentPath, CaptureKey, tickSize), assert);
            }
            Expect<InvalidDataException>(() => HistoryStore.Read(path, "Different instrument", original.TickSize), assert,
                "Another instrument/context must not load this archive.");
            Expect<InvalidDataException>(() => HistoryStore.Read(path, CaptureKey, .25), assert,
                "Tick-size mismatches must be rejected exactly.");
            Expect<FileNotFoundException>(() => HistoryStore.Read(Path.Combine(directory, "missing.fph"), CaptureKey, original.TickSize), assert,
                "Missing history must retain the ordinary missing-file contract.");

            BudgetTests(path, original, assert);
            CorruptionTests(directory, path, original, assert);
            ReplacementTests(directory, path, original, assert);
            ValidationTests(directory, assert);
            IsolationAndEmptySessionTests(directory, assert);
            ConcurrentTests(directory, assert);
            assert(Directory.GetFiles(directory, "*.tmp").Length == 0, "Successful and failed saves must not leave temporary archives.");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static EngineSnapshot Fixture()
    {
        const long large = 9007199254740993L;
        return new EngineSnapshot
        {
            TickSize = .00005, Version = 1234, TimeTicks = Origin + 101, SessionStartTicks = Origin - 17,
            DepthEvents = 711, TradeEvents = 103, InvalidEvents = 13, LateEvents = 17, DroppedFrames = 19, DroppedTrades = 23,
            CumulativeDelta = large - 7, SessionVolume = large + 18, Epoch = 9, SessionVwap = -1.2345678901234567,
            Connected = true, HasDepth = true, Status = "Live status must not be restored",
            Bids = new[] { new DepthLevel(-24692, 15) }, Asks = new[] { new DepthLevel(-24691, 20) },
            Frames = new[]
            {
                new HeatFrame(Origin, Origin + 13, 3, new[] { new HeatCell(-24692, BookSide.Bid, 17.123456789012345, .12345678901234568),
                    new HeatCell(-24691, BookSide.Ask, 24.125, 24.125) }, -24692, -24691),
                new HeatFrame(Origin + 13, Origin + 27, 4, new[] { new HeatCell(0, BookSide.Bid, 1, 0) }, 0, null),
                new HeatFrame(Origin + 41, Origin + 63, 4, new[] { new HeatCell(long.MaxValue, BookSide.Ask, .1, .05) }, null, long.MaxValue),
                new HeatFrame(Origin + 63, Origin + 101, 9, Array.Empty<HeatCell>(), null, null)
            },
            Trades = new[]
            {
                new TradeRecord(Origin + 7, -24691, large, TradeSide.Buy, 101, large, 3),
                new TradeRecord(Origin + 17, -24692, 7, TradeSide.Sell, 102, large - 7, 4),
                new TradeRecord(Origin + 79, -24691, 11, TradeSide.Unknown, 103, large - 7, 9)
            },
            Profile = new[] { new ProfileLevel(-24692, 0, 7, 0), new ProfileLevel(-24691, large, 0, 11) }
        };
    }

    private static void Compare(EngineSnapshot expected, EngineSnapshot actual, Action<bool, string> assert)
    {
        assert(expected.Version == actual.Version && expected.TimeTicks == actual.TimeTicks && expected.SessionStartTicks == actual.SessionStartTicks &&
            expected.DepthEvents == actual.DepthEvents && expected.TradeEvents == actual.TradeEvents && expected.InvalidEvents == actual.InvalidEvents &&
            expected.LateEvents == actual.LateEvents && expected.DroppedFrames == actual.DroppedFrames && expected.DroppedTrades == actual.DroppedTrades &&
            expected.CumulativeDelta == actual.CumulativeDelta && expected.SessionVolume == actual.SessionVolume && expected.Epoch == actual.Epoch &&
            Bits(expected.TickSize) == Bits(actual.TickSize) && Bits(expected.SessionVwap) == Bits(actual.SessionVwap),
            "Every snapshot counter, timestamp, epoch and floating-point field must round-trip exactly.");
        assert(!actual.Connected && !actual.HasDepth && actual.Bids.Length == 0 && actual.Asks.Length == 0 && actual.Status.Contains("fresh depth"),
            "Loaded captures must describe disconnected history with an empty live order book.");
        assert(expected.Frames.Length == actual.Frames.Length && expected.Trades.Length == actual.Trades.Length && expected.Profile.Length == actual.Profile.Length,
            "All retained histories must round-trip without loss.");
        for (int i = 0; i < expected.Frames.Length; i++)
        {
            var left = expected.Frames[i]; var right = actual.Frames[i];
            assert(left.FromTicks == right.FromTicks && left.ToTicks == right.ToTicks && left.Epoch == right.Epoch &&
                left.BestBidTick == right.BestBidTick && left.BestAskTick == right.BestAskTick && left.Cells.Length == right.Cells.Length,
                "Heat frames must preserve exact boundaries, gap epochs and nullable BBO prices.");
            for (int row = 0; row < left.Cells.Length; row++)
                assert(left.Cells[row].Tick == right.Cells[row].Tick && left.Cells[row].Side == right.Cells[row].Side &&
                    Bits(left.Cells[row].RawQuantity) == Bits(right.Cells[row].RawQuantity) && Bits(left.Cells[row].PersistentQuantity) == Bits(right.Cells[row].PersistentQuantity),
                    "Raw and persistent depth exposures must retain double precision and exact integer ticks.");
        }
        for (int i = 0; i < expected.Trades.Length; i++)
        {
            var left = expected.Trades[i]; var right = actual.Trades[i];
            assert(left.TimeTicks == right.TimeTicks && left.PriceTick == right.PriceTick && left.Volume == right.Volume && left.Side == right.Side &&
                left.Sequence == right.Sequence && left.CumulativeDelta == right.CumulativeDelta && left.Epoch == right.Epoch,
                "Executed trades must retain all integer fields, side and historical epoch without double conversion.");
        }
        for (int i = 0; i < expected.Profile.Length; i++)
        {
            var left = expected.Profile[i]; var right = actual.Profile[i];
            assert(left.Tick == right.Tick && left.Buy == right.Buy && left.Sell == right.Sell && left.Unknown == right.Unknown,
                "Session profiles must retain every captured volume bucket.");
        }
    }

    private static void BudgetTests(string path, EngineSnapshot snapshot, Action<bool, string> assert)
    {
        byte[] before = File.ReadAllBytes(path);
        foreach (var limit in new[]
        {
            new HistoryLimits { MaximumFileBytes = 256 }, new HistoryLimits { MaximumFrames = 1 },
            new HistoryLimits { MaximumTrades = 1 }, new HistoryLimits { MaximumProfileLevels = 1 }, new HistoryLimits { MaximumHeatCells = 1 }
        })
        {
            Expect<InvalidDataException>(() => HistoryStore.Read(path, CaptureKey, snapshot.TickSize, limit), assert,
                "Read must enforce each disk or allocation budget.");
            Expect<InvalidDataException>(() => HistoryStore.Write(path, CaptureKey, snapshot, limit), assert,
                "Write must reject captures beyond each configured budget.");
            assert(before.SequenceEqual(File.ReadAllBytes(path)), "Budget failures must preserve the previous capture byte for byte.");
        }
        Expect<ArgumentOutOfRangeException>(() => HistoryStore.Read(path, CaptureKey, snapshot.TickSize, new HistoryLimits { MaximumFrames = int.MaxValue }),
            assert, "Callers cannot disable the hard allocation ceiling.");
    }

    private static void CorruptionTests(string directory, string path, EngineSnapshot snapshot, Action<bool, string> assert)
    {
        byte[] valid = File.ReadAllBytes(path);
        string corruptPath = Path.Combine(directory, "corrupt.fph");
        foreach (int length in new[] { 0, 7, 28, valid.Length - 1, valid.Length - 32 })
        {
            File.WriteAllBytes(corruptPath, valid.Take(length).ToArray());
            Expect<InvalidDataException>(() => HistoryStore.Read(corruptPath, CaptureKey, snapshot.TickSize), assert,
                "Truncated archives must never produce partial history.");
        }
        foreach (int offset in new[] { 0, valid.Length / 2, valid.Length - 1 })
        {
            var changed = (byte[])valid.Clone(); changed[offset] ^= 1; File.WriteAllBytes(corruptPath, changed);
            Expect<InvalidDataException>(() => HistoryStore.Read(corruptPath, CaptureKey, snapshot.TickSize), assert,
                "Header, payload and digest bit corruption must all be rejected.");
        }
        int frameCountOffset = 124 + Encoding.UTF8.GetByteCount(CaptureKey);
        foreach (int count in new[] { -1, int.MaxValue, 1000000 })
        {
            var changed = (byte[])valid.Clone(); BitConverter.GetBytes(count).CopyTo(changed, frameCountOffset); Rehash(changed);
            File.WriteAllBytes(corruptPath, changed);
            Expect<InvalidDataException>(() => HistoryStore.Read(corruptPath, CaptureKey, snapshot.TickSize, new HistoryLimits { MaximumFrames = 1000000 }), assert,
                "Even correctly checksummed hostile counts must be rejected before allocating absent records.");
        }
        var future = (byte[])valid.Clone(); BitConverter.GetBytes(999).CopyTo(future, 8); Rehash(future); File.WriteAllBytes(corruptPath, future);
        Expect<InvalidDataException>(() => HistoryStore.Read(corruptPath, CaptureKey, snapshot.TickSize), assert,
            "Unsupported format versions must not be interpreted as current records.");
    }

    private static void ReplacementTests(string directory, string path, EngineSnapshot original, Action<bool, string> assert)
    {
        var newer = Fixture(); newer.TimeTicks++; newer.Version++;
        assert(HistoryStore.Write(path, CaptureKey, newer), "A later coherent snapshot must atomically replace the existing file.");
        byte[] current = File.ReadAllBytes(path);
        assert(!HistoryStore.Write(path, CaptureKey, original) && current.SequenceEqual(File.ReadAllBytes(path)),
            "A delayed worker must not overwrite a newer captured market timestamp.");
        string foreignPath = Path.Combine(directory, "foreign.fph");
        byte[] foreign = Encoding.UTF8.GetBytes("These are user notes, not a capture archive."); File.WriteAllBytes(foreignPath, foreign);
        Expect<InvalidDataException>(() => HistoryStore.Write(foreignPath, CaptureKey, original), assert, "Existing arbitrary files must be protected.");
        assert(foreign.SequenceEqual(File.ReadAllBytes(foreignPath)), "Rejected arbitrary files must remain byte-identical.");
        Expect<InvalidDataException>(() => HistoryStore.Write(path, "Other instrument", newer), assert, "Another capture identity must not replace an owned file.");
        var wrongTick = Fixture(); wrongTick.TickSize = .25;
        Expect<InvalidDataException>(() => HistoryStore.Write(path, CaptureKey, wrongTick), assert, "Another instrument tick size must not replace a capture.");
        assert(current.SequenceEqual(File.ReadAllBytes(path)), "Header ownership failures must preserve the existing archive.");

        int timeOffset = 32 + Encoding.UTF8.GetByteCount(CaptureKey);
        var corruptTime = (byte[])current.Clone(); BitConverter.GetBytes(Origin + TimeSpan.TicksPerDay).CopyTo(corruptTime, timeOffset);
        File.WriteAllBytes(path, corruptTime);
        assert(HistoryStore.Write(path, CaptureKey, newer), "A checksum-invalid future timestamp must not block future autosave recovery.");
        Compare(newer, HistoryStore.Read(path, CaptureKey, newer.TickSize), assert);

        using (var held = new FileStream(path + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Expect<IOException>(() => HistoryStore.Write(path, CaptureKey, newer), assert, "An independent writer's lock must reject overlapping writes safely.");
        Compare(newer, HistoryStore.Read(path, CaptureKey, newer.TickSize), assert);
    }

    private static void ValidationTests(string directory, Action<bool, string> assert)
    {
        string path = Path.Combine(directory, "invalid.fph");
        var changes = new Action<EngineSnapshot>[]
        {
            s => s.SessionVwap = double.NaN, s => s.SessionVwap = double.PositiveInfinity,
            s => s.SessionVolume++, s => s.CumulativeDelta++, s => s.TradeEvents = 3,
            s => s.Frames[0].Cells[0] = new HeatCell(1, BookSide.Bid, 1, 1.0000000001),
            s => s.Frames[0].Cells[0] = new HeatCell(1, BookSide.Bid, double.NaN, 0),
            s => s.Frames[0].Cells[1] = s.Frames[0].Cells[0], s => s.Frames[1] = s.Frames[0],
            s => s.Profile[1] = s.Profile[0], s => s.Trades[1] = s.Trades[0], s => s.TickSize = double.MaxValue
        };
        foreach (var change in changes)
        {
            var bad = Fixture(); change(bad);
            Expect<InvalidDataException>(() => HistoryStore.Write(path, CaptureKey, bad), assert,
                "Invalid accounting, nonfinite values, duplicate rows and restore-incompatible records must not be saved.");
            assert(!File.Exists(path), "Validation must finish before creating an archive.");
        }
    }

    private static void IsolationAndEmptySessionTests(string directory, Action<bool, string> assert)
    {
        var engine = new MarketEngine(new EngineSettings { TickSize = .25, FrameMilliseconds = 100, PersistenceMilliseconds = 0 });
        string path = Path.Combine(directory, "engine.fph");
        var empty = engine.Snapshot();
        HistoryStore.Write(path, CaptureKey, empty);
        assert(double.IsNaN(HistoryStore.Read(path, CaptureKey, .25).SessionVwap), "An empty captured session must safely retain its undefined VWAP.");
        empty.SessionVwap = double.NegativeInfinity;
        Expect<InvalidDataException>(() => HistoryStore.Write(path, CaptureKey, empty), assert, "Infinity remains invalid even for an empty session.");
        engine.DepthSnapshot(new DateTime(Origin), new[] { new DepthLevel(100, 20) }, new[] { new DepthLevel(101, 30) });
        engine.Trade(new DateTime(Origin + 500000), 25.25, 5, 25, 25.25);
        engine.Advance(new DateTime(Origin + 1000000));
        var captured = engine.Snapshot(); HistoryStore.Write(path, CaptureKey, captured);
        var loaded = HistoryStore.Read(path, CaptureKey, .25);
        loaded.Frames[0].Cells[0] = new HeatCell(999, BookSide.Bid, 999, 999);
        loaded.Trades[0] = new TradeRecord(Origin, 999, 999, TradeSide.Unknown, 1, 0);
        loaded.Profile[0] = new ProfileLevel(999, 999, 0, 0);
        Compare(captured, HistoryStore.Read(path, CaptureKey, .25), assert);
        assert(engine.Snapshot().Frames.All(f => f.Cells.All(c => c.Tick != 999)) && engine.Snapshot().SessionVolume == 5,
            "Mutating loaded arrays must not alter the capturing engine or the saved archive.");
        var restored = new MarketEngine(new EngineSettings { TickSize = .25 });
        restored.RestoreHistory(HistoryStore.Read(path, CaptureKey, .25));
        assert(restored.Snapshot().SessionVolume == 5 && !restored.Snapshot().HasDepth && !restored.Snapshot().Connected,
            "Persisted engine output must be restorable without reviving captured live orders.");
    }

    private static void ConcurrentTests(string directory, Action<bool, string> assert)
    {
        string path = Path.Combine(directory, "concurrent.fph");
        HistoryStore.Write(path, CaptureKey, Fixture());
        var failures = new ConcurrentQueue<Exception>();
        using var start = new ManualResetEventSlim(false);
        var reader = Task.Run(() =>
        {
            start.Wait();
            try
            {
                for (int i = 0; i < 100; i++)
                {
                    var observed = HistoryStore.Read(path, CaptureKey, .00005);
                    if (observed.SessionVolume != Fixture().SessionVolume || observed.Frames.Length != 4)
                        throw new InvalidOperationException("Concurrent readers observed a partial archive.");
                }
            }
            catch (Exception error) { failures.Enqueue(error); }
        });
        var writers = Enumerable.Range(1, 12).Select(index => Task.Run(() =>
        {
            start.Wait();
            try { var value = Fixture(); value.TimeTicks += index; HistoryStore.Write(path, CaptureKey, value); }
            catch (Exception error) { failures.Enqueue(error); }
        })).ToArray();
        start.Set(); Task.WaitAll(writers.Append(reader).ToArray());
        assert(failures.IsEmpty, "Concurrent writers and readers must see complete checksummed archives: " + string.Join("; ", failures.Select(e => e.Message)));
        assert(HistoryStore.Read(path, CaptureKey, .00005).TimeTicks == Fixture().TimeTicks + 12,
            "Competing workers must preserve the newest market timestamp regardless of scheduling.");
    }

    private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);
    private static void Rehash(byte[] bytes) => SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)).CopyTo(bytes, bytes.Length - 32);
    private static void Expect<T>(Action action, Action<bool, string> assert, string message) where T : Exception
    {
        try { action(); }
        catch (T) { assert(true, message); return; }
        assert(false, message + " Expected " + typeof(T).Name + ".");
    }
}
