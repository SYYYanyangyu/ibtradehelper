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
  /** 成交份额（正数） */
  quantity: number
  /** 成交单价 */
  price: number
  /** 佣金/手续费 */
  commission: number
  /** 成交时间（UTC） */
  tradeTimeUtc: string
  /** 成交额 = quantity * price */
  grossAmount: number
  /** 净发生额：买入 = -(gross + commission)，卖出 = gross - commission */
  netAmount: number
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
