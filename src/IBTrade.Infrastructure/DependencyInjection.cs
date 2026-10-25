using IBTrade.Application.Ai;
using IBTrade.Application.IBKR;
using IBTrade.Application.OptionsIntel;
using IBTrade.Application.Orders;
using IBTrade.Domain.Repositories;
using IBTrade.Infrastructure.Ai;
using IBTrade.Infrastructure.IBKR;
using IBTrade.Infrastructure.IBKR.Tws;
using IBTrade.Application.MarketData;
using IBTrade.Infrastructure.MarketData;
using IBTrade.Infrastructure.OptionsIntel;
using IBTrade.Infrastructure.Orders;
using IBTrade.Infrastructure.Persistence;
using IBTrade.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IBTrade.Infrastructure;

/// <summary>
/// 基础设施层 DI 组装：注册 DbContext、仓储实现、外部客户端等。
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // 连接串解析策略（不再固化任何弱密码，缺失即启动失败）：
        // 1) 优先使用配置/环境变量 ConnectionStrings__Default（可含完整串，由部署方负责安全保管）；
        // 2) 否则由独立的环境变量 IBTRADE_DB_HOST/PORT/NAME/USER/PASSWORD 拼装（密码不再硬编码）。
        //    注意：ConnectionStrings__Default 中若仍写死 Password，应由部署方改为从密钥管理注入。
        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var dbHost = Environment.GetEnvironmentVariable("IBTRADE_DB_HOST") ?? "localhost";
            var dbPort = Environment.GetEnvironmentVariable("IBTRADE_DB_PORT") ?? "5432";
            var dbName = Environment.GetEnvironmentVariable("IBTRADE_DB_NAME") ?? "ibtrade";
            var dbUser = Environment.GetEnvironmentVariable("IBTRADE_DB_USER") ?? "aiuser";
            var dbPassword = Environment.GetEnvironmentVariable("IBTRADE_DB_PASSWORD");
            if (string.IsNullOrWhiteSpace(dbPassword))
                throw new InvalidOperationException(
                    "缺少数据库连接配置：请通过 ConnectionStrings__Default 或环境变量 IBTRADE_DB_PASSWORD（及 IBTRADE_DB_*）提供。" +
                    "已移除代码中硬编码的弱密码，杜绝默认弱口令风险。");
            connectionString = $"Host={dbHost};Port={dbPort};Database={dbName};Username={dbUser};Password={dbPassword}";
        }

        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

        // 注册 IFlexQueryClient 的 HTTP 实现。
        // 关键：IBKR Flex Web Service 在部分地区需经代理出网（否则真实 IP 直连会被 403 Forbidden）。
        // 代理来源优先级：配置 IBKR:Flex:Proxy > 环境变量 HTTPS_PROXY/ALL_PROXY/HTTP_PROXY。
        // 配置为空且无环境变量时不设代理（走系统默认），不影响无需代理的部署环境。
        var flexProxy = configuration["IBKR:Flex:Proxy"]
                        ?? Environment.GetEnvironmentVariable("HTTPS_PROXY")
                        ?? Environment.GetEnvironmentVariable("ALL_PROXY")
                        ?? Environment.GetEnvironmentVariable("HTTP_PROXY");
        services.AddHttpClient<FlexQueryClient>(client =>
            {
                // 关键：IBKR Flex Web Service 前置 Akamai WAF，会以 403 Forbidden 拒绝“无 User-Agent”的请求。
                // .NET HttpClient 默认不发 User-Agent，故必须显式设置一个，否则所有 Flex 调用都会 403。
                client.DefaultRequestHeaders.UserAgent.ParseAdd("IBTrade/1.0 (+FlexWebService)");
                // 大报表（含 Open Positions + NAV）经代理下载可能超过默认 100s
                client.Timeout = TimeSpan.FromMinutes(5);
            })
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                var handler = new HttpClientHandler();
                if (!string.IsNullOrWhiteSpace(flexProxy))
                {
                    handler.Proxy = new System.Net.WebProxy(flexProxy);
                    handler.UseProxy = true;
                }
                return handler;
            });
        // 使用 typed HttpClient 实例，避免再 new 一个未配超时/代理的 FlexQueryClient
        services.AddScoped<IFlexQueryClient>(sp => sp.GetRequiredService<FlexQueryClient>());

        services.AddScoped<IPositionRepository, PositionRepository>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<ITradeRepository, TradeRepository>();
        services.AddScoped<IDailySnapshotRepository, DailySnapshotRepository>();
        services.AddScoped<IPositionDailySnapshotRepository, PositionDailySnapshotRepository>();
        services.AddScoped<ICashTransactionRepository, CashTransactionRepository>();
        services.AddScoped<IRealizedPnlRepository, RealizedPnlRepository>();
        services.AddScoped<IDataFeedStatusRepository, DataFeedStatusRepository>();

        // TWS 实时客户端工厂（单例；每次同步按需 new 出短生命周期的 ITwsClient）
        services.AddSingleton<ITwsClientFactory, TwsClientFactory>();

        // 下单执行层（独立连接 / 单例）：下单客户端 + 订单持久化 + 风控 + 下单服务 + 对账
        services.AddSingleton<IRiskGuard, RiskGuard>();
        services.AddSingleton<TwsOrderClient>();
        services.AddSingleton<IOrderStore, EfOrderStore>();
        services.AddSingleton<IOrderService, OrderService>();
        services.AddSingleton<IOrderReconcileService, OrderReconcileService>();

        // AI 多厂商：openai / deepseek / qwen / claude（Key 占位；无 Key 回退启发式）
        services.Configure<LlmOptions>(configuration.GetSection(LlmOptions.SectionName));
        services.AddHttpClient("ibtrade-ai", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(45);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("IBTrade/1.0 (+AiStrategy)");
        });
        services.AddSingleton<ILlmProviderCatalog, LlmProviderCatalog>();
        services.AddSingleton<IAiStrategyAdvisor, MultiProviderLlmAdvisor>();

        var intelProxy = configuration["IBKR:Flex:Proxy"]
                         ?? Environment.GetEnvironmentVariable("HTTPS_PROXY")
                         ?? Environment.GetEnvironmentVariable("ALL_PROXY")
                         ?? Environment.GetEnvironmentVariable("HTTP_PROXY");
        services.AddSingleton<IOfficialIndexQuotes, FredIndexClient>();
        services.AddSingleton<IMacroCalendar, FedFomcCalendarClient>();

        services.AddHttpClient("ibtrade-intel", client =>
            {
                client.Timeout = TimeSpan.FromSeconds(12);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (compatible; IBTrade/1.0; +OptionsIntel)");
            })
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                var handler = new HttpClientHandler();
                if (!string.IsNullOrWhiteSpace(intelProxy))
                {
                    handler.Proxy = new System.Net.WebProxy(intelProxy);
                    handler.UseProxy = true;
                }
                return handler;
            });
        services.AddScoped<IHotOptionsUniverse, NasdaqHotNamesClient>();
        services.AddScoped<IOptionsChainClient, NasdaqOptionsChainClient>();
        services.AddScoped<ICompanyFundamentalsClient, NasdaqCompanyClient>();
        services.AddScoped<IMacroDataClient, MacroDataClient>();
        services.AddScoped<INewsFeedClient, NewsFeedClient>();
        services.AddScoped<PolymarketClient>();
        services.AddScoped<ManifoldClient>();
        services.AddScoped<IPredictionMarketClient, CompositePredictionClient>();
        services.AddScoped<IOptionsAiAdvisor, OptionsAiAdvisor>();

        return services;
    }
}
