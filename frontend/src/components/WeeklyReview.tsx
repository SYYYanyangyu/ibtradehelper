import { useEffect, useState } from 'react'
import { CalendarCheck, Loader2 } from 'lucide-react'
import { Card, CardContent, CardHeader, CardTitle } from './ui/Card'
import { cn, fmtMoney, gainClass, thCls, trCls } from '../lib/utils'
import { getWeeklyReview, type TradeEnvFilter } from '../lib/api'
import type { ReviewViolation, WeeklyReview as Review } from '../types'

interface Props {
  accountId?: string
  env?: TradeEnvFilter
}

const pct = (v: number | null) => (v == null ? '—' : `${v > 0 ? '+' : ''}${v.toFixed(2)}%`)
const etDateTime = (iso: string) =>
  new Date(iso).toLocaleString('zh-CN', {
    timeZone: 'America/New_York',
    hour12: false,
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit'
  })

export function WeeklyReview({ accountId, env }: Props) {
  const [weeks, setWeeks] = useState(8)
  const [data, setData] = useState<Review | null>(null)
  const [loading, setLoading] = useState(true)
  const [err, setErr] = useState<string | null>(null)
  const [sel, setSel] = useState<string | null>(null)

  useEffect(() => {
    let alive = true
    setLoading(true)
    getWeeklyReview(accountId, env, weeks)
      .then((d) => {
        if (!alive) return
        setData(d)
        setSel((s) => (s && d.weeks.some((w) => w.weekStart === s) ? s : d.weeks.find((w) => w.trades > 0)?.weekStart ?? null))
        setErr(null)
      })
      .catch((e) => alive && setErr(e instanceof Error ? e.message : '加载失败'))
      .finally(() => alive && setLoading(false))
    return () => {
      alive = false
    }
  }, [accountId, env, weeks])

  const week = data?.weeks.find((w) => w.weekStart === sel) ?? null
  const periodTrades = data?.weeks.reduce((n, w) => n + w.trades, 0) ?? 0

  return (
    <Card className="animate-fade-up">
      <CardHeader>
        <div>
          <CardTitle className="flex items-center gap-1.5">
            <CalendarCheck size={16} className="text-primary" />
            每周复盘
          </CardTitle>
          <p className="mt-0.5 text-xs text-muted-2">
            按 FIFO 平仓、以开仓成交为一笔，归入最后平仓的美东周 · 期权开仓逐条对照纪律：{data?.rulesSummary ?? '…'}
          </p>
        </div>
        <div className="flex items-center gap-1">
          {[4, 8, 12].map((n) => (
            <button
              key={n}
              onClick={() => setWeeks(n)}
              className={cn(
                'rounded-lg border px-2 py-0.5 text-xs transition',
                weeks === n ? 'border-primary/40 bg-primary/10 text-primary' : 'border-border/70 text-muted hover:bg-surface-2'
              )}
            >
              {n} 周
            </button>
          ))}
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        {loading && !data ? (
          <p className="flex items-center gap-1.5 text-sm text-muted-2">
            <Loader2 size={14} className="animate-spin" /> 计算中…
          </p>
        ) : err ? (
          <p className="text-sm text-down">{err}</p>
        ) : data ? (
          <>
            {data.dataNotes.length > 0 && (
              <ul className="space-y-0.5 text-xs text-amber-500">
                {data.dataNotes.map((n) => (
                  <li key={n}>{n}</li>
                ))}
              </ul>
            )}

            <div>
              <p className="mb-2 text-xs text-muted">
                近 {weeks} 周 {periodTrades} 笔 · 每条纪律被违反的次数与这些交易的胜率、净盈亏（一笔可能同时违反多条）
              </p>
              <ViolationBars items={data.periodViolations} />
            </div>

            <div className="overflow-x-auto">
              <table className="w-full text-sm tabular">
                <thead>
                  <tr className="text-left">
                    <th className={thCls}>周（美东）</th>
                    <th className={cn(thCls, 'text-right')}>笔数</th>
                    <th className={cn(thCls, 'text-right')}>胜率</th>
                    <th className={cn(thCls, 'text-right')}>净盈亏</th>
                    <th className={cn(thCls, 'text-right')}>盈亏比 PF</th>
                    <th className={cn(thCls, 'text-right')}>每笔期望</th>
                    <th className={cn(thCls, 'text-right')}>最大回撤</th>
                    <th className={cn(thCls, 'text-right')}>守纪律</th>
                    <th className={cn(thCls, 'text-right')}>违规</th>
                    <th className={cn(thCls, 'text-right')}>账户周收益</th>
                    <th className={cn(thCls, 'text-right')}>{data.benchmark}</th>
                  </tr>
                </thead>
                <tbody>
                  {data.weeks.map((w) => (
                    <tr
                      key={w.weekStart}
                      onClick={() => w.trades > 0 && setSel(w.weekStart)}
                      className={cn(
                        trCls,
                        w.trades > 0 && 'cursor-pointer',
                        sel === w.weekStart && 'bg-primary/5'
                      )}
                    >
                      <td className="px-3 py-2 text-xs">
                        {w.weekStart.slice(5)} – {w.weekEnd.slice(5)}
                      </td>
                      <td className="px-3 text-right">{w.trades || '—'}</td>
                      <td className="px-3 text-right">{w.trades ? `${w.winRate}%` : '—'}</td>
                      <td className={cn('px-3 text-right', gainClass(w.net))}>{w.trades ? fmtMoney(w.net) : '—'}</td>
                      <td className={cn('px-3 text-right', w.profitFactor != null && (w.profitFactor >= 1 ? 'text-up' : 'text-down'))}>
                        {w.profitFactor ?? '—'}
                      </td>
                      <td className={cn('px-3 text-right', gainClass(w.expectancy))}>{w.trades ? fmtMoney(w.expectancy) : '—'}</td>
                      <td className="px-3 text-right text-down">{w.maxDrawdown ? `−${fmtMoney(w.maxDrawdown)}` : '—'}</td>
                      <td className="px-3 text-right text-xs">
                        {w.compliantTrades ? (
                          <>
                            {w.compliantTrades} 笔 <span className={gainClass(w.compliantNet)}>{fmtMoney(w.compliantNet)}</span>
                          </>
                        ) : (
                          '—'
                        )}
                      </td>
                      <td className="px-3 text-right text-xs">
                        {w.violatingTrades ? (
                          <>
                            {w.violatingTrades} 笔 <span className={gainClass(w.violatingNet)}>{fmtMoney(w.violatingNet)}</span>
                          </>
                        ) : (
                          '—'
                        )}
                      </td>
                      <td className={cn('px-3 text-right', gainClass(w.accountReturnPct ?? 0))}>{pct(w.accountReturnPct)}</td>
                      <td className={cn('px-3 text-right', gainClass(w.benchmarkReturnPct ?? 0))}>{pct(w.benchmarkReturnPct)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
              <p className="mt-1.5 text-[11px] text-muted-2">
                盈亏为扣佣金后的已实现 · 账户周收益 = 本周纯交易盈亏（Flex 净值差减出入金）÷ 上周末净值（期初不足 $100 时不算百分比）· {data.benchmark} 为 Gateway 日线收盘
              </p>
            </div>

            {week && (
              <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
                <div>
                  <p className="mb-2 text-xs text-muted">
                    {week.weekStart} 这周：平均盈利 <span className="text-up">{fmtMoney(week.avgWin)}</span> · 平均亏损{' '}
                    <span className="text-down">{fmtMoney(week.avgLoss)}</span> · 守纪律的单胜率 {week.compliantWinRate}%
                  </p>
                  <ViolationBars items={week.violations} />
                </div>
                <div>
                  <p className="mb-2 text-xs text-muted">这周亏得最多的违规单</p>
                  {week.worstViolations.length === 0 ? (
                    <p className="text-xs text-muted-2">没有违规单</p>
                  ) : (
                    <ul className="space-y-1.5">
                      {week.worstViolations.map((t) => (
                        <li key={t.openTradeId} className="rounded-lg border border-border/50 px-2.5 py-1.5 text-xs">
                          <div className="flex items-center justify-between">
                            <span className="font-medium text-foreground">{t.symbol.replace(/\s+/g, ' ')}</span>
                            <span className={cn('font-semibold', gainClass(t.net))}>{fmtMoney(t.net)}</span>
                          </div>
                          <div className="mt-0.5 text-muted-2">
                            美东 {etDateTime(t.openedAtUtc)} → {etDateTime(t.closedAtUtc)} · 权利金 {fmtMoney(t.premium)} ·{' '}
                            <span className="text-down">{t.violations.join('、')}</span>
                          </div>
                        </li>
                      ))}
                    </ul>
                  )}
                </div>
              </div>
            )}
          </>
        ) : null}
      </CardContent>
    </Card>
  )
}

function ViolationBars({ items }: { items: ReviewViolation[] }) {
  const max = Math.max(1, ...items.map((v) => Math.abs(v.net)))
  return (
    <div className="space-y-1">
      {items.map((v) => (
        <div key={v.key} className="flex items-center gap-2 text-xs">
          <span className="w-28 shrink-0 text-foreground">{v.label}</span>
          <span className="w-24 shrink-0 text-right text-muted-2">
            {v.count ? `${v.count} 笔 · 胜 ${Math.round((v.wins * 100) / v.count)}%` : '0 笔'}
          </span>
          <div className="relative h-2 flex-1 rounded bg-surface-2">
            {v.count > 0 && (
              <div
                className={cn('absolute top-0 h-2 rounded', v.net < 0 ? 'bg-down/70' : 'bg-up/70')}
                style={{ width: `${(Math.abs(v.net) / max) * 100}%` }}
              />
            )}
          </div>
          <span className={cn('w-20 shrink-0 text-right font-medium tabular', gainClass(v.net))}>
            {v.count ? fmtMoney(v.net) : '—'}
          </span>
        </div>
      ))}
    </div>
  )
}
