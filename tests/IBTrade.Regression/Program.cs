using System.Reflection;
using IBTrade.Application.MarketData;
using IBTrade.Application.MarketData.Dtos;
using IBTrade.Application.Orders;
using IBTrade.Application.Orders.Dtos;
using IBTrade.Application.Quant.TrendScalper;
using IBTrade.Domain.Enums;

var dir = Path.Combine(Path.GetTempPath(), "ibtrade-regression-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
Environment.SetEnvironmentVariable("IBTRADE_DATA_DIR", dir);
try
{
    await CheckExit("timeout without market data", new DateTimeOffset(2026, 10, 26, 16, 0, 0, TimeSpan.Zero), 20, "time", false);
    await CheckExit("closing deadline without market data", new DateTimeOffset(2026, 10, 26, 19, 46, 0, TimeSpan.Zero), 1, "eod", false);
    await CheckExit("quote failure protects open position", new DateTimeOffset(2026, 10, 26, 16, 0, 0, TimeSpan.Zero), 1, "data", true);
    await CheckExit("empty quotes protect open position", new DateTimeOffset(2026, 10, 26, 16, 0, 0, TimeSpan.Zero), 1, "data", true, true);
    var quoteNow = new DateTime(2026, 10, 26, 12, 0, 0);
    foreach (var (age, accepted) in new[] { (1, true), (3, false), (-1, false) })
    {
        var history = new BrokenHistory(true) { Bars = [new CandleDto(quoteNow.AddMinutes(-age), 1, 1, 1, 1, 1)] };
        var engine = new TrendScalperEngine(history, new FakeOrders(), null!);
        var method = typeof(TrendScalperEngine).GetMethod("OptionMidAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var result = await (Task<decimal?>)method.Invoke(engine, ["test", TimeSpan.Zero, quoteNow, CancellationToken.None])!;
        if ((result is not null) != accepted) throw new Exception("Quote freshness failed: " + age);
        Console.WriteLine("PASS quote age " + age + " minutes");
    }
    var day = new DateTime(2026, 10, 26, 10, 0, 0);
    EtBar Bar(int minute, decimal o, decimal h, decimal l, decimal c) => new(day.AddMinutes(minute), o, h, l, c, 100);
    void Assert(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS " + label); }
    var call = new[] { Bar(0, 100, 102, 100, 101), Bar(1, 100.01m, 101, 100, 100.8m) };
    Assert(TrendResearch.Retest(call, "C", 100, 10, 0.03m), "call retest accepted");
    Assert(!TrendResearch.Retest([call[1]], "C", 100, 10, 0.03m), "no prior breakout rejected");
    Assert(!TrendResearch.Retest([call[0], Bar(1, 101, 102, 100.5m, 101.8m)], "C", 100, 10, 0.03m), "chasing without retest rejected");
    Assert(!TrendResearch.Retest([call[0], Bar(1, 100, 101, 99, 100.8m)], "C", 100, 10, 0.03m), "failed level rejected");
    Assert(TrendResearch.Retest([Bar(0, 100, 100, 98, 99), Bar(1, 99.99m, 100, 99, 99.2m)], "P", 100, 10, 0.03m), "put retest accepted");
    Assert(!TrendResearch.Retest([Bar(0, 100, 102, 100, 101), Bar(1, 100, 100, 99, 99), call[1]], "C", 100, 1, 0.03m), "expired breakout rejected");
    var ranges = new[] { Bar(0, 100, 101, 99, 100), Bar(1, 103, 104, 102, 103), Bar(2, 103, 104, 102, 103) };
    Assert(TrendResearch.Atr(ranges, 2) == 3m, "ATR includes price gaps");
    Assert(TrendResearch.Atr(ranges, 3) is null, "insufficient ATR history rejected");
    Assert(TrendResearch.Atr([Bar(0, 100, 100, 100, 100), Bar(1, 100, 100, 100, 100), Bar(2, 100, 100, 100, 100)], 2) is null, "zero ATR rejected");
    var costed = new TrendScalperTrade { Pnl = 10, EstimatedCommission = 1.3m };
    Assert(costed.EstimatedNetPnl == 8.7m, "commission deducted once");
    Assert(new TrendScalperTrade { Pnl = 10 }.EstimatedNetPnl is null, "missing historical fees remain unknown");
    Assert(!new TrendScalperSettings().RequireRetest && !new TrendScalperSettings().UseAtrExits, "baseline remains default");
    Console.WriteLine("All 19 regression scenarios passed. No broker connection or real orders used.");
}
finally { Directory.Delete(dir, true); }

async Task CheckExit(string label, DateTimeOffset now, int heldMinutes, string reason, bool expectHistory, bool empty = false)
{
    File.Delete(Path.Combine(dir, "trend-scalper.json"));
    var history = new BrokenHistory(empty);
    var orders = new FakeOrders();
    // Null context intentionally proves position exits never depend on external calendar/VIX services.
    var engine = new TrendScalperEngine(history, orders, null!, clock: new FixedClock(now));
    typeof(TrendScalperEngine).GetField("_position", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(engine,
        new TrendScalperPosition { Occ = "QQQ261026C00600000", EntryPrice = 1, EntryUnderlying = 600,
            Quantity = 1, OpenedAtUtc = now.UtcDateTime.AddMinutes(-heldMinutes) });
    await engine.TickAsync();
    var status = engine.GetStatus();
    if (orders.Sent.Count != 1 || orders.Sent[0].Side != OrderSide.Sell || orders.Sent[0].Type != OrderType.Market
        || status.Position is not null || status.Trades.Single().ExitReason != reason || (history.Calls > 0) != expectHistory)
        throw new Exception(label + " failed: " + status.LastError);
    Console.WriteLine("PASS " + label);
}
sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
sealed class BrokenHistory(bool empty) : IHistoricalDataService
{
    public int Calls;
    public IReadOnlyList<CandleDto> Bars { get; init; } = [];
    public Task<IReadOnlyList<CandleDto>> GetBarsAsync(string symbol, string? exchange = null, string? currency = "USD",
        string duration = "1 Y", string barSize = "1 day", CancellationToken ct = default, string? secType = null,
        string? whatToShow = null, string? endDateTime = null)
    {
        Calls++;
        if (!empty) throw new IOException("simulated data outage");
        return Task.FromResult(Bars);
    }
}
sealed class FakeOrders : IOrderService
{
    public List<PlaceOrderRequest> Sent = [];
    public IRiskGuard Risk { get; } = new RiskGuard();
    public Task<OrderDto> PlaceAsync(PlaceOrderRequest req, CancellationToken ct = default, decimal? estimatedPrice = null)
    {
        Sent.Add(req);
        return Task.FromResult(new OrderDto { Id = "test", Status = OrderStatus.Filled, FilledQuantity = req.Quantity, AvgFillPrice = 0.9m });
    }
    public Task<bool> CancelAsync(string id, CancellationToken ct = default) => Task.FromResult(true);
    public OrderDto? Get(string id) => null;
    public IReadOnlyList<OrderDto> List() => [];
}
