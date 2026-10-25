import { useCallback, useEffect, useState } from 'react'
import { Loader2, Newspaper, RefreshCw } from 'lucide-react'
import { Card, CardContent, CardHeader, CardTitle } from './ui/Card'
import { cn } from '../lib/utils'
import { getMarketNews } from '../lib/api'
import type { MarketNewsFeed, MarketNewsItem, TrendScalperStatus } from '../types'

type Ctx = TrendScalperStatus['context']

/** Gateway 最新 VIX 与较前收涨跌；取不到就显示原因。 */
export function LiveVix({ ctx }: { ctx: Ctx }) {
  if (ctx.liveVix == null) {
    return <span className="text-muted-2">{ctx.liveVixError ? `未取到（${ctx.liveVixError.slice(0, 40)}）` : '—'}</span>
  }
  const chg = ctx.liveVixChangePct
  return (
    <span>
      <b className="text-foreground">{ctx.liveVix}</b>
      {chg != null && (
        <span className={cn('ml-1', chg > 0 ? 'text-down' : chg < 0 ? 'text-up' : 'text-muted-2')}>
          {chg > 0 ? '+' : ''}
          {chg}%
        </span>
      )}
      <span className="ml-1 text-muted-2">（{ctx.liveVixDate} · Gateway）</span>
    </span>
  )
}

const TAG_CLS: Record<string, string> = {
  美联储: 'border-primary/30 bg-primary/10 text-primary',
  地缘: 'border-down/30 bg-down/10 text-down',
  数据: 'border-amber-500/30 bg-amber-500/10 text-amber-500',
  财报: 'border-border/70 text-muted'
}

const PROVIDER: Record<string, string> = {
  BRFG: 'Briefing',
  BRFUPDN: 'Briefing 评级',
  DJNL: 'DJ 简报',
  BZ: 'Benzinga',
  FLY: 'Fly',
  'DJ-N': 'Dow Jones',
  'DJ-RTA': 'DJ 亚太',
  'DJ-RTE': 'DJ 欧洲',
  'DJ-RTG': 'DJ 全球',
  'DJ-RTPRO': 'DJ Pro'
}

const etTime = (iso: string | null) =>
  iso
    ? new Date(iso).toLocaleString('zh-CN', {
        timeZone: 'America/New_York',
        hour12: false,
        month: '2-digit',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit'
      })
    : '—'

interface Props {
  symbol?: string
  limit?: number
}

export function MarketNewsCard({ symbol = 'QQQ', limit = 14 }: Props) {
  const [feed, setFeed] = useState<MarketNewsFeed | null>(null)
  const [loading, setLoading] = useState(false)
  const [err, setErr] = useState<string | null>(null)
  const [tab, setTab] = useState<'tape' | 'symbol'>('tape')

  const load = useCallback(async () => {
    setLoading(true)
    try {
      setFeed(await getMarketNews(symbol))
      setErr(null)
    } catch (e) {
      setErr(e instanceof Error ? e.message : '加载失败')
    } finally {
      setLoading(false)
    }
  }, [symbol])

  useEffect(() => {
    load()
    const id = window.setInterval(load, 60000)
    return () => window.clearInterval(id)
  }, [load])

  const list: MarketNewsItem[] = feed ? (tab === 'tape' ? feed.tape : feed.items).slice(0, limit) : []
  const s = feed?.summary
  const ts = feed?.tapeStatus

  return (
    <Card className="animate-fade-up">
      <CardHeader>
        <div>
          <CardTitle className="flex items-center gap-1.5">
            <Newspaper size={16} className="text-primary" />
            新闻与情绪 · IBKR
          </CardTitle>
          <p className="mt-0.5 text-xs text-muted-2">
            只做提示，不参与开仓判断（历史新闻没法回测，未经验证的信号不进规则）· 标签按关键词匹配 · 情绪分为 IBKR
            头条自带的 K 值
          </p>
        </div>
        <button
          onClick={load}
          disabled={loading}
          className="inline-flex items-center gap-1 rounded-lg border border-border/70 px-2 py-1 text-xs text-muted transition hover:bg-surface-2 disabled:opacity-40"
        >
          {loading ? <Loader2 size={12} className="animate-spin" /> : <RefreshCw size={12} />}
          刷新
        </button>
      </CardHeader>
      <CardContent className="space-y-3">
        {err && <p className="text-xs text-down">{err}</p>}
        {feed && s && ts && (
          <>
            <div className="flex flex-wrap items-center gap-x-4 gap-y-1 rounded-lg border border-border/60 px-3 py-2 text-xs tabular">
              <span className="text-muted-2">近 {s.windowMinutes / 60} 小时</span>
              <span>
                头条 <b className="text-foreground">{s.count}</b>
              </span>
              <span>
                美联储 <b className={s.fed ? 'text-primary' : 'text-foreground'}>{s.fed}</b>
              </span>
              <span>
                地缘 <b className={s.geo ? 'text-down' : 'text-foreground'}>{s.geo}</b>
              </span>
              <span>
                经济数据 <b className={s.macro ? 'text-amber-500' : 'text-foreground'}>{s.macro}</b>
              </span>
              <span>
                平均情绪{' '}
                <b className={cn(s.avgSentiment == null ? 'text-muted-2' : s.avgSentiment > 0 ? 'text-up' : s.avgSentiment < 0 ? 'text-down' : '')}>
                  {s.avgSentiment ?? '无评分'}
                </b>
                {s.scored > 0 && <span className="text-muted-2">（{s.scored} 条有分）</span>}
              </span>
              <span className={cn('ml-auto', ts.connected ? 'text-up' : 'text-down')}>
                快讯{ts.connected ? '已连接' : `未连接${ts.error ? `：${ts.error}` : ''}`}
              </span>
            </div>

            <div className="flex items-center gap-1">
              {(['tape', 'symbol'] as const).map((t) => (
                <button
                  key={t}
                  onClick={() => setTab(t)}
                  className={cn(
                    'rounded-lg border px-2 py-0.5 text-xs transition',
                    tab === t ? 'border-primary/40 bg-primary/10 text-primary' : 'border-border/70 text-muted hover:bg-surface-2'
                  )}
                >
                  {t === 'tape' ? `全市场快讯（${feed.tape.length}）` : `${feed.symbol} 头条（${feed.items.length}）`}
                </button>
              ))}
            </div>

            {list.length === 0 ? (
              <p className="text-xs text-muted-2">
                {tab === 'tape' ? '快讯从 API 启动后开始累积；休市时很少有新推送。' : feed.error ?? '没有头条'}
              </p>
            ) : (
              <ul className="divide-y divide-border/40">
                {list.map((n) => (
                  <li key={`${n.provider}|${n.articleId}`} className="flex items-start gap-2 py-1.5 text-xs">
                    <span className="w-[86px] shrink-0 tabular text-muted-2">{etTime(n.timeUtc)}</span>
                    <span className="w-[72px] shrink-0 truncate text-muted-2">{PROVIDER[n.provider] ?? n.provider}</span>
                    <span className="flex-1 text-foreground">
                      {n.headline}
                      {n.tags.map((t) => (
                        <span key={t} className={cn('ml-1.5 rounded border px-1 py-px text-[10px]', TAG_CLS[t])}>
                          {t}
                        </span>
                      ))}
                    </span>
                    {n.sentiment != null && (
                      <span className={cn('shrink-0 tabular', n.sentiment > 0 ? 'text-up' : n.sentiment < 0 ? 'text-down' : 'text-muted')}>
                        {n.sentiment > 0 ? '+' : ''}
                        {n.sentiment.toFixed(2)}
                      </span>
                    )}
                  </li>
                ))}
              </ul>
            )}

            <p className="text-[11px] text-muted-2">
              时间为美东 · 快讯源：Briefing.com 盘中评论、Dow Jones 简报、Benzinga、Fly（后两个需账号已开通）
              {ts.providerErrors.length > 0 && ` · 未开通：${ts.providerErrors.map((e) => e.split('：')[0]).join('、')}`}
            </p>
          </>
        )}
      </CardContent>
    </Card>
  )
}
