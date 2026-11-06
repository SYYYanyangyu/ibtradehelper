using IBTrade.Domain.Common;

namespace IBTrade.Domain.Entities;

public class NotificationHistory : Entity
{
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string? Url { get; set; }
    public string? Tag { get; set; }
    public DateTime SentAtUtc { get; set; }
    public int SubscriptionsSent { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public string? ContentHash { get; set; }
}
