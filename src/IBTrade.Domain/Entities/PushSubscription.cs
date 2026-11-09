using IBTrade.Domain.Common;

namespace IBTrade.Domain.Entities;

public class PushSubscription : Entity
{
    public string Endpoint { get; set; } = "";
    public string P256dh { get; set; } = "";
    public string Auth { get; set; } = "";
    public string OwnershipToken { get; set; } = "";
    public bool EnableScheduledBriefs { get; set; } = true;
    public bool EnableBreakingNews { get; set; } = true;
    public string ScheduledBriefTimes { get; set; } = "08:00,21:00";
    public string Timezone { get; set; } = "Asia/Shanghai";
    public DateTime? LastNotifiedUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public bool IsActive { get; set; } = true;
    public int FailureCount { get; set; }
    public string? LastErrorMessage { get; set; }
}
