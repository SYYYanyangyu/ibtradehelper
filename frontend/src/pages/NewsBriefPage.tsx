import { useState, useEffect, useCallback, useMemo, useRef } from 'react'
import { RefreshCw, ExternalLink, Clock, AlertTriangle, BookOpen, Sparkles, Bell } from 'lucide-react'
import { Card, CardContent, CardHeader, CardTitle } from '../components/ui/Card'
import { Badge } from '../components/ui/Badge'
import { getLiveFinanceBrief, isFinanceBrief, refreshFinanceBrief } from '../lib/api'
import { cn } from '../lib/utils'
import type { FinanceBrief } from '../lib/api'
import { PushNotificationSettings } from '../components/PushNotificationSettings'

export function NewsBriefPage() {
  const [brief, setBrief] = useState<FinanceBrief | null>(null)
  const [loading, setLoading] = useState(true)
  const [refreshing, setRefreshing] = useState(false)
  const [polling, setPolling] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [toast, setToast] = useState<{ kind: 'ok' | 'err'; text: string } | null>(null)
  const [activeSection, setActiveSection] = useState<string | null>(null)
  const [showPushSettings, setShowPushSettings] = useState(false)
  const pollRef = useRef<number | null>(null)

  const showToast = useCallback((kind: 'ok' | 'err', text: string) => {
    setToast({ kind, text })
    window.setTimeout(() => setToast(null), 3500)
  }, [])

  const applyBrief = useCallback((data: FinanceBrief) => {
    setBrief(data)
    setActiveSection((current) => current ?? data.sections[0]?.id ?? null)
    setError(null)
  }, [])

  const loadSnapshot = useCallback(async () => {
    const res = await fetch(`/feeds/finance-brief.json?t=${Date.now()}`)
    if (!res.ok) throw new Error('加载简报失败')
    const data = (await res.json()) as FinanceBrief
    applyBrief(data)
  }, [applyBrief])

  async function loadBrief() {
    try {
      const live = await getLiveFinanceBrief()
      if (isFinanceBrief(live)) {
        applyBrief(live)
        return
      }
      await loadSnapshot()
    } catch {
      try {
        await loadSnapshot()
      } catch (e) {
        setError(e instanceof Error ? e.message : '加载失败')
        setBrief(null)
      }
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    loadBrief()
    return () => {
      if (pollRef.current != null) window.clearInterval(pollRef.current)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  function stopPolling() {
    if (pollRef.current != null) {
      window.clearInterval(pollRef.current)
      pollRef.current = null
    }
    setPolling(false)
    setRefreshing(false)
  }

  async function handleRefresh() {
    setRefreshing(true)
    setToast(null)
    setError(null)
    stopPolling()
    setRefreshing(true)
    const prevUpdatedAt = brief?.updatedAt

    try {
      const result = await refreshFinanceBrief()
      if (isFinanceBrief(result)) {
        applyBrief(result)
        showToast('ok', '简报已更新')
        setRefreshing(false)
        return
      }

      setPolling(true)
      showToast('ok', '已通知 Grok Bot 从 X 更新，正在等待回传...')

      let attempts = 0
      const maxAttempts = 24
      pollRef.current = window.setInterval(async () => {
        attempts++
        try {
          const live = await getLiveFinanceBrief()
          if (isFinanceBrief(live) && live.updatedAt !== prevUpdatedAt) {
            applyBrief(live)
            showToast('ok', '简报已更新')
            stopPolling()
            return
          }
        } catch (e) {
          if (attempts >= maxAttempts) {
            showToast('err', e instanceof Error ? e.message : '等待回传失败')
            stopPolling()
            return
          }
        }
        if (attempts >= maxAttempts) {
          showToast('err', '等待超时，Bot 还没有回传简报')
          stopPolling()
        }
      }, 5000)
    } catch (e) {
      showToast('err', e instanceof Error ? e.message : '刷新失败，请检查后端配置')
      setRefreshing(false)
      setPolling(false)
    }
  }

  const timeAgo = useMemo(() => {
    if (!brief?.updatedAt) return ''
    const now = new Date()
    const updated = new Date(brief.updatedAt)
    const diff = now.getTime() - updated.getTime()
    const hours = Math.floor(diff / (1000 * 60 * 60))
    const minutes = Math.floor((diff % (1000 * 60 * 60)) / (1000 * 60))
    if (hours > 24) {
      return `${Math.floor(hours / 24)} 天前`
    }
    if (hours > 0) {
      return `${hours} 小时前`
    }
    if (minutes > 0) {
      return `${minutes} 分钟前`
    }
    return '刚刚'
  }, [brief?.updatedAt])

  const currentSection = useMemo(
    () => brief?.sections.find((s) => s.id === activeSection),
    [brief, activeSection]
  )

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

      <PushNotificationSettings />

      <Card className="animate-fade-up">
        <CardHeader>
          <div className="flex items-start gap-3">
            <div className="grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-primary/10 text-primary">
              <BookOpen size={20} />
            </div>
            <div className="min-w-0 flex-1">
              <CardTitle className="flex items-center gap-2">
                Finance Brief · 金融简报
              </CardTitle>
              <div className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted-2">
                {brief && (
                  <>
                    <span className="inline-flex items-center gap-1">
                      <Clock size={12} />
                      {timeAgo}更新
                    </span>
                    <Badge tone={brief.source === 'manual' ? 'primary' : 'neutral'}>
                      {brief.source === 'manual' ? '手动' : '定时'}
                    </Badge>
                  </>
                )}
              </div>
            </div>
          </div>
          <button
            onClick={handleRefresh}
            disabled={refreshing || polling}
            className="inline-flex items-center gap-1.5 rounded-lg border border-border px-3 py-1.5 text-xs text-muted transition hover:bg-surface-2 hover:text-foreground disabled:opacity-40"
          >
            <RefreshCw size={13} className={refreshing || polling ? 'animate-spin' : ''} />
            更新
          </button>
        </CardHeader>

        {polling && (
          <CardContent className="pb-3">
            <div className="flex items-center gap-2 rounded-lg border border-primary/25 bg-primary/5 px-3 py-2 text-xs text-primary">
              <RefreshCw size={14} className="animate-spin" />
              正在等待 Grok Bot 从 X 回传最新简报，预计 1-2 分钟...
            </div>
          </CardContent>
        )}

        {error && brief && (
          <CardContent className="pb-3">
            <div className="flex items-center gap-2 rounded-lg border border-down/25 bg-down/5 px-3 py-2 text-xs text-down">
              <AlertTriangle size={14} />
              {error}
            </div>
          </CardContent>
        )}
      </Card>

      {brief && brief.sections.length > 0 && (
        <>
          <div className="sticky top-12 z-10 -mx-3 overflow-x-auto border-y border-border/50 bg-surface/90 px-3 backdrop-blur-xl sm:-mx-6 sm:px-6">
            <div className="flex items-center gap-2 py-2">
              {brief.sections.map((section) => (
                <button
                  key={section.id}
                  onClick={() => setActiveSection(section.id)}
                  className={cn(
                    'shrink-0 rounded-lg border px-3 py-1.5 text-xs font-medium transition',
                    activeSection === section.id
                      ? 'border-primary/40 bg-primary/10 text-primary'
                      : 'border-border/70 text-muted hover:bg-surface-2 hover:text-foreground'
                  )}
                >
                  {section.title}
                  <span className="ml-1.5 text-[10px] opacity-70">
                    {section.items.length}
                  </span>
                </button>
              ))}
            </div>
          </div>

          {currentSection && (
            <div className="space-y-3">
              {currentSection.items.map((item, idx) => (
                <Card key={idx} className="group animate-fade-up transition-all hover:shadow-md">
                  <CardContent className="py-4">
                    <div className="flex items-start gap-3">
                      <div className="grid h-8 w-8 shrink-0 place-items-center rounded-lg bg-primary/5 text-primary/60 transition-colors group-hover:bg-primary/10 group-hover:text-primary">
                        <Sparkles size={14} />
                      </div>
                      <div className="min-w-0 flex-1 space-y-2">
                        <div className="flex items-start justify-between gap-3">
                          <h3 className="text-sm font-semibold leading-snug text-foreground group-hover:text-primary">
                            {item.headline}
                          </h3>
                          {item.url && (
                            <a
                              href={item.url}
                              target="_blank"
                              rel="noreferrer"
                              className="shrink-0 text-primary/60 transition-colors hover:text-primary"
                              aria-label="查看详情"
                            >
                              <ExternalLink size={16} />
                            </a>
                          )}
                        </div>
                        <p className="text-xs leading-relaxed text-muted">{item.summary}</p>
                        {item.tickers.length > 0 && (
                          <div className="flex flex-wrap gap-1.5">
                            {item.tickers.map((ticker) => (
                              <span
                                key={ticker}
                                className="inline-flex items-center rounded border border-border/60 bg-surface-2/50 px-1.5 py-0.5 text-[10px] font-medium tabular-nums text-muted-2 transition-colors hover:border-primary/40 hover:bg-primary/5 hover:text-primary"
                              >
                                {ticker}
                              </span>
                            ))}
                          </div>
                        )}
                      </div>
                    </div>
                  </CardContent>
                </Card>
              ))}
            </div>
          )}
        </>
      )}

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
