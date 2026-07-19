namespace IBTrade.Application.Orders.Dtos;

/// <summary>单标的对账行：本地成交净额 vs DB 持仓。</summary>
public class OrderReconcileRow
{
    public string Symbol { get; set; } = "";
    /// <summary>本地订单成交净股数（买+ / 卖-，按 FilledQuantity）。</summary>
    public decimal LocalFillNet { get; set; }
    /// <summary>持久化持仓合计（Flex/TWS 同步）。</summary>
    public decimal DbPositionQty { get; set; }
    /// <summary>差额 = LocalFillNet - DbPositionQty。</summary>
    public decimal Delta { get; set; }
    public int OpenOrderCount { get; set; }
    public int FilledOrderCount { get; set; }
    /// <summary>true = |delta| 超过容差，需关注。</summary>
    public bool Mismatch { get; set; }
}

public class OrderReconcileResult
{
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
    /// <summary>容差（股），默认 0.0001。</summary>
    public decimal Tolerance { get; set; } = 0.0001m;
    public int MismatchCount { get; set; }
    public string Summary { get; set; } = "";
    public List<OrderReconcileRow> Rows { get; set; } = new();
}
