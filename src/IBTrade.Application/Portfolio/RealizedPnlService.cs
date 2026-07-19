using System.Linq;
using IBTrade.Application.Portfolio.Dtos;
using IBTrade.Domain.Entities;
using IBTrade.Domain.Enums;
using IBTrade.Domain.Repositories;

namespace IBTrade.Application.Portfolio;

/// <summary>
/// 按标的历史盈亏：优先 Flex SymbolSummary（fifoPnlRealized）；
/// 若 Flex 段为空或 fifo 全 0（常见），回退为成交 FIFO 估算。
/// </summary>
public class RealizedPnlService(
    IRealizedPnlRepository repo,
    IAccountRepository accountRepository,
    ITradeRepository tradeRepository)
    : IRealizedPnlService
{
    public async Task<RealizedPnlSummaryDto> GetAsync(string? accountId, TradeEnv? env = null, CancellationToken ct = default)
    {
        var allowed = await ResolveAllowedAccountIdsAsync(accountId, env, ct);

        var flexRows = (string.IsNullOrWhiteSpace(accountId)
                ? await repo.GetAllAsync(ct)
                : await repo.GetByAccountAsync(accountId, ct))
            .Where(r => allowed is null || allowed.Contains(r.AccountId))
            .ToList();

        var useFlex = flexRows.Any(r => r.Realized != 0m);
        List<RealizedPnlRowDto> rows;
        if (useFlex)
        {
            rows = flexRows
                .Select(r => new RealizedPnlRowDto(
                    r.AccountId, r.Symbol, r.AssetCategory, r.Description, r.Currency,
                    r.Realized, r.Unrealized))
                .OrderBy(r => r.RealizedPnl)
                .ToList();
        }
        else
        {
            var trades = (await tradeRepository.GetAllAsync(ct))
                .Where(t => allowed is null || allowed.Contains(t.AccountId))
                .ToList();
            rows = ComputeFifoRows(trades)
                .OrderBy(r => r.RealizedPnl)
                .ToList();
        }

        return new RealizedPnlSummaryDto(
            Scope: string.IsNullOrWhiteSpace(accountId) ? (env is null ? "ALL" : env.ToString()!) : accountId!,
            TotalRealizedPnl: Math.Round(rows.Sum(x => x.RealizedPnl), 2),
            TotalUnrealizedPnl: Math.Round(rows.Sum(x => x.UnrealizedPnl), 2),
            SymbolCount: rows.Count,
            AsOfUtc: DateTime.UtcNow,
            Rows: rows);
    }

    private static List<RealizedPnlRowDto> ComputeFifoRows(IReadOnlyList<Trade> trades)
    {
        var result = new List<RealizedPnlRowDto>();
        foreach (var group in trades.GroupBy(t => (t.AccountId, t.Symbol)))
        {
            var lots = new List<(decimal Qty, decimal Price, decimal Commission)>();
            decimal realized = 0m;
            string? asset = null;

            foreach (var t in group.OrderBy(x => x.TradeTimeUtc))
            {
                asset ??= t.AssetType.ToString();
                var signedQty = t.Side == Side.Buy ? t.Quantity : -t.Quantity;
                var price = t.Price;
                var commission = t.Commission;
                var remaining = signedQty;
                var mult = t.AssetType == AssetType.Option ? 100m : 1m;

                while (remaining != 0 && lots.Count > 0)
                {
                    var lot = lots[0];
                    if (Math.Sign(lot.Qty) == Math.Sign(remaining))
                    {
                        var absLot = Math.Abs(lot.Qty);
                        var absRem = Math.Abs(remaining);
                        var newQty = lot.Qty + remaining;
                        var newPrice = lot.Qty != 0
                            ? (lot.Price * absLot + price * absRem) / (absLot + absRem)
                            : price;
                        lots[0] = (newQty, newPrice, lot.Commission + commission);
                        remaining = 0;
                        break;
                    }

                    var matched = Math.Min(Math.Abs(remaining), Math.Abs(lot.Qty)) * Math.Sign(lot.Qty);
                    var closeQty = Math.Abs(matched);
                    var pnl = remaining > 0
                        ? (lot.Price - price) * closeQty * mult
                        : (price - lot.Price) * closeQty * mult;
                    pnl -= commission * (closeQty / Math.Abs(signedQty));
                    pnl -= lot.Commission * (closeQty / Math.Abs(lot.Qty));
                    realized += pnl;

                    remaining -= matched;
                    var lotNewQty = lot.Qty - matched;
                    var lotNewCommission = lot.Commission - lot.Commission * (closeQty / Math.Abs(lot.Qty));
                    if (lotNewQty == 0) lots.RemoveAt(0);
                    else lots[0] = (lotNewQty, lot.Price, lotNewCommission);
                }

                if (remaining != 0)
                    lots.Insert(0, (remaining, price, commission));
            }

            if (realized == 0m) continue;
            result.Add(new RealizedPnlRowDto(
                group.Key.AccountId,
                group.Key.Symbol,
                asset ?? "Stock",
                null,
                "USD",
                Math.Round(realized, 2),
                0m));
        }

        return result;
    }

    private async Task<HashSet<string>?> ResolveAllowedAccountIdsAsync(string? accountId, TradeEnv? env, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(accountId))
        {
            if (env is null) return new HashSet<string> { accountId };
            var acc = await accountRepository.GetByAccountIdAsync(accountId, ct);
            return acc is not null && acc.Environment == env ? new HashSet<string> { accountId } : new HashSet<string>();
        }
        if (env is null) return null;
        var accounts = await accountRepository.GetAllAsync(ct);
        return new HashSet<string>(accounts.Where(a => a.Environment == env).Select(a => a.AccountId));
    }
}
