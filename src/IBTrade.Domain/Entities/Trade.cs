using IBTrade.Domain.Common;
using IBTrade.Domain.Enums;

namespace IBTrade.Domain.Entities;

/// <summary>成交：某一笔已发生的买卖记录。</summary>
public class Trade : Entity
{
    public required string AccountId { get; set; }
    public required string Symbol { get; set; }
    public AssetType AssetType { get; set; }
    public Side Side { get; set; }
    public string Currency { get; set; } = "USD";

    /// <summary>成交数量（正数）</summary>
    public decimal Quantity { get; set; }

    /// <summary>成交单价（正股每股；期权为每股权利金）</summary>
    public decimal Price { get; set; }

    /// <summary>合约乘数：正股 1，美股期权通常 100。</summary>
    public decimal Multiplier { get; set; } = 1m;

    /// <summary>佣金/手续费（入库为正数）</summary>
    public decimal Commission { get; set; }

    /// <summary>成交时间（UTC）</summary>
    public DateTime TradeTimeUtc { get; set; }

    public decimal EffectiveMultiplier => Multiplier > 0 ? Multiplier : 1m;

    /// <summary>成交额（不含费）= 数量 × 价格 × 乘数</summary>
    public decimal GrossAmount => Quantity * Price * EffectiveMultiplier;

    /// <summary>净发生额：买入为负现金，卖出为正现金。</summary>
    public decimal NetAmount => Side == Side.Buy
        ? -(GrossAmount + Math.Abs(Commission))
        : (GrossAmount - Math.Abs(Commission));
}
