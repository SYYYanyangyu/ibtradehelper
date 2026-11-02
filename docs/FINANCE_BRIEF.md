# Finance Brief 金融简报功能

## 概述

金融简报（Finance Brief）是一个由 Grok Bot 自动更新的财经新闻摘要功能，显示在 IBTrade 的「简报」页面中。

## ⭐ 实时更新特性（无需 git pull）

点击「更新」按钮后，前端会：
1. 触发 Grok Bot webhook 生成最新简报
2. **自动从 GitHub raw API 轮询获取最新内容**（无需本地 git pull）
3. 智能等待最多 2 分钟，每 5 秒检查一次更新
4. 检测到 `updatedAt` 变化后立即显示最新内容

## 工作流程

```
┌─────────────┐         ┌──────────────┐         ┌─────────────┐
│   Grok Bot  │ ───────>│ GitHub main  │<─────── │ 后端 API    │
│   定时/手动  │  提交    │ (JSON 文件)   │  拉取   │ (实时获取)  │
└─────────────┘         └──────────────┘         └─────────────┘
      ▲                                                  │
      │                                                  │
      │  POST /api/news/refresh                         │ GET /api/news/brief
      └──────────────────────────────────────────────────┘
                     用户点击「更新」按钮
                            ↓
                   前端智能轮询等待更新
```

### 流程说明

1. **Grok Bot 写入**：Grok Bot 定时或手动将最新的金融简报内容提交到 GitHub `frontend/public/feeds/finance-brief.json`
2. **前端展示**：用户访问「简报」页面时，前端调用后端 API 获取最新内容
3. **手动刷新（智能轮询）**：用户点击「更新」按钮时：
   - 前端调用后端 API `/api/news/refresh`（POST）触发 webhook
   - 后端通知 Grok Bot 请求更新
   - 前端开始智能轮询（每 5 秒调用 `/api/news/brief`）
   - 后端从 GitHub raw API 获取最新 JSON（带 `no-cache` 头）
   - 检测到 `updatedAt` 字段变化时停止轮询，显示最新内容
   - 最多轮询 24 次（2 分钟），超时则提示用户

## 文件结构

```
frontend/
├── public/
│   └── feeds/
│       └── finance-brief.json        # 简报数据文件（Grok Bot 写入，前端读取）
├── src/
│   ├── pages/
│   │   └── NewsBriefPage.tsx         # 简报页面组件
│   ├── components/
│   │   └── AppHeader.tsx             # 导航栏（包含「简报」按钮）
│   └── App.tsx                        # 主应用（添加了 brief 视图）

src/IBTrade.Api/
├── Controllers/
│   └── MarketNewsController.cs       # 包含 /api/news/refresh 端点
├── appsettings.Example.json          # 配置示例（包含 GrokBot webhook）
└── Program.cs                         # 注册 GrokBot 配置和 HttpClient
```

## JSON 数据格式

```json
{
  "updatedAt": "2026-11-02T10:00:00+08:00",
  "source": "scheduled",
  "sections": [
    {
      "id": "trump",
      "title": "特朗普政策",
      "items": [
        {
          "headline": "特朗普关税政策升级",
          "summary": "新一轮对华科技产品关税提案引发市场波动，半导体板块承压。",
          "url": "https://www.bloomberg.com/news/politics",
          "tickers": ["NVDA", "AMD", "INTC"]
        }
      ]
    }
  ]
}
```

### 字段说明

- `updatedAt`: 更新时间（ISO-8601 格式，建议使用 Asia/Shanghai 或 UTC）
- `source`: 更新来源（`scheduled` = 定时自动，`manual` = 手动触发）
- `sections`: 简报分类列表
  - `id`: 分类标识（如 `trump`, `fed`, `mega_cap`, `semiconductor` 等）
  - `title`: 分类标题（中文）
  - `items`: 该分类下的新闻条目
    - `headline`: 新闻标题
    - `summary`: 新闻摘要
    - `url`: 新闻详情链接
    - `tickers`: 相关股票代码列表

## 后端配置

在 `appsettings.Secrets.json` 或环境变量中配置：

```json
{
  "GrokBot": {
    "WebhookUrl": "https://your-grok-bot-webhook.example.com/webhook",
    "WebhookKey": "YOUR_WEBHOOK_SECRET_KEY",
    "GitHubOwner": "SYYYanyangyu",
    "GitHubRepo": "ibtradehelper",
    "GitHubBranch": "main"
  }
}
```

### 配置说明

- **WebhookUrl**: Grok Bot webhook 地址（必需）
- **WebhookKey**: Webhook 授权密钥（必需）- 以 `Authorization: Bearer {key}` 头部发送
- **GitHubOwner**: GitHub 用户名（默认：`SYYYanyangyu`）
- **GitHubRepo**: 仓库名（默认：`ibtradehelper`）
- **GitHubBranch**: 分支名（默认：`main`）

或使用环境变量：

```bash
export GrokBot__WebhookUrl="https://your-grok-bot-webhook.example.com/webhook"
export GrokBot__WebhookKey="YOUR_WEBHOOK_SECRET_KEY"
export GrokBot__GitHubOwner="SYYYanyangyu"  # 可选，默认值已设置
export GrokBot__GitHubRepo="ibtradehelper"  # 可选，默认值已设置
export GrokBot__GitHubBranch="main"         # 可选，默认值已设置
```

## API 端点

### GET /api/news/brief

从 GitHub raw API 获取最新简报（绕过本地文件缓存）。

**请求：**
```
GET /api/news/brief
Cache-Control: no-cache
```

**响应：**
- `200 OK`: 返回最新的 JSON 内容
- `404 Not Found`: GitHub 上文件不存在
- `500 Internal Server Error`: 获取失败

### POST /api/news/refresh

触发 Grok Bot 更新简报。

**请求：**
```
POST /api/news/refresh
Content-Type: application/json
```

**响应：**
- `202 Accepted`: 成功触发更新请求
- `500 Internal Server Error`: webhook 未配置或调用失败

## 安全注意事项

- ✅ **Webhook 密钥不暴露给前端**：前端只调用后端 API，后端负责添加 `Authorization: Bearer <key>` 头
- ✅ **配置文件不提交**：`appsettings.Secrets.json` 已在 `.gitignore` 中，真实密钥不会进入版本控制
- ✅ **示例配置安全**：`appsettings.Example.json` 中只包含占位符，没有真实密钥
- ✅ **GitHub 只读访问**：后端从 GitHub raw API 读取，无需 token（公开仓库）
- ✅ **默认值安全**：GitHubOwner/Repo/Branch 有合理默认值，无需暴露在环境变量中

## Grok Bot 集成说明

Grok Bot 需要：

1. **监听 webhook**：接收来自 `/api/news/refresh` 的 POST 请求
2. **验证 Authorization 头**：检查 `Bearer <WebhookKey>` 是否匹配
3. **更新并提交 JSON 文件**：将最新简报内容提交到 GitHub `frontend/public/feeds/finance-brief.json`
4. **请求格式**：
   ```json
   {
     "action": "refresh_finance_brief"
   }
   ```
5. **响应要求**：建议在 2 分钟内完成更新（匹配前端轮询超时）

## 技术亮点

### 1. 无需 git pull
- 前端从 `/api/news/brief` 获取最新内容
- 后端从 GitHub raw API 实时拉取
- 开发者无需手动 `git pull` 即可看到更新

### 2. 智能轮询
- 点击「更新」后自动等待 Grok Bot 完成
- 每 5 秒检查一次 `updatedAt` 字段变化
- 最多等待 2 分钟，超时友好提示

### 3. 降级处理
- `/api/news/brief` 失败时回退到本地静态文件
- 确保页面始终可访问

### 4. 缓存绕过
- GitHub raw API 请求带 `Cache-Control: no-cache` 头
- 避免 CDN 缓存导致的延迟

## 开发与测试

### 前端开发

```bash
cd frontend
npm run dev
```

访问 http://localhost:5173 点击「简报」查看页面。

### 后端开发

```bash
cd src/IBTrade.Api
dotnet run
```

API 运行在 http://localhost:8090

### 测试刷新功能

```bash
# 需要先配置 webhook URL 和 key
curl -X POST http://localhost:8090/api/news/refresh

# 测试获取最新简报（从 GitHub）
curl http://localhost:8090/api/news/brief
```

### 完整测试流程

1. 配置 `appsettings.Secrets.json` 中的 GrokBot 配置
2. 启动后端和前端
3. 访问「简报」页面
4. 点击「更新」按钮
5. 观察轮询状态提示（蓝色框）
6. Grok Bot 完成后，页面自动显示最新内容
7. 或 2 分钟后超时提示

### 手动更新 JSON（无 Grok Bot 时）

可以手动修改 GitHub 上的 `frontend/public/feeds/finance-brief.json`：
1. 更新内容
2. 修改 `updatedAt` 字段为当前时间
3. 提交到 main 分支
4. 点击前端「更新」按钮
5. 等待轮询检测到变化（约 5-10 秒）
