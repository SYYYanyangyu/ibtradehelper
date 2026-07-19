using System.ComponentModel.DataAnnotations;
using IBTrade.Domain.Enums;

namespace IBTrade.Application.Orders.Dtos;

/// <summary>下单请求（来自 API 或自动交易引擎）。</summary>
public class PlaceOrderRequest
{
    [Required(ErrorMessage = "Symbol 必填")]
    public string Symbol { get; set; } = "";

    public OrderSide Side { get; set; } = OrderSide.Buy;
    public OrderType Type { get; set; } = OrderType.Market;

    [Range(0.0001d, 10_000_000d, ErrorMessage = "下单数量必须为正且不超过安全上限")]
    public decimal Quantity { get; set; }

    /// <summary>限价单必填；市价单忽略（具体校验见 OrderService）。</summary>
    public decimal? LimitPrice { get; set; }

    /// <summary>
    /// 估算成交价（可选）。用于风控「单笔金额上限」校验：
    /// 限价单默认用 LimitPrice；市价单若无可靠实时价，调用方应传入最新市价（如前端行情），
    /// 否则当 MaxOrderAmount&gt;0 时会被风控直接拒绝（失败-关闭，绝不静默放行）。
    /// </summary>
    public decimal? EstimatedPrice { get; set; }

    /// <summary>触发策略（手动下单可空）。</summary>
    public string? Strategy { get; set; }

    public string? Note { get; set; }
}
