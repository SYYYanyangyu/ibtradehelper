import { useEffect, useMemo, useState } from 'react'
import { ChevronLeft, ChevronRight, Calendar } from 'lucide-react'
import { Card, CardContent } from './ui/Card'
import { cn, fmtMoney, fmtPct, gainClass } from '../lib/utils'
import { PositionContribution } from './PositionContribution'
import { DailyPnlChart } from './DailyPnlChart'
import type { TradeEnvFilter } from '../lib/api'
import type { DailyPnlPoint } from '../types'

interface Props {
  data: DailyPnlPoint[]
  currency: string
  /** 与看板环境芯片一致，避免个股贡献串到 ALL */
  env?: TradeEnvFilter
  accountId?: string
}

/** 每日盈亏：克制排版 + 主从联动（日条 / 图 / 详情）。 */
export function DailyPnlPanel({ data, currency, env, accountId }: Props) {
  const months = useMemo(() => {
    const set = new Set(data.map((d) => d.date.slice(0, 7)))
    return [...set].sort()
  }, [data])

  const [month, setMonth] = useState(() => months[months.length - 1] ?? '')
  const safeMonth = months.includes(month) ? month : (months[months.length - 1] ?? '')
  const [selectedDate, setSelectedDate] = useState<string | null>(null)

  const monthDays = useMemo(
    () =>
      data
        .filter((d) => d.date.startsWith(safeMonth))
        .sort((a, b) => a.date.localeCompare(b.date)),
    [data, safeMonth]
  )

  const summary = useMemo(() => {
    const monthPnl = monthDays.reduce((s, d) => s + d.dailyPnl, 0)
    const up = monthDays.filter((d) => d.dailyPnl > 0).length
    const down = monthDays.filter((d) => d.dailyPnl < 0).length
    return { monthPnl, up, down }
  }, [monthDays])

  useEffect(() => {
    if (!monthDays.length) {
      setSelectedDate(null)
      return
    }
    setSelectedDate((cur) =>
      cur && monthDays.some((d) => d.date === cur) ? cur : monthDays[monthDays.length - 1].date
    )
  }, [safeMonth, monthDays])

  const selected = monthDays.find((d) => d.date === selectedDate) ?? null
  const selectedIdx = monthDays.findIndex((d) => d.date === selectedDate)

  if (!data.length) {
    return (
      <Card className="animate-fade-up">
        <CardContent className="flex flex-col items-center gap-2 py-16 text-center">
          <div className="grid h-10 w-10 place-items-center rounded-xl bg-surface-2 text-muted-2">
            <Calendar size={18} />
          </div>
          <p className="text-sm text-muted">运行几天后展示每日盈亏</p>
          <p className="text-xs text-muted-2">同步 Flex 后将自动填充日净值数据</p>
        </CardContent>
      </Card>
    )
  }

  const y = safeMonth.slice(0, 4)
  const m = String(parseInt(safeMonth.slice(5, 7), 10))

  return (
    <Card className="animate-fade-up overflow-hidden">
      <CardContent className="p-0">
        {/* 顶栏：标题 + 月切换，一条线切开 */}
        <div className="flex items-center justify-between gap-4 border-b border-border px-5 py-4 sm:px-6">
          <div>
            <p className="text-[11px] font-medium uppercase tracking-[0.14em] text-muted-2">Daily P&L · Flex</p>
            <h3 className="mt-0.5 text-base font-semibold tracking-tight">每日盈亏</h3>
            <p className="mt-0.5 text-[11px] text-muted-2">净值来自 Flex 官方报表（Gateway 不覆盖）</p>
          </div>
          <MonthNav months={months} month={safeMonth} onChange={setMonth} />
        </div>

        <div className="px-5 py-5 sm:px-6 sm:py-6">
          {/* 月数字：大、干净、少装饰 */}
          <div className="flex flex-wrap items-end justify-between gap-4">
            <div>
              <p className="text-sm text-muted">
                {y} 年 {m} 月
              </p>
              <p
                className={cn(
                  'mt-2 text-4xl font-semibold tracking-[-0.03em] tabular sm:text-[2.75rem]',
                  gainClass(summary.monthPnl)
                )}
              >
                {summary.monthPnl > 0 ? '+' : ''}
                {fmtMoney(summary.monthPnl, currency)}
              </p>
            </div>
            <p className="pb-1 text-sm text-muted-2">
              <span className="text-foreground">{monthDays.length}</span> 天记录
              <span className="mx-2 opacity-40">·</span>
              <span className="text-up">{summary.up}</span>
              <span className="mx-1 opacity-40">/</span>
              <span className="text-down">{summary.down}</span>
            </p>
          </div>

          {/* 日选择：整月网格铺开，禁止横向滚动 */}
          {monthDays.length > 0 && (
            <div className="mt-6">
              <div className="mb-2 flex items-center justify-between text-[11px] text-muted-2">
                <span>本月交易日</span>
                <span className="flex items-center gap-1">
                  <button
                    type="button"
                    aria-label="上一日"
                    disabled={selectedIdx <= 0}
                    onClick={() => selectedIdx > 0 && setSelectedDate(monthDays[selectedIdx - 1].date)}
                    className="grid h-6 w-6 place-items-center rounded transition hover:bg-surface-2 disabled:opacity-25"
                  >
                    <ChevronLeft size={14} />
                  </button>
                  <button
                    type="button"
                    aria-label="下一日"
                    disabled={selectedIdx < 0 || selectedIdx >= monthDays.length - 1}
                    onClick={() =>
                      selectedIdx >= 0 &&
                      selectedIdx < monthDays.length - 1 &&
                      setSelectedDate(monthDays[selectedIdx + 1].date)
                    }
                    className="grid h-6 w-6 place-items-center rounded transition hover:bg-surface-2 disabled:opacity-25"
                  >
                    <ChevronRight size={14} />
                  </button>
                </span>
              </div>
              <div className="grid grid-cols-5 gap-1.5 sm:grid-cols-7 md:grid-cols-8 lg:grid-cols-10">
                {monthDays.map((d) => {
                  const active = d.date === selectedDate
                  return (
                    <button
                      key={d.date}
                      type="button"
                      onClick={() => setSelectedDate(d.date)}
                      className={cn(
                        'flex flex-col items-center rounded-lg px-1 py-2 transition',
                        active
                          ? 'bg-surface-2 ring-1 ring-border'
                          : 'hover:bg-surface-2/60'
                      )}
                    >
                      <span className="text-[11px] text-muted-2">
                        {d.date.slice(8)}
                        <span className="ml-0.5 opacity-70">{weekdayShort(d.date)}</span>
                      </span>
                      <span className={cn('mt-1 text-xs font-semibold tabular', gainClass(d.dailyPnl))}>
                        {compactPnl(d.dailyPnl)}
                      </span>
                      {active && (
                        <span
                          className={cn(
                            'mt-1.5 h-0.5 w-4 rounded-full',
                            d.dailyPnl > 0 ? 'bg-up' : d.dailyPnl < 0 ? 'bg-down' : 'bg-foreground'
                          )}
                        />
                      )}
                    </button>
                  )
                })}
              </div>
            </div>
          )}

          {/* 主体：图与详情，无套娃边框 */}
          <div className="mt-6 grid gap-8 lg:grid-cols-[minmax(0,1.5fr)_minmax(260px,0.9fr)] lg:gap-10">
            <div className="min-w-0">
              <div className="mb-3 flex items-baseline justify-between">
                <p className="text-xs text-muted-2">净值变动</p>
                <p className="text-[11px] text-muted-2/80">点击柱状切换</p>
              </div>
              <div className="h-52 w-full sm:h-60">
                <DailyPnlChart
                  data={monthDays}
                  currency={currency}
                  highlightDate={selectedDate ?? undefined}
                  onSelectDate={setSelectedDate}
                />
              </div>
            </div>

            {selected ? (
              <SelectedDay d={selected} currency={currency} env={env} accountId={accountId} />
            ) : (
              <div className="flex min-h-[200px] items-center justify-center text-sm text-muted-2">
                选择交易日
              </div>
            )}
          </div>
        </div>
      </CardContent>
    </Card>
  )
}

function SelectedDay({
  d,
  currency,
  env,
  accountId
}: {
  d: DailyPnlPoint
  currency: string
  env?: TradeEnvFilter
  accountId?: string
}) {
  return (
    <div className="min-w-0 lg:border-l lg:border-border lg:pl-8">
      <p className="text-[11px] font-medium uppercase tracking-[0.12em] text-muted-2">
        {weekdayLabel(d.date)} · {d.date}
      </p>
      <p className={cn('mt-2 text-3xl font-semibold tracking-[-0.03em] tabular', gainClass(d.dailyPnl))}>
        {d.dailyPnl > 0 ? '+' : ''}
        {fmtMoney(d.dailyPnl, currency)}
      </p>
      <p className="mt-1.5 text-sm text-muted">
        <span className={cn('font-medium tabular', gainClass(d.dailyPnlPct))}>
          {fmtPct(d.dailyPnlPct, false)}
        </span>
        <span className="mx-2 text-border">·</span>
        净值{' '}
        <span className="font-medium tabular text-foreground">{fmtMoney(d.netLiquidation, currency)}</span>
      </p>

      <dl className="mt-6 space-y-3 border-t border-border pt-5">
        <Row label="纯交易盈亏" value={d.tradingPnl} currency={currency} />
        <Row label="外部现金流" value={d.externalCashFlow} currency={currency} quiet={d.externalCashFlow === 0} />
        <Row label="累计净值变动" value={d.cumulativePnl} currency={currency} />
        <Row label="累计纯交易" value={d.cumulativeTradingPnl} currency={currency} />
      </dl>

      <div className="mt-6 border-t border-border pt-5">
        <PositionContribution
          date={d.date}
          currency={currency}
          embed
          env={env}
          accountId={accountId}
          emptyHint="该日无有效个股贡献"
        />
      </div>
    </div>
  )
}

function Row({
  label,
  value,
  currency,
  quiet
}: {
  label: string
  value: number
  currency: string
  quiet?: boolean
}) {
  return (
    <div className="flex items-baseline justify-between gap-4 text-sm">
      <dt className="text-muted-2">{label}</dt>
      <dd className={cn('font-medium tabular', quiet ? 'text-muted' : gainClass(value))}>
        {value > 0 ? '+' : ''}
        {fmtMoney(value, currency)}
      </dd>
    </div>
  )
}

function weekdayLabel(iso: string): string {
  const [y, m, d] = iso.split('-').map(Number)
  return ['周日', '周一', '周二', '周三', '周四', '周五', '周六'][
    new Date(Date.UTC(y, m - 1, d)).getUTCDay()
  ]
}

function weekdayShort(iso: string): string {
  const [y, m, d] = iso.split('-').map(Number)
  return ['日', '一', '二', '三', '四', '五', '六'][new Date(Date.UTC(y, m - 1, d)).getUTCDay()]
}

function compactPnl(v: number): string {
  const abs = Math.abs(v)
  const sign = v < 0 ? '-' : v > 0 ? '+' : ''
  if (abs >= 1_000_000) return `${sign}${(abs / 1_000_000).toFixed(1)}M`
  if (abs >= 1_000) return `${sign}${(abs / 1_000).toFixed(1)}k`
  return `${sign}${abs.toFixed(0)}`
}

function MonthNav({
  months,
  month,
  onChange
}: {
  months: string[]
  month: string
  onChange: (m: string) => void
}) {
  const i = months.indexOf(month)
  const label = month ? `${month.slice(0, 4)}.${month.slice(5)}` : '—'
  return (
    <div className="flex items-center gap-0.5 text-muted">
      <button
        type="button"
        onClick={() => i > 0 && onChange(months[i - 1])}
        disabled={i <= 0}
        aria-label="上一月"
        className="grid h-8 w-8 place-items-center rounded-md transition hover:bg-surface-2 hover:text-foreground disabled:opacity-25"
      >
        <ChevronLeft size={16} />
      </button>
      <span className="min-w-[4.25rem] text-center text-sm font-medium tabular text-foreground">
        {label}
      </span>
      <button
        type="button"
        onClick={() => i < months.length - 1 && onChange(months[i + 1])}
        disabled={i >= months.length - 1}
        aria-label="下一月"
        className="grid h-8 w-8 place-items-center rounded-md transition hover:bg-surface-2 hover:text-foreground disabled:opacity-25"
      >
        <ChevronRight size={16} />
      </button>
    </div>
  )
}
