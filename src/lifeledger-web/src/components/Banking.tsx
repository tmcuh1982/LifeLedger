import { ChangeEvent, useEffect, useMemo, useState } from 'react'
import { api } from '../api'
import type { Locale } from '../i18n'
import type { BankAccount, BankImporterDefinition, BankSpendingAverage, BankStatementPreview, BankTransaction, BankTransactionClassification, BankTransactionReview, LedgerItem, ScenarioData } from '../types'
import { DateField } from './DateField'

interface BankingProps {
  scenarioId: string
  data: ScenarioData
  locale: Locale
  openImport: boolean
  onImportOpened: () => void
  onDataChanged: () => Promise<void>
}

const classifications: BankTransactionClassification[] = ['Expense', 'Income', 'Transfer', 'Investment', 'AssetExpense', 'Ignored', 'Uncategorized']
const categories = ['housing', 'home_improvement', 'food', 'fuel', 'insurance', 'transport', 'leisure', 'health', 'taxes', 'subscriptions', 'travel', 'exceptional', 'income', 'other']
const tr = (locale: Locale, english: string, french: string) => locale === 'fr' ? french : english
const today = () => new Date().toISOString().slice(0, 10)

type TransactionEditor = {
  item: BankTransaction
  classification: BankTransactionClassification
  category: string
  isExcludedFromSpendingAnalysis: boolean
  linkedAssetId: string
  linkedInvestmentPlanId: string
  updateAssetValue: boolean
  newLinkedAssetValue: string
  assetValuedOn: string
}

function money(amount: number, currency: string, locale: Locale) {
  return new Intl.NumberFormat(locale, { style: 'currency', currency }).format(amount)
}

function classificationLabel(value: BankTransactionClassification, locale: Locale) {
  const labels: Record<BankTransactionClassification, Record<Locale, string>> = {
    Expense: { en: 'Expense', fr: 'Dépense', pl: 'Wydatek', de: 'Ausgabe', nl: 'Uitgave' },
    Income: { en: 'Income', fr: 'Revenu', pl: 'Dochód', de: 'Einnahme', nl: 'Inkomen' },
    Transfer: { en: 'Transfer between accounts', fr: 'Virement entre comptes', pl: 'Przelew między kontami', de: 'Umbuchung zwischen Konten', nl: 'Overboeking tussen rekeningen' },
    Investment: { en: 'Investment', fr: 'Investissement', pl: 'Inwestycja', de: 'Investition', nl: 'Investering' },
    AssetExpense: { en: 'Cost related to an asset', fr: 'Coût lié à un bien', pl: 'Koszt związany z majątkiem', de: 'Kosten eines Vermögenswerts', nl: 'Kosten van een bezit' },
    Ignored: { en: 'Ignore', fr: 'Ignorer', pl: 'Ignoruj', de: 'Ignorieren', nl: 'Negeren' },
    Uncategorized: { en: 'To classify', fr: 'À classer', pl: 'Do sklasyfikowania', de: 'Zu kategorisieren', nl: 'Te categoriseren' },
  }
  return labels[value][locale]
}

function categoryLabel(value: string, locale: Locale) {
  const labels: Record<string, Record<Locale, string>> = {
    housing: { en: 'Housing', fr: 'Logement', pl: 'Mieszkanie', de: 'Wohnen', nl: 'Wonen' },
    home_improvement: { en: 'Renovation and major works', fr: 'Rénovation et gros travaux', pl: 'Remont i duże prace', de: 'Renovierung und größere Arbeiten', nl: 'Renovatie en grote werken' },
    food: { en: 'Food and groceries', fr: 'Alimentation', pl: 'Żywność', de: 'Lebensmittel', nl: 'Voeding' },
    fuel: { en: 'Fuel', fr: 'Carburant', pl: 'Paliwo', de: 'Kraftstoff', nl: 'Brandstof' },
    insurance: { en: 'Insurance', fr: 'Assurances', pl: 'Ubezpieczenia', de: 'Versicherungen', nl: 'Verzekeringen' },
    transport: { en: 'Transport', fr: 'Transport', pl: 'Transport', de: 'Verkehr', nl: 'Vervoer' },
    leisure: { en: 'Leisure', fr: 'Loisirs', pl: 'Wypoczynek', de: 'Freizeit', nl: 'Vrije tijd' },
    health: { en: 'Health', fr: 'Santé', pl: 'Zdrowie', de: 'Gesundheit', nl: 'Gezondheid' },
    taxes: { en: 'Taxes', fr: 'Impôts', pl: 'Podatki', de: 'Steuern', nl: 'Belastingen' },
    subscriptions: { en: 'Subscriptions', fr: 'Abonnements', pl: 'Subskrypcje', de: 'Abonnements', nl: 'Abonnementen' },
    travel: { en: 'Travel', fr: 'Voyages', pl: 'Podróże', de: 'Reisen', nl: 'Reizen' },
    exceptional: { en: 'Exceptional purchase', fr: 'Achat exceptionnel', pl: 'Wyjątkowy zakup', de: 'Außergewöhnlicher Kauf', nl: 'Uitzonderlijke aankoop' },
    income: { en: 'Income', fr: 'Revenus', pl: 'Dochody', de: 'Einnahmen', nl: 'Inkomsten' },
    other: { en: 'Other', fr: 'Autre', pl: 'Inne', de: 'Sonstiges', nl: 'Overig' },
  }
  return labels[value]?.[locale] ?? value
}

/** Renders observed banking history and an in-memory, bank-specific import review workflow. */
export function Banking({ scenarioId, data, locale, openImport, onImportOpened, onDataChanged }: BankingProps) {
  const [accounts, setAccounts] = useState<BankAccount[]>([])
  const [transactions, setTransactions] = useState<BankTransaction[]>([])
  const [spendingAverages, setSpendingAverages] = useState<BankSpendingAverage[]>([])
  const [importers, setImporters] = useState<BankImporterDefinition[]>([])
  const [importOpen, setImportOpen] = useState(false)
  const [bankKey, setBankKey] = useState('')
  const [file, setFile] = useState<File>()
  const [preview, setPreview] = useState<BankStatementPreview>()
  const [currency, setCurrency] = useState('')
  const [accountName, setAccountName] = useState('')
  const [linkedAssetId, setLinkedAssetId] = useState('')
  const [reviews, setReviews] = useState<Record<string, BankTransactionReview>>({})
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string>()
  const [message, setMessage] = useState<string>()
  const [editingTransaction, setEditingTransaction] = useState<TransactionEditor>()
  const [applyingAverage, setApplyingAverage] = useState<string>()

  async function refresh() {
    const [nextAccounts, nextTransactions, nextImporters, nextAverages] = await Promise.all([api.bankAccounts(scenarioId), api.bankTransactions(scenarioId), api.bankImporters(), api.bankSpendingAverages(scenarioId)])
    setAccounts(nextAccounts); setTransactions(nextTransactions); setImporters(nextImporters); setSpendingAverages(nextAverages)
    setBankKey((current) => current || nextImporters[0]?.key || '')
  }

  useEffect(() => { void refresh().catch((reason) => setError(reason instanceof Error ? reason.message : tr(locale, 'Unable to load bank history.', 'Impossible de charger les opérations bancaires.'))) }, [scenarioId])
  useEffect(() => { if (openImport) { setImportOpen(true); onImportOpened() } }, [openImport, onImportOpened])

  const selectedImporter = importers.find((candidate) => candidate.key === bankKey)
  const cashAssets = data.assets.filter((asset) => asset.kind === 'Cash')
  const editedAsset = data.assets.find((asset) => asset.id === editingTransaction?.linkedAssetId)
  const monthlyExpenses = useMemo(() => {
    const byCurrency = spendingAverages.reduce((groups, item) => groups.set(item.currency, (groups.get(item.currency) ?? 0) + item.averageMonthlyAmount), new Map<string, number>())
    return Array.from(byCurrency.entries()).map(([currencyCode, amount]) => money(amount, currencyCode, locale)).join(' · ')
  }, [spendingAverages, locale])

  async function applyAverage(average: BankSpendingAverage) {
    const key = `${average.category}:${average.currency}`
    setApplyingAverage(key); setError(undefined); setMessage(undefined)
    try {
      await api.applyBankSpendingAverage(scenarioId, average.category, average.currency, `${tr(locale, 'Observed spending', 'Dépenses observées')} · ${categoryLabel(average.category, locale)}`)
      setMessage(average.linkedExpenseId ? tr(locale, 'The simulation assumption was updated.', 'L’hypothèse de simulation a été mise à jour.') : tr(locale, 'This monthly average is now included in the simulation.', 'Cette moyenne mensuelle est maintenant incluse dans la simulation.'))
      await refresh(); await onDataChanged()
    } catch (reason) { setError(reason instanceof Error ? reason.message : tr(locale, 'Unable to update the simulation.', 'Impossible de mettre à jour la simulation.')) }
    finally { setApplyingAverage(undefined) }
  }

  function resetWizard() {
    setFile(undefined); setPreview(undefined); setCurrency(''); setAccountName(''); setLinkedAssetId(''); setReviews({}); setError(undefined)
  }

  async function previewFile(event?: ChangeEvent<HTMLInputElement>) {
    const nextFile = event?.target.files?.[0] ?? file
    if (!nextFile || !bankKey) return
    setFile(nextFile); setBusy(true); setError(undefined); setPreview(undefined)
    try {
      const nextPreview = await api.previewBankStatement(scenarioId, bankKey, nextFile)
      setPreview(nextPreview); setCurrency(nextPreview.detectedCurrency); setAccountName(`${nextPreview.bankName} ${nextPreview.maskedAccountIdentifier}`)
      setReviews(Object.fromEntries(nextPreview.transactions.map((item) => [item.fingerprint, { fingerprint: item.fingerprint, classification: item.suggestedClassification, category: item.suggestedCategory }])))
    } catch (reason) { setError(reason instanceof Error ? reason.message : tr(locale, 'Unable to read this statement.', 'Impossible de lire ce relevé.')) }
    finally { setBusy(false); if (event) event.target.value = '' }
  }

  function updateReview(fingerprint: string, changes: Partial<BankTransactionReview>) {
    setReviews((current) => ({ ...current, [fingerprint]: { ...current[fingerprint], ...changes } }))
  }

  function openTransactionEditor(item: BankTransaction) {
    setError(undefined)
    setEditingTransaction({ item, classification: item.classification, category: item.category, isExcludedFromSpendingAnalysis: item.isExcludedFromSpendingAnalysis, linkedAssetId: item.linkedAssetId ?? '', linkedInvestmentPlanId: item.linkedInvestmentPlanId ?? '', updateAssetValue: false, newLinkedAssetValue: '', assetValuedOn: today() })
  }

  function changeTransactionClassification(classification: BankTransactionClassification) {
    setEditingTransaction((current) => current ? {
      ...current,
      classification,
      linkedAssetId: classification === 'AssetExpense' ? current.linkedAssetId : '',
      linkedInvestmentPlanId: classification === 'Investment' ? current.linkedInvestmentPlanId : '',
      isExcludedFromSpendingAnalysis: classification === 'Expense' ? current.isExcludedFromSpendingAnalysis : true,
      updateAssetValue: classification === 'AssetExpense' && current.updateAssetValue
    } : current)
  }

  async function saveTransaction() {
    if (!editingTransaction) return
    const linkedAsset = data.assets.find((asset) => asset.id === editingTransaction.linkedAssetId)
    if (editingTransaction.updateAssetValue && (!linkedAsset || !editingTransaction.newLinkedAssetValue)) return
    setBusy(true); setError(undefined)
    try {
      await api.updateBankTransaction(editingTransaction.item.id, {
        classification: editingTransaction.classification,
        category: editingTransaction.category,
        isExcludedFromSpendingAnalysis: editingTransaction.isExcludedFromSpendingAnalysis,
        linkedAssetId: editingTransaction.classification === 'AssetExpense' ? editingTransaction.linkedAssetId || undefined : undefined,
        linkedInvestmentPlanId: editingTransaction.classification === 'Investment' ? editingTransaction.linkedInvestmentPlanId || undefined : undefined,
        newLinkedAssetValue: editingTransaction.updateAssetValue ? Number(editingTransaction.newLinkedAssetValue) : undefined,
        assetValuedOn: editingTransaction.updateAssetValue ? editingTransaction.assetValuedOn : undefined
      })
      setMessage(tr(locale, 'The operation was updated.', 'L’opération a été mise à jour.'))
      setEditingTransaction(undefined)
      await refresh()
      if (editingTransaction.updateAssetValue) await onDataChanged()
    } catch (reason) { setError(reason instanceof Error ? reason.message : tr(locale, 'Unable to update this operation.', 'Impossible de modifier cette opération.')) }
    finally { setBusy(false) }
  }

  async function commit() {
    if (!file || !preview || currency !== preview.detectedCurrency) return
    setBusy(true); setError(undefined)
    try {
      const result = await api.commitBankStatement(scenarioId, bankKey, accountName, currency, linkedAssetId || undefined, Object.values(reviews), file)
      setMessage(tr(locale, `${result.importedTransactions} operations imported; ${result.skippedDuplicates} duplicates skipped.`, `${result.importedTransactions} opérations importées ; ${result.skippedDuplicates} doublons ignorés.`))
      setImportOpen(false); resetWizard(); await refresh()
    } catch (reason) { setError(reason instanceof Error ? reason.message : tr(locale, 'Unable to import this statement.', 'Impossible d’importer ce relevé.')) }
    finally { setBusy(false) }
  }

  return <section className="space-y-5">
    <div className="flex flex-col justify-between gap-5 xl:flex-row xl:items-end"><div><p className="eyebrow">{tr(locale, 'Observed history', 'Historique observé')}</p><h1 className="mt-2 text-3xl font-semibold text-white">{tr(locale, 'Bank operations', 'Opérations bancaires')}</h1><p className="mt-2 max-w-2xl text-sm leading-6 text-muted">{tr(locale, 'Import statements by bank, classify real spending, and link operations to your assets. This history never changes your forecast automatically.', 'Importez les relevés selon la banque, classez les dépenses réelles et reliez les opérations à vos biens. Cet historique ne modifie jamais automatiquement vos prévisions.')}</p></div><button className="primary-button" onClick={() => { resetWizard(); setImportOpen(true) }}>{tr(locale, 'Import a statement', 'Importer un relevé')}</button></div>
    {message && <p className="rounded-xl border border-success/20 bg-success/10 px-4 py-3 text-sm text-success">{message}</p>}
    {error && !importOpen && <p className="rounded-xl border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">{error}</p>}
    <div className="grid gap-4 sm:grid-cols-3"><Summary label={tr(locale, 'Accounts', 'Comptes')} value={String(accounts.length)} /><Summary label={tr(locale, 'Imported operations', 'Opérations importées')} value={String(transactions.length)} /><Summary label={tr(locale, 'Observed monthly spending', 'Dépenses mensuelles observées')} value={monthlyExpenses || '—'} /></div>
    <article className="section-card">
      <div><p className="eyebrow">{tr(locale, 'Monthly averages', 'Moyennes mensuelles')}</p><h2 className="mt-1 text-lg font-semibold text-white">{tr(locale, 'Estimated from your bank history', 'Estimées depuis vos relevés')}</h2><p className="mt-2 max-w-3xl text-sm leading-6 text-muted">{tr(locale, 'Each category is averaged over every month covered by the imported statements, including months with no operation. Excluded operations and costs linked to an asset are not counted.', 'Chaque catégorie est moyennée sur tous les mois couverts par les relevés, y compris les mois sans opération. Les opérations exclues et les coûts liés à un bien ne sont pas comptés.')}</p></div>
      <div className="mt-5 grid gap-3 lg:grid-cols-2">{spendingAverages.map((average) => { const key = `${average.category}:${average.currency}`; return <div className="rounded-2xl border border-white/10 bg-white/5 p-4" key={key}><div className="flex flex-col justify-between gap-4 sm:flex-row sm:items-start"><div><div className="flex flex-wrap items-center gap-2"><p className="font-medium text-mist">{categoryLabel(average.category, locale)}</p>{average.linkedExpenseId && <span className="rounded-full bg-success/10 px-2 py-0.5 text-xs text-success">{tr(locale, 'In the simulation', 'Dans la simulation')}</span>}</div><p className="mt-2 text-2xl font-semibold text-white">{money(average.averageMonthlyAmount, average.currency, locale)}<span className="ml-1 text-sm font-normal text-muted">/{tr(locale, 'month', 'mois')}</span></p><p className="mt-2 text-xs leading-5 text-muted">{average.includedTransactions} {tr(locale, 'operation(s)', 'opération(s)')} · {average.observedMonths} {tr(locale, 'observed month(s)', 'mois observé(s)')} · {average.periodStartsOn} → {average.periodEndsOn}</p>{average.observedMonths < 3 && <p className="mt-2 text-xs text-warning">{tr(locale, 'Limited history: this average will become more reliable after 3 months.', 'Historique limité : cette moyenne sera plus fiable après 3 mois.')}</p>}</div><button className={average.linkedExpenseId ? 'ghost-button shrink-0' : 'primary-button shrink-0'} disabled={Boolean(applyingAverage)} onClick={() => void applyAverage(average)}>{applyingAverage === key ? tr(locale, 'Updating…', 'Mise à jour…') : average.linkedExpenseId ? tr(locale, 'Update simulation', 'Mettre à jour') : tr(locale, 'Use in simulation', 'Utiliser dans la simulation')}</button></div></div> })}{!spendingAverages.length && <p className="text-sm text-muted">{tr(locale, 'Classify imported expenses to reveal monthly averages here.', 'Classez les dépenses importées pour afficher ici leurs moyennes mensuelles.')}</p>}</div>
    </article>
    <article className="section-card"><div className="flex items-start justify-between gap-4"><div><p className="eyebrow">{tr(locale, 'Your accounts', 'Vos comptes')}</p><h2 className="mt-1 text-lg font-semibold text-white">{tr(locale, 'Locally registered', 'Enregistrés localement')}</h2></div><span className="rounded-full bg-sky/15 px-3 py-1 text-xs text-sky">{accounts.length}</span></div><div className="mt-4 grid gap-3 sm:grid-cols-2">{accounts.map((account) => <div className="rounded-2xl border border-white/10 bg-white/5 p-4" key={account.id}><div className="flex justify-between gap-3"><div><p className="font-medium text-mist">{account.name}</p><p className="mt-1 text-xs text-muted">{account.maskedIdentifier}</p></div><span className="h-fit rounded-lg bg-white/10 px-2 py-1 text-xs text-sky">{account.currency}</span></div><p className="mt-4 text-xs text-muted">{account.imports} {tr(locale, 'statement(s)', 'relevé(s)')} · {account.transactions} {tr(locale, 'operation(s)', 'opération(s)')}</p></div>)}{!accounts.length && <p className="text-sm text-muted">{tr(locale, 'No bank account imported yet.', 'Aucun compte bancaire importé pour le moment.')}</p>}</div></article>
    <article className="section-card"><p className="eyebrow">{tr(locale, 'Recent operations', 'Opérations récentes')}</p><div className="mt-4 divide-y divide-white/10">{transactions.slice(0, 100).map((item) => <div className="flex flex-col gap-3 py-3 sm:flex-row sm:items-center sm:justify-between" key={item.id}><div className="min-w-0"><p className="truncate text-sm font-medium text-mist">{item.description}</p><div className="mt-1 flex flex-wrap items-center gap-2 text-xs text-muted"><span>{new Intl.DateTimeFormat(locale, { dateStyle: 'medium' }).format(new Date(`${item.bookedOn}T00:00:00`))} · {classificationLabel(item.classification, locale)} · {categoryLabel(item.category, locale)}</span>{(item.isExcludedFromSpendingAnalysis || item.classification !== 'Expense') && <span className="rounded-full bg-white/10 px-2 py-0.5 text-sky">{tr(locale, 'Not counted in monthly spending', 'Non comptée dans les dépenses mensuelles')}</span>}</div></div><div className="flex shrink-0 items-center justify-between gap-4"><p className={`font-semibold ${item.amount < 0 ? 'text-warning' : 'text-success'}`}>{money(item.amount, item.currency, locale)}</p><button className="ghost-button" onClick={() => openTransactionEditor(item)}>{tr(locale, 'Edit', 'Modifier')}</button></div></div>)}{!transactions.length && <p className="py-5 text-sm text-muted">{tr(locale, 'Imported operations will appear here after your review.', 'Les opérations importées apparaîtront ici après votre validation.')}</p>}</div></article>
    {editingTransaction && <div className="fixed inset-0 z-30 overflow-y-auto bg-inkDeep/80 p-4 backdrop-blur-sm"><section aria-modal="true" className="modal-surface mx-auto my-6 w-full max-w-2xl rounded-3xl p-6" role="dialog"><div className="flex items-start justify-between gap-4"><div><p className="eyebrow">{tr(locale, 'Imported operation', 'Opération importée')}</p><h2 className="mt-2 text-xl font-semibold text-white">{tr(locale, 'Edit its financial meaning', 'Modifier son interprétation financière')}</h2></div><button className="text-muted hover:text-white" onClick={() => setEditingTransaction(undefined)}>✕</button></div>
      <div className="mt-5 rounded-2xl border border-white/10 bg-white/5 p-4"><div className="flex flex-col justify-between gap-3 sm:flex-row"><div className="min-w-0"><p className="text-sm font-medium text-mist">{editingTransaction.item.description}</p><p className="mt-1 text-xs text-muted">{new Intl.DateTimeFormat(locale, { dateStyle: 'long' }).format(new Date(`${editingTransaction.item.bookedOn}T00:00:00`))}</p></div><p className={`shrink-0 text-lg font-semibold ${editingTransaction.item.amount < 0 ? 'text-warning' : 'text-success'}`}>{money(editingTransaction.item.amount, editingTransaction.item.currency, locale)}</p></div></div>
      {error && <p className="mt-4 rounded-xl border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">{error}</p>}
      <div className="mt-5 space-y-4"><div className="grid gap-4 sm:grid-cols-2"><label className="text-sm text-mist">{tr(locale, 'What is this operation?', 'Quelle est la nature de cette opération ?')}<select className="field mt-2" value={editingTransaction.classification} onChange={(event) => changeTransactionClassification(event.target.value as BankTransactionClassification)}>{classifications.map((value) => <option className="bg-panel" key={value} value={value}>{classificationLabel(value, locale)}</option>)}</select></label><label className="text-sm text-mist">{tr(locale, 'Category', 'Catégorie')}<select className="field mt-2" value={editingTransaction.category} onChange={(event) => setEditingTransaction({ ...editingTransaction, category: event.target.value })}>{categories.map((value) => <option className="bg-panel" key={value} value={value}>{categoryLabel(value, locale)}</option>)}</select></label></div>
        {editingTransaction.classification === 'Expense' && <label className="flex items-start gap-3 rounded-xl border border-white/15 bg-white/5 px-3 py-3 text-sm text-mist"><input className="mt-1" checked={editingTransaction.isExcludedFromSpendingAnalysis} type="checkbox" onChange={(event) => setEditingTransaction({ ...editingTransaction, isExcludedFromSpendingAnalysis: event.target.checked })} /><span><span className="block">{tr(locale, 'Do not count in my monthly spending estimate', 'Ne pas compter dans mon estimation de dépenses mensuelles')}</span><span className="mt-1 block text-xs leading-5 text-muted">{tr(locale, 'Useful for a large one-off purchase that should remain visible in history.', 'Utile pour un achat exceptionnel qui doit rester visible dans l’historique.')}</span></span></label>}
        {editingTransaction.classification === 'AssetExpense' && <section className="rounded-2xl border border-sky/20 bg-sky/10 p-4"><p className="text-sm font-medium text-sky">{tr(locale, 'Work or cost related to an asset', 'Travaux ou coût lié à un bien')}</p><p className="mt-1 text-xs leading-5 text-muted">{tr(locale, 'This operation is automatically excluded from monthly living costs.', 'Cette opération est automatiquement exclue du coût de vie mensuel.')}</p><label className="mt-4 block text-sm text-mist">{tr(locale, 'Affected asset', 'Bien concerné')}<select className="field mt-2" value={editingTransaction.linkedAssetId} onChange={(event) => setEditingTransaction({ ...editingTransaction, linkedAssetId: event.target.value, updateAssetValue: false, newLinkedAssetValue: '' })}><option className="bg-panel" value="">—</option>{data.assets.map((asset) => <option className="bg-panel" key={asset.id} value={asset.id}>{asset.name}</option>)}</select></label>{editedAsset && <><div className="mt-4 rounded-xl bg-white/5 px-4 py-3 text-sm text-mist"><span className="text-muted">{tr(locale, 'Current recorded value', 'Valeur actuellement enregistrée')} · </span>{money(Number(editedAsset.currentValue ?? 0), String(editedAsset.currency ?? 'EUR'), locale)}</div><label className="mt-4 flex items-start gap-3 text-sm text-mist"><input className="mt-1" checked={editingTransaction.updateAssetValue} type="checkbox" onChange={(event) => setEditingTransaction({ ...editingTransaction, updateAssetValue: event.target.checked, newLinkedAssetValue: event.target.checked ? String(editedAsset.currentValue ?? '') : '' })} /><span><span className="block">{tr(locale, 'Record a new estimated value for this asset', 'Enregistrer une nouvelle valeur estimée pour ce bien')}</span><span className="mt-1 block text-xs leading-5 text-muted">{tr(locale, 'Enter the complete new value. The work cost is not added automatically because cost and value created may differ.', 'Saisissez la nouvelle valeur totale. Le coût des travaux n’est pas ajouté automatiquement car le coût et la valeur créée peuvent être différents.')}</span></span></label>{editingTransaction.updateAssetValue && <div className="mt-4 grid gap-4 sm:grid-cols-2"><label className="text-sm text-mist">{tr(locale, 'New total estimated value', 'Nouvelle valeur totale estimée')} ({String(editedAsset.currency ?? 'EUR')})<input className="field mt-2" min="0" required type="number" value={editingTransaction.newLinkedAssetValue} onChange={(event) => setEditingTransaction({ ...editingTransaction, newLinkedAssetValue: event.target.value })} /></label><DateField label={tr(locale, 'Valuation date', 'Date de l’estimation')} locale={locale} required value={editingTransaction.assetValuedOn} onChange={(value) => setEditingTransaction({ ...editingTransaction, assetValuedOn: value })} /></div>}</>}</section>}
        {editingTransaction.classification === 'Investment' && <LinkSelect label={tr(locale, 'Investment plan', 'Plan d’investissement')} items={data.investments} value={editingTransaction.linkedInvestmentPlanId} onChange={(value) => setEditingTransaction({ ...editingTransaction, linkedInvestmentPlanId: value ?? '' })} />}
      </div><div className="mt-7 flex justify-end gap-3"><button className="ghost-button" onClick={() => setEditingTransaction(undefined)}>{tr(locale, 'Cancel', 'Annuler')}</button><button className="primary-button" disabled={busy || (editingTransaction.updateAssetValue && !editingTransaction.newLinkedAssetValue)} onClick={() => void saveTransaction()}>{busy ? tr(locale, 'Saving…', 'Enregistrement…') : tr(locale, 'Save changes', 'Enregistrer les modifications')}</button></div>
    </section></div>}
    {importOpen && <div className="fixed inset-0 z-30 overflow-y-auto bg-inkDeep/80 p-4 backdrop-blur-sm"><section aria-modal="true" className="modal-surface mx-auto my-6 w-full max-w-5xl rounded-3xl p-6" role="dialog"><div className="flex items-start justify-between gap-4"><div><p className="eyebrow">{tr(locale, 'Local bank import', 'Import bancaire local')}</p><h2 className="mt-2 text-xl font-semibold text-white">{preview ? tr(locale, 'Review every operation', 'Vérifiez chaque opération') : tr(locale, 'Choose the bank and statement', 'Choisissez la banque et le relevé')}</h2></div><button className="text-muted hover:text-white" onClick={() => { setImportOpen(false); resetWizard() }}>✕</button></div>
      {error && <p className="mt-4 rounded-xl border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger">{error}</p>}
      {!preview ? <div className="mt-6 space-y-4"><label className="block text-sm text-mist">{tr(locale, 'Bank template', 'Modèle de banque')}<select className="field mt-2" value={bankKey} onChange={(event) => { setBankKey(event.target.value); setFile(undefined) }}>{importers.map((entry) => <option className="bg-panel" key={entry.key} value={entry.key}>{entry.bankName} · v{entry.version}</option>)}</select></label><div className="rounded-2xl border border-white/10 bg-white/5 p-4"><p className="text-sm font-medium text-mist">{selectedImporter ? selectedImporter.acceptedExtensions.join(', ').toUpperCase() : '—'}</p><p className="mt-1 text-xs leading-5 text-muted">{tr(locale, 'The document is read in memory by your own server. The original file is not retained.', 'Le document est lu en mémoire par votre propre serveur. Le fichier original n’est pas conservé.')}</p><label className="ghost-button mt-4 inline-flex cursor-pointer">{busy ? tr(locale, 'Reading…', 'Lecture…') : tr(locale, 'Choose the statement', 'Choisir le relevé')}<input accept={selectedImporter?.acceptedExtensions.join(',')} className="sr-only" disabled={busy} type="file" onChange={(event) => void previewFile(event)} /></label></div></div> : <div className="mt-6 space-y-5"><div className="grid gap-4 rounded-2xl border border-sky/20 bg-sky/10 p-4 sm:grid-cols-2 xl:grid-cols-4"><Info label={tr(locale, 'Bank', 'Banque')} value={preview.bankName} /><Info label={tr(locale, 'Account', 'Compte')} value={preview.maskedAccountIdentifier} /><Info label={tr(locale, 'Detected currency', 'Devise détectée')} value={preview.detectedCurrency} /><Info label={tr(locale, 'Operations', 'Opérations')} value={String(preview.transactions.length)} /></div><div className="grid gap-4 sm:grid-cols-3"><label className="block text-sm text-mist">{tr(locale, 'Account name', 'Nom du compte')}<input className="field mt-2" value={accountName} onChange={(event) => setAccountName(event.target.value)} /></label><label className="block text-sm text-mist">{tr(locale, 'Confirm account currency', 'Confirmer la devise du compte')}<select className="field mt-2" value={currency} onChange={(event) => setCurrency(event.target.value)}><option className="bg-panel" value={preview.detectedCurrency}>{preview.detectedCurrency}</option></select><span className="mt-1 block text-xs text-muted">{tr(locale, 'No automatic EUR fallback.', 'Aucun remplacement automatique par EUR.')}</span></label><label className="block text-sm text-mist">{tr(locale, 'Cash asset represented', 'Actif de trésorerie représenté')}<select className="field mt-2" value={linkedAssetId} onChange={(event) => setLinkedAssetId(event.target.value)}><option className="bg-panel" value="">{tr(locale, 'No linked asset', 'Aucun actif lié')}</option>{cashAssets.map((asset) => <option className="bg-panel" key={asset.id} value={asset.id}>{asset.name}</option>)}</select></label></div><div className="space-y-3">{preview.transactions.map((item) => { const review = reviews[item.fingerprint]; return <article className={`rounded-2xl border p-4 ${item.alreadyImported ? 'border-white/5 bg-white/[0.02] opacity-60' : 'border-white/10 bg-white/5'}`} key={item.fingerprint}><div className="flex flex-col justify-between gap-3 sm:flex-row"><div className="min-w-0"><p className="text-sm font-medium text-mist">{item.description}</p><p className="mt-1 text-xs text-muted">{item.bookedOn}{item.counterparty ? ` · ${item.counterparty}` : ''}{item.alreadyImported ? ` · ${tr(locale, 'Already imported', 'Déjà importée')}` : ''}</p></div><p className={`shrink-0 font-semibold ${item.amount < 0 ? 'text-warning' : 'text-success'}`}>{money(item.amount, item.currency, locale)}</p></div>{!item.alreadyImported && review && <div className="mt-4 grid gap-3 border-t border-white/10 pt-4 sm:grid-cols-3"><label className="text-xs text-muted">{tr(locale, 'Meaning', 'Nature')}<select className="field mt-2" value={review.classification} onChange={(event) => updateReview(item.fingerprint, { classification: event.target.value as BankTransactionClassification, linkedAssetId: undefined, linkedInvestmentPlanId: undefined })}>{classifications.map((value) => <option className="bg-panel" key={value} value={value}>{classificationLabel(value, locale)}</option>)}</select></label><label className="text-xs text-muted">{tr(locale, 'Category', 'Catégorie')}<select className="field mt-2" value={review.category} onChange={(event) => updateReview(item.fingerprint, { category: event.target.value })}>{categories.map((value) => <option className="bg-panel" key={value} value={value}>{categoryLabel(value, locale)}</option>)}</select></label>{review.classification === 'AssetExpense' ? <LinkSelect label={tr(locale, 'Related asset', 'Bien concerné')} items={data.assets} value={review.linkedAssetId} onChange={(value) => updateReview(item.fingerprint, { linkedAssetId: value })} /> : review.classification === 'Investment' ? <LinkSelect label={tr(locale, 'Investment plan', 'Plan d’investissement')} items={data.investments} value={review.linkedInvestmentPlanId} onChange={(value) => updateReview(item.fingerprint, { linkedInvestmentPlanId: value })} /> : <div />}</div>}</article> })}</div><div className="sticky bottom-0 flex flex-wrap justify-between gap-3 rounded-2xl border border-white/10 bg-panel p-4"><button className="ghost-button" onClick={() => { setPreview(undefined); setError(undefined) }}>{tr(locale, 'Change file', 'Changer de fichier')}</button><button className="primary-button" disabled={busy || !accountName.trim() || currency !== preview.detectedCurrency} onClick={() => void commit()}>{busy ? tr(locale, 'Importing…', 'Import…') : tr(locale, 'Confirm import', 'Confirmer l’import')}</button></div></div>}
    </section></div>}
  </section>
}

function Summary({ label, value }: { label: string; value: string }) { return <article className="section-card"><p className="eyebrow">{label}</p><p className="mt-3 text-2xl font-semibold text-white">{value}</p></article> }
function Info({ label, value }: { label: string; value: string }) { return <div><p className="text-xs text-muted">{label}</p><p className="mt-1 text-sm font-semibold text-mist">{value}</p></div> }
function LinkSelect({ label, items, value, onChange }: { label: string; items: LedgerItem[]; value?: string; onChange: (value?: string) => void }) { return <label className="text-xs text-muted">{label}<select className="field mt-2" value={value ?? ''} onChange={(event) => onChange(event.target.value || undefined)}><option className="bg-panel" value="">—</option>{items.map((item) => <option className="bg-panel" key={item.id} value={item.id}>{item.name}</option>)}</select></label> }
