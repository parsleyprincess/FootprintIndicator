using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FootprintIndicator.Core
{
    /// <summary>Allocation and disk budgets, applied before allocating archive arrays.</summary>
    public sealed class HistoryLimits
    {
        public long MaximumFileBytes = 128L * 1024 * 1024;
        public int MaximumFrames = 100000, MaximumTrades = 1000000;
        public int MaximumProfileLevels = 200000, MaximumHeatCells = 4000000;
    }

    /// <summary>
    /// Versioned, checksummed capture storage. Stores observed history, never a live
    /// book to resume. A coherent detached engine snapshot is required on write.
    /// Disk I/O belongs on a background worker, not in market-data callbacks.
    /// </summary>
    public static class HistoryStore
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("FPMHIST1");
        private static readonly object WriteGate = new object();
        private const int FormatVersion = 1, HashBytes = 32, MaximumKeyBytes = 4096;

        /// <summary>Stable filename; instrument/context text cannot become a path component.</summary>
        public static string PathFor(string directory, string captureKey)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("History directory is required.", "directory");
            byte[] key = Key(captureKey);
            byte[] digest;
            using (SHA256 hash = SHA256.Create()) digest = hash.ComputeHash(key);
            var name = new StringBuilder(64);
            foreach (byte value in digest) name.Append(value.ToString("x2"));
            return Path.Combine(Path.GetFullPath(directory), name + ".fph");
        }

        /// <summary>
        /// Atomically replace an owned capture. Returns false when the destination
        /// already contains a later market timestamp. Other writers are serialized
        /// in process and excluded across processes by an adjacent lock file.
        /// </summary>
        public static bool Write(string path, string captureKey, EngineSnapshot snapshot, HistoryLimits limits = null)
        {
            byte[] key = Key(captureKey);
            HistoryLimits budget = Budget(limits);
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            Validate(snapshot, budget);
            long size = EncodedSize(snapshot, key.Length);
            if (size > budget.MaximumFileBytes) throw new InvalidDataException("History archive exceeds the configured file-size budget.");
            string target = Target(path), directory = Path.GetDirectoryName(target);
            Directory.CreateDirectory(directory);
            lock (WriteGate)
            using (var ownershipLock = new FileStream(target + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                if (File.Exists(target))
                {
                    // Do not overwrite arbitrary user files or a different capture.
                    // An intact owned header permits replacing an old corrupt payload;
                    // Read still reports corruption explicitly to the caller first.
                    using (var existing = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                    using (var reader = new BinaryReader(existing, Encoding.UTF8, true))
                    {
                        Header(reader, captureKey, snapshot.TickSize);
                        reader.ReadInt64(); // engine version
                        long existingTime = reader.ReadInt64();
                        if (existingTime > snapshot.TimeTicks && Time(existingTime))
                        {
                            if (existing.Length > budget.MaximumFileBytes)
                                throw new InvalidDataException("Existing history exceeds the configured file-size budget; it was preserved.");
                            // A corrupt timestamp must not indefinitely block repair
                            // of an otherwise recognizable capture owned by this key.
                            if (ChecksumMatches(existing)) return false;
                        }
                    }
                }
                string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                    {
                        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                        {
                            writer.Write(Magic); writer.Write(FormatVersion);
                            writer.Write(key.Length); writer.Write(key); writer.Write(snapshot.TickSize);
                            WriteSnapshot(writer, snapshot);
                            writer.Flush();
                        }
                        long payloadLength = stream.Length;
                        byte[] digest = Hash(stream, payloadLength);
                        stream.Position = payloadLength; stream.Write(digest, 0, digest.Length);
                        if (stream.Length != size) throw new InvalidDataException("History serialization length mismatch.");
                        stream.Flush(true);
                    }
                    if (File.Exists(target)) File.Replace(temporary, target, null);
                    else File.Move(temporary, target);
                    return true;
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }

        /// <summary>
        /// Load detached history. Connected/HasDepth are false and Bids/Asks empty,
        /// irrespective of the captured connection state. Corrupt/mismatched files
        /// throw; missing files use the ordinary FileNotFoundException contract.
        /// </summary>
        public static EngineSnapshot Read(string path, string expectedCaptureKey, double expectedTickSize, HistoryLimits limits = null)
        {
            Key(expectedCaptureKey);
            if (!Finite(expectedTickSize) || expectedTickSize <= 0) throw new ArgumentOutOfRangeException("expectedTickSize");
            HistoryLimits budget = Budget(limits);
            using (var stream = new FileStream(Target(path), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                if (stream.Length < Magic.Length + 4 + 4 + 8 + HashBytes || stream.Length > budget.MaximumFileBytes)
                    throw new InvalidDataException("History archive is truncated or exceeds the configured file-size budget.");
                long payloadLength = stream.Length - HashBytes;
                if (!ChecksumMatches(stream)) throw new InvalidDataException("History checksum failed; the archive was not loaded.");
                stream.Position = 0;
                using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
                {
                    Header(reader, expectedCaptureKey, expectedTickSize);
                    EngineSnapshot snapshot = ReadSnapshot(reader, budget, expectedTickSize);
                    if (stream.Position != payloadLength) throw new InvalidDataException("History archive has unexpected trailing or missing fields.");
                    Validate(snapshot, budget);
                    return snapshot;
                }
            }
        }

        private static void WriteSnapshot(BinaryWriter writer, EngineSnapshot s)
        {
            writer.Write(s.Version); writer.Write(s.TimeTicks); writer.Write(s.SessionStartTicks);
            writer.Write(s.DepthEvents); writer.Write(s.TradeEvents); writer.Write(s.InvalidEvents);
            writer.Write(s.LateEvents); writer.Write(s.DroppedFrames); writer.Write(s.DroppedTrades);
            writer.Write(s.CumulativeDelta); writer.Write(s.SessionVolume); writer.Write(s.Epoch); writer.Write(s.SessionVwap);
            writer.Write(s.Frames.Length);
            foreach (HeatFrame frame in s.Frames)
            {
                writer.Write(frame.FromTicks); writer.Write(frame.ToTicks); writer.Write(frame.Epoch);
                NullableTick(writer, frame.BestBidTick); NullableTick(writer, frame.BestAskTick);
                writer.Write(frame.Cells.Length);
                foreach (HeatCell cell in frame.Cells)
                { writer.Write(cell.Tick); writer.Write((byte)cell.Side); writer.Write(cell.RawQuantity); writer.Write(cell.PersistentQuantity); }
            }
            writer.Write(s.Trades.Length);
            foreach (TradeRecord trade in s.Trades)
            {
                writer.Write(trade.TimeTicks); writer.Write(trade.PriceTick); writer.Write(trade.Volume);
                writer.Write(trade.Sequence); writer.Write(trade.CumulativeDelta); writer.Write(trade.Epoch); writer.Write((byte)trade.Side);
            }
            writer.Write(s.Profile.Length);
            foreach (ProfileLevel level in s.Profile)
            { writer.Write(level.Tick); writer.Write(level.Buy); writer.Write(level.Sell); writer.Write(level.Unknown); }
        }

        private static EngineSnapshot ReadSnapshot(BinaryReader reader, HistoryLimits budget, double tickSize)
        {
            var s = new EngineSnapshot
            {
                Version = reader.ReadInt64(), TimeTicks = reader.ReadInt64(), SessionStartTicks = reader.ReadInt64(),
                DepthEvents = reader.ReadInt64(), TradeEvents = reader.ReadInt64(), InvalidEvents = reader.ReadInt64(),
                LateEvents = reader.ReadInt64(), DroppedFrames = reader.ReadInt64(), DroppedTrades = reader.ReadInt64(),
                CumulativeDelta = reader.ReadInt64(), SessionVolume = reader.ReadInt64(), Epoch = reader.ReadInt32(), SessionVwap = reader.ReadDouble(),
                TickSize = tickSize, Connected = false, HasDepth = false, Status = "Saved capture; waiting for fresh depth"
            };
            s.Frames = new HeatFrame[Count(reader, budget.MaximumFrames, "frame", 26)];
            long cells = 0;
            for (int i = 0; i < s.Frames.Length; i++)
            {
                long from = reader.ReadInt64(), to = reader.ReadInt64(); int epoch = reader.ReadInt32();
                long? bid = NullableTick(reader), ask = NullableTick(reader);
                int count = Count(reader, budget.MaximumHeatCells, "heat cell", 25);
                cells += count;
                if (cells > budget.MaximumHeatCells) throw new InvalidDataException("History heat-cell budget exceeded.");
                var rows = new HeatCell[count];
                for (int row = 0; row < count; row++) rows[row] = new HeatCell(reader.ReadInt64(), (BookSide)reader.ReadByte(), reader.ReadDouble(), reader.ReadDouble());
                s.Frames[i] = new HeatFrame(from, to, epoch, rows, bid, ask);
            }
            s.Trades = new TradeRecord[Count(reader, budget.MaximumTrades, "trade", 45)];
            for (int i = 0; i < s.Trades.Length; i++)
            {
                long time = reader.ReadInt64(), tick = reader.ReadInt64(), volume = reader.ReadInt64();
                long sequence = reader.ReadInt64(), delta = reader.ReadInt64(); int epoch = reader.ReadInt32();
                s.Trades[i] = new TradeRecord(time, tick, volume, (TradeSide)reader.ReadByte(), sequence, delta, epoch);
            }
            s.Profile = new ProfileLevel[Count(reader, budget.MaximumProfileLevels, "profile level", 32)];
            for (int i = 0; i < s.Profile.Length; i++)
                s.Profile[i] = new ProfileLevel(reader.ReadInt64(), reader.ReadInt64(), reader.ReadInt64(), reader.ReadInt64());
            return s;
        }

        private static void Validate(EngineSnapshot s, HistoryLimits budget)
        {
            if (!Finite(s.TickSize) || s.TickSize <= 0 || !Time(s.TimeTicks) || !Time(s.SessionStartTicks) || s.SessionStartTicks > s.TimeTicks ||
                s.Version < 0 || s.DepthEvents < 0 || s.TradeEvents < 0 || s.InvalidEvents < 0 || s.LateEvents < 0 ||
                s.DroppedFrames < 0 || s.DroppedTrades < 0 || s.SessionVolume < 0 || s.Epoch < 0 ||
                (s.SessionVolume > 0 ? !Finite(s.SessionVwap) : double.IsInfinity(s.SessionVwap)))
                throw new InvalidDataException("History contains invalid snapshot metadata.");
            if (s.Frames == null || s.Trades == null || s.Profile == null || s.Frames.Length > budget.MaximumFrames ||
                s.Trades.Length > budget.MaximumTrades || s.Profile.Length > budget.MaximumProfileLevels || s.Trades.LongLength > s.TradeEvents)
                throw new InvalidDataException("History arrays are missing or exceed configured budgets.");
            long end = 0, cells = 0;
            foreach (HeatFrame frame in s.Frames)
            {
                if (frame == null || !Time(frame.FromTicks) || frame.ToTicks <= frame.FromTicks || frame.ToTicks > s.TimeTicks ||
                    frame.FromTicks < end || frame.Epoch < 0 || frame.Cells == null)
                    throw new InvalidDataException("History contains invalid, overlapping or future heat frames.");
                end = frame.ToTicks; cells += frame.Cells.Length;
                if (cells > budget.MaximumHeatCells) throw new InvalidDataException("History heat-cell budget exceeded.");
                var bidTicks = new HashSet<long>(); var askTicks = new HashSet<long>();
                foreach (HeatCell cell in frame.Cells)
                {
                    if (cell == null || (cell.Side != BookSide.Bid && cell.Side != BookSide.Ask) ||
                        !Finite(cell.Tick * s.TickSize) || !Finite(cell.RawQuantity) || !Finite(cell.PersistentQuantity) ||
                        cell.RawQuantity < 0 || cell.PersistentQuantity < 0 || cell.PersistentQuantity > cell.RawQuantity ||
                        !(cell.Side == BookSide.Bid ? bidTicks : askTicks).Add(cell.Tick))
                        throw new InvalidDataException("History contains invalid or duplicate heat cells.");
                }
            }
            long previousTime = 0, previousSequence = 0;
            foreach (TradeRecord trade in s.Trades)
            {
                if (trade == null || !Time(trade.TimeTicks) || trade.TimeTicks > s.TimeTicks || trade.TimeTicks < previousTime ||
                    trade.Volume <= 0 || trade.Sequence <= previousSequence || trade.Sequence > s.TradeEvents || trade.Epoch < 0 ||
                    !Finite(trade.PriceTick * s.TickSize) ||
                    (trade.Side != TradeSide.Buy && trade.Side != TradeSide.Sell && trade.Side != TradeSide.Unknown))
                    throw new InvalidDataException("History contains invalid, unordered or future executions.");
                previousTime = trade.TimeTicks; previousSequence = trade.Sequence;
            }
            long total = 0, buy = 0, sell = 0;
            var prices = new HashSet<long>();
            try
            {
                foreach (ProfileLevel level in s.Profile)
                {
                    if (level == null || level.Buy < 0 || level.Sell < 0 || level.Unknown < 0 ||
                        !Finite(level.Tick * s.TickSize) || !prices.Add(level.Tick))
                        throw new InvalidDataException("History contains invalid or duplicate profile levels.");
                    checked { total += level.Buy + level.Sell + level.Unknown; buy += level.Buy; sell += level.Sell; }
                }
            }
            catch (OverflowException ex) { throw new InvalidDataException("History profile accounting overflowed.", ex); }
            if (total != s.SessionVolume || buy - sell != s.CumulativeDelta)
                throw new InvalidDataException("History profile totals disagree with session accounting.");
        }

        private static long EncodedSize(EngineSnapshot s, int keyBytes)
        {
            long size = Magic.Length + 4 + 4 + keyBytes + 8 + 11 * 8 + 4 + 8 + 4 + 4 + 4 + HashBytes;
            checked
            {
                foreach (HeatFrame f in s.Frames)
                    size += 26 + (f.BestBidTick.HasValue ? 8 : 0) + (f.BestAskTick.HasValue ? 8 : 0) + f.Cells.LongLength * 25;
                size += s.Trades.LongLength * 45 + s.Profile.LongLength * 32;
            }
            return size;
        }

        private static void Header(BinaryReader reader, string key, double tickSize)
        {
            byte[] magic = reader.ReadBytes(Magic.Length);
            if (magic.Length != Magic.Length) throw new InvalidDataException("History header is truncated.");
            for (int i = 0; i < Magic.Length; i++) if (magic[i] != Magic[i]) throw new InvalidDataException("File is not a FootprintMap capture; it will not be overwritten.");
            if (reader.ReadInt32() != FormatVersion) throw new InvalidDataException("Unsupported history format version.");
            int length = reader.ReadInt32();
            if (length < 1 || length > MaximumKeyBytes) throw new InvalidDataException("Invalid history capture-key length.");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length || !string.Equals(new UTF8Encoding(false, true).GetString(bytes), key, StringComparison.Ordinal))
                throw new InvalidDataException("History capture key does not match the instrument/context.");
            if (BitConverter.DoubleToInt64Bits(reader.ReadDouble()) != BitConverter.DoubleToInt64Bits(tickSize))
                throw new InvalidDataException("History tick size does not match the instrument.");
        }

        private static int Count(BinaryReader reader, int limit, string name, int minimumBytesPerItem)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > limit) throw new InvalidDataException("History " + name + " count exceeds its budget.");
            long remaining = reader.BaseStream.Length - HashBytes - reader.BaseStream.Position;
            if ((long)count * minimumBytesPerItem > remaining) throw new InvalidDataException("History " + name + " count exceeds the available payload.");
            return count;
        }
        private static void NullableTick(BinaryWriter writer, long? tick)
        { writer.Write(tick.HasValue); if (tick.HasValue) writer.Write(tick.Value); }
        private static long? NullableTick(BinaryReader reader)
        {
            byte present = reader.ReadByte();
            if (present > 1) throw new InvalidDataException("Invalid optional price marker.");
            return present == 0 ? (long?)null : reader.ReadInt64();
        }
        private static byte[] Key(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A full instrument and capture-context key is required.", "captureKey");
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(key);
            if (bytes.Length > MaximumKeyBytes) throw new ArgumentException("Capture key is too long.", "captureKey");
            return bytes;
        }
        private static HistoryLimits Budget(HistoryLimits value)
        {
            value = value ?? new HistoryLimits();
            if (value.MaximumFileBytes < 256 || value.MaximumFileBytes > 1024L * 1024 * 1024 ||
                value.MaximumFrames < 1 || value.MaximumFrames > 1000000 || value.MaximumTrades < 1 || value.MaximumTrades > 4000000 ||
                value.MaximumProfileLevels < 1 || value.MaximumProfileLevels > 1000000 || value.MaximumHeatCells < 1 || value.MaximumHeatCells > 16000000)
                throw new ArgumentOutOfRangeException("limits", "Invalid history file/count budgets.");
            return new HistoryLimits { MaximumFileBytes = value.MaximumFileBytes, MaximumFrames = value.MaximumFrames,
                MaximumTrades = value.MaximumTrades, MaximumProfileLevels = value.MaximumProfileLevels, MaximumHeatCells = value.MaximumHeatCells };
        }
        private static string Target(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !string.Equals(Path.GetExtension(path), ".fph", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("History files must use the .fph extension.", "path");
            return Path.GetFullPath(path);
        }
        private static byte[] Hash(Stream stream, long length)
        {
            stream.Position = 0;
            using (SHA256 hash = SHA256.Create())
            {
                var buffer = new byte[32768];
                long remaining = length;
                while (remaining > 0)
                {
                    int count = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                    if (count == 0) throw new EndOfStreamException("History ended while verifying its checksum.");
                    hash.TransformBlock(buffer, 0, count, buffer, 0); remaining -= count;
                }
                hash.TransformFinalBlock(new byte[0], 0, 0);
                return hash.Hash;
            }
        }
        private static bool ChecksumMatches(Stream stream)
        {
            if (stream.Length < HashBytes) return false;
            long payloadLength = stream.Length - HashBytes;
            byte[] actual = Hash(stream, payloadLength), expected = new byte[HashBytes];
            stream.Position = payloadLength;
            ReadExact(stream, expected);
            int mismatch = 0;
            for (int i = 0; i < HashBytes; i++) mismatch |= expected[i] ^ actual[i];
            return mismatch == 0;
        }
        private static void ReadExact(Stream stream, byte[] bytes)
        {
            int offset = 0;
            while (offset < bytes.Length)
            {
                int count = stream.Read(bytes, offset, bytes.Length - offset);
                if (count == 0) throw new EndOfStreamException("History checksum is truncated.");
                offset += count;
            }
        }
        private static bool Time(long ticks) { return ticks >= 0 && ticks <= DateTime.MaxValue.Ticks; }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
