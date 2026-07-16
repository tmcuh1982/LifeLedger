import { Area, AreaChart, Bar, BarChart, Cell, Pie, PieChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import type { AllocationSlice, NetWorthSnapshot, ProjectionYear } from '../types'

const palette = ['#adc9eb', '#b9f6c8', '#ffddb0', '#d5b7ff', '#ffb4ab', '#ffffff']

function compact(value: number, currency: string, locale = 'en') {
  return new Intl.NumberFormat(locale, { style: 'currency', currency, notation: 'compact', maximumFractionDigits: 1 }).format(value)
}

function ChartTooltip({ active, payload, currency, locale = 'en' }: { active?: boolean; payload?: Array<{ value: number; name: string; color: string }>; currency: string; locale?: string }) {
  if (!active || !payload?.length) return null
  return (
    <div className="rounded-xl border border-white/20 bg-inkDeep/95 px-3 py-2 text-xs shadow-glass">
      {payload.map((item) => <p className="py-0.5 text-mist" key={item.name}><span style={{ color: item.color }}>●</span> {item.name}: {compact(item.value, currency, locale)}</p>)}
    </div>
  )
}

export function NetWorthChart({ timeline, currency, locale = 'en' }: { timeline: ProjectionYear[]; currency: string; locale?: string }) {
  const data = timeline.filter((_, index) => index === 0 || index % 5 === 0 || index === timeline.length - 1)
  return (
    <div className="h-72">
      <ResponsiveContainer width="100%" height="100%">
        <AreaChart data={data} margin={{ top: 8, right: 8, left: -12, bottom: 0 }}>
          <defs><linearGradient id="netWorthFill" x1="0" y1="0" x2="0" y2="1"><stop offset="0%" stopColor="#adc9eb" stopOpacity={0.45} /><stop offset="100%" stopColor="#adc9eb" stopOpacity={0} /></linearGradient></defs>
          <XAxis dataKey="year" tickLine={false} axisLine={false} tick={{ fill: '#c4c7c8', fontSize: 11 }} minTickGap={20} />
          <YAxis tickFormatter={(value) => compact(value, currency, locale)} tickLine={false} axisLine={false} tick={{ fill: '#c4c7c8', fontSize: 11 }} width={70} />
          <Tooltip content={<ChartTooltip currency={currency} locale={locale} />} />
          <Area type="monotone" dataKey="netWorth" name={locale === 'fr' ? 'Patrimoine net' : 'Net worth'} stroke="#adc9eb" strokeWidth={2.5} fill="url(#netWorthFill)" />
          <Area type="monotone" dataKey="inflationAdjustedNetWorth" name={locale === 'fr' ? 'Valeur actuelle' : "Today's money"} stroke="#ffffff" strokeWidth={1.5} strokeDasharray="5 5" fill="transparent" />
        </AreaChart>
      </ResponsiveContainer>
    </div>
  )
}

export function ActualNetWorthChart({ history, currency, locale = 'en' }: { history: NetWorthSnapshot[]; currency: string; locale?: string }) {
  const data = history.map((snapshot) => ({ ...snapshot, date: new Intl.DateTimeFormat(locale, { day: '2-digit', month: 'short', year: 'numeric' }).format(new Date(snapshot.capturedAt)) }))
  return (
    <div className="h-64">
      <ResponsiveContainer width="100%" height="100%">
        <AreaChart data={data} margin={{ top: 8, right: 8, left: -12, bottom: 0 }}>
          <defs><linearGradient id="actualNetWorthFill" x1="0" y1="0" x2="0" y2="1"><stop offset="0%" stopColor="#b9f6c8" stopOpacity={0.4} /><stop offset="100%" stopColor="#b9f6c8" stopOpacity={0} /></linearGradient></defs>
          <XAxis dataKey="date" tickLine={false} axisLine={false} tick={{ fill: '#c4c7c8', fontSize: 11 }} minTickGap={28} />
          <YAxis tickFormatter={(value) => compact(value, currency, locale)} tickLine={false} axisLine={false} tick={{ fill: '#c4c7c8', fontSize: 11 }} width={70} />
          <Tooltip content={<ChartTooltip currency={currency} locale={locale} />} />
          <Area type="monotone" dataKey="netWorth" name={locale === 'fr' ? 'Patrimoine observé' : 'Observed net worth'} stroke="#b9f6c8" strokeWidth={2.5} fill="url(#actualNetWorthFill)" />
        </AreaChart>
      </ResponsiveContainer>
    </div>
  )
}

export function CashFlowChart({ timeline, currency, locale = 'en' }: { timeline: ProjectionYear[]; currency: string; locale?: string }) {
  const data = timeline.filter((_, index) => index > 0 && (index % 5 === 0 || index === timeline.length - 1))
  return (
    <div className="h-64">
      <ResponsiveContainer width="100%" height="100%">
        <BarChart data={data} margin={{ top: 8, right: 8, left: -12, bottom: 0 }}>
          <XAxis dataKey="year" tickLine={false} axisLine={false} tick={{ fill: '#c4c7c8', fontSize: 11 }} />
          <YAxis tickFormatter={(value) => compact(value, currency, locale)} tickLine={false} axisLine={false} tick={{ fill: '#c4c7c8', fontSize: 11 }} width={66} />
          <Tooltip content={<ChartTooltip currency={currency} locale={locale} />} />
          <Bar dataKey="cashFlow" name={locale === 'fr' ? 'Trésorerie annuelle' : 'Annual cash flow'} radius={[5, 5, 0, 0]} fill="#b9f6c8" />
        </BarChart>
      </ResponsiveContainer>
    </div>
  )
}

export function AllocationChart({ allocation, currency }: { allocation: AllocationSlice[]; currency: string }) {
  return (
    <div className="grid items-center gap-4 sm:grid-cols-[150px_1fr]">
      <div className="h-40"><ResponsiveContainer width="100%" height="100%"><PieChart><Pie data={allocation} dataKey="value" nameKey="name" innerRadius={42} outerRadius={66} paddingAngle={3}>{allocation.map((slice, index) => <Cell key={slice.name} fill={palette[index % palette.length]} />)}</Pie><Tooltip content={<ChartTooltip currency={currency} />} /></PieChart></ResponsiveContainer></div>
      <ul className="space-y-2.5">
        {allocation.slice(0, 5).map((slice, index) => <li className="flex items-center justify-between gap-3 text-sm" key={slice.name}><span className="flex min-w-0 items-center gap-2 text-mist"><i className="h-2.5 w-2.5 shrink-0 rounded-full" style={{ backgroundColor: palette[index % palette.length] }} /> <span className="truncate">{slice.name}</span></span><span className="shrink-0 text-muted">{slice.percentage}%</span></li>)}
      </ul>
    </div>
  )
}
