export function cn(...classes: (string | false | null | undefined)[]): string {
  return classes.filter(Boolean).join(' ')
}

export function fmtMoney(v: number, currency = 'USD'): string {
  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency,
    maximumFractionDigits: 2
  }).format(v)
}

/** 紧凑金额：1.2M / 845K / 1.2B，用于大数字更克制美观。 */
export function fmtCompact(v: number, currency = 'USD'): string {
  const abs = Math.abs(v)
  const sign = v < 0 ? '-' : ''
  if (abs >= 1_000_000_000)
    return `${sign}${currency === 'USD' ? '$' : ''}${(abs / 1_000_000_000).toFixed(2)}B`
  if (abs >= 1_000_000)
    return `${sign}${currency === 'USD' ? '$' : ''}${(abs / 1_000_000).toFixed(2)}M`
  if (abs >= 1_000)
    return `${sign}${currency === 'USD' ? '$' : ''}${(abs / 1_000).toFixed(1)}K`
  return fmtMoney(v, currency)
}

export function fmtNum(v: number): string {
  return new Intl.NumberFormat('en-US', { maximumFractionDigits: 4 }).format(v)
}

/** 百分比：输入 0.1234 -> "+12.34%"；可指定是否已为百分数。 */
export function fmtPct(v: number, asFraction = true, digits = 2): string {
  const p = asFraction ? v * 100 : v
  const sign = p > 0 ? '+' : ''
  return `${sign}${p.toFixed(digits)}%`
}

export function gainClass(v: number): string {
  if (v > 0) return 'text-up'
  if (v < 0) return 'text-down'
  return 'text-muted'
}

/** 全局统一的输入框样式 */
export const inputCls =
  'w-full rounded-lg border border-border bg-surface px-3 py-2 text-sm text-foreground outline-none transition placeholder:text-muted-2 focus:border-primary/50 focus:ring-2 focus:ring-primary/15'

/** 全局统一的表格表头样式 */
export const thCls = 'pb-2 px-3 text-[11px] font-medium uppercase tracking-wider text-muted-2'

/** 全局统一的表格行样式 */
export const trCls = 'border-t border-border/50 transition-colors hover:bg-surface-2/40'

// ---- 自动交易展示辅助（与组合看板配色一致）----
type Tone = 'neutral' | 'primary' | 'up' | 'down'

export interface Meta {
  label: string
  tone: Tone
}

/** 策略信号：Buy=买入(up) / Sell=卖出(down) / 其它=持有(neutral)。 */
export function signalMeta(sig?: string | null): Meta {
  if (sig === 'Buy') return { label: '买入', tone: 'up' }
  if (sig === 'Sell') return { label: '卖出', tone: 'down' }
  return { label: '持有', tone: 'neutral' }
}

/** 交易模式：Live=实盘(down) / Paper=模拟盘(primary)。 */
export function modeMeta(mode?: string | null): Meta {
  if (mode === 'Live') return { label: '实盘', tone: 'down' }
  return { label: '模拟盘', tone: 'primary' }
}

/** 演练模式：true=演练(primary) / false=实盘(down)。 */
export function dryRunMeta(dryRun?: boolean | null): Meta {
  if (dryRun) return { label: '演练', tone: 'primary' }
  return { label: '实盘', tone: 'down' }
}

/** 审计事件类型 → 标签与配色。 */
export function eventTypeMeta(type?: string | null): Meta {
  switch (type) {
    case 'Signal':
      return { label: '信号', tone: 'primary' }
    case 'OrderPlaced':
      return { label: '已下单', tone: 'up' }
    case 'DryRun':
      return { label: '演练', tone: 'primary' }
    case 'RiskBlocked':
    case 'OrderRejected':
    case 'Error':
      return { label: '拦截/错误', tone: 'down' }
    default:
      return { label: '信息', tone: 'neutral' }
  }
}

/** 下单类型中文。 */
export function orderTypeLabel(type?: string | null): string {
  return type === 'Limit' ? '限价' : '市价'
}

/** ISO 时间 → 本地可读时间。 */
export function fmtTime(t?: string | null): string {
  if (!t) return '—'
  const d = new Date(t)
  if (isNaN(d.getTime())) return '—'
  return d.toLocaleString('zh-CN', { hour12: false })
}
