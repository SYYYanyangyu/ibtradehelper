import { useState, useEffect, useMemo, useRef } from 'react'
import {
  Wallet,
  TrendingUp,
  Coins,
  LayoutDashboard,
  AlertTriangle,
  Inbox,
  X
} from 'lucide-react'
import {
  getPortfolio,
  getAccounts,
  getDailyPnl,
  getTrades,
  syncGateway,
  runFlexIngest,
  getDataFeedStatus,
  startLive,
  stopLive,
  getLive,
  type TradeEnvFilter,
  type TradeEnvSelection,
  type AccountInfo,
  type DataFeedStatusItem
} from './lib/api'
import type { TwsLiveSnapshot } from './lib/api'
import type { PortfolioSummary, DailyPnlPoint, TradeRecord } from './types'
import { useTheme } from './theme'
import { cn, fmtMoney, thCls, trCls } from './lib/utils'
import { CHART_COLORS } from './lib/chart'
import { DailyPnlPanel } from './components/DailyPnlPanel'
import { TodayPnlHero } from './components/TodayPnlHero'
import { PositionContribution } from './components/PositionContribution'
import { TradeSummary } from './components/TradeSummary'
import { StatCard } from './components/StatCard'
import { AllocationChart } from './components/AllocationChart'
import { PnlBarChart } from './components/PnlBarChart'
import { DashboardSkeleton } from './components/Skeleton'
import { AppHeader } from './components/AppHeader'
import { Card, CardContent, CardHeader, CardTitle } from './components/ui/Card'
import { BacktestPage } from './pages/BacktestPage'
import { OrdersPage } from './pages/OrdersPage'
import { AutoTradePage } from './pages/AutoTradePage'

export default function App() {
  const { theme, toggle } = useTheme()
  const [selected, setSelected] = useState('') // '' = 当前环境下全部账户
  /** 默认 Paper（真实数据）；嘉信 Mock 仅在切到 Live 且开启 ibtrade-mock 时生效。 */
  const [tradeEnv, setTradeEnv] = useState<TradeEnvSelection>('Paper')
  const [query, setQuery] = useState('')
  const [data, setData] = useState<PortfolioSummary | null>(null)
  const [accountInfos, setAccountInfos] = useState<AccountInfo[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [dailyPnl, setDailyPnl] = useState<DailyPnlPoint[]>([])
  const [trades, setTrades] = useState<TradeRecord[]>([])
  const [syncingGateway, setSyncingGateway] = useState(false)
  const [syncingFlex, setSyncingFlex] = useState(false)
  /** 顶部轻量提示（自动消失，不占大卡片） */
  const [toast, setToast] = useState<{ tone: 'ok' | 'warn'; text: string } | null>(null)
  const [feedStatuses, setFeedStatuses] = useState<DataFeedStatusItem[]>([])
  const [liveFlexHintDismissed, setLiveFlexHintDismissed] = useState(false)

  useEffect(() => {
    if (!toast) return
    const t = window.setTimeout(() => setToast(null), 4500)
    return () => window.clearTimeout(t)
  }, [toast])

  // 实时推送（持久订阅）相关
  const [liveOn, setLiveOn] = useState(false)
  const [liveData, setLiveData] = useState<TwsLiveSnapshot | null>(null)
  const [liveError, setLiveError] = useState<string | null>(null)
  const [togglingLive, setTogglingLive] = useState(false)
  const [view, setView] = useState<'dashboard' | 'backtest' | 'orders' | 'autotrade'>('dashboard')

  /** 防止 Paper↔Live 快速切换时旧请求后返回，把看板写回上一环境。 */
  const loadSeq = useRef(0)

  async function load(opts?: { accountId?: string; env?: TradeEnvSelection }) {
    const aid = opts?.accountId !== undefined ? opts.accountId : selected
    const env = opts?.env !== undefined ? opts.env : tradeEnv
    const envParam: TradeEnvFilter | undefined = env || undefined
    const seq = ++loadSeq.current

    setLoading(true)
    setError(null)
    try {
      const [d, pnlPoints, tradeRows] = await Promise.all([
        getPortfolio(aid || undefined, envParam),
        getDailyPnl(aid || undefined, 365, envParam).catch(() => [] as DailyPnlPoint[]),
        getTrades(aid || undefined, 365, envParam).catch(() => [] as TradeRecord[])
      ])
      if (seq !== loadSeq.current) return
      setData(d)
      setDailyPnl(pnlPoints)
      setTrades(tradeRows)
    } catch (e) {
      if (seq !== loadSeq.current) return
      setError(e instanceof Error ? e.message : '加载失败，请确认后端已启动 (localhost:8090)')
      setData(null)
      setDailyPnl([])
      setTrades([])
    } finally {
      if (seq === loadSeq.current) setLoading(false)
    }
  }

  function refreshFeedStatus() {
    getDataFeedStatus()
      .then((r) => setFeedStatuses(r.feeds ?? []))
      .catch(() => {})
  }

  // 账户目录（含环境标签）
  useEffect(() => {
    getAccounts()
      .then(setAccountInfos)
      .catch(() => {})
    refreshFeedStatus()
  }, [])

  // 环境切换或首次挂载：按当前 env 拉汇总（显式传入 env，避免闭包读到旧值）
  useEffect(() => {
    void load({ accountId: selected, env: tradeEnv })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tradeEnv])

  const currency = data?.positions[0]?.currency ?? 'USD'
  const todayDate = dailyPnl[dailyPnl.length - 1]?.date
  /** 图表/子面板强制重挂载，避免 Recharts 与内部 filter 状态残留上一环境 */
  const scopeKey = `${tradeEnv}:${selected || 'all'}:${data?.accountId ?? ''}:${data?.positionCount ?? 0}`

  const allocation = useMemo(
    () => data?.positions.map((p) => ({ name: p.symbol, value: p.marketValue, currency: p.currency })) ?? [],
    [data]
  )
  const pnl = useMemo(
    () =>
      data?.positions.map((p) => ({
        symbol: p.symbol,
        unrealized: p.unrealizedPnl,
        realized: p.realizedPnl,
        currency: p.currency
      })) ?? [],
    [data]
  )

  const cost = data?.totalCostBasis ?? 0
  const pctUnreal = cost ? (data?.totalUnrealizedPnl ?? 0) / cost : 0
  const pctReal = cost ? (data?.totalRealizedPnl ?? 0) / cost : 0
  const pctTotal = cost ? (data?.totalPnl ?? 0) / cost : 0

  /** Live 空仓时用官方净值曲线看「总亏」；持仓卡片全 0 会误导。 */
  const navStats = useMemo(() => {
    const meaningful = dailyPnl.filter((d) => Math.abs(d.netLiquidation) > 1)
    if (!meaningful.length) return null
    const first = meaningful[0]
    const last = meaningful[meaningful.length - 1]
    const peak = meaningful.reduce((best, d) =>
      d.netLiquidation > best.netLiquidation ? d : best
    )
    return {
      nav: last.netLiquidation,
      asOf: last.date,
      vsPeak: last.netLiquidation - peak.netLiquidation,
      peakNav: peak.netLiquidation,
      peakDate: peak.date,
      vsFirst: last.netLiquidation - first.netLiquidation,
      firstNav: first.netLiquidation,
      firstDate: first.date
    }
  }, [dailyPnl])

  const showLiveNavCards =
    tradeEnv === 'Live' && !!navStats && (data?.positionCount ?? 0) === 0

  const accounts = useMemo(() => {
    const list = tradeEnv
      ? accountInfos.filter((a) => a.environment === tradeEnv)
      : accountInfos
    return list.map((a) => a.accountId)
  }, [accountInfos, tradeEnv])

  function selectChip(acc: string) {
    const aid = acc === '全部' ? '' : acc
    setQuery('')
    setSelected(aid)
    void load({ accountId: aid, env: tradeEnv })
  }

  function selectEnv(next: TradeEnvSelection) {
    // 立刻清空，避免切换后短暂仍显示上一环境的市值分布/成交
    loadSeq.current += 1
    setSelected('')
    setQuery('')
    setData(null)
    setDailyPnl([])
    setTrades([])
    setError(null)
    setLoading(true)
    setTradeEnv(next)
    setLiveFlexHintDismissed(false)
  }

  function submitSearch() {
    const q = query.trim()
    setSelected(q)
    void load({ accountId: q, env: tradeEnv })
  }

  async function handleSyncGateway() {
    setSyncingGateway(true)
    setError(null)
    setToast(null)
    try {
      const res = await syncGateway()
      setToast({
        tone: 'ok',
        text: `Gateway 已刷新 ${res.accountId} 市价 · ${res.connectedPort}`
      })
      refreshFeedStatus()
      await load({ accountId: selected, env: tradeEnv })
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Gateway 同步失败，请确认本机 IB Gateway 已启动并开启 API')
    } finally {
      setSyncingGateway(false)
    }
  }

  async function handleSyncFlex() {
    setSyncingFlex(true)
    setError(null)
    setToast(null)
    try {
      const env: TradeEnvFilter | undefined = tradeEnv || undefined
      const res = await runFlexIngest(env)
      const feed =
        res.feeds?.find((f) => f.feed === 'Flex' && (!env || f.environment === env)) ??
        res.feeds?.find((f) => f.feed === 'Flex')
      const msg = feed?.message ?? ''
      const noPos = /positions=0/.test(msg)
      const noNav = /navDays=0/.test(msg)
      setFeedStatuses(res.feeds ?? [])
      await load({ accountId: selected, env: tradeEnv })
      getAccounts().then(setAccountInfos).catch(() => {})
      const after = await getPortfolio(selected || undefined, env).catch(() => null)
      const reallyNoPos = noPos && (after?.positionCount ?? 0) === 0
      const envLabel = res.env === 'all' ? 'All' : res.env
      if (reallyNoPos || noNav) {
        setToast({
          tone: 'warn',
          text:
            `Flex（${envLabel}）已同步，但缺` +
            [reallyNoPos ? '开仓' : null, noNav ? '净值' : null].filter(Boolean).join('/') +
            '段'
        })
      } else if (noPos) {
        setToast({ tone: 'ok', text: `Flex（${envLabel}）已同步 · 保留原持仓` })
      } else {
        setToast({ tone: 'ok', text: `Flex（${envLabel}）已同步` })
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Flex 摄入失败，请检查 Token / QueryId / 网络代理')
    } finally {
      setSyncingFlex(false)
    }
  }

  // 实时推送轮询：开启时每 2 秒拉一次实时快照，价格一动即刷新
  useEffect(() => {
    if (!liveOn) return
    let cancelled = false
    const tick = async () => {
      try {
        const d = await getLive()
        if (cancelled) return
        setLiveData(d)
        setLiveError(d.running ? null : d.lastError ?? '实时推送未运行')
      } catch (e) {
        if (cancelled) return
        setLiveError(e instanceof Error ? e.message : '读取实时数据失败')
      }
    }
    tick()
    const id = setInterval(tick, 2000)
    return () => {
      cancelled = true
      clearInterval(id)
    }
  }, [liveOn])

  async function handleToggleLive() {
    setTogglingLive(true)
    setLiveError(null)
    try {
      if (liveOn) {
        await stopLive()
        setLiveOn(false)
        setLiveData(null)
      } else {
        await startLive()
        setLiveOn(true)
        // 完整实时快照由 liveOn 触发的轮询（每 2s，且立即执行一次）填充，无需在此构造部分对象
      }
    } catch (e) {
      setLiveError(e instanceof Error ? e.message : '实时推送启动失败')
    } finally {
      setTogglingLive(false)
    }
  }

  return (
    <div className="relative min-h-full overflow-x-hidden">
      <AppHeader
        view={view}
        onViewChange={setView}
        theme={theme}
        onToggleTheme={toggle}
        query={query}
        onQueryChange={setQuery}
        onSearchSubmit={submitSearch}
        onSyncFlex={handleSyncFlex}
        syncingFlex={syncingFlex}
        onSyncGateway={handleSyncGateway}
        syncingGateway={syncingGateway}
        liveOn={liveOn}
        togglingLive={togglingLive}
        onToggleLive={handleToggleLive}
        tradeEnv={tradeEnv}
        onEnvChange={selectEnv}
        accounts={accounts}
        selected={selected}
        onSelectAccount={selectChip}
        feedStatuses={feedStatuses}
      />

      <main className="mx-auto max-w-7xl space-y-3 px-3 py-3 pb-[max(1rem,env(safe-area-inset-bottom))] sm:space-y-4 sm:px-6 sm:py-4">
        {view === 'autotrade' ? (
          <AutoTradePage />
        ) : view === 'orders' ? (
          <OrdersPage />
        ) : view === 'backtest' ? (
          <BacktestPage />
        ) : (
          <>
            {error && (
          <Card className="border-down/30 bg-down/5">
            <CardContent className="flex flex-col items-center gap-3 py-10 text-center">
              <AlertTriangle className="text-down" size={28} />
              <p className="text-sm text-down">{error}</p>
              <button
                onClick={() => void load({ accountId: selected, env: tradeEnv })}
                className="rounded-xl bg-down px-4 py-2 text-sm font-medium text-white transition hover:opacity-90"
              >
                重试
              </button>
            </CardContent>
          </Card>
        )}

        {toast && (
          <div
            className={cn(
              'flex items-center gap-2 rounded-lg border px-3 py-2 text-xs',
              toast.tone === 'ok'
                ? 'border-up/25 bg-up/5 text-up'
                : 'border-primary/25 bg-primary/5 text-primary'
            )}
          >
            <span className="min-w-0 flex-1 truncate">{toast.text}</span>
            <button
              type="button"
              onClick={() => setToast(null)}
              className="grid h-6 w-6 shrink-0 place-items-center rounded-md text-current/70 transition hover:bg-surface-2 hover:text-foreground"
              aria-label="关闭"
            >
              <X size={14} />
            </button>
          </div>
        )}

        {liveOn && (
          <Card className="border-up/30 bg-up/5 animate-fade-up">
            <CardContent className="space-y-3 py-4">
              <div className="flex flex-wrap items-center gap-2 text-sm">
                <span className="relative flex h-2.5 w-2.5">
                  <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-up opacity-75" />
                  <span className="relative inline-flex h-2.5 w-2.5 rounded-full bg-up" />
                </span>
                <span className="font-semibold text-up">实时推送中 · Gateway</span>
                {liveData?.running && (
                  <span className="text-xs text-muted">
                    账户 {liveData.accountId} · 端口 {liveData.connectedPort}
                  </span>
                )}
                <span className="ml-auto text-xs text-muted-2">内存行情，不改写 Flex 仓位结构</span>
              </div>

              {liveError && <p className="text-xs text-down">{liveError}</p>}

              {liveData?.positions?.length ? (
                <div className="overflow-x-auto">
                  <table className="w-full text-sm">
                    <thead>
                      <tr className="border-y border-border bg-surface-2/50 text-left">
                        <th className={cn(thCls, 'pr-3')}>标的</th>
                        <th className={cn(thCls, 'text-right')}>最新价</th>
                        <th className={cn(thCls, 'text-right')}>涨跌</th>
                        <th className={cn(thCls, 'text-right')}>市值</th>
                        <th className={cn(thCls, 'pl-3 text-right')}>未实现盈亏</th>
                      </tr>
                    </thead>
                    <tbody>
                      {liveData.positions.map((p) => (
                        <tr key={p.symbol} className={trCls}>
                          <td className="py-2.5 pr-3 font-medium">{p.symbol}</td>
                          <td className="py-2.5 px-3 text-right tabular-nums">{fmtMoney(p.lastPrice, p.currency)}</td>
                          <td className={cn('py-2.5 px-3 text-right tabular-nums', p.change > 0 ? 'text-up' : p.change < 0 ? 'text-down' : 'text-muted')}>
                            {p.change > 0 ? '+' : ''}
                            {fmtMoney(p.change, p.currency)}
                          </td>
                          <td className="py-2.5 px-3 text-right tabular-nums">{fmtMoney(p.marketValue, p.currency)}</td>
                          <td className={cn('py-2.5 pl-3 text-right tabular-nums', p.unrealizedPnl >= 0 ? 'text-up' : 'text-down')}>
                            {fmtMoney(p.unrealizedPnl, p.currency)}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              ) : (
                !liveError && <p className="text-xs text-muted-2">正在订阅行情…（若长时间无数据，请确认持仓合约支持实时行情权限）</p>
              )}
            </CardContent>
          </Card>
        )}

        {loading && !data && <DashboardSkeleton />}

        {data && !error && (
          <>
            {tradeEnv === 'Live' && data.positionCount === 0 && !liveFlexHintDismissed && (
              <Card className="border-primary/25 bg-primary/5">
                <CardContent className="relative py-4 pr-10 text-sm text-muted">
                  <button
                    type="button"
                    onClick={() => setLiveFlexHintDismissed(true)}
                    className="absolute right-3 top-3 grid h-6 w-6 place-items-center rounded-md text-muted-2 transition hover:bg-surface-2 hover:text-foreground"
                    aria-label="关闭提示"
                  >
                    <X size={14} />
                  </button>
                  <p className="font-medium text-foreground">真钱成交已有，但资产/持仓仍为空</p>
                  <p className="mt-1.5 text-xs leading-relaxed text-muted-2">
                    Live Flex Query（当前 QueryId）只带回了成交，没有「Open Positions / 日净值 NAV」。
                    请到 IB 账户管理 → Flex Queries 编辑该 Query，勾选开仓持仓与 Equity/NAV 后，再点「同步
                    Flex」。不必把 Gateway 登成真钱账号。
                  </p>
                </CardContent>
              </Card>
            )}

            <TodayPnlHero
              key={`hero-${scopeKey}`}
              data={dailyPnl}
              totalMarketValue={data.totalMarketValue}
              totalUnrealizedPnl={data.totalUnrealizedPnl}
              currency={currency}
              positions={data.positions}
              contribution={
                <PositionContribution
                  date={todayDate}
                  currency={currency}
                  embed
                  accountId={selected || undefined}
                  env={tradeEnv || undefined}
                  emptyHint={
                    tradeEnv === 'Live'
                      ? '该范围暂无逐日持仓贡献'
                      : tradeEnv === ''
                        ? 'All 范围暂无逐日持仓贡献'
                        : '该日无持仓记录'
                  }
                />
              }
            />

            <section className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
              {showLiveNavCards && navStats ? (
                <>
                  <StatCard
                    index={0}
                    label="官方净值"
                    value={navStats.nav}
                    currency={currency}
                    icon={<Wallet size={20} />}
                    sub={`截至 ${navStats.asOf} · 空仓`}
                  />
                  <StatCard
                    index={1}
                    label="较峰值（总回撤）"
                    value={navStats.vsPeak}
                    currency={currency}
                    icon={<TrendingUp size={20} />}
                    pnl
                    sub={`峰值日 ${navStats.peakDate}`}
                  />
                  <StatCard
                    index={2}
                    label="较首日净值"
                    value={navStats.vsFirst}
                    currency={currency}
                    icon={<Coins size={20} />}
                    pnl
                    sub={`自 ${navStats.firstDate}`}
                  />
                  <StatCard
                    index={3}
                    label="交易已实现(FIFO)"
                    value={data.totalRealizedPnl}
                    currency={currency}
                    icon={<LayoutDashboard size={20} />}
                    pnl
                    sub="Flex 未给 fifo · 由成交估算"
                  />
                </>
              ) : (
                <>
                  <StatCard
                    index={0}
                    label="总市值"
                    value={data.totalMarketValue}
                    currency={currency}
                    icon={<LayoutDashboard size={20} />}
                    sub={`${data.positionCount} 个标的`}
                  />
                  <StatCard
                    index={1}
                    label="未实现盈亏"
                    value={data.totalUnrealizedPnl}
                    currency={currency}
                    icon={<TrendingUp size={20} />}
                    pnl
                    pct={pctUnreal}
                  />
                  <StatCard
                    index={2}
                    label="已实现盈亏"
                    value={data.totalRealizedPnl}
                    currency={currency}
                    icon={<Coins size={20} />}
                    pnl
                    pct={pctReal}
                  />
                  <StatCard
                    index={3}
                    label="总盈亏"
                    value={data.totalPnl}
                    currency={currency}
                    icon={<Wallet size={20} />}
                    pnl
                    pct={pctTotal}
                    sub="未实现 + 已实现"
                  />
                </>
              )}
            </section>

            <section className="grid grid-cols-1 gap-6 lg:grid-cols-2">
              <Card className="animate-fade-up">
                <CardHeader>
                  <CardTitle>市值分布</CardTitle>
                  <span className="text-xs text-muted-2">按标的占比</span>
                </CardHeader>
                <CardContent>
                  {allocation.length ? (
                    <AllocationChart key={`alloc-${scopeKey}`} data={allocation} />
                  ) : (
                    <EmptyHint text="暂无持仓数据" />
                  )}
                </CardContent>
              </Card>

              <Card className="animate-fade-up">
                <CardHeader>
                  <CardTitle>各标的盈亏</CardTitle>
                  <div className="flex items-center gap-3 text-xs">
                    <Legend color={CHART_COLORS.up} label="未实现" />
                    <Legend color={CHART_COLORS.primary} label="已实现" />
                  </div>
                </CardHeader>
                <CardContent>
                  {pnl.length ? (
                    <PnlBarChart key={`pnlbar-${scopeKey}`} data={pnl} />
                  ) : (
                    <EmptyHint text="暂无持仓数据" />
                  )}
                </CardContent>
              </Card>
            </section>

            <DailyPnlPanel
              key={`dailypnl-${scopeKey}`}
              data={dailyPnl}
              currency={currency}
              env={tradeEnv || undefined}
              accountId={selected || undefined}
            />

            <TradeSummary
              key={`trades-${scopeKey}`}
              trades={trades}
              currency={currency}
              envLabel={
                tradeEnv === 'Paper'
                  ? 'Paper'
                  : tradeEnv === 'Live'
                    ? 'Live'
                    : 'All'
              }
            />

            <p className="text-center text-xs text-muted-2">
              账户：
              <span className="font-medium text-muted">{data.accountId}</span> · 持仓数：
              {data.positionCount} · 更新于 {new Date(data.asOfUtc).toLocaleString('zh-CN')}
            </p>
          </>
        )}

        {!loading && !data && !error && (
          <Card>
            <CardContent className="py-12">
              <EmptyHint text="暂无数据" />
            </CardContent>
          </Card>
        )}
          </>
        )}
      </main>
    </div>
  )
}

function Legend({ color, label }: { color: string; label: string }) {
  return (
    <span className="inline-flex items-center gap-1.5 text-muted">
      <span className="h-2.5 w-2.5 rounded-full" style={{ background: color }} />
      {label}
    </span>
  )
}

function EmptyHint({ text }: { text: string }) {
  return (
    <div className="flex flex-col items-center gap-2 py-12 text-center">
      <div className="grid h-12 w-12 place-items-center rounded-2xl bg-surface-2 text-muted-2">
        <Inbox size={22} />
      </div>
      <p className="text-sm text-muted">{text}</p>
    </div>
  )
}
