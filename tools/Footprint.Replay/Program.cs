using System.Text.Json;
using FootprintIndicator.Core;

// An explicitly synthetic fixture: fixed walls, moving near-touch quotes, bursts,
// ambiguous prints, and a genuine observation gap. No vendor data is fabricated.
var output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("artifacts/replay-data.json");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
var settings = new EngineSettings { TickSize = .25, FrameMilliseconds = 250,
    PersistenceMilliseconds = 1250, RetentionMinutes = 30, MaxDepthLevels = 100 };
var engine = new MarketEngine(settings);
var start = new DateTime(2026, 10, 23, 14, 30, 0, DateTimeKind.Unspecified);
var random = new Random(41421);
var bidLadder = new List<long>();
var askLadder = new List<long>();
const long center = 24000;
for (int i = 0; i < 480; ++i)
{
    var time = start.AddMilliseconds(i * 250);
    if (i == 240)
    {
        engine.SetConnected(time, false);
        bidLadder.Clear(); askLadder.Clear();
    }
    if (i >= 240 && i < 252) { engine.Advance(time); continue; }
    if (i == 252) engine.SetConnected(time, true);
    var mid = center + (long)Math.Round(11 * Math.Sin(i * .028) + 4 * Math.Sin(i * .077));
    ApplyBook(BookSide.Bid, bidLadder, Enumerable.Range(0, 65).Select(n => mid - n).ToList(), time, i);
    ApplyBook(BookSide.Ask, askLadder, Enumerable.Range(0, 65).Select(n => mid + 2 + n).ToList(), time, i);
    for (int j = 0; j < 4; ++j)
    {
        int side = random.Next(20);
        long tick = side == 0 ? mid + 1 : side < 11 ? mid + 2 : mid;
        long quantity = (i % 29 == 0 ? random.Next(45, 130) : random.Next(1, 12));
        engine.Trade(time.AddMilliseconds(j * 45), tick * .25, quantity, mid * .25, (mid + 2) * .25);
    }
    engine.Advance(time.AddMilliseconds(249));
}
engine.Advance(start.AddSeconds(120));
var snapshot = engine.Snapshot();
var flow = OrderFlowStudies.Build(snapshot, new OrderFlowSettings {
    AbsorptionVolume = 80, AbsorptionWindowMilliseconds = 1000, SweepVolume = 80,
    SweepWindowMilliseconds = 1000, SweepMinimumLevels = 2, LargePrintVolume = 60,
    EnableReplenishment = true, ReplenishmentVolume = 60, MaxMarkers = 5000 });
var views = new List<object>();
var fullLowerPanel = LowerPanel.Build(snapshot, start.Ticks, start.AddSeconds(120).Ticks, 1000);
var lowerScale = new LowerPanelScale().Update(snapshot.SessionStartTicks,
    fullLowerPanel.MinDelta, fullLowerPanel.MaxDelta, fullLowerPanel.MaxVolume);
foreach (int seconds in new[] { 120, 30 })
foreach (string filter in new[] { "False", "True", "Following" })
foreach (var cluster in new[] { ClusterMode.Pixel, ClusterMode.Smart })
{
    var viewport = new Viewport(start.AddSeconds(120 - seconds).Ticks, start.AddSeconds(120).Ticks,
        center - 40, center + 40, 1200, 640);
    DisplaySettings MakeDisplay(string preset) => new DisplaySettings {
        UsePersistence = filter != "False", SuppressFollowingLiquidity = filter == "Following", MinimumLiquidity = 6,
        AutomaticContrast = true, ContrastPercentile = preset == "focused" ? 98 : 100, Clustering = cluster,
        Gamma = preset == "focused" ? 2 : preset == "balanced" ? 1.4 : 1,
        HeatFloorPercent = preset == "focused" ? 20 : preset == "balanced" ? 10 : 0,
        BubbleScale = 12, BubbleReferenceVolume = 100, MaxBubbleRadius = 28, SmartPixels = 5, HeatColumnPixels = 2 };
    var display = MakeDisplay("normal");
    var scene = Visualization.Build(snapshot, viewport, display);
    var balancedDisplay = MakeDisplay("balanced");
    var balancedScene = Visualization.Build(snapshot, viewport, balancedDisplay);
    var focusedDisplay = MakeDisplay("focused");
    var focusedScene = Visualization.Build(snapshot, viewport, focusedDisplay);
    var profile = ProfileStudies.Build(snapshot, new ProfileSettings { Scope = ProfileScope.Visible,
        BuildFootprint = true, FootprintMilliseconds = 10000, BuildTpo = true, TpoMinutes = 1,
        BookLevels = 40, BookDistanceDecay = 8, LargeLiquidityMinimum = 200 }, viewport.FromTicks, viewport.ToTicks);
    double pixelsPerTick = viewport.Height / (viewport.MaxPriceTick - viewport.MinPriceTick);
    int ticksPerRow = ColumnAggregation.ChooseTicksPerRow(pixelsPerTick, 14);
    var bookColumns = ColumnAggregation.Book(snapshot.Bids, snapshot.Asks, 0, 0, ticksPerRow, false);
    var profileColumns = ColumnAggregation.Profile(snapshot.Profile, ticksPerRow);
    var timeGrid = NiceTimeGrid.Build(viewport.FromTicks, viewport.ToTicks, viewport.Width, 110);
    views.Add(new { key = seconds + ":" + filter + ":" + cluster, viewport, display, scene, profile,
        heatColors = HeatColors(scene, display), bubbleColors = BubbleColors(scene), hoverExamples = HoverExamples(scene, viewport),
        columns = new { ticksPerRow, pixelsPerTick, minimumRowPixels = 14,
            bookLabel = "COB Σ" + ticksPerRow + "t", profileLabel = "SVP Σ" + ticksPerRow + "t",
            book = bookColumns, profile = profileColumns, profileMaximum = Math.Max(1, profileColumns.Max(row => row.Total)) },
        // DateTime ticks exceed JavaScript's exact integer range. Preserve grid
        // anchors as strings; the renderer only converts relative offsets.
        timeGrid = new { timeGrid.IntervalTicks, Ticks = timeGrid.Ticks.Select(tick => tick.ToString()).ToArray(),
            labels = timeGrid.Ticks.Select(tick => new DateTime(tick).ToString("HH:mm:ss")).ToArray() },
        lowerPanel = LowerPanel.Build(snapshot, viewport.FromTicks, viewport.ToTicks, 1000), lowerScale,
        balanced = new { display = balancedDisplay, scene = balancedScene,
            heatColors = HeatColors(balancedScene, balancedDisplay), bubbleColors = BubbleColors(balancedScene),
            hoverExamples = HoverExamples(balancedScene, viewport) },
        focused = new { display = focusedDisplay, scene = focusedScene,
            heatColors = HeatColors(focusedScene, focusedDisplay), bubbleColors = BubbleColors(focusedScene),
            hoverExamples = HoverExamples(focusedScene, viewport) } });
}
var data = new
{
    title = "Footprint Map — synthetic engine replay",
    evidence = "Synthetic events processed by the actual C# engine and visualization model. This is a browser rendering preview, not a NinjaTrader runtime screenshot or a Bookmap capture.",
    symbol = "ES synthetic", tickSize = .25, startTicks = start.Ticks,
    snapshot.TimeTicks, snapshot.DepthEvents, snapshot.TradeEvents, snapshot.InvalidEvents,
    snapshot.LateEvents, snapshot.CumulativeDelta, snapshot.SessionVolume, snapshot.SessionVwap,
    snapshot.Bids, snapshot.Asks, snapshot.Profile,
    frames = snapshot.Frames.Select(f => new { f.FromTicks, f.ToTicks, f.Epoch, f.BestBidTick, f.BestAskTick }),
    trades = snapshot.Trades.Select(t => new { t.TimeTicks, t.PriceTick, t.Volume, t.Side, t.CumulativeDelta }),
    views, flow, navigationExamples = NavigationExamples(), bubbleColorExamples = BubbleColorExamples()
};
File.WriteAllText(output, JsonSerializer.Serialize(data, new JsonSerializerOptions { IncludeFields = true }));
Console.WriteLine($"Replay: {snapshot.DepthEvents:N0} depth events, {snapshot.TradeEvents:N0} trades, {snapshot.Frames.Length:N0} frames.");
Console.WriteLine($"Invalid: {snapshot.InvalidEvents}; late: {snapshot.LateEvents}; session volume: {snapshot.SessionVolume:N0}; CVD: {snapshot.CumulativeDelta:N0}.");
Console.WriteLine($"Wrote {views.Count} C#-generated views to {output}");
if (snapshot.InvalidEvents != 0 || snapshot.LateEvents != 0 || snapshot.TradeEvents != 1872)
    throw new InvalidOperationException("Synthetic replay fixture did not process as expected.");

Dictionary<string, string[]> HeatColors(RenderScene scene, DisplaySettings display)
{
    double floor = display.HeatFloorPercent / 100;
    return Enum.GetValues<HeatPalette>().ToDictionary(p => p.ToString(), p => scene.Heat.Select(tile =>
        p == display.Palette ? tile.Color.Hex : Visualization.Color(Math.Pow(
            Math.Clamp((Math.Clamp(tile.Quantity / scene.HeatMaximum, 0, 1) - floor) / (1 - floor), 0, 1),
            display.Gamma), p).Hex).ToArray());
}

object BubbleColors(RenderScene scene)
{
    var buy = new Rgb(42, 203, 139); var sell = new Rgb(225, 83, 72); var unknown = new Rgb(174, 184, 196);
    return new {
        dominant = scene.Bubbles.Select(b => Visualization.DominantBubbleColor(b, buy, sell, unknown).Hex).ToArray(),
        gradient = scene.Bubbles.Select(b => b.Total <= 0 ? unknown.Hex : new Rgb(
            (byte)((buy.R * (double)b.Buy + sell.R * (double)b.Sell + unknown.R * (double)b.Unknown) / b.Total),
            (byte)((buy.G * (double)b.Buy + sell.G * (double)b.Sell + unknown.G * (double)b.Unknown) / b.Total),
            (byte)((buy.B * (double)b.Buy + sell.B * (double)b.Sell + unknown.B * (double)b.Unknown) / b.Total)).Hex).ToArray()
    };
}

object[] BubbleColorExamples()
{
    var buy = new Rgb(42, 203, 139); var sell = new Rgb(225, 83, 72); var unknown = new Rgb(174, 184, 196);
    return new[] {
        ("buy", new Bubble { Buy = 12, Sell = 4, Unknown = 2 }),
        ("sell", new Bubble { Buy = 3, Sell = 12, Unknown = 2 }),
        ("unknown", new Bubble { Buy = 3, Sell = 4, Unknown = 12 }),
        ("tie", new Bubble { Buy = 12, Sell = 12, Unknown = 2 }),
        ("unknown-tie", new Bubble { Buy = 12, Sell = 4, Unknown = 12 }),
        ("sell-unknown-tie", new Bubble { Buy = 4, Sell = 12, Unknown = 12 }),
        ("three-way-tie", new Bubble { Buy = 12, Sell = 12, Unknown = 12 }),
        ("empty", new Bubble())
    }.Select(example => (object)new { key = example.Item1, bubble = example.Item2,
        color = Visualization.DominantBubbleColor(example.Item2, buy, sell, unknown).Hex }).ToArray();
}

object[] NavigationExamples()
{
    var navigation = new TimeNavigation(120);
    var samples = new List<object>();
    void Add(string key, string label, TimeRange range) => samples.Add(new { key, label,
        fromTicks = range.FromTicks.ToString(), toTicks = range.ToTicks.ToString(),
        from = new DateTime(range.FromTicks).ToString("HH:mm:ss.fff"),
        to = new DateTime(range.ToTicks).ToString("HH:mm:ss.fff"),
        seconds = range.SpanTicks / (double)TimeSpan.TicksPerSecond, navigation.IsLive });
    var live = navigation.Range(snapshot.TimeTicks);
    Add("live", "Live observation plus display padding", live);
    navigation.Pause();
    var paused = navigation.Range(snapshot.TimeTicks);
    Add("paused", "Paused absolute interval", paused);
    var advanced = navigation.Range(snapshot.TimeTicks + TimeSpan.TicksPerMinute);
    Add("advanced", "Same pause after another 60 s of feed time", advanced);
    if (paused.FromTicks != live.FromTicks || paused.ToTicks != live.ToTicks ||
        advanced.FromTicks != paused.FromTicks || advanced.ToTicks != paused.ToTicks)
        throw new InvalidOperationException("Replay pause moved the absolute time interval.");
    navigation.PanSeconds(-30);
    Add("pan", "Pan 30 s earlier; remain paused", navigation.LastRange);
    if (navigation.IsLive || navigation.LastRange.FromTicks != paused.FromTicks - 30 * TimeSpan.TicksPerSecond ||
        navigation.LastRange.ToTicks != paused.ToTicks - 30 * TimeSpan.TicksPerSecond)
        throw new InvalidOperationException("Replay pan did not preserve its paused time interval.");
    var before = navigation.LastRange;
    long anchor = before.FromTicks + before.SpanTicks * 3 / 4;
    navigation.Zoom(.5, .75);
    var zoomed = navigation.LastRange;
    if (zoomed.FromTicks + zoomed.SpanTicks * 3 / 4 != anchor)
        throw new InvalidOperationException("Replay navigation did not preserve the zoom anchor.");
    Add("zoom", "Half the window around its 75% time anchor", zoomed);
    navigation.ResumeLive();
    var resumed = navigation.Range(snapshot.TimeTicks + TimeSpan.TicksPerMinute);
    Add("resume", "Resume with the latest observation", resumed);
    if (!navigation.IsLive || resumed.SpanTicks != zoomed.SpanTicks ||
        resumed.ToTicks != snapshot.TimeTicks + TimeSpan.TicksPerMinute + zoomed.SpanTicks / 25)
        throw new InvalidOperationException("Replay resume did not follow the latest observation.");
    return samples.ToArray();
}

object[] HoverExamples(RenderScene scene, Viewport viewport)
{
    // Deliberately finite examples: these are actual core inspections at the marked
    // coordinates, not a second JavaScript implementation of the hover algorithm.
    var samples = new List<object>();
    void Add(string key, string label, double x, double y)
    {
        var result = HoverInspection.Inspect(snapshot, scene, viewport, x, y);
        if (result != null) samples.Add(new { key, label, result });
    }
    double X(long time) => (time - viewport.FromTicks) / (double)(viewport.ToTicks - viewport.FromTicks) * viewport.Width;
    double Y(long tick) => (viewport.MaxPriceTick - tick) / (viewport.MaxPriceTick - viewport.MinPriceTick) * viewport.Height;
    var visible = scene.Bubbles.Where(b => b.Radius > 0 && b.X >= 0 && b.X < viewport.Width && b.Y >= 0 && b.Y < viewport.Height).ToArray();
    var bubble = visible.OrderByDescending(b => b.Total).FirstOrDefault();
    if (bubble != null) Add("bubble", "Execution cluster", bubble.X, bubble.Y);
    var unknown = visible.Where(b => b.Unknown > 0).OrderByDescending(b => b.Total).FirstOrDefault();
    if (unknown != null) Add("unknown", "Cluster with unknown aggressor", unknown.X, unknown.Y);
    long sampleTime = viewport.FromTicks + (viewport.ToTicks - viewport.FromTicks) * 3 / 4 + TimeSpan.TicksPerMillisecond * 125;
    Add("wall", "Fixed liquidity wall", X(sampleTime), Y(center - 29));
    Add("weak", "Small captured liquidity", X(sampleTime), Y(center - 18));
    long gapTime = start.AddSeconds(61.5).Ticks;
    if (gapTime >= viewport.FromTicks && gapTime < viewport.ToTicks)
        Add("gap", "Disconnected interval", X(gapTime), Y(center - 29));
    return samples.ToArray();
}

void ApplyBook(BookSide side, List<long> current, List<long> desired, DateTime time, int step)
{
    var wanted = new HashSet<long>(desired);
    for (int position = current.Count - 1; position >= 0; --position)
        if (!wanted.Contains(current[position]))
        {
            engine.Depth(time, side, DepthMutation.Remove, position, current[position] * .25, 0);
            current.RemoveAt(position);
        }
    for (int position = 0; position < desired.Count; ++position)
    {
        long price = desired[position];
        // Fixed absolute-price walls vs a high-quantity band following the touch.
        long quantity = 8 + Math.Abs((price * 29 + step / 9 * 11) % 42);
        if (price == center - 29 || price == center + 28) quantity = 550 + step % 7 * 3;
        if (price == center - 21 || price == center + 37) quantity = 235;
        if (position == 8 || position == 9) quantity += 430;
        if (position >= current.Count || current[position] != price)
        {
            engine.Depth(time, side, DepthMutation.Insert, position, price * .25, quantity);
            current.Insert(position, price);
        }
        else engine.Depth(time, side, DepthMutation.Update, position, price * .25, quantity);
    }
}
