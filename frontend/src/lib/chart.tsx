import type { ReactNode } from 'react'
import { useTheme } from '../theme'

/** 与 index.css 设计令牌对齐的语义色，避免图表里散落硬编码 hex。 */
export const CHART_COLORS = {
  primary: '#6b7280',
  up: '#0f766e',
  down: '#b91c1c',
  rose: '#d97706',
  hsi: '#c2410c',
  spx: '#1d4ed8',
  dji: '#b45309',
  ixic: '#7c3aed'
} as const

/** 饼图 / 环形图调色板（低饱和中性系）。 */
export const PIE_PALETTE = [
  '#4b5563',
  '#64748b',
  '#168c64',
  '#b45309',
  '#b93737',
  '#475569',
  '#6b7280',
  '#0f766e',
  '#78716c',
  '#334155'
] as const

export interface ChartTheme {
  isDark: boolean
  /** 坐标轴刻度文字颜色 */
  tick: string
  /** 网格线颜色 */
  grid: string
  /** Tooltip / hover 光标遮罩颜色 */
  cursor: string
}

/** 统一图表主题（颜色随明暗主题切换），消除各图表重复的同套计算。 */
export function useChartTheme(): ChartTheme {
  const { theme } = useTheme()
  const isDark = theme === 'dark'
  return {
    isDark,
    tick: isDark ? '#94a3b8' : '#64748b',
    grid: isDark ? 'rgba(148,163,184,0.12)' : 'rgba(15,23,42,0.06)',
    cursor: isDark ? 'rgba(148,163,184,0.08)' : 'rgba(15,23,42,0.04)'
  }
}

/** 图表 Tooltip 统一外壳，三个图表共用同一套玻璃质感样式。 */
export function ChartTooltipShell({ children }: { children: ReactNode }) {
  return (
    <div className="rounded-xl border border-border bg-surface/95 px-3 py-2 text-xs shadow-soft backdrop-blur">
      {children}
    </div>
  )
}
