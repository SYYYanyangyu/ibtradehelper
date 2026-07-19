# IBTrade

Interactive Brokers 持仓 / 交易追踪服务，基于 **Clean Architecture（DDD 四层）** 构建。

## 项目结构（标准 .NET 布局）

```
trade/
├── IBTrade.sln                # 解决方案文件
├── .gitignore
├── README.md
└── src/                       # 所有源码项目
    ├── IBTrade.Domain/          # 领域层（实体 / 枚举 / 仓储接口，零外部依赖）
    ├── IBTrade.Application/     # 应用层（用例服务 / DTO / 接口）
    ├── IBTrade.Infrastructure/  # 基础设施层（EF Core / PostgreSQL / 外部客户端）
    └── IBTrade.Api/            # 表现层（Controllers / Composition Root）
```

## 依赖方向（严格单向朝内）

```
Api ──▶ Infrastructure ──▶ Application ──▶ Domain
```

- 领域层 `Domain` 不依赖任何外层，是整个架构的内核。
- 接口定义在内侧（如 `IPositionRepository` 在 Domain），实现在外侧（Infrastructure），即依赖倒置。
- 每一层暴露 `AddXxx()` 扩展方法，`Program.cs` 只做一行调用，保持清爽。

## 本地运行

```bash
# 1) 配置密钥（勿提交）：复制示例并填入 Flex Token / LLM Key / 数据库连接
cp src/IBTrade.Api/appsettings.Example.json src/IBTrade.Api/appsettings.Secrets.json

# 2) 后端
dotnet build
dotnet run --project src/IBTrade.Api   # 默认 http://localhost:8090

# 3) 前端
cd frontend && npm install && npm run dev
```

也可不用 Secrets 文件，改用环境变量：`IBKR_FLEX_TOKEN_LIVE` / `IBKR_FLEX_QUERYID_LIVE`、`IBTRADE_DB_PASSWORD`、`DASHSCOPE_API_KEY` 等。

- Swagger：`http://localhost:8090/swagger`
- 健康检查：`http://localhost:8090/health`

**请勿提交**：`appsettings.Secrets.json`、Flex/LLM 密钥、以及 `frontend/src/mock/**/*.json`（本地演示持仓）。

## 扩展约定（以新增一个实体为例）

1. `Domain/Entities/` 加实体（继承 `Entity` 基类）。
2. `Domain/Repositories/` 加仓储接口。
3. `Application/<Entity>/` 加 DTO + `<Entity>Service`（实现用例）。
4. `Infrastructure/Persistence/Configurations/` 加 EF 映射；`Repositories/` 加实现。
5. `Api/Controllers/` 加 Controller，委托 Application 服务。
