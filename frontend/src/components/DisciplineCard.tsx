import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { CheckCircle2, XCircle, Info, Loader2, Settings2, ShieldCheck, RefreshCw } from 'lucide-react'
import { Card, CardContent, CardHeader, CardTitle } from './ui/Card'
import { cn } from '../lib/utils'
import { checkDiscipline, getDiscipline, updateDisciplineSettings, type PlaceOrderRequest } from '../lib/api'
import type { DisciplineCheckResult, DisciplineSettings, DisciplineStatus } from '../types'

const inputCls =
  'w-full rounded-lg border border-border/70 bg-surface px-2.5 py-1.5 text-sm tabular text-foreground outline-none focus:border-primary/50 focus:ring-2 focus:ring-primary/15'

const pnlCls = (v: number) => (v > 0 ? 'text-up' : v < 0 ? 'text-down' : 'text-muted')
const signed = (v: number) => `${v > 0 ? '+' : v < 0 ? '−' : ''}$${Math.abs(v).toFixed(2)}`

interface Props {
  /** 当前下单表单；只有期权买入且合约填全才自动预检。 */
  order: PlaceOrderRequest | null
  onResult?: (r: DisciplineCheckResult | null) => void
}

export function DisciplineCard({ order, onResult }: Props) {
  const [st, setSt] = useState<DisciplineStatus | null>(null)
  const [draft, setDraft] = useState<DisciplineSettings | null>(null)
  const [showSettings, setShowSettings] = useState(false)
  const [check, setCheck] = useState<DisciplineCheckResult | null>(null)
  const [checking, setChecking] = useState(false)
  const [saving, setSaving] = useState(false)
  const [err, setErr] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      const s = await getDiscipline()
      setSt(s)
      setDraft((d) => d ?? s.settings)
      setErr(null)
    } catch (e) {
      setErr(e instanceof Error ? e.message : '加载失败')
    }
  }, [])

  useEffect(() => {
    load()
    const id = window.setInterval(load, 30000)
    return () => window.clearInterval(id)
  }, [load])

  const key = order ? JSON.stringify(order) : ''
  const runCheck = useCallback(async () => {
    if (!order) return
    setChecking(true)
    try {
      const r = await checkDiscipline(order)
      setCheck(r)
      onResult?.(r)
    } catch (e) {
      setCheck(null)
      onResult?.(null)
      setErr(e instanceof Error ? e.message : '预检失败')
    } finally {
      setChecking(false)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key])

  useEffect(() => {
    setCheck(null)
    onResult?.(null)
    if (!order) return
    const id = window.setTimeout(runCheck, 700)
    return () => window.clearTimeout(id)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key])

  const save = async () => {
    if (!draft) return
    setSaving(true)
    try {
      const s = await updateDisciplineSettings(draft)
      setSt(s)
      setDraft(s.settings)
      setShowSettings(false)
      setErr(null)
      if (order) runCheck()
    } catch (e) {
      setErr(e instanceof Error ? e.message : '保存失败')
    } finally {
      setSaving(false)
    }
  }

  if (!st || !draft) {
    return (
      <Card className="p-5 text-sm text-muted-2">
        {err ? `纪律闸门：${err}` : <Loader2 size={16} className="animate-spin" />}
      </Card>
    )
  }

  const s = st.settings
  const ctx = st.context
  const num = (k: keyof DisciplineSettings, step = 1) => (
    <input
      type="number"
      step={step}
      min={0}
      className={inputCls}
      value={String(draft[k])}
      onChange={(e) => setDraft({ ...draft, [k]: Number(e.target.value) })}
    />
  )

  return (
    <Card className="animate-fade-up">
      <CardHeader>
        <div>
          <CardTitle>下单纪律闸门</CardTitle>
          <p className="mt-0.5 text-xs text-muted-2">
            只管手动的期权开仓（买入），平仓永远放行 · {s.symbols.join(' / ')} · 美东 {s.entryStart}–{s.entryEnd} ·
            单笔 ≤ ${s.maxPremium}
            {s.requireTrend && ` · 顺势（开盘区间突破 + VWAP ≥ ${s.vwapMinPct}%）`}
            {s.maxPrevVix > 0 && ` · 前日 VIX < ${s.maxPrevVix}`}
            {s.skipFomcDays && ' · 避开 FOMC'} · 连亏 {s.stopAfterLosses} 停 · 日亏 ${s.dailyLossLimit} / 周亏 $
            {s.weeklyLossLimit} 熔断
          </p>
        </div>
        <div className="flex items-center gap-1.5">
          <span
            className={cn(
              'rounded-lg border px-2 py-0.5 text-xs font-medium',
              !s.enabled
                ? 'border-border/70 text-muted'
                : s.mode === 'Block'
                  ? 'border-up/25 bg-up/8 text-up'
                  : 'border-amber-500/30 bg-amber-500/10 text-amber-500'
            )}
          >
            {!s.enabled ? '已关闭' : s.mode === 'Block' ? '拦截模式' : '仅提示'}
          </span>
          <button
            title="设置"
            onClick={() => setShowSettings((v) => !v)}
            className="rounded-lg border border-border/70 p-1.5 text-muted transition hover:bg-surface-2"
          >
            <Settings2 size={14} />
          </button>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-6">
          <Stat label={`今日已实现（美东 ${st.todayEt}）`}>
            <span className={pnlCls(st.todayRealized)}>{signed(st.todayRealized)}</span>
            <span className="text-xs text-muted-2"> / −${s.dailyLossLimit}</span>
          </Stat>
          <Stat label="今日笔数 · 连亏">
            {st.todayTrades} 笔 ·{' '}
            <span className={st.consecutiveLosses >= s.stopAfterLosses && s.stopAfterLosses > 0 ? 'text-down' : ''}>
              {st.consecutiveLosses}/{s.stopAfterLosses}
            </span>
          </Stat>
          <Stat label="本周已实现">
            <span className={pnlCls(st.weekRealized)}>{signed(st.weekRealized)}</span>
            <span className="text-xs text-muted-2"> / −${s.weeklyLossLimit}</span>
          </Stat>
          <Stat label="前日 VIX（FRED）">
            {ctx.prevVix != null ? (
              <span className={ctx.prevVix >= s.maxPrevVix && s.maxPrevVix > 0 ? 'text-down' : ''}>
                {ctx.prevVix} <span className="text-xs text-muted-2">{ctx.prevVixDate}</span>
              </span>
            ) : (
              <span className="text-down">未取到</span>
            )}
          </Stat>
          <Stat label="最新 VIX（Gateway，仅提示）">
            {ctx.liveVix != null ? (
              <span>
                {ctx.liveVix}
                {ctx.liveVixChangePct != null && (
                  <span className={cn('ml-1 text-xs', ctx.liveVixChangePct > 0 ? 'text-down' : 'text-up')}>
                    {ctx.liveVixChangePct > 0 ? '+' : ''}
                    {ctx.liveVixChangePct}%
                  </span>
                )}
              </span>
            ) : (
              <span className="text-muted-2">未取到</span>
            )}
          </Stat>
          <Stat label="FOMC（美联储官网）">
            {ctx.isFomcDay == null ? (
              <span className="text-down">未取到</span>
            ) : ctx.isFomcDay ? (
              <span className="text-down">今天议息</span>
            ) : (
              <span>下次 {ctx.nextFomc ?? '—'}</span>
            )}
          </Stat>
        </div>

        <div className="rounded-xl border border-border/60 bg-surface/40 p-3">
          <div className="mb-2 flex items-center justify-between">
            <p className="flex items-center gap-1.5 text-sm font-medium text-foreground">
              <ShieldCheck size={15} className="text-primary" />
              当前表单预检
            </p>
            <button
              onClick={runCheck}
              disabled={!order || checking}
              className="inline-flex items-center gap-1 rounded-lg border border-border/70 px-2 py-1 text-xs text-muted transition hover:bg-surface-2 disabled:opacity-40"
            >
              {checking ? <Loader2 size={12} className="animate-spin" /> : <RefreshCw size={12} />}
              重新检查
            </button>
          </div>
          {!order ? (
            <p className="text-xs text-muted-2">在左侧选「期权 OPT · 买入」，填好标的、数量、到期、行权价后自动检查。</p>
          ) : checking && !check ? (
            <p className="flex items-center gap-1.5 text-xs text-muted-2">
              <Loader2 size={12} className="animate-spin" /> 正在拉 VIX / FOMC / 分钟线…
            </p>
          ) : check ? (
            <>
              <p
                className={cn(
                  'mb-2 rounded-lg px-2.5 py-1.5 text-sm font-medium',
                  !check.applies
                    ? 'bg-surface-2 text-muted'
                    : check.items.every((i) => i.ok || !i.blocking)
                      ? 'bg-up/10 text-up'
                      : check.allowed
                        ? 'bg-amber-500/10 text-amber-500'
                        : 'bg-down/10 text-down'
                )}
              >
                {check.summary}
              </p>
              <ul className="space-y-1">
                {check.items.map((i) => (
                  <li key={i.key} className="flex items-start gap-2 text-xs">
                    {i.ok ? (
                      <CheckCircle2 size={14} className="mt-px shrink-0 text-up" />
                    ) : i.blocking ? (
                      <XCircle size={14} className="mt-px shrink-0 text-down" />
                    ) : (
                      <Info size={14} className="mt-px shrink-0 text-amber-500" />
                    )}
                    <span className="w-44 shrink-0 text-foreground">{i.label}</span>
                    <span className="text-muted-2">{i.detail}</span>
                  </li>
                ))}
              </ul>
            </>
          ) : null}
        </div>

        {showSettings && (
          <div className="space-y-3 rounded-xl border border-border/60 p-3">
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
              <Field label="启用">
                <select
                  className={inputCls}
                  value={draft.enabled ? '1' : '0'}
                  onChange={(e) => setDraft({ ...draft, enabled: e.target.value === '1' })}
                >
                  <option value="1">开</option>
                  <option value="0">关</option>
                </select>
              </Field>
              <Field label="模式">
                <select
                  className={inputCls}
                  value={draft.mode}
                  onChange={(e) => setDraft({ ...draft, mode: e.target.value as 'Block' | 'Warn' })}
                >
                  <option value="Block">拦截（不满足直接拒单）</option>
                  <option value="Warn">仅提示</option>
                </select>
              </Field>
              <Field label="标的白名单（逗号分隔）">
                <input
                  className={cn(inputCls, 'uppercase')}
                  value={draft.symbols.join(',')}
                  onChange={(e) => setDraft({ ...draft, symbols: e.target.value.split(',').map((x) => x.trim()) })}
                />
              </Field>
              <Field label="单笔权利金上限 $">{num('maxPremium', 10)}</Field>
              <Field label="开仓开始（美东）">
                <input className={inputCls} value={draft.entryStart} onChange={(e) => setDraft({ ...draft, entryStart: e.target.value })} />
              </Field>
              <Field label="开仓结束（美东）">
                <input className={inputCls} value={draft.entryEnd} onChange={(e) => setDraft({ ...draft, entryEnd: e.target.value })} />
              </Field>
              <Field label="要求顺势">
                <select
                  className={inputCls}
                  value={draft.requireTrend ? '1' : '0'}
                  onChange={(e) => setDraft({ ...draft, requireTrend: e.target.value === '1' })}
                >
                  <option value="1">是</option>
                  <option value="0">否</option>
                </select>
              </Field>
              <Field label="VWAP 同向最少 %">{num('vwapMinPct', 0.05)}</Field>
              <Field label="前日 VIX 上限（0 = 关）">{num('maxPrevVix', 0.5)}</Field>
              <Field label="避开 FOMC 决议日">
                <select
                  className={inputCls}
                  value={draft.skipFomcDays ? '1' : '0'}
                  onChange={(e) => setDraft({ ...draft, skipFomcDays: e.target.value === '1' })}
                >
                  <option value="1">是</option>
                  <option value="0">否</option>
                </select>
              </Field>
              <Field label="当日连亏 N 笔停手">{num('stopAfterLosses')}</Field>
              <Field label="当日亏损熔断 $">{num('dailyLossLimit', 50)}</Field>
              <Field label="本周亏损熔断 $">{num('weeklyLossLimit', 100)}</Field>
            </div>
            <div className="flex justify-end gap-2">
              <button
                onClick={() => {
                  setDraft(st.settings)
                  setShowSettings(false)
                }}
                className="rounded-lg border border-border/70 px-3 py-1.5 text-xs text-muted hover:bg-surface-2"
              >
                取消
              </button>
              <button
                onClick={save}
                disabled={saving}
                className="inline-flex items-center gap-1 rounded-lg bg-primary px-3 py-1.5 text-xs font-semibold text-white hover:opacity-90 disabled:opacity-50"
              >
                {saving && <Loader2 size={12} className="animate-spin" />}
                保存
              </button>
            </div>
          </div>
        )}

        {err && <p className="text-xs text-down">{err}</p>}
        <p className="text-[11px] leading-relaxed text-muted-2">
          {st.scope}。在 IBKR App / TWS 里直接下的单这里看不到，要等次日 Flex 同步后才进每周复盘。
        </p>
      </CardContent>
    </Card>
  )
}

function Stat({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="rounded-xl border border-border/60 bg-surface/40 px-3 py-2">
      <p className="text-[11px] text-muted-2">{label}</p>
      <p className="mt-0.5 text-sm font-semibold tabular text-foreground">{children}</p>
    </div>
  )
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <label className="block">
      <span className="mb-1 block text-xs text-muted">{label}</span>
      {children}
    </label>
  )
}
