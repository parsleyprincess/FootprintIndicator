using System;

namespace FootprintIndicator.Core
{
    public enum BookSide { Bid, Ask }
    public enum DepthMutation { Insert, Update, Remove }
    public enum TradeSide { Buy, Sell, Unknown }
    public enum ClusterMode { Pixel, Smart, Time, Volume, Price, PriceAndSide, Individual }
    public enum BubbleColorMode { Gradient, Split, Pie }
    public enum HeatPalette { Classic, Grayscale, Ice }

    public sealed class EngineSettings
    {
        public double TickSize = 0.25;
        public int FrameMilliseconds = 250;
        public int PersistenceMilliseconds = 1000;
        public int RetentionMinutes = 30;
        public int MaxFrames = 7200;
        public int MaxTrades = 200000;
        public int MaxDepthLevels = 200;
        public int MaxProfileLevels = 20000;
        public int MaxCohortsPerLevel = 128;
    }

    public sealed class DepthLevel
    {
        public readonly long Tick, Quantity;
        public DepthLevel(long tick, long quantity) { Tick = tick; Quantity = quantity; }
    }
    public sealed class HeatCell
    {
        public readonly long Tick;
        public readonly BookSide Side;
        public readonly double RawQuantity, PersistentQuantity;
        public HeatCell(long tick, BookSide side, double raw, double persistent)
        { Tick = tick; Side = side; RawQuantity = raw; PersistentQuantity = persistent; }
    }
    public sealed class HeatFrame
    {
        public readonly long FromTicks, ToTicks;
        public readonly int Epoch;
        public readonly HeatCell[] Cells;
        public readonly long? BestBidTick, BestAskTick;
        public HeatFrame(long from, long to, int epoch, HeatCell[] cells, long? bid, long? ask)
        { FromTicks = from; ToTicks = to; Epoch = epoch; Cells = cells; BestBidTick = bid; BestAskTick = ask; }
    }
    public sealed class TradeRecord
    {
        public readonly long TimeTicks, PriceTick, Volume, Sequence, CumulativeDelta;
        public readonly int Epoch;
        public readonly TradeSide Side;
        public TradeRecord(long time, long tick, long volume, TradeSide side, long sequence, long delta, int epoch = 0)
        { TimeTicks = time; PriceTick = tick; Volume = volume; Side = side; Sequence = sequence; CumulativeDelta = delta; Epoch = epoch; }
    }
    public sealed class ProfileLevel
    {
        public readonly long Tick, Buy, Sell, Unknown;
        public long Total { get { return Buy + Sell + Unknown; } }
        public ProfileLevel(long tick, long buy, long sell, long unknown)
        { Tick = tick; Buy = buy; Sell = sell; Unknown = unknown; }
    }
    public sealed class EngineSnapshot
    {
        public long Version, TimeTicks, SessionStartTicks, DepthEvents, TradeEvents, InvalidEvents, LateEvents, DroppedFrames, DroppedTrades;
        public long CumulativeDelta, SessionVolume;
        public double TickSize, SessionVwap;
        public int Epoch;
        public bool Connected, HasDepth;
        public string Status;
        public HeatFrame[] Frames = new HeatFrame[0];
        public TradeRecord[] Trades = new TradeRecord[0];
        public DepthLevel[] Bids = new DepthLevel[0], Asks = new DepthLevel[0];
        public ProfileLevel[] Profile = new ProfileLevel[0];
    }

    public sealed class Viewport
    {
        public long FromTicks, ToTicks;
        public double MinPriceTick, MaxPriceTick;
        public int Width, Height;
        public Viewport(long from, long to, double minTick, double maxTick, int width, int height)
        { FromTicks = from; ToTicks = to; MinPriceTick = minTick; MaxPriceTick = maxTick; Width = width; Height = height; }
    }
    public sealed class DisplaySettings
    {
        public bool UsePersistence = true;
        public bool SuppressFollowingLiquidity = false;
        public int FollowingMinimumMoves = 3, FollowingWindowMilliseconds = 10000;
        public double FollowingSizeRatio = 3.0, FollowingSizeTolerance = 0.35;
        public double MinimumLiquidity = 1, TopLiquidityPercent = 100;
        public bool AutomaticContrast = true;
        public double ContrastPercentile = 97, ManualMaximum = 200, Gamma = 1;
        // Fraction of the contrast ceiling mapped to the dark background before gamma.
        public double HeatFloorPercent = 0;
        public HeatPalette Palette = HeatPalette.Classic;
        public int HeatColumnPixels = 2;
        public ClusterMode Clustering = ClusterMode.Smart;
        public int ClusterMilliseconds = 100, ClusterVolume = 100, SmartPixels = 8;
        // Volume runs restart at fixed absolute-time boundaries so trimming an
        // old partial anchor cannot rephase every later retained cluster.
        public int VolumeAnchorSeconds = 60;
        public long MinimumTradeSize = 1, MinimumBubbleVolume = 1;
        public bool DeltaBubbles = false;
        public double BubbleScale = 5, MaxBubbleRadius = 35;
        // A fixed volume reference keeps old executions the same size as new data arrives.
        public double BubbleReferenceVolume = 100;
    }
    public struct Rgb
    {
        public readonly byte R, G, B;
        public Rgb(byte r, byte g, byte b) { R = r; G = g; B = b; }
        public string Hex { get { return "#" + R.ToString("X2") + G.ToString("X2") + B.ToString("X2"); } }
    }
    public sealed class HeatTile
    {
        public float X, Y, Width, Height;
        public double Quantity;
        public Rgb Color;
    }
    public sealed class Bubble
    {
        public double X, Y, Radius, PriceTick;
        public long TimeTicks, FromTicks, ToTicks, Buy, Sell, Unknown;
        public long MinPriceTick, MaxPriceTick;
        public long Total { get { return Buy + Sell + Unknown; } }
        public long Delta { get { return Buy - Sell; } }
    }
    public sealed class RenderScene
    {
        public HeatTile[] Heat = new HeatTile[0];
        public Bubble[] Bubbles = new Bubble[0];
        public ProfileLevel[] VisibleProfile = new ProfileLevel[0];
        public double HeatMaximum, BubbleReferenceVolume;
    }
}
