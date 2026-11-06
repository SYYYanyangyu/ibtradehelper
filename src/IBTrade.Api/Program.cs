using IBTrade.Application;
using IBTrade.Infrastructure;
using IBTrade.Api.Auth;
using IBTrade.Api.HostedServices;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// 本地密钥（gitignored）：复制 appsettings.Example.json → appsettings.Secrets.json
// 同时尝试 ContentRoot 与输出目录，避免 cwd/--no-build 导致读不到。
foreach (var dir in new[] { builder.Environment.ContentRootPath, AppContext.BaseDirectory })
{
    var secretsPath = Path.Combine(dir, "appsettings.Secrets.json");
    if (!File.Exists(secretsPath)) continue;
    builder.Configuration.AddJsonFile(secretsPath, optional: false, reloadOnChange: true);
    break;
}

// ---- 服务注册（Composition Root）----
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

// 各层通过扩展方法自组装，Api 保持清爽
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor(); // 供控制器提取来源 IP，用于操作审计

// 速率限制：防止推送订阅/更新滥用
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    
    // 订阅端点：每IP每分钟5次
    options.AddPolicy("push_subscribe", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
    
    // 更新偏好：每IP每分钟10次
    options.AddPolicy("push_preferences", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
    
    // 取消订阅：每IP每分钟3次
    options.AddPolicy("push_unsubscribe", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});

// Grok Bot webhook 配置
builder.Services.Configure<IBTrade.Api.Controllers.GrokBotOptions>(
    builder.Configuration.GetSection("GrokBot"));
builder.Services.AddSingleton<IBTrade.Api.Controllers.FinanceBriefStore>();
builder.Services.AddHttpClient();

// VAPID Web Push 配置
builder.Services.Configure<IBTrade.Application.Push.VapidOptions>(
    builder.Configuration.GetSection("Vapid"));

// TWS 实时接入配置（appsettings 的 TWS 段或环境变量 TWS__*）
var twsOptions = new IBTrade.Application.IBKR.TwsOptions
{
    Host = builder.Configuration["TWS:Host"] ?? "127.0.0.1",
    Port = int.TryParse(builder.Configuration["TWS:Port"], out var p) ? p : 4002,
    PaperPorts = builder.Configuration.GetSection("TWS:PaperPorts").Get<int[]>() is { Length: > 0 } paperPorts
        ? paperPorts
        : new[] { 4002, 7497 },
    LivePorts = builder.Configuration.GetSection("TWS:LivePorts").Get<int[]>() is { Length: > 0 } livePorts
        ? livePorts
        : new[] { 4001, 7496 },
    Ports = builder.Configuration.GetSection("TWS:PaperPorts").Get<int[]>() is { Length: > 0 } legacy
        ? legacy
        : new[] { 4002, 7497 },
    ClientId = int.TryParse(builder.Configuration["TWS:ClientId"], out var c) ? c : 1,
    OrderClientId = int.TryParse(builder.Configuration["TWS:OrderClientId"], out var oc) ? oc : 5,
    Mode = (builder.Configuration["TWS:Mode"] ?? "Paper").Equals("Live", StringComparison.OrdinalIgnoreCase)
        ? IBTrade.Domain.Enums.TradingMode.Live : IBTrade.Domain.Enums.TradingMode.Paper,
    Enabled = ResolveTwsEnabled(builder.Environment, builder.Configuration),
    RefreshMinutes = int.TryParse(builder.Configuration["TWS:RefreshMinutes"], out var r) ? r : 2
};
builder.Services.AddSingleton(twsOptions);

static bool ResolveTwsEnabled(IHostEnvironment env, ConfigurationManager config)
{
    var raw = config["TWS:Enabled"];
    if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) || raw == "1") return true;
    if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase) || raw == "0") return false;
    // 开发环境默认每 RefreshMinutes 分钟自动 Gateway 同步，避免 Flex 缺段后看板每天变假数据
    return env.IsDevelopment();
}

// 每日净值快照后台服务：默认关闭，需环境变量 DAILY_SNAPSHOT__ENABLED=true 才启用
if ((builder.Configuration["DAILY_SNAPSHOT__ENABLED"] ?? "false").Equals("true", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddHostedService<DailySnapshotHostedService>();

// TWS 实时同步后台服务：Development 默认开启；生产需 TWS__ENABLED=true
if (twsOptions.Enabled)
    builder.Services.AddHostedService<TwsSyncHostedService>();

builder.Services.AddHostedService<NewsTapeHostedService>();
builder.Services.AddHostedService<ScheduledPushHostedService>();

// 跨域：开发环境为本地联调便利放开；生产环境必须显式配置 AllowedOrigins，否则默认拒绝跨域（安全优先）。
// 配置方式（appsettings 或环境变量）：AllowedOrigins = "https://a.example.com,https://b.example.com"
var allowedOrigins = builder.Configuration["AllowedOrigins"]?
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(p =>
    {
        if (builder.Environment.IsDevelopment())
        {
            p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        }
        else if (allowedOrigins is { Length: > 0 })
        {
            p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
        }
        else
        {
            // 生产环境未配置来源时拒绝任何跨域请求
            p.WithOrigins().AllowAnyHeader().AllowAnyMethod();
        }
    }));

var app = builder.Build();

// 启动时应用 EF 迁移（含 orders 表），避免手工 update 遗漏
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<IBTrade.Infrastructure.Persistence.AppDbContext>();
        db.Database.Migrate();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "数据库迁移失败，API 可能无法读写订单/持仓。");
    }
}

// 启动期安全自检：生产环境没有 ApiKey 就拒绝启动，避免持仓和下单接口被匿名访问。
if (string.IsNullOrWhiteSpace(builder.Configuration["ApiKey"]) &&
    string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("IBTRADE_API_KEY")))
{
    if (app.Environment.IsProduction())
    {
        throw new InvalidOperationException(
            "生产环境必须配置 IBTRADE_API_KEY（或 appsettings.ApiKey）。交易引擎拒绝以匿名方式启动。");
    }

    app.Logger.LogWarning(
        "交易引擎未配置 ApiKey：当前是开发环境，请求暂时放行。生产环境将拒绝启动。");
}

// ---- 中间件管道 ----
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseRateLimiter();
app.UseMiddleware<ApiKeyAuthMiddleware>();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");
app.MapGet("/", () => Results.Ok(new { service = "IBTrade", status = "ok" }));

app.Services.GetRequiredService<IBTrade.Application.Quant.TrendScalper.ITrendScalperEngine>().ResumeIfWasRunning();

app.Run();
