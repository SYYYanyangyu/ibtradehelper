import { useCallback, useEffect, useId, useMemo, useState, type ReactNode } from 'react'
import { ArrowDownRight, ArrowUpRight, ChevronDown, TrendingUp, X } from 'lucide-react'
import { cn, fmtMoney, fmtNum, fmtPct, gainClass } from '../lib/utils'
import type { DailyPnlPoint, PositionPnl } from '../types'

interface Props {
  data: DailyPnlPoint[]
  totalMarketValue: number
  totalUnrealizedPnl: number
  currency: string
  /** 嵌在今日盈亏底部；列表精简，点击行可看详情 */
  positions?: PositionPnl[]
  contribution?: ReactNode
}

/**
 * 今日盈亏主卡：净值摘要 + 个股贡献 + 精简持仓（可展开/弹层看全量字段）。
 */
export function TodayPnlHero({
  data,
  totalMarketValue,
  totalUnrealizedPnl,
  currency,
  positions = [],
  contribution
}: Props) {
  const today = data?.[data.length - 1]
  const v = today?.dailyPnl ?? 0
  const pct = today?.dailyPnlPct ?? 0

  const officialNav = today?.netLiquidation ?? null
  const positionMv = totalMarketValue ?? 0
  const unrealized = totalUnrealizedPnl ?? 0

  const meaningful = (data ?? []).filter((d) => Math.abs(d.netLiquidation) > 1)
  const firstMeaningful = meaningful[0] ?? null
  const peakPoint = meaningful.reduce<(typeof meaningful)[0] | null>((best, d) => {
    if (!best || d.netLiquidation > best.netLiquidation) return d
    return best
  }, null)

  const baselineNav = firstMeaningful?.netLiquidation ?? null
  const baselineDate = firstMeaningful?.date
  const peakNav = peakPoint?.netLiquidation ?? null
  const peakDate = peakPoint?.date

  const navVsBaseline =
    officialNav != null && baselineNav != null ? officialNav - baselineNav : null
  const navVsPeak = officialNav != null && peakNav != null ? officialNav - peakNav : null

  const isUp = v >= 0
  const flat = v === 0
  const noBaseline = (data?.length ?? 0) <= 1

  const localToday = useMemo(() => {
    const d = new Date()
    const local = new Date(d.getTime() - d.getTimezoneOffset() * 60000)
    return local.toISOString().slice(0, 10)
  }, [])
  const isStale = !!today?.date && today.date !== localToday

  const holdings = useMemo(
    () => [...positions].sort((a, b) => Math.abs(b.marketValue) - Math.abs(a.marketValue)),
    [positions]
  )
  const mvTotal = useMemo(
    () => positionMv > 0 ? positionMv : holdings.reduce((s, p) => s + Math.abs(p.marketValue), 0),
    [positionMv, holdings]
  )

  const [listOpen, setListOpen] = useState(false)
  const [expandedId, setExpandedId] = useState<number | null>(null)
  const [showAllDetail, setShowAllDetail] = useState(false)
  const closeAllDetail = useCallback(() => setShowAllDetail(false), [])

  const topSymbols = holdings
    .slice(0, 2)
    .map((p) => p.symbol.replace(/\s+/g, ' ').trim())
    .join(' · ')

  return (
    <div className="relative rounded-xl border border-border bg-surface shadow-sm animate-fade-up">
      <div className="p-4 sm:p-6">
        <div className={cn('relative grid gap-5 lg:grid-cols-[1.45fr_minmax(0,1fr)] lg:gap-8')}>
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-2">
              <TrendingUp size={14} className="text-muted-2" />
              <span className="text-sm font-medium text-muted">Day P&L</span>
              {today?.date && (
                <span className="rounded-md bg-surface-2 px-2 py-0.5 text-xs text-muted-2">
                  {today.date}
                  {isStale ? ' · Last session' : ''}
                </span>
              )}
            </div>

            <div className={cn('mt-2 flex flex-wrap items-baseline gap-2.5', gainClass(v))}>
              <span className="text-[1.75rem] font-semibold leading-none tabular tracking-tight sm:text-4xl">
                {v > 0 ? '+' : ''}
                {fmtMoney(v, currency)}
              </span>
              <span
                className={cn(
                  'inline-flex items-center gap-0.5 rounded-md px-2 py-0.5 text-xs font-medium tabular',
                  isUp ? 'bg-up/8 text-up' : 'bg-down/8 text-down'
                )}
              >
                {isUp ? <ArrowUpRight size={14} /> : <ArrowDownRight size={14} />}
                {fmtPct(pct, false)}
              </span>
            </div>

            <p className="mt-2 text-xs text-muted-2">
              {noBaseline
                ? 'Need 2+ NAV days to compute day P&L'
                : flat
                  ? 'Market closed · No change'
                  : 'Official daily NAV'}
            </p>

            <div className="mt-4 grid grid-cols-1 gap-3 border-t border-border/60 pt-4 sm:grid-cols-3 sm:gap-0 sm:divide-x sm:divide-border/60">
              <Metric
                label="Position MV"
                value={fmtMoney(positionMv, currency)}
                hint={
                  officialNav != null
                    ? `NAV ${fmtMoney(officialNav, currency)}`
                    : 'Open marks'
                }
              />
              <Metric
                label="Unrealized"
                value={`${unrealized > 0 ? '+' : ''}${fmtMoney(unrealized, currency)}`}
                valueClass={gainClass(unrealized)}
                hint="Open · Flex"
              />
              <Metric
                label="vs Peak"
                value={
                  navVsPeak == null
                    ? '—'
                    : `${navVsPeak > 0 ? '+' : ''}${fmtMoney(navVsPeak, currency)}`
                }
                valueClass={navVsPeak == null ? 'text-muted' : gainClass(navVsPeak)}
                hint={
                  peakNav == null
                    ? 'No NAV history'
                    : `Peak ${fmtMoney(peakNav, currency)}${peakDate ? ` · ${peakDate}` : ''}`
                }
              />
            </div>

            {navVsBaseline != null && baselineNav != null && (
              <p className="mt-3 text-xs text-muted-2">
                vs First NAV{' '}
                <span className={cn('tabular font-medium', gainClass(navVsBaseline))}>
                  {navVsBaseline > 0 ? '+' : ''}
                  {fmtMoney(navVsBaseline, currency)}
                </span>
                <span className="text-muted-2/80">
                  {' '}
                  · from {fmtMoney(baselineNav, currency)}
                  {baselineDate ? ` (${baselineDate})` : ''}
                </span>
              </p>
            )}
          </div>

          {contribution && (
            <div className="min-w-0 border-t border-border/70 pt-5 lg:border-l lg:border-t-0 lg:pl-8 lg:pt-0">
              {contribution}
            </div>
          )}
        </div>
      </div>

      {/* 默认折叠：一行摘要；展开后才是精简列表 */}
      <div className="border-t border-border/50">
        <button
          type="button"
          onClick={() => {
            setListOpen((o) => !o)
            if (listOpen) setExpandedId(null)
          }}
          className="flex w-full items-center gap-3 px-5 py-3 text-left transition hover:bg-surface-2/50 sm:px-6"
          aria-expanded={listOpen}
        >
          <div className="min-w-0 flex-1">
            <div className="flex flex-wrap items-baseline gap-x-2 gap-y-0.5">
              <span className="text-sm font-medium text-foreground">Positions</span>
              <span className="text-xs tabular text-muted-2">
                {holdings.length === 0
                  ? 'Flat'
                  : `${holdings.length} symbols · ${fmtMoney(positionMv, currency)}`}
              </span>
              {holdings.length > 0 && (
                <span className={cn('text-xs tabular font-medium', gainClass(unrealized))}>
                  {unrealized > 0 ? '+' : ''}
                  {fmtMoney(unrealized, currency)}
                </span>
              )}
            </div>
            {holdings.length > 0 && !listOpen && (
              <p className="mt-0.5 truncate text-[11px] text-muted-2">{topSymbols}</p>
            )}
          </div>
          <ChevronDown
            size={16}
            className={cn('shrink-0 text-muted-2 transition-transform duration-200', listOpen && 'rotate-180')}
          />
        </button>

        {listOpen && (
          <div className="border-t border-border/40 px-3 pb-3 pt-1 sm:px-4">
            {holdings.length === 0 ? (
              <p className="px-2 py-4 text-center text-sm text-muted-2">当前空仓</p>
            ) : (
              <>
                <ul className="max-h-[280px] space-y-0.5 overflow-y-auto overscroll-contain [-webkit-overflow-scrolling:touch]">
                  {holdings.map((p) => {
                    const share = mvTotal > 0 ? Math.abs(p.marketValue) / mvTotal : 0
                    const pnlPct =
                      Math.abs(p.costBasis) > 1e-9 ? p.unrealizedPnl / p.costBasis : 0
                    const open = expandedId === p.id
                    return (
                      <li key={p.id}>
                        <button
                          type="button"
                          onClick={() => setExpandedId(open ? null : p.id)}
                          className={cn(
                            'flex w-full items-center gap-3 rounded-lg px-2.5 py-2 text-left transition',
                            open ? 'bg-surface-2' : 'hover:bg-surface-2/70'
                          )}
                          aria-expanded={open}
                        >
                          <span
                            className={cn(
                              'h-1.5 w-1.5 shrink-0 rounded-full',
                              p.unrealizedPnl >= 0 ? 'bg-up' : 'bg-down'
                            )}
                          />
                          <span className="min-w-0 flex-1 truncate text-sm font-medium text-foreground">
                            {p.symbol}
                          </span>
                          <span className="hidden tabular text-xs text-muted-2 sm:inline">
                            {fmtNum(p.quantity)}
                          </span>
                          <span className="w-[5.5rem] shrink-0 text-right tabular text-sm text-foreground sm:w-28">
                            {fmtMoney(p.marketValue, p.currency)}
                          </span>
                          <span
                            className={cn(
                              'w-[5.5rem] shrink-0 text-right tabular text-sm font-medium sm:w-28',
                              gainClass(p.unrealizedPnl)
                            )}
                          >
                            {p.unrealizedPnl > 0 ? '+' : ''}
                            {fmtMoney(p.unrealizedPnl, p.currency)}
                          </span>
                          <ChevronDown
                            size={14}
                            className={cn(
                              'shrink-0 text-muted-2/80 transition-transform duration-200',
                              open && 'rotate-180'
                            )}
                          />
                        </button>

                        {open && (
                          <div className="mx-1 mb-1.5 mt-0.5 rounded-lg bg-surface-2/80 px-3 py-2.5">
                            <PositionDetailGrid p={p} share={share} pnlPct={pnlPct} />
                          </div>
                        )}
                      </li>
                    )
                  })}
                </ul>
                <div className="mt-2 flex justify-end px-1">
                  <button
                    type="button"
                    onClick={() => setShowAllDetail(true)}
                    className="text-[11px] font-medium text-muted transition hover:text-foreground"
                  >
                    打开对照表 →
                  </button>
                </div>
              </>
            )}
          </div>
        )}
      </div>

      {showAllDetail && (
        <PositionsDetailModal
          positions={holdings}
          totalMarketValue={mvTotal}
          onClose={closeAllDetail}
        />
      )}
    </div>
  )
}

function PositionDetailGrid({
  p,
  share,
  pnlPct
}: {
  p: PositionPnl
  share: number
  pnlPct: number
}) {
  const cells: { label: string; value: string; className?: string }[] = [
    { label: '仓位来源', value: p.source ?? 'Flex' },
    { label: '市价来源', value: p.priceSource ?? 'Flex' },
    { label: '数量', value: fmtNum(p.quantity) },
    { label: '均价', value: fmtMoney(p.avgCost, p.currency) },
    { label: '市价', value: fmtMoney(p.marketPrice, p.currency) },
    { label: '市值', value: fmtMoney(p.marketValue, p.currency) },
    { label: '成本', value: fmtMoney(p.costBasis, p.currency) },
    { label: '占比', value: fmtPct(share, true) },
    {
      label: '未实现',
      value: `${p.unrealizedPnl >= 0 ? '+' : ''}${fmtMoney(p.unrealizedPnl, p.currency)}`,
      className: gainClass(p.unrealizedPnl)
    },
    { label: '盈亏率', value: fmtPct(pnlPct, true), className: gainClass(pnlPct) },
    {
      label: '已实现',
      value: `${p.realizedPnl >= 0 ? '+' : ''}${fmtMoney(p.realizedPnl, p.currency)}`,
      className: gainClass(p.realizedPnl)
    },
    { label: '账户', value: p.accountId }
  ]

  return (
    <dl className="grid grid-cols-2 gap-x-3 gap-y-2 text-[12px] sm:grid-cols-3 sm:gap-x-5 sm:text-[13px] lg:grid-cols-4">
      {cells.map((c) => (
        <div key={c.label} className="flex min-w-0 flex-col gap-0.5">
          <dt className="shrink-0 text-[10px] uppercase tracking-wide text-muted-2">{c.label}</dt>
          <dd className={cn('break-all tabular font-medium text-foreground', c.className)}>
            {c.value}
          </dd>
        </div>
      ))}
    </dl>
  )
}

function PositionsDetailModal({
  positions,
  totalMarketValue,
  onClose
}: {
  positions: PositionPnl[]
  totalMarketValue: number
  onClose: () => void
}) {
  const titleId = useId()
  const total = totalMarketValue > 0 ? totalMarketValue : 0

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)

    // iOS：仅 overflow:hidden 无法锁滚动，且会和外层抢手势导致「卡住」
    const scrollY = window.scrollY
    const body = document.body
    const prev = {
      position: body.style.position,
      top: body.style.top,
      left: body.style.left,
      right: body.style.right,
      width: body.style.width,
      overflow: body.style.overflow
    }
    body.style.position = 'fixed'
    body.style.top = `-${scrollY}px`
    body.style.left = '0'
    body.style.right = '0'
    body.style.width = '100%'
    body.style.overflow = 'hidden'

    return () => {
      window.removeEventListener('keydown', onKey)
      body.style.position = prev.position
      body.style.top = prev.top
      body.style.left = prev.left
      body.style.right = prev.right
      body.style.width = prev.width
      body.style.overflow = prev.overflow
      window.scrollTo(0, scrollY)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  return (
    <div
      className="fixed inset-0 z-50 flex items-end justify-center bg-foreground/40 p-0 sm:items-center sm:p-6"
      onClick={onClose}
      role="presentation"
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        className="flex h-[min(92dvh,100%)] w-full max-w-5xl flex-col overflow-hidden rounded-t-2xl border border-border bg-surface shadow-lg sm:h-auto sm:max-h-[88dvh] sm:rounded-xl"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex shrink-0 items-center justify-between gap-3 border-b border-border px-4 py-3 sm:px-5">
          <div className="min-w-0">
            <h2 id={titleId} className="text-sm font-semibold text-foreground">
              持仓全部详情
            </h2>
            <p className="mt-0.5 truncate text-[11px] text-muted-2">
              {positions.length} 个标的 · 数量/均价 Flex · 市价可 Gateway 刷新
            </p>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="grid h-9 w-9 shrink-0 place-items-center rounded-md text-muted-2 transition hover:bg-surface-2 hover:text-foreground"
            aria-label="关闭"
          >
            <X size={16} />
          </button>
        </div>

        {/* 固定高度滚动区，避免移动端 flex 子项高度塌陷导致无法滑动 */}
        <div className="min-h-0 flex-1 overflow-y-auto overscroll-contain [-webkit-overflow-scrolling:touch]">
          <div className="hidden md:block">
            <table className="w-full text-sm">
              <thead className="sticky top-0 z-[1] bg-surface-2 text-left text-xs uppercase tracking-wide text-muted">
                <tr className="border-b border-border">
                  {[
                    '标的',
                    '来源',
                    '数量',
                    '均价',
                    '市价',
                    '市值',
                    '占比',
                    '未实现',
                    '盈亏率',
                    '已实现',
                    '成本'
                  ].map((h) => (
                    <th
                      key={h}
                      className={cn(
                        'px-3 py-2.5 font-medium whitespace-nowrap',
                        h !== '标的' && h !== '来源' && 'text-right'
                      )}
                    >
                      {h}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {positions.map((p) => {
                  const share = total > 0 ? p.marketValue / total : 0
                  const pnlPct = Math.abs(p.costBasis) > 1e-9 ? p.unrealizedPnl / p.costBasis : 0
                  return (
                    <tr key={p.id} className="border-b border-border/60 last:border-0">
                      <td className="px-3 py-2.5">
                        <div className="font-medium text-foreground">{p.symbol}</div>
                        <div className="text-[11px] text-muted-2">{p.assetType}</div>
                      </td>
                      <td className="px-3 py-2.5 text-[11px] text-muted-2 whitespace-nowrap">
                        <div>仓 {p.source ?? 'Flex'}</div>
                        <div>价 {p.priceSource ?? 'Flex'}</div>
                      </td>
                      <td className="px-3 py-2.5 text-right tabular">{fmtNum(p.quantity)}</td>
                      <td className="px-3 py-2.5 text-right tabular">
                        {fmtMoney(p.avgCost, p.currency)}
                      </td>
                      <td className="px-3 py-2.5 text-right tabular">
                        {fmtMoney(p.marketPrice, p.currency)}
                      </td>
                      <td className="px-3 py-2.5 text-right tabular font-medium">
                        {fmtMoney(p.marketValue, p.currency)}
                      </td>
                      <td className="px-3 py-2.5 text-right tabular">{fmtPct(share, true)}</td>
                      <td className={cn('px-3 py-2.5 text-right tabular font-medium', gainClass(p.unrealizedPnl))}>
                        {p.unrealizedPnl >= 0 ? '+' : ''}
                        {fmtMoney(p.unrealizedPnl, p.currency)}
                      </td>
                      <td className={cn('px-3 py-2.5 text-right tabular', gainClass(pnlPct))}>
                        {fmtPct(pnlPct, true)}
                      </td>
                      <td className={cn('px-3 py-2.5 text-right tabular', gainClass(p.realizedPnl))}>
                        {p.realizedPnl >= 0 ? '+' : ''}
                        {fmtMoney(p.realizedPnl, p.currency)}
                      </td>
                      <td className="px-3 py-2.5 text-right tabular text-muted">
                        {fmtMoney(p.costBasis, p.currency)}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>

          <ul className="divide-y divide-border pb-[env(safe-area-inset-bottom)] md:hidden">
            {positions.map((p) => {
              const share = total > 0 ? p.marketValue / total : 0
              const pnlPct = Math.abs(p.costBasis) > 1e-9 ? p.unrealizedPnl / p.costBasis : 0
              return (
                <li key={p.id} className="px-4 py-3">
                  <p className="truncate font-medium text-foreground">{p.symbol}</p>
                  <div className="mt-2">
                    <PositionDetailGrid p={p} share={share} pnlPct={pnlPct} />
                  </div>
                </li>
              )
            })}
          </ul>
        </div>
      </div>
    </div>
  )
}

function Metric({
  label,
  value,
  hint,
  valueClass
}: {
  label: string
  value: string
  hint: string
  valueClass?: string
}) {
  return (
    <div className="min-w-0 sm:px-4 first:sm:pl-0 last:sm:pr-0">
      <p className="text-[11px] font-medium uppercase tracking-wide text-muted-2">{label}</p>
      <p className={cn('mt-1 text-lg font-semibold tabular tracking-tight', valueClass)}>{value}</p>
      <p className="mt-1 truncate text-[11px] leading-snug text-muted-2" title={hint}>
        {hint}
      </p>
    </div>
  )
}
