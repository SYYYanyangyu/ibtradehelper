import { useEffect, useState } from 'react'
import { PieChart, Loader2 } from 'lucide-react'
import { Card, CardContent, CardHeader, CardTitle } from './ui/Card'
import { cn, fmtMoney, gainClass } from '../lib/utils'
import { getPositionDailyPnl, type TradeEnvFilter } from '../lib/api'
import type { PositionDailyPnl } from '../types'

interface Props {
  /** 日期 yyyy-MM-dd；留空取今天 */
  date?: string
  /** 账户；留空表示当前环境下全部账户 */
  accountId?: string
  /** Paper / Live，与看板环境芯片一致 */
  env?: TradeEnvFilter
  /**
   * 为 false 时不请求。
   * 默认 true。
   */
  enabled?: boolean
  /** 无数据时的说明 */
  emptyHint?: string
  currency?: string
  compact?: boolean
  embed?: boolean
}

/** 个股盈亏贡献：按日期展示贡献排行。数据在后端计算，前端只渲染。 */
export function PositionContribution({
  date,
  accountId,
  env,
  enabled = true,
  emptyHint = 'No position records for this day',
  currency = 'USD',
  compact,
  embed
}: Props) {
  const [data, setData] = useState<PositionDailyPnl | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(false)

  useEffect(() => {
    let cancelled = false
    if (!enabled) {
      setData(null)
      setLoading(false)
      setError(false)
      return
    }
    setLoading(true)
    setError(false)
    setData(null)
    getPositionDailyPnl(date, accountId || undefined, env)
      .then((d) => !cancelled && setData(d))
      .catch(() => !cancelled && setError(true))
      .finally(() => !cancelled && setLoading(false))
    return () => {
      cancelled = true
    }
  }, [date, accountId, env, enabled])

  const points = data?.points ?? []
  const total = data?.totalContribution ?? 0

  const rows = !loading && !error && points.length > 0 && (
    <div className="max-h-[13.5rem] space-y-3 overflow-y-auto overscroll-contain pr-1 [-webkit-overflow-scrolling:touch] sm:max-h-[16.5rem]">
      {points.map((r) => {
        const up = r.dailyContribution >= 0
        return (
          <div key={r.symbol}>
            <div className="flex items-baseline justify-between gap-3 text-sm">
              <span className="min-w-0 truncate font-medium tracking-tight text-foreground">
                {r.symbol}
              </span>
              <span className={cn('shrink-0 tabular font-medium', gainClass(r.dailyContribution))}>
                {r.dailyContribution > 0 ? '+' : ''}
                {fmtMoney(r.dailyContribution, currency)}
              </span>
            </div>
            <div className="mt-1.5 flex items-center gap-2">
              <div className="relative h-1 flex-1 overflow-hidden rounded-full bg-surface-2">
                <div
                  className={cn('absolute inset-y-0 left-0 rounded-full', up ? 'bg-up/70' : 'bg-down/70')}
                  style={{ width: `${Math.min(100, r.share * 100)}%` }}
                />
              </div>
              <span className="w-10 shrink-0 text-right text-[11px] tabular text-muted-2">
                {(r.share * 100).toFixed(0)}%
              </span>
            </div>
          </div>
        )
      })}
    </div>
  )

  const title = (
    <div className="flex items-center justify-between gap-2">
      <span className="flex items-center gap-1.5 text-xs font-medium uppercase tracking-[0.12em] text-muted-2">
        <PieChart size={12} />
        {date ? 'Contribution' : 'Day Contribution'}
      </span>
      {points.length > 0 && (
        <span className={cn('text-sm font-medium tabular', gainClass(total))}>
          {total > 0 ? '+' : ''}
          {fmtMoney(total, currency)}
        </span>
      )}
    </div>
  )

  const body = (
    <>
      {loading && (
        <div className="flex items-center justify-center gap-2 py-6">
          <Loader2 size={14} className="animate-spin text-muted-2" />
          <span className="text-sm text-muted-2">Loading…</span>
        </div>
      )}
      {error && (
        <div className="flex flex-col items-center gap-1 py-6 text-center">
          <p className="text-sm text-muted-2">Contribution unavailable</p>
        </div>
      )}
      {!loading && !error && points.length === 0 && (
        <div className="flex flex-col items-center gap-1 py-6 text-center">
          <p className="text-sm text-muted-2">{emptyHint}</p>
        </div>
      )}
      {rows}
    </>
  )

  if (embed) {
    return (
      <div>
        <div className="mb-3">{title}</div>
        {body}
      </div>
    )
  }

  return (
    <Card className={cn('animate-fade-up', compact && 'border-border/60 shadow-none')}>
      <CardHeader>
        <CardTitle className="sr-only">Contribution</CardTitle>
        {title}
      </CardHeader>
      <CardContent>{body}</CardContent>
    </Card>
  )
}
