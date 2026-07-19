import {
  AreaChart,
  Area,
  XAxis,
  YAxis,
  Tooltip as ReTooltip,
  ResponsiveContainer,
  CartesianGrid,
  ReferenceLine
} from 'recharts'
import { fmtCompact, fmtMoney } from '../lib/utils'
import { useChartTheme, CHART_COLORS, ChartTooltipShell } from '../lib/chart'

/** ISO / Date 字符串 → 横轴短日期 MM-DD（去掉 T00:00:00）。 */
function formatAxisDate(v: string | number): string {
  const s = String(v)
  const m = s.match(/(\d{4})-(\d{2})-(\d{2})/)
  return m ? `${m[2]}-${m[3]}` : s.length >= 10 ? s.slice(5, 10) : s
}

interface Props {
  data: Array<{ time: string; equity: number }>
  /** 初始资金，画一条虚线作为基准。 */
  initialCapital?: number
}

interface TooltipProps {
  active?: boolean
  payload?: Array<{ payload: { time: string; equity: number } }>
}

function EquityTooltip({ active, payload }: TooltipProps) {
  if (!active || !payload?.length) return null
  const d = payload[0].payload
  return (
    <ChartTooltipShell>
      <p className="font-semibold text-foreground">{d.time.slice(0, 10)}</p>
      <p className="tabular text-primary">权益 {fmtMoney(d.equity)}</p>
    </ChartTooltipShell>
  )
}

/** 回测资金曲线（权益随时间变化），风格与看板图表统一。 */
export function EquityCurveChart({ data, initialCapital }: Props) {
  const { tick, grid, cursor } = useChartTheme()
  if (!data.length) return null

  return (
    <ResponsiveContainer width="100%" height={320}>
      <AreaChart data={data} margin={{ top: 8, right: 12, left: 4, bottom: 0 }}>
        <defs>
          <linearGradient id="eqFill" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor={CHART_COLORS.primary} stopOpacity={0.25} />
            <stop offset="100%" stopColor={CHART_COLORS.primary} stopOpacity={0} />
          </linearGradient>
        </defs>
        <CartesianGrid strokeDasharray="3 3" stroke={grid} vertical={false} />
        <XAxis
          dataKey="time"
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
          width={52}
          tickFormatter={(v) => fmtCompact(Number(v))}
          domain={([min, max]) => {
            if (!Number.isFinite(min) || !Number.isFinite(max)) return ['auto', 'auto']
            const pad = Math.max((max - min) * 0.08, Math.abs(max) * 0.01, 100)
            return [Math.max(0, min - pad), max + pad]
          }}
        />
        <ReTooltip cursor={{ fill: cursor }} content={<EquityTooltip />} />
        {initialCapital && (
          <ReferenceLine
            y={initialCapital}
            stroke={CHART_COLORS.rose}
            strokeDasharray="4 4"
            strokeWidth={1}
            label={{ value: '初始资金', position: 'insideTopLeft', fill: tick, fontSize: 10 }}
          />
        )}
        <Area
          type="monotone"
          dataKey="equity"
          name="权益"
          stroke={CHART_COLORS.primary}
          strokeWidth={2}
          fill="url(#eqFill)"
          dot={false}
          isAnimationActive={false}
        />
      </AreaChart>
    </ResponsiveContainer>
  )
}
