import { ArrowDownRight, ArrowUpRight } from 'lucide-react'
import { Card } from './ui/Card'
import { cn, fmtMoney, fmtPct, gainClass } from '../lib/utils'

interface Props {
  label: string
  value: number
  icon: React.ReactNode
  currency?: string
  /** 按盈亏正负着色并显示 ± 号与百分比胶囊 */
  pnl?: boolean
  /** 盈亏相对基准的百分比（小数，如 0.1234） */
  pct?: number
  sub?: string
  index?: number
}

export function StatCard({
  label,
  value,
  icon,
  currency = 'USD',
  pnl,
  pct,
  sub,
  index = 0
}: Props) {
  const isUp = value >= 0
  const valueColor = pnl ? gainClass(value) : 'text-foreground'

  return (
    <Card
      className="group relative overflow-hidden p-5 animate-fade-up hover:-translate-y-0.5 hover:shadow-glow"
      style={{ animationDelay: `${index * 70}ms` }}
    >
      {/* 装饰光晕 */}
      <div
        className={cn(
          'pointer-events-none absolute -right-8 -top-10 h-28 w-28 rounded-full opacity-20 blur-2xl transition-opacity group-hover:opacity-40',
          pnl ? (isUp ? 'bg-up' : 'bg-down') : 'bg-primary'
        )}
      />

      <div className="relative flex items-start justify-between">
        <p className="text-sm font-medium text-muted">{label}</p>
        <div
          className={cn(
            'rounded-xl p-2.5 text-white shadow-sm',
            pnl
              ? isUp
                ? 'bg-gradient-to-br from-emerald-400 to-emerald-600'
                : 'bg-gradient-to-br from-rose-400 to-rose-600'
              : 'bg-gradient-to-br from-indigo-400 to-indigo-600'
          )}
        >
          {icon}
        </div>
      </div>

      <p className={cn('relative mt-3 text-[1.7rem] font-bold leading-none tabular', valueColor)}>
        {pnl && value > 0 ? '+' : ''}
        {fmtMoney(value, currency)}
      </p>

      <div className="relative mt-3 flex items-center gap-2">
        {pnl && typeof pct === 'number' && (
          <span
            className={cn(
              'inline-flex items-center gap-0.5 rounded-full px-2 py-0.5 text-xs font-semibold tabular',
              value > 0 ? 'bg-up/10' : value < 0 ? 'bg-down/10' : 'bg-surface-2',
              gainClass(value)
            )}
          >
            {isUp ? <ArrowUpRight size={13} /> : <ArrowDownRight size={13} />}
            {fmtPct(Math.abs(pct), true)}
          </span>
        )}
        {sub && <span className="text-xs text-muted-2">{sub}</span>}
      </div>
    </Card>
  )
}
