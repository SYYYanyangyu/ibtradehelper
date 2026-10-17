using IBTrade.Domain.Enums;

namespace IBTrade.Application.IBKR;

/// <summary>TWS / IB Gateway 连接配置（由 appsettings 的 TWS 段或环境变量 TWS__* 注入）。</summary>
public class TwsOptions
{
    /// <summary>Gateway 主机；本机 TWS 用 127.0.0.1。</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>API 端口：TWS 实盘 7496 / TWS 模拟 7497 / IB Gateway 实盘 4001 / 模拟 4002。</summary>
    public int Port { get; set; } = 4002;

    /// <summary>Paper 只连这些口：Gateway 模拟 4002、TWS 模拟 7497。绝不混扫实盘口。</summary>
    public int[] PaperPorts { get; set; } = { 4002, 7497 };

    /// <summary>Live 只连这些口：Gateway 实盘 4001、TWS 实盘 7496。</summary>
    public int[] LivePorts { get; set; } = { 4001, 7496 };

    /// <summary>兼容旧配置。新逻辑请用 <see cref="PortsFor"/>。</summary>
    public int[] Ports { get; set; } = { 4002, 7497 };

    public int[] PortsFor(TradingMode mode)
    {
        var list = mode == TradingMode.Live ? LivePorts : PaperPorts;
        if (list is { Length: > 0 }) return list;
        return mode == TradingMode.Live ? new[] { 4001, 7496 } : new[] { 4002, 7497 };
    }

    public static bool IsLivePort(int port) => port is 4001 or 7496;
    public static bool IsPaperPort(int port) => port is 4002 or 7497;

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
