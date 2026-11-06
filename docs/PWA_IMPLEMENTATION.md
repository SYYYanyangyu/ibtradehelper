# PWA 和 Web Push 实现说明

本文档详细说明 IBTrade PWA 和 Web Push 推送通知的技术实现。

## 架构概览

```
┌─────────────────────────────────────────────────────────────┐
│                         前端 (React)                          │
├─────────────────────────────────────────────────────────────┤
│  • Service Worker (sw.js)                                   │
│  • Push 订阅管理 (lib/push.ts)                              │
│  • 通知设置UI (PushNotificationSettings.tsx)                │
│  • PWA Manifest (manifest.json)                             │
└────────────────────┬────────────────────────────────────────┘
                     │ HTTPS / REST API
┌────────────────────▼────────────────────────────────────────┐
│                    后端 (.NET 8 API)                         │
├─────────────────────────────────────────────────────────────┤
│  Application Layer:                                         │
│  • IPushNotificationService                                 │
│  • PushNotificationService (WebPush库)                      │
│  • VapidOptions                                             │
│                                                             │
│  Infrastructure Layer:                                      │
│  • PushSubscriptionRepository                               │
│  • NotificationHistoryRepository                            │
│  • PostgreSQL (push_subscriptions, notification_histories)  │
│                                                             │
│  API Layer:                                                 │
│  • PushController (/api/push/*)                            │
│  • ScheduledPushHostedService (定时任务)                    │
└─────────────────────────────────────────────────────────────┘
```

## 文件结构

### 前端文件

```
frontend/
├── public/
│   ├── manifest.json              # PWA 清单
│   ├── sw.js                      # Service Worker
│   └── icons/                     # PWA 图标
│       ├── icon-72x72.svg
│       ├── icon-192x192.svg
│       └── icon-512x512.svg
├── src/
│   ├── lib/
│   │   ├── push.ts               # Push 订阅管理
│   │   ├── sw-register.ts        # SW 注册
│   │   └── __tests__/
│   │       └── push.test.ts      # 单元测试
│   ├── components/
│   │   └── PushNotificationSettings.tsx  # 通知设置UI
│   └── pages/
│       └── NewsBriefPage.tsx     # 金融简报页面（集成推送）
└── index.html                     # 添加 PWA meta 标签
```

### 后端文件

```
src/
├── IBTrade.Domain/
│   ├── Entities/
│   │   ├── PushSubscription.cs           # 订阅实体
│   │   └── NotificationHistory.cs        # 通知历史实体
│   └── Repositories/
│       ├── IPushSubscriptionRepository.cs
│       └── INotificationHistoryRepository.cs
│
├── IBTrade.Application/
│   ├── Push/
│   │   ├── Dtos/
│   │   │   └── PushSubscriptionDtos.cs
│   │   ├── IPushNotificationService.cs
│   │   └── PushNotificationService.cs     # 核心推送逻辑
│   └── DependencyInjection.cs            # DI 注册
│
├── IBTrade.Infrastructure/
│   ├── Persistence/
│   │   ├── Repositories/
│   │   │   ├── PushSubscriptionRepository.cs
│   │   │   └── NotificationHistoryRepository.cs
│   │   ├── Configurations/
│   │   │   ├── PushSubscriptionConfiguration.cs
│   │   │   └── NotificationHistoryConfiguration.cs
│   │   ├── Migrations/
│   │   │   └── 20261106000000_AddPushNotification.cs
│   │   └── AppDbContext.cs               # EF Core 上下文
│   └── DependencyInjection.cs            # DI 注册
│
└── IBTrade.Api/
    ├── Controllers/
    │   └── PushController.cs              # Push API 端点
    ├── HostedServices/
    │   └── ScheduledPushHostedService.cs  # 定时推送服务
    └── Program.cs                         # 应用启动配置
```

## 核心组件说明

### 1. Service Worker (sw.js)

Service Worker 是 PWA 的核心，负责：
- 缓存静态资源（离线访问）
- 拦截 fetch 请求（网络优先 + 缓存降级）
- 接收和显示推送通知
- 处理通知点击事件
- 处理订阅更新（pushsubscriptionchange）

关键代码：

```javascript
// 推送事件处理
self.addEventListener('push', (event) => {
  const data = event.data.json()
  event.waitUntil(
    self.registration.showNotification(title, options)
  )
})

// 通知点击处理
self.addEventListener('notificationclick', (event) => {
  event.notification.close()
  event.waitUntil(
    clients.openWindow(urlToOpen)
  )
})
```

### 2. 推送订阅管理 (lib/push.ts)

封装了 Web Push API 的调用：

```typescript
// 订阅推送
export async function subscribeToPush(registration: ServiceWorkerRegistration) {
  const publicKey = await getVapidPublicKey()
  const subscription = await registration.pushManager.subscribe({
    userVisibleOnly: true,
    applicationServerKey: urlBase64ToUint8Array(publicKey)
  })
  // 发送订阅到后端
  await fetch('/api/push/subscription', {
    method: 'POST',
    body: JSON.stringify(subscription.toJSON())
  })
}
```

### 3. 推送通知服务 (PushNotificationService.cs)

后端核心服务，负责：
- VAPID 密钥管理
- 订阅的 CRUD 操作
- 推送消息发送（使用 WebPush 库）
- 去重逻辑（基于内容哈希）
- 失败重试和订阅管理

关键方法：

```csharp
public async Task SendNotificationAsync(SendPushRequest request, CancellationToken ct)
{
    // 1. 获取活跃订阅
    var subscriptions = await GetActiveSubscriptionsAsync();
    
    // 2. 检查去重
    var contentHash = ComputeContentHash(request.Title, request.Body);
    if (await IsDuplicate(contentHash)) return;
    
    // 3. 发送推送
    foreach (var subscription in subscriptions) {
        await SendPushToSubscriptionAsync(subscription, payload, ct);
        UpdateSubscriptionStatus(subscription);
    }
    
    // 4. 记录历史
    await SaveNotificationHistory(...);
}
```

### 4. 定时推送服务 (ScheduledPushHostedService.cs)

后台服务，每分钟检查是否到达推送时间：

```csharp
private async void CheckAndSendPush(object? state)
{
    var shanghaiTime = TimeZoneInfo.ConvertTimeFromUtc(
        DateTime.UtcNow,
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai")
    );

    // 工作日 08:00 或 21:00
    if (IsWeekday(shanghaiTime) && IsScheduledTime(shanghaiTime))
    {
        await pushService.SendScheduledBriefAsync();
    }
}
```

### 5. 数据库设计

**push_subscriptions 表**:
```sql
CREATE TABLE push_subscriptions (
    id BIGSERIAL PRIMARY KEY,
    endpoint VARCHAR(512) NOT NULL UNIQUE,
    p256dh VARCHAR(128) NOT NULL,
    auth VARCHAR(64) NOT NULL,
    enable_scheduled_briefs BOOLEAN NOT NULL DEFAULT true,
    enable_breaking_news BOOLEAN NOT NULL DEFAULT true,
    scheduled_brief_times VARCHAR(128) NOT NULL DEFAULT '08:00,21:00',
    timezone VARCHAR(64) NOT NULL DEFAULT 'Asia/Shanghai',
    is_active BOOLEAN NOT NULL DEFAULT true,
    failure_count INTEGER NOT NULL DEFAULT 0,
    last_error_message VARCHAR(512),
    created_at_utc TIMESTAMP WITH TIME ZONE NOT NULL,
    updated_at_utc TIMESTAMP WITH TIME ZONE,
    last_notified_utc TIMESTAMP WITH TIME ZONE,
    expires_at_utc TIMESTAMP WITH TIME ZONE
);
```

**notification_histories 表**:
```sql
CREATE TABLE notification_histories (
    id BIGSERIAL PRIMARY KEY,
    type VARCHAR(64) NOT NULL,
    title VARCHAR(256) NOT NULL,
    body VARCHAR(1024) NOT NULL,
    url VARCHAR(512),
    tag VARCHAR(64),
    sent_at_utc TIMESTAMP WITH TIME ZONE NOT NULL,
    subscriptions_sent INTEGER NOT NULL,
    success_count INTEGER NOT NULL,
    failure_count INTEGER NOT NULL,
    content_hash VARCHAR(64),  -- 用于去重
    created_at_utc TIMESTAMP WITH TIME ZONE NOT NULL,
    updated_at_utc TIMESTAMP WITH TIME ZONE
);
```

索引优化：
- `endpoint` 唯一索引（快速查找订阅）
- `is_active` 索引（筛选活跃订阅）
- `is_active + enable_scheduled_briefs` 复合索引
- `is_active + enable_breaking_news` 复合索引
- `content_hash` 索引（去重查询）
- `sent_at_utc` 索引（时间范围查询）

## API 端点

### GET /api/push/vapid-public-key
返回 VAPID 公钥，用于前端订阅。

**响应**:
```json
{
  "publicKey": "BNz..."
}
```

### POST /api/push/subscription
创建或更新推送订阅。

**请求体**:
```json
{
  "endpoint": "https://fcm.googleapis.com/fcm/send/...",
  "keys": {
    "p256dh": "BPj...",
    "auth": "8Zt..."
  }
}
```

### DELETE /api/push/subscription
取消推送订阅。

**请求体**:
```json
{
  "endpoint": "https://fcm.googleapis.com/fcm/send/..."
}
```

### GET /api/push/preferences?endpoint=...
获取指定订阅的通知偏好。

**响应**:
```json
{
  "enableScheduledBriefs": true,
  "enableBreakingNews": true,
  "scheduledBriefTimes": ["08:00", "21:00"],
  "timezone": "Asia/Shanghai"
}
```

### PUT /api/push/preferences?endpoint=...
更新通知偏好。

**请求体**:
```json
{
  "enableScheduledBriefs": true,
  "enableBreakingNews": false,
  "scheduledBriefTimes": ["08:00", "21:00"],
  "timezone": "Asia/Shanghai"
}
```

### POST /api/push/send
手动发送推送（管理员）。

**请求体**:
```json
{
  "title": "重要提醒",
  "body": "这是推送内容",
  "url": "/news",
  "tag": "breaking-news",
  "important": true,
  "type": "breaking_news"
}
```

## 安全机制

### 1. VAPID 认证

使用 VAPID（Voluntary Application Server Identification）协议：
- 服务器用私钥签名推送请求
- 推送服务用公钥验证服务器身份
- 防止第三方冒充发送推送

### 2. 订阅端点保护

- 订阅端点是唯一的长随机字符串
- 只有拥有端点的服务器才能推送
- 端点泄露只影响单个订阅（不影响整体安全）

### 3. 数据去重

使用 SHA256 哈希防止重复推送：

```csharp
private static string ComputeContentHash(string title, string body)
{
    var combined = $"{title}|{body}";
    var bytes = Encoding.UTF8.GetBytes(combined);
    var hash = SHA256.HashData(bytes);
    return Convert.ToBase64String(hash);
}
```

### 4. 失败处理

- 连续失败 5 次自动禁用订阅
- HTTP 410 Gone 立即删除订阅（永久失效）
- 记录错误信息用于调试

## 性能优化

### 1. 数据库查询优化

- 使用复合索引加速条件查询
- 异步查询避免阻塞
- 批量发送推送（并发处理）

### 2. 缓存策略

Service Worker 缓存策略：
- 静态资源：Cache First（优先缓存）
- API 请求：Network First（网络优先，失败降级到缓存）
- 金融数据：Network Only（始终获取最新）

### 3. 推送负载优化

- 推送消息体积小（< 2KB）
- 深链接导航（点击直达相关页面）
- 避免频繁推送（定时 + 去重）

## iOS 特殊处理

iOS Safari 的 Web Push 有特殊限制：

1. **必须添加到主屏幕**: 浏览器模式不支持
2. **版本要求**: iOS 16.4+
3. **权限请求**: 只能由用户手势触发

前端检测和提示：

```typescript
export function isIOSPushSupported(): boolean {
  if (!isIOSDevice()) return true
  const version = getIOSVersion()
  return version !== null && version >= 16
}
```

UI 显示安装说明：

```tsx
{isIOS && (
  <div className="install-guide">
    <p>1. 点击 Safari 底部的「分享」按钮</p>
    <p>2. 选择「添加到主屏幕」</p>
    <p>3. 从主屏幕打开应用后订阅推送</p>
  </div>
)}
```

## 测试

### 单元测试

前端单元测试（Vitest）：

```bash
cd frontend
npm test
```

测试覆盖：
- Push 工具函数（设备检测、版本检测）
- VAPID 密钥转换
- 订阅状态管理

### 集成测试

手动测试流程：

1. **订阅测试**:
   ```bash
   # 打开浏览器控制台
   # 访问金融简报页面
   # 点击订阅按钮
   # 检查数据库订阅记录
   ```

2. **推送测试**:
   ```bash
   curl -X POST http://localhost:8090/api/push/send \
     -H "Content-Type: application/json" \
     -d '{"title":"测试","body":"测试推送"}'
   ```

3. **定时推送测试**:
   - 修改 `ScheduledPushHostedService.cs` 的时间条件
   - 或临时调整服务器时钟
   - 观察日志确认推送触发

### 压力测试

模拟多订阅推送：

```bash
# 创建 1000 个测试订阅
for i in {1..1000}; do
  curl -X POST http://localhost:8090/api/push/subscription \
    -H "Content-Type: application/json" \
    -d "{\"endpoint\":\"https://test.com/sub$i\",\"keys\":{\"p256dh\":\"test\",\"auth\":\"test\"}}"
done

# 发送推送，观察性能
time curl -X POST http://localhost:8090/api/push/send \
  -H "Content-Type: application/json" \
  -d '{"title":"压力测试","body":"测试"}'
```

## 监控和日志

### 推送成功率监控

查询成功率：

```sql
SELECT 
  DATE(sent_at_utc) as date,
  COUNT(*) as total_notifications,
  SUM(success_count) as total_success,
  SUM(failure_count) as total_failures,
  ROUND(100.0 * SUM(success_count) / NULLIF(SUM(subscriptions_sent), 0), 2) as success_rate
FROM notification_histories
WHERE sent_at_utc >= NOW() - INTERVAL '7 days'
GROUP BY DATE(sent_at_utc)
ORDER BY date DESC;
```

### 订阅健康度监控

查询失败订阅：

```sql
SELECT 
  failure_count,
  COUNT(*) as subscription_count,
  is_active
FROM push_subscriptions
GROUP BY failure_count, is_active
ORDER BY failure_count DESC;
```

### 应用日志

关键日志点：
- 订阅创建/删除
- 推送发送成功/失败
- VAPID 配置错误
- 定时任务触发

```csharp
_logger.LogInformation("新增推送订阅: {Endpoint}", request.Endpoint);
_logger.LogWarning("推送失败: {Endpoint}, 状态码: {StatusCode}", endpoint, statusCode);
_logger.LogError("VAPID 密钥未配置");
```

## 未来改进方向

1. **通知分组**: 支持多个简报聚合为一条通知
2. **自定义时间**: 让用户自选推送时间
3. **通知优先级**: 支持高/中/低优先级推送
4. **富文本通知**: 支持图片、动作按钮
5. **A/B 测试**: 测试不同通知文案的点击率
6. **推送分析**: 统计打开率、点击率
7. **推送模板**: 预定义常用推送格式
8. **批量操作**: 支持批量禁用/启用订阅

## 参考文档

- [Web Push Protocol - RFC 8030](https://datatracker.ietf.org/doc/html/rfc8030)
- [VAPID - RFC 8292](https://datatracker.ietf.org/doc/html/rfc8292)
- [Service Worker API - MDN](https://developer.mozilla.org/en-US/docs/Web/API/Service_Worker_API)
- [Push API - MDN](https://developer.mozilla.org/en-US/docs/Web/API/Push_API)
- [WebPush Library for .NET](https://github.com/web-push-libs/web-push-csharp)
