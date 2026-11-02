using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace IBTrade.Api.Auth;

/// <summary>
/// 轻量 API Key 鉴权中间件（面向本地自托管交易引擎，避免真实下单/启停接口匿名可调用）。
///
/// 安全策略（secure-by-default）：
///  - 已配置 ApiKey：所有请求必须携带正确 <c>X-Api-Key</c>（或 <c>Authorization: Bearer</c>）头，否则返回 401。
///  - 未配置 ApiKey：
///       · 开发环境：放行（保持本地联调便利），启动时已输出告警。
///       · 生产环境：拒绝全部业务请求（含只读持仓），进程在启动时也会直接失败。
/// 白名单：任何环境放行 <c>/health</c>。开发环境另放行 <c>/</c> 与 <c>/swagger*</c>。
///
/// Key 来源（优先级）：appsettings <c>ApiKey</c> 段 → 环境变量 <c>IBTRADE_API_KEY</c>。
/// </summary>
public sealed class ApiKeyAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;
    private const string HeaderName = "X-Api-Key";
    private const string EnvVarName = "IBTRADE_API_KEY";

    public ApiKeyAuthMiddleware(RequestDelegate next, IConfiguration configuration, IWebHostEnvironment env)
    {
        _next = next;
        _configuration = configuration;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // 健康检查永远放行，供本机探针使用。根路径与 Swagger 只在开发环境放行。
        if (path == "/health")
        {
            await _next(context);
            return;
        }

        if (_env.IsDevelopment() &&
            (path == "/" || path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        var configuredKey = _configuration["ApiKey"]
            ?? Environment.GetEnvironmentVariable(EnvVarName);

        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            // 未配置 ApiKey：生产环境拒绝全部业务请求。持仓和订单都是敏感数据，不能匿名读取。
            if (!_env.IsDevelopment())
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "引擎未启用鉴权，已拒绝匿名访问。请配置 IBTRADE_API_KEY 并在请求头携带 X-Api-Key。",
                    hint = "生产环境持仓、订单和下单接口都必须鉴权。仅 /health 可匿名访问。"
                });
                return;
            }

            await _next(context);
            return;
        }

        // 已配置 ApiKey：校验请求携带的 key
        var provided = context.Request.Headers[HeaderName].ToString().Trim();
        if (string.IsNullOrEmpty(provided))
        {
            var auth = context.Request.Headers["Authorization"].ToString();
            if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                provided = auth["Bearer ".Length..].Trim();
        }

        if (!FixedTimeEquals(provided, configuredKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "未授权：ApiKey 无效或缺失。请在请求头携带 X-Api-Key（或 Authorization: Bearer <key>）。"
            });
            return;
        }

        await _next(context);
    }

    private static bool FixedTimeEquals(string provided, string configured)
    {
        var left = Encoding.UTF8.GetBytes(provided);
        var right = Encoding.UTF8.GetBytes(configured);
        if (left.Length != right.Length)
        {
            CryptographicOperations.FixedTimeEquals(left, left);
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(left, right);
    }
}
