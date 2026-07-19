using System;
using System.Collections.Generic;
using System.Linq;
using IBTrade.Application.Portfolio.Dtos;
using IBTrade.Domain.Entities;
using IBTrade.Domain.Enums;
using IBTrade.Domain.Repositories;

namespace IBTrade.Application.Portfolio;

/// <summary>
/// 组合盈亏服务：编排持仓快照与成交历史，计算看板指标。
/// 未实现盈亏取自 <see cref="Position"/> 快照（IBKR 已算好平均成本与市场价）；
/// 已实现盈亏基于成交历史，按 (账户, 标的) 分组做 FIFO 配对计算。
/// 支持按 <see cref="TradeEnv"/> 过滤真实/模拟账户。
/// </summary>
public class PortfolioService(
    IPositionRepository positionRepository,
    ITradeRepository tradeRepository,
    IAccountRepository accountRepository,
    IRealizedPnlRepository realizedPnlRepository) : IPortfolioService
{
    public async Task<PortfolioSummaryDto> GetSummaryAsync(string? accountId, TradeEnv? env = null, CancellationToken ct = default)
    {
        var allowed = await ResolveAllowedAccountIdsAsync(accountId, env, ct);

        var positionsAll = await positionRepository.GetAllAsync(ct);
        var tradesAll = await tradeRepository.GetAllAsync(ct);
        var flexPnlAll = await realizedPnlRepository.GetAllAsync(ct);

        var positions = positionsAll
            .Where(p => allowed is null || allowed.Contains(p.AccountId))
            .ToList();
        var trades = tradesAll
            .Where(t => allowed is null || allowed.Contains(t.AccountId))
            .ToList();

        // Flex SymbolSummary（IB 官方）按账户+标的汇总；有则优先于本地 FIFO（避免「看着像赚、其实浮亏」）
        var flexByKey = flexPnlAll
            .Where(r => allowed is null || allowed.Contains(r.AccountId))
            .GroupBy(r => (r.AccountId, Symbol: r.Symbol.Trim()), StringTupleKeyComparer.Instance)
            .ToDictionary(
                g => g.Key,
                g => (Realized: g.Sum(x => x.Realized), Unrealized: g.Sum(x => x.Unrealized)));

        var fifoByKey = ComputeRealizedPnl(trades);
        // Flex SymbolSummary 常有行但 fifoPnlRealized 全 0（本账户即如此）——不能当权威已实现
        var useFlexRealized = flexByKey.Values.Any(x => x.Realized != 0m);
        var useFlexUnrealized = flexByKey.Values.Any(x => x.Unrealized != 0m);

        var positionDtos = positions.Select(p =>
        {
            var key = (p.AccountId, Symbol: p.Symbol.Trim());
            flexByKey.TryGetValue(key, out var flex);
            var realized = useFlexRealized
                ? flex.Realized
                : (fifoByKey.TryGetValue((p.AccountId, p.Symbol), out var r) ? r : 0m);
            var dto = PositionPnlDto.FromEntity(p, realized);
            // 成本缺失（Flex 常给 costBasis=0）时，(市价−0)×数量会把整段市值算成「浮盈」——改用 SymbolSummary 未实现
            if (useFlexUnrealized && (p.AvgCost == 0m || p.MarketPrice == 0m))
                dto = dto with { UnrealizedPnl = flex.Unrealized };
            return dto;
        }).ToList();

        var realizedOnPositions = positionDtos.Sum(x => x.RealizedPnl);
        var realizedFromTrades = Math.Round(fifoByKey.Values.Sum(), 2);
        // 空仓时持仓行没有已实现；用成交 FIFO 汇总，避免 Live 总盈亏永远显示 0
        var totalRealized = positionDtos.Count > 0
            ? (useFlexRealized ? realizedOnPositions : Math.Round(realizedOnPositions != 0 ? realizedOnPositions : realizedFromTrades, 2))
            : realizedFromTrades;
        var totalUnrealized = positionDtos.Sum(x => x.UnrealizedPnl);

        return new PortfolioSummaryDto(
            AccountId: string.IsNullOrWhiteSpace(accountId) ? (env is null ? "ALL" : env.ToString()!) : accountId!,
            TotalMarketValue: positionDtos.Sum(x => x.MarketValue),
            TotalUnrealizedPnl: totalUnrealized,
            TotalRealizedPnl: totalRealized,
            TotalCostBasis: positionDtos.Sum(x => x.CostBasis),
            TotalPnl: Math.Round(totalUnrealized + totalRealized, 2),
            PositionCount: positionDtos.Count,
            AsOfUtc: DateTime.UtcNow,
            Positions: positionDtos);
    }

    private sealed class StringTupleKeyComparer : IEqualityComparer<(string AccountId, string Symbol)>
    {
        public static readonly StringTupleKeyComparer Instance = new();
        public bool Equals((string AccountId, string Symbol) x, (string AccountId, string Symbol) y) =>
            string.Equals(x.AccountId, y.AccountId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Symbol, y.Symbol, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string AccountId, string Symbol) obj) =>
            HashCode.Combine(obj.AccountId.ToUpperInvariant(), obj.Symbol.ToUpperInvariant());
    }

    /// <summary>
    /// 计算允许查询的账户集合：
    /// - 指定 accountId：仅该账户（若同时指定 env 且冲突则返回空集）；
    /// - 仅指定 env：该环境下全部账户；
    /// - 都不指定：null 表示全部账户。
    /// </summary>
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

    /// <summary>
    /// FIFO 已实现盈亏：买入入队成为成本批次（含按比例资本化的佣金），
    /// 卖出按时间顺序匹配最早批次，差额即为已实现盈亏。
    /// </summary>
    /// <summary>
    /// FIFO 已实现盈亏（支持多头与空头）：
    /// 以 (账户, 标的) 分组，按时间排序逐笔处理。每个批次带符号——&gt;0 多头、&lt;0 空头。
    /// - 同向成交：并入首个批次（按数量加权均价，佣金全额计入批次）。
    /// - 反向成交：用当前成交平掉对手盘批次，差额（扣双边分摊佣金）即已实现盈亏。
    /// 多头平仓 (卖出)：盈亏 = (卖出价 - 建仓价) × 平仓量；空头平仓 (买入)：盈亏 = (建仓价 - 买入价) × 平仓量。
    /// </summary>
    private static Dictionary<(string AccountId, string Symbol), decimal> ComputeRealizedPnl(
        IReadOnlyList<Trade> trades)
    {
        var result = new Dictionary<(string, string), decimal>();

        foreach (var group in trades.GroupBy(t => (t.AccountId, t.Symbol)))
        {
            // 批次：(数量带符号, 均价, 剩余佣金)
            var lots = new List<(decimal Qty, decimal Price, decimal Commission)>();
            decimal realized = 0m;

            foreach (var t in group.OrderBy(x => x.TradeTimeUtc))
            {
                var signedQty = t.Side == Side.Buy ? t.Quantity : -t.Quantity;
                var price = t.Price;
                var commission = t.Commission;
                var remaining = signedQty;

                while (remaining != 0 && lots.Count > 0)
                {
                    var lot = lots[0];

                    // 同向：并入首个批次（按数量加权均价，佣金全额保留待平仓时分摊）
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

                    // 反向：平掉部分/全部对手盘批次
                    var matched = Math.Min(Math.Abs(remaining), Math.Abs(lot.Qty)) * Math.Sign(lot.Qty);
                    var closeQty = Math.Abs(matched);
                    // 期权成交价为权利金，合约乘数 100
                    var mult = t.AssetType == AssetType.Option ? 100m : 1m;
                    var pnl = remaining > 0
                        ? (lot.Price - price) * closeQty * mult   // 买入平空
                        : (price - lot.Price) * closeQty * mult;  // 卖出平多
                    // 佣金按平仓数量占比分摊（本次成交佣金 + 被平批次剩余佣金）
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
                {
                    // 无对手盘批次，新开仓
                    lots.Insert(0, (remaining, price, commission));
                }
            }

            result[group.Key] = realized;
        }

        return result;
    }
}
