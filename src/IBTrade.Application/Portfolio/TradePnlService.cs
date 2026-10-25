using IBTrade.Application.Portfolio.Dtos;
using IBTrade.Domain.Entities;
using IBTrade.Domain.Enums;
using IBTrade.Domain.Repositories;

namespace IBTrade.Application.Portfolio;

public interface ITradePnlService
{
    Task<TradePnlDto> GetAsync(string? accountId, TradeEnv? env, CancellationToken ct = default);
}

/// <summary>
/// 逐笔 FIFO 配对：每一例 = 一笔开仓批次被反向成交平掉的部分。
/// 盈亏 = (平仓价 − 开仓价) × 数量 × 乘数 − 分摊佣金；空头方向相反。
/// 未平仓只列成本和余量，不算浮盈（没有官方市价不编）。
/// </summary>
public class TradePnlService(ITradeRepository trades, IAccountRepository accounts) : ITradePnlService
{
    public async Task<TradePnlDto> GetAsync(string? accountId, TradeEnv? env, CancellationToken ct = default)
    {
        var allowed = await AllowAsync(accountId, env, ct);
        var list = (await trades.GetAllAsync(ct))
            .Where(t => allowed is null || allowed.Contains(t.AccountId))
            .OrderBy(t => t.TradeTimeUtc)
            .ThenBy(t => t.Id)
            .ToList();

        var closed = new List<ClosedTradeDto>();
        var open = new List<OpenLotDto>();
        var fillPnl = new Dictionary<long, decimal>();

        foreach (var group in list.GroupBy(t => (t.AccountId, t.Symbol)))
            Match(group.Key.AccountId, group.Key.Symbol, group.ToList(), closed, open, fillPnl);

        closed = closed.OrderByDescending(c => c.ClosedAtUtc).ThenByDescending(c => c.CloseTradeId).ToList();
        var wins = closed.Where(c => c.NetPnl > 0).ToList();
        var losses = closed.Where(c => c.NetPnl < 0).ToList();
        var flats = closed.Count - wins.Count - losses.Count;
        var scope = string.IsNullOrWhiteSpace(accountId) ? (env?.ToString() ?? "ALL") : accountId!;

        return new TradePnlDto(
            scope,
            DateTime.UtcNow,
            decimal.Round(closed.Sum(c => c.NetPnl), 2),
            decimal.Round(wins.Sum(c => c.NetPnl), 2),
            decimal.Round(losses.Sum(c => c.NetPnl), 2),
            wins.Count,
            losses.Count,
            flats,
            closed.Count,
            open.Count,
            "FIFO · official Flex fills · options include multiplier · commission by quantity",
            closed,
            open.OrderBy(o => o.OpenedAtUtc).ToList(),
            fillPnl.Select(kv => new FillPnlDto(kv.Key, decimal.Round(kv.Value, 2))).ToList());
    }

    private static void Match(
        string accountId,
        string symbol,
        List<Trade> fills,
        List<ClosedTradeDto> closed,
        List<OpenLotDto> open,
        Dictionary<long, decimal> fillPnl)
    {
        var lots = new List<Lot>();
        foreach (var t in fills)
        {
            var signed = t.Side == Side.Buy ? t.Quantity : -t.Quantity;
            var remaining = signed;
            var closeLeft = Math.Abs(t.Commission);

            while (remaining != 0 && lots.Count > 0 && Math.Sign(lots[0].SignedQty) != Math.Sign(remaining))
            {
                var lot = lots[0];
                var qty = Math.Min(Math.Abs(remaining), Math.Abs(lot.SignedQty));
                var mult = t.EffectiveMultiplier;
                var longClose = remaining < 0;
                var gross = longClose
                    ? (t.Price - lot.Price) * qty * mult
                    : (lot.Price - t.Price) * qty * mult;
                var closeComm = Math.Abs(t.Commission) * (qty / t.Quantity);
                var openComm = lot.Commission * (qty / Math.Abs(lot.SignedQty));
                var comm = closeComm + openComm;
                var net = decimal.Round(gross - comm, 6);

                closed.Add(new ClosedTradeDto(
                    accountId,
                    symbol,
                    t.AssetType.ToString(),
                    lot.SignedQty > 0 ? "Long" : "Short",
                    qty,
                    lot.Price,
                    t.Price,
                    mult,
                    lot.OpenedAtUtc,
                    t.TradeTimeUtc,
                    lot.TradeId,
                    t.Id,
                    decimal.Round(gross, 6),
                    decimal.Round(comm, 6),
                    decimal.Round(net, 2),
                    t.Currency));

                Add(fillPnl, t.Id, net);

                remaining = remaining > 0 ? remaining - qty : remaining + qty;
                lot.Commission -= openComm;
                lot.SignedQty = lot.SignedQty > 0 ? lot.SignedQty - qty : lot.SignedQty + qty;
                closeLeft -= closeComm;
                if (lot.SignedQty == 0) lots.RemoveAt(0);
                else lots[0] = lot;
            }

            if (remaining != 0)
            {
                lots.Add(new Lot
                {
                    SignedQty = remaining,
                    Price = t.Price,
                    Commission = remaining == signed ? Math.Abs(t.Commission) : Math.Max(closeLeft, 0),
                    OpenedAtUtc = t.TradeTimeUtc,
                    TradeId = t.Id,
                    AssetType = t.AssetType.ToString(),
                    Multiplier = t.EffectiveMultiplier,
                    Currency = t.Currency
                });
            }
        }

        foreach (var lot in lots.Where(l => l.SignedQty != 0))
        {
            open.Add(new OpenLotDto(
                accountId,
                symbol,
                lot.AssetType,
                lot.SignedQty > 0 ? "Long" : "Short",
                Math.Abs(lot.SignedQty),
                lot.Price,
                lot.Multiplier,
                lot.OpenedAtUtc,
                lot.TradeId,
                decimal.Round(lot.Commission, 6),
                lot.Currency));
        }
    }

    private static void Add(Dictionary<long, decimal> map, long id, decimal pnl)
    {
        map[id] = map.GetValueOrDefault(id) + pnl;
    }

    private async Task<HashSet<string>?> AllowAsync(string? accountId, TradeEnv? env, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(accountId))
        {
            if (env is null) return [accountId];
            var acc = await accounts.GetByAccountIdAsync(accountId, ct);
            return acc is not null && acc.Environment == env ? [accountId] : [];
        }
        if (env is null) return null;
        var all = await accounts.GetAllAsync(ct);
        return all.Where(a => a.Environment == env).Select(a => a.AccountId).ToHashSet();
    }

    private sealed class Lot
    {
        public decimal SignedQty;
        public decimal Price;
        public decimal Commission;
        public DateTime OpenedAtUtc;
        public long TradeId;
        public string AssetType = "Stock";
        public decimal Multiplier = 1;
        public string Currency = "USD";
    }
}
