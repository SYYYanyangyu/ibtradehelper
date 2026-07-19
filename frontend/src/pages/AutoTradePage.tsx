import { useState, useEffect, useCallback, type ReactNode } from 'react'
import {
  Play,
  Square,
  Plus,
  Trash2,
  Zap,
  Power,
  RefreshCw,
  AlertTriangle,
  Loader2,
  CheckCircle2,
  Bot,
  Layers,
  Activity,
  ShieldCheck,
  Clock,
  Radio,
} from 'lucide-react'
import { Card, CardContent, CardHeader, CardTitle } from '../components/ui/Card'
import { Badge } from '../components/ui/Badge'
import { cn, fmtMoney, fmtNum, signalMeta, modeMeta, dryRunMeta, eventTypeMeta, orderTypeLabel, fmtTime } from '../lib/utils'
import {
  getAutoTradeStatus,
  startAutoTrade,
  stopAutoTrade,
  updateAutoTradeSettings,
  addAutoTradeConfig,
  removeAutoTradeConfig,
  evaluateAutoTrade,
  listStrategies,
} from '../lib/api'
import type { AutoTradeStatus, AutoTradeSymbolConfig, AutoTradeSettings } from '../types'
import type { StrategyDef } from '../lib/api'

const inputCls =
  'w-full rounded-lg border border-border/70 bg-surface px-3 py-2 text-sm text-foreground outline-none transition placeholder:text-muted-2 focus:border-primary/50 focus:ring-2 focus:ring-primary/15'

function Toggle({ checked, onChange }: { checked: boolean; onChange: () => void }) {
  return (
    <button
      type="button"
      onClick={onChange}
      className={cn(
        'relative h-[22px] w-[40px] shrink-0 rounded-full transition-colors duration-200',
        checked ? 'bg-up' : 'bg-surface-2'
      )}
    >
      <span
        className={cn(
          'absolute top-[2px] h-[18px] w-[18px] rounded-full bg-white shadow-sm transition-transform duration-200',
          checked ? 'translate-x-[20px]' : 'translate-x-[2px]'
        )}
      />
    </button>
  )
}

export function AutoTradePage() {
  const [status, setStatus] = useState<AutoTradeStatus | null>(null)
  const [strategies, setStrategies] = useState<StrategyDef[]>([])
  const [strategyMap, setStrategyMap] = useState<Record<string, string>>({})
  const [initializing, setInitializing] = useState(true)
  const [loading, setLoading] = useState(false)
  const [busy, setBusy] = useState(false)
  const [evalSym, setEvalSym] = useState<string | null>(null)
  const [toast, setToast] = useState<{ kind: 'ok' | 'err'; text: string } | null>(null)

  const [settingsDraft, setSettingsDraft] = useState<AutoTradeSettings | null>(null)

  const [showAdd, setShowAdd] = useState(false)
  const [addForm, setAddForm] = useState({
    symbol: '',
    strategyKey: 'sma_cross',
    quantity: '100',
    barSize: '1 day',
    duration: '1 Y',
    orderType: 'Market' as 'Market' | 'Limit',
    limitPrice: '',
    dryRun: true,
    params: {} as Record<string, string>,
  })

  const showToast = useCallback((kind: 'ok' | 'err', text: string) => {
    setToast({ kind, text })
    window.setTimeout(() => setToast(null), 3500)
  }, [])

  const loadAll = useCallback(async () => {
    setInitializing(true)
    try {
      const [s, st] = await Promise.all([getAutoTradeStatus(), listStrategies()])
      setStatus(s)
      const list = st || []
      setStrategies(list)
      const m: Record<string, string> = {}
      list.forEach((x) => (m[x.key] = x.name))
      setStrategyMap(m)
      setSettingsDraft({
        enabled: s.settings.enabled,
        pollIntervalSeconds: s.settings.pollIntervalSeconds,
        useCompletedBarsOnly: s.settings.useCompletedBarsOnly !== false
      })
      setAddForm((prev) => {
        const inList = list.some((x) => x.key === prev.strategyKey)
        const key = inList ? prev.strategyKey : list[0]?.key ?? 'sma_cross'
        const def = list.find((x) => x.key === key)
        const params: Record<string, string> = {}
        def?.parameters.forEach((p) => (params[p.key] = String(p.defaultValue)))
        return { ...prev, strategyKey: key, params }
      })
    } catch (e) {
      showToast('err', e instanceof Error ? e.message : '加载失败，请确认交易引擎(C# 8090)已启动')
    } finally {
      setInitializing(false)
    }
  }, [showToast])

  const refreshStatus = useCallback(async () => {
    setLoading(true)
    try {
      setStatus(await getAutoTradeStatus())
    } catch (e) {
      showToast('err', e instanceof Error ? e.message : '刷新失败')
    } finally {
      setLoading(false)
    }
  }, [showToast])

  useEffect(() => {
    loadAll()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useEffect(() => {
    if (!status?.running) return
    const id = window.setInterval(() => void refreshStatus(), 15000)
    return () => window.clearInterval(id)
  }, [status?.running, refreshStatus])

  async function toggleEngine() {
    if (!status) return
    setBusy(true)
    setToast(null)
    try {
      if (status.running) await stopAutoTrade()
      else await startAutoTrade()
      await refreshStatus()
    } catch (e) {
      showToast('err', e instanceof Error ? e.message : '操作失败')
    } finally {
      setBusy(false)
    }
  }

  async function saveSettings() {
    if (!settingsDraft) return
    setBusy(true)
    try {
      await updateAutoTradeSettings({
        enabled: !!settingsDraft.enabled,
        pollIntervalSeconds: Number(settingsDraft.pollIntervalSeconds) || 120,
        useCompletedBarsOnly: settingsDraft.useCompletedBarsOnly !== false
      })
      showToast('ok', '全局设置已保存')
      await refreshStatus()
    } catch (e) {
      showToast('err', e instanceof Error ? e.message : '保存失败')
    } finally {
      setBusy(false)
    }
  }

  async function evaluate(symbol: string) {
    setEvalSym(symbol)
    setToast(null)
    try {
      await evaluateAutoTrade(symbol)
      showToast('ok', `已对 ${symbol} 评估一次`)
      await refreshStatus()
    } catch (e) {
      showToast('err', e instanceof Error ? e.message : '评估失败')
    } finally {
      setEvalSym(null)
    }
  }

  async function toggleDryRun(row: AutoTradeSymbolConfig) {
    setBusy(true)
    try {
      await addAutoTradeConfig({ ...row, dryRun: !row.dryRun })
      showToast('ok', `${row.symbol} 已切换为${!row.dryRun ? '实盘' : '演练'}`)
      await refreshStatus()
    } catch (e) {
      showToast('err', e instanceof Error ? e.message : '切换失败')
    } finally {
      setBusy(false)
    }
  }

  async function toggleEnabled(row: AutoTradeSymbolConfig) {
    setBusy(true)
    try {
      await addAutoTradeConfig({ ...row, enabled: !row.enabled })
      showToast('ok', `${row.symbol} 已${row.enabled ? '停用' : '启用'}`)
      await refreshStatus()
    } catch (e) {
      showToast('err', e instanceof Error ? e.message : '切换失败')
    } finally {
      setBusy(false)
    }
  }

  async function remove(symbol: string) {
    if (!window.confirm(`移除 ${symbol} 的 Quantitative Trading 订阅？`)) return
    setBusy(true)
    try {
      await removeAutoTradeConfig(symbol)
      showToast('ok', `已移除 ${symbol}`)
      await refreshStatus()
    } catch (e) {
      showToast('err', e instanceof Error ? e.message : '移除失败')
    } finally {
      setBusy(false)
    }
  }

  function onStrategyChange(key: string) {
    setAddForm((prev) => {
      const def = strategies.find((s) => s.key === key)
      const params: Record<string, string> = {}
      def?.parameters.forEach((p) => (params[p.key] = String(p.defaultValue)))
      return { ...prev, strategyKey: key, params }
    })
  }

  async function addConfig() {
    const symbol = addForm.symbol.trim().toUpperCase()
    const qty = Number(addForm.quantity)
    if (!symbol) return showToast('err', '请填写标的代码')
    if (!qty || qty <= 0) return showToast('err', '数量必须为正数')
    let limitPrice: number | null = null
    if (addForm.orderType === 'Limit') {
      const lp = Number(addForm.limitPrice)
      if (!lp || lp <= 0) return showToast('err', '限价单需填写有效的限价')
      limitPrice = lp
    }
    const def = strategies.find((s) => s.key === addForm.strategyKey)
    const parameters: Record<string, number> = {}
    def?.parameters.forEach((p) => {
      const raw = addForm.params[p.key]
      parameters[p.key] = raw !== undefined && raw !== '' ? Number(raw) : p.defaultValue
    })

    setBusy(true)
    try {
      await addAutoTradeConfig({
        symbol,
        strategyKey: addForm.strategyKey,
        parameters,
        barSize: addForm.barSize,
        duration: addForm.duration,
        quantity: qty,
        orderType: addForm.orderType,
        limitPrice,
        enabled: true,
        dryRun: addForm.dryRun,
      })
      showToast('ok', `已订阅 ${symbol}（默认演练模式，不执行交易）`)
      setAddForm({
        symbol: '',
        strategyKey: strategies[0]?.key ?? 'sma_cross',
        quantity: '100',
        barSize: '1 day',
        duration: '1 Y',
        orderType: 'Market',
        limitPrice: '',
        dryRun: true,
        params: defaultParams(strategies[0]),
      })
      setShowAdd(false)
      await refreshStatus()
    } catch (e) {
      showToast('err', e instanceof Error ? e.message : '订阅失败')
    } finally {
      setBusy(false)
    }
  }

  if (initializing || status === null) {
    return (
      <div className="flex items-center justify-center py-24">
        <Loader2 className="h-7 w-7 animate-spin text-muted-2" />
      </div>
    )
  }

  const sd = settingsDraft
  const risk = status.risk
  const symbols = status.symbols || []
  const enabledCount = symbols.filter((s) => s.config.enabled).length

  return (
    <>
      {/* Toast */}
      {toast && (
        <div
          className={cn(
            'flex items-center gap-2 rounded-xl border px-4 py-2.5 text-sm animate-fade-up',
            toast.kind === 'ok'
              ? 'border-up/25 bg-up/8 text-up'
              : 'border-down/25 bg-down/8 text-down'
          )}
        >
          {toast.kind === 'ok' ? <CheckCircle2 size={15} /> : <AlertTriangle size={15} />}
          {toast.text}
        </div>
      )}

      {/* ── 页面标题 ── */}
      <section className="flex flex-wrap items-center gap-4">
        <div className="flex items-center gap-3">
          <div className="grid h-10 w-10 place-items-center rounded-xl bg-primary/10 text-primary">
            <Bot size={20} />
          </div>
          <div>
            <h1 className="text-lg font-semibold tracking-tight text-foreground">量化交易</h1>
            <p className="text-xs text-muted-2">拉 K 线 → 跑策略 → 持仓去抖 → 风控交易 · 默认演练</p>
          </div>
        </div>

        <div className="ml-auto flex items-center gap-2">
          {/* 运行状态 */}
          <div
            className={cn(
              'inline-flex items-center gap-1.5 rounded-lg border px-2.5 py-1 text-xs font-medium',
              status.running
                ? 'border-up/20 bg-up/8 text-up'
                : 'border-border/70 bg-surface-2/60 text-muted'
            )}
          >
            <span className="relative flex h-1.5 w-1.5">
              {status.running && (
                <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-up opacity-75" />
              )}
              <span
                className={cn(
                  'relative inline-flex h-1.5 w-1.5 rounded-full',
                  status.running ? 'bg-up' : 'bg-muted-2'
                )}
              />
            </span>
            {status.running ? '运行中' : '已停止'}
            {risk && (
              <span className="ml-1 border-l border-current/20 pl-1.5 text-[0.65rem] font-normal opacity-70">
                {modeMeta(risk.mode).label}
              </span>
            )}
          </div>

          {/* 刷新 */}
          <button
            onClick={refreshStatus}
            disabled={loading}
            title="刷新"
            className="grid h-8 w-8 place-items-center rounded-lg text-muted transition hover:bg-surface-2 hover:text-foreground disabled:opacity-40"
          >
            <RefreshCw size={14} className={loading ? 'animate-spin' : ''} />
          </button>
        </div>
      </section>

      {/* ── 概览指标 ── */}
      <section className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        <MiniStat
          icon={<Radio size={15} />}
          label="引擎状态"
          value={status.running ? '运行中' : '已停止'}
          valueCls={status.running ? 'text-up' : 'text-muted-2'}
          sub={`${enabledCount}/${symbols.length} 标的已启用`}
          live={status.running}
        />
        <MiniStat icon={<Layers size={15} />} label="订阅标的" value={String(symbols.length)} sub="监控中" />
        <MiniStat icon={<ShieldCheck size={15} />} label="启用中" value={String(enabledCount)} valueCls="text-up" sub="参与评估" />
        <MiniStat
          icon={<Activity size={15} />}
          label="信号 / 事件"
          value={String(status.events?.length ?? 0)}
          valueCls="text-primary"
          sub="本轮累计"
        />
      </section>

      {/* ── 引擎控制 ── */}
      <Card className="animate-fade-up">
        <CardHeader>
          <CardTitle>引擎控制</CardTitle>
          <span className="text-xs text-muted-2">总开关关闭时循环空转，不评估任何标的</span>
        </CardHeader>
        <CardContent className="space-y-5">
          {/* 启停 + 状态 */}
          <div className="flex flex-wrap items-center gap-3">
            <button
              onClick={toggleEngine}
              disabled={busy}
              className={cn(
                'inline-flex items-center gap-1.5 rounded-xl px-5 py-2.5 text-sm font-semibold text-white transition disabled:opacity-50',
                status.running
                  ? 'bg-down hover:bg-down/90'
                  : 'bg-primary hover:opacity-90'
              )}
            >
              {busy ? (
                <Loader2 size={15} className="animate-spin" />
              ) : status.running ? (
                <Square size={15} />
              ) : (
                <Play size={15} />
              )}
              {status.running ? '停止引擎' : '启动引擎'}
            </button>

            <span
              className={cn(
                'inline-flex items-center gap-1.5 rounded-lg border px-2.5 py-1 text-xs font-medium',
                status.running ? 'border-up/20 bg-up/8 text-up' : 'border-border/70 text-muted'
              )}
            >
              <span className="relative flex h-2 w-2">
                {status.running && (
                  <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-up opacity-75" />
                )}
                <span className={cn('relative inline-flex h-2 w-2 rounded-full', status.running ? 'bg-up' : 'bg-muted-2')} />
              </span>
              {status.running
                ? `每 ${status.settings.pollIntervalSeconds}s 评估`
                : '已停止'}
            </span>

            {!status.settings.enabled && (
              <span className="inline-flex items-center gap-1 text-xs text-down/80">
                <AlertTriangle size={13} /> 总开关未开启，启动后也会空转
              </span>
            )}
          </div>

          <div className="h-px bg-border/50" />

          {/* 设置项 */}
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <div className="flex items-center justify-between gap-3 rounded-xl border border-border/60 bg-surface/50 px-4 py-3">
              <div>
                <p className="text-sm font-medium text-foreground">交易总开关</p>
                <p className="text-[11px] text-muted-2">关闭后拒绝一切交易</p>
              </div>
              <Toggle checked={!!sd?.enabled} onChange={() => sd && setSettingsDraft({ ...sd, enabled: !sd.enabled })} />
            </div>

            <div className="flex items-center justify-between gap-3 rounded-xl border border-border/60 bg-surface/50 px-4 py-3">
              <div>
                <p className="text-sm font-medium text-foreground">仅已收盘 K 线</p>
                <p className="text-[11px] text-muted-2">避免盘中假信号（推荐开）</p>
              </div>
              <Toggle
                checked={sd?.useCompletedBarsOnly !== false}
                onChange={() => sd && setSettingsDraft({ ...sd, useCompletedBarsOnly: !sd.useCompletedBarsOnly })}
              />
            </div>

            <div>
              <label className="mb-1.5 block text-xs font-medium text-muted">轮询周期（秒）</label>
              <input
                type="number"
                min={10}
                value={sd?.pollIntervalSeconds ?? 120}
                onChange={(e) => sd && setSettingsDraft({ ...sd, pollIntervalSeconds: Number(e.target.value) })}
                className={inputCls}
              />
            </div>

            <div className="flex items-end">
              <button
                onClick={saveSettings}
                disabled={busy}
                className="inline-flex w-full items-center justify-center gap-1.5 rounded-xl border border-border/70 px-4 py-2.5 text-sm font-medium text-foreground transition hover:bg-surface-2 disabled:opacity-50"
              >
                {busy ? <Loader2 size={14} className="animate-spin" /> : null}
                保存设置
              </button>
            </div>
          </div>
        </CardContent>
      </Card>

      {/* ── 订阅管理 ── */}
      <Card className="animate-fade-up">
        <CardHeader>
          <CardTitle>订阅管理</CardTitle>
          <div className="flex items-center gap-2">
            <span className="text-xs text-muted-2">{symbols.length} 个标的</span>
            <button
              onClick={() => setShowAdd((v) => !v)}
              className="inline-flex items-center gap-1 rounded-lg border border-border/70 px-2.5 py-1 text-xs font-medium text-primary transition hover:bg-primary/5"
            >
              <Plus size={12} /> {showAdd ? '收起' : '新增'}
            </button>
          </div>
        </CardHeader>
        <CardContent>
          {/* 新增表单 */}
          {showAdd && (
            <div className="mb-5 grid grid-cols-1 gap-3 rounded-xl border border-border/60 bg-surface/40 p-4 sm:grid-cols-2 lg:grid-cols-3">
              <div>
                <label className="mb-1.5 block text-xs font-medium text-muted">标的代码</label>
                <input
                  placeholder="NVDA"
                  value={addForm.symbol}
                  onChange={(e) => setAddForm({ ...addForm, symbol: e.target.value.toUpperCase() })}
                  className={cn(inputCls, 'uppercase')}
                />
              </div>
              <div>
                <label className="mb-1.5 block text-xs font-medium text-muted">策略</label>
                <select
                  value={addForm.strategyKey}
                  onChange={(e) => onStrategyChange(e.target.value)}
                  className={inputCls}
                >
                  {strategies.map((s) => (
                    <option key={s.key} value={s.key}>
                      {s.name}
                    </option>
                  ))}
                </select>
              </div>
              <div>
                <label className="mb-1.5 block text-xs font-medium text-muted">数量</label>
                <input
                  type="number"
                  min={1}
                  value={addForm.quantity}
                  onChange={(e) => setAddForm({ ...addForm, quantity: e.target.value })}
                  className={inputCls}
                />
              </div>
              <div>
                <label className="mb-1.5 block text-xs font-medium text-muted">K 线粒度</label>
                <input
                  value={addForm.barSize}
                  onChange={(e) => setAddForm({ ...addForm, barSize: e.target.value })}
                  className={cn(inputCls, 'uppercase')}
                />
              </div>
              <div>
                <label className="mb-1.5 block text-xs font-medium text-muted">回看区间</label>
                <input
                  value={addForm.duration}
                  onChange={(e) => setAddForm({ ...addForm, duration: e.target.value })}
                  className={cn(inputCls, 'uppercase')}
                />
              </div>
              <div>
                <label className="mb-1.5 block text-xs font-medium text-muted">交易类型</label>
                <div className="grid grid-cols-2 gap-2">
                  {(['Market', 'Limit'] as const).map((t) => (
                    <button
                      key={t}
                      onClick={() => setAddForm({ ...addForm, orderType: t })}
                      className={cn(
                        'rounded-lg border px-3 py-2 text-sm font-medium transition',
                        addForm.orderType === t
                          ? 'border-primary/40 bg-primary/8 text-primary'
                          : 'border-border/70 text-muted hover:bg-surface-2'
                      )}
                    >
                      {t === 'Market' ? '市价' : '限价'}
                    </button>
                  ))}
                </div>
              </div>

              {addForm.orderType === 'Limit' && (
                <div className="sm:col-span-2 lg:col-span-3">
                  <label className="mb-1.5 block text-xs font-medium text-muted">限价</label>
                  <input
                    type="number"
                    min={0}
                    step="any"
                    value={addForm.limitPrice}
                    onChange={(e) => setAddForm({ ...addForm, limitPrice: e.target.value })}
                    placeholder="0.00"
                    className={inputCls}
                  />
                </div>
              )}

              {/* 策略参数 */}
              <div className="sm:col-span-2 lg:col-span-3">
                <div className="mb-1 flex items-center gap-3">
                  <span className="h-px flex-1 bg-border/50" />
                  <span className="text-[11px] font-medium text-muted-2">
                    策略参数 · {strategies.find((s) => s.key === addForm.strategyKey)?.name ?? ''}
                  </span>
                  <span className="h-px flex-1 bg-border/50" />
                </div>
              </div>
              {strategies.find((s) => s.key === addForm.strategyKey)?.parameters.map((p) => (
                <div key={p.key}>
                  <label className="mb-1.5 block text-xs font-medium text-muted">{p.label}</label>
                  <input
                    type="number"
                    min={p.min ?? undefined}
                    max={p.max ?? undefined}
                    step={p.step ?? 1}
                    value={addForm.params[p.key] ?? String(p.defaultValue)}
                    onChange={(e) =>
                      setAddForm((prev) => ({ ...prev, params: { ...prev.params, [p.key]: e.target.value } }))
                    }
                    className={inputCls}
                  />
                </div>
              ))}

              <div className="flex items-center justify-between gap-3 sm:col-span-2 lg:col-span-3">
                <label className="flex items-center gap-2 text-sm text-muted">
                  <input
                    type="checkbox"
                    checked={addForm.dryRun}
                    onChange={(e) => setAddForm({ ...addForm, dryRun: e.target.checked })}
                    className="h-4 w-4 accent-primary"
                  />
                  演练模式（只发信号不执行）— 建议保持开启
                </label>
                <button
                  onClick={addConfig}
                  disabled={busy}
                  className="inline-flex items-center gap-1.5 rounded-xl bg-primary px-4 py-2.5 text-sm font-semibold text-white transition hover:opacity-90 disabled:opacity-50"
                >
                  {busy ? <Loader2 size={14} className="animate-spin" /> : <Plus size={14} />}
                  添加订阅
                </button>
              </div>
            </div>
          )}

          {/* 订阅列表 */}
          {symbols.length === 0 ? (
            <div className="flex flex-col items-center gap-2 py-10 text-center">
              <div className="grid h-10 w-10 place-items-center rounded-xl bg-surface-2 text-muted-2">
                <Layers size={18} />
              </div>
              <p className="text-sm text-muted">尚未订阅任何标的</p>
              <p className="text-xs text-muted-2">点击「新增」开始，默认演练模式</p>
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full min-w-[760px] text-sm">
                <thead>
                  <tr className="text-left text-[11px] uppercase tracking-wider text-muted-2">
                    <th className="pb-2 pr-3 font-medium">标的</th>
                    <th className="pb-2 px-3 font-medium">策略</th>
                    <th className="pb-2 px-3 font-medium text-right">数量</th>
                    <th className="pb-2 px-3 font-medium">类型</th>
                    <th className="pb-2 px-3 font-medium">模式</th>
                    <th className="pb-2 px-3 font-medium">状态</th>
                    <th className="hidden pb-2 px-3 font-medium lg:table-cell">信号</th>
                    <th className="hidden pb-2 px-3 font-medium text-right md:table-cell">持仓</th>
                    <th className="hidden pb-2 px-3 font-medium text-right lg:table-cell">挂单</th>
                    <th className="hidden pb-2 px-3 font-medium xl:table-cell">最近动作</th>
                    <th className="pb-2 pl-3 font-medium text-right">操作</th>
                  </tr>
                </thead>
                <tbody>
                  {symbols.map((s) => {
                    const cfg = s.config
                    const rt = s.runtime || {}
                    const sig = signalMeta(rt.lastSignal)
                    const dr = dryRunMeta(cfg.dryRun)
                    return (
                      <tr key={cfg.symbol} className="border-t border-border/50 transition hover:bg-surface/30">
                        <td className="py-3 pr-3 font-semibold text-foreground">{cfg.symbol}</td>
                        <td className="py-3 px-3 text-muted">{strategyMap[cfg.strategyKey] || cfg.strategyKey}</td>
                        <td className="py-3 px-3 text-right tabular-nums text-foreground">{fmtNum(cfg.quantity)}</td>
                        <td className="py-3 px-3 text-muted">
                          {orderTypeLabel(cfg.orderType)}
                          {cfg.orderType === 'Limit' && cfg.limitPrice != null && (
                            <span className="text-foreground"> @ {fmtMoney(cfg.limitPrice)}</span>
                          )}
                        </td>
                        <td className="py-3 px-3">
                          <Badge tone={dr.tone}>{dr.label}</Badge>
                        </td>
                        <td className="py-3 px-3">
                          <Badge tone={cfg.enabled ? 'up' : 'neutral'}>{cfg.enabled ? '启用' : '停用'}</Badge>
                        </td>
                        <td className="hidden py-3 px-3 lg:table-cell">
                          {rt.lastSignal ? (
                            <div className="flex flex-col gap-0.5">
                              <Badge tone={sig.tone}>{sig.label}</Badge>
                              {rt.signalBarTime && (
                                <span className="text-[10px] tabular-nums text-muted-2" title={rt.usedPenultimateBar ? '未收盘，用倒数第二根' : '已收盘棒'}>
                                  {String(rt.signalBarTime).slice(0, 10)}
                                  {rt.usedPenultimateBar ? ' · 前棒' : ''}
                                </span>
                              )}
                            </div>
                          ) : (
                            <span className="text-xs text-muted-2">—</span>
                          )}
                        </td>
                        <td className="hidden py-3 px-3 text-right tabular-nums text-muted md:table-cell">{fmtNum(rt.netQty ?? 0)}</td>
                        <td className="hidden py-3 px-3 text-right tabular-nums text-muted lg:table-cell">{rt.openOrders ?? 0}</td>
                        <td className="hidden max-w-[16rem] truncate py-3 px-3 text-xs text-muted-2 xl:table-cell" title={rt.lastAction || ''}>
                          {rt.lastAction || '—'}
                          {rt.lastError && <span className="text-down/70"> · {rt.lastError}</span>}
                        </td>
                        <td className="py-3 pl-3">
                          <div className="flex items-center justify-end gap-0.5">
                            <ActionBtn
                              onClick={() => toggleEnabled(cfg)}
                              title={cfg.enabled ? '停用' : '启用'}
                            >
                              <Power size={13} />
                            </ActionBtn>
                            <ActionBtn
                              onClick={() => evaluate(cfg.symbol)}
                              disabled={evalSym === cfg.symbol}
                              title="立即评估"
                              highlight
                            >
                              {evalSym === cfg.symbol ? <Loader2 size={13} className="animate-spin" /> : <Zap size={13} />}
                            </ActionBtn>
                            <ActionBtn
                              onClick={() => toggleDryRun(cfg)}
                              title={cfg.dryRun ? '转实盘' : '转演练'}
                            >
                              {cfg.dryRun ? <Play size={13} /> : <Square size={13} />}
                            </ActionBtn>
                            <ActionBtn
                              onClick={() => remove(cfg.symbol)}
                              title="移除"
                              danger
                            >
                              <Trash2 size={13} />
                            </ActionBtn>
                          </div>
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
          )}
        </CardContent>
      </Card>

      {/* ── 审计日志 ── */}
      <Card className="animate-fade-up">
        <CardHeader>
          <CardTitle>审计日志</CardTitle>
          <span className="text-xs text-muted-2">每轮评估 / 每笔下产生的可追踪记录</span>
        </CardHeader>
        <CardContent>
          {!status.events || status.events.length === 0 ? (
            <div className="flex flex-col items-center gap-2 py-10 text-center">
              <div className="grid h-10 w-10 place-items-center rounded-xl bg-surface-2 text-muted-2">
                <Clock size={18} />
              </div>
              <p className="text-sm text-muted">暂无事件</p>
              <p className="text-xs text-muted-2">启动引擎或点击「立即评估」后将出现记录</p>
            </div>
          ) : (
            <div className="max-h-[420px] space-y-1 overflow-auto pr-1">
              {status.events.map((e, i) => {
                const et = eventTypeMeta(e.type)
                const sig = e.signal ? signalMeta(e.signal) : null
                return (
                  <div key={`${e.time}-${i}`} className="flex items-start gap-2.5 rounded-lg border border-border/50 bg-surface/30 px-3 py-2">
                    <span className="mt-px shrink-0 text-[0.65rem] tabular-nums text-muted-2">{fmtTime(e.time)}</span>
                    <Badge tone={et.tone} className="shrink-0">{et.label}</Badge>
                    {e.symbol && <span className="shrink-0 font-mono text-xs text-muted">{e.symbol}</span>}
                    {sig && <Badge tone={sig.tone} className="shrink-0">{sig.label}</Badge>}
                    <span className="min-w-0 break-all text-xs leading-relaxed text-muted-2">{e.message}</span>
                  </div>
                )
              })}
            </div>
          )}
        </CardContent>
      </Card>
    </>
  )
}

function defaultParams(def?: StrategyDef): Record<string, string> {
  const out: Record<string, string> = {}
  def?.parameters.forEach((p) => (out[p.key] = String(p.defaultValue)))
  return out
}

function ActionBtn({
  onClick,
  disabled,
  title,
  highlight,
  danger,
  children,
}: {
  onClick: () => void
  disabled?: boolean
  title: string
  highlight?: boolean
  danger?: boolean
  children: ReactNode
}) {
  return (
    <button
      onClick={onClick}
      disabled={disabled}
      title={title}
      className={cn(
        'grid h-7 w-7 place-items-center rounded-lg border transition disabled:opacity-40',
        highlight
          ? 'border-primary/20 text-primary hover:bg-primary/10'
          : danger
            ? 'border-border/60 text-muted hover:border-down/30 hover:bg-down/8 hover:text-down'
            : 'border-border/60 text-muted hover:bg-surface-2 hover:text-foreground'
      )}
    >
      {children}
    </button>
  )
}

function MiniStat({
  icon,
  label,
  value,
  valueCls = 'text-foreground',
  sub,
  live,
}: {
  icon: ReactNode
  label: string
  value: string
  valueCls?: string
  sub?: string
  live?: boolean
}) {
  return (
    <Card className="p-4">
      <div className="flex items-center justify-between">
        <span className="text-[11px] font-medium uppercase tracking-wider text-muted-2">{label}</span>
        <span className="grid h-7 w-7 place-items-center rounded-lg bg-surface-2 text-muted">
          {live && (
            <span className="absolute inline-flex h-full w-full animate-ping rounded-lg bg-up/40" />
          )}
          {icon}
        </span>
      </div>
      <p className={cn('mt-2 text-xl font-bold tabular tracking-tight', valueCls)}>{value}</p>
      {sub && <p className="mt-0.5 text-xs text-muted-2">{sub}</p>}
    </Card>
  )
}
