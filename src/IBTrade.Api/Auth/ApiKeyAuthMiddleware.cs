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
///       · 生产环境：拒绝匿名“写操作”（POST/PUT/DELETE/PATCH），阻止未授权真实下单/启停/改配置；
///         只读看板（GET）仍可用，不影响查看。
/// 白名单（任何环境均放行）：<c>/health</c>、<c>/</c>、<c>/swagger*</c>。
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

        // 健康检查 / 根 / Swagger 文档永远放行（便于探针与本地调试文档）
        if (path == "/health" || path == "/" || path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Grok Bot 回传简报：用 GrokBot:WebhookKey 作为 Bearer，不要求引擎 ApiKey。
        if (HttpMethods.IsPost(context.Request.Method)
            && path.Equals("/api/news/brief", StringComparison.OrdinalIgnoreCase)
            && BearerMatchesWebhookKey(context))
        {
            await _next(context);
            return;
        }

        var configuredKey = _configuration["ApiKey"]
            ?? Environment.GetEnvironmentVariable(EnvVarName);

        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            // 未配置 ApiKey：生产环境禁止匿名写操作——最高风险是真实下单/启停/改配置被任意调用
            if (!_env.IsDevelopment() && IsWriteMethod(context.Request.Method))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "引擎未启用鉴权，已拒绝匿名写操作（下单/启停/改配置）。请配置 ApiKey（appsettings.ApiKey 或环境变量 IBTRADE_API_KEY）并在请求头携带 X-Api-Key 后重试。",
                    hint = "生产环境真实资金接口必须鉴权；仅只读看板(GET)可在未鉴权下访问。"
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

        if (!string.Equals(provided, configuredKey, StringComparison.Ordinal))
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

    private bool BearerMatchesWebhookKey(HttpContext context)
    {
        var expected = _configuration["GrokBot:WebhookKey"]?.Trim();
        if (string.IsNullOrWhiteSpace(expected)) return false;
        var auth = context.Request.Headers.Authorization.ToString();
        if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
        var provided = auth["Bearer ".Length..].Trim();
        return string.Equals(provided, expected, StringComparison.Ordinal);
    }

    private static bool IsWriteMethod(string method) =>
        !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && !HttpMethods.IsOptions(method);
}
