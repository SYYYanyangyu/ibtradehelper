import type {
  PortfolioSummary,
  PositionLite,
  CashTransaction,
  DailyPnlPoint,
  PerformanceCompare,
  PerformanceRange,
  PositionDailyPnl,
  TradePnl,
  TradeRecord,
  OrderDto,
  RiskSettings,
  OrderSide,
  OrderType,
  AutoTradeStatus,
  AutoTradeSymbolConfig,
  AutoTradeSettings,
  TrendScalperSettings,
  TrendScalperStatus,
  DisciplineSettings,
  DisciplineStatus,
  DisciplineCheckResult,
  WeeklyReview,
  MarketNewsFeed
} from '../types'

// 未设置时保持本地开发默认；显式设为空字符串时走当前页面的同源地址（由反向代理转发 /api）。
const rawApiBase = import.meta.env.VITE_API_BASE as string | undefined
const API_BASE: string = rawApiBase === undefined ? 'http://localhost:8090' : rawApiBase

export function apiUrl(path: string): URL {
  const base = API_BASE || (typeof window !== 'undefined' ? window.location.origin : 'http://127.0.0.1')
  return new URL(path, base)
}

// 引擎 API Key（可选）：配置后统一在请求头携带 X-Api-Key，使真实下单/启停接口受保护。
// 未配置时不附加任何头，兼容开发环境与只读看板场景。
const API_KEY: string =
  ((import.meta.env.VITE_IBTRADE_API_KEY as string | undefined) ?? '').trim()

// 操作人标识（用于引擎操作审计）：优先取本地设置，缺省记为 web-client。
const OPERATOR: string =
  ((localStorage.getItem('ibtrade-operator') as string | null) ?? 'web-client').trim() || 'web-client'

/**
 * 统一请求底层：处理 abort 超时 + 解析后端错误体。
 * - 默认 20s 超时，避免后端阻塞（如未连 TWS 的下单）导致前端永久 spinner。
 * - 2xx 返回解析后的 JSON；非 2xx 抛出携带后端 error 文案的 Error。
 */
async function req<T>(
  url: string,
  init?: RequestInit,
  timeoutMs = 20000
): Promise<T> {
  const ctrl = new AbortController()
  const timer = window.setTimeout(() => ctrl.abort(), timeoutMs)
  try {
    const mergedHeaders: Record<string, string> = {
      ...(API_KEY ? { 'X-Api-Key': API_KEY } : null),
      ...(OPERATOR ? { 'X-Operator': OPERATOR } : null),
      ...(init?.headers as Record<string, string> | undefined)
    }
    const res = await fetch(url, { ...init, signal: ctrl.signal, headers: mergedHeaders })
    const text = await res.text()
    const data = text ? safeJson(text) : null
    if (!res.ok) {
      let msg = `请求失败 (${res.status})`
      if (data && typeof data === 'object') {
        const err = (data as { error?: string }).error
        if (typeof err === 'string' && err) msg = err
      } else if (typeof data === 'string' && data) {
        msg = data
      }
      throw new Error(msg)
    }
    return data as T
  } catch (e) {
    if (e instanceof DOMException && e.name === 'AbortError') {
      throw new Error('请求超时，请确认交易引擎(C# 8090)已启动且网络可达')
    }
    throw e
  } finally {
    window.clearTimeout(timer)
  }
}

function safeJson(text: string): unknown {
  try {
    return JSON.parse(text)
  } catch {
    return text
  }
}

/** GET JSON（保留旧名以便现有调用）。 */
function getJson<T>(url: string): Promise<T> {
  return req<T>(url)
}

/** 下单/撤单耗时较长（未连 Gateway 时后端会等待连接），放宽到 45s。 */
const ORDER_TIMEOUT = 45000
/** 评估需拉历史 K 线跑策略，与回测同级，放宽到 60s。 */
const EVAL_TIMEOUT = 60000

// ---- 组合看板（调 /api/portfolio、/api/positions、/api/tws）----

export type TradeEnvFilter = 'Paper' | 'Live'
/** '' = Paper+Live 混合（请求不带 env，后端按真实账户聚合） */
export type TradeEnvSelection = TradeEnvFilter | ''

export interface AccountInfo {
  accountId: string
  environment: string
  alias?: string | null
  baseCurrency?: string
}

/** 组合看板汇总。env=Paper|Live 可隔离模拟/真实；不传则全部混算。 */
export function getPortfolio(accountId?: string, env?: TradeEnvFilter): Promise<PortfolioSummary> {
  const url = apiUrl('/api/portfolio/summary')
  if (accountId) url.searchParams.set('accountId', accountId)
  if (env) url.searchParams.set('env', env)
  return getJson<PortfolioSummary>(url.toString())
}

/** 账户列表：Flex 入库的真实 IB 账户。 */
export function getAccounts(): Promise<AccountInfo[]> {
  return getJson<AccountInfo[]>(apiUrl('/api/portfolio/accounts').toString())
}

/** 全部持仓（用于派生账户列表）。 */
export function getPositions(): Promise<PositionLite[]> {
  return getJson<PositionLite[]>(apiUrl('/api/positions').toString())
}

/** 每日盈亏曲线（最近 days 天）。env 用于 Paper/Live「全部」按环境汇总，避免串盘。 */
export function getDailyPnl(accountId?: string, days = 30, env?: TradeEnvFilter): Promise<DailyPnlPoint[]> {
  const url = apiUrl('/api/portfolio/daily-pnl')
  if (accountId) url.searchParams.set('accountId', accountId)
  if (env) url.searchParams.set('env', env)
  url.searchParams.set('days', String(days))
  return getJson<DailyPnlPoint[]>(url.toString())
}

/** 组合 vs 恒生/美股三大指数的区间收益率（Flex NAV + Gateway 指数日线）。 */
export function getPerformance(
  accountId?: string,
  range: PerformanceRange | string = '1Y',
  env?: TradeEnvFilter
): Promise<PerformanceCompare> {
  const url = apiUrl('/api/portfolio/performance')
  if (accountId) url.searchParams.set('accountId', accountId)
  if (env) url.searchParams.set('env', env)
  url.searchParams.set('range', range)
  return getJson<PerformanceCompare>(url.toString())
}

/** 查最近 days 天的外部现金流（入金/出金/股息等）。不传 accountId 取跨账户汇总。 */
export function getCashTransactions(accountId?: string, days = 30): Promise<CashTransaction[]> {
  const url = apiUrl('/api/portfolio/cash-transactions')
  if (accountId) url.searchParams.set('accountId', accountId)
  url.searchParams.set('days', String(days))
  return getJson<CashTransaction[]>(url.toString())
}

/** 个股每日盈亏归因。务必传 env，避免「全部」串到 Paper+Live 混算。 */
export function getPositionDailyPnl(
  date?: string,
  accountId?: string,
  env?: TradeEnvFilter
): Promise<PositionDailyPnl> {
  const url = apiUrl('/api/portfolio/position-daily-pnl')
  if (date) url.searchParams.set('date', date)
  if (accountId) url.searchParams.set('accountId', accountId)
  if (env) url.searchParams.set('env', env)
  return getJson<PositionDailyPnl>(url.toString())
}

/** 成交明细。务必传 env 以免 Paper/Live 混算。 */
export function getTrades(
  accountId?: string,
  days = 365,
  env?: TradeEnvFilter
): Promise<TradeRecord[]> {
  const url = apiUrl('/api/portfolio/trades')
  if (accountId) url.searchParams.set('accountId', accountId)
  if (env) url.searchParams.set('env', env)
  url.searchParams.set('days', String(days))
  return getJson<TradeRecord[]>(url.toString())
}

/** 逐笔 FIFO 平仓盈亏。不传 accountId/env 为全部账户。 */
export function getTradePnl(accountId?: string, env?: TradeEnvFilter): Promise<TradePnl> {
  const url = apiUrl('/api/portfolio/trade-pnl')
  if (accountId) url.searchParams.set('accountId', accountId)
  if (env) url.searchParams.set('env', env)
  return getJson<TradePnl>(url.toString())
}

/** 记录一笔外部现金流。金额符号会按类型自动归一（入金/股息/利息为正，出金/费用为负）。 */
export function addCashTransaction(reqBody: {
  accountId?: string
  type: CashTransaction['type']
  amount: number
  currency?: string
  date?: string
  note?: string
}): Promise<CashTransaction> {
  return req<CashTransaction>(apiUrl('/api/portfolio/cash-transactions').toString(), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(reqBody)
  })
}

/** 触发一次 IB Gateway 同步：刷新市价；不覆盖 Flex 的数量/均价。 */
export function syncGateway(): Promise<{
  accountId: string
  netLiquidation: number
  cashBalance: number
  positionCount: number
  connectedPort: number
  source: 'TWS' | 'Gateway'
  note?: string
}> {
  return req(apiUrl('/api/tws/sync').toString(), { method: 'POST' })
}

/** @deprecated 使用 syncGateway */
export const syncTws = syncGateway

export interface DataFeedStatusItem {
  feed: string
  environment?: string | null
  lastSuccessUtc: string
  accountId?: string | null
  connectedPort?: number | null
  message?: string | null
}

export interface DataFeedStatusResponse {
  feeds: DataFeedStatusItem[]
  legend: { flex: string; gateway: string; estimate: string }
}

/** 查询 Flex / Gateway 最近成功同步状态。 */
export function getDataFeedStatus(): Promise<DataFeedStatusResponse> {
  return req(apiUrl('/api/ingestion/status').toString())
}

/**
 * 拉取 Flex 报表入库（Token+QueryId）。
 * env 省略时跑全部已配置环境；指定 Paper/Live 只跑对应环境。
 */
export function runFlexIngest(env?: TradeEnvFilter): Promise<{
  status: string
  env: string
  queryId?: string | null
  feeds: DataFeedStatusItem[]
}> {
  const u = apiUrl('/api/ingestion/run')
  if (env) u.searchParams.set('env', env)
  // Flex Web Service 常需轮询几十秒～两分钟以上（尤其经代理），放宽到 3 分钟
  return req(u.toString(), { method: 'POST' }, 180_000)
}

export interface TwsLivePosition {
  symbol: string
  assetType: string
  currency: string
  quantity: number
  avgCost: number
  lastPrice: number
  bid: number
  ask: number
  marketValue: number
  unrealizedPnl: number
  change: number
}

export interface TwsLiveSnapshot {
  running: boolean
  accountId: string
  currency: string
  source: 'TWS' | 'Gateway'
  connectedPort: number
  lastTickUtc: string
  netLiquidation: number
  cashBalance: number
  positions: TwsLivePosition[]
  lastError?: string | null
}

/** 启动持久实时推送（连接保持，价格一动即刷新）。 */
export function startLive(): Promise<{ running: boolean; accountId: string; source: 'TWS' | 'Gateway'; connectedPort: number; positionCount: number }> {
  return req(apiUrl('/api/tws/live/start').toString(), { method: 'POST' })
}

/** 停止实时推送。 */
export function stopLive(): Promise<{ running: boolean }> {
  return req(apiUrl('/api/tws/live/stop').toString(), { method: 'POST' })
}

/** 读取当前实时快照（高频轮询）。 */
export function getLive(): Promise<TwsLiveSnapshot> {
  return req(apiUrl('/api/tws/live').toString())
}

// ---- 回测（与看板独立，调 /api/backtest）----

export interface BacktestTrade {
  entryTime: string
  entryPrice: number
  exitTime: string
  exitPrice: number
  quantity: number
  profitLoss: number
  returnPct: number
  commission?: number
  slippageCost?: number
}

export interface BacktestEquityPoint {
  time: string
  equity: number
}

export interface BacktestResult {
  symbol: string
  strategy: string
  startTime: string | null
  endTime: string | null
  barCount: number
  initialCapital: number
  finalEquity: number
  totalReturnPct: number
  buyHoldReturnPct: number
  maxDrawdownPct: number
  tradeCount: number
  winCount: number
  winRatePct: number
  sharpe: number
  fillMode?: string
  slippageBps?: number
  commissionPerTrade?: number
  totalCommission?: number
  totalSlippageCost?: number
  totalCost?: number
  costDragPct?: number
  approxGrossReturnPct?: number
  trades: BacktestTrade[]
  equityCurve: BacktestEquityPoint[]
}

export interface BacktestRequest {
  symbol: string
  strategy?: string
  duration?: string
  barSize?: string
  parameters?: Record<string, number>
  initialCapital?: number
  commissionPerTrade?: number
  /** next_open（默认，信号次根开盘）| close（同 bar 收盘，仅对比） */
  fillMode?: 'next_open' | 'close' | string
  /** 单边滑点基点，默认 5 */
  slippageBps?: number
}

/** 运行一次回测：拉历史数据 → 跑策略 → 输出绩效。 */
export async function runBacktest(body: BacktestRequest): Promise<BacktestResult> {
  return req<BacktestResult>(apiUrl('/api/backtest/run').toString(), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body)
  }, 60000)
}

export interface StrategyParamDef {
  key: string
  label: string
  defaultValue: number
  /** 参数说明（后端当前未返回，前端按可选处理，缺失时仅不显示提示）。 */
  description?: string
  step?: number
  min?: number | null
  max?: number | null
}

export interface StrategyDef {
  key: string
  name: string
  parameters: StrategyParamDef[]
}

/** 列出可用策略（含参数元数据，用于动态渲染参数输入框）。 */
export function listStrategies(): Promise<StrategyDef[]> {
  return getJson<StrategyDef[]>(apiUrl('/api/backtest/strategies').toString())
}

// ---- AI 策略闭环（白名单策略 + 强制演练部署）----

export interface AiSuggestResult {
  symbol: string
  strategyKey: string
  strategyName: string
  parameters: Record<string, number>
  duration: string
  barSize: string
  source: 'heuristic' | 'llm' | string
  rationale: string
  confidence: number
}

export interface AiSuggestAndRunResult {
  suggestion: AiSuggestResult
  backtest: BacktestResult
}

export interface DeployDryRunResult {
  symbol: string
  strategyKey: string
  parameters: Record<string, number>
  dryRun: boolean
  engineRunning: boolean
  riskMode?: string
  liveAcknowledged?: boolean
  message: string
}

export interface DeployCheckItem {
  id: string
  level: 'pass' | 'warn' | 'block' | 'info' | string
  title: string
  detail: string
}

export interface DeployPreflightResult {
  canDeploy: boolean
  hasWarnings: boolean
  summary: string
  checks: DeployCheckItem[]
  symbol: string
  strategyKey: string
  strategyName: string
  parameters: Record<string, number>
  duration: string
  barSize: string
  quantity: number
  riskMode: string
  liveAcknowledged: boolean
  engineRunning: boolean
  masterEnabled: boolean
  completedBarsOnly: boolean
}

export type DeployPreflightBody = {
  symbol: string
  strategyKey: string
  parameters?: Record<string, number>
  duration?: string
  barSize?: string
  quantity?: number
  fillMode?: string
  slippageBps?: number
  totalReturnPct?: number
  buyHoldReturnPct?: number
  maxDrawdownPct?: number
  sharpe?: number
  tradeCount?: number
  barCount?: number
  costDragPct?: number
  totalCost?: number
}

/** 部署前检查清单（不改配置）。 */
export function deployPreflight(body: DeployPreflightBody): Promise<DeployPreflightResult> {
  return req<DeployPreflightResult>(
    apiUrl('/api/ai/strategy/deploy-preflight').toString(),
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body)
    }
  )
}

export interface LlmProviderStatus {
  id: string
  displayName: string
  apiStyle: string
  baseUrl: string
  model: string
  models: string[]
  hasApiKey: boolean
  apiKeyEnv: string
}

export interface LlmCatalog {
  activeProvider: string
  providers: LlmProviderStatus[]
}

/** 列出 AI 厂商/模型占位配置。 */
export function listAiProviders(): Promise<LlmCatalog> {
  return getJson<LlmCatalog>(apiUrl('/api/ai/providers').toString())
}

/** 切换当前厂商和/或模型。 */
export function selectAiProvider(body: {
  providerId?: string
  model?: string
}): Promise<LlmCatalog> {
  return req<LlmCatalog>(apiUrl('/api/ai/providers/select').toString(), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body)
  })
}

/** 从厂商拉取模型列表（需已配置 Key）。 */
export function refreshAiModels(providerId: string): Promise<{
  provider: LlmProviderStatus
  warning?: string | null
  catalog: LlmCatalog
}> {
  return req(
    apiUrl(`/api/ai/providers/${encodeURIComponent(providerId)}/refresh-models`).toString(),
    { method: 'POST' },
    60000
  )
}

/** AI/启发式：自然语言建议策略并立即回测。 */
export function aiSuggestAndRun(body: {
  prompt: string
  symbol?: string
  duration?: string
  barSize?: string
  initialCapital?: number
  fillMode?: string
  slippageBps?: number
  commissionPerTrade?: number
}): Promise<AiSuggestAndRunResult> {
  return req<AiSuggestAndRunResult>(
    apiUrl('/api/ai/strategy/suggest-and-run').toString(),
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body)
    },
    90000
  )
}

/** 将回测参数部署到量化引擎（服务端强制 DryRun=true；有警告需 acknowledgeWarnings）。 */
export function deployDryRun(body: DeployPreflightBody & {
  startEngine?: boolean
  acknowledgeWarnings?: boolean
}): Promise<DeployDryRunResult> {
  return req<DeployDryRunResult>(
    apiUrl('/api/ai/strategy/deploy-dry-run').toString(),
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body)
    }
  )
}

// ---- 下单（与看板/回测独立，调 /api/orders）----

export interface PlaceOrderRequest {
  symbol: string
  side: OrderSide
  type: OrderType
  quantity: number
  limitPrice?: number | null
  estimatedPrice?: number | null
  strategy?: string | null
  note?: string | null
  secType?: 'STK' | 'OPT'
  expiry?: string | null
  strike?: number | null
  right?: 'C' | 'P' | null
  multiplier?: number | null
}

/** 列出全部订单（状态为实时合并，已持久化）。 */
export function listOrders(): Promise<OrderDto[]> {
  return getJson<OrderDto[]>(apiUrl('/api/orders').toString())
}

export interface OrderReconcileRow {
  symbol: string
  localFillNet: number
  dbPositionQty: number
  delta: number
  openOrderCount: number
  filledOrderCount: number
  mismatch: boolean
}

export interface OrderReconcileResult {
  checkedAt: string
  tolerance: number
  mismatchCount: number
  summary: string
  rows: OrderReconcileRow[]
}

/** 本地成交净额 vs DB 持仓对账（只读）。 */
export function reconcileOrders(): Promise<OrderReconcileResult> {
  return getJson<OrderReconcileResult>(apiUrl('/api/orders/reconcile').toString())
}

/** 下限价/市价单。风控拦截或 Gateway 未连会抛错。 */
export function placeOrder(body: PlaceOrderRequest): Promise<OrderDto> {
  return req<OrderDto>(apiUrl('/api/orders').toString(), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body)
  }, ORDER_TIMEOUT)
}

/** 撤单（仅对活动订单有效）。 */
export function cancelOrder(id: string): Promise<void> {
  return req<void>(
    apiUrl(`/api/orders/${encodeURIComponent(id)}`).toString(),
    { method: 'DELETE' },
    ORDER_TIMEOUT
  )
}

/** 读取风控配置。 */
export function getRiskSettings(): Promise<RiskSettings> {
  return getJson<RiskSettings>(apiUrl('/api/orders/risk-settings').toString())
}

/** 更新风控配置（关闭总开关 / 设模拟盘 / 调上限等）。 */
export function updateRiskSettings(s: RiskSettings): Promise<RiskSettings> {
  return req<RiskSettings>(apiUrl('/api/orders/risk-settings').toString(), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(s)
  })
}

// ---- 自动交易（调 /api/autotrade）----

/** 引擎总览：运行状态 / 全局设置 / 风控 / 各标的订阅+运行时 / 审计日志。 */
export function getAutoTradeStatus(): Promise<AutoTradeStatus> {
  return getJson<AutoTradeStatus>(apiUrl('/api/autotrade/status').toString())
}

/** 启动后台轮询循环。 */
export function startAutoTrade(): Promise<{ running: boolean }> {
  return req<{ running: boolean }>(apiUrl('/api/autotrade/start').toString(), { method: 'POST' })
}

/** 停止后台轮询循环。 */
export function stopAutoTrade(): Promise<{ running: boolean }> {
  return req<{ running: boolean }>(apiUrl('/api/autotrade/stop').toString(), { method: 'POST' })
}

/** 更新全局设置（Enabled / PollIntervalSeconds）。 */
export function updateAutoTradeSettings(s: AutoTradeSettings): Promise<AutoTradeSettings> {
  return req<AutoTradeSettings>(apiUrl('/api/autotrade/settings').toString(), {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(s)
  })
}

/** 新增或更新一个标的的订阅（按 symbol 幂等）。 */
export function addAutoTradeConfig(cfg: AutoTradeSymbolConfig): Promise<AutoTradeSymbolConfig> {
  return req<AutoTradeSymbolConfig>(apiUrl('/api/autotrade/config').toString(), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(cfg)
  })
}

/** 移除某个标的的订阅。 */
export function removeAutoTradeConfig(symbol: string): Promise<void> {
  return req<void>(
    apiUrl(`/api/autotrade/config/${encodeURIComponent(symbol)}`).toString(),
    { method: 'DELETE' }
  )
}

/** 立即对单个标的评估一次。 */
export function evaluateAutoTrade(symbol: string): Promise<{ evaluated: string }> {
  return req<{ evaluated: string }>(
    apiUrl(`/api/autotrade/evaluate/${encodeURIComponent(symbol)}`).toString(),
    { method: 'POST' },
    EVAL_TIMEOUT
  )
}

// ---- QQQ 0DTE 顺势短线（调 /api/trend-scalper，只 Paper）----

const TREND = '/api/trend-scalper'

export function getTrendScalper(): Promise<TrendScalperStatus> {
  return getJson<TrendScalperStatus>(apiUrl(TREND).toString())
}

export function updateTrendScalperSettings(s: TrendScalperSettings): Promise<TrendScalperStatus> {
  return req<TrendScalperStatus>(apiUrl(`${TREND}/settings`).toString(), {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(s)
  })
}

export function trendScalperAction(action: 'start' | 'stop' | 'tick' | 'flatten'): Promise<TrendScalperStatus> {
  return req<TrendScalperStatus>(
    apiUrl(`${TREND}/${action}`).toString(),
    { method: 'POST' },
    action === 'stop' ? 20000 : EVAL_TIMEOUT
  )
}

const DISCIPLINE = '/api/discipline'

export function getDiscipline(): Promise<DisciplineStatus> {
  return getJson<DisciplineStatus>(apiUrl(DISCIPLINE).toString())
}

export function updateDisciplineSettings(s: DisciplineSettings): Promise<DisciplineStatus> {
  return req<DisciplineStatus>(apiUrl(`${DISCIPLINE}/settings`).toString(), {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(s)
  })
}

/** 纪律预检（不下单）：顺势项要拉分钟线，放宽超时。 */
export function checkDiscipline(body: PlaceOrderRequest): Promise<DisciplineCheckResult> {
  return req<DisciplineCheckResult>(apiUrl(`${DISCIPLINE}/check`).toString(), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body)
  }, EVAL_TIMEOUT)
}

export function getWeeklyReview(accountId?: string, env?: TradeEnvFilter, weeks = 8): Promise<WeeklyReview> {
  const url = apiUrl('/api/portfolio/weekly-review')
  if (accountId) url.searchParams.set('accountId', accountId)
  if (env) url.searchParams.set('env', env)
  url.searchParams.set('weeks', String(weeks))
  return req<WeeklyReview>(url.toString(), undefined, EVAL_TIMEOUT)
}

export interface FinanceBriefItem {
  headline: string
  summary: string
  url: string
  tickers: string[]
}

export interface FinanceBriefSection {
  id: string
  title: string
  items: FinanceBriefItem[]
}

/** Grok Bot 从 X 整理后的简报。 */
export interface FinanceBrief {
  updatedAt: string
  source: 'scheduled' | 'manual' | string
  sections: FinanceBriefSection[]
}

export function isFinanceBrief(value: unknown): value is FinanceBrief {
  if (!value || typeof value !== 'object') return false
  const brief = value as Partial<FinanceBrief>
  return typeof brief.updatedAt === 'string' && Array.isArray(brief.sections)
}

/** 本次进程里 Bot 回传的简报。还没有回传时为 null，页面用本地快照。 */
export function getLiveFinanceBrief(): Promise<FinanceBrief | null> {
  return req<FinanceBrief | null>(apiUrl('/api/news/brief').toString())
}

/** 通知 Grok Bot 按 X 上的最新内容重做简报。若响应体就是简报 JSON，直接返回。 */
export function refreshFinanceBrief(): Promise<FinanceBrief | { status?: string; message?: string }> {
  return req(
    apiUrl('/api/news/refresh').toString(),
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: '{}'
    },
    180000
  )
}

export function getMarketNews(symbol = 'QQQ'): Promise<MarketNewsFeed> {
  const url = apiUrl('/api/market-news')
  url.searchParams.set('symbol', symbol)
  return req<MarketNewsFeed>(url.toString(), undefined, EVAL_TIMEOUT)
}
