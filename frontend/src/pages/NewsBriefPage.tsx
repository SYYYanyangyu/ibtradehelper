import { useState, useEffect, useCallback } from 'react'
import { RefreshCw, ExternalLink, Clock, AlertTriangle, TrendingUp } from 'lucide-react'
import { Card, CardContent, CardHeader, CardTitle } from '../components/ui/Card'
import { Badge } from '../components/ui/Badge'
import { cn } from '../lib/utils'

interface BriefItem {
  headline: string
  summary: string
  url: string
  tickers: string[]
}

interface BriefSection {
  id: string
  title: string
  items: BriefItem[]
}

interface FinanceBrief {
  updatedAt: string
  source: 'scheduled' | 'manual'
  sections: BriefSection[]
}

export function NewsBriefPage() {
  const [brief, setBrief] = useState<FinanceBrief | null>(null)
  const [loading, setLoading] = useState(true)
  const [refreshing, setRefreshing] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [toast, setToast] = useState<{ kind: 'ok' | 'err'; text: string } | null>(null)

  const showToast = useCallback((kind: 'ok' | 'err', text: string) => {
    setToast({ kind, text })
    window.setTimeout(() => setToast(null), 3500)
  }, [])

  async function loadBrief() {
    try {
      const cacheBust = `?t=${Date.now()}`
      const res = await fetch(`/feeds/finance-brief.json${cacheBust}`)
      if (!res.ok) throw new Error('加载简报失败')
      const data = await res.json()
      setBrief(data)
      setError(null)
    } catch (e) {
      setError(e instanceof Error ? e.message : '加载失败')
      setBrief(null)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    loadBrief()
  }, [])

  async function handleRefresh() {
    setRefreshing(true)
    setToast(null)
    setError(null)
    try {
      const res = await fetch('/api/news/refresh', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' }
      })
      
      if (!res.ok) {
        const text = await res.text()
        throw new Error(text || '请求刷新失败')
      }

      showToast('ok', '已请求 Grok Bot 更新简报，请稍后刷新查看')
      
      setTimeout(() => {
        loadBrief()
      }, 3000)
    } catch (e) {
      showToast('err', e instanceof Error ? e.message : '刷新失败，请检查后端配置')
    } finally {
      setRefreshing(false)
    }
  }

  function formatTime(iso: string): string {
    try {
      const d = new Date(iso)
      return d.toLocaleString('zh-CN', {
        timeZone: 'Asia/Shanghai',
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit'
      })
    } catch {
      return iso
    }
  }

  if (loading) {
    return (
      <div className="space-y-4">
        <Card>
          <CardContent className="flex items-center justify-center py-20">
            <div className="flex items-center gap-3 text-muted">
              <RefreshCw size={20} className="animate-spin" />
              <span>加载中...</span>
            </div>
          </CardContent>
        </Card>
      </div>
    )
  }

  if (error && !brief) {
    return (
      <div className="space-y-4">
        <Card className="border-down/30 bg-down/5">
          <CardContent className="flex flex-col items-center gap-3 py-10 text-center">
            <AlertTriangle className="text-down" size={28} />
            <p className="text-sm text-down">{error}</p>
            <button
              onClick={() => {
                setLoading(true)
                setError(null)
                loadBrief()
              }}
              className="rounded-xl bg-down px-4 py-2 text-sm font-medium text-white transition hover:opacity-90"
            >
              重试
            </button>
          </CardContent>
        </Card>
      </div>
    )
  }

  return (
    <div className="space-y-4">
      {toast && (
        <div
          className={cn(
            'flex items-center gap-2 rounded-lg border px-3 py-2 text-xs',
            toast.kind === 'ok'
              ? 'border-up/25 bg-up/5 text-up'
              : 'border-down/25 bg-down/5 text-down'
          )}
        >
          <span className="min-w-0 flex-1 truncate">{toast.text}</span>
        </div>
      )}

      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <CardTitle className="flex items-center gap-2">
                <TrendingUp size={20} className="text-primary" />
                Finance Brief · 金融简报
              </CardTitle>
              {brief && (
                <div className="mt-1 flex items-center gap-2 text-xs text-muted-2">
                  <Clock size={13} />
                  <span>更新于：{formatTime(brief.updatedAt)}</span>
                  <Badge tone={brief.source === 'manual' ? 'primary' : 'neutral'}>
                    {brief.source === 'manual' ? '手动' : '定时'}
                  </Badge>
                </div>
              )}
            </div>
            <button
              onClick={handleRefresh}
              disabled={refreshing}
              className={cn(
                'flex items-center gap-2 rounded-lg bg-primary px-4 py-2 text-sm font-medium text-white transition',
                refreshing ? 'opacity-50' : 'hover:opacity-90'
              )}
            >
              <RefreshCw size={15} className={refreshing ? 'animate-spin' : ''} />
              更新
            </button>
          </div>
        </CardHeader>
      </Card>

      {brief?.sections.map((section) => (
        <Card key={section.id} className="animate-fade-up">
          <CardHeader>
            <CardTitle className="text-lg">{section.title}</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="space-y-4">
              {section.items.map((item, idx) => (
                <div
                  key={idx}
                  className="group rounded-lg border border-border/50 bg-surface-2/30 p-4 transition hover:border-border hover:bg-surface-2/60"
                >
                  <div className="flex items-start justify-between gap-3">
                    <div className="min-w-0 flex-1 space-y-2">
                      <h3 className="font-semibold text-foreground group-hover:text-primary">
                        {item.headline}
                      </h3>
                      <p className="text-sm leading-relaxed text-muted">{item.summary}</p>
                      <div className="flex flex-wrap items-center gap-2">
                        {item.tickers.map((ticker) => (
                          <Badge key={ticker} tone="neutral">
                            {ticker}
                          </Badge>
                        ))}
                      </div>
                    </div>
                    <a
                      href={item.url}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="shrink-0 rounded-md p-2 text-muted transition hover:bg-surface hover:text-primary"
                      title="查看详情"
                    >
                      <ExternalLink size={16} />
                    </a>
                  </div>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>
      ))}

      {brief && brief.sections.length === 0 && (
        <Card>
          <CardContent className="py-12 text-center text-sm text-muted">
            暂无简报内容
          </CardContent>
        </Card>
      )}
    </div>
  )
}
