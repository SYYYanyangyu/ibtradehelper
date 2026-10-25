import { useState, useEffect, useCallback, useMemo } from 'react'
import { ShoppingCart, Ban, ShieldAlert, Loader2, AlertTriangle, RefreshCw, CheckCircle2 } from 'lucide-react'
import { Card, CardContent, CardHeader, CardTitle } from '../components/ui/Card'
import { Badge } from '../components/ui/Badge'
import { DisciplineCard } from '../components/DisciplineCard'
import { MarketNewsCard } from '../components/MarketNewsCard'
import { cn, fmtMoney, inputCls, thCls, trCls } from '../lib/utils'
import {
  listOrders,
  placeOrder,
  cancelOrder,
  getRiskSettings,
  updateRiskSettings,
  reconcileOrders,
  type OrderReconcileResult,
  type PlaceOrderRequest
} from '../lib/api'
import type { DisciplineCheckResult, OrderDto, RiskSettings, OrderSide, OrderType, OrderStatus } from '../types'

// 活动订单（可撤单）的状态
const WORKING: OrderStatus[] = ['PendingSubmit', 'PreSubmitted', 'Submitted', 'PartiallyFilled', 'ApiPending']

const STATUS_LABEL: Record<OrderStatus, string> = {
  PendingSubmit: '待提交',
  PreSubmitted: '预提交',
  Submitted: '已提交',
  PartiallyFilled: '部分成交',
  Filled: '已成交',
  Cancelled: '已撤销',
  Rejected: '已拒绝',
  Failed: '失败',
  ApiPending: 'API 处理中',
  Inactive: '无效'
}

function statusTone(s: OrderStatus): 'primary' | 'up' | 'down' | 'neutral' {
  if (s === 'Filled') return 'up'
  if (s === 'Cancelled' || s === 'Rejected' || s === 'Failed' || s === 'Inactive') return 'down'
  if (s === 'PartiallyFilled') return 'primary'
  return 'neutral'
}

export function OrdersPage() {
  const [orders, setOrders] = useState<OrderDto[]>([])
  const [risk, setRisk] = useState<RiskSettings | null>(null)
  const [loading, setLoading] = useState(true)
  const [submitting, setSubmitting] = useState(false)
  const [toast, setToast] = useState<{ kind: 'ok' | 'err'; text: string } | null>(null)

  // 下单表单
  const [symbol, setSymbol] = useState('')
  const [side, setSide] = useState<OrderSide>('Buy')
  const [type, setType] = useState<OrderType>('Market')
  const [quantity, setQuantity] = useState('')
  const [limitPrice, setLimitPrice] = useState('')
  const [estimatedPrice, setEstimatedPrice] = useState('')
  const [secType, setSecType] = useState<'STK' | 'OPT'>('STK')
  const [expiry, setExpiry] = useState('')
  const [strike, setStrike] = useState('')
  const [right, setRight] = useState<'C' | 'P'>('C')
  const [note, setNote] = useState('')
  const [confirmLive, setConfirmLive] = useState(false)

  // 风控面板（编辑态）
  const [riskDraft, setRiskDraft] = useState<RiskSettings | null>(null)
  const [savingRisk, setSavingRisk] = useState(false)
  const [reconcile, setReconcile] = useState<OrderReconcileResult | null>(null)
  const [reconciling, setReconciling] = useState(false)
  const [gate, setGate] = useState<DisciplineCheckResult | null>(null)

  const showToast = useCallback((kind: 'ok' | 'err', text: string) => {
    setToast({ kind, text })
    window.setTimeout(() => setToast(null), 3500)
  }, [])

  async function load() {
    setLoading(true)
    try {
      const [os, rs] = await Promise.all([listOrders(), getRiskSettings()])
      setOrders(os)
      setRisk(rs)
      setRiskDraft(rs)
      setConfirmLive(!!rs?.liveAcknowledged)
    } catch (e) {
      showToast('err', e instanceof Error ? e.message : '加载订单失败')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    load()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  async function handlePlace() {
    setToast(null)
    const sym = symbol.trim().toUpperCase()
    const qty = Number(quantity)
    if (!sym) return showToast('err', '请填写标的代码')
    if (!qty || qty <= 0) return showToast('err', '交易数量必须为正数')
    if (type === 'Limit') {
      const lp = Number(limitPrice)
      if (!lp || lp <= 0) return showToast('err', '限价单需填写有效的限价')
    }
    const est = Number(estimatedPrice)
    if (type === 'Market' && (!est || est <= 0)) {
      return showToast('err', '市价单必须填写参考价，风控按 数量×价格×乘数 校验金额')
    }
    if (secType === 'OPT') {
      if (!expiry.trim()) return showToast('err', '期权单必须填写到期日')
      if (!Number(strike) || Number(strike) <= 0) return showToast('err', '期权单必须填写行权价')
    }
    if (risk?.mode === 'Live' && !confirmLive) {
      return showToast('err', '实盘模式下请先勾选风险确认')
    }

    setSubmitting(true)
    try {
      await placeOrder({
        symbol: sym,
        side,
        type,
        quantity: qty,
        limitPrice: type === 'Limit' ? Number(limitPrice) : null,
        estimatedPrice: est > 0 ? est : type === 'Limit' ? Number(limitPrice) : null,
        secType,
        expiry: secType === 'OPT' ? expiry.trim() : null,
        strike: secType === 'OPT' ? Number(strike) : null,
        right: secType === 'OPT' ? right : null,
        multiplier: secType === 'OPT' ? 100 : 1,
        note: note.trim() || null
      })
      showToast('ok', `${sym} ${side === 'Buy' ? '买入' : '卖出'} 订单已提交`)
      setSymbol('')
      setQuantity('')
      setLimitPrice('')
      setEstimatedPrice('')
      setNote('')
      setConfirmLive(false)
      await load()
    } catch (e) {
      showToast('err', e instanceof Error ? e.message : '交易提交失败')
    } finally {
      setSubmitting(false)
    }
  }

  async function handleCancel(o: OrderDto) {
    setToast(null)
    try {
      await cancelOrder(o.id)
      showToast('ok', `${o.symbol} 订单已撤销`)
      await load()
    } catch (e) {
      showToast('err', e instanceof Error ? e.message : '撤单失败')
    }
  }

  async function handleSaveRisk() {
    if (!riskDraft) return
    setSavingRisk(true)
    try {
      const saved = await updateRiskSettings(riskDraft)
      setRisk(saved)
      setRiskDraft(saved)
      showToast('ok', '风控配置已保存')
    } catch (e) {
      showToast('err', e instanceof Error ? e.message : '保存失败')
    } finally {
      setSavingRisk(false)
    }
  }

  async function handleReconcile() {
    setReconciling(true)
    setToast(null)
    try {
      const r = await reconcileOrders()
      setReconcile(r)
      showToast(r.mismatchCount > 0 ? 'err' : 'ok', r.summary)
    } catch (e) {
      showToast('err', e instanceof Error ? e.message : '对账失败')
    } finally {
      setReconciling(false)
    }
  }

  const disciplineOrder = useMemo<PlaceOrderRequest | null>(() => {
    const sym = symbol.trim().toUpperCase()
    const qty = Number(quantity)
    if (secType !== 'OPT' || side !== 'Buy' || !sym || !(qty > 0) || !expiry.trim() || !(Number(strike) > 0)) return null
    const lp = Number(limitPrice)
    const est = Number(estimatedPrice)
    return {
      symbol: sym,
      side,
      type,
      quantity: qty,
      limitPrice: type === 'Limit' && lp > 0 ? lp : null,
      estimatedPrice: est > 0 ? est : null,
      secType,
      expiry: expiry.trim(),
      strike: Number(strike),
      right,
      multiplier: 100
    }
  }, [symbol, quantity, secType, side, expiry, strike, limitPrice, estimatedPrice, type, right])

  const isLive = risk?.mode === 'Live'
  const riskDirty = riskDraft && risk && JSON.stringify(riskDraft) !== JSON.stringify(risk)

  return (
    <>
      {toast && (
        <div
          className={cn(
            'flex items-center gap-2 rounded-xl border px-4 py-2.5 text-sm animate-fade-up',
            toast.kind === 'ok'
              ? 'border-up/30 bg-up/10 text-up'
              : 'border-down/30 bg-down/10 text-down'
          )}
        >
          {toast.kind === 'ok' ? <CheckCircle2 size={16} /> : <AlertTriangle size={16} />}
          {toast.text}
        </div>
      )}

      <section className="grid grid-cols-1 gap-6 lg:grid-cols-2">
        {/* 下单表单 */}
        <Card className="animate-fade-up">
          <CardHeader>
            <CardTitle>Trading</CardTitle>
            <span className="text-xs text-muted-2">
              Paper 只连 4002/7497 · Live 只连 4001/7496 · 期权按乘数 100 计金额
            </span>
          </CardHeader>
          <CardContent className="space-y-4">
            {isLive && (
              <div className="flex items-start gap-2 rounded-xl border border-down/30 bg-down/5 px-3 py-2.5 text-xs text-down">
                <ShieldAlert size={16} className="mt-0.5 shrink-0" />
                <span>
                  当前为 <b>实盘模式</b>。真实交易需先在下方<b>风控配置</b>中勾选「确认实盘风险」
                  （保存后生效）；未确认时后端风控会拒绝一切真实交易。请确保已充分理解风险。
                </span>
              </div>
            )}

            <div className="grid grid-cols-2 gap-3">
              <div className="col-span-2">
                <label className="mb-1 block text-xs text-muted">标的代码</label>
                <input
                  value={symbol}
                  onChange={(e) => setSymbol(e.target.value)}
                  placeholder="如 AAPL / TSLA"
                  className={cn(inputCls, 'uppercase')}
                />
              </div>

              <div className="col-span-2">
                <label className="mb-1 block text-xs text-muted">品种</label>
                <div className="grid grid-cols-2 gap-2">
                  {(['STK', 'OPT'] as const).map((t) => (
                    <button
                      key={t}
                      onClick={() => setSecType(t)}
                      className={cn(
                        'rounded-lg border px-3 py-2 text-sm font-medium transition',
                        secType === t
                          ? 'border-primary/40 bg-primary/10 text-primary'
                          : 'border-border bg-surface/60 text-muted hover:bg-surface-2'
                      )}
                    >
                      {t === 'STK' ? '正股 STK' : '期权 OPT'}
                    </button>
                  ))}
                </div>
              </div>

              <div>
                <label className="mb-1 block text-xs text-muted">方向</label>
                <div className="grid grid-cols-2 gap-2">
                  {(['Buy', 'Sell'] as OrderSide[]).map((s) => (
                    <button
                      key={s}
                      onClick={() => setSide(s)}
                      className={cn(
                        'rounded-lg border px-3 py-2 text-sm font-medium transition',
                        side === s
                          ? s === 'Buy'
                            ? 'border-up/40 bg-up/10 text-up'
                            : 'border-down/40 bg-down/10 text-down'
                          : 'border-border bg-surface/60 text-muted hover:bg-surface-2'
                      )}
                    >
                      {s === 'Buy' ? '买入' : '卖出'}
                    </button>
                  ))}
                </div>
              </div>

              <div>
                <label className="mb-1 block text-xs text-muted">类型</label>
                <div className="grid grid-cols-2 gap-2">
                  {(['Market', 'Limit'] as OrderType[]).map((t) => (
                    <button
                      key={t}
                      onClick={() => setType(t)}
                      className={cn(
                        'rounded-lg border px-3 py-2 text-sm font-medium transition',
                        type === t
                          ? 'border-primary/40 bg-primary/10 text-primary'
                          : 'border-border bg-surface/60 text-muted hover:bg-surface-2'
                      )}
                    >
                      {t === 'Market' ? '市价' : '限价'}
                    </button>
                  ))}
                </div>
              </div>

              <div>
                <label className="mb-1 block text-xs text-muted">数量</label>
                <input
                  type="number"
                  min={0}
                  step="any"
                  value={quantity}
                  onChange={(e) => setQuantity(e.target.value)}
                  placeholder="0"
                  className={inputCls}
                />
              </div>

              <div>
                <label className="mb-1 block text-xs text-muted">限价 {type === 'Limit' ? '' : '（市价忽略）'}</label>
                <input
                  type="number"
                  min={0}
                  step="any"
                  value={limitPrice}
                  disabled={type !== 'Limit'}
                  onChange={(e) => setLimitPrice(e.target.value)}
                  placeholder="0.00"
                  className={cn(inputCls, type !== 'Limit' && 'opacity-50')}
                />
              </div>

              <div>
                <label className="mb-1 block text-xs text-muted">参考价 {type === 'Market' ? '（市价必填）' : '（可选）'}</label>
                <input
                  type="number"
                  min={0}
                  step="any"
                  value={estimatedPrice}
                  onChange={(e) => setEstimatedPrice(e.target.value)}
                  placeholder={secType === 'OPT' ? '每股权利金' : '最新价'}
                  className={inputCls}
                />
              </div>

              {secType === 'OPT' && (
                <>
                  <div>
                    <label className="mb-1 block text-xs text-muted">到期 yyyy-MM-dd</label>
                    <input
                      value={expiry}
                      onChange={(e) => setExpiry(e.target.value)}
                      placeholder="2026-10-24"
                      className={inputCls}
                    />
                  </div>
                  <div>
                    <label className="mb-1 block text-xs text-muted">行权价</label>
                    <input
                      type="number"
                      min={0}
                      step="any"
                      value={strike}
                      onChange={(e) => setStrike(e.target.value)}
                      placeholder="0.00"
                      className={inputCls}
                    />
                  </div>
                  <div className="col-span-2">
                    <label className="mb-1 block text-xs text-muted">权利</label>
                    <div className="grid grid-cols-2 gap-2">
                      {(['C', 'P'] as const).map((r) => (
                        <button
                          key={r}
                          onClick={() => setRight(r)}
                          className={cn(
                            'rounded-lg border px-3 py-2 text-sm font-medium transition',
                            right === r
                              ? 'border-primary/40 bg-primary/10 text-primary'
                              : 'border-border bg-surface/60 text-muted hover:bg-surface-2'
                          )}
                        >
                          {r === 'C' ? 'Call' : 'Put'}
                        </button>
                      ))}
                    </div>
                  </div>
                </>
              )}

              <div className="col-span-2">
                <label className="mb-1 block text-xs text-muted">备注（可选）</label>
                <input
                  value={note}
                  onChange={(e) => setNote(e.target.value)}
                  placeholder="策略 / 理由"
                  className={inputCls}
                />
              </div>
            </div>

            {isLive && (
              <label className="flex items-center gap-2 text-xs text-down">
                <input
                  type="checkbox"
                  checked={confirmLive}
                  onChange={(e) => {
                    setConfirmLive(e.target.checked)
                    setRiskDraft((d) => (d ? { ...d, liveAcknowledged: e.target.checked } : d))
                  }}
                  className="h-4 w-4 accent-down"
                />
                我已知晓实盘风险，确认真实交易（并请在风控配置中保存此确认）
              </label>
            )}

            {gate?.applies && !gate.items.every((i) => i.ok || !i.blocking) && (
              <div
                className={cn(
                  'flex items-start gap-2 rounded-xl border px-3 py-2 text-xs',
                  gate.allowed ? 'border-amber-500/30 bg-amber-500/10 text-amber-500' : 'border-down/30 bg-down/5 text-down'
                )}
              >
                <ShieldAlert size={15} className="mt-0.5 shrink-0" />
                <span>
                  {gate.summary}
                  {!gate.allowed && '。提交会被后端拒绝；平仓单不受影响。'}
                </span>
              </div>
            )}

            <button
              onClick={handlePlace}
              disabled={submitting}
              className="inline-flex w-full items-center justify-center gap-1.5 rounded-xl bg-primary px-4 py-2.5 text-sm font-semibold text-white transition hover:opacity-90 disabled:opacity-50"
            >
              {submitting ? <Loader2 size={16} className="animate-spin" /> : <ShoppingCart size={16} />}
              {submitting ? '提交中…' : `提交${side === 'Buy' ? '买入' : '卖出'}订单`}
            </button>
          </CardContent>
        </Card>

        {/* 风控面板 */}
        <Card className="animate-fade-up">
          <CardHeader>
            <CardTitle>风控配置</CardTitle>
            <span className="text-xs text-muted-2">保存后写入磁盘 · Paper 只连模拟口 · 期权金额含乘数</span>
          </CardHeader>
          <CardContent className="space-y-4">
            {riskDraft ? (
              <>
                <div className="flex items-center justify-between">
                  <div>
                    <p className="text-sm font-medium text-foreground">交易总开关</p>
                    <p className="text-xs text-muted-2">关闭后拒绝一切交易</p>
                  </div>
                  <button
                    onClick={() => setRiskDraft({ ...riskDraft, enabled: !riskDraft.enabled })}
                    className={cn(
                      'relative h-6 w-11 rounded-full transition',
                      riskDraft.enabled ? 'bg-up' : 'bg-surface-2'
                    )}
                  >
                    <span
                      className={cn(
                        'absolute top-0.5 h-5 w-5 rounded-full bg-white shadow transition',
                        riskDraft.enabled ? 'left-[1.375rem]' : 'left-0.5'
                      )}
                    />
                  </button>
                </div>

                <div>
                  <label className="mb-1 block text-xs text-muted">交易模式</label>
                  <div className="grid grid-cols-2 gap-2">
                    {(['Paper', 'Live'] as const).map((m) => (
                      <button
                        key={m}
                        onClick={() => setRiskDraft({ ...riskDraft, mode: m })}
                        className={cn(
                          'rounded-lg border px-3 py-2 text-sm font-medium transition',
                          riskDraft.mode === m
                            ? m === 'Paper'
                              ? 'border-primary/40 bg-primary/10 text-primary'
                              : 'border-down/40 bg-down/10 text-down'
                            : 'border-border bg-surface/60 text-muted hover:bg-surface-2'
                        )}
                      >
                        {m === 'Paper' ? '模拟盘 Paper' : '实盘 Live'}
                      </button>
                    ))}
                  </div>
                </div>

                <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
                  <div>
                    <label className="mb-1 block text-xs text-muted">最大持仓数</label>
                    <input
                      type="number"
                      min={0}
                      value={riskDraft.maxOpenPositions}
                      onChange={(e) => setRiskDraft({ ...riskDraft, maxOpenPositions: Number(e.target.value) })}
                      className={inputCls}
                    />
                  </div>
                  <div>
                    <label className="mb-1 block text-xs text-muted">单笔上限($)</label>
                    <input
                      type="number"
                      min={0}
                      value={riskDraft.maxOrderAmount}
                      onChange={(e) => setRiskDraft({ ...riskDraft, maxOrderAmount: Number(e.target.value) })}
                      className={inputCls}
                    />
                  </div>
                  <div>
                    <label className="mb-1 block text-xs text-muted">当日熔断($)</label>
                    <input
                      type="number"
                      min={0}
                      value={riskDraft.dailyLossLimit}
                      onChange={(e) => setRiskDraft({ ...riskDraft, dailyLossLimit: Number(e.target.value) })}
                      className={inputCls}
                    />
                  </div>
                </div>

                <button
                  onClick={handleSaveRisk}
                  disabled={savingRisk || !riskDirty}
                  className="inline-flex w-full items-center justify-center gap-1.5 rounded-xl border border-border bg-surface/80 px-4 py-2.5 text-sm font-semibold text-foreground transition hover:bg-surface-2 disabled:opacity-50"
                >
                  {savingRisk ? <Loader2 size={16} className="animate-spin" /> : null}
                  {savingRisk ? '保存中…' : '保存风控配置'}
                </button>
              </>
            ) : (
              <p className="py-8 text-center text-sm text-muted">加载中…</p>
            )}
          </CardContent>
        </Card>
      </section>

      <DisciplineCard order={disciplineOrder} onResult={setGate} />

      <MarketNewsCard />

      {/* 订单列表 */}
      <Card className="animate-fade-up">
        <CardHeader>
          <CardTitle>订单列表</CardTitle>
          <div className="flex items-center gap-2">
            <span className="text-xs text-muted-2">{orders.length} 笔 · 已持久化</span>
            <button
              onClick={handleReconcile}
              disabled={reconciling}
              className="inline-flex items-center gap-1.5 rounded-lg border border-border bg-surface/80 px-2.5 py-1.5 text-xs font-medium text-foreground transition hover:bg-surface-2 disabled:opacity-50"
            >
              <ShieldAlert size={13} className={reconciling ? 'animate-pulse' : ''} />
              {reconciling ? '对账中…' : '对账'}
            </button>
            <button
              onClick={load}
              disabled={loading}
              className="inline-flex items-center gap-1.5 rounded-lg border border-border bg-surface/80 px-2.5 py-1.5 text-xs font-medium text-foreground transition hover:bg-surface-2 disabled:opacity-50"
            >
              <RefreshCw size={13} className={loading ? 'animate-spin' : ''} />
              刷新
            </button>
          </div>
        </CardHeader>
        <CardContent>
          {loading && orders.length === 0 ? (
            <div className="flex items-center justify-center py-10">
              <Loader2 className="h-6 w-6 animate-spin text-muted-2" />
            </div>
          ) : orders.length === 0 ? (
            <div className="flex flex-col items-center gap-2 py-10 text-center">
              <div className="grid h-10 w-10 place-items-center rounded-xl bg-surface-2 text-muted-2">
                <ShoppingCart size={18} />
              </div>
              <p className="text-sm text-muted">暂无订单</p>
              <p className="text-xs text-muted-2">在上方下单后将出现在此处</p>
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-y border-border bg-surface-2/50 text-left">
                    <th className={cn(thCls, 'pr-3')}>标的</th>
                    <th className={thCls}>方向</th>
                    <th className={thCls}>类型</th>
                    <th className={cn(thCls, 'text-right')}>数量</th>
                    <th className={cn(thCls, 'text-right')}>限价</th>
                    <th className={thCls}>状态</th>
                    <th className={cn(thCls, 'text-right')}>已成/均价</th>
                    <th className={thCls}>备注</th>
                    <th className={cn(thCls, 'pl-3 text-right')}>操作</th>
                  </tr>
                </thead>
                <tbody>
                  {orders.map((o) => {
                    const working = WORKING.includes(o.status)
                    return (
                      <tr key={o.id} className={trCls}>
                        <td className="py-2.5 pr-3 font-semibold">{o.symbol}</td>
                        <td className={cn('py-2.5 px-3 font-medium', o.side === 'Buy' ? 'text-up' : 'text-down')}>
                          {o.side === 'Buy' ? '买入' : '卖出'}
                        </td>
                        <td className="py-2.5 px-3 text-muted">{o.type === 'Market' ? '市价' : '限价'}</td>
                        <td className="py-2.5 px-3 text-right tabular-nums">{o.quantity}</td>
                        <td className="py-2.5 px-3 text-right tabular-nums text-muted">
                          {o.limitPrice != null ? fmtMoney(o.limitPrice) : '—'}
                        </td>
                        <td className="py-2.5 px-3">
                          <Badge tone={statusTone(o.status)} title={o.status}>
                            {STATUS_LABEL[o.status]}
                          </Badge>
                          {o.lastError && (
                            <p className="mt-1 max-w-[14rem] truncate text-[0.7rem] text-down" title={o.lastError}>
                              {o.lastError}
                            </p>
                          )}
                        </td>
                        <td className="py-2.5 px-3 text-right tabular-nums text-muted">
                          {o.filledQuantity > 0 ? (
                            <>
                              {o.filledQuantity}
                              {o.avgFillPrice > 0 && <span className="text-foreground"> @ {fmtMoney(o.avgFillPrice)}</span>}
                            </>
                          ) : (
                            '—'
                          )}
                        </td>
                        <td className="py-2.5 px-3 text-muted">{o.note || '—'}</td>
                        <td className="py-2.5 pl-3 text-right">
                          {working ? (
                            <button
                              onClick={() => handleCancel(o)}
                              className="inline-flex items-center gap-1 rounded-lg border border-down/30 bg-down/5 px-2.5 py-1.5 text-xs font-medium text-down transition hover:bg-down/10"
                            >
                              <Ban size={13} /> 撤单
                            </button>
                          ) : (
                            <span className="text-xs text-muted-2">—</span>
                          )}
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

      {reconcile && (
        <Card className="animate-fade-up">
          <CardHeader>
            <CardTitle>订单对账</CardTitle>
            <span className="text-xs text-muted-2">
              本地成交净额 vs DB 持仓 · {reconcile.mismatchCount} 处差额
            </span>
          </CardHeader>
          <CardContent className="space-y-3">
            <p className="text-xs leading-relaxed text-muted">{reconcile.summary}</p>
            {reconcile.rows.length === 0 ? (
              <div className="flex flex-col items-center gap-2 py-8 text-center">
                <div className="grid h-10 w-10 place-items-center rounded-xl bg-surface-2 text-muted-2">
                  <RefreshCw size={18} />
                </div>
                <p className="text-sm text-muted">暂无可对账标的</p>
              </div>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="border-y border-border bg-surface-2/50 text-left">
                      <th className={cn(thCls, 'pr-3')}>标的</th>
                      <th className={cn(thCls, 'text-right')}>本地成交净额</th>
                      <th className={cn(thCls, 'text-right')}>DB 持仓</th>
                      <th className={cn(thCls, 'text-right')}>差额</th>
                      <th className={cn(thCls, 'text-right')}>挂单</th>
                      <th className={cn(thCls, 'pl-3')}>状态</th>
                    </tr>
                  </thead>
                  <tbody>
                    {reconcile.rows.map((r) => (
                      <tr key={r.symbol} className={trCls}>
                        <td className="py-2.5 pr-3 font-semibold">{r.symbol}</td>
                        <td className="py-2.5 px-3 text-right tabular-nums">{r.localFillNet}</td>
                        <td className="py-2.5 px-3 text-right tabular-nums">{r.dbPositionQty}</td>
                        <td
                          className={cn(
                            'py-2.5 px-3 text-right tabular-nums',
                            r.mismatch ? 'text-down' : 'text-muted'
                          )}
                        >
                          {r.delta}
                        </td>
                        <td className="py-2.5 px-3 text-right tabular-nums text-muted">{r.openOrderCount}</td>
                        <td className="py-2.5 pl-3">
                          <Badge tone={r.mismatch ? 'down' : 'up'}>{r.mismatch ? '不一致' : '一致'}</Badge>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </CardContent>
        </Card>
      )}
    </>
  )
}
