import { useState, useEffect, type ReactNode } from 'react'
import {
  Play,
  Loader2,
  TrendingUp,
  Activity,
  Gauge,
  Percent,
  Target,
  Calendar,
  Wallet,
  BarChart3,
  LineChart,
  Sparkles,
  Bot,
  ShieldCheck,
  CheckCircle2,
  AlertTriangle,
  XCircle,
  Info
} from 'lucide-react'
import { Card, CardContent, CardHeader, CardTitle } from '../components/ui/Card'
import { EquityCurveChart } from '../components/EquityCurveChart'
import {
  runBacktest,
  listStrategies,
  aiSuggestAndRun,
  deployDryRun,
  deployPreflight,
  listAiProviders,
  selectAiProvider,
  refreshAiModels,
  type BacktestResult,
  type BacktestRequest,
  type StrategyDef,
  type AiSuggestResult,
  type LlmCatalog,
  type DeployPreflightResult,
  type DeployPreflightBody
} from '../lib/api'
import { cn, fmtMoney, fmtPct, gainClass, inputCls, thCls, trCls } from '../lib/utils'

const DURATION_OPTIONS = ['1 D', '60 D', '1 M', '3 M', '6 M', '1 Y', '2 Y', '3 Y', '5 Y', 'YTD']
const BARSIZE_OPTIONS = ['1 min', '5 mins', '15 mins', '30 mins', '1 hour', '1 day']

/** 回测 + AI 策略闭环：建议 → 回测 → 一键演练部署。 */
export function BacktestPage() {
  const [symbol, setSymbol] = useState('AAPL')
  const [strategy, setStrategy] = useState('sma_cross')
  const [duration, setDuration] = useState('1 Y')
  const [barSize, setBarSize] = useState('1 day')
  const [capital, setCapital] = useState('100000')
  const [fillMode, setFillMode] = useState<'next_open' | 'close'>('next_open')
  const [slippageBps, setSlippageBps] = useState('5')
  const [commission, setCommission] = useState('1')
  const [strategies, setStrategies] = useState<StrategyDef[]>([])
  const [params, setParams] = useState<Record<string, string>>({})
  const [loading, setLoading] = useState(false)
  const [aiLoading, setAiLoading] = useState(false)
  const [deploying, setDeploying] = useState(false)
  const [preflighting, setPreflighting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [result, setResult] = useState<BacktestResult | null>(null)
  const [aiPrompt, setAiPrompt] = useState('用均线做 AAPL 中长线，偏稳健')
  const [suggestion, setSuggestion] = useState<AiSuggestResult | null>(null)
  const [deployMsg, setDeployMsg] = useState<string | null>(null)
  const [preflight, setPreflight] = useState<DeployPreflightResult | null>(null)
  const [aiCatalog, setAiCatalog] = useState<LlmCatalog | null>(null)
  const [aiBusy, setAiBusy] = useState(false)

  function defaultsOf(def?: StrategyDef): Record<string, string> {
    const out: Record<string, string> = {}
    def?.parameters.forEach((p) => {
      out[p.key] = String(p.defaultValue)
    })
    return out
  }

  useEffect(() => {
    listStrategies()
      .then((list) => {
        setStrategies(list)
        setParams((prev) => ({ ...defaultsOf(list.find((s) => s.key === 'sma_cross')), ...prev }))
      })
      .catch(() => {})
    listAiProviders()
      .then(setAiCatalog)
      .catch(() => {})
  }, [])

  const activeProvider = aiCatalog?.providers.find((p) => p.id === aiCatalog.activeProvider)

  async function onSelectProvider(providerId: string) {
    setAiBusy(true)
    setError(null)
    try {
      setAiCatalog(await selectAiProvider({ providerId }))
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to switch provider')
    } finally {
      setAiBusy(false)
    }
  }

  async function onSelectModel(model: string) {
    if (!aiCatalog) return
    setAiBusy(true)
    setError(null)
    try {
      setAiCatalog(await selectAiProvider({ providerId: aiCatalog.activeProvider, model }))
    } catch (e) {
      setError(e instanceof Error ? e.message : '切换模型失败')
    } finally {
      setAiBusy(false)
    }
  }

  async function onRefreshModels() {
    if (!aiCatalog) return
    setAiBusy(true)
    setError(null)
    try {
      const r = await refreshAiModels(aiCatalog.activeProvider)
      setAiCatalog(r.catalog)
      if (r.warning) setError(r.warning)
    } catch (e) {
      setError(e instanceof Error ? e.message : '拉取模型失败')
    } finally {
      setAiBusy(false)
    }
  }

  function onStrategyChange(key: string) {
    setStrategy(key)
    const def = strategies.find((s) => s.key === key)
    if (def) setParams(defaultsOf(def))
  }

  function applySuggestion(s: AiSuggestResult) {
    setSuggestion(s)
    setSymbol(s.symbol)
    setStrategy(s.strategyKey)
    setDuration(s.duration)
    setBarSize(s.barSize)
    const next: Record<string, string> = {}
    Object.entries(s.parameters).forEach(([k, v]) => {
      next[k] = String(v)
    })
    setParams(next)
  }

  async function run() {
    setLoading(true)
    setError(null)
    setDeployMsg(null)
    setPreflight(null)
    const def = strategies.find((s) => s.key === strategy)
    const parameters: Record<string, number> = {}
    def?.parameters.forEach((p) => {
      const raw = params[p.key]
      parameters[p.key] = raw !== undefined && raw !== '' ? Number(raw) : p.defaultValue
    })
    const req: BacktestRequest = {
      symbol: symbol.trim(),
      strategy,
      duration: duration.trim(),
      barSize: barSize.trim(),
      parameters,
      initialCapital: +capital,
      commissionPerTrade: Number(commission) || 0,
      fillMode,
      slippageBps: Number(slippageBps) || 5
    }
    try {
      setResult(await runBacktest(req))
    } catch (e) {
      setError(e instanceof Error ? e.message : '回测失败')
    } finally {
      setLoading(false)
    }
  }

  async function runAi() {
    setAiLoading(true)
    setError(null)
    setDeployMsg(null)
    setPreflight(null)
    try {
      // 不传表单默认的 symbol/duration/barSize，否则会把 AAPL·1Y·1day
      // 当成「用户指定」盖住 prompt 里的「闪迪 / SNDK」等意图。
      const r = await aiSuggestAndRun({
        prompt: aiPrompt.trim(),
        initialCapital: +capital || 100000,
        fillMode,
        slippageBps: Number(slippageBps) || 5,
        commissionPerTrade: Number(commission) || 0
      })
      applySuggestion(r.suggestion)
      setResult(r.backtest)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'AI 策略失败')
    } finally {
      setAiLoading(false)
    }
  }

  function buildDeployBody(): DeployPreflightBody | null {
    if (!result) return null
    const def = strategies.find((s) => s.key === strategy)
    const parameters: Record<string, number> = {}
    def?.parameters.forEach((p) => {
      const raw = params[p.key]
      parameters[p.key] = raw !== undefined && raw !== '' ? Number(raw) : p.defaultValue
    })
    return {
      symbol: result.symbol || symbol,
      strategyKey: strategy,
      parameters,
      duration,
      barSize,
      quantity: 100,
      fillMode,
      slippageBps: Number(slippageBps) || 5,
      totalReturnPct: result.totalReturnPct,
      buyHoldReturnPct: result.buyHoldReturnPct,
      maxDrawdownPct: result.maxDrawdownPct,
      sharpe: result.sharpe,
      tradeCount: result.tradeCount,
      barCount: result.barCount,
      costDragPct: result.costDragPct,
      totalCost: result.totalCost
    }
  }

  /** 第一步：跑检查清单，展示结果，不真正部署。 */
  async function onPreflight() {
    const body = buildDeployBody()
    if (!body) return
    setPreflighting(true)
    setError(null)
    setDeployMsg(null)
    try {
      setPreflight(await deployPreflight(body))
    } catch (e) {
      setError(e instanceof Error ? e.message : '检查清单失败')
      setPreflight(null)
    } finally {
      setPreflighting(false)
    }
  }

  /** 第二步：确认后部署（有警告则带 acknowledgeWarnings）。 */
  async function onConfirmDeploy() {
    const body = buildDeployBody()
    if (!body || !preflight?.canDeploy) return
    setDeploying(true)
    setError(null)
    setDeployMsg(null)
    try {
      const d = await deployDryRun({
        ...body,
        startEngine: false,
        acknowledgeWarnings: !!preflight.hasWarnings
      })
      setDeployMsg(d.message)
      setPreflight(null)
    } catch (e) {
      setError(e instanceof Error ? e.message : '部署失败')
    } finally {
      setDeploying(false)
    }
  }

  const activeStrategy = strategies.find((s) => s.key === strategy)
  const busy = loading || aiLoading || deploying || preflighting || aiBusy

  return (
    <>
      {/* AI 策略助手 */}
      <Card className="animate-fade-up overflow-hidden">
        <div className="flex items-start justify-between gap-3 border-b border-border px-5 py-4">
          <div className="flex items-start gap-3">
            <div className="grid h-9 w-9 place-items-center rounded-lg border border-border bg-surface-2 text-foreground">
              <Sparkles size={16} />
            </div>
            <div>
              <CardTitle className="text-base">AI 策略助手</CardTitle>
              <p className="mt-0.5 text-xs text-muted-2">
                自然语言 → 白名单策略 + 参数 → 回测（次日开盘+滑点）。绝不执行任意代码；无 Key
                时本地启发式。部署永远 DryRun。
              </p>
            </div>
          </div>
          <span className="inline-flex items-center gap-1 rounded-md bg-surface-2 px-2 py-1 text-[11px] text-muted-2">
            <ShieldCheck size={12} />
            Safe Routing
          </span>
        </div>
        <CardContent className="space-y-3">
          {aiCatalog && (
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
              <Field label="Provider" hint={!activeProvider?.hasApiKey ? `No Key → heuristic (${activeProvider?.apiKeyEnv ?? ''})` : 'Key configured'}>
                <select
                  value={aiCatalog.activeProvider}
                  onChange={(e) => onSelectProvider(e.target.value)}
                  disabled={aiBusy}
                  className={inputCls}
                >
                  {aiCatalog.providers.map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.displayName}
                      {p.hasApiKey ? '' : ' · No Key'}
                    </option>
                  ))}
                </select>
              </Field>
              <Field label="Model">
                <select
                  value={activeProvider?.model ?? ''}
                  onChange={(e) => onSelectModel(e.target.value)}
                  disabled={aiBusy || !activeProvider}
                  className={inputCls}
                >
                  {(activeProvider?.models?.length ? activeProvider.models : [activeProvider?.model ?? '']).map(
                    (m) =>
                      m ? (
                        <option key={m} value={m}>
                          {m}
                        </option>
                      ) : null
                  )}
                </select>
              </Field>
              <Field label="Models" hint="Requires API Key">
                <button
                  type="button"
                  onClick={onRefreshModels}
                  disabled={aiBusy || !activeProvider?.hasApiKey}
                  className="inline-flex w-full items-center justify-center gap-2 rounded-lg border border-border bg-surface px-3 py-2 text-sm transition hover:bg-surface-2 disabled:opacity-40"
                >
                  {aiBusy ? <Loader2 size={14} className="animate-spin" /> : null}
                  Refresh
                </button>
              </Field>
            </div>
          )}
          <textarea
            value={aiPrompt}
            onChange={(e) => setAiPrompt(e.target.value)}
            rows={3}
            placeholder="例如：用 RSI 做 NVDA 短线，偏激进；或 唐奇安突破跑 TSLA 一年日线"
            className={cn(inputCls, 'resize-none')}
          />
          <div className="flex flex-wrap items-center gap-3">
            <button
              type="button"
              onClick={runAi}
              disabled={busy || !aiPrompt.trim()}
              className="inline-flex items-center gap-2 rounded-lg bg-foreground px-4 py-2 text-sm font-medium text-bg transition hover:opacity-90 disabled:opacity-40"
            >
              {aiLoading ? <Loader2 size={15} className="animate-spin" /> : <Bot size={15} />}
              {aiLoading ? '生成并回测中…' : 'AI 生成并回测'}
            </button>
            {suggestion && (
              <p className="text-xs text-muted-2">
                来源 <span className="font-medium text-foreground">{suggestion.source}</span>
                <span className="mx-1.5 opacity-40">·</span>
                置信 {(suggestion.confidence * 100).toFixed(0)}%
                <span className="mx-1.5 opacity-40">·</span>
                {suggestion.strategyName}
              </p>
            )}
          </div>
          {suggestion?.rationale && (
            <div className="rounded-lg bg-surface-2/60 px-3 py-2 text-xs leading-relaxed text-muted">
              <p className="mb-1 text-[11px] font-medium uppercase tracking-[0.1em] text-muted-2">
                策略说明（非报错）
              </p>
              <p>{suggestion.rationale}</p>
            </div>
          )}
        </CardContent>
      </Card>

      <Card className="animate-fade-up overflow-hidden">
        <div className="border-b border-border px-5 py-4">
          <CardTitle className="text-base">回测参数</CardTitle>
          <p className="mt-0.5 text-xs text-muted-2">
            历史数据来自 IB Gateway · 默认次日开盘成交 + 滑点 · Paper/Live 由量化页风控独立控制
          </p>
        </div>

        <CardContent className="space-y-5">
          <section>
            <p className="mb-2.5 text-[11px] font-medium uppercase tracking-[0.12em] text-muted-2">基础设置</p>
            <div className="grid grid-cols-1 gap-3.5 sm:grid-cols-2 lg:grid-cols-3">
              <Field label="标的" icon={<Target size={14} />} hint="如 AAPL">
                <input value={symbol} onChange={(e) => setSymbol(e.target.value)} className={inputCls} />
              </Field>
              <Field label="策略" icon={<LineChart size={14} />}>
                <select value={strategy} onChange={(e) => onStrategyChange(e.target.value)} className={inputCls}>
                  {strategies.map((s) => (
                    <option key={s.key} value={s.key}>
                      {s.name}
                    </option>
                  ))}
                </select>
              </Field>
              <Field label="区间" icon={<Calendar size={14} />}>
                <select value={duration} onChange={(e) => setDuration(e.target.value)} className={inputCls}>
                  {DURATION_OPTIONS.map((d) => (
                    <option key={d} value={d}>
                      {d}
                    </option>
                  ))}
                </select>
              </Field>
              <Field label="K线" icon={<BarChart3 size={14} />}>
                <select value={barSize} onChange={(e) => setBarSize(e.target.value)} className={inputCls}>
                  {BARSIZE_OPTIONS.map((b) => (
                    <option key={b} value={b}>
                      {b}
                    </option>
                  ))}
                </select>
              </Field>
              <Field label="初始资金" icon={<Wallet size={14} />}>
                <input value={capital} onChange={(e) => setCapital(e.target.value)} className={inputCls} />
              </Field>
              <Field label="撮合" hint="推荐次日开盘，避免同 bar 前瞻">
                <select
                  value={fillMode}
                  onChange={(e) => setFillMode(e.target.value as 'next_open' | 'close')}
                  className={inputCls}
                >
                  <option value="next_open">次日开盘（推荐）</option>
                  <option value="close">当日收盘（对比用）</option>
                </select>
              </Field>
              <Field label="滑点 (bps)" hint="买入上浮 / 卖出下浮，默认 5">
                <input
                  type="number"
                  min={0}
                  max={500}
                  step={1}
                  value={slippageBps}
                  onChange={(e) => setSlippageBps(e.target.value)}
                  className={inputCls}
                />
              </Field>
              <Field label="单边佣金" hint="进出各扣一次，默认 1">
                <input
                  type="number"
                  min={0}
                  max={1000}
                  step={0.1}
                  value={commission}
                  onChange={(e) => setCommission(e.target.value)}
                  className={inputCls}
                />
              </Field>
            </div>
          </section>

          {activeStrategy?.parameters.length ? (
            <section>
              <p className="mb-2.5 text-[11px] font-medium uppercase tracking-[0.12em] text-muted-2">
                策略参数 · {activeStrategy.name}
              </p>
              <div className="grid grid-cols-1 gap-3.5 sm:grid-cols-2 lg:grid-cols-3">
                {activeStrategy.parameters.map((p) => (
                  <Field key={p.key} label={p.label} hint={p.description}>
                    <input
                      type="number"
                      value={params[p.key] ?? String(p.defaultValue)}
                      min={p.min ?? undefined}
                      max={p.max ?? undefined}
                      step={p.step ?? 1}
                      onChange={(e) => setParams((prev) => ({ ...prev, [p.key]: e.target.value }))}
                      className={inputCls}
                    />
                  </Field>
                ))}
              </div>
            </section>
          ) : null}

          <div className="flex flex-wrap items-center gap-3 border-t border-border pt-4">
            <button
              type="button"
              onClick={run}
              disabled={busy}
              className="inline-flex items-center gap-2 rounded-lg border border-border bg-surface px-4 py-2 text-sm font-medium transition hover:bg-surface-2 disabled:opacity-40"
            >
              {loading ? <Loader2 size={15} className="animate-spin" /> : <Play size={15} />}
              {loading ? '回测中…' : '运行回测'}
            </button>
            {result && (
              <button
                type="button"
                onClick={onPreflight}
                disabled={busy}
                className="inline-flex items-center gap-2 rounded-lg bg-foreground px-4 py-2 text-sm font-medium text-bg transition hover:opacity-90 disabled:opacity-40"
              >
                {preflighting ? <Loader2 size={15} className="animate-spin" /> : <ShieldCheck size={15} />}
                {preflighting ? '检查中…' : '部署前检查'}
              </button>
            )}
            {error && (
              <span className="rounded-lg bg-down/10 px-3 py-1.5 text-sm text-down">{error}</span>
            )}
            {deployMsg && (
              <span className="max-w-xl rounded-lg bg-up/10 px-3 py-1.5 text-xs leading-relaxed text-up">
                {deployMsg}
              </span>
            )}
          </div>

          {preflight && (
            <section className="mt-4 rounded-xl border border-border bg-surface-2/40 p-4">
              <div className="mb-3 flex flex-wrap items-start justify-between gap-2">
                <div>
                  <p className="text-sm font-medium text-foreground">部署前检查清单</p>
                  <p className="mt-0.5 text-xs text-muted-2">{preflight.summary}</p>
                </div>
                <span
                  className={cn(
                    'rounded-md px-2 py-1 text-[11px] font-medium',
                    !preflight.canDeploy
                      ? 'bg-down/10 text-down'
                      : preflight.hasWarnings
                        ? 'bg-amber-500/10 text-amber-700 dark:text-amber-300'
                        : 'bg-up/10 text-up'
                  )}
                >
                  {!preflight.canDeploy ? '已阻断' : preflight.hasWarnings ? '有警告' : '通过'}
                </span>
              </div>
              <ul className="space-y-2">
                {preflight.checks.map((c) => (
                  <li key={c.id} className="flex gap-2.5 text-xs leading-relaxed">
                    <CheckLevelIcon level={c.level} />
                    <div className="min-w-0">
                      <p className="font-medium text-foreground">{c.title}</p>
                      <p className="text-muted-2">{c.detail}</p>
                    </div>
                  </li>
                ))}
              </ul>
              <div className="mt-4 flex flex-wrap items-center gap-3 border-t border-border pt-3">
                <button
                  type="button"
                  onClick={onConfirmDeploy}
                  disabled={busy || !preflight.canDeploy}
                  className="inline-flex items-center gap-2 rounded-lg bg-foreground px-4 py-2 text-sm font-medium text-bg transition hover:opacity-90 disabled:opacity-40"
                >
                  {deploying ? <Loader2 size={15} className="animate-spin" /> : <ShieldCheck size={15} />}
                  {preflight.hasWarnings ? '确认警告并部署（强制演练）' : '确认部署（强制演练）'}
                </button>
                <button
                  type="button"
                  onClick={() => setPreflight(null)}
                  disabled={busy}
                  className="text-xs text-muted-2 transition hover:text-foreground"
                >
                  取消
                </button>
                <p className="text-[11px] text-muted-2">
                  {preflight.symbol} · {preflight.strategyName || preflight.strategyKey} ·{' '}
                  {preflight.duration}/{preflight.barSize} · qty {preflight.quantity} · 风控{' '}
                  {preflight.riskMode}
                </p>
              </div>
            </section>
          )}
        </CardContent>
      </Card>

      {result && (
        <>
          <section className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            <Metric
              icon={<TrendingUp size={16} />}
              label="策略收益"
              value={fmtPct(result.totalReturnPct, false)}
              cls={gainClass(result.totalReturnPct)}
              sub={`买入持有 ${fmtPct(result.buyHoldReturnPct, false)}`}
            />
            <Metric
              icon={<Activity size={16} />}
              label="最大回撤"
              value={`-${result.maxDrawdownPct.toFixed(2)}%`}
              cls="text-down"
            />
            <Metric
              icon={<Gauge size={16} />}
              label="夏普比率"
              value={result.sharpe.toFixed(2)}
              cls="text-foreground"
            />
            <Metric
              icon={<Percent size={16} />}
              label="胜率"
              value={`${result.winRatePct.toFixed(1)}%`}
              cls={gainClass(result.winRatePct - 50)}
              sub={`${result.winCount}/${result.tradeCount} 笔`}
            />
          </section>

          <Card className="animate-fade-up">
            <CardHeader>
              <CardTitle>交易成本</CardTitle>
              <span className="text-xs text-muted-2">
                {(result.fillMode === 'close' ? '当日收盘' : '次日开盘') +
                  ` · 滑点 ${result.slippageBps ?? 0} bps · 单边佣金 ${fmtMoney(result.commissionPerTrade ?? 0)}`}
              </span>
            </CardHeader>
            <CardContent>
              <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
                <div>
                  <p className="text-[11px] text-muted-2">累计佣金</p>
                  <p className="mt-1 text-sm font-semibold tabular-nums">{fmtMoney(result.totalCommission ?? 0)}</p>
                </div>
                <div>
                  <p className="text-[11px] text-muted-2">累计滑点</p>
                  <p className="mt-1 text-sm font-semibold tabular-nums">{fmtMoney(result.totalSlippageCost ?? 0)}</p>
                </div>
                <div>
                  <p className="text-[11px] text-muted-2">总成本</p>
                  <p className="mt-1 text-sm font-semibold tabular-nums">{fmtMoney(result.totalCost ?? 0)}</p>
                </div>
                <div>
                  <p className="text-[11px] text-muted-2">成本拖累</p>
                  <p className="mt-1 text-sm font-semibold tabular-nums text-down">
                    {(result.costDragPct ?? 0).toFixed(2)}%
                  </p>
                  <p className="mt-0.5 text-[11px] text-muted-2">
                    近似毛收益 {(result.approxGrossReturnPct ?? result.totalReturnPct).toFixed(2)}%
                  </p>
                </div>
              </div>
            </CardContent>
          </Card>

          <Card className="animate-fade-up">
            <CardHeader>
              <CardTitle>资金曲线</CardTitle>
              <span className="text-xs text-muted-2">
                {result.symbol} · {result.barCount} 根K线 · {result.startTime?.slice(0, 10)} ~{' '}
                {result.endTime?.slice(0, 10)}
              </span>
            </CardHeader>
            <CardContent>
              <EquityCurveChart data={result.equityCurve} initialCapital={result.initialCapital} />
            </CardContent>
          </Card>

          <Card className="animate-fade-up">
            <CardHeader>
              <CardTitle>交易明细</CardTitle>
              <span className="text-xs text-muted-2">{result.tradeCount} 笔</span>
            </CardHeader>
            <CardContent>
              {result.trades.length ? (
                <div className="overflow-x-auto">
                  <table className="w-full text-sm">
                    <thead>
                      <tr className="border-y border-border bg-surface-2/50 text-left">
                        <th className={cn(thCls, 'pr-3')}>#</th>
                        <th className={thCls}>买入日</th>
                        <th className={cn(thCls, 'text-right')}>买入价</th>
                        <th className={thCls}>卖出日</th>
                        <th className={cn(thCls, 'text-right')}>卖出价</th>
                        <th className={cn(thCls, 'text-right')}>数量</th>
                        <th className={cn(thCls, 'text-right')}>佣金</th>
                        <th className={cn(thCls, 'text-right')}>滑点</th>
                        <th className={cn(thCls, 'text-right')}>盈亏</th>
                        <th className={cn(thCls, 'pl-3 text-right')}>收益率</th>
                      </tr>
                    </thead>
                    <tbody>
                      {result.trades.map((t, i) => (
                        <tr key={i} className={trCls}>
                          <td className="py-2.5 pr-3 text-muted">{i + 1}</td>
                          <td className="py-2.5 px-3 tabular-nums">{t.entryTime.slice(0, 10)}</td>
                          <td className="py-2.5 px-3 text-right tabular-nums">{fmtMoney(t.entryPrice)}</td>
                          <td className="py-2.5 px-3 tabular-nums">{t.exitTime.slice(0, 10)}</td>
                          <td className="py-2.5 px-3 text-right tabular-nums">{fmtMoney(t.exitPrice)}</td>
                          <td className="py-2.5 px-3 text-right tabular-nums">{t.quantity}</td>
                          <td className="py-2.5 px-3 text-right tabular-nums text-muted-2">
                            {fmtMoney(t.commission ?? 0)}
                          </td>
                          <td className="py-2.5 px-3 text-right tabular-nums text-muted-2">
                            {fmtMoney(t.slippageCost ?? 0)}
                          </td>
                          <td className={cn('py-2.5 px-3 text-right tabular-nums', gainClass(t.profitLoss))}>
                            {t.profitLoss >= 0 ? '+' : ''}
                            {fmtMoney(t.profitLoss)}
                          </td>
                          <td className={cn('py-2.5 pl-3 text-right tabular-nums', gainClass(t.returnPct))}>
                            {t.returnPct >= 0 ? '+' : ''}
                            {t.returnPct.toFixed(2)}%
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              ) : (
                <EmptyState icon={<Activity size={18} />} title="该区间内无交易信号" hint="调整策略参数或回看区间后重试" />
              )}
            </CardContent>
          </Card>
        </>
      )}
    </>
  )
}

function CheckLevelIcon({ level }: { level: string }) {
  if (level === 'block') return <XCircle size={14} className="mt-0.5 shrink-0 text-down" />
  if (level === 'warn') return <AlertTriangle size={14} className="mt-0.5 shrink-0 text-amber-600 dark:text-amber-400" />
  if (level === 'info') return <Info size={14} className="mt-0.5 shrink-0 text-muted-2" />
  return <CheckCircle2 size={14} className="mt-0.5 shrink-0 text-up" />
}

function Field({
  label,
  icon,
  hint,
  children
}: {
  label: string
  icon?: ReactNode
  hint?: string
  children: ReactNode
}) {
  return (
    <div>
      <div className="mb-1.5 flex items-center gap-1.5">
        {icon && <span className="text-muted-2">{icon}</span>}
        <label className="text-xs font-medium text-muted">{label}</label>
      </div>
      {children}
      {hint && <p className="mt-1 text-[11px] leading-tight text-muted-2">{hint}</p>}
    </div>
  )
}

function EmptyState({ icon, title, hint }: { icon: ReactNode; title: string; hint?: string }) {
  return (
    <div className="flex flex-col items-center gap-2 py-10 text-center">
      <div className="grid h-10 w-10 place-items-center rounded-xl bg-surface-2 text-muted-2">{icon}</div>
      <p className="text-sm text-muted">{title}</p>
      {hint && <p className="text-xs text-muted-2">{hint}</p>}
    </div>
  )
}

function Metric({
  icon,
  label,
  value,
  cls,
  sub
}: {
  icon: ReactNode
  label: string
  value: string
  cls: string
  sub?: string
}) {
  return (
    <Card className="p-4">
      <div className="flex items-center justify-between">
        <span className="text-xs text-muted">{label}</span>
        <span className="text-muted-2">{icon}</span>
      </div>
      <p className={cn('mt-2 text-xl font-semibold tabular', cls)}>{value}</p>
      {sub && <p className="mt-1 text-xs text-muted-2">{sub}</p>}
    </Card>
  )
}
