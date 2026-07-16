import { FormEvent, useEffect, useMemo, useState } from 'react'
import { Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { api } from '../api'
import type { Locale } from '../i18n'
import type { LedgerItem, ScenarioData } from '../types'

type Resource = keyof ScenarioData
type Draft = Record<string, string | boolean>

interface PlannerProps {
  data: ScenarioData
  scenarioId: string
  currency: string
  locale: Locale
  onCreate: (resource: Resource, item: Record<string, unknown>) => Promise<void>
  onUpdate: (resource: Resource, id: string, item: Record<string, unknown>) => Promise<void>
  onDelete: (resource: Resource, id: string) => Promise<void>
}

const translations = {
  en: { eyebrow: 'Life inputs', title: 'Build your financial picture.', intro: 'Enter the facts that shape your financial life. Every value stays on your server.', add: 'Add', edit: 'Edit', remove: 'Remove', none: 'Nothing added yet.', save: 'Save', saving: 'Saving…', cancel: 'Cancel', newInput: 'New entry', editInput: 'Edit entry', name: 'Name', currency: 'Currency', start: 'Start date', end: 'End date', amount: 'Amount', monthly: 'Monthly amount', expenseAmount: 'Amount each time', frequency: 'How often?', repeat: 'Repeat this event', category: 'Category', ticker: 'Ticker symbol', quantity: 'Number of shares', priceHistory: 'Price history', taxCountry: 'Country of taxation', taxRate: 'Tax rate (%)', capitalGainsTax: 'Tax on annual gains', return: 'Expected annual return (%)', growth: 'Annual growth (%)', volatility: 'Value can go up or down (%)', liquid: 'Available quickly', liquidHelp: 'Money you can use quickly, such as a bank account, savings or an investment you can sell easily.', taxable: 'Taxable income', payment: 'Monthly payment', balance: 'Outstanding balance', rate: 'Interest rate (%)', index: 'Index to inflation', date: 'Event date', oneOff: 'One-off impact', recurring: 'Monthly impact', duration: 'Duration (months, 0 = ongoing)', notes: 'Notes', monthlyContribution: 'Monthly contribution', expenseDate: 'Date of expense', saveInAdvance: 'Set money aside every month', saveInAdvanceHelp: 'LifeLedger reserves part of the amount each month and pays the expense from this envelope when it is due.', savingsStart: 'Start saving on', monthlyReserve: 'Estimated monthly envelope' },
  fr: { eyebrow: 'Données financières', title: 'Construisez votre situation financière.', intro: 'Saisissez les éléments qui façonnent votre vie financière. Toutes les données restent sur votre serveur.', add: 'Ajouter', edit: 'Modifier', remove: 'Supprimer', none: 'Aucune donnée pour le moment.', save: 'Enregistrer', saving: 'Enregistrement…', cancel: 'Annuler', newInput: 'Nouvelle entrée', editInput: 'Modifier l’entrée', name: 'Nom', currency: 'Devise', start: 'Date de début', end: 'Date de fin', amount: 'Montant', monthly: 'Montant mensuel', expenseAmount: 'Montant à chaque fois', frequency: 'À quelle fréquence ?', repeat: 'Répéter cet événement', category: 'Catégorie', ticker: 'Ticker', quantity: 'Nombre de titres', priceHistory: 'Historique du cours', taxCountry: 'Pays d’imposition', taxRate: 'Taux d’impôt (%)', capitalGainsTax: 'Impôt annuel sur les plus-values', return: 'Rendement annuel attendu (%)', growth: 'Croissance annuelle (%)', volatility: 'Valeur qui peut monter ou baisser (%)', liquid: 'Disponible rapidement', liquidHelp: 'Argent utilisable rapidement : compte bancaire, épargne ou placement facile à vendre.', taxable: 'Revenu imposable', payment: 'Mensualité', balance: 'Solde restant dû', rate: 'Taux d’intérêt (%)', index: 'Indexé sur l’inflation', date: 'Date de l’événement', oneOff: 'Impact ponctuel', recurring: 'Impact mensuel', duration: 'Durée (mois, 0 = permanent)', notes: 'Notes', monthlyContribution: 'Versement mensuel', expenseDate: 'Date de la dépense', saveInAdvance: 'Mettre de côté chaque mois', saveInAdvanceHelp: 'LifeLedger réserve une partie du montant chaque mois et paie la dépense depuis cette enveloppe à la date prévue.', savingsStart: 'Commencer à mettre de côté le', monthlyReserve: 'Enveloppe mensuelle estimée' },
} as const

const english = translations.en
const today = () => new Date().toISOString().slice(0, 10)
const fiftyYearsFromToday = () => { const date = new Date(); date.setFullYear(date.getFullYear() + 50); return date.toISOString().slice(0, 10) }

function copy(locale: Locale) { return locale === 'fr' ? translations.fr : english }
function number(value: string | boolean | undefined) { return Number(value || 0) }
function value(item: LedgerItem, key: string) { const raw = item[key]; return raw === undefined || raw === null ? '' : String(raw) }
function checked(item: LedgerItem, key: string, fallback = false) { return typeof item[key] === 'boolean' ? item[key] as boolean : fallback }
function percent(item: LedgerItem, key: string) { const raw = Number(item[key] ?? 0); return String(raw * 100) }
/** Splits a planned one-off expense evenly across all inclusive calendar months before it is due. */
function reservePerMonth(draft: Draft) {
  const start = new Date(`${String(draft.savingsStartsOn || today())}T00:00:00`)
  const due = new Date(`${String(draft.startsOn || today())}T00:00:00`)
  const months = Math.max(1, (due.getFullYear() - start.getFullYear()) * 12 + due.getMonth() - start.getMonth() + 1)
  return number(draft.monthlyAmount) / months
}

function resourceDefinitions(locale: Locale) {
  const french = locale === 'fr'
  return [
    { resource: 'incomes' as const, title: french ? 'Revenus' : 'Income', description: french ? 'Salaires, activité indépendante, loyers, dividendes et pensions.' : 'Salaries, freelance, rental, dividends and pensions.', symbol: '↗' },
    { resource: 'assets' as const, title: french ? 'Actifs' : 'Assets', description: french ? 'Liquidités, placements, immobilier, entreprises et objets de valeur.' : 'Cash, investments, property, businesses and valuables.', symbol: '◈' },
    { resource: 'liabilities' as const, title: french ? 'Dettes' : 'Liabilities', description: french ? 'Crédits immobiliers, prêts, leasing et crédit.' : 'Mortgages, loans, leasing and credit.', symbol: '↓' },
    { resource: 'expenses' as const, title: french ? 'Dépenses' : 'Expenses', description: french ? 'Dépenses récurrentes ou exceptionnelles, indexées sur l’inflation.' : 'Recurring and exceptional spending, inflation indexed.', symbol: '−' },
    { resource: 'investments' as const, title: french ? 'Investissements' : 'Investments', description: french ? 'Versements réguliers et rendement attendu.' : 'Recurring contributions and expected returns.', symbol: '⌁' },
    { resource: 'events' as const, title: french ? 'Événements de vie' : 'Life events', description: french ? 'Logement, enfant, héritage ou changement de carrière.' : 'Homes, children, inheritance, career changes and more.', symbol: '✦' },
  ]
}

function itemValue(item: LedgerItem, resource: Resource) {
  if (resource === 'assets') return item.currentValue ?? 0
  if (resource === 'liabilities') return -(item.outstandingBalance ?? 0)
  if (resource === 'events') return item.oneOffCashImpact ?? 0
  return item.monthlyAmount ?? Number(item.monthlyContribution ?? 0)
}

function money(amount: number, currency: string, locale: Locale) {
  return new Intl.NumberFormat(locale, { style: 'currency', currency, maximumFractionDigits: 0, signDisplay: amount < 0 ? 'always' : 'auto' }).format(amount)
}

function newDraft(resource: Resource, currency: string): Draft {
  const base = { name: '', currency, startsOn: today(), endsOn: '' }
  if (resource === 'incomes') return { ...base, kind: 'Salary', monthlyAmount: '', annualGrowthRate: '2', isTaxable: true, taxRate: '0', taxCountryCode: '' }
  if (resource === 'assets') return { ...base, kind: 'Cash', currentValue: '', ticker: '', quantity: '0', capitalGainsTaxRate: '0', capitalGainsTaxCountryCode: '', expectedAnnualReturn: '0', volatility: '0', isLiquid: true }
  if (resource === 'liabilities') return { ...base, kind: 'Mortgage', outstandingBalance: '', interestRate: '4', monthlyPayment: '', paidOffOn: '' }
  if (resource === 'expenses') return { ...base, kind: 'Recurring', monthlyAmount: '', frequency: 'Monthly', endsOn: fiftyYearsFromToday(), indexedToInflation: true, saveInAdvance: false, savingsStartsOn: today() }
  if (resource === 'investments') return { ...base, monthlyContribution: '', expectedAnnualReturn: '6' }
  return { ...base, kind: 'Custom', happensOn: today(), repeats: false, recurrenceFrequency: 'Monthly', recurrenceEndsOn: fiftyYearsFromToday(), oneOffCashImpact: '', monthlyCashImpact: '0', durationMonths: '0', notes: '' }
}

function editDraft(resource: Resource, item: LedgerItem, currency: string): Draft {
  const base = { name: value(item, 'name'), currency: value(item, 'currency') || currency, startsOn: value(item, 'startsOn'), endsOn: value(item, 'endsOn') }
  if (resource === 'incomes') return { ...base, kind: value(item, 'kind'), monthlyAmount: value(item, 'monthlyAmount'), annualGrowthRate: percent(item, 'annualGrowthRate'), isTaxable: checked(item, 'isTaxable', true), taxRate: percent(item, 'taxRate'), taxCountryCode: value(item, 'taxCountryCode') }
  if (resource === 'assets') return { ...base, kind: value(item, 'kind'), currentValue: value(item, 'currentValue'), ticker: value(item, 'ticker'), quantity: value(item, 'quantity'), capitalGainsTaxRate: percent(item, 'capitalGainsTaxRate'), capitalGainsTaxCountryCode: value(item, 'capitalGainsTaxCountryCode'), expectedAnnualReturn: percent(item, 'expectedAnnualReturn'), volatility: percent(item, 'volatility'), isLiquid: checked(item, 'isLiquid', true) }
  if (resource === 'liabilities') return { ...base, kind: value(item, 'kind'), outstandingBalance: value(item, 'outstandingBalance'), interestRate: percent(item, 'interestRate'), monthlyPayment: value(item, 'monthlyPayment'), paidOffOn: value(item, 'paidOffOn') }
  if (resource === 'expenses') return { ...base, kind: value(item, 'kind'), monthlyAmount: value(item, 'monthlyAmount'), frequency: value(item, 'frequency') || 'Monthly', indexedToInflation: checked(item, 'indexedToInflation', true), saveInAdvance: checked(item, 'saveInAdvance'), savingsStartsOn: value(item, 'savingsStartsOn') || today() }
  if (resource === 'investments') return { ...base, monthlyContribution: value(item, 'monthlyContribution'), expectedAnnualReturn: percent(item, 'expectedAnnualReturn') }
  return { ...base, kind: value(item, 'kind'), happensOn: value(item, 'happensOn'), repeats: item.recurrenceFrequency !== undefined && item.recurrenceFrequency !== null, recurrenceFrequency: value(item, 'recurrenceFrequency') || 'Monthly', recurrenceEndsOn: value(item, 'recurrenceEndsOn') || fiftyYearsFromToday(), oneOffCashImpact: value(item, 'oneOffCashImpact'), monthlyCashImpact: value(item, 'monthlyCashImpact'), durationMonths: value(item, 'durationMonths'), notes: value(item, 'notes') }
}

function payload(resource: Resource, draft: Draft) {
  const common = { name: String(draft.name).trim(), currency: String(draft.currency).toUpperCase() }
  const dates = { startsOn: String(draft.startsOn || today()), endsOn: draft.endsOn ? String(draft.endsOn) : null }
  if (resource === 'incomes') return { ...common, ...dates, kind: draft.kind, monthlyAmount: number(draft.monthlyAmount), annualGrowthRate: number(draft.annualGrowthRate) / 100, isTaxable: Boolean(draft.isTaxable), taxRate: number(draft.taxRate) / 100, taxCountryCode: String(draft.taxCountryCode || '').trim().toUpperCase() || null }
  if (resource === 'assets') return { ...common, kind: draft.kind, currentValue: number(draft.currentValue), ticker: String(draft.ticker || '').trim().toUpperCase() || null, quantity: number(draft.quantity), capitalGainsTaxRate: number(draft.capitalGainsTaxRate) / 100, capitalGainsTaxCountryCode: String(draft.capitalGainsTaxCountryCode || '').trim().toUpperCase() || null, expectedAnnualReturn: number(draft.expectedAnnualReturn) / 100, volatility: number(draft.volatility) / 100, isLiquid: Boolean(draft.isLiquid) }
  if (resource === 'liabilities') return { ...common, kind: draft.kind, outstandingBalance: number(draft.outstandingBalance), interestRate: number(draft.interestRate) / 100, monthlyPayment: number(draft.monthlyPayment), paidOffOn: draft.paidOffOn ? String(draft.paidOffOn) : null }
  if (resource === 'expenses') return { ...common, ...dates, kind: draft.kind, frequency: draft.frequency || 'Monthly', monthlyAmount: number(draft.monthlyAmount), indexedToInflation: Boolean(draft.indexedToInflation), saveInAdvance: draft.kind === 'Exceptional' && Boolean(draft.saveInAdvance), savingsStartsOn: draft.kind === 'Exceptional' && draft.saveInAdvance ? String(draft.savingsStartsOn || today()) : null }
  if (resource === 'investments') return { ...common, ...dates, monthlyContribution: number(draft.monthlyContribution), expectedAnnualReturn: number(draft.expectedAnnualReturn) / 100 }
  return { ...common, kind: draft.kind, happensOn: String(draft.happensOn || today()), recurrenceFrequency: draft.repeats ? draft.recurrenceFrequency || 'Monthly' : null, recurrenceEndsOn: draft.repeats ? String(draft.recurrenceEndsOn || fiftyYearsFromToday()) : null, oneOffCashImpact: number(draft.oneOffCashImpact), monthlyCashImpact: draft.repeats ? 0 : number(draft.monthlyCashImpact), durationMonths: draft.repeats ? 0 : number(draft.durationMonths), notes: String(draft.notes || '') }
}

export function Planner({ data, currency, locale, onCreate, onUpdate, onDelete }: PlannerProps) {
  const [active, setActive] = useState<{ resource: Resource; item?: LedgerItem } | null>(null)
  const [draft, setDraft] = useState<Draft>({})
  const [submitting, setSubmitting] = useState(false)
  const [rates, setRates] = useState<Array<{ code: string; unitsPerEuro: number; source: string; isStale: boolean }>>([])
  const [refreshingRates, setRefreshingRates] = useState(false)
  const t = copy(locale)
  const definitions = useMemo(() => resourceDefinitions(locale), [locale])

  useEffect(() => { void api.currencies().then(setRates).catch(() => setRates([])) }, [])
  async function refreshRates() { setRefreshingRates(true); try { setRates(await api.refreshCurrencies()) } finally { setRefreshingRates(false) } }

  function open(resource: Resource, item?: LedgerItem) { setActive({ resource, item }); setDraft(item ? editDraft(resource, item, currency) : newDraft(resource, currency)) }
  function setField(name: string, next: string | boolean) {
    setDraft((current) => {
      if (active?.resource === 'assets' && name === 'kind' && next === 'Cash') {
        return { ...current, kind: 'Cash', expectedAnnualReturn: '0', volatility: '0', isLiquid: true }
      }
      if (active?.resource === 'expenses' && name === 'kind' && next === 'Exceptional') {
        return { ...current, kind: 'Exceptional', endsOn: '', saveInAdvance: true, savingsStartsOn: String(current.savingsStartsOn || today()) }
      }
      return { ...current, [name]: next }
    })
  }
  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!active || !String(draft.name).trim()) return
    setSubmitting(true)
    try { active.item ? await onUpdate(active.resource, active.item.id, payload(active.resource, draft)) : await onCreate(active.resource, payload(active.resource, draft)); setActive(null) }
    finally { setSubmitting(false) }
  }

  return <section className="space-y-5"><div><p className="eyebrow">{t.eyebrow}</p><h1 className="mt-2 text-3xl font-semibold text-white">{t.title}</h1><p className="mt-2 max-w-2xl text-sm leading-6 text-muted">{t.intro}</p></div><article className="section-card"><div className="flex flex-col justify-between gap-4 sm:flex-row sm:items-center"><div><p className="eyebrow">{locale === 'fr' ? 'Devises' : 'Currencies'}</p><p className="mt-1 text-sm text-muted">{locale === 'fr' ? 'Taux enregistrés localement, exprimés pour 1 EUR.' : 'Rates cached locally, quoted per 1 EUR.'}</p></div><button className="ghost-button" disabled={refreshingRates} onClick={() => void refreshRates()}>{refreshingRates ? (locale === 'fr' ? 'Actualisation…' : 'Refreshing…') : (locale === 'fr' ? 'Actualiser les taux BCE' : 'Refresh ECB rates')}</button></div><div className="mt-4 flex flex-wrap gap-2">{rates.slice(0, 8).map((rate) => <span className="rounded-xl bg-white/5 px-3 py-2 text-xs text-mist" key={rate.code}>{rate.code} · {rate.unitsPerEuro.toFixed(4)} {rate.isStale ? '⚠' : ''}</span>)}</div></article><div className="grid gap-4 xl:grid-cols-2">{definitions.map((definition) => <article className="section-card" key={definition.resource}><div className="flex items-start justify-between gap-4"><div><span className="grid h-9 w-9 place-items-center rounded-xl bg-white/10 text-sky">{definition.symbol}</span><h2 className="mt-4 text-lg font-semibold text-white">{definition.title}</h2><p className="mt-1 text-sm leading-5 text-muted">{definition.description}</p></div><button className="ghost-button shrink-0" onClick={() => open(definition.resource)}>{t.add}</button></div><div className="mt-5 divide-y divide-white/10 border-y border-white/10">{data[definition.resource].length === 0 ? <p className="py-4 text-sm text-muted">{t.none}</p> : data[definition.resource].map((item) => <div className="flex items-center justify-between gap-4 py-3" key={item.id}><button className="min-w-0 text-left" onClick={() => open(definition.resource, item)}><p className="truncate text-sm font-medium text-mist">{item.name}</p><p className="mt-0.5 text-xs text-muted">{item.kind ?? (definition.resource === 'investments' ? t.monthlyContribution : '')}{item.ticker ? ` · ${String(item.ticker)}` : ''}</p></button><div className="flex shrink-0 items-center gap-3"><span className="text-sm text-sky">{money(itemValue(item, definition.resource), currency, locale)}</span><button className="text-xs text-muted transition hover:text-mist" onClick={() => open(definition.resource, item)}>{t.edit}</button><button className="text-xs text-muted transition hover:text-danger" onClick={() => void onDelete(definition.resource, item.id)}>{t.remove}</button></div></div>)}</div></article>)}</div>{active && <Editor assetId={active.item?.id} resource={active.resource} draft={draft} locale={locale} submitting={submitting} editing={Boolean(active.item)} onField={setField} onCancel={() => setActive(null)} onSubmit={submit} />}</section>
}

function Editor({ assetId, resource, draft, locale, submitting, editing, onField, onCancel, onSubmit }: { assetId?: string; resource: Resource; draft: Draft; locale: Locale; submitting: boolean; editing: boolean; onField: (name: string, value: string | boolean) => void; onCancel: () => void; onSubmit: (event: FormEvent) => Promise<void> }) {
  const t = copy(locale)
  const field = (name: string, label: string, type = 'text', required = false, alignLabel = false) => <label className={`block text-sm text-mist ${alignLabel ? 'flex min-w-0 flex-col' : ''}`}><span className={alignLabel ? 'flex min-h-10 items-end' : ''}>{label}</span><input className="field mt-2" required={required} type={type} value={String(draft[name] ?? '')} onChange={(event) => onField(name, event.target.value)} /></label>
  const select = (name: string, label: string, options: string[]) => <label className="block text-sm text-mist">{label}<select className="field mt-2" value={String(draft[name] ?? '')} onChange={(event) => onField(name, event.target.value)}>{options.map((option) => <option className="bg-panel" key={option}>{option}</option>)}</select></label>
  const frequencyOptions = locale === 'fr'
    ? [{ value: 'Daily', label: 'Chaque jour' }, { value: 'Weekly', label: 'Chaque semaine' }, { value: 'EveryTwoWeeks', label: 'Toutes les 2 semaines' }, { value: 'Monthly', label: 'Chaque mois' }, { value: 'Quarterly', label: 'Tous les 3 mois' }, { value: 'Yearly', label: 'Chaque année' }, { value: 'EveryFiveYears', label: 'Tous les 5 ans' }]
    : [{ value: 'Daily', label: 'Every day' }, { value: 'Weekly', label: 'Every week' }, { value: 'EveryTwoWeeks', label: 'Every 2 weeks' }, { value: 'Monthly', label: 'Every month' }, { value: 'Quarterly', label: 'Every 3 months' }, { value: 'Yearly', label: 'Every year' }, { value: 'EveryFiveYears', label: 'Every 5 years' }]
  const frequencySelect = <label className="block text-sm text-mist">{t.frequency}<select className="field mt-2" value={String(draft.frequency ?? 'Monthly')} onChange={(event) => onField('frequency', event.target.value)}>{frequencyOptions.map((option) => <option className="bg-panel" key={option.value} value={option.value}>{option.label}</option>)}</select></label>
  const recurrenceFrequencySelect = <label className="block text-sm text-mist">{t.frequency}<select className="field mt-2" value={String(draft.recurrenceFrequency ?? 'Monthly')} onChange={(event) => onField('recurrenceFrequency', event.target.value)}>{frequencyOptions.map((option) => <option className="bg-panel" key={option.value} value={option.value}>{option.label}</option>)}</select></label>
  const eventKindOptions = locale === 'fr'
    ? [{ value: 'HousePurchase', label: 'Achat d’un logement' }, { value: 'NewChild', label: 'Nouvel enfant' }, { value: 'Inheritance', label: 'Héritage' }, { value: 'JobLoss', label: 'Perte d’emploi' }, { value: 'SalaryIncrease', label: 'Augmentation de salaire' }, { value: 'BusinessCreation', label: 'Création d’entreprise' }, { value: 'EarlyRetirement', label: 'Retraite anticipée' }, { value: 'Relocation', label: 'Déménagement' }, { value: 'Divorce', label: 'Séparation ou divorce' }, { value: 'Custom', label: 'Autre événement' }]
    : [{ value: 'HousePurchase', label: 'House purchase' }, { value: 'NewChild', label: 'New child' }, { value: 'Inheritance', label: 'Inheritance' }, { value: 'JobLoss', label: 'Job loss' }, { value: 'SalaryIncrease', label: 'Salary increase' }, { value: 'BusinessCreation', label: 'Business creation' }, { value: 'EarlyRetirement', label: 'Early retirement' }, { value: 'Relocation', label: 'Relocation' }, { value: 'Divorce', label: 'Separation or divorce' }, { value: 'Custom', label: 'Other event' }]
  const eventKindSelect = <label className="block text-sm text-mist">{t.category}<select className="field mt-2" value={String(draft.kind ?? 'Custom')} onChange={(event) => onField('kind', event.target.value)}>{eventKindOptions.map((option) => <option className="bg-panel" key={option.value} value={option.value}>{option.label}</option>)}</select></label>
  const toggle = (name: string, label: string, help?: string) => <label className="flex items-start gap-3 rounded-xl border border-white/15 bg-white/5 px-3 py-3 text-sm text-mist"><input className="mt-1" checked={Boolean(draft[name])} type="checkbox" onChange={(event) => onField(name, event.target.checked)} /><span><span className="block">{label}</span>{help && <span className="mt-1 block text-xs leading-5 text-muted">{help}</span>}</span></label>
  const currencyCombobox = <label className="block text-sm text-mist">{t.currency}<input className="field mt-2" required list="lifeledger-currencies" value={String(draft.currency ?? '')} onChange={(event) => onField('currency', event.target.value.toUpperCase())} /><datalist id="lifeledger-currencies">{['EUR', 'USD', 'PLN', 'GBP', 'CHF', 'CAD', 'JPY'].map((code) => <option key={code} value={code} />)}</datalist></label>
  const countryField = (name: string, label: string) => <label className="block text-sm text-mist">{label}<input className="field mt-2" list="lifeledger-tax-countries" value={String(draft[name] ?? '')} onChange={(event) => onField(name, event.target.value.toUpperCase())} /><datalist id="lifeledger-tax-countries"><option value="PL">Pologne</option><option value="FR">France</option><option value="BE">Belgique</option><option value="DE">Allemagne</option><option value="NL">Pays-Bas</option><option value="US">États-Unis</option></datalist></label>
  const common = <><div className="grid gap-4 sm:grid-cols-2">{field('name', t.name, 'text', true)}{currencyCombobox}</div></>
  const dated = <div className="grid gap-4 sm:grid-cols-2">{field('startsOn', t.start, 'date')}{field('endsOn', t.end, 'date')}</div>
  const monthlyReserve = reservePerMonth(draft)
  const content = resource === 'incomes' ? <>{common}{select('kind', t.category, ['Salary', 'Freelance', 'Rental', 'Dividends', 'Pension', 'Royalties', 'Other'])}<div className="grid gap-4 sm:grid-cols-2">{field('monthlyAmount', t.monthly, 'number', true)}{field('annualGrowthRate', t.growth, 'number')}</div>{dated}{toggle('isTaxable', t.taxable)}{draft.isTaxable && <div className="grid gap-4 sm:grid-cols-2">{countryField('taxCountryCode', t.taxCountry)}{field('taxRate', t.taxRate, 'number')}</div>}</> : resource === 'assets' ? <>{common}{select('kind', t.category, ['Cash', 'Etf', 'Stock', 'Crypto', 'RealEstate', 'Business', 'Collectible', 'Other'])}{(draft.kind === 'Etf' || draft.kind === 'Stock') && <div className="grid gap-4 sm:grid-cols-2">{field('ticker', t.ticker, 'text')}{field('quantity', t.quantity, 'number', true)}</div>}<div className="grid gap-4 sm:grid-cols-3">{field('currentValue', t.amount, 'number', true, true)}{field('expectedAnnualReturn', t.return, 'number', false, true)}{field('volatility', t.volatility, 'number', false, true)}</div>{draft.kind !== 'Cash' && <div className="grid gap-4 sm:grid-cols-2">{countryField('capitalGainsTaxCountryCode', t.taxCountry)}{field('capitalGainsTaxRate', t.capitalGainsTax, 'number')}</div>}{assetId && (draft.kind === 'Etf' || draft.kind === 'Stock') && <AssetPriceChart assetId={assetId} locale={locale} title={t.priceHistory} />}{draft.kind !== 'Cash' && toggle('isLiquid', t.liquid, t.liquidHelp)}</> : resource === 'liabilities' ? <>{common}{select('kind', t.category, ['Mortgage', 'Loan', 'Leasing', 'Credit', 'Other'])}<div className="grid gap-4 sm:grid-cols-3">{field('outstandingBalance', t.balance, 'number', true)}{field('interestRate', t.rate, 'number')}{field('monthlyPayment', t.payment, 'number')}</div>{field('paidOffOn', t.end, 'date')}</> : resource === 'expenses' ? <>{common}{select('kind', t.category, ['Recurring', 'Exceptional'])}{draft.kind === 'Recurring' ? <>{frequencySelect}{field('monthlyAmount', t.expenseAmount, 'number', true)}{dated}{toggle('indexedToInflation', t.index)}</> : <><div className="grid gap-4 sm:grid-cols-2">{field('monthlyAmount', t.amount, 'number', true)}{field('startsOn', t.expenseDate, 'date', true)}</div>{toggle('saveInAdvance', t.saveInAdvance, t.saveInAdvanceHelp)}</>}{draft.kind === 'Exceptional' && draft.saveInAdvance && <><div className="grid gap-4 sm:grid-cols-2">{field('savingsStartsOn', t.savingsStart, 'date', true)}<div className="rounded-xl border border-sky/20 bg-sky/10 px-4 py-3 text-sm text-sky"><p className="font-medium">{t.monthlyReserve}</p><p className="mt-1 text-lg">{new Intl.NumberFormat(locale, { style: 'currency', currency: String(draft.currency || 'EUR') }).format(monthlyReserve)}</p></div></div><p className="rounded-xl bg-white/5 px-4 py-3 text-xs leading-5 text-muted">{locale === 'fr' ? 'Cette somme est réservée chaque mois jusqu’au mois de la dépense. Elle reste comprise dans votre patrimoine jusqu’au paiement.' : 'This amount is reserved every month through the month of the expense. It remains part of your net worth until it is paid.'}</p></>}</> : resource === 'investments' ? <>{common}<div className="grid gap-4 sm:grid-cols-2">{field('monthlyContribution', t.monthlyContribution, 'number', true)}{field('expectedAnnualReturn', t.return, 'number')}</div>{dated}</> : <>{common}{eventKindSelect}{field('happensOn', t.date, 'date')}{toggle('repeats', t.repeat)}{draft.repeats ? <>{recurrenceFrequencySelect}{field('recurrenceEndsOn', t.end, 'date')}{field('oneOffCashImpact', t.expenseAmount, 'number')}</> : <><div className="grid gap-4 sm:grid-cols-2">{field('oneOffCashImpact', t.oneOff, 'number')}{field('monthlyCashImpact', t.recurring, 'number')}</div>{field('durationMonths', t.duration, 'number')}</>}<label className="block text-sm text-mist">{t.notes}<textarea className="field mt-2 min-h-24" value={String(draft.notes ?? '')} onChange={(event) => onField('notes', event.target.value)} /></label></>
  return <div className="fixed inset-0 z-20 overflow-y-auto bg-inkDeep/70 p-4 backdrop-blur-sm"><form className="glass mx-auto my-6 w-full max-w-2xl rounded-3xl p-6" onSubmit={(event) => void onSubmit(event)}><div className="flex items-start justify-between gap-4"><div><p className="eyebrow">{editing ? t.editInput : t.newInput}</p><h2 className="mt-2 text-xl font-semibold text-white">{String(draft.name || '—')}</h2></div><button className="text-muted hover:text-white" type="button" onClick={onCancel}>✕</button></div><div className="mt-6 space-y-4">{content}</div><div className="mt-7 flex justify-end gap-3"><button className="ghost-button" type="button" onClick={onCancel}>{t.cancel}</button><button className="primary-button" disabled={submitting}>{submitting ? t.saving : t.save}</button></div></form></div>
}

function AssetPriceChart({ assetId, locale, title }: { assetId: string; locale: Locale; title: string }) {
  const [history, setHistory] = useState<Array<{ capturedAt: string; price: number; currency: string }>>([])
  useEffect(() => { void api.assetHistory(assetId).then(setHistory).catch(() => setHistory([])) }, [assetId])
  if (history.length === 0) return <p className="rounded-xl bg-white/5 px-3 py-3 text-xs text-muted">{locale === 'fr' ? 'Le graphique apparaîtra après la première mise à jour du cours.' : 'The chart appears after the first quote refresh.'}</p>
  const currency = history.at(-1)?.currency ?? 'EUR'
  const points = history.map((entry) => ({ ...entry, label: new Date(entry.capturedAt).toLocaleDateString(locale, { month: 'short', day: 'numeric' }) }))
  return <article className="rounded-2xl border border-white/10 bg-white/5 p-4"><p className="text-sm font-medium text-mist">{title}</p><div className="mt-3 h-44"><ResponsiveContainer width="100%" height="100%"><LineChart data={points}><XAxis dataKey="label" tick={{ fill: '#c4c7c8', fontSize: 11 }} /><YAxis tick={{ fill: '#c4c7c8', fontSize: 11 }} width={45} /><Tooltip formatter={(price) => new Intl.NumberFormat(locale, { style: 'currency', currency }).format(Number(price ?? 0))} /><Line dataKey="price" dot={false} stroke="#adc9eb" strokeWidth={2} type="monotone" /></LineChart></ResponsiveContainer></div></article>
}
