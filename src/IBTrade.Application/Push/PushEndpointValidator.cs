using System.Net;
using System.Text.RegularExpressions;

namespace IBTrade.Application.Push;

/// <summary>
/// Web Push订阅端点SSRF防护验证器
/// </summary>
public static class PushEndpointValidator
{
    private static readonly string[] TrustedPushProviders = new[]
    {
        "fcm.googleapis.com",
        "android.googleapis.com",
        "updates.push.services.mozilla.com",
        "updates-autopush.stage.mozaws.net",
        "updates-autopush.dev.mozaws.net",
        "web.push.apple.com",
        "wns2-am3p.notify.windows.com",
        "wns2-bn1p.notify.windows.com",
        "wns2-db5p.notify.windows.com",
        "wns2-sn1p.notify.windows.com",
        "wns2-par02p.notify.windows.com"
    };

    private static readonly Regex WildcardPatterns = new(
        @"^(wns2-[a-z0-9]+\.notify\.windows\.com)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    /// <summary>
    /// 验证推送端点是否安全（防止SSRF攻击）
    /// </summary>
    public static void ValidateEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("订阅端点不能为空");

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            throw new ArgumentException("订阅端点必须是有效的绝对URL");

        // 1. 必须HTTPS
        if (uri.Scheme != "https")
            throw new ArgumentException("订阅端点必须使用HTTPS协议");

        // 2. 禁止userinfo
        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("订阅端点不得包含用户信息(userinfo)");

        // 3. 禁止非标准端口
        if (uri.Port != 443)
            throw new ArgumentException($"订阅端点必须使用标准HTTPS端口(443), 当前: {uri.Port}");

        // 4. 禁止IP地址
        if (IPAddress.TryParse(uri.Host, out _))
            throw new ArgumentException("订阅端点必须使用域名，不得使用IP地址");

        // 5. 白名单验证
        var host = uri.Host.ToLowerInvariant();
        
        // 精确匹配
        if (TrustedPushProviders.Contains(host))
            return;

        // 通配符匹配
        if (WildcardPatterns.IsMatch(host))
            return;

        throw new ArgumentException($"订阅端点域名 '{host}' 不在可信推送服务白名单中");
    }

    /// <summary>
    /// 检查端点是否有效（不抛异常版本）
    /// </summary>
    public static bool IsValidEndpoint(string endpoint)
    {
        try
        {
            ValidateEndpoint(endpoint);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
