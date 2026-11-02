import { useState, useEffect, useMemo } from 'react'
import { RefreshCw, ExternalLink, Search, ArrowUpRight, Newspaper, AlertCircle, X } from 'lucide-react'
import { refreshFinanceBrief } from '../lib/api'
import './NewsBriefPage.css'

interface BriefItem { headline: string; summary: string; url: string; tickers: string[] }
interface BriefSection { id: string; title: string; items: BriefItem[] }
interface FinanceBrief { updatedAt: string; source: 'scheduled' | 'manual'; sections: BriefSection[] }
const descriptions: Record<string, string> = {
  trump: '政策动向与全球市场', fed: '利率、通胀与货币政策', mega_cap: '科技巨头与市场焦点',
  storage: '存储周期与算力需求', robotics: '具身智能与产业进展', biotech: '医药研发与生命科学', semiconductor: '芯片产业链与制造',
}
function sourceName(url: string) {
  try { const u = new URL(url); return ['https:', 'http:'].includes(u.protocol) ? u.hostname.replace(/^www\./, '') : '' } catch { return '' }
}
function chinesePunctuation(text: string) {
  return text.replace(/,(?!\d)/g, '，').replace(/;/g, '；').replace(/\(/g, '（').replace(/\)/g, '）')
}
function formatDate(value?: string) {
  if (!value || !Number.isFinite(Date.parse(value))) return '更新时间待确认'
  return new Intl.DateTimeFormat('zh-CN', { timeZone: 'Asia/Shanghai', month: 'long', day: 'numeric', hour: '2-digit', minute: '2-digit', hour12: false }).format(new Date(value))
}

export function NewsBriefPage() {
  const [brief, setBrief] = useState<FinanceBrief | null>(null)
  const [loading, setLoading] = useState(true)
  const [refreshing, setRefreshing] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [active, setActive] = useState('all')
  const [query, setQuery] = useState('')

  async function loadBrief(signal?: AbortSignal) {
    try {
      const response = await fetch(`/feeds/finance-brief.json?t=${Date.now()}`, { signal })
      if (!response.ok) throw new Error('暂时无法读取简报，请稍后重试。')
      const data: FinanceBrief = await response.json()
      if (!Array.isArray(data.sections) || data.sections.some(s => !Array.isArray(s.items))) throw new Error('简报格式异常，请检查数据源。')
      setBrief(data)
      setActive(current => current === 'all' || data.sections.some(s => s.id === current) ? current : 'all')
      setError('')
    } catch (e) {
      if (!signal?.aborted) setError(e instanceof Error ? e.message : '简报加载失败')
    } finally { if (!signal?.aborted) setLoading(false) }
  }
  useEffect(() => { const controller = new AbortController(); void loadBrief(controller.signal); return () => controller.abort() }, [])

  async function refresh() {
    setRefreshing(true); setNotice(''); setError('')
    try {
      await refreshFinanceBrief()
      setNotice('更新请求已发送。内容生成后，点击“重新读取”查看。')
      await loadBrief()
    } catch (e) { setError(e instanceof Error ? e.message : '更新失败，请稍后重试。') }
    finally { setRefreshing(false) }
  }
  const allItems = useMemo(() => brief?.sections.flatMap(section => section.items.map(item => ({ ...item, section }))) ?? [], [brief])
  const items = useMemo(() => allItems.filter(item => (active === 'all' || item.section.id === active) &&
    `${chinesePunctuation(item.headline)} ${chinesePunctuation(item.summary)} ${item.tickers.join(' ')}`.toLocaleLowerCase().includes(query.trim().toLocaleLowerCase())), [allItems, active, query])
  const section = brief?.sections.find(s => s.id === active)

  return <section className="newsroom" aria-label="中文财经简报">
    <header className="newsroom-heading">
      <div><div className="newsroom-eyebrow"><span /> 市场观察 · 中文简报</div><h1>财经简报<span>读懂市场，关注变化。</span></h1>
        <p className="newsroom-date">{formatDate(brief?.updatedAt)} <span>北京时间</span><i />{brief?.source === 'manual' ? '手动更新' : '定时更新'}</p></div>
      <button className="newsroom-refresh" onClick={refresh} disabled={refreshing}><RefreshCw size={15} className={refreshing ? 'animate-spin' : ''} />{refreshing ? '正在请求…' : '更新简报'}</button>
    </header>
    {(error || notice) && <div className={`newsroom-notice ${error ? 'is-error' : ''}`} role={error ? 'alert' : 'status'}><AlertCircle size={16} /><span>{error || notice}</span><button onClick={() => void loadBrief()}>重新读取</button></div>}
    <div className="newsroom-layout">
      <aside className="newsroom-sidebar"><div className="newsroom-nav-title">内容导航 <span>{brief?.sections.length ?? 0} 个板块</span></div>
        <nav aria-label="简报栏目"><button aria-pressed={active === 'all'} className={active === 'all' ? 'is-active' : ''} onClick={() => setActive('all')}><span>全部简报</span><b>{allItems.length}</b></button>
          {brief?.sections.map(s => <button key={s.id} aria-pressed={active === s.id} className={active === s.id ? 'is-active' : ''} onClick={() => setActive(s.id)}><span>{s.title}</span><b>{s.items.length}</b></button>)}
        </nav><div className="newsroom-note"><Newspaper size={19} /><p>信息有出处<br /><span>点击原文，了解完整背景。</span></p></div>
      </aside>
      <main className="newsroom-main">
        <div className="newsroom-toolbar"><div><h2>{section?.title ?? '全部简报'} <span>{items.length} 条</span></h2><p>{section ? descriptions[section.id] ?? '追踪行业动态与市场变化' : '从宏观政策到产业趋势，按板块浏览市场动态。'}</p></div>
          <label className="newsroom-search"><Search size={15} /><input aria-label="搜索简报" placeholder="搜索新闻或股票代码" value={query} onChange={e => setQuery(e.target.value)} />{query && <button aria-label="清空搜索" onClick={() => setQuery('')}><X size={14} /></button>}</label>
        </div>
        {loading ? <div className="newsroom-empty" role="status"><RefreshCw className="animate-spin" size={24} /><h3>正在整理简报…</h3></div> : items.length === 0 ? <div className="newsroom-empty"><Search size={26} /><h3>{query ? '没有找到相关简报' : '暂无简报'}</h3><p>{query ? '试试其他关键词，或切换至全部板块。' : '可重新读取数据或请求更新。'}</p><button onClick={() => { setQuery(''); setActive('all'); void loadBrief() }}>查看全部简报</button></div> :
          <div className="newsroom-list">{items.map((item, index) => <article className="newsroom-article" key={`${item.section.id}-${item.url}-${index}`}>
            <div className="newsroom-number">{String(index + 1).padStart(2, '0')}</div><div className="newsroom-article-body">
              <div className="newsroom-meta"><span>{item.section.title}</span><i />{sourceName(item.url) || '来源待确认'}</div>
              <h3>{sourceName(item.url) ? <a href={item.url} target="_blank" rel="noopener noreferrer">{chinesePunctuation(item.headline)}<ArrowUpRight size={17} /></a> : item.headline}</h3>
              <p className="newsroom-summary">{chinesePunctuation(item.summary)}</p>
              <footer><div className="newsroom-tickers">{item.tickers.map(ticker => <span key={ticker}>{ticker}</span>)}</div>{sourceName(item.url) && <a className="newsroom-source" href={item.url} target="_blank" rel="noopener noreferrer">阅读原文<ExternalLink size={12} /></a>}</footer>
            </div>
          </article>)}</div>}
        <div className="newsroom-bottom">以上为来源内容摘要，未经独立核实。请以原始报道为准。</div>
      </main>
    </div>
  </section>
}
