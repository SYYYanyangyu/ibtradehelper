import { useMemo, useState } from 'react'
import { ArrowDownRight, ArrowUpRight, Receipt } from 'lucide-react'
import type { TradeRecord } from '../types'
import { cn, fmtMoney, fmtNum, fmtTime, gainClass, thCls, trCls } from '../lib/utils'
import { Card, CardContent, CardHeader, CardTitle } from './ui/Card'

interface Props {
  trades: TradeRecord[]
  currency: string
  /** 当前环境标签，仅展示用 */
  envLabel?: string
}

/**
 * 成交汇总：买/卖概览 + 按标的成交额条形 + 按日买卖热力 + 明细表。
 */
export function TradeSummary({ trades, currency, envLabel }: Props) {
  const [filterSymbol, setFilterSymbol] = useState<string | null>(null)
  const [filterDate, setFilterDate] = useState<string | null>(null)

  const { buys, sells, buyShares, buyGross, sellShares, sellGross, netCash } = useMemo(() => {
    const buys = trades.filter((t) => t.side === 'Buy')
    const sells = trades.filter((t) => t.side === 'Sell')
    const sum = (arr: TradeRecord[], f: (t: TradeRecord) => number) =>
      arr.reduce((acc, t) => acc + f(t), 0)
    return {
      buys,
      sells,
      buyShares: sum(buys, (t) => t.quantity),
      buyGross: sum(buys, (t) => t.grossAmount),
      sellShares: sum(sells, (t) => t.quantity),
      sellGross: sum(sells, (t) => t.grossAmount),
      netCash: sum(trades, (t) => t.netAmount)
    }
  }, [trades])

  const bySymbol = useMemo(() => {
    const map = new Map<string, { buy: number; sell: number; count: number }>()
    for (const t of trades) {
      const cur = map.get(t.symbol) ?? { buy: 0, sell: 0, count: 0 }
      if (t.side === 'Buy') cur.buy += t.grossAmount
      else cur.sell += t.grossAmount
      cur.count += 1
      map.set(t.symbol, cur)
    }
    return [...map.entries()]
      .map(([symbol, v]) => ({ symbol, ...v, total: v.buy + v.sell }))
      .sort((a, b) => b.total - a.total)
      .slice(0, 12)
  }, [trades])

  const maxSymbol = Math.max(1, ...bySymbol.map((s) => s.total))

  const byDay = useMemo(() => {
    const map = new Map<string, { buy: number; sell: number; count: number }>()
    for (const t of trades) {
      const day = t.tradeTimeUtc.slice(0, 10)
      const cur = map.get(day) ?? { buy: 0, sell: 0, count: 0 }
      if (t.side === 'Buy') cur.buy += t.grossAmount
      else cur.sell += t.grossAmount
      cur.count += 1
      map.set(day, cur)
    }
    return [...map.entries()]
      .map(([date, v]) => ({ date, ...v }))
      .sort((a, b) => a.date.localeCompare(b.date))
      .slice(-28)
  }, [trades])

  const maxDay = Math.max(1, ...byDay.map((d) => d.buy + d.sell))

  const sorted = useMemo(() => {
    let list = [...trades]
    if (filterSymbol) list = list.filter((t) => t.symbol === filterSymbol)
    if (filterDate) list = list.filter((t) => t.tradeTimeUtc.startsWith(filterDate))
    return list.sort((a, b) => b.tradeTimeUtc.localeCompare(a.tradeTimeUtc))
  }, [trades, filterSymbol, filterDate])

  if (trades.length === 0) {
    return (
      <Card className="animate-fade-up">
        <CardHeader>
          <CardTitle>Trades</CardTitle>
          <span className="text-xs text-muted-2">Buy / sell shares & notional</span>
        </CardHeader>
        <CardContent>
          <div className="flex flex-col items-center gap-2 py-12 text-center">
            <div className="grid h-12 w-12 place-items-center rounded-2xl bg-surface-2 text-muted-2">
              <Receipt size={22} />
            </div>
            <p className="text-sm text-muted">No trades for this account</p>
          </div>
        </CardContent>
      </Card>
    )
  }

  return (
    <Card className="animate-fade-up">
      <CardHeader className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <Receipt size={18} className="text-primary" />
          <CardTitle>Trades</CardTitle>
        </div>
        <span className="text-xs text-muted-2">
          Flex
          {envLabel ? ` · ${envLabel}` : ''} · {trades.length} fills · click symbol/date to filter
        </span>
      </CardHeader>
      <CardContent className="space-y-5">
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <div className="rounded-xl border border-border bg-surface-2/40 p-4">
            <div className="flex items-center gap-1.5 text-sm font-medium text-up">
              <ArrowUpRight size={15} /> Buy
              <span className="ml-auto text-xs text-muted-2">{buys.length}</span>
            </div>
            <div className="mt-3 grid grid-cols-2 gap-3">
              <Metric label="Shares" value={fmtNum(buyShares)} tone="up" />
              <Metric label="Notional" value={fmtMoney(buyGross, currency)} tone="up" />
            </div>
          </div>
          <div className="rounded-xl border border-border bg-surface-2/40 p-4">
            <div className="flex items-center gap-1.5 text-sm font-medium text-down">
              <ArrowDownRight size={15} /> Sell
              <span className="ml-auto text-xs text-muted-2">{sells.length}</span>
            </div>
            <div className="mt-3 grid grid-cols-2 gap-3">
              <Metric label="Shares" value={fmtNum(sellShares)} tone="down" />
              <Metric label="Notional" value={fmtMoney(sellGross, currency)} tone="down" />
            </div>
          </div>
        </div>

        <div className="flex items-center justify-between rounded-xl bg-surface-2/60 px-4 py-2.5 text-sm">
          <span className="text-muted">Net cash (buy − sell)</span>
          <span className={cn('font-semibold tabular-nums', gainClass(netCash))}>
            {fmtMoney(netCash, currency)}
          </span>
        </div>

        {/* 按标的：买卖成交额对比条 */}
        {bySymbol.length > 0 && (
          <div>
            <div className="mb-2 flex items-center justify-between">
              <p className="text-xs font-medium text-muted">Notional by symbol</p>
              {filterSymbol && (
                <button
                  type="button"
                  onClick={() => setFilterSymbol(null)}
                  className="text-xs text-primary hover:underline"
                >
                  清除标的筛选
                </button>
              )}
            </div>
            <div className="space-y-2">
              {bySymbol.map((s) => (
                <button
                  key={s.symbol}
                  type="button"
                  onClick={() => setFilterSymbol(filterSymbol === s.symbol ? null : s.symbol)}
                  className={cn(
                    'grid w-full grid-cols-[88px_1fr_auto] items-center gap-2 rounded-xl px-2 py-1.5 text-left transition hover:bg-surface-2/80',
                    filterSymbol === s.symbol && 'bg-primary/10 ring-1 ring-primary/30'
                  )}
                >
                  <span className="truncate text-sm font-medium">{s.symbol}</span>
                  <div className="relative h-2.5 overflow-hidden rounded-full bg-surface-2">
                    <div
                      className="absolute inset-y-0 left-0 rounded-full bg-up/45"
                      style={{ width: `${(s.buy / maxSymbol) * 100}%` }}
                    />
                    <div
                      className="absolute inset-y-0 rounded-full bg-down/45"
                      style={{
                        left: `${(s.buy / maxSymbol) * 100}%`,
                        width: `${(s.sell / maxSymbol) * 100}%`
                      }}
                    />
                  </div>
                  <span className="text-xs tabular text-muted-2">{s.count}笔</span>
                </button>
              ))}
            </div>
            <div className="mt-2 flex gap-3 text-[11px] text-muted-2">
              <span className="inline-flex items-center gap-1">
                <span className="h-2 w-2 rounded-full bg-up/70" /> 买
              </span>
              <span className="inline-flex items-center gap-1">
                <span className="h-2 w-2 rounded-full bg-down/70" /> 卖
              </span>
            </div>
          </div>
        )}

        {/* 近 28 个有成交日：买卖柱 */}
        {byDay.length > 0 && (
          <div>
            <div className="mb-2 flex items-center justify-between">
              <p className="text-xs font-medium text-muted">近期成交节奏（有成交的交易日）</p>
              {filterDate && (
                <button
                  type="button"
                  onClick={() => setFilterDate(null)}
                  className="text-xs text-primary hover:underline"
                >
                  清除日期筛选
                </button>
              )}
            </div>
            <div className="flex h-28 items-end gap-1 overflow-x-auto pb-1">
              {byDay.map((d) => {
                const hBuy = Math.max(4, (d.buy / maxDay) * 100)
                const hSell = Math.max(d.sell > 0 ? 4 : 0, (d.sell / maxDay) * 100)
                const active = filterDate === d.date
                return (
                  <button
                    key={d.date}
                    type="button"
                    title={`${d.date}\n买 ${fmtMoney(d.buy, currency)}\n卖 ${fmtMoney(d.sell, currency)}\n${d.count} 笔`}
                    onClick={() => setFilterDate(active ? null : d.date)}
                    className={cn(
                      'group flex min-w-[18px] flex-1 flex-col items-center justify-end gap-0.5 rounded-md px-0.5 py-1 transition',
                      active ? 'bg-primary/15' : 'hover:bg-surface-2/80'
                    )}
                  >
                    <div className="flex h-20 w-full items-end justify-center gap-px">
                      <div
                        className="w-[45%] rounded-t-sm bg-up/50 transition group-hover:bg-up/70"
                        style={{ height: `${hBuy}%` }}
                      />
                      <div
                        className="w-[45%] rounded-t-sm bg-down/50 transition group-hover:bg-down/70"
                        style={{ height: `${hSell}%` }}
                      />
                    </div>
                    <span className="text-[9px] tabular text-muted-2">{d.date.slice(8)}</span>
                  </button>
                )
              })}
            </div>
          </div>
        )}

        {(filterSymbol || filterDate) && (
          <p className="text-xs text-muted">
            当前筛选：
            {filterSymbol && <span className="font-medium text-foreground"> {filterSymbol}</span>}
            {filterDate && <span className="font-medium text-foreground"> {filterDate}</span>}
            <span className="text-muted-2"> · {sorted.length} 笔</span>
          </p>
        )}

        <div className="max-h-[420px] overflow-y-auto">
          <table className="w-full text-sm">
            <thead className="sticky top-0 z-10 bg-bg/95 backdrop-blur">
              <tr className="border-y border-border bg-surface-2/50 text-left">
                <th className={cn(thCls, 'pr-3')}>Time</th>
                <th className={cn(thCls, 'px-2')}>Symbol</th>
                <th className={cn(thCls, 'px-2')}>Side</th>
                <th className={cn(thCls, 'px-2 text-right')}>Qty</th>
                <th className={cn(thCls, 'px-2 text-right')}>Price</th>
                <th className={cn(thCls, 'px-2 text-right')}>Notional</th>
                <th className={cn(thCls, 'px-2 text-right')}>Comm</th>
                <th className={cn(thCls, 'pl-2 text-right')}>Net</th>
              </tr>
            </thead>
            <tbody>
              {sorted.map((t) => {
                const isBuy = t.side === 'Buy'
                return (
                  <tr key={t.id} className={trCls}>
                    <td className="py-2 pr-3 text-xs text-muted tabular-nums">
                      {fmtTime(t.tradeTimeUtc)}
                    </td>
                    <td className="py-2 px-2 font-medium">{t.symbol}</td>
                    <td className="py-2 px-2">
                      <span
                        className={cn(
                          'inline-flex rounded-md px-1.5 py-0.5 text-xs font-medium',
                          isBuy ? 'bg-up/10 text-up' : 'bg-down/10 text-down'
                        )}
                      >
                        {isBuy ? 'Buy' : 'Sell'}
                      </span>
                    </td>
                    <td className="py-2 px-2 text-right tabular-nums">{fmtNum(t.quantity)}</td>
                    <td className="py-2 px-2 text-right tabular-nums">{fmtMoney(t.price, t.currency)}</td>
                    <td className="py-2 px-2 text-right tabular-nums font-medium">
                      {fmtMoney(t.grossAmount, t.currency)}
                    </td>
                    <td className="py-2 px-2 text-right tabular-nums text-muted">
                      {fmtMoney(t.commission, t.currency)}
                    </td>
                    <td className={cn('py-2 pl-2 text-right tabular-nums', gainClass(t.netAmount))}>
                      {fmtMoney(t.netAmount, t.currency)}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      </CardContent>
    </Card>
  )
}

function Metric({ label, value, tone }: { label: string; value: string; tone: 'up' | 'down' }) {
  return (
    <div>
      <p className="text-xs text-muted-2">{label}</p>
      <p className={cn('mt-0.5 text-lg font-semibold tabular-nums', tone === 'up' ? 'text-up' : 'text-down')}>
        {value}
      </p>
    </div>
  )
}
