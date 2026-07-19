import { Card, CardContent, CardHeader, CardTitle } from './ui/Card'
import { Badge } from './ui/Badge'
import type { PositionPnl } from '../types'
import { cn, fmtMoney, fmtNum, fmtPct, gainClass } from '../lib/utils'

function Money({ v, currency }: { v: number; currency: string }) {
  return <span className={cn('tabular', gainClass(v))}>{v >= 0 ? '+' : ''}{fmtMoney(v, currency)}</span>
}

/** 仓位占比 = 该标的市值 / 组合总市值；盈亏率 = 未实现盈亏 / 成本。 */
function usePositionMetrics(positions: PositionPnl[], totalMarketValue: number) {
  const total = totalMarketValue > 0 ? totalMarketValue : 0
  return positions.map((p) => ({
    ...p,
    share: total > 0 ? p.marketValue / total : 0,
    pnlPct: Math.abs(p.costBasis) > 1e-9 ? p.unrealizedPnl / p.costBasis : 0,
  }))
}

export function PositionsTable({ positions, totalMarketValue }: { positions: PositionPnl[]; totalMarketValue: number }) {
  const rows = usePositionMetrics(positions, totalMarketValue)
  return (
    <Card className="animate-fade-up">
      <CardHeader>
        <div>
          <CardTitle>持仓明细</CardTitle>
          <p className="mt-0.5 text-[11px] text-muted-2">数量/均价 · Flex · 市价可由 Gateway 刷新</p>
        </div>
        <Badge tone="neutral">{positions.length} 个标的</Badge>
      </CardHeader>
      <CardContent className="p-0">
        {/* 桌面端：表格 */}
        <div className="hidden overflow-x-auto md:block">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-y border-border bg-surface-2/50 text-left text-xs uppercase tracking-wide text-muted">
                <th className="px-5 py-3 font-medium">标的</th>
                <th className="px-5 py-3 font-medium">来源</th>
                <th className="px-5 py-3 text-right font-medium">数量</th>
                <th className="px-5 py-3 text-right font-medium">均价</th>
                <th className="px-5 py-3 text-right font-medium">市价</th>
                <th className="px-5 py-3 text-right font-medium">市值</th>
                <th className="px-5 py-3 text-right font-medium">占比</th>
                <th className="px-5 py-3 text-right font-medium">未实现</th>
                <th className="px-5 py-3 text-right font-medium">盈亏率</th>
                <th className="px-5 py-3 text-right font-medium">已实现</th>
                <th className="px-5 py-3 text-right font-medium">成本</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((p) => (
                <tr
                  key={p.id}
                  className="border-b border-border/60 transition-colors last:border-0 hover:bg-surface-2/40"
                >
                  <td className="px-5 py-3.5">
                    <div className="flex items-center gap-2.5">
                      <span
                        className={cn(
                          'h-8 w-1 rounded-full',
                          p.unrealizedPnl >= 0 ? 'bg-up/70' : 'bg-down/70'
                        )}
                      />
                      <div>
                        <div className="font-semibold text-foreground">{p.symbol}</div>
                        <Badge tone="neutral" className="mt-1">{p.assetType}</Badge>
                      </div>
                    </div>
                  </td>
                  <td className="px-5 py-3.5">
                    <div className="flex flex-col gap-0.5 text-[10px] text-muted-2">
                      <span>仓 {p.source ?? 'Flex'}</span>
                      <span>价 {p.priceSource ?? 'Flex'}</span>
                    </div>
                  </td>
                  <td className="px-5 py-3.5 text-right tabular">{fmtNum(p.quantity)}</td>
                  <td className="px-5 py-3.5 text-right tabular">{fmtMoney(p.avgCost, p.currency)}</td>
                  <td className="px-5 py-3.5 text-right tabular">{fmtMoney(p.marketPrice, p.currency)}</td>
                  <td className="px-5 py-3.5 text-right tabular font-medium">{fmtMoney(p.marketValue, p.currency)}</td>
                  <td className="px-5 py-3.5 text-right tabular">
                    <div className="flex items-center justify-end gap-2">
                      <span className="h-1.5 w-10 overflow-hidden rounded-full bg-surface-2">
                        <span className="block h-full rounded-full bg-foreground/40" style={{ width: `${Math.min(p.share * 100, 100)}%` }} />
                      </span>
                      <span className="w-12 text-right font-medium">{fmtPct(p.share)}</span>
                    </div>
                  </td>
                  <td className="px-5 py-3.5 text-right tabular font-semibold"><Money v={p.unrealizedPnl} currency={p.currency} /></td>
                  <td className="px-5 py-3.5 text-right tabular font-semibold"><span className={cn('tabular', gainClass(p.pnlPct))}>{fmtPct(p.pnlPct)}</span></td>
                  <td className="px-5 py-3.5 text-right tabular font-semibold"><Money v={p.realizedPnl} currency={p.currency} /></td>
                  <td className="px-5 py-3.5 text-right tabular text-muted">{fmtMoney(p.costBasis, p.currency)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {/* 移动端：卡片列表 */}
        <ul className="divide-y divide-border/60 md:hidden">
          {rows.map((p) => (
            <li key={p.id} className="p-4">
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-2.5">
                  <span
                    className={cn(
                      'h-9 w-1 rounded-full',
                      p.unrealizedPnl >= 0 ? 'bg-up/70' : 'bg-down/70'
                    )}
                  />
                  <div>
                    <div className="font-semibold text-foreground">{p.symbol}</div>
                    <Badge tone="neutral" className="mt-1">{p.assetType}</Badge>
                  </div>
                </div>
                <div className="text-right">
                  <div className="text-xs text-muted">市值</div>
                  <div className="font-semibold tabular">{fmtMoney(p.marketValue, p.currency)}</div>
                  <div className="mt-0.5 text-xs text-muted">
                    占比 <span className="font-medium text-foreground">{fmtPct(p.share)}</span>
                  </div>
                </div>
              </div>

              <div className="mt-3 grid grid-cols-2 gap-x-4 gap-y-2.5 text-sm">
                <Field label="仓位来源" value={p.source ?? 'Flex'} />
                <Field label="市价来源" value={p.priceSource ?? 'Flex'} />
                <Field label="数量" value={fmtNum(p.quantity)} />
                <Field label="均价" value={fmtMoney(p.avgCost, p.currency)} />
                <Field label="市价" value={fmtMoney(p.marketPrice, p.currency)} />
                <Field label="成本" value={fmtMoney(p.costBasis, p.currency)} />
                <Field label="未实现" value={`${p.unrealizedPnl >= 0 ? '+' : ''}${fmtMoney(p.unrealizedPnl, p.currency)}`} className={gainClass(p.unrealizedPnl)} />
                <Field label="盈亏率" value={fmtPct(p.pnlPct)} className={gainClass(p.pnlPct)} />
                <Field label="已实现" value={`${p.realizedPnl >= 0 ? '+' : ''}${fmtMoney(p.realizedPnl, p.currency)}`} className={gainClass(p.realizedPnl)} />
              </div>
            </li>
          ))}
        </ul>
      </CardContent>
    </Card>
  )
}

function Field({ label, value, className }: { label: string; value: string; className?: string }) {
  return (
    <div className="flex items-center justify-between">
      <span className="text-muted">{label}</span>
      <span className={cn('tabular font-medium', className ?? 'text-foreground')}>{value}</span>
    </div>
  )
}
