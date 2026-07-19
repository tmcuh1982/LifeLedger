import { Area, AreaChart, Bar, BarChart, Cell, ComposedChart, Line, Pie, PieChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import type { AllocationSlice, AssetValuation, NetWorthSnapshot, ProjectionWealthComponent, ProjectionYear } from '../types'

/** Chooses whether projection charts are read against calendar years or the person's age. */
export type TimelineScale = 'year' | 'age'

const palette = ['#adc9eb', '#b9f6c8', '#ffddb0', '#d5b7ff', '#ffb4ab', '#ffffff']

const componentCopy: Record<string, Record<string, string>> = {
  Cash: { en: 'Cash', fr: 'Liquidités', pl: 'Gotówka', de: 'Liquidität', nl: 'Cash' },
  Etf: { en: 'ETFs', fr: 'ETF', pl: 'ETF-y', de: 'ETFs', nl: 'ETF’s' },
  Stock: { en: 'Stocks', fr: 'Actions', pl: 'Akcje', de: 'Aktien', nl: 'Aandelen' },
  Crypto: { en: 'Crypto', fr: 'Cryptomonnaies', pl: 'Kryptowaluty', de: 'Kryptowerte', nl: 'Crypto' },
  RealEstate: { en: 'Real estate', fr: 'Immobilier', pl: 'Nieruchomości', de: 'Immobilien', nl: 'Vastgoed' },
  Business: { en: 'Businesses', fr: 'Entreprises', pl: 'Firmy', de: 'Unternehmen', nl: 'Bedrijven' },
  Collectible: { en: 'Collectibles', fr: 'Objets de collection', pl: 'Przedmioty kolekcjonerskie', de: 'Sammlerstücke', nl: 'Verzamelobjecten' },
  Other: { en: 'Other assets', fr: 'Autres actifs', pl: 'Inne aktywa', de: 'Andere Vermögenswerte', nl: 'Andere activa' },
  InvestmentPlans: { en: 'Future investments', fr: 'Investissements futurs', pl: 'Przyszłe inwestycje', de: 'Künftige Investitionen', nl: 'Toekomstige beleggingen' },
  ProjectedCash: { en: 'Projected cash', fr: 'Trésorerie projetée', pl: 'Prognozowana gotówka', de: 'Prognostizierte Liquidität', nl: 'Verwachte cash' },
  PlannedExpenseReserve: { en: 'Planned expense reserve', fr: 'Réserve pour dépenses prévues', pl: 'Rezerwa na planowane wydatki', de: 'Rücklage für geplante Ausgaben', nl: 'Reserve voor geplande uitgaven' },
  Liabilities: { en: 'Outstanding debt', fr: 'Dettes restantes', pl: 'Pozostałe zadłużenie', de: 'Restschulden', nl: 'Resterende schulden' }
}

function localized(copy: Record<string, string>, locale: string) {
  return copy[locale] ?? copy.en
}

function componentLabel(component: ProjectionWealthComponent, locale: string) {
  const isBuiltInAssetCategory = component.type === 'Asset' && component.kind === component.category
  return localized(componentCopy[isBuiltInAssetCategory ? component.kind ?? component.category : component.category] ?? { en: component.category }, locale)
}

function compact(value: number, currency: string, locale = 'en') {
  return new Intl.NumberFormat(locale, { style: 'currency', currency, notation: 'compact', maximumFractionDigits: 1 }).format(value)
}

function ChartTooltip({ active, payload, currency, locale = 'en', label, scale }: { active?: boolean; payload?: Array<{ value: number; name: string; color: string }>; currency: string; locale?: string; label?: string | number; scale?: TimelineScale }) {
  if (!active || !payload?.length) return null
  return (
    <div className="rounded-xl border border-white/20 bg-inkDeep/95 px-3 py-2 text-xs shadow-glass">
      {label !== undefined && scale && <p className="border-b border-white/10 pb-1.5 font-medium text-white">{scale === 'age' ? (locale === 'fr' ? `Âge : ${label} ans` : `Age: ${label}`) : (locale === 'fr' ? `Année : ${label}` : `Year: ${label}`)}</p>}
      {payload.map((item) => <p className="py-0.5 text-mist" key={item.name}><span style={{ color: item.color }}>●</span> {item.name}: {compact(item.value, currency, locale)}</p>)}
    </div>
  )
}

export function NetWorthChart({ timeline, currency, locale = 'en', scale = 'year' }: { timeline: ProjectionYear[]; currency: string; locale?: string; scale?: TimelineScale }) {
  const sampledTimeline = timeline.filter((_, index) => index === 0 || index % 5 === 0 || index === timeline.length - 1)
  const components = Array.from(new Map(timeline.flatMap((row) => row.wealthComponents ?? []).map((component) => [component.key, component])).values())
    .filter((component) => timeline.some((row) => Math.abs(row.wealthComponents?.find((candidate) => candidate.key === component.key)?.value ?? 0) >= 0.01))
  const data = sampledTimeline.map((row) => ({
    ...row,
    ...Object.fromEntries(components.map((component, index) => [`wealthComponent${index}`, row.wealthComponents?.find((candidate) => candidate.key === component.key)?.value ?? 0]))
  }))
  const totalLabel = localized({ en: 'Total net worth', fr: 'Patrimoine net total', pl: 'Łączny majątek netto', de: 'Gesamtnettovermögen', nl: 'Totaal nettovermogen' }, locale)
  const realValueLabel = localized({ en: "Total in today's money", fr: 'Total en pouvoir d’achat actuel', pl: 'Suma w dzisiejszej wartości pieniądza', de: 'Gesamtwert in heutiger Kaufkraft', nl: 'Totaal in huidige koopkracht' }, locale)
  const projectedSales = timeline.flatMap((row) => row.assetSales ?? [])
  const saleLabels = {
    title: localized({ en: 'Planned sales applied to this path', fr: 'Ventes planifiées appliquées à cette trajectoire', pl: 'Planowane sprzedaże na tej ścieżce', de: 'Geplante Verkäufe in diesem Verlauf', nl: 'Geplande verkopen in dit verloop' }, locale),
    gross: localized({ en: 'Gross sale price', fr: 'Prix de vente brut', pl: 'Cena sprzedaży brutto', de: 'Bruttoverkaufspreis', nl: 'Brutoverkoopprijs' }, locale),
    costs: localized({ en: 'Selling costs', fr: 'Frais de vente', pl: 'Koszty sprzedaży', de: 'Verkaufskosten', nl: 'Verkoopkosten' }, locale),
    tax: localized({ en: 'Capital-gains tax', fr: 'Impôt sur la plus-value', pl: 'Podatek od zysku', de: 'Steuer auf den Gewinn', nl: 'Belasting op de meerwaarde' }, locale),
    debt: localized({ en: 'Debt repaid', fr: 'Dette remboursée', pl: 'Spłacony dług', de: 'Getilgte Schulden', nl: 'Afgeloste schuld' }, locale),
    net: localized({ en: 'Money transferred', fr: 'Argent transféré', pl: 'Przekazane środki', de: 'Übertragener Betrag', nl: 'Overgedragen bedrag' }, locale)
  }
  return (
    <div>
      <div className="h-72">
        <ResponsiveContainer width="100%" height="100%">
          <ComposedChart data={data} margin={{ top: 8, right: 8, left: -12, bottom: 0 }}>
          <XAxis dataKey={scale} tickLine={false} axisLine={false} tick={{ fill: '#c4c7c8', fontSize: 11 }} minTickGap={20} />
          <YAxis tickFormatter={(value) => compact(value, currency, locale)} tickLine={false} axisLine={false} tick={{ fill: '#c4c7c8', fontSize: 11 }} width={70} />
          <Tooltip content={<ChartTooltip currency={currency} locale={locale} scale={scale} />} />
          {components.map((component, index) => <Area key={component.key} type="monotone" dataKey={`wealthComponent${index}`} name={componentLabel(component, locale)} stackId="wealth" stroke={palette[index % palette.length]} strokeWidth={1.25} fill={palette[index % palette.length]} fillOpacity={0.24} />)}
          <Line type="monotone" dataKey="netWorth" name={totalLabel} stroke="#ffffff" strokeWidth={3} dot={false} />
          <Line type="monotone" dataKey="inflationAdjustedNetWorth" name={realValueLabel} stroke="#c4c7c8" strokeWidth={1.5} strokeDasharray="5 5" dot={false} />
          </ComposedChart>
        </ResponsiveContainer>
      </div>
      <ul className="mt-4 flex flex-wrap gap-x-4 gap-y-2 border-t border-white/10 pt-4">
        {components.map((component, index) => <li className="flex items-center gap-2 text-xs text-muted" key={component.key}><i className="h-2.5 w-2.5 rounded-full" style={{ backgroundColor: palette[index % palette.length] }} />{componentLabel(component, locale)}</li>)}
        <li className="flex items-center gap-2 text-xs font-medium text-mist"><i className="h-0.5 w-4 bg-white" />{totalLabel}</li>
      </ul>
      {projectedSales.length > 0 && <section className="mt-4 border-t border-white/10 pt-4"><p className="text-xs font-semibold uppercase tracking-[0.08em] text-muted">{saleLabels.title}</p><div className="mt-3 space-y-2">{projectedSales.map((sale) => <details className="rounded-xl border border-white/10 bg-white/5 px-4 py-3" key={sale.saleId}><summary className="cursor-pointer text-sm font-medium text-mist">{new Intl.DateTimeFormat(locale, { year: 'numeric', month: 'long' }).format(new Date(`${sale.happensOn}T00:00:00`))} · {sale.name} · {compact(sale.netProceeds, sale.currency, locale)}</summary><dl className="mt-3 grid gap-2 border-t border-white/10 pt-3 text-xs sm:grid-cols-2"><div className="flex justify-between gap-3"><dt className="text-muted">{saleLabels.gross}</dt><dd className="text-mist">{compact(sale.grossProceeds, sale.currency, locale)}</dd></div><div className="flex justify-between gap-3"><dt className="text-muted">{saleLabels.costs}</dt><dd className="text-mist">− {compact(sale.sellingCosts, sale.currency, locale)}</dd></div><div className="flex justify-between gap-3"><dt className="text-muted">{saleLabels.tax}</dt><dd className="text-mist">− {compact(sale.capitalGainsTax, sale.currency, locale)}</dd></div><div className="flex justify-between gap-3"><dt className="text-muted">{saleLabels.debt}</dt><dd className="text-mist">− {compact(sale.debtRepaid, sale.currency, locale)}</dd></div><div className="flex justify-between gap-3 border-t border-white/10 pt-2 font-medium sm:col-span-2"><dt className="text-mist">{saleLabels.net}</dt><dd className="text-sky">{compact(sale.netProceeds, sale.currency, locale)}</dd></div></dl></details>)}</div></section>}
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

/** Shows the locally observed total value of one asset rather than a projected market path. */
export function AssetValuationChart({ history, currency, locale = 'en' }: { history: AssetValuation[]; currency: string; locale?: string }) {
  const data = history.map((snapshot) => ({ ...snapshot, date: new Intl.DateTimeFormat(locale, { day: '2-digit', month: 'short', year: 'numeric' }).format(new Date(`${snapshot.valuedOn}T00:00:00`)) }))
  return (
    <div className="h-64">
      <ResponsiveContainer width="100%" height="100%">
        <AreaChart data={data} margin={{ top: 8, right: 8, left: -12, bottom: 0 }}>
          <defs><linearGradient id="assetValuationFill" x1="0" y1="0" x2="0" y2="1"><stop offset="0%" stopColor="#adc9eb" stopOpacity={0.42} /><stop offset="100%" stopColor="#adc9eb" stopOpacity={0} /></linearGradient></defs>
          <XAxis dataKey="date" tickLine={false} axisLine={false} tick={{ fill: '#c4c7c8', fontSize: 11 }} minTickGap={28} />
          <YAxis tickFormatter={(value) => compact(value, currency, locale)} tickLine={false} axisLine={false} tick={{ fill: '#c4c7c8', fontSize: 11 }} width={70} />
          <Tooltip content={<ChartTooltip currency={currency} locale={locale} />} />
          <Area type="monotone" dataKey="value" name={locale === 'fr' ? 'Valeur observée' : 'Observed value'} stroke="#adc9eb" strokeWidth={2.5} fill="url(#assetValuationFill)" />
        </AreaChart>
      </ResponsiveContainer>
    </div>
  )
}

export function CashFlowChart({ timeline, currency, locale = 'en', scale = 'year' }: { timeline: ProjectionYear[]; currency: string; locale?: string; scale?: TimelineScale }) {
  const data = timeline.filter((_, index) => index > 0 && (index % 5 === 0 || index === timeline.length - 1))
  return (
    <div className="h-64">
      <ResponsiveContainer width="100%" height="100%">
        <BarChart data={data} margin={{ top: 8, right: 8, left: -12, bottom: 0 }}>
          <XAxis dataKey={scale} tickLine={false} axisLine={false} tick={{ fill: '#c4c7c8', fontSize: 11 }} />
          <YAxis tickFormatter={(value) => compact(value, currency, locale)} tickLine={false} axisLine={false} tick={{ fill: '#c4c7c8', fontSize: 11 }} width={66} />
          <Tooltip content={<ChartTooltip currency={currency} locale={locale} scale={scale} />} />
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
