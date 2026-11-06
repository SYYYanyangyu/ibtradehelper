# PWA 和 Web Push 推送通知设置指南

本指南说明如何配置和使用 IBTrade 的 PWA（渐进式 Web 应用）和 Web Push 推送通知功能。

## 功能特性

✅ **PWA 特性**
- 可安装到主屏幕，像原生应用一样使用
- 离线访问能力（缓存关键资源）
- 响应式移动端界面优化

✅ **Web Push 推送通知**
- 定时金融简报（工作日 08:00 和 21:00 北京时间）
- 重要突发新闻推送
- 自动去重，避免重复通知
- 安全的订阅管理和持久化存储
- 支持订阅过期和失败重试处理

✅ **平台支持**
- Android: Chrome、Edge、Firefox 全面支持
- iOS: Safari 16.4+ 支持（需添加到主屏幕后使用）
- Desktop: Chrome、Edge、Firefox 支持

## 快速开始

### 1. 生成 VAPID 密钥对

Web Push 需要 VAPID 密钥对进行身份验证。运行以下命令生成：

```bash
cd /path/to/ibtradehelper
node scripts/generate-vapid-keys.js
```

该命令会输出公钥、私钥和配置示例。

### 2. 配置密钥

**方式 A: 使用 appsettings.Secrets.json（开发环境推荐）**

在 `src/IBTrade.Api/` 目录下创建或编辑 `appsettings.Secrets.json`：

```json
{
  "Vapid": {
    "PublicKey": "YOUR_PUBLIC_KEY_HERE",
    "PrivateKey": "YOUR_PRIVATE_KEY_HERE",
    "Subject": "mailto:your-email@example.com"
  }
}
```

**方式 B: 使用环境变量（生产环境推荐）**

```bash
export Vapid__PublicKey="YOUR_PUBLIC_KEY_HERE"
export Vapid__PrivateKey="YOUR_PRIVATE_KEY_HERE"
export Vapid__Subject="mailto:your-email@example.com"
```

或在 Docker Compose 的 `.env` 文件中：

```env
Vapid__PublicKey=YOUR_PUBLIC_KEY_HERE
Vapid__PrivateKey=YOUR_PRIVATE_KEY_HERE
Vapid__Subject=mailto:your-email@example.com
```

**重要提示：**
- ⚠️ 私钥必须保密，不要提交到代码仓库
- ⚠️ `Subject` 必须是有效的 `mailto:` 或 `https://` URL
- ⚠️ 生产环境必须使用环境变量，不要在配置文件中硬编码密钥

### 3. 数据库迁移

启动应用时会自动运行数据库迁移，创建以下表：
- `push_subscriptions` - 推送订阅记录
- `notification_histories` - 通知历史（用于去重）

如需手动运行迁移：

```bash
cd src/IBTrade.Api
dotnet ef database update --project ../IBTrade.Infrastructure
```

### 4. 启动服务

**开发环境：**

```bash
# 后端
cd src/IBTrade.Api
dotnet run

# 前端（新终端）
cd frontend
npm install
npm run dev
```

**生产环境（Docker Compose）：**

```bash
cd deploy
# 确保 .env 文件包含 VAPID 配置
docker-compose up -d
```

## iOS 安装说明

iOS Safari 的 PWA 和推送通知需要特殊设置：

1. **系统要求**: iOS 16.4 或更高版本
2. **添加到主屏幕**:
   - 在 Safari 中打开应用
   - 点击底部的「分享」按钮
   - 向下滚动，选择「添加到主屏幕」
   - 点击右上角「添加」完成安装
3. **订阅推送**:
   - 必须从主屏幕打开应用（不能在 Safari 浏览器中）
   - 进入「Finance Brief · 金融简报」页面
   - 点击「订阅推送通知」按钮
   - 在弹出的权限对话框中选择「允许」

**注意**: iOS 不支持在 Safari 浏览器中直接订阅推送，必须作为独立应用运行。

## Android 安装说明

Android 的 PWA 支持更完善：

1. **添加到主屏幕**:
   - 在 Chrome/Edge 中打开应用
   - 浏览器会自动提示「添加到主屏幕」
   - 或点击菜单 → 「添加到主屏幕」/「安装应用」
2. **订阅推送**:
   - 可以在浏览器或独立应用中订阅
   - 进入金融简报页面
   - 点击「订阅推送通知」
   - 允许通知权限

## 推送通知配置

### 通知偏好设置

用户可以在「Finance Brief · 金融简报」页面配置：

1. **定时金融简报**
   - 开启/关闭定时推送
   - 默认时间: 工作日 08:00 和 21:00（北京时间）
   
2. **重要突发新闻**
   - 开启/关闭突发新闻推送
   - 仅推送高优先级新闻，自动去重

### 推送时间说明

- 定时推送在北京时间（Asia/Shanghai）工作日执行
- 周末和节假日不推送定时简报
- 突发新闻不受时间限制，实时推送

## API 端点

### 推送管理 API

```
GET  /api/push/vapid-public-key        获取 VAPID 公钥
POST /api/push/subscription             订阅推送
DELETE /api/push/subscription           取消订阅
GET  /api/push/preferences?endpoint=... 获取通知偏好
PUT  /api/push/preferences?endpoint=... 更新通知偏好
POST /api/push/send                     发送推送（管理员）
GET  /api/push/stats                    获取订阅统计
```

### 手动发送推送示例

```bash
curl -X POST http://localhost:8090/api/push/send \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: YOUR_API_KEY" \
  -d '{
    "title": "测试通知",
    "body": "这是一条测试推送",
    "url": "/",
    "important": false
  }'
```

## 安全注意事项

### VAPID 密钥管理

- ✅ 使用环境变量注入生产密钥
- ✅ 将 `appsettings.Secrets.json` 添加到 `.gitignore`
- ✅ 定期轮换密钥（需重新订阅）
- ❌ 不要在公开仓库中提交私钥
- ❌ 不要在客户端代码中暴露私钥

### HTTPS 要求

- Web Push 必须在 HTTPS 环境下工作
- 开发环境 localhost 例外（HTTP 也可用于测试）
- 生产部署必须配置 SSL 证书

### 订阅管理

- 订阅端点自动去重（同一端点只保留一个订阅）
- 失败 5 次后自动禁用订阅
- 收到 HTTP 410 Gone 响应立即删除订阅
- 支持订阅过期自动更新（pushsubscriptionchange 事件）

## 故障排查

### iOS 推送不工作

1. 确认 iOS 版本 >= 16.4
2. 确认应用已添加到主屏幕
3. 确认从主屏幕（不是 Safari）打开应用
4. 检查「设置」→「通知」中是否允许通知

### Android 推送不工作

1. 确认浏览器版本是最新的
2. 检查系统通知权限
3. 检查浏览器通知权限
4. 确认网络连接正常

### VAPID 配置错误

如果看到 "VAPID 公钥未配置" 错误：

1. 检查配置文件或环境变量是否正确
2. 重启后端服务
3. 运行 `node scripts/generate-vapid-keys.js` 重新生成密钥
4. 确认 `appsettings.Secrets.json` 在正确的目录

### 推送发送失败

检查后端日志：

```bash
# Docker 环境
docker-compose logs -f api

# 开发环境
# 查看控制台输出
```

常见错误：
- `401 Unauthorized`: VAPID 密钥配置错误
- `410 Gone`: 订阅已过期，需要重新订阅
- `404 Not Found`: 推送端点无效

## 测试

### 手动测试清单

- [ ] PWA 安装（iOS 和 Android）
- [ ] 推送订阅和取消订阅
- [ ] 偏好设置保存
- [ ] 定时推送（修改服务器时间测试）
- [ ] 突发新闻推送
- [ ] 推送去重
- [ ] 订阅失败重试
- [ ] 离线访问

### 自动化测试

运行单元测试（需要 .NET SDK）：

```bash
cd src/IBTrade.Api
dotnet test
```

## 图标自定义

默认图标是简单的占位符。要替换为自定义图标：

1. 准备不同尺寸的 PNG 图标（72、96、128、144、152、192、384、512px）
2. 将图标放在 `frontend/public/icons/` 目录
3. 确保文件名格式为 `icon-{size}x{size}.png`
4. 重新构建前端

```bash
cd frontend
npm run build
```

## 生产部署注意事项

1. **HTTPS**: 必须使用 HTTPS（Let's Encrypt 证书推荐）
2. **VAPID 密钥**: 使用环境变量，不要提交到代码仓库
3. **数据库备份**: 定期备份订阅数据
4. **监控**: 监控推送成功率和失败日志
5. **速率限制**: 考虑添加推送频率限制防止滥用

## 相关资源

- [Web Push API - MDN](https://developer.mozilla.org/en-US/docs/Web/API/Push_API)
- [PWA - web.dev](https://web.dev/progressive-web-apps/)
- [iOS Web Push - Apple Developer](https://developer.apple.com/documentation/usernotifications/sending_web_push_notifications_in_safari_and_other_browsers)
- [VAPID - RFC 8292](https://datatracker.ietf.org/doc/html/rfc8292)

## 常见问题

**Q: 为什么 iOS 必须添加到主屏幕才能接收推送？**

A: 这是 Apple 的设计决策，Web Push 只在独立应用模式下工作，不支持在 Safari 浏览器中。

**Q: 推送消息会占用多少数据？**

A: 每条推送约 1-2KB，定时简报每天 2 次，突发新闻视情况而定。流量消耗很小。

**Q: 可以在多个设备上订阅吗？**

A: 可以。每个设备会有独立的订阅端点，可以各自配置推送偏好。

**Q: 密钥轮换后已有订阅怎么办？**

A: 更换密钥后，旧订阅会失效。用户需要重新订阅（应用会自动提示）。

**Q: 可以自定义推送时间吗？**

A: 当前版本固定为 08:00 和 21:00 北京时间。如需自定义，可修改 `ScheduledPushHostedService.cs`。
