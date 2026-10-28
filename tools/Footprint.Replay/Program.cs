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
foreach (int seconds in new[] { 120, 30 })
foreach (string filter in new[] { "False", "True", "Following" })
foreach (var cluster in new[] { ClusterMode.Pixel, ClusterMode.Smart })
{
    var viewport = new Viewport(start.AddSeconds(120 - seconds).Ticks, start.AddSeconds(120).Ticks,
        center - 40, center + 40, 1200, 640);
    var display = new DisplaySettings { UsePersistence = filter != "False", SuppressFollowingLiquidity = filter == "Following", MinimumLiquidity = 6,
        AutomaticContrast = true, ContrastPercentile = 98, Clustering = cluster,
        BubbleScale = 4.2, MaxBubbleRadius = 28, SmartPixels = 5, HeatColumnPixels = 2 };
    var scene = Visualization.Build(snapshot, viewport, display);
    var profile = ProfileStudies.Build(snapshot, new ProfileSettings { Scope = ProfileScope.Visible,
        BuildFootprint = true, FootprintMilliseconds = 10000, BuildTpo = true, TpoMinutes = 1,
        BookLevels = 40, BookDistanceDecay = 8, LargeLiquidityMinimum = 200 }, viewport.FromTicks, viewport.ToTicks);
    views.Add(new { key = seconds + ":" + filter + ":" + cluster, viewport, scene, profile });
}
var palettes = Enum.GetValues<HeatPalette>().ToDictionary(p => p.ToString(),
    p => Enumerable.Range(0, 1024).Select(i => Visualization.Color(i / 1023.0, p).Hex).ToArray());
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
    palettes, views, flow
};
File.WriteAllText(output, JsonSerializer.Serialize(data, new JsonSerializerOptions { IncludeFields = true }));
Console.WriteLine($"Replay: {snapshot.DepthEvents:N0} depth events, {snapshot.TradeEvents:N0} trades, {snapshot.Frames.Length:N0} frames.");
Console.WriteLine($"Invalid: {snapshot.InvalidEvents}; late: {snapshot.LateEvents}; session volume: {snapshot.SessionVolume:N0}; CVD: {snapshot.CumulativeDelta:N0}.");
Console.WriteLine($"Wrote {views.Count} C#-generated views to {output}");
if (snapshot.InvalidEvents != 0 || snapshot.LateEvents != 0 || snapshot.TradeEvents != 1872)
    throw new InvalidOperationException("Synthetic replay fixture did not process as expected.");

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
