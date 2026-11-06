namespace IBTrade.Application.Push.Dtos;

public record SubscribeRequest(
    string Endpoint,
    PushKeys Keys
);

public record PushKeys(
    string P256dh,
    string Auth
);

public record UnsubscribeRequest(string Endpoint);

public record NotificationPreferences(
    bool EnableScheduledBriefs,
    bool EnableBreakingNews,
    List<string> ScheduledBriefTimes,
    string Timezone
);

public record VapidPublicKeyResponse(string PublicKey);

public record SendPushRequest(
    string Title,
    string Body,
    string? Url = null,
    string? Tag = null,
    bool Important = false,
    string? Type = null
);
