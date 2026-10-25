import { Card, CardContent } from './ui/Card'
import {
  HOLD_BUCKET_LABEL,
  WEEKDAY_LABEL,
  type HoldBucket,
  type TradeProfileModel
} from '../lib/tradeProfile'
import { cn, fmtMoney, gainClass } from '../lib/utils'

const HOLD_ORDER: HoldBucket[] = ['scalp', 'intraday', 'session', 'swing', 'position']

interface Props {
  profile: TradeProfileModel
}

export function TradeProfile({ profile: p }: Props) {
  const hourMax = Math.max(1, ...p.session.hours.map((h) => h.n))
  const dayMax = Math.max(1, ...p.session.weekdays.map((d) => d.n))
  const holdMax = Math.max(1, ...HOLD_ORDER.map((k) => p.hold.buckets[k]))

  return (
    <Card>
      <CardContent className="space-y-4 p-4 sm:p-5">
        <div className="flex flex-wrap items-start justify-between gap-2">
          <div>
            <p className="text-[11px] font-medium uppercase tracking-[0.14em] text-muted-2">Profile</p>
            <h3 className="mt-0.5 text-base font-semibold tracking-tight">{p.headline}</h3>
          </div>
          <p className="text-[11px] text-muted-2">FIFO 已实现 · 不含未平仓浮盈 · 时段为美东</p>
        </div>

        <div className="flex flex-wrap gap-1.5">
          {p.tags.map((t) => (
            <span key={t} className="rounded-full bg-surface-2 px-2.5 py-1 text-[11px] text-muted">
              {t}
            </span>
          ))}
        </div>

        <div className="space-y-2 text-[13px] leading-relaxed text-muted">
          {p.paragraphs.map((text) => (
            <p key={text.slice(0, 24)}>{text}</p>
          ))}
        </div>

        {p.warnings.map((w) => (
          <p key={w.slice(0, 24)} className="rounded-lg bg-down/5 px-3 py-2 text-[12px] text-down">
            {w}
          </p>
        ))}

        <div className="grid grid-cols-2 gap-2 sm:grid-cols-4 lg:grid-cols-8">
          <Metric label="中位持仓" value={holdShort(p.hold.medianMs)} />
          <Metric label="当日平仓" value={`${(p.hold.sameDayPct * 100).toFixed(0)}%`} />
          <Metric label="胜率" value={`${(p.edge.wr * 100).toFixed(0)}%`} />
          <Metric label="利润因子" value={finite(p.edge.pf)} />
          <Metric
            label="单例期望"
            value={`${p.edge.expectancy >= 0 ? '+' : ''}${fmtMoney(p.edge.expectancy, p.currency)}`}
            tone={p.edge.expectancy >= 0 ? 'up' : 'down'}
          />
          <Metric label="盈亏比" value={finite(p.edge.payoff)} />
          <Metric label="日均例数" value={p.edge.perDay.toFixed(1)} />
          <Metric label="最长连亏" value={`${p.edge.maxLoseStreak}`} />
        </div>

        <div className="grid gap-4 lg:grid-cols-3">
          <Bars
            title="持仓时长"
            items={HOLD_ORDER.map((k) => ({
              label: HOLD_BUCKET_LABEL[k],
              n: p.hold.buckets[k],
              max: holdMax
            }))}
          />
          <Bars
            title="结构"
            items={[
              { label: '期权', n: p.mix.option, max: p.n },
              { label: '正股', n: p.mix.stock, max: p.n },
              { label: '多头', n: p.mix.long, max: p.n },
              { label: '空头', n: p.mix.short, max: p.n },
              { label: 'Call', n: p.mix.call, max: Math.max(1, p.mix.call + p.mix.put) },
              { label: 'Put', n: p.mix.put, max: Math.max(1, p.mix.call + p.mix.put) }
            ]}
          />
          <div>
            <p className="mb-2 text-[11px] text-muted-2">美东开仓时段</p>
            <div className="flex h-16 items-end gap-0.5">
              {Array.from({ length: 24 }, (_, hour) => {
                const n = p.session.hours.find((h) => h.hour === hour)?.n ?? 0
                const rth = hour >= 9 && hour < 16
                return (
                  <div
                    key={hour}
                    title={`${hour}:00 · ${n}`}
                    className={cn('min-w-0 flex-1 rounded-sm', rth ? 'bg-foreground/55' : 'bg-foreground/20')}
                    style={{ height: `${Math.max(n ? 12 : 4, (n / hourMax) * 100)}%` }}
                  />
                )
              })}
            </div>
            <div className="mt-2 flex gap-1">
              {p.session.weekdays.map((d) => (
                <div key={d.day} className="min-w-0 flex-1 text-center">
                  <div className="mx-auto h-8 w-full max-w-[1.5rem] overflow-hidden rounded-sm bg-surface-2">
                    <div
                      className="w-full bg-foreground/45"
                      style={{ height: `${(d.n / dayMax) * 100}%`, marginTop: `${(1 - d.n / dayMax) * 100}%` }}
                    />
                  </div>
                  <p className="mt-1 text-[10px] text-muted-2">{WEEKDAY_LABEL[d.day]}</p>
                </div>
              ))}
            </div>
            <p className="mt-2 text-[11px] text-muted-2">
              盘前 {p.session.pre} · 常规 {p.session.rth} · 盘后 {p.session.after}
            </p>
          </div>
        </div>

        {p.roots.length > 0 && (
          <div>
            <p className="mb-2 text-[11px] text-muted-2">标的（按已平例数）</p>
            <div className="flex flex-wrap gap-1.5">
              {p.roots.map((r) => (
                <span
                  key={r.root}
                  className="inline-flex items-center gap-1.5 rounded-full bg-surface-2 px-2.5 py-1 text-[11px]"
                >
                  <span className="font-medium">{r.root}</span>
                  <span className="tabular text-muted-2">{r.n}</span>
                  <span className={cn('tabular', gainClass(r.pnl))}>
                    {r.pnl > 0 ? '+' : ''}
                    {fmtMoney(r.pnl, p.currency)}
                  </span>
                </span>
              ))}
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  )
}

function holdShort(ms: number): string {
  if (!Number.isFinite(ms) || ms <= 0) return '—'
  const m = ms / 60_000
  if (m < 60) return `${Math.max(1, Math.round(m))}m`
  const h = m / 60
  if (h < 48) return `${h.toFixed(h < 10 ? 1 : 0)}h`
  return `${(h / 24).toFixed(1)}d`
}

function finite(v: number): string {
  if (!Number.isFinite(v)) return '—'
  return v.toFixed(2)
}

function Metric({
  label,
  value,
  tone
}: {
  label: string
  value: string
  tone?: 'up' | 'down'
}) {
  return (
    <div className="rounded-xl bg-bg px-3 py-2 ring-1 ring-border/70">
      <p className="text-[10px] text-muted-2">{label}</p>
      <p
        className={cn(
          'mt-0.5 text-sm font-semibold tabular',
          tone === 'up' ? 'text-up' : tone === 'down' ? 'text-down' : 'text-foreground'
        )}
      >
        {value}
      </p>
    </div>
  )
}

function Bars({
  title,
  items
}: {
  title: string
  items: { label: string; n: number; max: number }[]
}) {
  return (
    <div>
      <p className="mb-2 text-[11px] text-muted-2">{title}</p>
      <div className="space-y-1.5">
        {items.map((it) => (
          <div key={it.label} className="grid grid-cols-[4.5rem_1fr_2rem] items-center gap-2 text-[11px]">
            <span className="text-muted-2">{it.label}</span>
            <div className="h-1.5 overflow-hidden rounded-full bg-surface-2">
              <div
                className="h-full rounded-full bg-foreground/45"
                style={{ width: `${it.max ? (it.n / it.max) * 100 : 0}%` }}
              />
            </div>
            <span className="text-right tabular text-muted">{it.n}</span>
          </div>
        ))}
      </div>
    </div>
  )
}
