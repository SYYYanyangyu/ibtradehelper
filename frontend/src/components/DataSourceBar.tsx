import type { DataFeedStatusItem } from '../lib/api'
import { cn } from '../lib/utils'

function fmtAgo(iso?: string | null): string {
  if (!iso) return '未同步'
  const t = new Date(iso).getTime()
  if (Number.isNaN(t)) return '未同步'
  const sec = Math.max(0, Math.floor((Date.now() - t) / 1000))
  if (sec < 60) return `${sec}s`
  if (sec < 3600) return `${Math.floor(sec / 60)}m`
  if (sec < 86400) return `${Math.floor(sec / 3600)}h`
  return `${Math.floor(sec / 86400)}d`
}

interface Props {
  feeds: DataFeedStatusItem[]
  tradeEnv?: string
  className?: string
}

/** 只读数据源状态：轻量文字，不抢操作区。 */
export function DataSourceBar({ feeds, tradeEnv, className }: Props) {
  const flex = feeds.filter((f) => f.feed === 'Flex')
  const flexForEnv =
    tradeEnv && tradeEnv !== ''
      ? flex.find((f) => f.environment === tradeEnv) ?? flex[0]
      : flex.sort((a, b) => +new Date(b.lastSuccessUtc) - +new Date(a.lastSuccessUtc))[0]
  const gateway = feeds.find((f) => f.feed === 'Gateway')

  const flexOk = !!flexForEnv?.lastSuccessUtc
  const gwOk = !!gateway?.lastSuccessUtc

  return (
    <div
      className={cn(
        'flex min-w-0 flex-wrap items-center gap-x-3 gap-y-1 text-[11px] tabular text-muted-2 sm:text-[12px]',
        className
      )}
      title="关网关不影响 Flex 历史；关网关后无实时价/下单/K线"
    >
      <span className="inline-flex max-w-full items-center gap-1.5" title="成交、日净值、持仓 · Flex">
        <span className={cn('h-1.5 w-1.5 shrink-0 rounded-full', flexOk ? 'bg-up' : 'bg-muted-2')} />
        <span className="shrink-0 text-muted">Flex</span>
        <span className="min-w-0 truncate">
          {flexForEnv
            ? [flexForEnv.environment, fmtAgo(flexForEnv.lastSuccessUtc)].filter(Boolean).join(' · ')
            : '—'}
        </span>
      </span>
      <span className="hidden h-3 w-px shrink-0 bg-border sm:block" aria-hidden />
      <span className="inline-flex max-w-full items-center gap-1.5" title="实时市价 / 下单 · Gateway">
        <span className={cn('h-1.5 w-1.5 shrink-0 rounded-full', gwOk ? 'bg-up' : 'bg-muted-2')} />
        <span className="shrink-0 text-muted">GW</span>
        <span className="min-w-0 truncate">
          {gateway
            ? [gateway.connectedPort, fmtAgo(gateway.lastSuccessUtc)].filter(Boolean).join(' · ')
            : '—'}
        </span>
      </span>
    </div>
  )
}
