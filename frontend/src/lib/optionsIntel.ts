import { apiUrl } from './api'

const API_KEY: string =
  ((import.meta.env.VITE_IBTRADE_API_KEY as string | undefined) ?? '').trim()

export interface OptionSide {
  last: number | null
  change: number | null
  bid: number | null
  ask: number | null
  volume: number | null
  openInterest: number | null
}

export interface OptionRow {
  expiry: string
  strike: number
  call: OptionSide
  put: OptionSide
}

export interface UnderlyingQuote {
  symbol: string
  companyName?: string | null
  last?: number | null
  change?: number | null
  changePct?: number | null
  volume?: string | null
  marketStatus?: string | null
  lastTrade?: string | null
  exchange?: string | null
}

export interface ChainStats {
  rowCount: number
  totalRows: number
  atmStrike?: number | null
  callVolume: number
  putVolume: number
  callOpenInterest: number
  putOpenInterest: number
  putCallVolumeRatio?: number | null
  putCallOiRatio?: number | null
}

export interface EarningsRow {
  fiscalQuarter: string
  dateReported: string
  eps?: number | null
  consensus?: number | null
  surprisePct?: number | null
}

export interface NewsItem {
  title: string
  url?: string | null
  published?: string | null
  source: string
}

export interface FedEvent {
  kind: string
  title: string
  date?: string | null
  url?: string | null
  upcoming: boolean
}

export interface TreasuryRate {
  security: string
  ratePct?: number | null
  asOf?: string | null
}

export interface PredictionMarket {
  question: string
  probability?: number | null
  url?: string | null
  venue: string
  endDate?: string | null
  previousProbability?: number | null
  change?: number | null
  volume?: number | null
}

export interface OptionsPaperSettings {
  symbol: string
  quantity: number
  pollSeconds: number
  minProbMove: number
  sendToGateway: boolean
}

export interface OptionsPaperSignal {
  atUtc: string
  symbol: string
  bias: string
  reason: string
  marketQuestion?: string | null
  venue?: string | null
  probability?: number | null
  change?: number | null
  putCallOiRatio?: number | null
  spot?: number | null
}

export interface OptionsPaperFill {
  id: string
  atUtc: string
  symbol: string
  side: 'Buy' | 'Sell' | string
  quantity: number
  price: number
  venue: string
  brokerOrderId?: string | null
  reason: string
  error?: string | null
}

export interface OptionsPaperStatus {
  running: boolean
  settings: OptionsPaperSettings
  accountHint: string
  riskMode: string
  netQty: number
  avgPrice: number
  mark?: number | null
  unrealized: number
  realized: number
  totalPnl: number
  signalCount: number
  fillCount: number
  lastSignal?: OptionsPaperSignal | null
  lastError?: string | null
  lastTickUtc?: string | null
  signals: OptionsPaperSignal[]
  fills: OptionsPaperFill[]
  markets: PredictionMarket[]
}

export interface DataSourceStatus {
  id: string
  label: string
  status: string
  message?: string | null
  url?: string | null
}

export interface HotName {
  rank: number
  symbol: string
  name?: string | null
  last?: number | null
  changePct?: number | null
  kind: string
  bucket?: string
}

export interface HotUniverse {
  asOf: string
  rule: string
  sourceUrl: string
  names: HotName[]
  currentIsHot: boolean
  currentRank?: number | null
  source: DataSourceStatus
}

export interface OptionsContext {
  symbol: string
  companyName?: string | null
  asOfUtc: string
  underlying?: UnderlyingQuote | null
  expiries: string[]
  selectedExpiry?: string | null
  chain: OptionRow[]
  stats?: ChainStats | null
  earnings: EarningsRow[]
  news: NewsItem[]
  fed: FedEvent[]
  treasury: TreasuryRate[]
  predictionMarkets: PredictionMarket[]
  sources: DataSourceStatus[]
  hot?: HotUniverse | null
}

export interface OptionsAnalyzeResult {
  symbol: string
  source: string
  confidence: number
  bias: 'bullish' | 'bearish' | 'neutral' | string
  summary: string
  drivers: string[]
  risks: string[]
  suggestedFocus?: string | null
  context: OptionsContext
}

async function req<T>(url: string, init?: RequestInit, timeoutMs = 45000): Promise<T> {
  const ctrl = new AbortController()
  const timer = window.setTimeout(() => ctrl.abort(), timeoutMs)
  try {
    const res = await fetch(url, {
      ...init,
      signal: ctrl.signal,
      headers: {
        ...(API_KEY ? { 'X-Api-Key': API_KEY } : null),
        ...(init?.headers as Record<string, string> | undefined)
      }
    })
    const text = await res.text()
    const data = text ? JSON.parse(text) : null
    if (!res.ok) {
      const err = data && typeof data === 'object' ? (data as { error?: string }).error : null
      throw new Error(err || `请求失败 (${res.status})`)
    }
    return data as T
  } catch (e) {
    if (e instanceof DOMException && e.name === 'AbortError') {
      throw new Error('请求超时，请确认后端已启动')
    }
    throw e
  } finally {
    window.clearTimeout(timer)
  }
}

export function getHotOptions(symbol?: string): Promise<HotUniverse> {
  const url = apiUrl('/api/options/hot')
  if (symbol) url.searchParams.set('symbol', symbol)
  return req<HotUniverse>(url.toString(), undefined, 15000)
}

export function getOptionsContext(symbol: string, expiry?: string): Promise<OptionsContext> {
  const url = apiUrl('/api/options/context')
  url.searchParams.set('symbol', symbol)
  if (expiry) url.searchParams.set('expiry', expiry)
  return req<OptionsContext>(url.toString(), undefined, 40000)
}

export function analyzeOptions(body: {
  symbol: string
  expiry?: string
  prompt?: string
}): Promise<OptionsAnalyzeResult> {
  return req<OptionsAnalyzeResult>(
    apiUrl('/api/options/analyze').toString(),
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body)
    },
    90000
  )
}

export function getOptionsPaper(): Promise<OptionsPaperStatus> {
  return req<OptionsPaperStatus>(apiUrl('/api/options/paper').toString())
}

export function updateOptionsPaperSettings(settings: OptionsPaperSettings): Promise<OptionsPaperStatus> {
  return req<OptionsPaperStatus>(apiUrl('/api/options/paper/settings').toString(), {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(settings)
  })
}

export function startOptionsPaper(): Promise<OptionsPaperStatus> {
  return req<OptionsPaperStatus>(apiUrl('/api/options/paper/start').toString(), {
    method: 'POST'
  })
}

export function stopOptionsPaper(): Promise<OptionsPaperStatus> {
  return req<OptionsPaperStatus>(apiUrl('/api/options/paper/stop').toString(), {
    method: 'POST'
  })
}

export function tickOptionsPaper(): Promise<OptionsPaperStatus> {
  return req<OptionsPaperStatus>(
    apiUrl('/api/options/paper/tick').toString(),
    { method: 'POST' },
    45000
  )
}
