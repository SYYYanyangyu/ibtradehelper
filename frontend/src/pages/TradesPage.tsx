import { useEffect, useMemo, useState } from 'react'
import { ArrowDownRight, ArrowUpRight, ChevronLeft, ChevronRight, Loader2, Receipt, Search, X } from 'lucide-react'
import { Card, CardContent } from '../components/ui/Card'
import { TradeProfile } from '../components/TradeProfile'
import { WeeklyReview } from '../components/WeeklyReview'
import { getTradePnl, getTrades, type AccountInfo, type TradeEnvFilter } from '../lib/api'
import { buildTradeProfile } from '../lib/tradeProfile'
import type { ClosedTrade, OpenLot, TradePnl, TradeRecord } from '../types'
import { cn, fmtMoney, fmtNum, fmtTime, gainClass, thCls, trCls } from '../lib/utils'

type SideFilter = 'all' | 'Buy' | 'Sell'
type AssetFilter = 'all' | 'Option' | 'Stock'
type Layout = 'pnl' | 'day' | 'table'
type PnlFilter = 'all' | 'win' | 'lose'

const PAGE_SIZE = 20

interface Props {
  env?: TradeEnvFilter
  accountId?: string
  accounts: AccountInfo[]
}

const WEEK = ['日', '一', '二', '三', '四', '五', '六']

function dayKey(iso: string): string {
  return iso.slice(0, 10)
}

function weekday(iso: string): string {
  const [y, m, d] = iso.split('-').map(Number)
  return WEEK[new Date(Date.UTC(y, m - 1, d)).getUTCDay()] ?? ''
}

function isOption(t: TradeRecord): boolean {
  return t.assetType === 'Option' || /\s\d{6}[CP]\d{8}$/.test(t.symbol)
}

function accountLabel(id: string, accounts: AccountInfo[]): string {
  const a = accounts.find((x) => x.accountId === id)
  if (a?.alias) return a.alias
  return id === 'DUN716484' ? 'Paper' : id
}

function envOf(id: string, accounts: AccountInfo[]): 'Paper' | 'Live' {
  const a = accounts.find((x) => x.accountId === id)
  if (a?.environment === 'Paper') return 'Paper'
  if (id === 'DUN716484') return 'Paper'
  return 'Live'
}

export function TradesPage({ env, accountId, accounts }: Props) {
  const [rows, setRows] = useState<TradeRecord[]>([])
  const [pnl, setPnl] = useState<TradePnl | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [q, setQ] = useState('')
  const [side, setSide] = useState<SideFilter>('all')
  const [asset, setAsset] = useState<AssetFilter>('all')
  const [symbol, setSymbol] = useState<string | null>(null)
  const [layout, setLayout] = useState<Layout>('pnl')
  const [pnlFilter, setPnlFilter] = useState<PnlFilter>('all')
  const [page, setPage] = useState(1)

  useEffect(() => {
    let alive = true
    setLoading(true)
    setError(null)
    Promise.all([getTrades(undefined, 3650), getTradePnl()])
      .then(([list, book]) => {
        if (!alive) return
        setRows(list)
        setPnl(book)
      })
      .catch((e) => {
        if (!alive) return
        setError(e instanceof Error ? e.message : 'Trades 加载失败')
        setRows([])
        setPnl(null)
      })
      .finally(() => {
        if (alive) setLoading(false)
      })
    return () => {
      alive = false
    }
  }, [])

  const scoped = useMemo(() => {
    return rows.filter((t) => {
      if (accountId && t.accountId !== accountId) return false
      if (env && envOf(t.accountId, accounts) !== env) return false
      return true
    })
  }, [rows, accountId, env, accounts])

  const filtered = useMemo(() => {
    const needle = q.trim().toUpperCase()
    return scoped.filter((t) => {
      if (side !== 'all' && t.side !== side) return false
      if (asset === 'Option' && !isOption(t)) return false
      if (asset === 'Stock' && isOption(t)) return false
      if (symbol && t.symbol !== symbol) return false
      if (needle && !t.symbol.toUpperCase().includes(needle) && !t.accountId.toUpperCase().includes(needle))
        return false
      return true
    })
  }, [scoped, q, side, asset, symbol])

  const stats = useMemo(() => {
    let buyN = 0
    let sellN = 0
    let buyQty = 0
    let sellQty = 0
    let buyNotional = 0
    let sellNotional = 0
    let net = 0
    for (const t of filtered) {
      const g = t.grossAmount
      net += t.netAmount
      if (t.side === 'Buy') {
        buyN += 1
        buyQty += t.quantity
        buyNotional += g
      } else {
        sellN += 1
        sellQty += t.quantity
        sellNotional += g
      }
    }
    const dates = filtered.map((t) => dayKey(t.tradeTimeUtc)).sort()
    return {
      buyN,
      sellN,
      buyQty,
      sellQty,
      buyNotional,
      sellNotional,
      net,
      from: dates[0],
      to: dates[dates.length - 1]
    }
  }, [filtered])

  const bySymbol = useMemo(() => {
    const map = new Map<string, { n: number; notional: number }>()
    for (const t of filtered) {
      const cur = map.get(t.symbol) ?? { n: 0, notional: 0 }
      cur.n += 1
      cur.notional += t.grossAmount
      map.set(t.symbol, cur)
    }
    return [...map.entries()]
      .map(([s, v]) => ({ symbol: s, ...v }))
      .sort((a, b) => b.notional - a.notional)
      .slice(0, 16)
  }, [filtered])

  const days = useMemo(() => {
    const map = new Map<string, TradeRecord[]>()
    const sorted = [...filtered].sort((a, b) => b.tradeTimeUtc.localeCompare(a.tradeTimeUtc))
    for (const t of sorted) {
      const k = dayKey(t.tradeTimeUtc)
      const list = map.get(k)
      if (list) list.push(t)
      else map.set(k, [t])
    }
    return [...map.entries()].map(([date, fills]) => {
      const buy = fills.filter((t) => t.side === 'Buy').reduce((s, t) => s + t.grossAmount, 0)
      const sell = fills.filter((t) => t.side === 'Sell').reduce((s, t) => s + t.grossAmount, 0)
      const net = fills.reduce((s, t) => s + t.netAmount, 0)
      return { date, fills, buy, sell, net }
    })
  }, [filtered])

  const fillPnl = useMemo(() => {
    const m = new Map<number, number>()
    for (const f of pnl?.byFill ?? []) m.set(f.tradeId, f.realizedPnl)
    return m
  }, [pnl])

  const closed = useMemo(() => {
    const needle = q.trim().toUpperCase()
    return (pnl?.closed ?? []).filter((c) => {
      if (accountId && c.accountId !== accountId) return false
      if (env && envOf(c.accountId, accounts) !== env) return false
      if (symbol && c.symbol !== symbol) return false
      if (asset === 'Option' && c.assetType !== 'Option') return false
      if (asset === 'Stock' && c.assetType === 'Option') return false
      if (pnlFilter === 'win' && c.netPnl <= 0) return false
      if (pnlFilter === 'lose' && c.netPnl >= 0) return false
      if (needle && !c.symbol.toUpperCase().includes(needle) && !c.accountId.toUpperCase().includes(needle))
        return false
      return true
    })
  }, [pnl, accountId, env, accounts, symbol, asset, pnlFilter, q])

  const openLots = useMemo(() => {
    const needle = q.trim().toUpperCase()
    return (pnl?.open ?? []).filter((o) => {
      if (accountId && o.accountId !== accountId) return false
      if (env && envOf(o.accountId, accounts) !== env) return false
      if (symbol && o.symbol !== symbol) return false
      if (asset === 'Option' && o.assetType !== 'Option') return false
      if (asset === 'Stock' && o.assetType === 'Option') return false
      if (needle && !o.symbol.toUpperCase().includes(needle) && !o.accountId.toUpperCase().includes(needle))
        return false
      return true
    })
  }, [pnl, accountId, env, accounts, symbol, asset, q])

  const closedForProfile = useMemo(() => {
    const needle = q.trim().toUpperCase()
    return (pnl?.closed ?? []).filter((c) => {
      if (accountId && c.accountId !== accountId) return false
      if (env && envOf(c.accountId, accounts) !== env) return false
      if (symbol && c.symbol !== symbol) return false
      if (asset === 'Option' && c.assetType !== 'Option') return false
      if (asset === 'Stock' && c.assetType === 'Option') return false
      if (needle && !c.symbol.toUpperCase().includes(needle) && !c.accountId.toUpperCase().includes(needle))
        return false
      return true
    })
  }, [pnl, accountId, env, accounts, symbol, asset, q])

  const paperIds = useMemo(
    () => new Set(accounts.filter((a) => a.environment === 'Paper').map((a) => a.accountId)),
    [accounts]
  )

  const profile = useMemo(
    () => buildTradeProfile(closedForProfile, paperIds),
    [closedForProfile, paperIds]
  )

  const pnlStats = useMemo(() => {
    const wins = closed.filter((c) => c.netPnl > 0)
    const losses = closed.filter((c) => c.netPnl < 0)
    const net = closed.reduce((s, c) => s + c.netPnl, 0)
    const rate = closed.length ? wins.length / closed.length : 0
    return {
      net,
      profit: wins.reduce((s, c) => s + c.netPnl, 0),
      loss: losses.reduce((s, c) => s + c.netPnl, 0),
      wins: wins.length,
      losses: losses.length,
      rate
    }
  }, [closed])

  const currency = filtered[0]?.currency ?? closed[0]?.currency ?? 'USD'
  const hasFilter = side !== 'all' || asset !== 'all' || !!symbol || !!q.trim() || pnlFilter !== 'all'

  useEffect(() => {
    setPage(1)
  }, [accountId, env, q, side, asset, symbol, layout, pnlFilter])

  const itemCount =
    layout === 'pnl' ? closed.length : layout === 'day' ? days.length : filtered.length
  const pageCount = Math.max(1, Math.ceil(itemCount / PAGE_SIZE))
  const safePage = Math.min(page, pageCount)
  const sliceStart = (safePage - 1) * PAGE_SIZE
  const pagedClosed = closed.slice(sliceStart, sliceStart + PAGE_SIZE)
  const pagedDays = days.slice(sliceStart, sliceStart + PAGE_SIZE)
  const pagedFills = useMemo(
    () => [...filtered].sort((a, b) => b.tradeTimeUtc.localeCompare(a.tradeTimeUtc)),
    [filtered]
  ).slice(sliceStart, sliceStart + PAGE_SIZE)

  return (
    <div className="space-y-3 sm:space-y-4">
      <div>
        <p className="text-[11px] font-medium uppercase tracking-[0.14em] text-muted-2">Fills · Flex</p>
        <h2 className="mt-0.5 text-lg font-semibold tracking-tight">Trades</h2>
        <p className="mt-0.5 text-[12px] text-muted-2">
          Flex official fills · FIFO realized P&L · options include multiplier
          {stats.from && stats.to ? ` · ${stats.from} → ${stats.to}` : ''}
        </p>
      </div>

      {error && (
        <Card className="border-down/30 bg-down/5">
          <CardContent className="py-4 text-sm text-down">{error}</CardContent>
        </Card>
      )}

      <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
        <Stat
          label="已实现盈亏"
          value={fmtMoney(pnlStats.net, currency)}
          sub={`胜 ${pnlStats.wins} · 负 ${pnlStats.losses}${closed.length ? ` · 胜率 ${(pnlStats.rate * 100).toFixed(0)}%` : ''}`}
          tone={pnlStats.net >= 0 ? 'up' : 'down'}
        />
        <Stat
          label="盈利合计"
          value={fmtMoney(pnlStats.profit, currency)}
          sub={`${pnlStats.wins} 例赚钱`}
          tone="up"
        />
        <Stat
          label="亏损合计"
          value={fmtMoney(pnlStats.loss, currency)}
          sub={`${pnlStats.losses} 例亏钱`}
          tone="down"
        />
        <Stat
          label="Fills / Open"
          value={`${filtered.length} 笔`}
          sub={`未平仓 ${openLots.length} 批 · 现金 ${fmtMoney(stats.net, currency)}`}
        />
      </div>

      {profile && <TradeProfile profile={profile} />}

      <WeeklyReview accountId={accountId} env={env} />

      <Card>
        <CardContent className="space-y-3">
          <div className="flex flex-wrap items-center gap-2">
            <label className="relative min-w-[10rem] flex-1">
              <Search size={14} className="pointer-events-none absolute left-2.5 top-1/2 -translate-y-1/2 text-muted-2" />
              <input
                value={q}
                onChange={(e) => setQ(e.target.value)}
                placeholder="搜标的或账户"
                className="h-9 w-full rounded-lg border-0 bg-bg py-0 pl-8 pr-3 text-sm outline-none ring-1 ring-border/80 placeholder:text-muted-2 focus:ring-foreground/25"
              />
            </label>
            <Seg
              value={side}
              onChange={setSide}
              options={[
                { id: 'all', label: '买卖' },
                { id: 'Buy', label: '买' },
                { id: 'Sell', label: '卖' }
              ]}
            />
            <Seg
              value={asset}
              onChange={setAsset}
              options={[
                { id: 'all', label: '全部品种' },
                { id: 'Option', label: '期权' },
                { id: 'Stock', label: '正股' }
              ]}
            />
            <Seg
              value={layout}
              onChange={setLayout}
              options={[
                { id: 'pnl', label: '盈亏' },
                { id: 'day', label: '按日' },
                { id: 'table', label: '表格' }
              ]}
            />
            {layout === 'pnl' && (
              <Seg
                value={pnlFilter}
                onChange={setPnlFilter}
                options={[
                  { id: 'all', label: '全部例' },
                  { id: 'win', label: '只看赚' },
                  { id: 'lose', label: '只看亏' }
                ]}
              />
            )}
            {hasFilter && (
              <button
                type="button"
                onClick={() => {
                  setQ('')
                  setSide('all')
                  setAsset('all')
                  setSymbol(null)
                  setPnlFilter('all')
                }}
                className="inline-flex h-9 items-center gap-1 rounded-lg px-2.5 text-xs text-muted hover:bg-surface-2 hover:text-foreground"
              >
                <X size={12} /> 清除
              </button>
            )}
          </div>

          {bySymbol.length > 0 && (
            <div className="flex flex-wrap gap-1.5">
              {bySymbol.map((s) => (
                <button
                  key={s.symbol}
                  type="button"
                  onClick={() => setSymbol(symbol === s.symbol ? null : s.symbol)}
                  className={cn(
                    'max-w-[14rem] truncate rounded-full px-2.5 py-1 text-[11px] transition',
                    symbol === s.symbol
                      ? 'bg-foreground text-bg'
                      : 'bg-surface-2 text-muted hover:text-foreground'
                  )}
                  title={`${s.symbol} · ${s.n}笔 · ${fmtMoney(s.notional, currency)}`}
                >
                  {s.symbol} <span className="tabular opacity-70">{s.n}</span>
                </button>
              ))}
            </div>
          )}
        </CardContent>
      </Card>

      {loading ? (
        <div className="grid h-48 place-items-center text-muted">
          <Loader2 size={20} className="animate-spin" />
        </div>
      ) : filtered.length === 0 ? (
        <Card>
          <CardContent className="flex flex-col items-center gap-2 py-16 text-center">
            <Receipt size={22} className="text-muted-2" />
            <p className="text-sm text-muted">没有符合条件的 Trades</p>
            <p className="text-xs text-muted-2">换个账户，或先同步 Flex</p>
          </CardContent>
        </Card>
      ) : (
        <>
          {layout === 'pnl' ? (
            <PnlBook
              closed={pagedClosed}
              openLots={safePage === pageCount ? openLots : []}
              accounts={accounts}
              currency={currency}
              method={pnl?.method}
            />
          ) : layout === 'day' ? (
            <div className="space-y-3">
              {pagedDays.map((d) => (
                <DayCard key={d.date} day={d} accounts={accounts} currency={currency} fillPnl={fillPnl} />
              ))}
            </div>
          ) : (
            <Card className="overflow-hidden">
              <div className="max-h-[70vh] overflow-auto">
                <table className="w-full min-w-[720px] text-sm">
                  <thead className="sticky top-0 z-10 bg-bg/95 backdrop-blur">
                    <tr className="border-y border-border bg-surface-2/50 text-left">
                      <th className={cn(thCls, 'pr-3')}>时间</th>
                      <th className={cn(thCls, 'px-2')}>账户</th>
                      <th className={cn(thCls, 'px-2')}>标的</th>
                      <th className={cn(thCls, 'px-2')}>方向</th>
                      <th className={cn(thCls, 'px-2 text-right')}>数量</th>
                      <th className={cn(thCls, 'px-2 text-right')}>价格</th>
                      <th className={cn(thCls, 'px-2 text-right')}>Notional</th>
                      <th className={cn(thCls, 'pl-2 text-right')}>盈亏</th>
                    </tr>
                  </thead>
                  <tbody>
                    {pagedFills.map((t) => (
                      <FillRow key={t.id} t={t} accounts={accounts} realized={fillPnl.get(t.id)} />
                    ))}
                  </tbody>
                </table>
              </div>
            </Card>
          )}
          <Pager
            page={safePage}
            pageCount={pageCount}
            total={itemCount}
            pageSize={PAGE_SIZE}
            onChange={setPage}
          />
        </>
      )}
    </div>
  )
}

function holdLabel(openIso: string, closeIso: string): string {
  const ms = new Date(closeIso).getTime() - new Date(openIso).getTime()
  if (!Number.isFinite(ms) || ms < 0) return '—'
  const h = ms / 3_600_000
  if (h < 1) return `${Math.max(1, Math.round(ms / 60_000))}分钟`
  if (h < 48) return `${h.toFixed(h < 10 ? 1 : 0)}小时`
  return `${(h / 24).toFixed(1)}天`
}

function PnlBook({
  closed,
  openLots,
  accounts,
  currency,
  method
}: {
  closed: ClosedTrade[]
  openLots: OpenLot[]
  accounts: AccountInfo[]
  currency: string
  method?: string
}) {
  return (
    <div className="space-y-3">
      {method && <p className="text-[11px] text-muted-2">{method}</p>}
      {closed.length === 0 && openLots.length === 0 && (
        <Card>
          <CardContent className="py-12 text-center text-sm text-muted">没有配对到平仓盈亏</CardContent>
        </Card>
      )}
      {closed.map((c, i) => (
        <Card key={`${c.closeTradeId}-${c.openTradeId}-${i}`}>
          <CardContent className="p-4 sm:p-5">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div className="min-w-0">
                <p className="truncate text-sm font-semibold" title={c.symbol}>
                  {c.symbol}
                </p>
                <p className="mt-0.5 text-[12px] text-muted-2">
                  {c.direction === 'Short' ? '空头' : '多头'}
                  {' · '}
                  {fmtNum(c.quantity)}
                  {c.assetType === 'Option' ? '张' : '股'}
                  {c.multiplier > 1 ? ` ×${fmtNum(c.multiplier)}` : ''}
                  {' · '}
                  {accountLabel(c.accountId, accounts)}
                </p>
              </div>
              <p className={cn('text-2xl font-semibold tabular tracking-tight', gainClass(c.netPnl))}>
                {c.netPnl > 0 ? '+' : ''}
                {fmtMoney(c.netPnl, c.currency || currency)}
              </p>
            </div>
            <div className="mt-3 grid gap-2 text-[12px] sm:grid-cols-2">
              <p className="text-muted">
                开仓 {fmtTime(c.openedAtUtc)}
                <span className="mx-1.5 text-border">·</span>
                {c.direction === 'Short' ? '卖' : '买'} {fmtMoney(c.openPrice, c.currency)}
              </p>
              <p className="text-muted">
                平仓 {fmtTime(c.closedAtUtc)}
                <span className="mx-1.5 text-border">·</span>
                {c.direction === 'Short' ? '买' : '卖'} {fmtMoney(c.closePrice, c.currency)}
              </p>
              <p className="text-muted-2">
                价差 {fmtMoney(c.closePrice - c.openPrice, c.currency)}
                {' · '}
                毛利 {c.grossPnl > 0 ? '+' : ''}
                {fmtMoney(c.grossPnl, c.currency)}
              </p>
              <p className="text-muted-2">
                持有 {holdLabel(c.openedAtUtc, c.closedAtUtc)}
                {' · '}
                佣金 {fmtMoney(c.commission, c.currency)}
              </p>
            </div>
          </CardContent>
        </Card>
      ))}
      {openLots.length > 0 && (
        <Card>
          <CardContent className="space-y-2 p-4 sm:p-5">
            <p className="text-xs font-medium text-muted">未平仓 {openLots.length} 批 · 无官方市价，不算浮盈</p>
            {openLots.map((o) => (
              <div key={`${o.tradeId}-${o.symbol}`} className="flex flex-wrap items-baseline justify-between gap-2 text-sm">
                <span className="min-w-0 truncate font-medium">{o.symbol}</span>
                <span className="text-xs text-muted-2">
                  {o.direction === 'Short' ? '空' : '多'} {fmtNum(o.quantity)}
                  {o.assetType === 'Option' ? '张' : '股'} @ {fmtMoney(o.openPrice, o.currency)}
                  {' · '}
                  {fmtTime(o.openedAtUtc)}
                </span>
              </div>
            ))}
          </CardContent>
        </Card>
      )}
    </div>
  )
}

function DayCard({
  day,
  accounts,
  currency,
  fillPnl
}: {
  day: { date: string; fills: TradeRecord[]; buy: number; sell: number; net: number }
  accounts: AccountInfo[]
  currency: string
  fillPnl: Map<number, number>
}) {
  const total = Math.max(day.buy + day.sell, 1)
  return (
    <Card>
      <CardContent className="p-0">
        <div className="flex flex-wrap items-center gap-x-3 gap-y-1 border-b border-border px-4 py-3 sm:px-5">
          <p className="text-sm font-semibold tabular">
            {day.date}
            <span className="ml-1.5 text-xs font-normal text-muted-2">周{weekday(day.date)}</span>
          </p>
          <p className="text-xs text-muted-2">{day.fills.length} 笔</p>
          <div className="h-1.5 min-w-[7rem] flex-1 overflow-hidden rounded-full bg-surface-2">
            <div className="flex h-full w-full">
              <div className="h-full bg-up/50" style={{ width: `${(day.buy / total) * 100}%` }} />
              <div className="h-full bg-down/50" style={{ width: `${(day.sell / total) * 100}%` }} />
            </div>
          </div>
          <p className={cn('text-sm font-semibold tabular', gainClass(day.net))}>{fmtMoney(day.net, currency)}</p>
        </div>
        <div className="divide-y divide-border/60">
          {day.fills.map((t) => (
            <FillLine key={t.id} t={t} accounts={accounts} realized={fillPnl.get(t.id)} />
          ))}
        </div>
      </CardContent>
    </Card>
  )
}

function FillLine({
  t,
  accounts,
  realized
}: {
  t: TradeRecord
  accounts: AccountInfo[]
  realized?: number
}) {
  const buy = t.side === 'Buy'
  const env = envOf(t.accountId, accounts)
  return (
    <div className="grid grid-cols-[auto_1fr_auto] items-center gap-3 px-4 py-2.5 sm:grid-cols-[4.5rem_auto_minmax(0,1fr)_auto_auto_auto] sm:px-5">
      <span className="text-[11px] tabular text-muted-2">{fmtTime(t.tradeTimeUtc).slice(-8)}</span>
      <span
        className={cn(
          'inline-flex items-center gap-0.5 rounded-md px-1.5 py-0.5 text-[11px] font-medium',
          buy ? 'bg-up/10 text-up' : 'bg-down/10 text-down'
        )}
      >
        {buy ? <ArrowUpRight size={11} /> : <ArrowDownRight size={11} />}
        {buy ? '买' : '卖'}
      </span>
      <span className="min-w-0 truncate text-sm font-medium" title={t.symbol}>
        {t.symbol}
      </span>
      <span className="hidden text-right text-xs tabular text-muted sm:block">
        {fmtNum(t.quantity)}
        <span className="ml-1 text-muted-2">{isOption(t) ? '张' : '股'}</span>
        <span className="mx-1 text-border">·</span>
        {fmtMoney(t.price, t.currency)}
      </span>
      <span className="text-right text-sm font-medium tabular">
        {realized != null ? (
          <span className={gainClass(realized)}>
            {realized > 0 ? '+' : ''}
            {fmtMoney(realized, t.currency)}
          </span>
        ) : (
          fmtMoney(t.grossAmount, t.currency)
        )}
      </span>
      <span className="hidden text-right text-[11px] text-muted-2 sm:block">
        <span className={env === 'Paper' ? 'text-up' : 'text-muted'}>{accountLabel(t.accountId, accounts)}</span>
      </span>
    </div>
  )
}

function FillRow({
  t,
  accounts,
  realized
}: {
  t: TradeRecord
  accounts: AccountInfo[]
  realized?: number
}) {
  const buy = t.side === 'Buy'
  return (
    <tr className={trCls}>
      <td className="py-2 pr-3 text-xs tabular text-muted">{fmtTime(t.tradeTimeUtc)}</td>
      <td className="px-2 text-xs text-muted-2">{accountLabel(t.accountId, accounts)}</td>
      <td className="px-2 font-medium">{t.symbol}</td>
      <td className="px-2">
        <span className={cn('rounded-md px-1.5 py-0.5 text-xs font-medium', buy ? 'bg-up/10 text-up' : 'bg-down/10 text-down')}>
          {buy ? '买' : '卖'}
        </span>
      </td>
      <td className="px-2 text-right tabular">{fmtNum(t.quantity)}</td>
      <td className="px-2 text-right tabular">{fmtMoney(t.price, t.currency)}</td>
      <td className="px-2 text-right font-medium tabular">{fmtMoney(t.grossAmount, t.currency)}</td>
      <td className={cn('py-2 pl-2 text-right tabular', realized == null ? gainClass(t.netAmount) : gainClass(realized))}>
        {realized == null
          ? fmtMoney(t.netAmount, t.currency)
          : `${realized > 0 ? '+' : ''}${fmtMoney(realized, t.currency)}`}
      </td>
    </tr>
  )
}

function Stat({
  label,
  value,
  sub,
  tone
}: {
  label: string
  value: string
  sub?: string
  tone?: 'up' | 'down'
}) {
  return (
    <Card>
      <CardContent className="p-4">
        <p className="text-[11px] text-muted-2">{label}</p>
        <p
          className={cn(
            'mt-1 text-lg font-semibold tabular tracking-tight',
            tone === 'up' ? 'text-up' : tone === 'down' ? 'text-down' : 'text-foreground'
          )}
        >
          {value}
        </p>
        {sub && <p className="mt-0.5 text-[11px] text-muted-2">{sub}</p>}
      </CardContent>
    </Card>
  )
}

function Seg<T extends string>({
  value,
  onChange,
  options
}: {
  value: T
  onChange: (v: T) => void
  options: { id: T; label: string }[]
}) {
  return (
    <div className="flex rounded-lg bg-bg p-0.5 ring-1 ring-border/80">
      {options.map((o) => (
        <button
          key={o.id}
          type="button"
          onClick={() => onChange(o.id)}
          className={cn(
            'rounded-md px-2.5 py-1.5 text-[12px] font-medium transition',
            value === o.id ? 'bg-surface text-foreground shadow-soft' : 'text-muted hover:text-foreground'
          )}
        >
          {o.label}
        </button>
      ))}
    </div>
  )
}

function Pager({
  page,
  pageCount,
  total,
  pageSize,
  onChange
}: {
  page: number
  pageCount: number
  total: number
  pageSize: number
  onChange: (p: number) => void
}) {
  if (total === 0) return null
  const from = (page - 1) * pageSize + 1
  const to = Math.min(page * pageSize, total)
  return (
    <div className="flex flex-wrap items-center justify-between gap-2 pt-1">
      <p className="text-[12px] text-muted-2">
        {from}–{to} / {total}
      </p>
      <div className="flex items-center gap-1">
        <button
          type="button"
          disabled={page <= 1}
          onClick={() => onChange(page - 1)}
          className="grid h-8 w-8 place-items-center rounded-lg text-muted ring-1 ring-border/80 transition hover:bg-surface-2 hover:text-foreground disabled:opacity-30"
          aria-label="上一页"
        >
          <ChevronLeft size={16} />
        </button>
        <p className="min-w-[4.5rem] text-center text-[12px] tabular text-muted">
          {page} / {pageCount}
        </p>
        <button
          type="button"
          disabled={page >= pageCount}
          onClick={() => onChange(page + 1)}
          className="grid h-8 w-8 place-items-center rounded-lg text-muted ring-1 ring-border/80 transition hover:bg-surface-2 hover:text-foreground disabled:opacity-30"
          aria-label="下一页"
        >
          <ChevronRight size={16} />
        </button>
      </div>
    </div>
  )
}
