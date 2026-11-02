// 与后端 IBTrade.Application.Portfolio DTO 对齐
export interface PositionPnl {
  id: number
  accountId: string
  symbol: string
  assetType: string
  currency: string
  quantity: number
  avgCost: number
  marketPrice: number
  marketValue: number
  unrealizedPnl: number
  realizedPnl: number
  costBasis: number
  /** 数量/均价来源：Flex / Gateway */
  source?: string
  /** 市价来源：Flex / Gateway */
  priceSource?: string
}

export interface PortfolioSummary {
  accountId: string
  totalMarketValue: number
  totalUnrealizedPnl: number
  totalRealizedPnl: number
  totalCostBasis: number
  totalPnl: number
  positionCount: number
  asOfUtc: string
  positions: PositionPnl[]
}

// 精简持仓（用于派生账户列表），对应后端 PositionDto
export interface PositionLite {
  id: number
  accountId: string
  symbol: string
  assetType: string
  currency: string
  quantity: number
  avgCost: number
  marketPrice: number
  marketValue: number
  unrealizedPnl: number
}

// 每日盈亏曲线上的一个点，对应后端 DailyPnlPointDto
export interface DailyPnlPoint {
  date: string
  netLiquidation: number
  dailyPnl: number
  dailyPnlPct: number
  cumulativePnl: number
  /** 当日外部现金流（入金/出金/股息等，带符号） */
  externalCashFlow: number
  /** 纯交易盈亏 = dailyPnl − externalCashFlow */
  tradingPnl: number
  /** 区间累计纯交易盈亏 */
  cumulativeTradingPnl: number
}

export type PerformanceRange = '1M' | '3M' | '6M' | 'YTD' | '1Y' | 'ALL'

export interface PerformanceSeries {
  id: string
  label: string
  periodReturnPct: number | null
  status: string
  message?: string | null
}

export interface PerformancePoint {
  date: string
  portfolio: number | null
  hsi: number | null
  spx: number | null
  dji: number | null
  ixic: number | null
}

export interface PerformanceCompare {
  range: PerformanceRange | string
  asOfUtc: string
  portfolioLabel: string
  series: PerformanceSeries[]
  points: PerformancePoint[]
  fromDate?: string | null
  toDate?: string | null
  startNav?: number | null
  endNav?: number | null
  method?: string | null
}

// 某日、某只标的的盈亏贡献点，对应后端 PositionDailyPnlPointDto
export interface PositionDailyPnlPoint {
  symbol: string
  assetType: string
  marketValue: number
  prevMarketValue: number
  /** 当日贡献 = 当日市值 − 前一日市值 */
  dailyContribution: number
  unrealizedPnl: number
  /** 贡献占当日总贡献绝对值的比例（0..1） */
  share: number
}

// 某日个股盈亏归因结果，对应后端 PositionDailyPnlDto（数据与计算均在后端完成，前端只渲染）
export interface PositionDailyPnl {
  /** 归因日期（UTC 当日），对应后端 DateUtc */
  dateUtc: string
  /** 当日总贡献 = 各标的贡献之和（持仓市值当日变动合计） */
  totalContribution: number
  /** 按贡献降序排列的逐标的明细 */
  points: PositionDailyPnlPoint[]
}

// 一笔外部现金流记录，对应后端 CashTransaction
export interface CashTransaction {
  id: number
  accountId: string
  date: string
  type: 'Deposit' | 'Withdrawal' | 'Dividend' | 'Interest' | 'Fee' | 'Other'
  amount: number
  currency: string
  note?: string
}

// 一笔成交明细，对应后端 Trade 实体（来自 IBKR Flex 报表落库的 Trades 表）
export interface TradeRecord {
  id: number
  accountId: string
  symbol: string
  assetType: string
  /** 成交方向：'Buy' = 买入，'Sell' = 卖出 */
  side: 'Buy' | 'Sell'
  currency: string
  /** 成交数量（正股=股，期权=张） */
  quantity: number
  /** 成交单价（期权为每股权利金） */
  price: number
  /** 合约乘数，期权通常 100 */
  multiplier?: number
  /** 佣金/手续费 */
  commission: number
  /** 成交时间（UTC） */
  tradeTimeUtc: string
  /** 成交额 = quantity × price × multiplier */
  grossAmount: number
  /** 净现金：买入为负，卖出为正 */
  netAmount: number
}

export interface ClosedTrade {
  accountId: string
  symbol: string
  assetType: string
  direction: 'Long' | 'Short' | string
  quantity: number
  openPrice: number
  closePrice: number
  multiplier: number
  openedAtUtc: string
  closedAtUtc: string
  openTradeId: number
  closeTradeId: number
  grossPnl: number
  commission: number
  netPnl: number
  currency: string
}

export interface OpenLot {
  accountId: string
  symbol: string
  assetType: string
  direction: 'Long' | 'Short' | string
  quantity: number
  openPrice: number
  multiplier: number
  openedAtUtc: string
  tradeId: number
  remainingCommission: number
  currency: string
}

export interface TradePnl {
  scope: string
  asOfUtc: string
  totalNetPnl: number
  grossProfit: number
  grossLoss: number
  winCount: number
  lossCount: number
  flatCount: number
  closedCount: number
  openLotCount: number
  method: string
  closed: ClosedTrade[]
  open: OpenLot[]
  byFill: { tradeId: number; realizedPnl: number }[]
}

// ---- 下单（对应后端 OrdersController / OrderDto / RiskSettings）----

export type OrderSide = 'Buy' | 'Sell'
export type OrderType = 'Market' | 'Limit'
export type OrderStatus =
  | 'PendingSubmit'
  | 'PreSubmitted'
  | 'Submitted'
  | 'PartiallyFilled'
  | 'Filled'
  | 'Cancelled'
  | 'Rejected'
  | 'Failed'
  | 'ApiPending'
  | 'Inactive'
export type TradingMode = 'Paper' | 'Live'

// 订单对外视图（含从下单客户端实时合并的成交状态）
export interface OrderDto {
  id: string
  brokerOrderId: number | null
  symbol: string
  side: OrderSide
  type: OrderType
  quantity: number
  limitPrice: number | null
  status: OrderStatus
  filledQuantity: number
  avgFillPrice: number
  strategy: string | null
  note: string | null
  createdAt: string
  closedAt: string | null
  lastError: string | null
}

// 风控配置（可在运行时通过 API 调整）
export interface RiskSettings {
  enabled: boolean
  mode: TradingMode
  liveAcknowledged: boolean
  maxOpenPositions: number
  maxOrderAmount: number
  dailyLossLimit: number
}

// ---- 自动交易（对应后端 AutoTradeController）----

export type AutoTradeMode = 'Paper' | 'Live'
export type AutoTradeEventType =
  | 'Info'
  | 'Signal'
  | 'OrderPlaced'
  | 'OrderRejected'
  | 'RiskBlocked'
  | 'DryRun'
  | 'Error'

// 引擎全局设置
export interface AutoTradeSettings {
  enabled: boolean
  pollIntervalSeconds: number
  /** 仅用已收盘 K 线决策（默认 true） */
  useCompletedBarsOnly?: boolean
}

// 引擎风控（与手动下单共享 RiskSettings，但这里仅读）
export interface AutoTradeRisk {
  enabled: boolean
  mode: AutoTradeMode
  maxOpenPositions: number
  maxOrderAmount: number
  dailyLossLimit: number
}

// 单个标的的订阅配置
export interface AutoTradeSymbolConfig {
  symbol: string
  strategyKey: string
  parameters: Record<string, number>
  barSize: string
  duration: string
  quantity: number
  orderType: OrderType
  limitPrice: number | null
  enabled: boolean
  dryRun: boolean
}

// 单个标的的运行时状态（信号 / 持仓 / 挂单 / 最近动作）
export interface AutoTradeSymbolRuntime {
  symbol: string
  strategyKey?: string | null
  lastSignal?: string | null
  /** 产生信号的已收盘 K 线时间 */
  signalBarTime?: string | null
  /** 是否因未收盘而改用倒数第二根 */
  usedPenultimateBar?: boolean
  netQty?: number
  openOrders?: number
  lastEvaluatedAt?: string | null
  lastAction?: string | null
  lastError?: string | null
}

// 订阅 = 配置 + 运行时
export interface AutoTradeSymbol {
  config: AutoTradeSymbolConfig
  runtime: AutoTradeSymbolRuntime
}

// 审计日志事件
export interface AutoTradeEvent {
  time: string
  symbol: string
  type: AutoTradeEventType
  message: string
  strategyKey?: string | null
  signal?: string | null
}

// 引擎总览
export interface AutoTradeStatus {
  running: boolean
  settings: AutoTradeSettings
  risk: AutoTradeRisk
  symbols: AutoTradeSymbol[]
  events: AutoTradeEvent[]
}

// ---- QQQ 0DTE 顺势短线（Paper）----

export interface TrendScalperSettings {
  requireRetest: boolean
  retestLookback: number
  retestTolerancePct: number
  useAtrExits: boolean
  atrPeriod: number
  atrStopMultiple: number
  atrTargetMultiple: number
  estimatedCommissionPerContractSide: number
  underlying: string
  budgetPerTrade: number
  dailyLossLimit: number
  useLimitOrders: boolean
  limitBufferPct: number
  maxContracts: number
  takeProfitPct: number
  stopLossPct: number
  maxHoldMinutes: number
  vwapMinPct: number
  rangePosMin: number
  entryStart: string
  entryEnd: string
  flatBy: string
  maxTradesPerDay: number
  stopAfterLosses: number
  allowCalls: boolean
  allowPuts: boolean
  pollSeconds: number
  maxPrevVix: number
  skipFomcDays: boolean
}

export interface TrendScalperPosition {
  occ: string
  expiry: string
  strike: number
  right: 'C' | 'P'
  quantity: number
  entryPrice: number
  entryUnderlying: number
  openedAtUtc: string
  entryReason: string
  pendingExitOrderId?: string | null
}

export interface TrendScalperTrade {
  id: string
  dateEt: string
  occ: string
  right: 'C' | 'P'
  strike: number
  quantity: number
  entryAtUtc: string
  entryPrice: number
  entryUnderlying: number
  exitAtUtc: string
  exitPrice: number
  exitUnderlying: number
  exitReason: 'tp' | 'sl' | 'time' | 'eod' | 'manual' | 'data'
  entryReason: string
  pnl: number
  entryMid?: number | null
  exitMid?: number | null
  slippage?: number | null
}

export interface TrendScalperSnapshot {
  atUtc: string
  barEt?: string | null
  price?: number | null
  vwap?: number | null
  orbHigh?: number | null
  orbLow?: number | null
  dayHigh?: number | null
  dayLow?: number | null
  decision: 'buy-call' | 'buy-put' | 'hold' | 'exit'
  reason: string
}

export interface TrendScalperStatus {
  running: boolean
  settings: TrendScalperSettings
  riskMode: string
  session: string
  position: TrendScalperPosition | null
  lastUnderlying: number | null
  context: {
    prevVix: number | null
    prevVixDate: string | null
    isFomcDay: boolean | null
    nextFomc: string | null
    error: string | null
    liveVix?: number | null
    liveVixDate?: string | null
    liveVixChangePct?: number | null
    liveVixError?: string | null
  }
  today: { dateEt: string; trades: number; wins: number; losses: number; consecutiveLosses: number; realized: number }
  total: {
    costedTrades: number
    estimatedNetExpectancy: number | null
    trades: number
    wins: number
    winRate: number
    pnl: number
    profitFactor: number | null
    avgWin: number
    avgLoss: number
    slippageTrades: number
    totalSlippage: number
    avgSlippage: number | null
  }
  last: TrendScalperSnapshot | null
  lastError: string | null
  lastTickUtc: string | null
  trades: TrendScalperTrade[]
  log: TrendScalperSnapshot[]
}

export interface DisciplineSettings {
  enabled: boolean
  mode: 'Block' | 'Warn'
  symbols: string[]
  entryStart: string
  entryEnd: string
  maxPremium: number
  requireTrend: boolean
  vwapMinPct: number
  maxPrevVix: number
  skipFomcDays: boolean
  stopAfterLosses: number
  dailyLossLimit: number
  weeklyLossLimit: number
}

export interface DisciplineCheckItem {
  key: string
  label: string
  ok: boolean
  blocking: boolean
  detail: string
}

export interface DisciplineCheckResult {
  applies: boolean
  allowed: boolean
  mode: 'Block' | 'Warn'
  summary: string
  items: DisciplineCheckItem[]
}

export interface DisciplineStatus {
  settings: DisciplineSettings
  todayEt: string
  todayRealized: number
  todayTrades: number
  consecutiveLosses: number
  weekRealized: number
  context: TrendScalperStatus['context']
  scope: string
  today: { contract: string; quantity: number; entryPrice: number; exitPrice: number; closedAtUtc: string; pnl: number }[]
}

export interface ReviewViolation {
  key: string
  label: string
  count: number
  wins: number
  net: number
}

export interface ReviewTrade {
  openTradeId: number
  symbol: string
  openedAtUtc: string
  closedAtUtc: string
  premium: number
  net: number
  violations: string[]
}

export interface ReviewWeek {
  weekStart: string
  weekEnd: string
  trades: number
  wins: number
  winRate: number
  net: number
  avgWin: number
  avgLoss: number
  profitFactor: number | null
  expectancy: number
  maxDrawdown: number
  compliantTrades: number
  compliantNet: number
  compliantWinRate: number
  violatingTrades: number
  violatingNet: number
  accountTradingPnl: number | null
  accountReturnPct: number | null
  benchmarkReturnPct: number | null
  violations: ReviewViolation[]
  worstViolations: ReviewTrade[]
}

export interface WeeklyReview {
  asOfUtc: string
  benchmark: string
  rulesSummary: string
  dataNotes: string[]
  periodViolations: ReviewViolation[]
  weeks: ReviewWeek[]
}

export interface MarketNewsItem {
  timeUtc: string | null
  rawTime: string
  provider: string
  articleId: string
  headline: string
  sentiment: number | null
  confidence: number | null
  tags: string[]
}

export interface MarketNewsFeed {
  symbol: string
  providers: string[]
  refreshedUtc: string | null
  error: string | null
  summary: {
    windowMinutes: number
    count: number
    scored: number
    avgSentiment: number | null
    fed: number
    geo: number
    macro: number
  }
  items: MarketNewsItem[]
  tape: MarketNewsItem[]
  tapeStatus: {
    connected: boolean
    sinceUtc: string | null
    lastHeadlineUtc: string | null
    received: number
    error: string | null
    providerErrors: string[]
  }
}
