import { useEffect, useMemo, useState } from 'react'
import {
  LineChart,
  Line,
  XAxis,
  YAxis,
  Tooltip as ReTooltip,
  ResponsiveContainer,
  CartesianGrid,
  ReferenceLine
} from 'recharts'
import { Loader2 } from 'lucide-react'
import { Card, CardContent } from './ui/Card'
import { getPerformance, type TradeEnvFilter } from '../lib/api'
import type { PerformanceCompare, PerformancePoint, PerformanceRange } from '../types'
import { cn, fmtMoney, fmtPct, gainClass } from '../lib/utils'
import { CHART_COLORS, ChartTooltipShell, useChartTheme } from '../lib/chart'

const RANGES: { id: PerformanceRange; label: string }[] = [
  { id: '1M', label: '1月' },
  { id: '3M', label: '3月' },
  { id: '6M', label: '6月' },
  { id: 'YTD', label: '今年' },
  { id: '1Y', label: '1年' },
  { id: 'ALL', label: '全部' }
]

const LINES = [
  { key: 'portfolio' as const, id: 'portfolio', label: '我的组合', color: CHART_COLORS.up, width: 2.4 },
  { key: 'hsi' as const, id: 'hsi', label: '恒生', color: CHART_COLORS.hsi, width: 1.5 },
  { key: 'spx' as const, id: 'spx', label: '标普500', color: CHART_COLORS.spx, width: 1.5 },
  { key: 'dji' as const, id: 'dji', label: '道琼斯', color: CHART_COLORS.dji, width: 1.5 },
  { key: 'ixic' as const, id: 'ixic', label: '纳斯达克', color: CHART_COLORS.ixic, width: 1.5 }
]

function formatAxisDate(v: string | number): string {
  const s = String(v)
  const m = s.match(/(\d{4})-(\d{2})-(\d{2})/)
  return m ? `${m[2]}-${m[3]}` : s
}

function TooltipBody({
  active,
  payload,
  label
}: {
  active?: boolean
  payload?: Array<{ name: string; value: number; color: string; dataKey: string }>
  label?: string
}) {
  if (!active || !payload?.length) return null
  return (
    <ChartTooltipShell>
      <p className="mb-1 font-semibold text-foreground">{label}</p>
      {payload.map((p) => (
        <p key={p.dataKey} className="tabular" style={{ color: p.color }}>
          {p.name} {fmtPct(Number(p.value), false)}
        </p>
      ))}
    </ChartTooltipShell>
  )
}

interface Props {
  env?: TradeEnvFilter
  accountId?: string
}

export function PerformanceCurve({ env, accountId }: Props) {
  const { tick, grid, cursor } = useChartTheme()
  const [range, setRange] = useState<PerformanceRange>('1Y')
  const [hidden, setHidden] = useState<Record<string, boolean>>({})
  const [data, setData] = useState<PerformanceCompare | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let alive = true
    setLoading(true)
    setError(null)
    getPerformance(accountId || undefined, range, env)
      .then((d) => {
        if (!alive) return
        setData(d)
      })
      .catch((e) => {
        if (!alive) return
        setError(e instanceof Error ? e.message : '收益曲线加载失败')
        setData(null)
      })
      .finally(() => {
        if (alive) setLoading(false)
      })
    return () => {
      alive = false
    }
  }, [range, env, accountId])

  const seriesById = useMemo(() => {
    const m = new Map((data?.series ?? []).map((s) => [s.id, s]))
    return m
  }, [data])

  const chartData = data?.points ?? []
  const hasLine = (key: keyof PerformancePoint) =>
    chartData.some((p) => p[key] != null) && !hidden[key]

  return (
    <Card className="animate-fade-up overflow-hidden">
      <CardContent className="p-0">
        <div className="flex flex-wrap items-start justify-between gap-3 border-b border-border px-5 py-4 sm:px-6">
          <div>
            <p className="text-[11px] font-medium uppercase tracking-[0.14em] text-muted-2">
              Return · vs benchmarks
            </p>
            <h3 className="mt-0.5 text-base font-semibold tracking-tight">收益曲线</h3>
            <p className="mt-0.5 text-[11px] text-muted-2">
              组合 = Flex 官方净值 · 美股三大 = 美联储 FRED · 恒生 = Gateway
            </p>
            {data?.fromDate && data.toDate && (
              <p className="mt-1 text-[11px] text-muted">
                {data.fromDate} {data.startNav != null ? fmtMoney(data.startNav, 'USD') : ''}
                {' → '}
                {data.toDate} {data.endNav != null ? fmtMoney(data.endNav, 'USD') : ''}
                {data.method ? ` · ${data.method}` : ''}
              </p>
            )}
          </div>
          <div className="flex flex-wrap gap-1 rounded-lg bg-surface-2/80 p-1">
            {RANGES.map((r) => (
              <button
                key={r.id}
                type="button"
                onClick={() => setRange(r.id)}
                className={cn(
                  'rounded-md px-2.5 py-1 text-[11px] font-medium transition',
                  range === r.id
                    ? 'bg-surface text-foreground shadow-soft'
                    : 'text-muted hover:text-foreground'
                )}
              >
                {r.label}
              </button>
            ))}
          </div>
        </div>

        <div className="flex flex-wrap gap-2 px-5 pt-4 sm:px-6">
          {LINES.map((line) => {
            var s = seriesById.get(line.id)
            const on = !hidden[line.id]
            const ret = s?.periodReturnPct
            return (
              <button
                key={line.id}
                type="button"
                onClick={() => setHidden((h) => ({ ...h, [line.id]: !h[line.id] }))}
                className={cn(
                  'inline-flex items-center gap-1.5 rounded-full border px-2.5 py-1 text-[11px] transition',
                  on ? 'border-border bg-surface' : 'border-transparent bg-surface-2 text-muted-2 line-through'
                )}
              >
                <span className="h-1.5 w-3 rounded-full" style={{ background: line.color }} />
                <span>{s?.label ?? line.label}</span>
                <span className={cn('tabular', ret == null ? 'text-muted-2' : gainClass(ret))}>
                  {ret == null ? (s?.status === 'error' ? '—' : '…') : fmtPct(ret, false)}
                </span>
              </button>
            )
          })}
        </div>

        <div className="px-2 pb-4 pt-2 sm:px-3">
          {loading ? (
            <div className="grid h-[280px] place-items-center text-muted">
              <Loader2 size={18} className="animate-spin" />
            </div>
          ) : error ? (
            <p className="px-4 py-16 text-center text-sm text-down">{error}</p>
          ) : !chartData.length ? (
            <p className="px-4 py-16 text-center text-sm text-muted-2">
              还没有可画的净值。先同步 Flex。
            </p>
          ) : (
            <ResponsiveContainer width="100%" height={280}>
              <LineChart data={chartData} margin={{ top: 8, right: 12, left: 0, bottom: 0 }}>
                <CartesianGrid strokeDasharray="3 3" stroke={grid} vertical={false} />
                <XAxis
                  dataKey="date"
                  tick={{ fontSize: 11, fill: tick }}
                  axisLine={false}
                  tickLine={false}
                  minTickGap={28}
                  tickFormatter={formatAxisDate}
                />
                <YAxis
                  tick={{ fontSize: 11, fill: tick }}
                  axisLine={false}
                  tickLine={false}
                  width={48}
                  tickFormatter={(v) => `${Number(v).toFixed(0)}%`}
                />
                <ReferenceLine y={0} stroke={tick} strokeDasharray="3 3" strokeOpacity={0.45} />
                <ReTooltip cursor={{ stroke: cursor, strokeWidth: 20 }} content={<TooltipBody />} />
                {LINES.map(
                  (line) =>
                    hasLine(line.key) && (
                      <Line
                        key={line.id}
                        type="monotone"
                        dataKey={line.key}
                        name={line.label}
                        stroke={line.color}
                        strokeWidth={line.width}
                        dot={false}
                        connectNulls
                        isAnimationActive={false}
                      />
                    )
                )}
              </LineChart>
            </ResponsiveContainer>
          )}
        </div>

        {data?.series.some((s) => s.status === 'error') && (
          <p className="border-t border-border px-5 py-2.5 text-[11px] text-muted-2 sm:px-6">
            {data.series
              .filter((s) => s.status === 'error')
              .map((s) => `${s.label}：${s.message ?? '未取到'}`)
              .join(' · ')}
          </p>
        )}
      </CardContent>
    </Card>
  )
}
