# IB 交易引擎项目 · 逐功能自测与质量评估

> 范围：`trade/`（C# 引擎 `IBTrade.*` + React 前端）。逐文件精读后的人工自测（非单元自测），从**安全性 / 稳定性 / 合理性 / 代码质量（可读性·可维护性·健壮性）**四维度评估。
> 评估基线：以"本地自托管、Paper 模拟盘优先、真实资金需显式放开"为设计意图。

---

## 一、结论速览（按严重程度排序）

| 级别 | 位置 | 问题 | 影响 | 状态 |
|---|---|---|---|---|
| 🔴 P0 | `AutoTrader.ComputePosition` (AutoTrader.cs:302-323) | 去抖只看**本引擎内存订单**，不读券商真实持仓 | 引擎重启或券商已有持仓时，**重复建仓**（推翻"重启安全"旧结论） | ✅ 已修复 |
| 🔴 P0 | `IngestionService` (IngestionService.cs:56-83) | Flex 持仓/成交**纯追加**无去重（仓 `PositionRepository.AddAsync` 仅 Add） | 重复摄入报表产生重复行 → 市值/未实现盈亏/FIFO 已实现盈亏全部被放大 | ✅ 已修复 |
| 🟠 P1 | `RiskGuard.Check` (RiskGuard.cs:58-59) | 对 `Mode==Live` **永远拦截**，但 `RiskSettings` 注释与前端却提供"实盘 Live"开关 | 死配置 + UI 误导：切到实盘必然失败，且与注释矛盾 | ✅ 已修复 |
| 🟠 P1 | `TwsOrderClient.CancelOrderAsync` (TwsOrderClient.cs:155) | 超时/失败也 `return true` | 撤单失败被掩盖，前端显示"已撤销" | ✅ 已修复 |
| 🟡 P2 | `HistoricalDataService` (HistoricalDataService.cs:18-23) | 每次新建 `TwsClient` 但固定 `clientId+2=3` | 并发历史请求（回测+自动交易）会因同 clientId 被 IBKR 拒绝 |
| 🟡 P2 | DB 默认连接串 (DependencyInjection.cs:24-25) | 明文弱密码 `Password=123456`/`aiuser`（虽可 env 覆盖） | 默认值弱，易误用上线 |
| 🟡 P2 | Flex token 走 URL (FlexQueryClient.cs:31,39) | token 在查询串，会被日志/代理记录 | 凭证泄露风险（外部约束，仅提示） |
| 🟢 P3 | 重复 `AuditCtx()` 样板 + `RecordAudit` 混入自动交易事件流 | 两个控制器几乎相同的审计样板；下单审计写入自动交易事件流且无持久化 | 可维护性/语义清晰度/审计留存不足 |
| 🟢 P3 | 前端 `OPERATOR` 模块加载时一次性读取 (api.ts:26-27) | 运行中改 operator 不生效；`实盘` 文案在 Paper 护栏下实为"非演练下单" | 轻微误导 |

**亮点（做得好的）** 见第四节。

---

## 二、逐功能自测

### 1. 入口 / 鉴权 / 配置（Program.cs · ApiKeyAuthMiddleware.cs · CORS）
- **安全性**：`ApiKeyAuthMiddleware` 采用 secure-by-default——配置 key 后全接口校验 `X-Api-Key`/`Bearer`；**未配置 key 时，生产环境拒绝匿名写操作（POST/PUT/DELETE），只读 GET 照常**。白名单 `/health`、`/`、`/swagger*`。设计合理。
- **稳定性**：启动期对"未配置 ApiKey"明确告警（Program.cs:72-79）。
- **合理性**：CORS 在开发放开、生产必须显式配置 `AllowedOrigins` 否则拒绝跨域（Program.cs:47-68）。合理。
- **代码质量**：中间件职责单一、注释充分。**小问题**：生产环境"拒绝写"依赖 `!IsDevelopment()`，若有人误把生产 `ASPNETCORE_ENVIRONMENT` 设为 `Development` 则保护失效——可接受（环境约定），但建议生产环境即便 Development 也强制要求 key（或新增 `RequireApiKey` 显式开关）。

### 2. 下单与风控（OrdersController · OrderService · RiskGuard · InMemoryOrderStore · PlaceOrderRequest）
- **健壮性（好）**：三层校验——DTO `DataAnnotations`（数量>0、上限）+ 服务层 `ValidateRequest`（负数/零/超大/限价必填且为正，OrderService.cs:137-159）+ `RiskGuard.Check`（总开关/持仓数/单笔金额/当日熔断）。
- **安全性**：所有下单经统一入口，风控硬拦截；`OrderService.PlaceAsync` 有 30s 超时 + 连接重试（`EnsureConnectedAsync`）；失败回滚状态。
- **🔴 P0 关联**：去抖逻辑不在此层，见第 3 节 `AutoTrader`。
- **🟠 P1 · 实盘死配置**：`RiskGuard.Check` 一见 `Mode==Live` 即 `return (false,"当前为实盘模式，下单被风控拦截")`；但 `RiskSettings` 注释写"实盘需显式置为 Live"（RiskGuard.cs:13-14），前端 `OrdersPage` 还提供"实盘 Live"开关并勾选确认（OrdersPage.tsx:333-349）。结论：**Live 模式永远下不了单**。若这是有意的安全护栏，应（a）把注释改成"实盘当前被强制拦截"，（b）前端隐藏/禁用 Live 开关或明确提示不可达；若将来要真放开实盘，应使用更强的闸门（如环境变量 `ALLOW_LIVE_TRADING=true` + 二次确认），而非现在"看似可设实则必败"。
- **🟡 当日熔断透明化（好）**：`GetDailyRealizedPnlAsync` 恒返回 0 且显式 `Console.WriteLine` 告警（OrderService.cs:37-39），避免熔断"静默失效"——但用的是 `Console.WriteLine` 而非 `ILogger`，在容器化部署下日志不易采集，建议改用 `ILogger`。
- **🟠 P1 · CancelOrderAsync 掩盖失败**：`TwsOrderClient.CancelOrderAsync` 无论是否真正撤成功都 `return true`（TwsOrderClient.cs:155）；`OrdersController.Cancel` 据此返回 200 `cancelled:true`。撤单被拒/超时会被误报为成功。应仅在状态确认 `Cancelled/Rejected` 时返回 true，否则 false。
- **可维护性**：`OrdersController` 注入 `IAutoTrader` 仅用于 `RecordAudit`（OrdersController.cs:16），把"手动下单"审计混入"自动交易事件流"语义混杂；且 `AuditCtx()` 与 `AutoTradeController` 几乎重复（见 P3）。

### 3. 自动交易引擎（AutoTrader · AutoTradeSettings · AutoTradeEvent · IAutoTrader）
- **安全性/默认安全（好）**：总开关默认关、每标的默认 `DryRun=true`（只发信号不下单）、串行评估、`Log` 审计、手动触发带来源 IP/操作人。
- **🟢 去抖逻辑（会话内有效）**：基于净持仓（`netQty<=0 && openBuy==0` 才买；`netQty>0 && openSell==0` 才卖），避免同信号重复下单——**在单次运行会话内是正确的**。
- **🔴 P0 · 重启/券商已有持仓会重复建仓（最关键）**：`ComputePosition` 只读 `_orders.List()`（内存订单 `InMemoryOrderStore`），**不读券商真实持仓**。
  - 引擎重启后内存清空、`netQty=0`；若券商仍持有上次由本引擎建立的仓位（或用户经 IBKR 手动建的仓），下一轮 Buy 信号会**再次买入 → 双倍持仓**。
  - 这与之前"重启安全、不会重复下单"的结论**相反**：之前误以为去抖依赖 broker 实时状态，实际依赖的仅是本引擎内存订单历史。
  - **修复方向**：`ComputePosition` 应基于 `TwsLiveFeed` 最新快照或 `PositionService` 的券商真实持仓（而非仅本引擎订单）推导 `netQty/openBuy/openSell`；或在引擎启动时从券商同步一次基准持仓。
- **稳健性**：`LoopAsync` 每标的间 `Task.Delay(2s)` 规避 IBKR pacing；最小 poll 5s；单标的异常被捕获并记日志不中断循环。良好。
- **🟢 合理性小瑕疵**：`dryRun=false` 在全局 `RiskGuard.Mode=Paper` 下实际仍是 Paper 下单，但 UI 文案"转实盘"（AutoTradePage.tsx:164-175, 691-697）易误导，应称"真实下单（仍受模拟盘护栏）"。

### 4. TWS / IB Gateway 接入（TwsClient · TwsOrderClient · TwsSyncService · TwsLiveFeed · TwsOptions）
- **稳定性（好）**：
  - `TwsLiveFeed` 持久连接 + 后台 5s 监控**自动重连**（TwsLiveFeed.cs:87-124）。
  - `TwsClient` 连接失败 `TcpPortReachable` 探测，区分"端口未开"与"已开未登录/未授权"，给出可操作提示（TwsClient.cs:251-254）。
  - 错误码分类：良性(2104-2170)忽略；连接/登录类(501-504/1100-1102)标记失败；历史请求类按 reqId 标记（TwsClient.cs:436-454）。处理得当。
  - 多端口回退：sync/live/historical 依次尝试 7497/7496/4002/4001，零配置兼容 TWS 与 Gateway。
- **隔离性（好，已核实）**：`TwsClient` 经工厂每次 `new`（不同 socket、不同 clientId）；clientId 分配 live=1、sync=2、historical=3、order=5，互不冲突；历史行情 `finally` 中的 `Disconnect()` 不会误伤实时推送单例。
- **🟡 P2 · 历史 clientId 并发冲突**：`HistoricalDataService` 单例每次 `new TwsClient` 但**固定 clientId=3**（HistoricalDataService.cs:18）。若回测与自动交易轮询**同时**拉历史（同 clientId=3），第二个连接会被 IBKR 拒绝。建议每次递增分配 clientId（如 `Interlocked` 计数器）或使用连接池。
- **🟡 P2 关联**：撤单失败掩盖见第 2 节。
- **健壮性**：`EnsureConnectedAsync` 用 `TaskCompletionSource` + 超时；`nextValidId` 用 `Math.Max` 防复用。合理。

### 5. 数据接入（IngestionService · FlexQueryClient）
- **🔴 P0 · 持仓/成交重复追加（最关键数据正确性问题）**：`IngestionService.IngestAsync` 第 2 步持仓 `positionRepo.AddAsync`、第 3 步成交 `tradeRepo.AddRangeAsync` 均为**纯追加**，无去重键（IngestionService.cs:56-83；`PositionRepository.AddAsync` 仅 `db.Positions.AddAsync`，TradeRepository 同理）。
  - 后果：重复跑 `/api/ingestion/run`（或同环境多次摄入）会写入**重复 Position/Trade 行** → `PortfolioService.GetSummaryAsync` 市值/未实现盈亏被放大、FIFO 已实现盈亏错乱。
  - 对比：`TwsSyncService.PersistAsync` 已正确用 `ReplaceByAccountAsync`（快照语义），但 Flex 路径没有。
  - **修复方向**：持仓按 (AccountId, Symbol) **upsert/替换**（快照语义，同 TWS 路径）；成交需可靠去重（如唯一约束 `(AccountId, Symbol, TradeTimeUtc, Side, Price, Quantity)` + `ON CONFLICT`）。
- **🟡 P2 · Flex token 走 URL**：`?t={token}`（FlexQueryClient.cs:31,39），token 会出现在服务器访问日志/代理日志。属 IBKR Flex Web Service 的外部约束，无法改 header，建议文档提示"确保传输链路 HTTPS 且仅授权服务调用"。
- **合理性（好）**：现金交易"仅当报表确有现金数据才整批替换"（避免清掉手动录入）；`RealizedPnls` 按账户整批替换覆盖。设计细致。
- **稳健性（好）**：`DownloadXmlAsync` 对报表未就绪(1004/Failed)有限重试(5 次)；XML 解析按属性读取、容错。

### 6. 组合 / PnL / 持仓（PortfolioService · SnapshotService · DailyPnlService · PositionDailyPnlService · RealizedPnlService · PositionService）
- **正确性（好）**：
  - `PortfolioService.ComputeRealizedPnl` 按 (账户, 标的) **FIFO 配对**买卖、佣金资本化，逻辑正确。
  - `DailyPnlService` 净值差分**扣除当日外部现金流**得纯交易盈亏（避免把入金算成赚），首条无前日可比记为 0。合理。
  - `PositionDailyPnlService` 个股当日贡献=今日市值−前日市值，含已平仓标的 `−前日市值`；无快照时用实时持仓回退。合理。
  - `SnapshotService` 的 `Upsert`（按 account+date / account+date+symbol）**幂等**，故 `rebuild-history`/`capture-today` 重复调用安全（已核实 DailySnapshotRepository/PositionDailySnapshotRepository 的 Upsert）。
- **稳健性（好）**：`rebuild-history` 用"历史收盘价 × 真实持仓数量"重建市值，`CloseOnOrBefore` 停牌日用前一交易日价；跳过周末。设计务实。
- **🟢 合理性**：`RebuildHistoryAsync` 用固定 `"2 M"` 窗口拉历史，对超过 2 个月的回看区间若早期无数据会 `anyPrice=false` 跳过该日——长区间边缘日可能缺失，属已知简化。
- **可维护性（好）**：服务拆分清晰（组合/快照/每日/个股/已实现/持仓），依赖经接口注入。

### 7. 回测（BacktestService · Strategies · Indicators · BacktestController）
- **合理性（好，且文档化）**：`BacktestService` 为**多头 long-only、全仓进出、信号根收盘价成交、固定佣金**的简化模型（BacktestService.cs:8-12），足够验证方向性策略；`CommissionPerTrade` 默认 1（BacktestRequest.cs:33）。
- **指标**：总收益、买入持有对比、最大回撤、胜率、夏普（按 K 线粒度年化）。
- **🟢 小幅不准（非 bug）**：
  - 用"权益收益率"序列算 Sharpe，在成交根同时含现金变化，轻微失真；
  - 未做拆股/分红调整，若回看区间发生拆股，价格序列会断裂。
  - 均属简化模型固有局限，建议文档注明。
- **策略实现（好）**：`SmaCrossStrategy` 在 fast≥slow 时自动纠正；`GenerateSignals` 与 K 线等长、其余默认 Hold；`StrategyFactory` 自动汇总注册的 `IStrategy`，新增策略零侵入。扩展性良好。
- **安全性/健壮性**：`BacktestController.Run` 捕获 `ArgumentException`(400)/`InvalidOperationException`(502) 并返回友好错误；前端超时 60s。合理。

### 8. 前端（App.tsx · api.ts · OrdersPage · AutoTradePage · BacktestPage · 组件）
- **工程质量（好）**：`api.ts` 统一请求底层（abort 超时 + 错误体解析）、`X-Api-Key`/`X-Operator` 透传；UI 骨架屏、toast、响应式、深色模式、演练模式强提示。
- **稳健性（好）**：看板主加载失败有重试；`loadDailyPnl/loadTrades` 失败不阻塞主看板；实时推送 2s 轮询 + 自动重连 UI 状态。
- **🟢 P3 · `OPERATOR` 一次性读取**（api.ts:26-27）：模块加载时读 `localStorage`，运行中改操作人不生效，需刷新。轻微。
- **🟢 P3 · 文案误导**：自动交易"转实盘"按钮在 Paper 护栏下实为"非演练真实下单"（见第 3 节）。
- **合理性**：`BacktestPage` 进入自动跑一次（依赖网关历史数据），无数据时友好提示；`OrdersPage` 数量/限价前端校验与后端双层一致。

---

## 三、代码质量总评（可读性 / 可维护性 / 健壮性）

**优点**
- 分层清晰：Api（薄控制器）→ Application（用例/领域）→ Infrastructure（外部/持久化）→ Domain（实体/枚举/仓储接口）。
- 依赖经接口注入、Composition Root 集中在 `Program.cs` + 两个 `DependencyInjection`；策略/指标可插拔。
- 关键安全/稳定决策均有 XML 注释说明意图（如 RiskGuard、ApiKey 中间件、TwsClient 错误码分类）。
- 统一异常处理与审计样板已在控制器层起步。

**待改进（P3）**
1. **重复样板**：`OrdersController.AuditCtx()` 与 `AutoTradeController.AuditCtx()` 几乎相同（OrdersController.cs:18-24 vs AutoTradeController.cs:23-29）→ 抽成 `IHttpContextAccessor` 扩展方法或基类 `ApiControllerBase`。
2. **审计语义/留存**：手动下单审计经 `IAutoTrader.RecordAudit` 写入自动交易事件流（AutoTrader.cs:362-363），事件流内存上限 200、重启即失、无持久化 → 审计价值有限。建议独立 `IAuditLogger`（可落库）。
3. **风控拦截用 `Console.WriteLine`**：`OrderService.cs:39` 应用 `ILogger`。
4. **配置文档缺失**：`ApiKey`/`IBTRADE_API_KEY`、`AllowedOrigins`、`TWS:*`、`IBKR_FLEX_*`、`IBTRADE_DB_PASSWORD` 等缺少集中说明（appsettings 示例/README）。
5. **弱默认值**：DB 默认弱密码（P2）、Live 模式开关误导（P1）——属"配置即文档"问题。

---

## 四、亮点（确认无需改动）
- 下单多层校验 + 超时/重试 + 审计来源 IP/操作人。
- 自动交易默认 DryRun + 总开关关闭 + 串行 + 审计 + 会话内去抖。
- API Key secure-by-default + 生产 CORS 拒绝跨域。
- 快照 Upsert 幂等、TwsLiveFeed 自动重连、多端口回退、错误码分类。
- 组合 FIFO 已实现盈亏、每日盈亏扣外部现金流、个股归因、回测长-only 简化且文档化。
- 前端统一错误/超时/骨架/toast/响应式。

---

## 五、建议修复优先级
1. **P0**：`AutoTrader` 改用券商真实持仓去抖（防重启/已有持仓重复建仓）。
2. **P0**：`IngestionService` 持仓/成交改为 upsert/唯一约束去重。
3. **P1**：`RiskGuard` Live 策略与 UI 开关一致性（禁用并隐藏，或加环境变量强闸门）。
4. **P1**：`CancelOrderAsync` 仅在确认撤成功时返回 true。
5. **P2**：历史 clientId 递增分配；DB 默认密码强告警+文档；Flex token 文档提示。
6. **P3**：审计样板抽取 + 独立 `IAuditLogger` 落库；`Console.WriteLine`→`ILogger`；集中配置文档。

> 本次为**评估/自测**，未改动代码。需要我直接修复上述 P0/P1 吗？建议优先做第 1、2 项（真实资金与数据正确性风险最高）。

---

## 六、已修复记录（P0 / P1）

均已落地并通过 `dotnet build`（0 错误 0 警告）与前端 `tsc --noEmit` 校验。

### P0-1 · 自动交易重启重复建仓 —— `AutoTrader.cs`
- 新增 `ComputePositionAsync`：持仓基线来自**持久化持仓**（`IPositionRepository.GetAllAsync`，由 Flex / TWS 同步写入，重启后仍在），再叠加本会话内存订单的成交/挂单增量。
- 引擎为 `Singleton` 而仓储为 `Scoped`，为避免捕获 Scoped 服务，改为注入 `IServiceScopeFactory` 并在调用时建 `CreateAsyncScope()` 解析仓储。
- 去抖逻辑不变（空仓且无可挂买单才买 / 持仓且无挂卖单才卖），但基线不再随重启归零 → 杜绝重复建仓。
- 已知轻微偏差：若本次会话内又执行 Flex 摄入，同一笔成交可能同时计入基线与增量（保守高估，不会重复建仓）。已在代码注释中说明。

### P0-2 · Flex 数据重复追加 —— `IngestionService.cs`
- 持仓：由纯 `AddAsync` 改为**按账户 `ReplaceByAccountAsync`**（快照语义，与 `TwsSyncService` 一致）。
- 成交：新增 `TradeSignature` 复合键（账户+标的+时间+方向+价格+数量+佣金）去重，摄入前对每账户比对既有成交，仅插入未见过的行。
- 结果与 `TwsSyncService` 的幂等行为对齐，重复摄入不再产生重复行。

### P1-3 · 实盘死配置 —— `RiskGuard.cs` + 前端
- `RiskSettings` 新增 `LiveAcknowledged`（默认 `false`）。`RiskGuard.Check` 仅在 `Mode==Live && !LiveAcknowledged` 时拦截，消息明确指引去勾选确认。
- 前端 `types.ts` 的 `RiskSettings` 增加 `liveAcknowledged`；`OrdersPage` 的实盘确认勾选框同时写入风控草稿并在保存时一并提交，加载时回填勾选状态。实盘模式现在**可达且受显式确认门控**。

### P1-4 · 撤单失败被掩盖 —— `TwsOrderClient.cs`
- `CancelOrderAsync` 超时（未在限定时间内确认 `Cancelled`/`Rejected`）返回 `false`；仅当确实确认撤单/拒单时才 `true`。`OrdersController.Cancel` 据此返回 404，前端不再误报"已撤销"。

> P2/P3（历史 clientId 并发、DB 默认弱密码、Flex token 走 URL、审计落库、Console→ILogger、配置文档）暂未改动，留作后续改进。
