import {
  BarChart,
  Bar,
  XAxis,
  YAxis,
  Tooltip as ReTooltip,
  ResponsiveContainer,
  CartesianGrid,
  Cell
} from 'recharts'
import { fmtMoney } from '../lib/utils'
import { useChartTheme, CHART_COLORS, ChartTooltipShell } from '../lib/chart'

interface Datum {
  symbol: string
  unrealized: number
  realized: number
  currency?: string
}

interface PnlTooltipProps {
  active?: boolean
  payload?: Array<{ dataKey?: string | number; value?: number }>
  label?: string | number
  currency?: string
}

function PnlTooltip({ active, payload, label, currency }: PnlTooltipProps) {
  if (!active || !payload?.length) return null
  const u = payload.find((p) => p.dataKey === 'unrealized')?.value ?? 0
  const r = payload.find((p) => p.dataKey === 'realized')?.value ?? 0
  return (
    <ChartTooltipShell>
      <p className="mb-1 font-semibold text-foreground">{label}</p>
      <p className="tabular text-up">未实现 {u >= 0 ? '+' : ''}{fmtMoney(u, currency)}</p>
      <p className="tabular text-primary">已实现 {r >= 0 ? '+' : ''}{fmtMoney(r, currency)}</p>
    </ChartTooltipShell>
  )
}

export function PnlBarChart({ data }: { data: Datum[] }) {
  const { tick, grid, cursor } = useChartTheme()
  const currency = data[0]?.currency ?? 'USD'

  return (
    <ResponsiveContainer width="100%" height={270}>
      <BarChart data={data} barGap={6} margin={{ top: 8, right: 4, left: -8, bottom: 0 }}>
        <CartesianGrid strokeDasharray="3 3" stroke={grid} vertical={false} />
        <XAxis
          dataKey="symbol"
          tick={{ fontSize: 12, fill: tick }}
          axisLine={false}
          tickLine={false}
        />
        <YAxis
          tick={{ fontSize: 11, fill: tick }}
          axisLine={false}
          tickLine={false}
          width={52}
          tickFormatter={(v) => fmtMoney(v, currency)}
        />
        <ReTooltip cursor={{ fill: cursor }} content={<PnlTooltip currency={currency} />} />
        <Bar dataKey="unrealized" name="未实现" radius={[5, 5, 0, 0]} maxBarSize={26}>
          {data.map((d, i) => (
            <Cell key={i} fill={d.unrealized >= 0 ? CHART_COLORS.up : CHART_COLORS.down} />
          ))}
        </Bar>
        <Bar dataKey="realized" name="已实现" radius={[5, 5, 0, 0]} maxBarSize={26}>
          {data.map((d, i) => (
            <Cell key={i} fill={d.realized >= 0 ? CHART_COLORS.primary : CHART_COLORS.rose} />
          ))}
        </Bar>
      </BarChart>
    </ResponsiveContainer>
  )
}
