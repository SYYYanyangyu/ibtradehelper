using IBTrade.Application.Orders.Dtos;
using IBTrade.Domain.Entities;
using IBTrade.Domain.Enums;

namespace IBTrade.Application.Orders;

/// <summary>风控配置（可在运行时通过 API 调整）。</summary>
public class RiskSettings
{
    /// <summary>总开关：关闭则拒绝一切下单。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>交易模式。实盘需显式置为 Live 且同时勾选 LiveAcknowledged（默认 Paper 模拟盘）。</summary>
    public TradingMode Mode { get; set; } = TradingMode.Paper;

    /// <summary>
    /// 实盘风险确认。仅当 Mode==Live 且本字段为 true 时才允许真实下单；
    /// 默认 false，作为安全护栏防止误触实盘。切换回 Paper 后应视为失效，
    /// 由前端在切到 Live 时强制用户主动勾选。
    /// </summary>
    public bool LiveAcknowledged { get; set; }

    /// <summary>最大同时持仓订单数（0=不限）。</summary>
    public int MaxOpenPositions { get; set; } = 5;

    /// <summary>单笔订单最大金额（按 数量×限价/市价估计，0=不限）。</summary>
    public decimal MaxOrderAmount { get; set; } = 50_000m;

    /// <summary>当日累计亏损熔断线（以成交盈亏计，0=不熔断）。</summary>
    public decimal DailyLossLimit { get; set; } = 0m;
}

/// <summary>风控检查：下单前校验，拦截越界/实盘未授权/熔断等情况。</summary>
public interface IRiskGuard
{
    RiskSettings Settings { get; }
    void Update(RiskSettings settings);
    (bool Ok, string? Reason) Check(PlaceOrderRequest req, IReadOnlyList<Order> openOrders, decimal dayRealizedPnl, decimal? estimatedPrice);
}

public class RiskGuard : IRiskGuard
{
    private readonly object _gate = new();
    private RiskSettings _settings = new();

    public RiskSettings Settings
    {
        get { lock (_gate) return _settings; }
    }

    public void Update(RiskSettings settings)
    {
        lock (_gate) _settings = settings;
    }

    public (bool Ok, string? Reason) Check(
        PlaceOrderRequest req, IReadOnlyList<Order> openOrders, decimal dayRealizedPnl, decimal? estimatedPrice)
    {
        RiskSettings s;
        lock (_gate) s = _settings;

        if (!s.Enabled)
            return (false, "交易总开关已关闭");

        if (s.Mode == TradingMode.Live && !s.LiveAcknowledged)
            return (false, "实盘模式需先在风控配置中勾选「确认实盘风险」（LiveAcknowledged=true）。未确认前拒绝一切真实下单。");

        if (req.Quantity <= 0)
            return (false, "下单数量必须为正");

        if (s.MaxOpenPositions > 0 && openOrders.Count >= s.MaxOpenPositions)
            return (false, $"超出最大持仓数上限（{s.MaxOpenPositions}）");

        if (s.MaxOrderAmount > 0)
        {
            // 失败-关闭：缺少可估算价格时，绝不让市价单绕过金额上限（S-3）。
            if (!estimatedPrice.HasValue)
                return (false, "无法估算成交金额，单笔金额上限无法校验。市价单请传入 EstimatedPrice（如最新市价）或改用限价单。");
            var amount = estimatedPrice.Value * req.Quantity;
            if (amount > s.MaxOrderAmount)
                return (false, $"单笔金额 ${amount:F2} 超出上限 ${s.MaxOrderAmount:F2}");
        }

        if (s.DailyLossLimit > 0 && dayRealizedPnl < -s.DailyLossLimit)
            return (false, $"当日亏损 ${(-dayRealizedPnl):F2} 已达熔断线 ${s.DailyLossLimit:F2}");

        return (true, null);
    }
}
