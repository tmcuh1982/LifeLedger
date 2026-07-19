import type { AssetProfileDefinition, AssetProfileFieldDefinition, LedgerItem } from '../types'
import type { Locale } from '../i18n'
import { DateField } from './DateField'

type Draft = Record<string, string | boolean>
type CurrencyRate = { code: string; unitsPerEuro: number }

interface AssetDossierEditorProps {
  draft: Draft
  definitions: AssetProfileDefinition[]
  liabilities: LedgerItem[]
  rates: CurrencyRate[]
  locale: Locale
  onField: (name: string, value: string | boolean) => void
}

const labels = {
  acquisition: { en: 'Purchase and current estimate', fr: 'Achat et estimation actuelle', pl: 'Zakup i bieżąca wycena', de: 'Kauf und aktuelle Schätzung', nl: 'Aankoop en huidige schatting' },
  purchasePrice: { en: 'Purchase price', fr: 'Prix d’achat', pl: 'Cena zakupu', de: 'Kaufpreis', nl: 'Aankoopprijs' },
  ownership: { en: 'Share of the asset you own (%)', fr: 'Part du bien qui vous appartient (%)', pl: 'Twoja część aktywa (%)', de: 'Ihr Eigentumsanteil (%)', nl: 'Uw eigendomsaandeel (%)' },
  ownershipHelp: { en: 'The values entered are for the whole asset. LifeLedger counts only your share in your wealth.', fr: 'Les valeurs saisies concernent le bien complet. LifeLedger compte uniquement votre part dans votre patrimoine.', pl: 'Wartości dotyczą całego aktywa. LifeLedger uwzględnia tylko Twoją część.', de: 'Die Werte gelten für den gesamten Vermögenswert. LifeLedger berücksichtigt nur Ihren Anteil.', nl: 'De waarden gelden voor het volledige bezit. LifeLedger telt alleen uw aandeel.' },
  costs: { en: 'Purchase costs and taxes', fr: 'Frais et taxes d’achat', pl: 'Koszty i podatki zakupu', de: 'Kaufnebenkosten und Steuern', nl: 'Aankoopkosten en belastingen' },
  purchasedOn: { en: 'Purchase date', fr: 'Date d’achat', pl: 'Data zakupu', de: 'Kaufdatum', nl: 'Aankoopdatum' },
  valuedOn: { en: 'Estimate date', fr: 'Date de l’estimation', pl: 'Data wyceny', de: 'Bewertungsdatum', nl: 'Taxatiedatum' },
  source: { en: 'Where does this estimate come from?', fr: 'D’où vient cette estimation ?', pl: 'Skąd pochodzi ta wycena?', de: 'Woher stammt diese Schätzung?', nl: 'Waar komt deze schatting vandaan?' },
  sourcePlaceholder: { en: 'Example: estate agent, invoice, personal estimate', fr: 'Ex. agence immobilière, facture, estimation personnelle', pl: 'Np. agent, faktura, własna wycena', de: 'Z. B. Makler, Rechnung, eigene Schätzung', nl: 'Bijv. makelaar, factuur, eigen schatting' },
  characteristics: { en: 'Detailed characteristics', fr: 'Caractéristiques détaillées', pl: 'Szczegółowe cechy', de: 'Detaillierte Merkmale', nl: 'Gedetailleerde kenmerken' },
  sheet: { en: 'Type of detailed sheet', fr: 'Type de fiche détaillée', pl: 'Typ karty szczegółowej', de: 'Art des Detailblatts', nl: 'Type detailfiche' },
  noSheet: { en: 'No detailed sheet', fr: 'Aucune fiche détaillée', pl: 'Brak karty szczegółowej', de: 'Kein Detailblatt', nl: 'Geen detailfiche' },
  debts: { en: 'Debts linked to this asset', fr: 'Dettes liées à cet actif', pl: 'Długi powiązane z tym aktywem', de: 'Mit diesem Vermögenswert verbundene Schulden', nl: 'Schulden gekoppeld aan dit bezit' },
  debtHelp: { en: 'Enter the share of each debt that finances this asset. Leave empty if it is unrelated.', fr: 'Indiquez la part de chaque dette qui finance cet actif. Laissez vide si elle n’est pas liée.', pl: 'Podaj część każdego długu finansującą ten składnik. Pozostaw puste, jeśli nie dotyczy.', de: 'Geben Sie den Anteil jeder Schuld an, der diesen Vermögenswert finanziert.', nl: 'Vul het aandeel van elke schuld in dat dit bezit financiert.' },
  linkedShare: { en: 'Share of this debt financing the asset (%)', fr: 'Part de ce crédit finançant ce bien (%)', pl: 'Część długu finansująca aktywo (%)', de: 'Anteil dieser Schuld zur Finanzierung (%)', nl: 'Deel van deze schuld voor dit bezit (%)' },
  performance: { en: 'Current result', fr: 'Résultat actuel', pl: 'Bieżący wynik', de: 'Aktuelles Ergebnis', nl: 'Huidig resultaat' },
  basis: { en: 'Total purchase cost', fr: 'Coût total d’achat', pl: 'Całkowity koszt zakupu', de: 'Gesamte Anschaffungskosten', nl: 'Totale aankoopkosten' },
  gain: { en: 'Gain or loss', fr: 'Gain ou perte', pl: 'Zysk lub strata', de: 'Gewinn oder Verlust', nl: 'Winst of verlies' },
  linkedDebt: { en: 'Debt linked today', fr: 'Dette liée aujourd’hui', pl: 'Powiązany dług dzisiaj', de: 'Heute verbundene Schuld', nl: 'Vandaag gekoppelde schuld' },
  equity: { en: 'Value after linked debt', fr: 'Valeur après dette liée', pl: 'Wartość po odjęciu długu', de: 'Wert nach verbundener Schuld', nl: 'Waarde na gekoppelde schuld' },
} as const

function t(locale: Locale, key: keyof typeof labels) { return labels[key][locale] }
function translated(values: Record<string, string>, locale: Locale) { return values[locale] ?? values.en ?? Object.values(values)[0] ?? '' }
function number(value: string | boolean | undefined) { return Number(value || 0) }
function money(value: number, currency: string, locale: Locale) { return new Intl.NumberFormat(locale, { style: 'currency', currency, maximumFractionDigits: 0 }).format(value) }

/** Converts through the same locally cached units-per-euro convention as the planner. */
function convert(value: number, from: string, to: string, rates: CurrencyRate[]) {
  if (from === to) return value
  const fromRate = rates.find((rate) => rate.code === from)?.unitsPerEuro
  const toRate = rates.find((rate) => rate.code === to)?.unitsPerEuro
  return fromRate && toRate ? value / fromRate * toRate : undefined
}

/** Renders the schema-driven characteristic sheet and common financial dossier fields. */
export function AssetDossierEditor({ draft, definitions, liabilities, rates, locale, onField }: AssetDossierEditorProps) {
  const currency = String(draft.currency || 'EUR')
  const definition = definitions.find((candidate) => candidate.key === draft.profileDefinitionKey)
  const ownershipRate = number(draft.ownershipRate) / 100
  const basis = (number(draft.purchasePrice) + number(draft.acquisitionCosts)) * ownershipRate
  const ownedValue = number(draft.currentValue) * ownershipRate
  const gain = ownedValue - basis
  const linkedAmounts = liabilities.map((liability) => {
    const allocation = number(draft[`liability:${liability.id}`]) / 100
    const responsibilityRate = Number(liability.responsibilityRate ?? 1)
    return allocation > 0 ? convert(Number(liability.outstandingBalance || 0) * responsibilityRate * allocation, String(liability.currency || currency), currency, rates) : 0
  })
  const hasUnknownConversion = linkedAmounts.some((amount) => amount === undefined)
  const linkedDebt = linkedAmounts.reduce<number>((sum, amount) => sum + (amount ?? 0), 0)

  return <div className="space-y-5">
    <section className="rounded-2xl border border-white/10 bg-white/[0.035] p-4">
      <p className="text-sm font-semibold text-white">{t(locale, 'acquisition')}</p>
      <div className="mt-4 grid gap-4 sm:grid-cols-2">
        <Field label={`${t(locale, 'purchasePrice')} (${currency})`} name="purchasePrice" type="number" value={draft.purchasePrice} onField={onField} />
        <Field label={`${t(locale, 'costs')} (${currency})`} name="acquisitionCosts" type="number" value={draft.acquisitionCosts} onField={onField} />
        <Field label={t(locale, 'ownership')} name="ownershipRate" type="number" value={draft.ownershipRate} onField={onField} />
        <DateField label={t(locale, 'purchasedOn')} locale={locale} value={String(draft.purchasedOn ?? '')} onChange={(value) => onField('purchasedOn', value)} />
        <DateField label={t(locale, 'valuedOn')} locale={locale} value={String(draft.valuedOn ?? '')} onChange={(value) => onField('valuedOn', value)} />
      </div>
      <p className="mt-3 rounded-xl border border-sky/20 bg-sky/10 px-3 py-2 text-xs leading-5 text-sky">{t(locale, 'ownershipHelp')}</p>
      <label className="mt-4 block text-sm text-mist">{t(locale, 'source')}<input className="field mt-2" value={String(draft.valuationSource || '')} placeholder={t(locale, 'sourcePlaceholder')} onChange={(event) => onField('valuationSource', event.target.value)} /></label>
    </section>

    <section className="rounded-2xl border border-white/10 bg-white/[0.035] p-4">
      <p className="text-sm font-semibold text-white">{t(locale, 'characteristics')}</p>
      <label className="mt-4 block text-sm text-mist">{t(locale, 'sheet')}<select className="field mt-2" value={String(draft.profileDefinitionKey || '')} onChange={(event) => onField('profileDefinitionKey', event.target.value)}><option className="bg-panel" value="">{t(locale, 'noSheet')}</option>{definitions.map((candidate) => <option className="bg-panel" key={candidate.key} value={candidate.key}>{translated(candidate.labels, locale)}</option>)}</select></label>
      {definition && <div className="mt-4 grid gap-4 sm:grid-cols-2">{definition.fields.map((field) => <DynamicField key={field.key} field={field} locale={locale} value={draft[`profile:${field.key}`]} onField={onField} />)}</div>}
    </section>

    {liabilities.length > 0 && <section className="rounded-2xl border border-white/10 bg-white/[0.035] p-4">
      <p className="text-sm font-semibold text-white">{t(locale, 'debts')}</p>
      <p className="mt-1 text-xs leading-5 text-muted">{t(locale, 'debtHelp')}</p>
      <div className="mt-4 space-y-3">{liabilities.map((liability) => <div className="grid items-end gap-3 rounded-xl bg-white/5 p-3 sm:grid-cols-[1fr_160px]" key={liability.id}><div><p className="text-sm font-medium text-mist">{liability.name}</p><p className="mt-1 text-xs text-muted">{money(Number(liability.outstandingBalance || 0), String(liability.currency || currency), locale)}</p></div><Field label={t(locale, 'linkedShare')} name={`liability:${liability.id}`} type="number" value={draft[`liability:${liability.id}`]} onField={onField} /></div>)}</div>
    </section>}

    <section className="rounded-2xl border border-sky/20 bg-sky/10 p-4">
      <p className="text-sm font-semibold text-sky">{t(locale, 'performance')}</p>
      <dl className="mt-3 grid gap-3 text-sm sm:grid-cols-2">
        <Metric label={t(locale, 'basis')} value={money(basis, currency, locale)} />
        <Metric label={t(locale, 'gain')} value={`${money(gain, currency, locale)}${basis > 0 ? ` · ${(gain / basis * 100).toFixed(1)}%` : ''}`} tone={gain >= 0 ? 'text-success' : 'text-warning'} />
        <Metric label={t(locale, 'linkedDebt')} value={hasUnknownConversion ? '—' : money(linkedDebt, currency, locale)} />
        <Metric label={t(locale, 'equity')} value={hasUnknownConversion ? '—' : money(ownedValue - linkedDebt, currency, locale)} />
      </dl>
    </section>
  </div>
}

function Field({ label, name, type, value, onField }: { label: string; name: string; type: string; value?: string | boolean; onField: (name: string, value: string | boolean) => void }) {
  return <label className="block text-sm text-mist">{label}<input className="field mt-2" max={name === 'ownershipRate' ? 100 : undefined} min={type === 'number' ? 0 : undefined} step={type === 'number' ? 'any' : undefined} type={type} value={String(value ?? '')} onChange={(event) => onField(name, event.target.value)} /></label>
}

function DynamicField({ field, locale, value, onField }: { field: AssetProfileFieldDefinition; locale: Locale; value?: string | boolean; onField: (name: string, value: string | boolean) => void }) {
  const name = `profile:${field.key}`
  const label = translated(field.labels, locale)
  if (field.type === 'Boolean') return <label className="flex items-center gap-3 rounded-xl border border-white/10 bg-white/5 px-3 py-3 text-sm text-mist"><input checked={Boolean(value)} type="checkbox" onChange={(event) => onField(name, event.target.checked)} />{label}</label>
  if (field.type === 'Select') return <label className="block text-sm text-mist">{label}<select className="field mt-2" required={field.required} value={String(value ?? '')} onChange={(event) => onField(name, event.target.value)}><option className="bg-panel" value="">—</option>{field.options?.map((option) => <option className="bg-panel" key={option.value} value={option.value}>{translated(option.labels, locale)}</option>)}</select></label>
  if (field.type === 'Date') return <DateField label={label} locale={locale} required={field.required} value={String(value ?? '')} onChange={(next) => onField(name, next)} />
  const numeric = ['Number', 'Area', 'Distance', 'Condition'].includes(field.type)
  return <label className="block text-sm text-mist">{label}<input className="field mt-2" max={field.type === 'Condition' ? 5 : undefined} min={numeric ? field.type === 'Condition' ? 1 : 0 : undefined} required={field.required} step={numeric ? 'any' : undefined} type={numeric ? 'number' : 'text'} value={String(value ?? '')} onChange={(event) => onField(name, event.target.value)} /></label>
}

function Metric({ label, value, tone = 'text-white' }: { label: string; value: string; tone?: string }) {
  return <div><dt className="text-xs text-muted">{label}</dt><dd className={`mt-1 font-medium ${tone}`}>{value}</dd></div>
}
