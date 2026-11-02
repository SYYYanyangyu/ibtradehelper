import type { ReactNode } from 'react'
import {
  Wallet,
  Search,
  Sun,
  Moon,
  Cable,
  Radio,
  CloudDownload,
  ChevronDown
} from 'lucide-react'
import { cn } from '../lib/utils'
import { DataSourceBar } from './DataSourceBar'
import type { TradeEnvSelection, DataFeedStatusItem } from '../lib/api'

export type AppView = 'dashboard' | 'trades' | 'backtest' | 'orders' | 'autotrade' | 'options' | 'brief'

const NAV: { id: AppView; label: string }[] = [
  { id: 'dashboard', label: 'Dashboard' },
  { id: 'trades', label: 'Trades' },
  { id: 'backtest', label: 'Backtest' },
  { id: 'orders', label: 'Trading' },
  { id: 'autotrade', label: 'Quant' },
  { id: 'options', label: 'Options' },
  { id: 'brief', label: '简报' }
]

const ENV: { id: TradeEnvSelection; label: string }[] = [
  { id: 'Paper', label: 'Paper' },
  { id: 'Live', label: 'Live' },
  { id: '', label: 'All' }
]

interface Props {
  view: AppView
  onViewChange: (v: AppView) => void
  theme: 'light' | 'dark'
  onToggleTheme: () => void
  query: string
  onQueryChange: (q: string) => void
  onSearchSubmit: () => void
  onSyncFlex: () => void
  syncingFlex: boolean
  onSyncGateway: () => void
  syncingGateway: boolean
  liveOn: boolean
  togglingLive: boolean
  onToggleLive: () => void
  tradeEnv: TradeEnvSelection
  onEnvChange: (env: TradeEnvSelection) => void
  accounts: { id: string; label: string }[]
  selected: string
  onSelectAccount: (label: string) => void
  feedStatuses: DataFeedStatusItem[]
}

function ToolBtn({
  label,
  onClick,
  disabled,
  active,
  children
}: {
  label: string
  onClick: () => void
  disabled?: boolean
  active?: boolean
  children: ReactNode
}) {
  return (
    <button
      type="button"
      title={label}
      aria-label={label}
      disabled={disabled}
      onClick={onClick}
      className={cn(
        'grid h-8 w-8 place-items-center rounded-md transition disabled:opacity-40',
        active
          ? 'bg-up/12 text-up'
          : 'text-muted hover:bg-bg hover:text-foreground'
      )}
    >
      {children}
    </button>
  )
}

function AccountSelect({
  accounts,
  selected,
  onSelectAccount,
  className
}: {
  accounts: { id: string; label: string }[]
  selected: string
  onSelectAccount: (label: string) => void
  className?: string
}) {
  return (
    <div className={cn('relative min-w-0', className)}>
      <select
        aria-label="选择账户"
        value={selected === '' ? '__all__' : selected}
        onChange={(e) => {
          const v = e.target.value
          onSelectAccount(v === '__all__' ? '全部' : v)
        }}
        className="h-8 w-full appearance-none truncate rounded-lg border-0 bg-surface py-0 pl-2.5 pr-7 text-[12px] font-medium tabular text-foreground outline-none ring-1 ring-border/70 focus:ring-foreground/25"
      >
        <option value="__all__">All accounts ({accounts.length})</option>
        {accounts.map((a) => (
          <option key={a.id} value={a.id}>
            {a.label}
          </option>
        ))}
      </select>
      <ChevronDown
        size={14}
        className="pointer-events-none absolute right-2 top-1/2 -translate-y-1/2 text-muted-2"
      />
    </div>
  )
}

function AccountChips({
  accounts,
  selected,
  onSelectAccount,
  className
}: {
  accounts: { id: string; label: string }[]
  selected: string
  onSelectAccount: (label: string) => void
  className?: string
}) {
  return (
    <div
      className={cn(
        'flex min-w-0 items-center gap-1 overflow-x-auto overscroll-x-contain [-ms-overflow-style:none] [scrollbar-width:none] [&::-webkit-scrollbar]:hidden',
        className
      )}
    >
      <button
        type="button"
        onClick={() => onSelectAccount('全部')}
        className={cn(
          'shrink-0 rounded-lg px-2.5 py-1 text-[12px] font-medium transition',
          selected === ''
            ? 'bg-foreground text-bg'
            : 'text-muted hover:text-foreground'
        )}
      >
        All
      </button>
      {accounts.map((a) => (
        <button
          key={a.id}
          type="button"
          onClick={() => onSelectAccount(a.id)}
          className={cn(
            'shrink-0 rounded-lg px-2.5 py-1 text-[12px] font-medium tabular transition',
            selected === a.id
              ? 'bg-foreground text-bg'
              : 'text-muted hover:text-foreground'
          )}
          title={a.id}
        >
          {a.label}
        </button>
      ))}
    </div>
  )
}

export function AppHeader({
  view,
  onViewChange,
  theme,
  onToggleTheme,
  query,
  onQueryChange,
  onSearchSubmit,
  onSyncFlex,
  syncingFlex,
  onSyncGateway,
  syncingGateway,
  liveOn,
  togglingLive,
  onToggleLive,
  tradeEnv,
  onEnvChange,
  accounts,
  selected,
  onSelectAccount,
  feedStatuses
}: Props) {
  return (
    <header className="sticky top-0 z-20 border-b border-border/70 bg-surface/85 backdrop-blur-xl">
      <div className="mx-auto flex h-12 max-w-7xl items-center gap-2 px-3 sm:gap-5 sm:px-6">
        <div className="flex shrink-0 items-center gap-2.5">
          <div className="grid h-8 w-8 place-items-center rounded-lg bg-foreground text-bg">
            <Wallet size={15} strokeWidth={2} />
          </div>
          <span className="text-[15px] font-semibold tracking-tight max-md:sr-only">IBTrade</span>
        </div>

        {/* 移动端：四等分保证全显示；桌面：原先横排导航 */}
        <nav
          aria-label="页面"
          className="grid min-w-0 flex-1 grid-cols-7 items-stretch md:flex md:items-stretch md:gap-0.5"
        >
          {NAV.map((item) => {
            const active = view === item.id
            return (
              <button
                key={item.id}
                type="button"
                onClick={() => onViewChange(item.id)}
                className={cn(
                  'relative text-center text-[11px] font-medium transition-colors md:shrink-0 md:px-3 md:text-[13px]',
                  active ? 'text-foreground' : 'text-muted hover:text-foreground'
                )}
              >
                {item.label}
                <span
                  className={cn(
                    'absolute inset-x-1 bottom-0 h-[2px] rounded-full bg-foreground transition-opacity md:inset-x-3',
                    active ? 'opacity-100' : 'opacity-0'
                  )}
                />
              </button>
            )
          })}
        </nav>

        <div className="flex shrink-0 items-center gap-2">
          {(view === 'dashboard' || view === 'trades') && (
            <div className="relative hidden sm:block">
              <Search className="pointer-events-none absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted-2" />
              <input
                value={query}
                onChange={(e) => onQueryChange(e.target.value)}
                onKeyDown={(e) => e.key === 'Enter' && onSearchSubmit()}
                placeholder="Account"
                className="h-8 w-[7.25rem] rounded-lg border-0 bg-bg py-0 pl-8 pr-2.5 text-xs outline-none ring-1 ring-border/80 transition placeholder:text-muted-2 focus:ring-foreground/25 lg:w-36"
              />
            </div>
          )}

          <div className="flex items-center rounded-lg bg-bg p-0.5 ring-1 ring-border/80">
            <ToolBtn label="同步 Flex 报表" onClick={onSyncFlex} disabled={syncingFlex}>
              <CloudDownload size={15} className={syncingFlex ? 'animate-spin' : undefined} />
            </ToolBtn>
            <ToolBtn label="同步 Gateway 市价" onClick={onSyncGateway} disabled={syncingGateway}>
              <Cable size={15} className={syncingGateway ? 'animate-spin' : undefined} />
            </ToolBtn>
            <ToolBtn
              label={liveOn ? '关闭实时推送' : '开启实时推送'}
              onClick={onToggleLive}
              disabled={togglingLive}
              active={liveOn}
            >
              <Radio size={15} className={liveOn ? 'animate-pulse' : undefined} />
            </ToolBtn>
            <span className="mx-0.5 h-4 w-px bg-border/80" aria-hidden />
            <ToolBtn label="切换主题" onClick={onToggleTheme}>
              {theme === 'dark' ? <Sun size={15} /> : <Moon size={15} />}
            </ToolBtn>
          </div>
        </div>
      </div>

      {(view === 'dashboard' || view === 'trades') && (
        <div className="border-t border-border/50 bg-bg/40">
          {/* 桌面：单行环境 · 账户芯片 · 数据源（原 Web 观感） */}
          <div className="mx-auto hidden h-11 max-w-7xl items-center gap-5 px-6 md:flex">
            <DataSourceBar
              feeds={feedStatuses}
              tradeEnv={tradeEnv || undefined}
              className="min-w-0 shrink-0"
            />

            <div
              role="tablist"
              aria-label="交易环境"
              className="flex shrink-0 items-center gap-0.5 rounded-lg bg-surface p-0.5 ring-1 ring-border/70"
            >
              {ENV.map((opt) => {
                const active = tradeEnv === opt.id
                return (
                  <button
                    key={opt.label}
                    type="button"
                    role="tab"
                    aria-selected={active}
                    onClick={() => onEnvChange(opt.id)}
                    className={cn(
                      'rounded-md px-2.5 py-1 text-[12px] font-medium transition',
                      active
                        ? opt.id === 'Live'
                          ? 'bg-down/12 text-down'
                          : opt.id === 'Paper'
                            ? 'bg-up/12 text-up'
                            : 'bg-foreground text-bg'
                        : 'text-muted hover:text-foreground'
                    )}
                  >
                    {opt.label}
                  </button>
                )
              })}
            </div>

            {accounts.length > 0 && (
              <AccountChips
                accounts={accounts}
                selected={selected}
                onSelectAccount={onSelectAccount}
                className="min-w-0 flex-1"
              />
            )}
          </div>

          {/* 移动：环境 + 账户下拉 + 数据源换行 */}
          <div className="mx-auto flex max-w-7xl flex-col gap-2 px-3 py-2 md:hidden">
            <div className="flex min-w-0 items-center gap-2">
              <div
                role="tablist"
                aria-label="交易环境"
                className="flex shrink-0 items-center gap-0.5 rounded-lg bg-surface p-0.5 ring-1 ring-border/70"
              >
                {ENV.map((opt) => {
                  const active = tradeEnv === opt.id
                  return (
                    <button
                      key={opt.label}
                      type="button"
                      role="tab"
                      aria-selected={active}
                      onClick={() => onEnvChange(opt.id)}
                      className={cn(
                        'rounded-md px-2.5 py-1 text-[12px] font-medium transition',
                        active
                          ? opt.id === 'Live'
                            ? 'bg-down/12 text-down'
                            : opt.id === 'Paper'
                              ? 'bg-up/12 text-up'
                              : 'bg-foreground text-bg'
                          : 'text-muted hover:text-foreground'
                      )}
                    >
                      {opt.label}
                    </button>
                  )
                })}
              </div>
              {accounts.length > 0 && (
                <AccountSelect
                  accounts={accounts}
                  selected={selected}
                  onSelectAccount={onSelectAccount}
                  className="min-w-0 flex-1"
                />
              )}
            </div>
            <DataSourceBar
              feeds={feedStatuses}
              tradeEnv={tradeEnv || undefined}
              className="min-w-0"
            />
          </div>
        </div>
      )}
    </header>
  )
}
