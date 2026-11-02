# Finance Brief 金融简报功能

## 概述

金融简报（Finance Brief）是一个由 Grok Bot 自动更新的财经新闻摘要功能，显示在 IBTrade 的「简报」页面中。

## 工作流程

```
┌─────────────┐         ┌──────────────┐         ┌─────────────┐
│   Grok Bot  │ ───────>│ JSON 文件     │<─────── │ 前端页面     │
│   定时/手动  │  写入    │ (public/feeds)│  读取   │ (简报视图)   │
└─────────────┘         └──────────────┘         └─────────────┘
      ▲                                                  │
      │                                                  │
      │  POST /api/news/refresh                         │
      └──────────────────────────────────────────────────┘
                     用户点击「更新」按钮
```

### 流程说明

1. **Grok Bot 写入**：Grok Bot 定时或手动将最新的金融简报内容写入 `frontend/public/feeds/finance-brief.json`
2. **前端展示**：用户访问「简报」页面时，前端加载并展示 JSON 文件内容
3. **手动刷新**：用户点击「更新」按钮时：
   - 前端调用后端 API `/api/news/refresh`（POST）
   - 后端通过 webhook 通知 Grok Bot 请求更新
   - Grok Bot 接收到请求后更新 JSON 文件
   - 前端等待数秒后自动重新加载 JSON

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

或使用环境变量：

```bash
export GrokBot__WebhookUrl="https://your-grok-bot-webhook.example.com/webhook"
export GrokBot__WebhookKey="YOUR_WEBHOOK_SECRET_KEY"
```

## API 端点

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

## Grok Bot 集成说明

Grok Bot 需要：

1. **监听 webhook**：接收来自 `/api/news/refresh` 的 POST 请求
2. **验证 Authorization 头**：检查 `Bearer <WebhookKey>` 是否匹配
3. **更新 JSON 文件**：将最新简报内容写入 `frontend/public/feeds/finance-brief.json`
4. **请求格式**：
   ```json
   {
     "action": "refresh_finance_brief"
   }
   ```

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
```

### 手动更新 JSON（无 Grok Bot 时）

直接编辑 `frontend/public/feeds/finance-brief.json`，前端会在下次刷新时加载新内容。


## 中文内容约定

更新请求附带 `language: "zh-CN"`。Webhook 接收端须据此使用简体中文生成栏目名称、新闻标题和摘要，保留公司名称、股票代码、数字及原文链接。前端不会将英文原文伪装成已翻译内容；此参数需要接收端支持。摘要应忠实来源，不添加未经来源支持的结论。
