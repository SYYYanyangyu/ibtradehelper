import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import {
  Activity,
  AlertTriangle,
  Bot,
  Calendar,
  Landmark,
  Loader2,
  Newspaper,
  Play,
  RefreshCw,
  Sparkles,
  Square,
  Target,
  Zap
} from 'lucide-react'
import { Card, CardContent, CardHeader, CardTitle } from '../components/ui/Card'
import { Badge } from '../components/ui/Badge'
import { cn, fmtMoney, fmtNum, fmtPct, gainClass, inputCls, thCls, trCls } from '../lib/utils'
import {
  analyzeOptions,
  getHotOptions,
  getOptionsContext,
  getOptionsPaper,
  startOptionsPaper,
  stopOptionsPaper,
  tickOptionsPaper,
  updateOptionsPaperSettings,
  type HotName,
  type HotUniverse,
  type OptionsAnalyzeResult,
  type OptionsContext,
  type OptionsPaperStatus,
  type PredictionMarket
} from '../lib/optionsIntel'

export function OptionsIntelPage() {
  const [symbol, setSymbol] = useState('')
  const [draft, setDraft] = useState('')
  const [expiry, setExpiry] = useState<string>('')
  const [ctx, setCtx] = useState<OptionsContext | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [prompt, setPrompt] = useState('结合财报、美联储和下周期权链，偏看波动还是方向？')
  const [ai, setAi] = useState<OptionsAnalyzeResult | null>(null)
  const [aiLoading, setAiLoading] = useState(false)
  const [aiError, setAiError] = useState<string | null>(null)
  const [paper, setPaper] = useState<OptionsPaperStatus | null>(null)
  const [paperBusy, setPaperBusy] = useState(false)
  const [paperError, setPaperError] = useState<string | null>(null)
  const [qtyDraft, setQtyDraft] = useState('1')
  const [moveDraft, setMoveDraft] = useState('3')
  const [hot, setHot] = useState<HotUniverse | null>(null)
  const [hotLoading, setHotLoading] = useState(true)
  const [hotError, setHotError] = useState<string | null>(null)

  const pickSymbol = useCallback((next: string) => {
    const sym = next.trim().toUpperCase()
    if (!sym) return
    setDraft(sym)
    setExpiry('')
    setAi(null)
    setSymbol(sym)
  }, [])

  const loadGen = useRef(0)
  const load = useCallback(async (sym: string, exp?: string) => {
    const gen = ++loadGen.current
    setLoading(true)
    setError(null)
    try {
      const data = await getOptionsContext(sym, exp || undefined)
      if (gen !== loadGen.current) return
      setCtx(data)
      if (data.selectedExpiry && !exp) setExpiry(data.selectedExpiry)
    } catch (e) {
      if (gen !== loadGen.current) return
      setError(e instanceof Error ? e.message : '加载失败')
      setCtx(null)
    } finally {
      if (gen === loadGen.current) setLoading(false)
    }
  }, [])

  const loadHot = useCallback(async () => {
    setHotLoading(true)
    setHotError(null)
    try {
      const data = await getHotOptions()
      setHot(data)
      if (!data.names.length) setHotError(data.source?.message || '热门榜为空')
    } catch (e) {
      setHot(null)
      setHotError(e instanceof Error ? e.message : '热门榜失败')
    } finally {
      setHotLoading(false)
    }
  }, [])

  useEffect(() => {
    void loadHot()
  }, [loadHot])

  useEffect(() => {
    if (symbol || !hot?.names[0]) return
    const first = hot.names[0].symbol
    setDraft(first)
    setSymbol(first)
  }, [hot, symbol])

  useEffect(() => {
    if (!symbol) return
    void load(symbol, expiry || undefined)
  }, [symbol, expiry, load])

  const refreshPaper = useCallback(async (syncDrafts = false) => {
    try {
      const st = await getOptionsPaper()
      setPaper(st)
      if (syncDrafts) {
        setQtyDraft(String(st.settings.quantity))
        setMoveDraft(String(Math.round(st.settings.minProbMove * 100)))
      }
      setPaperError(st.lastError ?? null)
    } catch (e) {
      setPaperError(e instanceof Error ? e.message : 'Paper 状态失败')
    }
  }, [])

  useEffect(() => {
    void refreshPaper(true)
  }, [refreshPaper])

  useEffect(() => {
    if (!paper?.running) return
    const id = window.setInterval(() => void refreshPaper(false), 8000)
    return () => window.clearInterval(id)
  }, [paper?.running, refreshPaper])

  async function paperStart() {
    setPaperBusy(true)
    setPaperError(null)
    try {
      await updateOptionsPaperSettings({
        symbol,
        quantity: Number(qtyDraft) || 1,
        pollSeconds: 90,
        minProbMove: (Number(moveDraft) || 3) / 100,
        sendToGateway: true
      })
      setPaper(await startOptionsPaper())
      window.setTimeout(() => void refreshPaper(false), 2500)
    } catch (e) {
      setPaperError(e instanceof Error ? e.message : '启动失败')
    } finally {
      setPaperBusy(false)
    }
  }

  async function paperStop() {
    setPaperBusy(true)
    try {
      setPaper(await stopOptionsPaper())
    } catch (e) {
      setPaperError(e instanceof Error ? e.message : '停止失败')
    } finally {
      setPaperBusy(false)
    }
  }

  async function paperTick() {
    setPaperBusy(true)
    setPaperError(null)
    try {
      await updateOptionsPaperSettings({
        symbol,
        quantity: Number(qtyDraft) || 1,
        pollSeconds: 90,
        minProbMove: (Number(moveDraft) || 3) / 100,
        sendToGateway: true
      })
      setPaper(await tickOptionsPaper())
    } catch (e) {
      setPaperError(e instanceof Error ? e.message : '评估失败')
    } finally {
      setPaperBusy(false)
    }
  }

  function submitSymbol() {
    pickSymbol(draft)
  }

  const currentIsHot = !!symbol && (hot?.names.some((n) => n.symbol === symbol) ?? false)
  const dollarNames = (hot?.names ?? []).filter((n) => (n.bucket ?? 'dollar') === 'dollar')
  const n100Names = (hot?.names ?? []).filter((n) => n.bucket === 'n100')

  async function runAi() {
    setAiLoading(true)
    setAiError(null)
    try {
      setAi(await analyzeOptions({ symbol, expiry: expiry || undefined, prompt }))
    } catch (e) {
      setAiError(e instanceof Error ? e.message : '分析失败')
    } finally {
      setAiLoading(false)
    }
  }

  const u = ctx?.underlying
  const stats = ctx?.stats
  const atm = stats?.atmStrike

  const biasTone = ai?.bias === 'bullish' ? 'up' : ai?.bias === 'bearish' ? 'down' : 'primary'

  const expiries = useMemo(() => {
    const list = ctx?.expiries ?? []
    if (list.length <= 10) return list
    const selected = expiry || ctx?.selectedExpiry
    const idx = Math.max(0, list.findIndex((e) => e === selected))
    const start = Math.max(0, idx - 4)
    return list.slice(start, start + 10)
  }, [ctx, expiry])

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader>
          <div>
            <CardTitle>期权情报</CardTitle>
            <p className="mt-0.5 text-xs text-muted-2">
              先点今日热门，不必手输代码。Nasdaq 官方成交额 + 纳指100异动，点选即加载期权链。
            </p>
          </div>
          <button
            type="button"
            onClick={() => {
              void loadHot()
              if (symbol) void load(symbol, expiry || undefined)
            }}
            className="inline-flex items-center gap-1.5 rounded-lg border border-border px-3 py-1.5 text-xs text-muted transition hover:bg-surface-2 hover:text-foreground"
          >
            <RefreshCw size={13} className={loading || hotLoading ? 'animate-spin' : undefined} />
            刷新
          </button>
        </CardHeader>
        <CardContent className="space-y-4">
          <HotBoard
            title="成交额最活跃"
            hint={hot?.asOf || 'Nasdaq Most Active by Dollar Volume'}
            names={dollarNames}
            loading={hotLoading}
            error={hotError}
            selected={symbol}
            onPick={pickSymbol}
            onRetry={() => void loadHot()}
          />
          {n100Names.length > 0 && (
            <HotBoard
              title="纳指100异动"
              hint="Nasdaq-100 Movers"
              names={n100Names}
              selected={symbol}
              onPick={pickSymbol}
            />
          )}
          {symbol && hot && hot.names.length > 0 && !currentIsHot && (
            <p className="text-xs text-down">
              {symbol} 不在今日官方热门榜，Paper 不会下单。请点上面的卡片。
            </p>
          )}
          <div className="flex flex-wrap items-end gap-2">
            <label className="min-w-[140px] flex-1">
              <span className="mb-1 block text-[11px] uppercase tracking-wider text-muted-2">
                自己搜（可选）
              </span>
              <input
                value={draft}
                onChange={(e) => setDraft(e.target.value.toUpperCase())}
                onKeyDown={(e) => e.key === 'Enter' && submitSymbol()}
                className={inputCls}
                placeholder="例如 MU"
              />
            </label>
            <button
              type="button"
              onClick={submitSymbol}
              className="rounded-lg border border-border px-4 py-2 text-sm text-muted hover:text-foreground"
            >
              加载期权链
            </button>
          </div>
          {u && (
            <div className="flex flex-wrap items-baseline gap-x-4 gap-y-1 text-sm">
              <span className="font-semibold">{u.companyName || ctx?.symbol}</span>
              <span className="tabular-nums">{u.last != null ? fmtMoney(u.last) : '—'}</span>
              <span className={cn('tabular-nums', gainClass(u.change ?? 0))}>
                {u.change != null ? `${u.change > 0 ? '+' : ''}${fmtNum(u.change)}` : ''}
                {u.changePct != null ? ` (${u.changePct > 0 ? '+' : ''}${u.changePct}%)` : ''}
              </span>
              <span className="text-xs text-muted-2">
                {u.exchange} · {u.marketStatus} · {u.lastTrade}
              </span>
            </div>
          )}
          {ctx?.expiries?.length ? (
            <div className="flex flex-wrap gap-1.5">
              {expiries.map((e) => (
                <button
                  key={e}
                  type="button"
                  onClick={() => setExpiry(e)}
                  className={cn(
                    'rounded-full border px-2.5 py-1 text-[11px] tabular-nums',
                    (expiry || ctx.selectedExpiry) === e
                      ? 'border-foreground bg-foreground text-bg'
                      : 'border-border text-muted hover:text-foreground'
                  )}
                >
                  {e}
                </button>
              ))}
              {ctx.expiries.length > expiries.length && (
                <span className="self-center text-[11px] text-muted-2">+{ctx.expiries.length - expiries.length}</span>
              )}
            </div>
          ) : null}
        </CardContent>
      </Card>

      {error && (
        <Card className="border-down/30 bg-down/5">
          <CardContent className="flex items-center gap-2 py-4 text-sm text-down">
            <AlertTriangle size={16} />
            {error}
          </CardContent>
        </Card>
      )}

      <Card>
        <CardHeader>
          <div>
            <CardTitle>Paper 自动模拟</CardTitle>
            <p className="mt-0.5 text-xs text-muted-2">
              只连 Gateway Paper 4002/7497 · ATM Call 真实 OPT · 不成交不记账
            </p>
          </div>
          <Badge tone={paper?.running ? 'up' : 'neutral'}>{paper?.running ? '运行中' : '已停止'}</Badge>
        </CardHeader>
        <CardContent className="space-y-3">
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            <MiniStat label="持仓" value={paper ? `${paper.netQty}` : '—'} />
            <MiniStat label="成本" value={paper?.avgPrice ? fmtMoney(paper.avgPrice) : '—'} />
            <MiniStat label="盯市" value={paper?.mark != null ? fmtMoney(paper.mark) : '—'} />
            <MiniStat
              label="总盈亏"
              value={paper ? fmtMoney(paper.totalPnl) : '—'}
              tone={paper ? paper.totalPnl : 0}
            />
          </div>
          <div className="flex flex-wrap items-end gap-2">
            <label className="w-24">
              <span className="mb-1 block text-[11px] uppercase tracking-wider text-muted-2">张数</span>
              <input value={qtyDraft} onChange={(e) => setQtyDraft(e.target.value)} className={inputCls} />
            </label>
            <label className="w-28">
              <span className="mb-1 block text-[11px] uppercase tracking-wider text-muted-2">阈值 %</span>
              <input value={moveDraft} onChange={(e) => setMoveDraft(e.target.value)} className={inputCls} />
            </label>
            <button
              type="button"
              disabled={paperBusy || (hot != null && hot.names.length > 0 && !currentIsHot)}
              onClick={() => void paperStart()}
              className="inline-flex items-center gap-1.5 rounded-lg bg-foreground px-3 py-2 text-xs font-medium text-bg disabled:opacity-40"
            >
              {paperBusy ? <Loader2 size={13} className="animate-spin" /> : <Play size={13} />}
              启动
            </button>
            <button
              type="button"
              disabled={paperBusy}
              onClick={() => void paperStop()}
              className="inline-flex items-center gap-1.5 rounded-lg border border-border px-3 py-2 text-xs disabled:opacity-40"
            >
              <Square size={13} />
              停止
            </button>
            <button
              type="button"
              disabled={paperBusy}
              onClick={() => void paperTick()}
              className="inline-flex items-center gap-1.5 rounded-lg border border-border px-3 py-2 text-xs disabled:opacity-40"
            >
              <Zap size={13} />
              立刻评估
            </button>
          </div>
          {paperError && <p className="text-xs text-down">{paperError}</p>}
          {paper?.lastSignal && (
            <p className="text-xs text-muted">
              信号 <Badge tone={paper.lastSignal.bias === 'buy' ? 'up' : paper.lastSignal.bias === 'sell' ? 'down' : 'neutral'}>{paper.lastSignal.bias}</Badge>
              {' '}
              {paper.lastSignal.reason}
            </p>
          )}
          {paper?.fills.length ? (
            <table className="w-full text-sm">
              <thead>
                <tr>
                  <th className={thCls}>时间</th>
                  <th className={thCls}>方向</th>
                  <th className={cn(thCls, 'text-right')}>数量</th>
                  <th className={cn(thCls, 'text-right')}>价格</th>
                  <th className={thCls}>通道</th>
                </tr>
              </thead>
              <tbody>
                {paper.fills.slice(0, 8).map((f) => (
                  <tr key={f.id} className={trCls}>
                    <td className="py-1.5 text-xs text-muted">{new Date(f.atUtc).toLocaleString()}</td>
                    <td className={cn('py-1.5', f.side === 'Buy' ? 'text-up' : 'text-down')}>{f.side}</td>
                    <td className="py-1.5 text-right tabular-nums">{f.quantity}</td>
                    <td className="py-1.5 text-right tabular-nums">{fmtMoney(f.price)}</td>
                    <td className="py-1.5 text-xs text-muted-2">
                      {f.venue}
                      {f.error ? ` · ${f.error}` : ''}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          ) : (
            <p className="text-xs text-muted-2">还没有成交。第一次采样只建基线；概率变动达到阈值才会下单。</p>
          )}
        </CardContent>
      </Card>

      {ctx && (
        <section className="grid grid-cols-2 gap-3 sm:grid-cols-4">
          <Stat label="ATM" value={atm != null ? fmtNum(atm) : '—'} icon={<Target size={16} />} />
          <Stat
            label="P/C 持仓"
            value={stats?.putCallOiRatio != null ? stats.putCallOiRatio.toFixed(2) : '—'}
            icon={<Activity size={16} />}
          />
          <Stat
            label="Call OI"
            value={stats ? stats.callOpenInterest.toLocaleString() : '—'}
            icon={<Sparkles size={16} />}
          />
          <Stat
            label="Put OI"
            value={stats ? stats.putOpenInterest.toLocaleString() : '—'}
            icon={<Activity size={16} />}
          />
        </section>
      )}

      <Card>
        <CardHeader>
          <CardTitle>期权链</CardTitle>
          <span className="text-xs text-muted-2">
            Nasdaq 延迟 · 展示 ATM±14 · 共 {stats?.totalRows ?? 0} 行
          </span>
        </CardHeader>
        <CardContent className="overflow-x-auto">
          {loading ? (
            <p className="py-8 text-center text-sm text-muted">加载 {symbol || '期权链'}…</p>
          ) : ctx?.chain.length ? (
            <table className="w-full min-w-[720px] text-sm">
              <thead>
                <tr className="border-y border-border bg-surface-2/50">
                  <th className={cn(thCls, 'text-right')} colSpan={4}>
                    Calls
                  </th>
                  <th className={cn(thCls, 'text-center')}>Strike</th>
                  <th className={cn(thCls, 'text-left')} colSpan={4}>
                    Puts
                  </th>
                </tr>
                <tr className="text-[11px] text-muted-2">
                  <th className="px-2 py-1 text-right">Bid</th>
                  <th className="px-2 py-1 text-right">Ask</th>
                  <th className="px-2 py-1 text-right">Vol</th>
                  <th className="px-2 py-1 text-right">OI</th>
                  <th className="px-2 py-1 text-center">K</th>
                  <th className="px-2 py-1 text-left">Bid</th>
                  <th className="px-2 py-1 text-left">Ask</th>
                  <th className="px-2 py-1 text-left">Vol</th>
                  <th className="px-2 py-1 text-left">OI</th>
                </tr>
              </thead>
              <tbody>
                {ctx.chain.map((row) => {
                  const isAtm = atm != null && row.strike === atm
                  return (
                    <tr key={`${row.expiry}-${row.strike}`} className={cn(trCls, isAtm && 'bg-primary/5')}>
                      <Td num right v={row.call.bid} />
                      <Td num right v={row.call.ask} />
                      <Td num right v={row.call.volume} />
                      <Td num right v={row.call.openInterest} />
                      <td className={cn('px-2 py-1.5 text-center font-semibold tabular-nums', isAtm && 'text-primary')}>
                        {fmtNum(row.strike)}
                      </td>
                      <Td num v={row.put.bid} />
                      <Td num v={row.put.ask} />
                      <Td num v={row.put.volume} />
                      <Td num v={row.put.openInterest} />
                    </tr>
                  )
                })}
              </tbody>
            </table>
          ) : (
            <p className="py-8 text-center text-sm text-muted">该到期日暂无期权行</p>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="inline-flex items-center gap-2">
            <Bot size={16} />
            AI 解读
          </CardTitle>
          <span className="text-xs text-muted-2">只分析上方已拉取的公开数据</span>
        </CardHeader>
        <CardContent className="space-y-3">
          <textarea
            value={prompt}
            onChange={(e) => setPrompt(e.target.value)}
            rows={2}
            className={cn(inputCls, 'resize-none')}
            placeholder="例如：下周 FOMC 前，AAPL 该看 IV 扩张还是方向？"
          />
          <button
            type="button"
            disabled={aiLoading || !symbol}
            onClick={() => void runAi()}
            className="inline-flex items-center gap-2 rounded-lg bg-foreground px-4 py-2 text-sm font-medium text-bg disabled:opacity-40"
          >
            {aiLoading ? <Loader2 size={15} className="animate-spin" /> : <Sparkles size={15} />}
            {aiLoading ? '分析中…' : '用当前数据做 AI 分析'}
          </button>
          {aiError && <p className="text-xs text-down">{aiError}</p>}
          {ai && (
            <div className="space-y-2 rounded-xl border border-border bg-surface-2/40 p-4">
              <div className="flex flex-wrap items-center gap-2">
                <Badge tone={biasTone}>{ai.bias}</Badge>
                <span className="text-xs text-muted-2">
                  {ai.source} · 置信 {(ai.confidence * 100).toFixed(0)}%
                </span>
              </div>
              <p className="text-sm leading-relaxed">{ai.summary}</p>
              {ai.suggestedFocus && (
                <p className="text-xs text-muted">关注：{ai.suggestedFocus}</p>
              )}
              {ai.drivers.length > 0 && (
                <ul className="list-disc space-y-1 pl-4 text-xs text-muted">
                  {ai.drivers.map((d) => (
                    <li key={d}>{d}</li>
                  ))}
                </ul>
              )}
              {ai.risks.length > 0 && (
                <ul className="list-disc space-y-1 pl-4 text-xs text-down/80">
                  {ai.risks.map((d) => (
                    <li key={d}>{d}</li>
                  ))}
                </ul>
              )}
            </div>
          )}
        </CardContent>
      </Card>

      <section className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <Panel title="财报 Surprise" icon={<Calendar size={15} />} hint="Nasdaq Earnings">
          {ctx?.earnings.length ? (
            <table className="w-full text-sm">
              <thead>
                <tr>
                  <th className={thCls}>季度</th>
                  <th className={thCls}>公布</th>
                  <th className={cn(thCls, 'text-right')}>EPS</th>
                  <th className={cn(thCls, 'text-right')}>预期</th>
                  <th className={cn(thCls, 'text-right')}>惊喜</th>
                </tr>
              </thead>
              <tbody>
                {ctx.earnings.map((e) => (
                  <tr key={e.fiscalQuarter + e.dateReported} className={trCls}>
                    <td className="py-2">{e.fiscalQuarter}</td>
                    <td className="py-2 text-muted">{e.dateReported}</td>
                    <td className="py-2 text-right tabular-nums">{e.eps ?? '—'}</td>
                    <td className="py-2 text-right tabular-nums">{e.consensus ?? '—'}</td>
                    <td className={cn('py-2 text-right tabular-nums', gainClass(e.surprisePct ?? 0))}>
                      {e.surprisePct != null ? `${e.surprisePct}%` : '—'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          ) : (
            <Empty text="暂无财报 surprise" />
          )}
        </Panel>

        <Panel title="新闻标题" icon={<Newspaper size={15} />} hint="Yahoo / Google RSS">
          {ctx?.news.length ? (
            <ul className="space-y-2">
              {ctx.news.slice(0, 10).map((n) => (
                <li key={n.title} className="text-sm">
                  {n.url ? (
                    <a href={n.url} target="_blank" rel="noreferrer" className="hover:underline">
                      {n.title}
                    </a>
                  ) : (
                    n.title
                  )}
                  <p className="text-[11px] text-muted-2">
                    {n.source}
                    {n.published ? ` · ${n.published}` : ''}
                  </p>
                </li>
              ))}
            </ul>
          ) : (
            <Empty text="暂无新闻标题" />
          )}
        </Panel>

        <Panel title="美联储" icon={<Landmark size={15} />} hint="FOMC 日历 + 货币政策 RSS">
          {ctx?.fed.length ? (
            <ul className="space-y-2">
              {ctx.fed.slice(0, 10).map((f) => (
                <li key={f.kind + f.title} className="text-sm">
                  <div className="flex items-center gap-2">
                    <Badge tone={f.upcoming ? 'primary' : 'neutral'}>{f.kind}</Badge>
                    <span className="text-xs text-muted-2">{f.date}</span>
                  </div>
                  {f.url ? (
                    <a href={f.url} target="_blank" rel="noreferrer" className="hover:underline">
                      {f.title}
                    </a>
                  ) : (
                    f.title
                  )}
                </li>
              ))}
            </ul>
          ) : (
            <Empty text="暂无美联储事件" />
          )}
        </Panel>

        <Panel title="宏观补充" icon={<Landmark size={15} />} hint="财政部利率 + 预测市场涨跌">
          {ctx?.treasury.length ? (
            <ul className="mb-3 space-y-1 text-sm">
              {ctx.treasury.map((t) => (
                <li key={t.security} className="flex justify-between">
                  <span className="text-muted">{t.security}</span>
                  <span className="tabular-nums">{t.ratePct ?? '—'}%</span>
                </li>
              ))}
              <li className="text-[11px] text-muted-2">as of {ctx.treasury[0]?.asOf}</li>
            </ul>
          ) : (
            <Empty text="财政部利率未取到" />
          )}
          <p className="mb-2 text-[11px] font-medium uppercase tracking-wider text-muted-2">
            预测市场涨跌
          </p>
          <PredictionList markets={paper?.markets?.length ? paper.markets : ctx?.predictionMarkets ?? []} />
        </Panel>
      </section>

      {ctx?.sources.length ? (
        <div className="flex flex-wrap gap-1.5">
          {ctx.sources.map((s) => (
            <Badge key={s.id} tone={s.status === 'ok' ? 'up' : 'down'} title={s.message ?? ''}>
              {s.label}
              {s.message ? ` · ${s.message}` : ''}
            </Badge>
          ))}
        </div>
      ) : null}
    </div>
  )
}

function HotBoard({
  title,
  hint,
  names,
  loading,
  error,
  selected,
  onPick,
  onRetry
}: {
  title: string
  hint: string
  names: HotName[]
  loading?: boolean
  error?: string | null
  selected: string
  onPick: (symbol: string) => void
  onRetry?: () => void
}) {
  return (
    <div>
      <div className="mb-2 flex items-center justify-between gap-3">
        <div>
          <p className="text-[11px] font-medium uppercase tracking-wider text-muted-2">{title}</p>
          <p className="text-[11px] text-muted-2/80">{hint}</p>
        </div>
        {error && onRetry && (
          <button type="button" onClick={onRetry} className="text-[11px] text-down hover:underline">
            重试
          </button>
        )}
      </div>
      {loading && !names.length ? (
        <div className="grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-5">
          {Array.from({ length: 10 }, (_, i) => (
            <div key={i} className="h-[72px] animate-pulse rounded-xl bg-surface-2" />
          ))}
        </div>
      ) : names.length ? (
        <div className="grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-5">
          {names.map((n) => {
            const active = n.symbol === selected
            return (
              <button
                key={`${n.bucket ?? 'hot'}-${n.symbol}`}
                type="button"
                onClick={() => onPick(n.symbol)}
                className={cn(
                  'flex flex-col items-start rounded-xl border px-3 py-2.5 text-left transition',
                  active
                    ? 'border-foreground bg-foreground text-bg'
                    : 'border-border hover:bg-surface-2'
                )}
              >
                <div className="flex w-full items-baseline justify-between gap-2">
                  <span className="text-sm font-semibold tracking-tight">{n.symbol}</span>
                  <span
                    className={cn(
                      'text-[11px] tabular-nums',
                      active ? 'text-bg/75' : gainClass(n.changePct ?? 0)
                    )}
                  >
                    {n.changePct != null ? fmtPct(n.changePct, false) : '—'}
                  </span>
                </div>
                <span className={cn('mt-0.5 line-clamp-1 text-[11px]', active ? 'text-bg/65' : 'text-muted-2')}>
                  {n.name || (n.kind === 'etf' ? 'ETF' : '股票')}
                </span>
                <span className={cn('mt-1 text-xs tabular-nums', active ? 'text-bg/80' : 'text-muted')}>
                  {n.last != null ? fmtMoney(n.last) : '—'}
                  <span className={cn('ml-1.5', active ? 'text-bg/50' : 'text-muted-2')}>
                    #{n.rank}
                    {n.kind === 'etf' ? ' ETF' : ''}
                  </span>
                </span>
              </button>
            )
          })}
        </div>
      ) : (
        <p className="rounded-xl border border-dashed border-border px-3 py-6 text-center text-sm text-muted">
          {error || '热门榜暂时没有数据'}
        </p>
      )}
    </div>
  )
}

function MiniStat({ label, value, tone }: { label: string; value: string; tone?: number }) {
  return (
    <div className="rounded-xl border border-border bg-surface-2/40 px-3 py-2">
      <p className="text-[11px] uppercase tracking-wider text-muted-2">{label}</p>
      <p className={cn('text-sm font-semibold tabular-nums', tone != null && gainClass(tone))}>{value}</p>
    </div>
  )
}

function fmtProb(v?: number | null) {
  if (v == null) return '—'
  const pct = Number(v) <= 1 ? Number(v) * 100 : Number(v)
  return `${pct.toFixed(1)}%`
}

function PredictionList({ markets }: { markets: PredictionMarket[] }) {
  if (!markets.length) {
    return <Empty text="预测市场未取到（Polymarket 常被 451，会自动改走 Manifold）" />
  }
  return (
    <ul className="space-y-2">
      {markets.map((m) => (
        <li key={m.venue + m.question} className="text-sm">
          {m.url ? (
            <a href={m.url} target="_blank" rel="noreferrer" className="hover:underline">
              {m.question}
            </a>
          ) : (
            m.question
          )}
          <p className="text-[11px] text-muted-2">
            {m.venue}
            {` · ${fmtProb(m.probability)}`}
            {m.change != null ? (
              <span className={cn('ml-1', gainClass(m.change))}>{fmtPct(m.change)}</span>
            ) : (
              <span className="ml-1">· 基线</span>
            )}
          </p>
        </li>
      ))}
    </ul>
  )
}

function Stat({ label, value, icon }: { label: string; value: string; icon: ReactNode }) {
  return (
    <Card>
      <CardContent className="flex items-center gap-3 py-4">
        <div className="grid h-9 w-9 place-items-center rounded-xl bg-surface-2 text-muted">{icon}</div>
        <div>
          <p className="text-[11px] uppercase tracking-wider text-muted-2">{label}</p>
          <p className="text-lg font-semibold tabular-nums">{value}</p>
        </div>
      </CardContent>
    </Card>
  )
}

function Panel({
  title,
  hint,
  icon,
  children
}: {
  title: string
  hint: string
  icon: ReactNode
  children: ReactNode
}) {
  return (
    <Card>
      <CardHeader>
        <CardTitle className="inline-flex items-center gap-2">
          {icon}
          {title}
        </CardTitle>
        <span className="text-xs text-muted-2">{hint}</span>
      </CardHeader>
      <CardContent>{children}</CardContent>
    </Card>
  )
}

function Empty({ text }: { text: string }) {
  return <p className="py-6 text-center text-sm text-muted">{text}</p>
}

function Td({ v, right, num }: { v: number | null | undefined; right?: boolean; num?: boolean }) {
  return (
    <td className={cn('px-2 py-1.5 tabular-nums', right && 'text-right', num && 'text-muted')}>
      {v == null ? '—' : v.toLocaleString()}
    </td>
  )
}
