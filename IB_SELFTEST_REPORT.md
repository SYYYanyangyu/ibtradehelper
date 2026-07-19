# IBTrade 项目逐功能自测与安全 / 稳定性 / 代码质量评估报告

> 自测方式：**逐文件人工走读 + 逻辑推演**（非单元测试）。覆盖后端 125 个 `.cs`（Domain / Application / Infrastructure / Api）与前端的页面、类型、API client。
> 严重度：🔴 阻断 / 真实资金风险 · 🟠 中（数据正确性或稳定性隐患） · 🟡 低 / 改进建议 · 👍 亮点
> 说明：上一轮已修复的 4 个 P0/P1（自动交易重启重复建仓、Flex 重复追加、实盘死配置、撤单掩盖）**不在本表重复计分**，但其中“自动交易重启修复”引入了一个**新的双计回归**，见下方 C-1。

---

## 一、功能逐项自测结果

| 功能 | 自测结论 | 关键风险 |
|---|---|---|
| 持仓看板 / 盈亏汇总（`PortfolioService`） | ⚠️ 基本正确 | C-2 FIFO 不处理做空；A-4 账户环境误标 |
| 每日盈亏曲线（`DailyPnlService`） | ⚠️ 有保留 | B-3 时区错位；D-7 缺口日归因 |
| 个股每日盈亏归因（`PositionDailyPnlService`） | ✅ 正确 | — |
| 现金流录入 / 查询 | ⚠️ 有保留 | C-5 整批替换清空窗口外真实现金流 |
| TWS 实时同步 / 推送（`TwsSyncService` / `TwsLiveFeed`） | ⚠️ 有保留 | S-1 启用 TWS 启动崩溃；S-2 clientId 并发 |
| Flex 报表摄入（`IngestionService`） | ⚠️ 有保留 | C-5 现金流替换清空；A-3 accountId 缺失回退空串 |
| 下单 / 撤单（`OrderService` / `TwsOrderClient`） | ⚠️ 有保留 | S-3 手动市价单绕过金额风控 |
| 风控（`RiskGuard`） | ✅ 逻辑正确 | S-3 市价单空档；C-9 DailyLossLimit 死配置 |
| 自动交易引擎（`AutoTrader`） | ⚠️ 有保留 | C-1 持仓双计超卖；C-9 配置重启丢失 |
| 回测（`BacktestService` + 策略） | ✅ 合理 | 简化 long-only 模型，与实盘引擎口径不完全一致（预期内） |
| 快照重建 / 种子（`SnapshotService`） | ⚠️ 有保留 | C-6 Seed 清空现金流；B-3 时区 |

---

## 二、安全问题（Security）

### 🔴 S-3 手动**市价单**绕过“单笔金额上限”风控 —— 真实资金风险
- 位置：`OrdersController.Place`（`src/IBTrade.Api/Controllers/OrdersController.cs:37`）与 `RiskGuard.Check`（`src/IBTrade.Application/Orders/RiskGuard.cs:74`）。
- 现象：控制器对手动下单传 `estimatedPrice = req.Type == Limit ? req.LimitPrice : null`。**市价单传 `null`** → `RiskGuard.Check` 中 `if (s.MaxOrderAmount > 0 && estimatedPrice.HasValue)` 因 `HasValue=false` 直接跳过金额校验。
- 后果：手动下一个 10,000,000 股、单价 $500 的市价 BUY（`= $50 亿`）只会被 `MaxSafeQuantity` 拦（上限 1e7 股），但**完全绕过 `MaxOrderAmount`**，可触发远超风险限额的真实下单（实盘且 `LiveAcknowledged` 时即为真实资金损失）。
- 对比：自动交易引擎对市价单传 `estPrice = candles[^1].Close`，**是有金额校验的**——两处口径不一致，说明这是遗漏而非设计。
- 建议：下单服务应始终用可获得的估算价（限价用限价、市价用最近行情/上根收盘）参与风控；或在 `Check` 中对市价单显式拒绝“无法估算金额”。

### 🔴 S-1（安全/可用）IBKR Flex Token 以明文出现在 URL 查询串
- 位置：`FlexQueryClient.DownloadXmlAsync`（`src/IBTrade.Infrastructure/IBKR/FlexQueryClient.cs:31,39`）。`token`、`queryId` 拼接在 `?t=...&q=...` 中。
- 现象：密钥进入 HTTP URL → 出现在出站日志、代理日志、以及 `HttpClient` 可能的诊断日志中。这是 IBKR Flex Web Service 的 API 设计限制（只支持 query 参数），但仍属应披露的高敏感信息暴露。
- 建议：明确在文档/README 标记“Flex token 等同账户只读凭据”；避免在日志中打印完整 URL；如可行优先用环境变量且不在任何日志回显。

### 🔴 S-2（安全）数据库默认弱密码硬编码进源码
- 位置：`Infrastructure/DependencyInjection.cs:25` 回退连接串 `Password=123456`，用户名 `aiuser`。
- 虽然支持 `IBTRADE_DB_PASSWORD` 环境变量覆盖，但**源码里躺着一个可连的弱口令**，且任何未配置环境变量的部署都会用弱口令起库。
- 建议：移除源码默认密码，强制要求通过环境变量/密钥注入；或至少在缺少环境变量时启动失败而非回退弱口令。

### 🟠 S-4 API Key 比较非恒定时间 + 生产只读接口匿名可读
- `ApiKeyAuthMiddleware`（`src/IBTrade.Api/Auth/ApiKeyAuthMiddleware.cs:76`）用 `string.Equals(ordinal)` 比较密钥，存在理论计时侧信道；生产环境所有 `GET` 看板接口**无需密钥**即可读（含 `/api/autotrade/status`、`/api/orders/risk-settings`）。
- 影响：本地自托管可接受；若暴露到公网，看板数据（账户、持仓、成交）完全公开。建议只读接口也建议鉴权或至少绑定内网。

### 🟡 S-5 审计操作人（`X-Operator`）由客户端头随意伪造
- `AutoTradeController` / `OrdersController` 直接信任请求头 `X-Operator` 写入审计。前端 `lib/api.ts` 从 `localStorage` 读取，可任意伪造。
- 建议：审计的“操作人”若为安全凭证，应来自服务端认证身份，而非客户端头。

### 🟡 S-6 无速率限制 / 防暴力
- 下单、启停、改风控均无节流。本地单用户可接受，公网部署需补充。

### 🟡 S-7 CORS 开发环境 `AllowAnyOrigin`
- `Program.cs:55` 开发环境放开全部跨域。生产需显式 `AllowedOrigins`，默认拒绝（已处理），属合理。

---

## 三、稳定性 / 启动问题（Stability）

### 🔴 ST-1 启用 TWS 实时同步会导致**应用启动即崩溃**
- 位置：`TwsSyncHostedService`（`src/IBTrade.Api/HostedServices/TwsSyncHostedService.cs:11`）构造函数直接注入 `ITwsSyncService`，而 `ITwsSyncService` 在 Application DI 中注册为 **Scoped**（`Application/DependencyInjection.cs:27`），托管服务是 **Singleton**。
- 后果：当 `TWS__ENABLED=true` 时，ASP.NET Core 在启动解析该单例时会抛 `InvalidOperationException: Cannot consume scoped service 'ITwsSyncService' from singleton 'TwsSyncHostedService'`，**进程起不来**。
- 为何之前没暴露：默认 `TWS__ENABLED=false`，该托管服务不被注册，所以常规运行（含之前的“自测”）都没触发。一旦用户按文档打开 TWS 同步，启动必崩。
- 修复：参照 `DailySnapshotHostedService`，把 `ITwsSyncService` 改为通过 `IServiceScopeFactory` 在每个同步周期建作用域解析（不要直接注入 Scoped 服务到 Singleton）。

### 🔴 ST-2 `HistoricalDataService` 固定 `clientId = 3`，并发历史请求撞 clientId
- 位置：`HistoricalDataService.cs:18`（`options.ClientId + 2` 固定为 3）。
- 现象：自动交易引擎评估、回测、前端 `GET /api/marketdata/history`、`rebuild-history` 全部走同一个 `clientId=3` 的 `ITwsClient`。这些路径**可并发**（例如后台自动交易 + 用户在前端点回测 / 看板触发历史），IB Gateway 会对“同一 clientId 重复连接”返回 `clientId already in use` 或握手失败。
- 后果：自动交易评估或回测随机失败，表现为“偶尔拉不到历史数据”。
- 修复：用 `Interlocked` 自增的每请求唯一 clientId（如 `3 + n`），或在应用内对历史数据路径做串行化队列。

### 🟠 ST-3 无 DB 唯一约束，Upsert 存在 TOCTOU 竞态
- `DailySnapshot`（账户+日期）、`PositionDailySnapshot`（账户+日期+标的）仅靠 `FirstOrDefault` 再决定 insert/update，**没有数据库唯一索引**。
- 并发场景下（如 `TwsSyncHostedService` 周期同步 与 手动 `POST /api/portfolio/snapshot` 同时发生）两路都读到“不存在”→ 各插一条 → 重复行，进而每日盈亏被翻倍。
- 建议：在迁移中为这些表加复合唯一索引，并把 Upsert 改为数据库级 `ON CONFLICT`（或先查后写放在事务内）。`Position` 同理（按账户整批替换与 TWS 同步/Flex 摄入并发时也可能重复）。

### 🟡 ST-4 `TwsClient` 读线程在 `Disconnect()` 置 `_client=null` 后仍可能 NRE
- `TwsClient.cs:263` 读线程循环 `while (_client.IsConnected())`，`Disconnect()` 把 `_client` 置 null 后线程下次判空抛 `NullReferenceException`（被外层 catch 吞掉，功能不受影响，但属于脏实现，且异常会被某些日志框架当作错误上报）。

### 🟡 ST-5 `TwsOrderClient._states` 字典无上限增长
- 每笔订单的 `BrokerOrderState` 永久留在内存字典；撤单超时（已修为返回 false）或终态后从不清理 → 长时间运行内存单调增长。建议终态订单定时归档/移除。

### 🟡 ST-6 `TwsLiveFeed` 自动重连用相同 `clientId`
- 重连创建新 `ITwsClient(clientId=options.ClientId)`，若旧连接未完全释放，Gateway 可能报 `clientId already in use`。建议重连前确保旧 client `eDisconnect` 完成并短暂退避。

---

## 四、业务逻辑 / 正确性（Correctness）

### 🔴 C-1 自动交易持仓“双计”→ SELL 信号时按 `netQty` 超卖（上一轮修复引入的回归）
- 位置：`AutoTrader.ComputePositionAsync`（`src/IBTrade.Application/AutoTrade/AutoTrader.cs:313-353`）。
- 现象：去抖用的净持仓 = **持久化 DB 基线 + 本会话内存已成交流水**（两者相加）。
  - 引擎 BUY 成交 → 内存 `sessionNet` 记 +qty；
  - 之后某次 Flex/TWS 同步把该持仓写入 DB → `baseline` 也记 +qty；
  - 下一轮评估 `netQty = baseline + sessionNet = 2×qty`。
- 后果：出现 SELL 信号时，`wantSide = netQty > 0 && openSell==0 → Sell qty = netQty`，**按双倍持仓卖出（超卖）**。原“重启重复建仓”问题被修掉，但换成了“同步后超卖”——仍是真实资金风险。
- 根因：DB 基线与会话内存流水**不是互斥来源**，同一笔成交被两边各算一次（代码注释自己也承认“轻微高估”，但对卖侧是严重高估）。
- 修复方向（任选）：
  1. **以 DB 基线为权威**：`netQty = baseline`，本会话仅用**未完结挂单**做去抖（不把已成交的再加回）；或
  2. 维护“本会话已发且尚未被同步覆盖”的增量集合，避免与 DB 重复累加；或
  3. 用 `brokerOrderId` 把内存订单与持久化持仓对账后再合并。
  无论哪种，都需保证“同一笔成交只计一次”。

### 🟠 C-2 `PortfolioService` FIFO 已实现盈亏**不处理做空**
- 位置：`PortfolioService.ComputeRealizedPnl`（`src/IBTrade.Application/Portfolio/PortfolioService.cs:80-126`）。
- 现象：算法假设“先 BUY 建批次，SELL 匹配”。若先出现 SELL（开空或平空），`lots` 为空，`realized += proceeds`（卖出收入，未减成本）→ **空头平仓/开空算出错误的大额“已实现盈亏”**。
- 影响：`Position.Quantity` 支持负值（做空），但 FIFO 未维护空头库存。当前默认 long-only 回测/自动交易不触发，但一旦有空头成交（Flex 报表含卖空），看板“已实现盈亏”失真。
- 建议：扩展到支持负库存（空头批次）配对，或至少在文档/UI 明确“不支持做空盈亏计算”。

### 🟠 C-3 时区约定不一致（本地交易日 vs UTC），非 UTC 时区快照错位
- 位置：`SnapshotService.CaptureTodayAsync`（`src/IBTrade.Application/Portfolio/SnapshotService.cs:32-35`）用 `TradingCalendar.LatestTradingDay(DateTime.Now)`（**本地日期**）后 `SpecifyKind(Utc)`；而 `SeedHistoryAsync:176` 用 `DateTime.UtcNow.Date`（**UTC 日期**），`DailySnapshotRepository.GetRecentAsync:12` 与 `PortfolioController.GetTrades` 用 `DateTime.UtcNow.Date` 过滤。
- 后果：服务器若不在 UTC 时区，“今日”快照按本地日期存、却被按 UTC 日期查询/过滤，会出现**快照落在错误日期、每日盈亏曲线缺一天或重复一天**。UTC 时区部署无此问题（潜藏 bug）。
- 建议：全系统统一一个“交易日”口径（推荐全部用 UTC 日期，或统一用服务器本地日期并贯穿所有读写），并在 `DateUtc` 上一致地 `SpecifyKind`。

### 🟠 C-4 `TwsSyncService` 建账户不设置 `Environment` → Paper 账户被误标 Live
- 位置：`TwsSyncService.PersistAsync`（`src/IBTrade.Application/IBKR/TwsSyncService.cs:54-60`）新建 `Account` 时未赋值 `Environment`，而 `Account.Environment` 默认是 `Live`（`Domain/Entities/Account.cs:22`）。
- 后果：从 **Paper（模拟）TWS** 同步进来的账户被标成 `Live`，污染 `/api/portfolio?env=live` 的过滤，把模拟数据混入实盘统计。
- 建议：TWS 同步时按 `TwsOptions.Mode` 写入 `Environment`，或让 `Environment` 默认 `Paper`。

### 🟠 C-5 `IngestionService` 现金流按账户**整批替换** → 窗口外的真实现金流被清空
- 位置：`IngestionService.cs:103-122`（`ReplaceByAccountAsync`）。
- 现象：Flex “Cash Transactions” 报表若只覆盖某窗口（如近 30 天），整批替换会**删掉该账户所有既有现金流水，再insert窗口内这批** → 窗口外的历史现金（如早期入金）丢失。
- 影响：每日盈亏的“纯交易盈亏”扣减失真（入金被洗掉，净值差分被误算成盈利）。
- 建议：现金流水也应像 `Trade` 那样做**增量 + 去重**，而非整批替换；或仅替换报表实际覆盖的日期范围。

### 🟠 C-6 `SnapshotService.SeedHistoryAsync` 的 `ReplaceByAccountAsync` 清空该账户全部现金流
- 位置：`SnapshotService.cs:211-222`。种子接口把 `ReplaceByAccountAsync(key, [demoDeposit])` 写入——若 `key` 是真实账户号，会**抹掉该账户所有真实录入的现金流**（演示种子有破坏性）。
- 缓解：接口已标 `ApiExplorerSettings(IgnoreApi = true)` 并在注释写“生产勿调用”，但仍可被直接 POST。建议加保护（如仅允许 `AccountId == "DEMO"` 或显式 `--force` 参数）。

### 🟠 C-7 每日盈亏缺口日归因不准确
- 位置：`DailyPnlService.cs:40-64`。若相邻快照间隔 >1 天（周末/未运行），差分会把多日累计变动算作单日盈亏。
- 影响：曲线在缺口后的一天出现“异常大”的日盈亏。属已知简化，建议前端标注或后端按自然日插值。

### 🟠 C-8 自动交易配置 / 全局设置**全内存，重启即丢失**
- 位置：`AutoTrader._configs` / `_settings`（`AutoTrader.cs:36-39`）。
- 影响：用户精心配置的订阅标的、DryRun、策略参数，进程一重启全部清空，需重新录入。可用性差，且容易因“忘记重配 DryRun=true”而在重启后误触发（虽仍有全局 `LiveAcknowledged` 兜底）。
- 建议：配置持久化到 DB 或文件，启动时加载。

### 🟠 C-9 `DailyLossLimit`（当日亏损熔断）实际是死配置
- 位置：`OrderService.GetDailyRealizedPnlAsync`（`src/IBTrade.Infrastructure/Orders/OrderService.cs:166`）**恒返回 0**，仅 `Console.WriteLine` 告警。
- 影响：配置了 `DailyLossLimit` 也不会真正熔断（风控 `RiskGuard.Check` 的日亏分支永远不满足）。这是“安全特性静默失效”——比没有更危险，因为运维会误以为有保护。
- 建议：接入真实的当日盈亏数据源（如从 `RealizedPnl`/成交计算当日累计），或干脆在 UI 标注“未启用”并移除该死分支，避免误导。

### 🟡 C-10 `FlexQueryClient.Parse` 解析失败静默归零
- `Dec()`（`FlexQueryClient.cs:135-140`）解析失败返回 `0m`，可能把价格/数量错误归零而不告警；`ParseFlexDateTime` 失败回退 `DateTime.UtcNow`（见下）。

### 🟡 C-11 Flex 成交时间被强制视为 UTC，可能跨日错位
- 位置：`FlexQueryClient.ParseFlexDateTime`（`FlexQueryClient.cs:165-178`）用 `AssumeUniversal` + `SpecifyKind(Utc)`。IBKR Flex 的 `dateTime` 通常是账户时区（多为美东），按 UTC 解读会**偏移数小时**，可能把一笔收盘后的成交算到“次日”，影响 FIFO 配对与每日盈亏归属。建议按报表配置时区解析。

### 🟡 C-12 `FlexPositionDto`/`FlexTradeDto` 的 `accountId` 仅按行读取，缺失时回退空串
- 位置：`FlexQueryClient.Parse`（`FlexQueryClient.cs:91-105`）`Attr(e,"accountId")` 缺省 `""`。若 Flex 报表行上不带 `accountId`（只放在 `<FlexStatement accountId=...>` 上），所有持仓/成交会变成 `AccountId=""`，破坏按账户替换与汇总。建议回退到父 `<FlexStatement>` 的 `accountId`。

---

## 五、代码质量（可读性 / 维护性 / 健壮性）

### 👍 亮点
- **分层清晰**：Domain（实体/枚举/仓储接口）/ Application（用例+服务）/ Infrastructure（EF+TWS+Flex）/ Api（控制器+中间件），依赖方向正确，接口隔离好。
- **注释质量高**：几乎每个类/方法都有中文 `///` 说明设计意图、约束与权衡，可读性很好（例如 `AutoTrader`、`TwsClient`、`SnapshotService` 的注释）。
- **前端质量高**：`types.ts` 与后端 DTO 逐一对齐；`lib/api.ts` 统一封装超时(20/45/60s)与错误解析；页面有完善的 loading / toast / 错误兜底；实盘确认与后端形成纵深防御。
- **防御性校验到位**：`OrderService.ValidateRequest` 拦负数/零/超上限/负限价；`RiskGuard` 多层拦截；`UpsertConfig` 同样做了数量校验。

### 🟡 待改进（维护性 / 健壮性）
1. **实体层不一致**：`Order` 实体未继承 `Entity`（自带 `Id`/时间字段），与 `Position`/`Trade` 等不一致；建议统一。
2. **重复枚举**：`Side` 与 `OrderSide` 是两套完全一样的买/卖枚举（Trade 用前者、Order 用后者），易误用。建议统一为一个。
3. **`Console.WriteLine` 仍在用**：`OrderService` 用 `Console.WriteLine` 输出风控告警（C-9），应改用注入的 `ILogger`。
4. **仓储层缺约束配置**：所有去重/幂等依赖业务层 `FirstOrDefault`+`RemoveRange`/`AddRange`，没有数据库唯一索引兜底（见 ST-3）。建议迁移中加索引。
5. **无启动期自动迁移**：`Program.cs` 未调用 `Database.Migrate()`/`EnsureCreated()`，首次部署需手动 `dotnet ef database update`，否则启动后首个 DB 操作抛异常。建议加 `app.Services.CreateScope()` 内 `db.Database.Migrate()`。
6. **前端 BacktestPage 进入即自动 `run()`**：未连 Gateway 时会触发一次失败请求（已 catch，仅 UX 噪音）。
7. **前端无运行时 API Key 入口**：`VITE_IBTRADE_API_KEY` 仅构建期注入（`lib/api.ts:22`），生产环境要调实盘必须在构建时固化密钥，无“运行时填写”路径。对自托管可接受，但需文档说明。
8. **`CaptureTodayAsync` 的 `today` 变量在 `asOf` 有值时仍被 `TradingCalendar.LatestTradingDay(DateTime.Now)` 覆盖**（`SnapshotService.cs:32-34`）：当传入 `asOf`（回填历史）时，`today` 又被本地今日覆盖，导致回填日期失效——这是个**逻辑 bug**：`asOf` 参数实际上不生效（除非 `asOf` 分支在后面覆盖，但代码里 `today = asOf.Value.Date` 又被下一行 `today = LatestTradingDay(DateTime.Now)` 覆盖）。请重点核实：回填历史快照的 `date` 入参当前是**无效**的。

---

## 六、综合评估与修复优先级

### 整体评价
- **架构与可读性**：优秀（分层、注释、前端工程化都到位）。
- **安全性**：本地自托管场景下可接受，但存在**真实资金相关的两处硬伤**（市价单绕过金额风控、自动交易双计超卖）和源码弱口令/TFlex token 暴露，公网部署风险高。
- **稳定性**：存在**启动时崩溃（启用 TWS）**与**历史数据 clientId 并发**两个会显著影响可用性的阻断点；其余为健壮性改进。
- **正确性**：核心看板在“无做空、UTC 时区、纯 Flex 摄入”假设下正确；一旦引入做空、非 UTC 部署、TWS 同步、或后台并发，会出现数据失真。

### 必须优先修复（P0，阻断 / 真实资金）
1. **C-1** 自动交易持仓双计超卖（改合并口径，确保每笔成交只计一次）。
2. **ST-1** TWS 托管服务 Scoped→Singleton 启动崩溃（改 `IServiceScopeFactory`）。
3. **ST-2** 历史数据 `clientId=3` 并发冲突（每请求唯一 clientId 或串行化）。
4. **S-3** 手动市价单绕过 `MaxOrderAmount`（下单服务始终传入估算价或显式拒绝无价市价单）。

### 应当修复（P1，数据正确 / 安全加固）
5. **C-2** FIFO 做空处理（或明确不支持并拦截）。
6. **C-3** 时区口径统一（本地 vs UTC）。
7. **C-4** TWS 同步账户 `Environment` 赋值。
8. **C-5 / C-6** 现金流水改增量去重，种子接口加保护。
9. **C-8** 自动交易配置持久化。
10. **C-9** `DailyLossLimit` 要么真接入要么从 UI 移除（消除死配置误导）。
11. **S-1 / S-2** Flex token 不在日志回显；DB 默认弱口令改为强制环境变量。

### 建议改进（P2，稳健性 / 质量）
12. **ST-3** 加唯一索引 + 事务化 Upsert。
13. **ST-4/5/6** 读线程 NRE、订单态清理、重连退避。
14. **C-11/12** Flex 时间时区、accountId 父级回退。
15. **代码质量 1-8** 实体统一、枚举合并、`ILogger` 替换 `Console`、自动迁移、前端 API Key 入口、回填日期 bug（见五-8）。

---

## 七、附：自测覆盖说明
- ✅ 已逐文件走读：Domain（9 实体 + 6 枚举 + 7 仓储接口）、Infrastructure（仓储实现、TwsClient、TwsOrderClient、FlexQueryClient、OrderService、DI）、Application（AutoTrader、Ingestion、TwsSync、TwsLiveFeed、Snapshot、Portfolio、DailyPnl、RealizedPnl、PositionDailyPnl、Backtest+策略+指标、RiskGuard、MarketData、DI）、Api（Program、ApiKeyAuthMiddleware、8 个控制器、2 个 HostedService）、前端（types、lib/api、3 个页面、App）。
- ⚠️ 未执行：因依赖 IB Gateway / TWS / PostgreSQL 运行实例与真实账户，未做集成运行验证；上述结论均来自静态走读与逻辑推演，建议在启用 TWS / 实盘前先按 P0 修复并做最小连通性验证。
