import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { Play, Square, RefreshCw, LogOut, Settings2, Loader2, AlertTriangle } from 'lucide-react'
import { Card, CardContent, CardHeader, CardTitle } from './ui/Card'
import { cn, fmtMoney } from '../lib/utils'
import { getTrendScalper, updateTrendScalperSettings, trendScalperAction } from '../lib/api'
import { LiveVix } from './MarketNewsCard'
import type { TrendScalperSettings, TrendScalperStatus, TrendScalperTrade } from '../types'

const inputCls =
  'w-full rounded-lg border border-border/70 bg-surface px-2.5 py-1.5 text-sm tabular text-foreground outline-none focus:border-primary/50 focus:ring-2 focus:ring-primary/15'

const EXIT_LABEL: Record<TrendScalperTrade['exitReason'], string> = {
  tp: '止盈',
  sl: '止损',
  time: '超时',
  eod: '收盘前',
  manual: '手动',
}

const DECISION_LABEL: Record<string, string> = {
  'buy-call': '买 Call',
  'buy-put': '买 Put',
  exit: '平仓',
  hold: '观望',
}

const etTime = (iso: string) =>
  new Date(iso).toLocaleTimeString('en-US', { timeZone: 'America/New_York', hour12: false, hour: '2-digit', minute: '2-digit' })

const pnlCls = (v: number) => (v > 0 ? 'text-up' : v < 0 ? 'text-down' : 'text-muted')

export function TrendScalperCard() {
  const [st, setSt] = useState<TrendScalperStatus | null>(null)
  const [draft, setDraft] = useState<TrendScalperSettings | null>(null)
  const [showSettings, setShowSettings] = useState(false)
  const [busy, setBusy] = useState<string | null>(null)
  const [err, setErr] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      const s = await getTrendScalper()
      setSt(s)
      setDraft((d) => d ?? s.settings)
      setErr(null)
    } catch (e) {
      setErr(e instanceof Error ? e.message : '加载失败')
    }
  }, [])

  useEffect(() => {
    load()
    const id = window.setInterval(load, 10000)
    return () => window.clearInterval(id)
  }, [load])

  const act = async (a: 'start' | 'stop' | 'tick' | 'flatten') => {
    setBusy(a)
    try {
      setSt(await trendScalperAction(a))
      setErr(null)
    } catch (e) {
      setErr(e instanceof Error ? e.message : '操作失败')
    } finally {
      setBusy(null)
    }
  }

  const save = async () => {
    if (!draft) return
    setBusy('save')
    try {
      const s = await updateTrendScalperSettings(draft)
      setSt(s)
      setDraft(s.settings)
      setShowSettings(false)
      setErr(null)
    } catch (e) {
      setErr(e instanceof Error ? e.message : '保存失败')
    } finally {
      setBusy(null)
    }
  }

  if (!st || !draft) {
    return (
      <Card className="p-5 text-sm text-muted-2">
        {err ? `Trend Scalper：${err}` : <Loader2 size={16} className="animate-spin" />}
      </Card>
    )
  }

  const s = st.settings
  const pos = st.position
  const posMove =
    pos && st.lastUnderlying
      ? (pos.right === 'C' ? 1 : -1) * (st.lastUnderlying / pos.entryUnderlying - 1) * 100
      : null
  const num = (k: keyof TrendScalperSettings, step = 1) => (
    <input
      type="number"
      step={step}
      className={inputCls}
      value={String(draft[k])}
      onChange={(e) => setDraft({ ...draft, [k]: Number(e.target.value) })}
    />
  )
  const text = (k: 'entryStart' | 'entryEnd' | 'flatBy') => (
    <input className={inputCls} value={draft[k]} onChange={(e) => setDraft({ ...draft, [k]: e.target.value })} />
  )

  return (
    <Card className="animate-fade-up">
      <CardHeader>
        <div>
          <CardTitle>QQQ 0DTE Trend Scalper · Paper</CardTitle>
          <p className="mt-0.5 text-xs text-muted-2">
            开盘区间同向突破 + VWAP 同侧 ≥{s.vwapMinPct}% + 区间位置 ≥{s.rangePosMin} · 美东 {s.entryStart}–{s.entryEnd} 开仓 ·
            止盈 +{s.takeProfitPct}% / 止损 −{s.stopLossPct}%（按 QQQ）· 最长 {s.maxHoldMinutes} 分钟 · 单笔 ≤ ${s.budgetPerTrade} ·
            {s.useLimitOrders ? `限价单（mid ±${s.limitBufferPct}%，未成交撤单）` : '市价单'} ·
            连亏 {s.stopAfterLosses} 停 · 日 ≤{s.maxTradesPerDay} 笔
            {s.dailyLossLimit > 0 && ` · 日亏 ≤ $${s.dailyLossLimit}`}
            {s.maxPrevVix > 0 && ` · 前日 VIX ≥ ${s.maxPrevVix} 不做`}
            {s.skipFomcDays && ' · FOMC 决议日不做'}
          </p>
        </div>
        <div className="flex items-center gap-1.5">
          <span
            className={cn(
              'rounded-lg border px-2 py-0.5 text-xs font-medium',
              st.running ? 'border-up/25 bg-up/8 text-up' : 'border-border/70 text-muted'
            )}
          >
            {st.running ? '运行中' : '已停止'} · {st.riskMode}
          </span>
          {st.running ? (
            <IconBtn title="停止" onClick={() => act('stop')} busy={busy === 'stop'}>
              <Square size={14} />
            </IconBtn>
          ) : (
            <IconBtn title="启动（只 Paper）" onClick={() => act('start')} busy={busy === 'start'} highlight>
              <Play size={14} />
            </IconBtn>
          )}
          <IconBtn title="立即评估一次" onClick={() => act('tick')} busy={busy === 'tick'}>
            <RefreshCw size={14} />
          </IconBtn>
          <IconBtn title="参数" onClick={() => setShowSettings((v) => !v)}>
            <Settings2 size={14} />
          </IconBtn>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        <p className="rounded-lg border border-amber-500/25 bg-amber-500/8 px-3 py-2 text-xs text-amber-600 dark:text-amber-400">
          历史回测（165 个交易日 QQQ 1 分钟线）：裸规则利润因子 0.85；加 VIX &lt; 20 与避开 FOMC 后 0.99，约等于打平，且尚未计期权价差与佣金。Paper 只用于验证，不要据此上实盘。
        </p>

        <div className="flex flex-wrap items-center gap-x-4 gap-y-1 rounded-lg border border-border/60 px-3 py-2 text-xs tabular">
          <span className="text-muted-2">市场环境</span>
          <span>
            前日 VIX{' '}
            <b className={cn(st.context.prevVix != null && s.maxPrevVix > 0 && st.context.prevVix >= s.maxPrevVix ? 'text-down' : 'text-foreground')}>
              {st.context.prevVix ?? '—'}
            </b>
            {st.context.prevVixDate && <span className="text-muted-2">（{st.context.prevVixDate} · FRED）</span>}
          </span>
          <span>
            最新 VIX <LiveVix ctx={st.context} />
          </span>
          <span>
            FOMC：
            <b className={st.context.isFomcDay ? 'text-down' : 'text-foreground'}>
              {st.context.isFomcDay == null ? '—' : st.context.isFomcDay ? '今天是决议日' : '今天不是'}
            </b>
            {st.context.nextFomc && <span className="text-muted-2">（下次 {st.context.nextFomc} · federalreserve.gov）</span>}
          </span>
          {st.context.error && <span className="text-down">{st.context.error}</span>}
        </div>

        {(err || st.lastError) && (
          <div className="flex items-start gap-2 rounded-lg border border-down/25 bg-down/8 px-3 py-2 text-xs text-down">
            <AlertTriangle size={14} className="mt-0.5 shrink-0" />
            {err || st.lastError}
          </div>
        )}

        <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
          <Stat label="累计笔数" value={String(st.total.trades)} sub={`胜 ${st.total.wins}`} />
          <Stat label="胜率" value={st.total.trades ? `${(st.total.winRate * 100).toFixed(0)}%` : '—'} />
          <Stat label="累计盈亏" value={fmtMoney(st.total.pnl)} cls={pnlCls(st.total.pnl)} sub="不含佣金" />
          <Stat
            label="利润因子"
            value={st.total.profitFactor == null ? '—' : st.total.profitFactor.toFixed(2)}
            sub={st.total.trades ? `均赢 ${fmtMoney(st.total.avgWin)} / 均亏 ${fmtMoney(st.total.avgLoss)}` : undefined}
          />
          <Stat
            label={`今日 ${st.today.dateEt || ''}`}
            value={fmtMoney(st.today.realized)}
            cls={pnlCls(st.today.realized)}
            sub={`${st.today.trades} 笔 · 连亏 ${st.today.consecutiveLosses}`}
          />
          <Stat
            label="平均滑点 / 笔"
            value={st.total.avgSlippage == null ? '—' : fmtMoney(st.total.avgSlippage)}
            cls={st.total.avgSlippage != null && st.total.avgSlippage > 0 ? 'text-down' : undefined}
            sub={st.total.slippageTrades ? `${st.total.slippageTrades} 笔共 ${fmtMoney(st.total.totalSlippage)} · 正数为成本` : '成交价对比下单前 mid'}
          />
        </div>

        <div className="grid gap-3 lg:grid-cols-2">
          <div className="rounded-xl border border-border/60 p-3 text-sm">
            <div className="mb-1 flex items-center justify-between text-xs text-muted-2">
              <span>当前持仓</span>
              {pos && (
                <button
                  onClick={() => act('flatten')}
                  disabled={busy === 'flatten' || !!pos.pendingExitOrderId}
                  className="inline-flex items-center gap-1 rounded-md border border-down/30 px-2 py-0.5 text-down hover:bg-down/8 disabled:opacity-40"
                >
                  <LogOut size={12} /> 市价平仓
                </button>
              )}
            </div>
            {pos ? (
              <div className="space-y-0.5 tabular">
                <div className="font-medium text-foreground">
                  {pos.quantity} × {pos.occ} @ {pos.entryPrice.toFixed(2)}
                </div>
                <div className="text-xs text-muted">
                  开仓 {etTime(pos.openedAtUtc)} ET · QQQ {pos.entryUnderlying.toFixed(2)} → {st.lastUnderlying?.toFixed(2) ?? '—'}
                  {posMove != null && <span className={cn('ml-1', pnlCls(posMove))}>（同向 {posMove.toFixed(3)}%）</span>}
                  {pos.pendingExitOrderId && <span className="ml-1 text-amber-500">平仓单在途</span>}
                </div>
              </div>
            ) : (
              <div className="text-muted-2">空仓</div>
            )}
          </div>
          <div className="rounded-xl border border-border/60 p-3 text-sm">
            <div className="mb-1 flex items-center justify-between text-xs text-muted-2">
              <span>
                {st.session || '未评估'}
                {st.last?.barEt && ` · 最近已收盘 K 线 ${st.last.barEt} ET`}
              </span>
              {st.lastTickUtc && <span>评估于 {etTime(st.lastTickUtc)} ET</span>}
            </div>
            {st.last ? (
              <>
                <div className="text-foreground">
                  <span className="mr-1 font-medium">{DECISION_LABEL[st.last.decision] ?? st.last.decision}</span>
                  <span className="text-muted">{st.last.reason}</span>
                </div>
                {st.last.price != null && st.last.vwap != null && (
                  <div className="mt-1 text-xs tabular text-muted-2">
                    QQQ {st.last.price} · VWAP {st.last.vwap} · 开盘区间 {st.last.orbLow}–{st.last.orbHigh} · 当日 {st.last.dayLow}–
                    {st.last.dayHigh}
                  </div>
                )}
              </>
            ) : (
              <div className="text-muted-2">还没有评估。开盘后启动即可。</div>
            )}
          </div>
        </div>

        {showSettings && (
          <div className="rounded-xl border border-border/60 p-3">
            <div className="grid grid-cols-2 gap-3 text-xs text-muted sm:grid-cols-4 lg:grid-cols-6">
              <Field label="单笔预算 $">{num('budgetPerTrade', 10)}</Field>
              <Field label="当日亏损上限 $（0=关）">{num('dailyLossLimit', 50)}</Field>
              <Field label="限价缓冲 %">{num('limitBufferPct', 0.5)}</Field>
              <Field label="张数上限">{num('maxContracts')}</Field>
              <Field label="止盈 QQQ %">{num('takeProfitPct', 0.01)}</Field>
              <Field label="止损 QQQ %">{num('stopLossPct', 0.01)}</Field>
              <Field label="最长持仓 分钟">{num('maxHoldMinutes')}</Field>
              <Field label="VWAP 同侧 %">{num('vwapMinPct', 0.05)}</Field>
              <Field label="区间位置 0–1">{num('rangePosMin', 0.05)}</Field>
              <Field label="开仓开始 ET">{text('entryStart')}</Field>
              <Field label="开仓结束 ET">{text('entryEnd')}</Field>
              <Field label="强平 ET">{text('flatBy')}</Field>
              <Field label="每日笔数上限">{num('maxTradesPerDay')}</Field>
              <Field label="连亏几笔停">{num('stopAfterLosses')}</Field>
              <Field label="前日 VIX 上限（0=关）">{num('maxPrevVix', 0.5)}</Field>
            </div>
            <div className="mt-3 flex flex-wrap items-center gap-4 text-xs">
              <label className="inline-flex items-center gap-1.5">
                <input type="checkbox" checked={draft.allowCalls} onChange={(e) => setDraft({ ...draft, allowCalls: e.target.checked })} />
                做 Call
              </label>
              <label className="inline-flex items-center gap-1.5">
                <input type="checkbox" checked={draft.allowPuts} onChange={(e) => setDraft({ ...draft, allowPuts: e.target.checked })} />
                做 Put
              </label>
              <label className="inline-flex items-center gap-1.5">
                <input type="checkbox" checked={draft.skipFomcDays} onChange={(e) => setDraft({ ...draft, skipFomcDays: e.target.checked })} />
                FOMC 决议日不做
              </label>
              <label className="inline-flex items-center gap-1.5">
                <input type="checkbox" checked={draft.useLimitOrders} onChange={(e) => setDraft({ ...draft, useLimitOrders: e.target.checked })} />
                用限价单
              </label>
              <div className="ml-auto flex gap-2">
                <button onClick={() => { setDraft(st.settings); setShowSettings(false) }} className="rounded-lg border border-border/70 px-3 py-1.5 text-muted hover:bg-surface-2">
                  取消
                </button>
                <button
                  onClick={save}
                  disabled={busy === 'save'}
                  className="rounded-lg bg-primary px-3 py-1.5 font-medium text-white hover:bg-primary/90 disabled:opacity-50"
                >
                  保存
                </button>
              </div>
            </div>
          </div>
        )}

        <div>
          <div className="mb-1.5 text-xs font-medium text-muted-2">Paper 成交记录</div>
          {st.trades.length === 0 ? (
            <div className="rounded-lg border border-dashed border-border/70 px-3 py-4 text-center text-xs text-muted-2">还没有成交</div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-xs tabular">
                <thead className="text-left text-muted-2">
                  <tr>
                    <th className="py-1 pr-3 font-normal">日期</th>
                    <th className="py-1 pr-3 font-normal">开 → 平 ET</th>
                    <th className="py-1 pr-3 font-normal">合约</th>
                    <th className="py-1 pr-3 text-right font-normal">张</th>
                    <th className="py-1 pr-3 text-right font-normal">开 / 平</th>
                    <th className="py-1 pr-3 text-right font-normal">QQQ 开 / 平</th>
                    <th className="py-1 pr-3 font-normal">离场</th>
                    <th className="py-1 pr-3 text-right font-normal">滑点</th>
                    <th className="py-1 text-right font-normal">盈亏</th>
                  </tr>
                </thead>
                <tbody>
                  {st.trades.slice(0, 30).map((t) => (
                    <tr key={t.id} className="border-t border-border/40" title={t.entryReason}>
                      <td className="py-1 pr-3 text-muted">{t.dateEt}</td>
                      <td className="py-1 pr-3">{etTime(t.entryAtUtc)} → {etTime(t.exitAtUtc)}</td>
                      <td className="py-1 pr-3">{t.occ}</td>
                      <td className="py-1 pr-3 text-right">{t.quantity}</td>
                      <td className="py-1 pr-3 text-right">{t.entryPrice.toFixed(2)} / {t.exitPrice.toFixed(2)}</td>
                      <td className="py-1 pr-3 text-right">{t.entryUnderlying.toFixed(2)} / {t.exitUnderlying.toFixed(2)}</td>
                      <td className="py-1 pr-3">{EXIT_LABEL[t.exitReason] ?? t.exitReason}</td>
                      <td
                        className="py-1 pr-3 text-right text-muted"
                        title={t.entryMid != null && t.exitMid != null ? `mid 开 ${t.entryMid.toFixed(2)} / 平 ${t.exitMid.toFixed(2)}` : undefined}
                      >
                        {t.slippage == null ? '—' : fmtMoney(t.slippage)}
                      </td>
                      <td className={cn('py-1 text-right font-medium', pnlCls(t.pnl))}>{fmtMoney(t.pnl)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>

        {st.log.length > 0 && (
          <details className="text-xs">
            <summary className="cursor-pointer text-muted-2">决策日志（{st.log.length}）</summary>
            <ul className="mt-1.5 space-y-1">
              {st.log.slice(0, 30).map((l, i) => (
                <li key={i} className="flex gap-2">
                  <span className="shrink-0 tabular text-muted-2">{etTime(l.atUtc)}</span>
                  <span className={cn('shrink-0 font-medium', l.decision === 'hold' ? 'text-muted' : 'text-primary')}>
                    {DECISION_LABEL[l.decision] ?? l.decision}
                  </span>
                  <span className="text-muted">{l.reason}</span>
                </li>
              ))}
            </ul>
          </details>
        )}
      </CardContent>
    </Card>
  )
}

function IconBtn({
  title,
  onClick,
  busy,
  highlight,
  children,
}: {
  title: string
  onClick: () => void
  busy?: boolean
  highlight?: boolean
  children: ReactNode
}) {
  return (
    <button
      title={title}
      onClick={onClick}
      disabled={busy}
      className={cn(
        'grid h-7 w-7 place-items-center rounded-lg border transition disabled:opacity-40',
        highlight ? 'border-primary/30 text-primary hover:bg-primary/10' : 'border-border/60 text-muted hover:bg-surface-2 hover:text-foreground'
      )}
    >
      {busy ? <Loader2 size={14} className="animate-spin" /> : children}
    </button>
  )
}

function Stat({ label, value, sub, cls }: { label: string; value: string; sub?: string; cls?: string }) {
  return (
    <div className="rounded-xl border border-border/60 px-3 py-2">
      <div className="text-[11px] text-muted-2">{label}</div>
      <div className={cn('mt-0.5 text-base font-semibold tabular', cls ?? 'text-foreground')}>{value}</div>
      {sub && <div className="text-[11px] text-muted-2">{sub}</div>}
    </div>
  )
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <label className="space-y-1">
      <span>{label}</span>
      {children}
    </label>
  )
}
