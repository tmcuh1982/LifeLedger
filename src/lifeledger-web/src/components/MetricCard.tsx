import type { ReactNode } from 'react'

interface MetricCardProps {
  label: string
  value: string
  detail?: string
  tone?: 'default' | 'success' | 'warning'
  icon: ReactNode
}

export function MetricCard({ label, value, detail, tone = 'default', icon }: MetricCardProps) {
  const toneClass = tone === 'success' ? 'text-success' : tone === 'warning' ? 'text-warning' : 'text-sky'
  return (
    <article className="metric-card">
      <div className="flex items-start justify-between gap-3">
        <p className="eyebrow">{label}</p>
        <span className={`grid h-9 w-9 place-items-center rounded-xl bg-white/10 text-sm ${toneClass}`}>{icon}</span>
      </div>
      <p className="mt-5 text-2xl font-semibold tracking-tight text-white">{value}</p>
      {detail && <p className="mt-2 text-sm text-muted">{detail}</p>}
    </article>
  )
}
