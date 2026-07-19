import {
  ComposedChart,
  Bar,
  Area,
  XAxis,
  YAxis,
  Tooltip as ReTooltip,
  ResponsiveContainer,
  CartesianGrid,
  Cell
} from 'recharts'
import { useChartTheme, CHART_COLORS, ChartTooltipShell } from '../lib/chart'
import type { DailyPnlPoint } from '../types'

interface DailyPnlChartProps {
  data: DailyPnlPoint[]
  currency?: string
  highlightDate?: string
  onSelectDate?: (date: string) => void
}

function DailyPnlTooltip({
  active,
  payload,
  label
}: {
  active?: boolean
  payload?: Array<{ payload: DailyPnlPoint }>
  label?: string | number
}) {
  if (!active || !payload?.length) return null
  const d = payload[0].payload
  const up = d.dailyPnl >= 0
  return (
    <ChartTooltipShell>
      <p className="text-[11px] text-muted-2">{label}</p>
      <p className={`mt-0.5 text-sm font-semibold tabular ${up ? 'text-up' : 'text-down'}`}>
        {up ? '+' : ''}
        {d.dailyPnl.toLocaleString('en-US', { style: 'currency', currency: 'USD' })}
      </p>
    </ChartTooltipShell>
  )
}

export function DailyPnlChart({ data, highlightDate, onSelectDate }: DailyPnlChartProps) {
  const { tick, grid, cursor } = useChartTheme()
  if (!data.length) return null

  return (
    <ResponsiveContainer width="100%" height="100%">
      <ComposedChart
        data={data}
        margin={{ top: 4, right: 4, left: 0, bottom: 0 }}
        onClick={(state) => {
          const date = (state?.activePayload?.[0]?.payload as DailyPnlPoint | undefined)?.date
          if (date && onSelectDate) onSelectDate(date)
        }}
      >
        <defs>
          <linearGradient id="netFillQuiet" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor={CHART_COLORS.primary} stopOpacity={0.1} />
            <stop offset="100%" stopColor={CHART_COLORS.primary} stopOpacity={0} />
          </linearGradient>
        </defs>
        <CartesianGrid strokeDasharray="2 6" stroke={grid} vertical={false} />
        <XAxis
          dataKey="date"
          tick={{ fontSize: 11, fill: tick }}
          axisLine={false}
          tickLine={false}
          minTickGap={24}
          tickFormatter={(d) => (typeof d === 'string' ? d.slice(5) : d)}
        />
        <YAxis
          yAxisId="pnl"
          tick={{ fontSize: 11, fill: tick }}
          axisLine={false}
          tickLine={false}
          width={44}
          tickFormatter={(v) => {
            const n = Number(v)
            const a = Math.abs(n)
            if (a >= 1_000_000) return `${(n / 1_000_000).toFixed(1)}M`
            if (a >= 1_000) return `${(n / 1_000).toFixed(0)}k`
            return `${n}`
          }}
        />
        <YAxis yAxisId="net" orientation="right" hide />
        <ReTooltip cursor={{ fill: cursor }} content={<DailyPnlTooltip />} />
        <Bar yAxisId="pnl" dataKey="dailyPnl" radius={[2, 2, 0, 0]} maxBarSize={36} cursor="pointer">
          {data.map((d) => {
            const hi = !highlightDate || highlightDate === d.date
            const fill = d.dailyPnl >= 0 ? CHART_COLORS.up : CHART_COLORS.down
            return <Cell key={d.date} fill={fill} fillOpacity={hi ? 0.9 : 0.22} />
          })}
        </Bar>
        <Area
          yAxisId="net"
          type="monotone"
          dataKey="netLiquidation"
          stroke={CHART_COLORS.primary}
          strokeWidth={1.25}
          strokeOpacity={0.55}
          fill="url(#netFillQuiet)"
          dot={false}
          activeDot={false}
        />
      </ComposedChart>
    </ResponsiveContainer>
  )
}
