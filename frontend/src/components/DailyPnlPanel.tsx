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
  const months = useMemo(() => monthsBetween(data), [data])

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
          <p className="text-sm text-muted">Daily P&L appears after a few sessions</p>
          <p className="text-xs text-muted-2">Sync Flex to fill official NAV</p>
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
            <h3 className="mt-0.5 text-base font-semibold tracking-tight">Daily P&L</h3>
            <p className="mt-0.5 text-[11px] text-muted-2">Official Flex NAV (Gateway does not overwrite)</p>
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

          <MonthCalendar
            month={safeMonth}
            points={monthDays}
            selectedDate={selectedDate}
            selectedIdx={selectedIdx}
            onSelect={setSelectedDate}
          />

          {/* 主体：图与详情，无套娃边框 */}
          <div className="mt-6 grid gap-8 lg:grid-cols-[minmax(0,1.5fr)_minmax(260px,0.9fr)] lg:gap-10">
            <div className="min-w-0">
              <div className="mb-3 flex items-baseline justify-between">
                <p className="text-xs text-muted-2">NAV change</p>
                <p className="text-[11px] text-muted-2/80">Click bar to select</p>
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
                Pick a session
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
        NAV{' '}
        <span className="font-medium tabular text-foreground">{fmtMoney(d.netLiquidation, currency)}</span>
      </p>

      <dl className="mt-6 space-y-3 border-t border-border pt-5">
        <Row label="Trading P&L" value={d.tradingPnl} currency={currency} />
        <Row label="External cash" value={d.externalCashFlow} currency={currency} quiet={d.externalCashFlow === 0} />
        <Row label="Cum. NAV change" value={d.cumulativePnl} currency={currency} />
        <Row label="Cum. trading" value={d.cumulativeTradingPnl} currency={currency} />
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

const WEEKDAYS = ['一', '二', '三', '四', '五', '六', '日'] as const

function monthsBetween(data: DailyPnlPoint[]): string[] {
  if (!data.length) return []
  const dates = data.map((d) => d.date).sort()
  const start = dates[0].slice(0, 7)
  const end = dates[dates.length - 1].slice(0, 7)
  const out: string[] = []
  let [y, m] = start.split('-').map(Number)
  const [ey, em] = end.split('-').map(Number)
  while (y < ey || (y === ey && m <= em)) {
    out.push(`${y}-${String(m).padStart(2, '0')}`)
    m += 1
    if (m > 12) {
      m = 1
      y += 1
    }
  }
  return out
}

function isoDate(y: number, m: number, d: number): string {
  return `${y}-${String(m).padStart(2, '0')}-${String(d).padStart(2, '0')}`
}

function buildMonthCells(month: string, points: DailyPnlPoint[]) {
  if (!month) return []
  const y = Number(month.slice(0, 4))
  const m = Number(month.slice(5, 7))
  const last = new Date(Date.UTC(y, m, 0)).getUTCDate()
  const firstDow = new Date(Date.UTC(y, m - 1, 1)).getUTCDay()
  const offset = (firstDow + 6) % 7
  const byDate = new Map(points.map((p) => [p.date, p]))
  const cells: { key: string; date: string | null; day: number | null; point?: DailyPnlPoint }[] = []
  for (let i = 0; i < offset; i++) {
    cells.push({ key: `pad-pre-${i}`, date: null, day: null })
  }
  for (let d = 1; d <= last; d++) {
    const date = isoDate(y, m, d)
    cells.push({ key: date, date, day: d, point: byDate.get(date) })
  }
  while (cells.length % 7 !== 0) {
    cells.push({ key: `pad-post-${cells.length}`, date: null, day: null })
  }
  return cells
}

function MonthCalendar({
  month,
  points,
  selectedDate,
  selectedIdx,
  onSelect
}: {
  month: string
  points: DailyPnlPoint[]
  selectedDate: string | null
  selectedIdx: number
  onSelect: (date: string) => void
}) {
  const cells = useMemo(() => buildMonthCells(month, points), [month, points])
  return (
    <div className="mt-6">
      <div className="mb-2 flex items-center justify-between text-[11px] text-muted-2">
        <span>本月日历</span>
        <span className="flex items-center gap-1">
          <button
            type="button"
            aria-label="Previous day"
            disabled={selectedIdx <= 0}
            onClick={() => selectedIdx > 0 && onSelect(points[selectedIdx - 1].date)}
            className="grid h-6 w-6 place-items-center rounded transition hover:bg-surface-2 disabled:opacity-25"
          >
            <ChevronLeft size={14} />
          </button>
          <button
            type="button"
            aria-label="Next day"
            disabled={selectedIdx < 0 || selectedIdx >= points.length - 1}
            onClick={() =>
              selectedIdx >= 0 && selectedIdx < points.length - 1 && onSelect(points[selectedIdx + 1].date)
            }
            className="grid h-6 w-6 place-items-center rounded transition hover:bg-surface-2 disabled:opacity-25"
          >
            <ChevronRight size={14} />
          </button>
        </span>
      </div>
      <div className="grid grid-cols-7 gap-1">
        {WEEKDAYS.map((w) => (
          <div
            key={w}
            className={cn(
              'pb-1 text-center text-[10px] font-medium uppercase tracking-wide',
              w === '六' || w === '日' ? 'text-muted-2/70' : 'text-muted-2'
            )}
          >
            {w}
          </div>
        ))}
        {cells.map((cell, i) => {
          if (!cell.date || cell.day == null) {
            return <div key={cell.key} className="min-h-[3.25rem] rounded-lg" />
          }
          const point = cell.point
          const active = cell.date === selectedDate
          const weekend = i % 7 >= 5
          if (!point) {
            return (
              <div
                key={cell.key}
                className={cn(
                  'flex min-h-[3.25rem] flex-col items-center rounded-lg px-1 py-1.5',
                  weekend ? 'text-muted-2/50' : 'text-muted-2/70'
                )}
              >
                <span className="text-[11px] tabular">{cell.day}</span>
              </div>
            )
          }
          return (
            <button
              key={cell.key}
              type="button"
              onClick={() => onSelect(point.date)}
              className={cn(
                'flex min-h-[3.25rem] flex-col items-center rounded-lg px-1 py-1.5 transition',
                active ? 'bg-surface-2 ring-1 ring-border' : 'hover:bg-surface-2/60'
              )}
            >
              <span className="text-[11px] tabular text-muted-2">{cell.day}</span>
              <span className={cn('mt-0.5 text-xs font-semibold tabular', gainClass(point.dailyPnl))}>
                {compactPnl(point.dailyPnl)}
              </span>
              {active && (
                <span
                  className={cn(
                    'mt-1 h-0.5 w-4 rounded-full',
                    point.dailyPnl > 0 ? 'bg-up' : point.dailyPnl < 0 ? 'bg-down' : 'bg-foreground'
                  )}
                />
              )}
            </button>
          )
        })}
      </div>
    </div>
  )
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
