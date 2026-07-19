using IBTrade.Domain.Common;
using IBTrade.Domain.Enums;

namespace IBTrade.Domain.Entities;

/// <summary>IBKR 账户（一个账户对应一组持仓和成交）。</summary>
public class Account : Entity
{
    /// <summary>IBKR 账户号（业务主键，如 U1234567）</summary>
    public required string AccountId { get; set; }

    /// <summary>券商名称，如 "Interactive Brokers"</summary>
    public string Broker { get; set; } = "Interactive Brokers";

    /// <summary>账户基础币种</summary>
    public string BaseCurrency { get; set; } = "USD";

    /// <summary>账户别名 / 备注</summary>
    public string? Alias { get; set; }

    /// <summary>数据来源环境：Live=真实账户, Paper=模拟账户</summary>
    public TradeEnv Environment { get; set; } = TradeEnv.Live;
}
