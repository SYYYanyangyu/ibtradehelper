import type {
  PortfolioSummary,
  DailyPnlPoint,
  TradeRecord,
  PositionDailyPnl,
  PositionLite
} from '../types'

export interface MockAccountInfo {
  accountId: string
  environment: string
  alias?: string | null
  baseCurrency?: string
}

type MockBundle = {
  accounts: MockAccountInfo[]
  portfolio: PortfolioSummary
  dailyPnl: DailyPnlPoint[]
  trades: TradeRecord[]
  positionDailyPnl: PositionDailyPnl
}

/** 本地可选文件（已 gitignore）；公开仓库无此数据，glob 为空。 */
const modules = import.meta.glob('./schwab-portfolio.json', { eager: true }) as Record<
  string,
  { default: MockBundle }
>
const raw: MockBundle | null = Object.values(modules)[0]?.default ?? null

/**
 * 嘉信 Mock：只注入 Live，绝不碰 Paper。
 * 开：localStorage `ibtrade-mock=1` 或 VITE_USE_MOCK=1
 * 关：localStorage `ibtrade-mock=0` / 删掉该 key，或 VITE_USE_MOCK=0
 */
export function isMockEnabled(): boolean {
  const env = import.meta.env.VITE_USE_MOCK
  if (env === '0' || env === 'false') return false
  if (env === '1' || env === 'true') return true
  try {
    return localStorage.getItem('ibtrade-mock') === '1'
  } catch {
    return false
  }
}

/** 仅当显式请求 Live 时走 Mock。Paper / All 一律真实 API。 */
export function shouldUseLiveMock(env?: string | null): boolean {
  return isMockEnabled() && env === 'Live'
}

function requireMock(): MockBundle {
  if (!raw) {
    throw new Error(
      'Mock 已开启但未找到 frontend/src/mock/schwab-portfolio.json（该文件不入库）。请关闭 ibtrade-mock，或本地自行放置数据文件。'
    )
  }
  return raw
}

export function mockAccounts(): MockAccountInfo[] {
  return requireMock().accounts
}

export function mockPortfolio(): PortfolioSummary {
  return requireMock().portfolio
}

export function mockDailyPnl(days = 365): DailyPnlPoint[] {
  const all = requireMock().dailyPnl
  if (days >= all.length) return all
  return all.slice(-days)
}

export function mockTrades(): TradeRecord[] {
  return requireMock().trades
}

export function mockPositionDailyPnl(): PositionDailyPnl {
  return requireMock().positionDailyPnl
}

export function mockPositionsLite(): PositionLite[] {
  return requireMock().portfolio.positions.map((p) => ({
    id: p.id,
    accountId: p.accountId,
    symbol: p.symbol,
    assetType: p.assetType,
    currency: p.currency,
    quantity: p.quantity,
    avgCost: p.avgCost,
    marketPrice: p.marketPrice,
    marketValue: p.marketValue,
    unrealizedPnl: p.unrealizedPnl
  }))
}
