using IBTrade.Application.OptionsIntel;
using IBTrade.Application.OptionsIntel.Dtos;
using Microsoft.Extensions.Logging;

namespace IBTrade.Infrastructure.OptionsIntel;

/// <summary>Polymarket（优先）+ Manifold（可用时补齐涨跌）。对结果套快照计算涨跌。</summary>
public class CompositePredictionClient(
    PolymarketClient polymarket,
    ManifoldClient manifold,
    PredictionSnapshotStore snapshots,
    ILogger<CompositePredictionClient>? logger = null) : IPredictionMarketClient
{
    public async Task<(IReadOnlyList<PredictionMarketDto> Markets, DataSourceStatusDto Source)> SearchAsync(
        string symbol, string? companyName, CancellationToken ct = default)
    {
        var polyTask = polymarket.SearchAsync(symbol, companyName, ct);
        var manTask = manifold.SearchAsync(symbol, companyName, ct);
        await Task.WhenAll(polyTask, manTask);

        var poly = await polyTask;
        var man = await manTask;
        var merged = poly.Markets.Concat(man.Markets).ToList();
        var tracked = snapshots.Apply(merged);

        var ok = tracked.Count > 0;
        var venues = string.Join(" + ", new[]
        {
            poly.Source.Status == "ok" ? "Polymarket" : null,
            man.Source.Status == "ok" ? "Manifold" : null
        }.Where(x => x is not null));

        var msg = ok
            ? $"{tracked.Count} 个市场 · {venues} · 涨跌相对上次拉取"
            : $"Polymarket: {poly.Source.Message}; Manifold: {man.Source.Message}";

        logger?.LogInformation("预测市场合并：poly={Poly} man={Man} n={N}",
            poly.Source.Status, man.Source.Status, tracked.Count);

        return (tracked, new DataSourceStatusDto(
            "prediction",
            "Prediction markets",
            ok ? "ok" : "error",
            msg,
            man.Source.Status == "ok" ? man.Source.Url : poly.Source.Url));
    }
}
