using IBTrade.Domain.Enums;

namespace IBTrade.Application.IBKR;

/// <summary>TWS / IB Gateway 连接配置（由 appsettings 的 TWS 段或环境变量 TWS__* 注入）。</summary>
public class TwsOptions
{
    /// <summary>Gateway 主机；本机 TWS 用 127.0.0.1。</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>API 端口：TWS 实盘 7496 / TWS 模拟 7497 / IB Gateway 实盘 4001 / 模拟 4002。</summary>
    public int Port { get; set; } = 7497;

    /// <summary>
    /// 候选端口列表：同步时依次尝试，哪个开着连哪个，从而同时兼容 TWS 与 IB Gateway。
    /// 若显式设置了单端口（TWS:Port），则只用该端口。
    /// </summary>
    public int[] Ports { get; set; } = { 7497, 7496, 4002, 4001 };

    /// <summary>客户端 ID（同一 TWS 实例可连多个，需各不相同）。</summary>
    public int ClientId { get; set; } = 1;

    /// <summary>是否启用 TWS 实时接入（默认关闭）。</summary>
    public bool Enabled { get; set; }

    /// <summary>自动刷新间隔（分钟），用于后台轮询拉取实时持仓。</summary>
    public int RefreshMinutes { get; set; } = 2;

    /// <summary>下单客户端使用的 clientId（须与看板实时推送 clientId=1、历史数据 clientId=ClientId+2 不同）。</summary>
    public int OrderClientId { get; set; } = 5;

    /// <summary>交易模式：Paper（模拟盘，默认）/ Live（实盘，需显式开启）。</summary>
    public TradingMode Mode { get; set; } = TradingMode.Paper;
}
