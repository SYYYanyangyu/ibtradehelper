import type { ClosedTrade } from '../types'
import { fmtMoney } from './utils'

export type HoldBucket = 'scalp' | 'intraday' | 'session' | 'swing' | 'position'

export const HOLD_BUCKET_LABEL: Record<HoldBucket, string> = {
  scalp: '超短 <15分',
  intraday: '日内 <4时',
  session: '当日 <24时',
  swing: '波段 <7天',
  position: '中长线'
}

export const WEEKDAY_LABEL = ['一', '二', '三', '四', '五', '六', '日'] as const

export interface RootStat {
  root: string
  n: number
  pnl: number
}

export interface TradeProfileModel {
  n: number
  tags: string[]
  headline: string
  paragraphs: string[]
  warnings: string[]
  currency: string
  hold: {
    medianMs: number
    meanMs: number
    sameDayPct: number
    buckets: Record<HoldBucket, number>
  }
  edge: {
    wins: number
    losses: number
    wr: number
    pf: number
    expectancy: number
    avgWin: number
    avgLoss: number
    payoff: number
    commission: number
    commDrag: number
    maxWin: number
    maxLoss: number
    maxLoseStreak: number
    activeDays: number
    perDay: number
  }
  mix: {
    option: number
    stock: number
    long: number
    short: number
    call: number
    put: number
  }
  session: {
    pre: number
    rth: number
    after: number
    hours: { hour: number; n: number }[]
    weekdays: { day: number; n: number }[]
  }
  roots: RootStat[]
  envSplit: { paperN: number; paperPnl: number; liveN: number; livePnl: number }
}

const HOLD_ORDER: HoldBucket[] = ['scalp', 'intraday', 'session', 'swing', 'position']

export function isPaperAccount(accountId: string, paperIds: Set<string>): boolean {
  return paperIds.has(accountId) || accountId === 'DUN716484'
}

export function parseOcc(symbol: string): { root: string; cp: 'C' | 'P' } | null {
  const i = symbol.lastIndexOf(' ')
  if (i < 0) return null
  const right = symbol.slice(i + 1)
  if (right.length === 15 && (right[6] === 'C' || right[6] === 'P')) {
    return { root: symbol.slice(0, i).trim(), cp: right[6] as 'C' | 'P' }
  }
  return null
}

export function underlyingRoot(symbol: string): string {
  return parseOcc(symbol)?.root ?? symbol.trim()
}

export function holdDurationMs(c: ClosedTrade): number {
  const a = new Date(c.openedAtUtc).getTime()
  const b = new Date(c.closedAtUtc).getTime()
  if (!Number.isFinite(a) || !Number.isFinite(b) || b < a) return 0
  return b - a
}

export function holdBucket(ms: number): HoldBucket {
  const h = ms / 3_600_000
  if (h < 0.25) return 'scalp'
  if (h < 4) return 'intraday'
  if (h < 24) return 'session'
  if (h < 24 * 7) return 'swing'
  return 'position'
}

export function fmtHold(ms: number): string {
  if (!Number.isFinite(ms) || ms <= 0) return '—'
  const m = ms / 60_000
  if (m < 60) return `${Math.max(1, Math.round(m))} 分钟`
  const h = m / 60
  if (h < 48) return `${h.toFixed(h < 10 ? 1 : 0)} 小时`
  return `${(h / 24).toFixed(1)} 天`
}

function median(xs: number[]): number {
  if (xs.length === 0) return 0
  const s = [...xs].sort((a, b) => a - b)
  const mid = Math.floor(s.length / 2)
  return s.length % 2 ? s[mid]! : (s[mid - 1]! + s[mid]!) / 2
}

function nyParts(iso: string): { hour: number; minute: number; weekday: number } {
  const d = new Date(iso)
  const fmt = new Intl.DateTimeFormat('en-US', {
    timeZone: 'America/New_York',
    weekday: 'short',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23'
  })
  const parts = fmt.formatToParts(d)
  const map = Object.fromEntries(parts.map((p) => [p.type, p.value]))
  const wd = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'].indexOf(map.weekday ?? '')
  return {
    hour: Number(map.hour ?? 0) % 24,
    minute: Number(map.minute ?? 0),
    weekday: wd < 0 ? 0 : wd
  }
}

function nySession(iso: string): 'pre' | 'rth' | 'after' {
  const { hour, minute } = nyParts(iso)
  const t = hour * 60 + minute
  if (t < 9 * 60 + 30) return 'pre'
  if (t < 16 * 60) return 'rth'
  return 'after'
}

function share(part: number, total: number): number {
  return total > 0 ? part / total : 0
}

function pct(part: number, total: number): string {
  return `${(share(part, total) * 100).toFixed(0)}%`
}

export function buildTradeProfile(closed: ClosedTrade[], paperIds: Set<string>): TradeProfileModel | null {
  if (closed.length === 0) return null

  const currency = closed[0]?.currency || 'USD'
  const buckets: Record<HoldBucket, number> = {
    scalp: 0,
    intraday: 0,
    session: 0,
    swing: 0,
    position: 0
  }
  const holds: number[] = []
  let sameDay = 0
  let option = 0
  let stock = 0
  let longN = 0
  let shortN = 0
  let call = 0
  let put = 0
  let commission = 0
  let paperN = 0
  let paperPnl = 0
  let liveN = 0
  let livePnl = 0
  let pre = 0
  let rth = 0
  let after = 0
  const hourMap = new Map<number, number>()
  const dayMap = new Map<number, number>()
  const rootMap = new Map<string, RootStat>()
  const dayKeys = new Set<string>()
  const chrono = [...closed].sort((a, b) => a.closedAtUtc.localeCompare(b.closedAtUtc))
  let loseStreak = 0
  let maxLoseStreak = 0

  for (const c of closed) {
    const ms = holdDurationMs(c)
    holds.push(ms)
    buckets[holdBucket(ms)] += 1
    const od = c.openedAtUtc.slice(0, 10)
    const cd = c.closedAtUtc.slice(0, 10)
    if (od && od === cd) sameDay += 1
    dayKeys.add(cd || od)

    const occ = parseOcc(c.symbol)
    const isOpt = c.assetType === 'Option' || !!occ
    if (isOpt) option += 1
    else stock += 1
    if (c.direction === 'Short') shortN += 1
    else longN += 1
    if (occ?.cp === 'C') call += 1
    else if (occ?.cp === 'P') put += 1

    commission += c.commission
    const paper = isPaperAccount(c.accountId, paperIds)
    if (paper) {
      paperN += 1
      paperPnl += c.netPnl
    } else {
      liveN += 1
      livePnl += c.netPnl
    }

    const sess = nySession(c.openedAtUtc)
    if (sess === 'pre') pre += 1
    else if (sess === 'rth') rth += 1
    else after += 1
    const ny = nyParts(c.openedAtUtc)
    hourMap.set(ny.hour, (hourMap.get(ny.hour) ?? 0) + 1)
    const wd = ny.weekday === 0 ? 6 : ny.weekday - 1
    dayMap.set(wd, (dayMap.get(wd) ?? 0) + 1)

    const root = underlyingRoot(c.symbol)
    const cur = rootMap.get(root) ?? { root, n: 0, pnl: 0 }
    cur.n += 1
    cur.pnl += c.netPnl
    rootMap.set(root, cur)
  }

  for (const c of chrono) {
    if (c.netPnl < 0) {
      loseStreak += 1
      if (loseStreak > maxLoseStreak) maxLoseStreak = loseStreak
    } else {
      loseStreak = 0
    }
  }

  const wins = closed.filter((c) => c.netPnl > 0)
  const losses = closed.filter((c) => c.netPnl < 0)
  const profit = wins.reduce((s, c) => s + c.netPnl, 0)
  const lossAbs = Math.abs(losses.reduce((s, c) => s + c.netPnl, 0))
  const avgWin = wins.length ? profit / wins.length : 0
  const avgLoss = losses.length ? losses.reduce((s, c) => s + c.netPnl, 0) / losses.length : 0
  const pf = lossAbs > 0 ? profit / lossAbs : profit > 0 ? Number.POSITIVE_INFINITY : 0
  const n = closed.length
  const activeDays = dayKeys.size || 1

  const model: TradeProfileModel = {
    n,
    tags: [],
    headline: '',
    paragraphs: [],
    warnings: [],
    currency,
    hold: {
      medianMs: median(holds),
      meanMs: holds.reduce((s, v) => s + v, 0) / n,
      sameDayPct: share(sameDay, n),
      buckets
    },
    edge: {
      wins: wins.length,
      losses: losses.length,
      wr: share(wins.length, n),
      pf,
      expectancy: closed.reduce((s, c) => s + c.netPnl, 0) / n,
      avgWin,
      avgLoss,
      payoff: avgLoss < 0 ? avgWin / Math.abs(avgLoss) : avgWin > 0 ? Number.POSITIVE_INFINITY : 0,
      commission,
      commDrag: profit > 0 ? commission / profit : 0,
      maxWin: wins.reduce((m, c) => Math.max(m, c.netPnl), 0),
      maxLoss: losses.reduce((m, c) => Math.min(m, c.netPnl), 0),
      maxLoseStreak,
      activeDays,
      perDay: n / activeDays
    },
    mix: { option, stock, long: longN, short: shortN, call, put },
    session: {
      pre,
      rth,
      after,
      hours: [...hourMap.entries()]
        .map(([hour, count]) => ({ hour, n: count }))
        .sort((a, b) => a.hour - b.hour),
      weekdays: Array.from({ length: 7 }, (_, day) => ({ day, n: dayMap.get(day) ?? 0 }))
    },
    roots: [...rootMap.values()].sort((a, b) => b.n - a.n).slice(0, 8),
    envSplit: { paperN, paperPnl, liveN, livePnl }
  }

  decorateCopy(model)
  return model
}

function decorateCopy(p: TradeProfileModel) {
  const { n, mix, hold, edge, roots, envSplit, currency } = p
  const tags: string[] = []
  const shortHold = hold.buckets.scalp + hold.buckets.intraday
  const dominantHold = HOLD_ORDER.reduce((a, b) => (hold.buckets[b] > hold.buckets[a] ? b : a))

  if (share(shortHold, n) >= 0.7 || hold.sameDayPct >= 0.75) tags.push('日内短线')
  else if (dominantHold === 'swing') tags.push('波段持仓')
  else if (dominantHold === 'position') tags.push('中长线')
  else tags.push('短线偏波段')

  if (share(mix.option, n) >= 0.8) tags.push('期权为主')
  else if (share(mix.stock, n) >= 0.7) tags.push('正股为主')
  else tags.push('股期混合')

  if (share(mix.long, n) >= 0.85 && share(mix.option, n) >= 0.6) tags.push('权利金买方')
  else if (share(mix.short, n) >= 0.6) tags.push('偏空 / 卖方')
  else if (share(mix.long, n) >= 0.85) tags.push('做多为主')

  const cp = mix.call + mix.put
  if (cp >= 8) {
    if (share(mix.call, cp) >= 0.62) tags.push('偏 Call')
    else if (share(mix.put, cp) >= 0.62) tags.push('偏 Put')
    else tags.push('Call / Put 均衡')
  }

  if (edge.wr >= 0.55 && edge.payoff > 0 && edge.payoff < 0.9) tags.push('高胜率、亏单更大')
  else if (edge.wr < 0.4 && edge.payoff >= 1.4) tags.push('低胜率博大盈')
  else if (edge.pf >= 1.15) tags.push('正期望')
  else if (edge.pf > 0 && edge.pf < 0.9) tags.push('负期望')

  const top = roots[0]
  if (top && share(top.n, n) >= 0.45) tags.push(`${top.root} 集中`)

  if (envSplit.paperN > 0 && envSplit.liveN > 0) tags.push('Paper + Live 混合')
  else if (envSplit.paperN === n) tags.push('Paper 模拟')

  p.tags = tags.slice(0, 6)
  p.headline = tags.slice(0, 3).join(' · ') || '交易画像'

  const holdTxt = HOLD_BUCKET_LABEL[dominantHold]
  const sessMain =
    hold.sameDayPct >= 0.75
      ? `当日平仓 ${pct(Math.round(hold.sameDayPct * n), n)}`
      : `${holdTxt} 最多（${hold.buckets[dominantHold]} 例）`

  const dirTxt =
    mix.short === 0
      ? '几乎只做多'
      : `多 ${mix.long} / 空 ${mix.short}`
  const cpTxt =
    cp > 0 ? `Call ${mix.call}、Put ${mix.put}（${pct(mix.call, cp)} Call）` : '正股或无法识别期权代码'
  const topTxt = top
    ? `${top.root} ${top.n} 例、${top.pnl >= 0 ? '+' : ''}${fmtMoney(top.pnl, currency)}`
    : '—'

  const paragraphs = [
    `已平 ${n} 例，跨 ${edge.activeDays} 个交易日，日均 ${edge.perDay.toFixed(1)} 例。${dirTxt}；期权 ${mix.option} / 正股 ${mix.stock}。${cpTxt}。中位持仓 ${fmtHold(hold.medianMs)}（均值 ${fmtHold(hold.meanMs)}），${sessMain}。`,
    `胜率 ${(edge.wr * 100).toFixed(0)}%（${edge.wins} 赚 / ${edge.losses} 亏），平均盈利 ${fmtMoney(edge.avgWin, currency)}，平均亏损 ${fmtMoney(edge.avgLoss, currency)}，盈亏比 ${Number.isFinite(edge.payoff) ? edge.payoff.toFixed(2) : '—'}，利润因子 ${Number.isFinite(edge.pf) ? edge.pf.toFixed(2) : '—'}，单例期望 ${edge.expectancy >= 0 ? '+' : ''}${fmtMoney(edge.expectancy, currency)}。佣金 ${fmtMoney(edge.commission, currency)}${edge.commDrag >= 0.005 ? `，约占盈利合计 ${(edge.commDrag * 100).toFixed(edge.commDrag < 0.01 ? 1 : 0)}%` : ''}。最大单笔 ${fmtMoney(edge.maxWin, currency)} / ${fmtMoney(edge.maxLoss, currency)}，最长连亏 ${edge.maxLoseStreak} 例。`,
    `标的集中在 ${topTxt}。开仓时段按美东：盘前 ${preLabel(p)}、常规 ${rthLabel(p)}、盘后 ${afterLabel(p)}。`
  ]
  p.paragraphs = paragraphs

  const warnings: string[] = []
  if (envSplit.paperN > 0 && envSplit.liveN > 0) {
    warnings.push(
      `当前混了 Paper ${envSplit.paperN} 例（${fmtMoney(envSplit.paperPnl, currency)}）和 Live ${envSplit.liveN} 例（${fmtMoney(envSplit.livePnl, currency)}）。Paper 仓位更大时会盖住真人风格，建议切到 snow / Live 看个人画像。`
    )
  } else if (envSplit.paperN === n) {
    warnings.push('这是 Paper 模拟盘：风格和盈亏不能直接当成实盘画像。')
  }
  if (edge.pf > 0 && edge.pf < 1) {
    warnings.push('利润因子低于 1：已实现期望为负，亏的总金额大于赚的总金额。')
  }
  if (edge.wr >= 0.55 && edge.payoff > 0 && edge.payoff < 0.85) {
    warnings.push('胜率不低，但平均亏单大于平均盈单，一串小赚可能被少数大亏吃掉。')
  }
  if (edge.commDrag >= 0.25) {
    warnings.push('佣金已经吃掉相当一部分盈利，超短线频率偏高时成本会更明显。')
  }
  p.warnings = warnings
}

function preLabel(p: TradeProfileModel): string {
  return `${p.session.pre}（${pct(p.session.pre, p.n)}）`
}
function rthLabel(p: TradeProfileModel): string {
  return `${p.session.rth}（${pct(p.session.rth, p.n)}）`
}
function afterLabel(p: TradeProfileModel): string {
  return `${p.session.after}（${pct(p.session.after, p.n)}）`
}
