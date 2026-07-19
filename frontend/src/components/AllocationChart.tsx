import { useState } from 'react'
import { PieChart, Pie, Cell, ResponsiveContainer, Sector, Tooltip as ReTooltip } from 'recharts'
import { fmtCompact, fmtMoney } from '../lib/utils'
import { PIE_PALETTE, ChartTooltipShell } from '../lib/chart'

interface Datum {
  name: string
  value: number
  currency?: string
}

interface ActiveShapeProps {
  cx?: number
  cy?: number
  innerRadius?: number
  outerRadius?: number
  startAngle?: number
  endAngle?: number
  fill?: string
}

function ActiveShape(props: ActiveShapeProps) {
  const { cx, cy, innerRadius, outerRadius, startAngle, endAngle, fill } = props
  return (
    <g>
      <Sector
        cx={cx}
        cy={cy}
        innerRadius={innerRadius}
        outerRadius={(outerRadius ?? 0) + 6}
        startAngle={startAngle}
        endAngle={endAngle}
        fill={fill}
        cornerRadius={4}
      />
    </g>
  )
}

interface AllocTooltipProps {
  active?: boolean
  payload?: Array<{ payload: Datum }>
  currency?: string
}

function AllocTooltip({ active, payload, currency }: AllocTooltipProps) {
  if (!active || !payload?.length) return null
  const d = payload[0].payload
  return (
    <ChartTooltipShell>
      <p className="font-semibold text-foreground">{d.name}</p>
      <p className="tabular text-muted">{fmtMoney(d.value, currency)}</p>
    </ChartTooltipShell>
  )
}

export function AllocationChart({ data }: { data: Datum[] }) {
  const [active, setActive] = useState<number | undefined>(undefined)
  const total = data.reduce((s, d) => s + d.value, 0)
  const currency = data[0]?.currency ?? 'USD'

  if (!total) return null

  return (
    <div className="flex flex-col items-center gap-4 sm:flex-row sm:items-center">
      <div className="relative h-[230px] w-[230px] shrink-0">
        <ResponsiveContainer width="100%" height="100%">
          <PieChart>
            <Pie
              data={data}
              dataKey="value"
              nameKey="name"
              cx="50%"
              cy="50%"
              innerRadius={68}
              outerRadius={100}
              paddingAngle={2}
              stroke="none"
              activeIndex={active}
              activeShape={ActiveShape}
              onMouseEnter={(_, i) => setActive(i)}
              onMouseLeave={() => setActive(undefined)}
              isAnimationActive
            >
              {data.map((d, i) => (
                <Cell key={`${d.name}-${i}`} fill={PIE_PALETTE[i % PIE_PALETTE.length]} />
              ))}
            </Pie>
            <ReTooltip content={<AllocTooltip currency={currency} />} />
          </PieChart>
        </ResponsiveContainer>
        <div className="pointer-events-none absolute inset-0 flex flex-col items-center justify-center">
          <span className="text-[0.7rem] font-medium uppercase tracking-wider text-muted-2">
            总市值
          </span>
          <span className="text-lg font-bold tabular text-foreground">
            {fmtCompact(total, currency)}
          </span>
        </div>
      </div>

      <ul className="grid w-full grid-cols-2 gap-x-4 gap-y-2 sm:flex-1">
        {data.map((d, i) => {
          const pct = ((d.value / total) * 100).toFixed(1)
          return (
            <li
              key={d.name}
              onMouseEnter={() => setActive(i)}
              onMouseLeave={() => setActive(undefined)}
              className={cnItem(active === i)}
            >
              <span
                className="h-2.5 w-2.5 shrink-0 rounded-full"
                style={{ background: PIE_PALETTE[i % PIE_PALETTE.length] }}
              />
              <span className="flex-1 truncate font-medium text-foreground">{d.name}</span>
              <span className="tabular text-muted">{pct}%</span>
            </li>
          )
        })}
      </ul>
    </div>
  )
}

function cnItem(active: boolean): string {
  return [
    'flex items-center gap-2 rounded-lg px-2 py-1.5 text-xs transition-colors',
    active ? 'bg-surface-2' : 'hover:bg-surface-2/60'
  ].join(' ')
}
