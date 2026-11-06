using System.Security.Cryptography;
using System.Text;

namespace IBTrade.Application.Push.Dtos;

/// <summary>
/// 订阅令牌：服务器生成的不可猜测凭证，用于验证订阅所有权。
/// 客户端在订阅时获得此令牌，后续操作必须提供匹配的令牌。
/// </summary>
public static class SubscriptionToken
{
    /// <summary>
    /// 为订阅端点生成安全令牌（32字节随机+HMAC签名）
    /// </summary>
    public static string Generate(string endpoint, string secret)
    {
        var random = RandomNumberGenerator.GetBytes(32);
        var endpointBytes = Encoding.UTF8.GetBytes(endpoint);
        var combined = new byte[endpointBytes.Length + random.Length];
        Buffer.BlockCopy(endpointBytes, 0, combined, 0, endpointBytes.Length);
        Buffer.BlockCopy(random, 0, combined, endpointBytes.Length, random.Length);
        
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = hmac.ComputeHash(combined);
        var token = Convert.ToBase64String(random) + "." + Convert.ToBase64String(signature);
        return token;
    }

    /// <summary>
    /// 验证令牌是否匹配订阅端点
    /// </summary>
    public static bool Verify(string endpoint, string token, string secret)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 2) return false;

            var random = Convert.FromBase64String(parts[0]);
            var signature = Convert.FromBase64String(parts[1]);
            var endpointBytes = Encoding.UTF8.GetBytes(endpoint);
            var combined = new byte[endpointBytes.Length + random.Length];
            Buffer.BlockCopy(endpointBytes, 0, combined, 0, endpointBytes.Length);
            Buffer.BlockCopy(random, 0, combined, endpointBytes.Length, random.Length);
            
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var expectedSignature = hmac.ComputeHash(combined);

            return CryptographicOperations.FixedTimeEquals(signature, expectedSignature);
        }
        catch
        {
            return false;
        }
    }
}
