import { useEffect, useMemo, useState } from 'react'
import { api } from '../api'
import { assetCategoryLabel } from '../assetCategories'
import type { Locale } from '../i18n'
import type { AssetCategory, AssetDossierResponse, AssetValuation, LedgerItem } from '../types'
import { AssetValuationChart } from './Charts'

type CurrencyRate = { code: string; unitsPerEuro: number }

interface WealthProps {
  assets: LedgerItem[]
  assetCategories: AssetCategory[]
  baseCurrency: string
  locale: Locale
  onEdit: () => void
}

const copy = {
  en: { eyebrow: 'Detailed wealth', title: 'Understand what you own today.', intro: 'Current estimates, acquisition costs, gains, linked debt and local valuation history in one place.', edit: 'Update assets', current: 'Current assets', cost: 'Total purchase cost', gain: 'Unrealised gain', equity: 'Value after linked debt', purchase: 'Purchase cost', debt: 'Linked debt', lastEstimate: 'Last estimate', source: 'Source', history: 'Valuation history', points: 'point(s)', currentStatus: 'Estimate is current', staleStatus: 'Estimate should be updated', unknownStatus: 'Estimate date missing', noHistory: 'A first history point will appear after the next valuation.', noAssets: 'Add an asset to start building your detailed wealth view.', loading: 'Loading wealth details…' },
  fr: { eyebrow: 'Patrimoine détaillé', title: 'Comprenez ce que vous possédez aujourd’hui.', intro: 'Estimations actuelles, coûts d’achat, plus-values, dettes liées et historique local réunis au même endroit.', edit: 'Mettre à jour les actifs', current: 'Total des actifs', cost: 'Coût total d’achat', gain: 'Plus-value actuelle', equity: 'Valeur après dettes liées', purchase: 'Coût d’achat', debt: 'Dette liée', lastEstimate: 'Dernière estimation', source: 'Source', history: 'Historique des valorisations', points: 'point(s)', currentStatus: 'Estimation à jour', staleStatus: 'Estimation à actualiser', unknownStatus: 'Date d’estimation manquante', noHistory: 'Un premier point apparaîtra après la prochaine valorisation.', noAssets: 'Ajoutez un actif pour commencer à détailler votre patrimoine.', loading: 'Chargement du patrimoine…' },
  pl: { eyebrow: 'Szczegółowy majątek', title: 'Zrozum, co posiadasz dzisiaj.', intro: 'Bieżące wyceny, koszty zakupu, zyski, powiązane długi i lokalna historia w jednym miejscu.', edit: 'Aktualizuj aktywa', current: 'Aktywa ogółem', cost: 'Łączny koszt zakupu', gain: 'Aktualny zysk', equity: 'Wartość po odjęciu długów', purchase: 'Koszt zakupu', debt: 'Powiązany dług', lastEstimate: 'Ostatnia wycena', source: 'Źródło', history: 'Historia wycen', points: 'punkt(y)', currentStatus: 'Wycena jest aktualna', staleStatus: 'Wycena wymaga aktualizacji', unknownStatus: 'Brak daty wyceny', noHistory: 'Pierwszy punkt pojawi się po kolejnej wycenie.', noAssets: 'Dodaj aktywo, aby zbudować szczegółowy widok majątku.', loading: 'Ładowanie majątku…' },
  de: { eyebrow: 'Vermögen im Detail', title: 'Verstehen Sie, was Sie heute besitzen.', intro: 'Aktuelle Schätzungen, Kaufkosten, Gewinne, verbundene Schulden und lokaler Verlauf an einem Ort.', edit: 'Vermögen aktualisieren', current: 'Vermögen gesamt', cost: 'Gesamte Kaufkosten', gain: 'Aktueller Gewinn', equity: 'Wert nach Schulden', purchase: 'Kaufkosten', debt: 'Verbundene Schulden', lastEstimate: 'Letzte Schätzung', source: 'Quelle', history: 'Bewertungsverlauf', points: 'Punkt(e)', currentStatus: 'Schätzung ist aktuell', staleStatus: 'Schätzung aktualisieren', unknownStatus: 'Schätzdatum fehlt', noHistory: 'Nach der nächsten Bewertung erscheint der erste Punkt.', noAssets: 'Fügen Sie einen Vermögenswert hinzu, um die Detailansicht zu starten.', loading: 'Vermögen wird geladen…' },
  nl: { eyebrow: 'Vermogen in detail', title: 'Begrijp wat u vandaag bezit.', intro: 'Actuele schattingen, aankoopkosten, winst, gekoppelde schulden en lokale historie op één plek.', edit: 'Activa bijwerken', current: 'Totale activa', cost: 'Totale aankoopkosten', gain: 'Huidige winst', equity: 'Waarde na gekoppelde schuld', purchase: 'Aankoopkosten', debt: 'Gekoppelde schuld', lastEstimate: 'Laatste schatting', source: 'Bron', history: 'Waarderingshistorie', points: 'punt(en)', currentStatus: 'Schatting is actueel', staleStatus: 'Schatting moet worden bijgewerkt', unknownStatus: 'Schattingdatum ontbreekt', noHistory: 'Na de volgende waardering verschijnt het eerste punt.', noAssets: 'Voeg een actief toe om uw gedetailleerde vermogen te bekijken.', loading: 'Vermogen laden…' },
} as const

function money(value: number, currency: string, locale: Locale, compact = false) {
  return new Intl.NumberFormat(locale, { style: 'currency', currency, notation: compact ? 'compact' : 'standard', maximumFractionDigits: compact ? 1 : 0 }).format(value)
}

function convert(value: number, from: string, to: string, rates: CurrencyRate[]) {
  if (from === to) return value
  const fromRate = rates.find((rate) => rate.code === from)?.unitsPerEuro
  const toRate = rates.find((rate) => rate.code === to)?.unitsPerEuro
  return fromRate && toRate ? value / fromRate * toRate : 0
}

function valuationState(asset: LedgerItem) {
  if (asset.kind === 'Cash') return 'current'
  if (!asset.valuedOn) return 'unknown'
  const ageInDays = (Date.now() - new Date(`${String(asset.valuedOn)}T00:00:00`).getTime()) / 86_400_000
  const maximumAge = asset.ticker || asset.kind === 'Crypto' ? 7 : 365
  return ageInDays > maximumAge ? 'stale' : 'current'
}

/** Renders an actionable, local-first inventory of current assets and their observed valuation history. */
export function Wealth({ assets, assetCategories, baseCurrency, locale, onEdit }: WealthProps) {
  const text = copy[locale]
  const [dossiers, setDossiers] = useState<AssetDossierResponse[]>([])
  const [histories, setHistories] = useState<Record<string, AssetValuation[]>>({})
  const [rates, setRates] = useState<CurrencyRate[]>([])
  const [selectedId, setSelectedId] = useState<string>()
  const [loading, setLoading] = useState(false)

  useEffect(() => {
    let active = true
    setLoading(true)
    void Promise.all([
      Promise.all(assets.map((asset) => api.assetDossier(asset.id))),
      Promise.all(assets.map(async (asset) => [asset.id, await api.assetValuations(asset.id)] as const)),
      api.currencies(),
    ]).then(([nextDossiers, nextHistories, nextRates]) => {
      if (!active) return
      setDossiers(nextDossiers)
      setHistories(Object.fromEntries(nextHistories))
      setRates(nextRates)
      setSelectedId((current) => current && assets.some((asset) => asset.id === current) ? current : assets[0]?.id)
    }).finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [assets])

  const totals = useMemo(() => dossiers.reduce((sum, dossier) => {
    const currency = String(dossier.asset.currency || baseCurrency)
    return {
      current: sum.current + convert(Number(dossier.asset.currentValue || 0), currency, baseCurrency, rates),
      cost: sum.cost + convert(dossier.performance.acquisitionBasis, currency, baseCurrency, rates),
      gain: sum.gain + convert(dossier.performance.grossGain, currency, baseCurrency, rates),
      equity: sum.equity + convert(dossier.performance.netEquity, currency, baseCurrency, rates),
    }
  }, { current: 0, cost: 0, gain: 0, equity: 0 }), [dossiers, rates, baseCurrency])

  const selected = dossiers.find((dossier) => dossier.asset.id === selectedId) ?? dossiers[0]
  const selectedHistory = selected ? histories[selected.asset.id] ?? [] : []

  if (!assets.length) return <section className="space-y-5"><Header locale={locale} onEdit={onEdit} /><article className="section-card py-12 text-center text-sm text-muted">{text.noAssets}</article></section>
  if (loading && !dossiers.length) return <section className="space-y-5"><Header locale={locale} onEdit={onEdit} /><article className="section-card animate-pulse py-12 text-center text-sm text-muted">{text.loading}</article></section>

  return <section className="space-y-5">
    <Header locale={locale} onEdit={onEdit} />
    <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
      <Summary label={text.current} value={money(totals.current, baseCurrency, locale, true)} symbol="◈" />
      <Summary label={text.cost} value={money(totals.cost, baseCurrency, locale, true)} symbol="◇" />
      <Summary label={text.gain} value={money(totals.gain, baseCurrency, locale, true)} symbol="↗" tone={totals.gain >= 0 ? 'text-success' : 'text-warning'} />
      <Summary label={text.equity} value={money(totals.equity, baseCurrency, locale, true)} symbol="⌁" />
    </div>
    <div className="grid gap-4 xl:grid-cols-[1fr_1.25fr]">
      <div className="space-y-3">{dossiers.map((dossier) => {
        const asset = dossier.asset
        const currency = String(asset.currency || baseCurrency)
        const status = valuationState(asset)
        const selectedCard = asset.id === selected?.asset.id
        return <button className={`section-card glass-hover w-full text-left ${selectedCard ? 'ring-1 ring-sky/70' : ''}`} key={asset.id} onClick={() => setSelectedId(asset.id)}>
          <div className="flex items-start justify-between gap-4"><div className="min-w-0"><p className="truncate text-base font-semibold text-white">{asset.name}</p><p className="mt-1 text-xs text-muted">{assetCategoryLabel(locale, asset, assetCategories)}</p></div><p className="shrink-0 text-lg font-semibold text-mist">{money(Number(asset.currentValue || 0), currency, locale)}</p></div>
          <div className="mt-4 grid grid-cols-3 gap-3 border-t border-white/10 pt-4 text-xs"><SmallMetric label={text.purchase} value={money(dossier.performance.acquisitionBasis, currency, locale)} /><SmallMetric label={text.gain} value={`${dossier.performance.gainRate == null ? '—' : `${(dossier.performance.gainRate * 100).toFixed(1)}%`}`} tone={dossier.performance.grossGain >= 0 ? 'text-success' : 'text-warning'} /><SmallMetric label={text.debt} value={money(dossier.performance.linkedDebt, currency, locale)} /></div>
          <div className={`mt-4 inline-flex rounded-full px-2.5 py-1 text-xs font-medium ${status === 'current' ? 'bg-success/10 text-success' : 'bg-warning/10 text-warning'}`}>{status === 'current' ? text.currentStatus : status === 'stale' ? text.staleStatus : text.unknownStatus}</div>
        </button>
      })}</div>
      {selected && <article className="section-card self-start xl:sticky xl:top-5">
        <div className="flex flex-wrap items-start justify-between gap-4"><div><p className="eyebrow">{text.history}</p><h2 className="mt-1 text-xl font-semibold text-white">{selected.asset.name}</h2></div><span className="rounded-full bg-sky/15 px-3 py-1 text-xs font-medium text-sky">{selectedHistory.length} {text.points}</span></div>
        <dl className="mt-5 grid gap-3 rounded-2xl bg-white/5 p-4 text-sm sm:grid-cols-2"><Detail label={text.lastEstimate} value={selected.asset.valuedOn ? new Intl.DateTimeFormat(locale, { dateStyle: 'medium' }).format(new Date(`${String(selected.asset.valuedOn)}T00:00:00`)) : '—'} /><Detail label={text.source} value={String(selected.asset.valuationSource || '—')} /><Detail label={text.debt} value={money(selected.performance.linkedDebt, selected.performance.currency, locale)} /><Detail label={text.equity} value={money(selected.performance.netEquity, selected.performance.currency, locale)} /></dl>
        {selectedHistory.length ? <div className="mt-5"><AssetValuationChart history={selectedHistory} currency={selected.performance.currency} locale={locale} /></div> : <p className="mt-5 rounded-xl bg-white/5 px-4 py-5 text-sm leading-6 text-muted">{text.noHistory}</p>}
      </article>}
    </div>
  </section>
}

function Header({ locale, onEdit }: { locale: Locale; onEdit: () => void }) {
  const text = copy[locale]
  return <div className="flex flex-col justify-between gap-5 xl:flex-row xl:items-end"><div><p className="eyebrow">{text.eyebrow}</p><h1 className="mt-2 max-w-2xl text-3xl font-semibold tracking-tight text-white">{text.title}</h1><p className="mt-3 max-w-2xl text-sm leading-6 text-muted">{text.intro}</p></div><button className="ghost-button" onClick={onEdit}>{text.edit}</button></div>
}

function Summary({ label, value, symbol, tone = 'text-white' }: { label: string; value: string; symbol: string; tone?: string }) {
  return <article className="section-card"><div className="flex items-start justify-between gap-3"><div><p className="eyebrow">{label}</p><p className={`mt-3 text-2xl font-semibold ${tone}`}>{value}</p></div><span className="grid h-9 w-9 place-items-center rounded-xl bg-white/10 text-sky">{symbol}</span></div></article>
}

function SmallMetric({ label, value, tone = 'text-mist' }: { label: string; value: string; tone?: string }) {
  return <div><p className="text-muted">{label}</p><p className={`mt-1 truncate font-medium ${tone}`}>{value}</p></div>
}

function Detail({ label, value }: { label: string; value: string }) {
  return <div><dt className="text-xs text-muted">{label}</dt><dd className="mt-1 font-medium text-mist">{value}</dd></div>
}
