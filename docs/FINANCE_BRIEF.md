# Finance Brief 金融简报功能

## 概述

金融简报（Finance Brief）是一个由 Grok Bot 自动更新的财经新闻摘要功能，显示在 IBTrade 的「简报」页面中。

## 数据从哪来

条目内容来自 **X**。Grok Bot 读取 X 上的动态并整理成简报 JSON。仓库里的 `frontend/public/feeds/finance-brief.json` 只是 Bot 上次提交的快照，页面打开时用它做兜底。

点「更新」之后，**不从 GitHub 拉文件**。后端用 `Authorization: Bearer <WebhookKey>` 调用现有 Grok Bot webhook，请 Bot 按 X 上的最新内容重做简报，然后：

1. Bot 把这份 JSON 放在 webhook 响应体里，或
2. Bot 再 `POST /api/news/brief`，请求头同样是 `Authorization: Bearer <WebhookKey>`

后端只把 Bot 回传的 JSON 放在内存里给页面。GitHub 上的提交仍然可以当存档，但不是这次刷新的数据源。

## 工作流程

```
X 上的动态
    │
    ▼
Grok Bot 整理成简报 JSON
    │  webhook 响应体，或 POST /api/news/brief
    ▼
后端内存缓存  ──GET /api/news/brief──►  简报页
    ▲
    │ POST /api/news/refresh（Bearer 只在服务端）
    └──────── 用户点击「更新」
```

打开页面时：若本进程还没有 Bot 回传，就显示本地 `finance-brief.json` 快照（内容仍是之前从 X 整理的条目）。
点更新后：等 webhook 直接返回 JSON，或每 5 秒看一次缓存，直到 `updatedAt` 变化。最多等 2 分钟。

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
    "WebhookKey": "YOUR_WEBHOOK_SECRET_KEY"
  }
}
```

### Secrets 键

| 键 | 必需 | 作用 |
| --- | --- | --- |
| `GrokBot:WebhookUrl` | 是 | Grok Bot webhook 地址。只放在服务端。 |
| `GrokBot:WebhookKey` | 是 | 调用 webhook，以及校验 Bot 回传 `POST /api/news/brief`。请求头固定为 `Authorization: Bearer <WebhookKey>`。 |

环境变量（双下划线）：

```bash
export GrokBot__WebhookUrl="https://your-grok-bot-webhook.example.com/webhook"
export GrokBot__WebhookKey="YOUR_WEBHOOK_SECRET_KEY"
```

浏览器不读取这两个键。若引擎另外配置了 `ApiKey`，前端刷新仍走原来的 `X-Api-Key`；Bot 回传简报只认 `GrokBot:WebhookKey`。

## API 端点

### POST /api/news/refresh

通知 Grok Bot 按 X 上的最新内容重做简报。服务端附带 `Authorization: Bearer <WebhookKey>`。

**响应：**
- `200`：响应体就是简报 JSON（含 `updatedAt` 与 `sections`），页面直接显示
- `202`：Bot 已接单，稍后会把 JSON POST 回来；页面继续等
- `500` / `504`：webhook 未配置、调用失败或超时

### GET /api/news/brief

读取本进程里 Bot 已经回传的简报。

- `200`：简报 JSON
- `204`：还没有回传，页面使用本地 `finance-brief.json` 快照

### POST /api/news/brief

给 Grok Bot 用。请求头 `Authorization: Bearer <GrokBot:WebhookKey>`，正文为简报 JSON。

## 安全注意事项

- Webhook 密钥只在服务端，前端刷新不带这个密钥
- `appsettings.Secrets.json` 不提交
- 示例配置只有占位符

## Grok Bot 集成说明

1. 监听 webhook，校验 `Authorization: Bearer <WebhookKey>`
2. 用 **X** 上的最新内容生成简报 JSON（字段与 `finance-brief.json` 相同）
3. 把这份 JSON 作为 webhook 的 HTTP 响应体返回，或再 `POST /api/news/brief`（同一个 Bearer）
4. 收到的触发体仍是 `{ "action": "refresh_finance_brief" }`
5. 提交到 GitHub 只是存档，页面不会为了这次刷新去读 GitHub

建议在 2 分钟内回传，和页面等待时间一致。

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
# 触发 Bot（服务端会带 Bearer webhook key）
curl -X POST http://localhost:8090/api/news/refresh

# 查看 Bot 是否已经回传。没有回传时是 204
curl -i http://localhost:8090/api/news/brief

# 模拟 Bot 把 X 上整理好的 JSON 推回来（不要从浏览器带这个 key）
curl -X POST http://localhost:8090/api/news/brief \
  -H "Authorization: Bearer $GrokBot__WebhookKey" \
  -H "Content-Type: application/json" \
  --data-binary @frontend/public/feeds/finance-brief.json
```

### 本地验证

1. `appsettings.Secrets.json` 只填 `GrokBot:WebhookUrl` 和 `GrokBot:WebhookKey`
2. 启动后端和前端，打开「简报」。此时看到的是本地快照，条目链接仍是 X
3. 点「更新」。Bot 若在 webhook 响应里直接返回新 JSON，页面马上换成新条目，不需要 `git pull`，也不读 GitHub
4. Bot 若稍后才推送，页面会显示「正在等待回传」，直到 `updatedAt` 变化或 2 分钟超时

没有 Bot 时，可以用上面的 `POST /api/news/brief` 把一份改过 `updatedAt` 的 JSON 推给本机 API，确认页面会换成这份内容。

## 中文内容约定

更新请求附带 `language: "zh-CN"`。Webhook 接收端须据此使用简体中文生成栏目名称、新闻标题和摘要，保留公司名称、股票代码、数字及原文链接。前端不会将英文原文伪装成已翻译内容；此参数需要接收端支持。摘要应忠实来源，不添加未经来源支持的结论。
