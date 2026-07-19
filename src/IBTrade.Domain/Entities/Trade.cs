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

    /// <summary>成交单价</summary>
    public decimal Price { get; set; }

    /// <summary>佣金/手续费</summary>
    public decimal Commission { get; set; }

    /// <summary>成交时间（UTC）</summary>
    public DateTime TradeTimeUtc { get; set; }

    /// <summary>成交额（不含费）= 数量 × 价格</summary>
    public decimal GrossAmount => Quantity * Price;

    /// <summary>净发生额 = 买加费 / 卖减费</summary>
    public decimal NetAmount => Side == Side.Buy
        ? -(GrossAmount + Commission)
        : (GrossAmount - Commission);
}
