import { ChangeEvent, useState } from 'react'
import { assetKindLabel, builtInAssetKinds } from '../assetCategories'
import { api } from '../api'
import type { Locale } from '../i18n'
import type { AssetCategory, LifeLedgerExport, Profile } from '../types'

const currencies = ['EUR', 'USD', 'PLN', 'GBP', 'CHF', 'CAD', 'JPY']
const tr = (locale: Locale, english: string, french: string) => locale === 'fr' ? french : english

/** Returns the rounded planning horizon associated with a selectable reference. */
function referenceAge(reference: 'neutral' | 'female' | 'male') { return reference === 'male' ? 79 : reference === 'female' ? 84 : 82 }

/** Maps the stored optional sex value to the matching life-expectancy reference. */
function referenceFromSex(sex: Profile['sex']): 'neutral' | 'female' | 'male' { return sex === 'Male' ? 'male' : sex === 'Female' ? 'female' : 'neutral' }

/** Selects the visible reference without overwriting a person's explicitly customised age. */
function referenceFor(lifespan = 82, sex: Profile['sex'] = 'Neutral'): 'neutral' | 'female' | 'male' | 'custom' {
  const reference = referenceFromSex(sex)
  return lifespan === referenceAge(reference) ? reference : 'custom'
}

/** Calculates age today with the birthday boundary respected. */
function ageOnToday(birthDate: string): number | undefined {
  if (!birthDate) return undefined
  const birth = new Date(`${birthDate}T00:00:00`)
  if (Number.isNaN(birth.getTime())) return undefined
  const today = new Date()
  let age = today.getFullYear() - birth.getFullYear()
  const beforeBirthday = today.getMonth() < birth.getMonth() || (today.getMonth() === birth.getMonth() && today.getDate() < birth.getDate())
  if (beforeBirthday) age--
  return age >= 0 ? age : undefined
}

interface SettingsProps {
  locale: Locale
  profile?: Profile
  assetCategories: AssetCategory[]
  saving: boolean
  onClose: () => void
  onSaveProfile: (profile: Profile) => Promise<void>
  onCreateAssetCategory: (name: string) => Promise<AssetCategory[]>
  onRenameAssetCategory: (currentName: string, name: string) => Promise<AssetCategory[]>
  onDeleteAssetCategory: (name: string) => Promise<AssetCategory[]>
  onExport: (fileName: string) => Promise<void>
  onRestore: (document: LifeLedgerExport) => Promise<void>
  onResetMarketHistory: () => Promise<void>
  onResetNetWorthHistory: () => Promise<void>
  onDeleteAllData: () => Promise<void>
}

export function Settings({ locale, profile, assetCategories, saving, onClose, onSaveProfile, onCreateAssetCategory, onRenameAssetCategory, onDeleteAssetCategory, onExport, onRestore, onResetMarketHistory, onResetNetWorthHistory, onDeleteAllData }: SettingsProps) {
  const [currency, setCurrency] = useState(profile?.baseCurrency ?? 'EUR')
  const [birthDate, setBirthDate] = useState(profile?.birthDate ?? '')
  const [sex, setSex] = useState<Profile['sex']>(profile?.sex ?? 'Neutral')
  const [expectedLifespan, setExpectedLifespan] = useState(profile?.expectedLifespan ?? 82)
  const [lifespanReference, setLifespanReference] = useState<'neutral' | 'female' | 'male' | 'custom'>(referenceFor(profile?.expectedLifespan, profile?.sex))
  const [restoring, setRestoring] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const [message, setMessage] = useState<string>()
  const [csvSummary, setCsvSummary] = useState<{ transactions: number; months: number; totalExpenses: number; averageMonthlyExpenses: number; currency: string; categories: Array<{ name: string; total: number }> }>()
  const [customCategories, setCustomCategories] = useState(assetCategories)
  const [newCategory, setNewCategory] = useState('')
  const [categoryBusy, setCategoryBusy] = useState(false)

  const currentAge = ageOnToday(birthDate)
  const canSaveProfile = profile && (currency !== profile.baseCurrency || birthDate !== profile.birthDate || sex !== profile.sex || expectedLifespan !== profile.expectedLifespan)

  function setReference(reference: 'neutral' | 'female' | 'male' | 'custom') {
    setLifespanReference(reference)
    if (reference !== 'custom') setExpectedLifespan(referenceAge(reference))
  }

  function changeSex(nextSex: Profile['sex']) {
    setSex(nextSex)
    if (lifespanReference !== 'custom') setReference(referenceFromSex(nextSex))
  }

  async function restore(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    if (!file) return

    try {
      const document = JSON.parse(await file.text()) as LifeLedgerExport
      if (document.schemaVersion !== 1) throw new Error(tr(locale, 'This file is not a compatible LifeLedger backup.', 'Ce fichier n’est pas une sauvegarde LifeLedger compatible.'))
      if (!window.confirm(tr(locale, 'Restore this backup and replace the current data?', 'Restaurer cette sauvegarde et remplacer les données actuelles ?'))) return
      setRestoring(true)
      await onRestore(document)
      setMessage(tr(locale, 'Backup restored.', 'Sauvegarde restaurée.'))
    } catch (reason) {
      setMessage(reason instanceof Error ? reason.message : tr(locale, 'Unable to restore this backup.', 'Impossible de restaurer cette sauvegarde.'))
    } finally {
      setRestoring(false)
      event.target.value = ''
    }
  }

  async function resetHistory() {
    if (!window.confirm(tr(locale, 'Delete every locally stored market-price point?', 'Supprimer tous les points de cours enregistrés localement ?'))) return
    await onResetMarketHistory()
    setMessage(tr(locale, 'Price history reset.', 'Historique des cours réinitialisé.'))
  }

  async function resetNetWorthHistory() {
    if (!window.confirm(tr(locale, 'Delete every locally stored net-worth history point?', 'Supprimer tous les points d’historique de patrimoine enregistrés localement ?'))) return
    await onResetNetWorthHistory()
    setMessage(tr(locale, 'Net-worth history reset.', 'Historique du patrimoine réinitialisé.'))
  }

  async function deleteAllData() {
    const warning = tr(
      locale,
      'Delete every profile, scenario, income, asset, debt, expense, life event and chart point? This cannot be undone. Create a backup first if you may need this data.',
      'Supprimer tous les profils, scénarios, revenus, actifs, dettes, dépenses, événements de vie et points de graphique ? Cette action est irréversible. Créez une sauvegarde si vous souhaitez conserver ces données.'
    )
    if (!window.confirm(warning)) return

    const confirmation = window.prompt(tr(locale, 'Type DELETE to permanently erase your local data.', 'Tapez DELETE pour effacer définitivement vos données locales.'))
    if (confirmation !== 'DELETE') return

    try {
      setDeleting(true)
      await onDeleteAllData()
    } catch (reason) {
      setMessage(reason instanceof Error ? reason.message : tr(locale, 'Unable to delete local data.', 'Impossible de supprimer les données locales.'))
      setDeleting(false)
    }
  }

  async function analyseCsv(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    if (!file) return
    try { setCsvSummary(await api.analyseExpenseCsv(await file.text())) }
    catch (reason) { setMessage(reason instanceof Error ? reason.message : tr(locale, 'Unable to read this CSV file.', 'Impossible de lire ce fichier CSV.')) }
    finally { event.target.value = '' }
  }

  async function addAssetCategory() {
    if (!newCategory.trim()) return
    try { setCategoryBusy(true); setCustomCategories(await onCreateAssetCategory(newCategory)); setNewCategory(''); setMessage(undefined) }
    catch (reason) { setMessage(reason instanceof Error ? reason.message : tr(locale, 'Unable to add this category.', 'Impossible d’ajouter cette catégorie.')) }
    finally { setCategoryBusy(false) }
  }

  async function renameAssetCategory(currentName: string, name: string) {
    try { setCategoryBusy(true); setCustomCategories(await onRenameAssetCategory(currentName, name)); setMessage(tr(locale, 'Category updated.', 'Catégorie mise à jour.')) }
    catch (reason) { setMessage(reason instanceof Error ? reason.message : tr(locale, 'Unable to rename this category.', 'Impossible de renommer cette catégorie.')) }
    finally { setCategoryBusy(false) }
  }

  async function deleteAssetCategory(name: string) {
    if (!window.confirm(tr(locale, `Delete the category “${name}”?`, `Supprimer la catégorie « ${name} » ?`))) return
    try { setCategoryBusy(true); setCustomCategories(await onDeleteAssetCategory(name)); setMessage(tr(locale, 'Category deleted.', 'Catégorie supprimée.')) }
    catch (reason) { setMessage(reason instanceof Error ? reason.message : tr(locale, 'Unable to delete this category.', 'Impossible de supprimer cette catégorie.')) }
    finally { setCategoryBusy(false) }
  }

  return (
    <div className="fixed inset-0 z-30 overflow-y-auto bg-inkDeep/70 p-4 backdrop-blur-sm">
      <section aria-modal="true" aria-label={tr(locale, 'Settings', 'Paramètres')} className="glass mx-auto my-6 w-full max-w-xl rounded-3xl p-6" role="dialog">
        <div className="flex items-start justify-between gap-4">
          <div>
            <p className="eyebrow">{tr(locale, 'Settings', 'Paramètres')}</p>
            <h2 className="mt-2 text-xl font-semibold text-white">{tr(locale, 'Your private LifeLedger', 'Votre LifeLedger privé')}</h2>
          </div>
          <button aria-label={tr(locale, 'Close settings', 'Fermer les paramètres')} className="text-muted transition hover:text-white" type="button" onClick={onClose}>✕</button>
        </div>

        <div className="mt-6 space-y-4">
          {profile && <article className="rounded-2xl border border-white/10 bg-white/5 p-4">
            <label className="block text-sm text-mist">
              {tr(locale, 'Default currency', 'Devise par défaut')}
              <select className="field mt-2" value={currency} onChange={(event) => setCurrency(event.target.value)}>
                {currencies.map((entry) => <option className="bg-panel" key={entry} value={entry}>{entry}</option>)}
              </select>
            </label>
            <p className="mt-2 text-xs leading-5 text-muted">{tr(locale, 'All totals and forecasts are shown in this currency.', 'Tous les totaux et prévisions seront affichés dans cette devise.')}</p>
            <div className="mt-5 border-t border-white/10 pt-4">
              <p className="text-sm font-medium text-mist">{tr(locale, 'About you', 'À propos de vous')}</p>
              <p className="mt-1 text-xs leading-5 text-muted">{tr(locale, 'Your date of birth gives every forecast both a calendar year and an age. Sex is optional and only guides the life-expectancy reference; it never changes your finances automatically.', 'Votre date de naissance donne à chaque prévision une année civile et un âge. Le sexe est facultatif et sert uniquement de référence pour l’espérance de vie ; il ne modifie jamais vos finances automatiquement.')}</p>
              <div className="mt-3 grid gap-3 sm:grid-cols-2">
                <label className="block text-sm text-mist">
                  {tr(locale, 'Date of birth', 'Date de naissance')}
                  <input className="field mt-2" max={new Date().toISOString().slice(0, 10)} type="date" value={birthDate} onChange={(event) => setBirthDate(event.target.value)} />
                  {currentAge !== undefined && <span className="mt-2 block text-xs text-sky">{tr(locale, 'Current age:', 'Âge actuel :')} {currentAge} {tr(locale, 'years', 'ans')}</span>}
                </label>
                <label className="block text-sm text-mist">
                  {tr(locale, 'Sex for reference statistics', 'Sexe pour les statistiques de référence')}
                  <select className="field mt-2" value={sex} onChange={(event) => changeSex(event.target.value as Profile['sex'])}>
                    <option className="bg-panel" value="Neutral">{tr(locale, 'Neutral / prefer not to say', 'Neutre / préfère ne pas préciser')}</option>
                    <option className="bg-panel" value="Female">{tr(locale, 'Woman', 'Femme')}</option>
                    <option className="bg-panel" value="Male">{tr(locale, 'Man', 'Homme')}</option>
                  </select>
                </label>
              </div>
            </div>
            <div className="mt-5 border-t border-white/10 pt-4">
              <p className="text-sm font-medium text-mist">{tr(locale, 'Life expectancy for projections', 'Espérance de vie pour les projections')}</p>
              <p className="mt-1 text-xs leading-5 text-muted">{tr(locale, 'This sets the final age of your Monte Carlo and long-term forecasts. It is only a planning assumption.', 'Cette valeur définit l’âge final de vos simulations Monte Carlo et prévisions long terme. C’est uniquement une hypothèse de planification.')}</p>
              <div className="mt-3 grid gap-3 sm:grid-cols-2">
                <label className="block text-sm text-mist">
                  {tr(locale, 'European reference', 'Référence européenne')}
                  <select className="field mt-2" value={lifespanReference} onChange={(event) => {
                    setReference(event.target.value as 'neutral' | 'female' | 'male' | 'custom')
                  }}>
                    <option className="bg-panel" value="neutral">{tr(locale, 'Neutral reference · 82 years', 'Référence neutre · 82 ans')}</option>
                    <option className="bg-panel" value="male">{tr(locale, 'Man · 79 years', 'Homme · 79 ans')}</option>
                    <option className="bg-panel" value="female">{tr(locale, 'Woman · 84 years', 'Femme · 84 ans')}</option>
                    <option className="bg-panel" value="custom">{tr(locale, 'Custom value', 'Valeur personnalisée')}</option>
                  </select>
                </label>
                <label className="block text-sm text-mist">
                  {tr(locale, 'Final age', 'Âge final')}
                  <input className="field mt-2" max="130" min="50" type="number" value={expectedLifespan} onChange={(event) => { setLifespanReference('custom'); setExpectedLifespan(Math.max(50, Math.min(130, Number(event.target.value) || 50))) }} />
                </label>
              </div>
              <p className="mt-3 text-xs leading-5 text-muted">{tr(locale, 'European references are rounded planning values: 79 for men, 84 for women and 82 for the neutral reference. They are editable assumptions, not a prediction.', 'Les références européennes sont des hypothèses arrondies : 79 ans pour les hommes, 84 ans pour les femmes et 82 ans pour la référence neutre. Elles sont modifiables et ne constituent pas une prédiction.')}</p>
            </div>
            <button className="ghost-button mt-4" disabled={saving || !canSaveProfile || !birthDate} onClick={() => profile && void onSaveProfile({ ...profile, baseCurrency: currency, birthDate, sex, expectedLifespan })}>{saving ? tr(locale, 'Saving…', 'Enregistrement…') : tr(locale, 'Save settings', 'Enregistrer les paramètres')}</button>
          </article>}

          <article className="rounded-2xl border border-white/10 bg-white/5 p-4">
            <p className="text-sm font-medium text-mist">{tr(locale, 'Asset categories', 'Catégories d’actifs')}</p>
            <p className="mt-1 text-xs leading-5 text-muted">{tr(locale, 'Built-in categories are translated automatically. Add personal categories for classifications that match your life.', 'Les catégories intégrées sont traduites automatiquement. Ajoutez vos propres catégories pour adapter le classement à votre patrimoine.')}</p>
            <div className="mt-3 flex flex-wrap gap-2">{builtInAssetKinds.map((kind) => <span className="rounded-xl border border-white/10 bg-white/5 px-3 py-2 text-xs text-mist" key={kind}>{assetKindLabel(locale, kind)}</span>)}</div>
            <div className="mt-4 space-y-2 border-t border-white/10 pt-4">
              {customCategories.map((category) => <AssetCategoryRow busy={categoryBusy} category={category} key={category.name} locale={locale} onRename={renameAssetCategory} onDelete={deleteAssetCategory} />)}
              {customCategories.length === 0 && <p className="text-xs text-muted">{tr(locale, 'No personal category yet.', 'Aucune catégorie personnelle pour le moment.')}</p>}
            </div>
            <div className="mt-3 flex flex-col gap-2 sm:flex-row">
              <input aria-label={tr(locale, 'New asset category', 'Nouvelle catégorie d’actifs')} className="field" maxLength={80} placeholder={tr(locale, 'For example: Vehicles', 'Par exemple : Véhicules')} value={newCategory} onChange={(event) => setNewCategory(event.target.value)} />
              <button className="ghost-button shrink-0" disabled={categoryBusy || !newCategory.trim()} onClick={() => void addAssetCategory()}>{tr(locale, 'Add category', 'Ajouter')}</button>
            </div>
          </article>

          <article className="rounded-2xl border border-white/10 bg-white/5 p-4">
            <p className="text-sm font-medium text-mist">{tr(locale, 'Import bank or Revolut CSV', 'Importer un CSV bancaire ou Revolut')}</p>
            <p className="mt-1 text-xs leading-5 text-muted">{tr(locale, 'The file is analysed only in memory on your local server. Transactions are not saved.', 'Le fichier est analysé uniquement en mémoire sur votre serveur local. Les transactions ne sont pas enregistrées.')}</p>
            <label className="ghost-button mt-3 inline-flex cursor-pointer">{tr(locale, 'Choose CSV file', 'Choisir un fichier CSV')}<input accept=".csv,text/csv" className="sr-only" type="file" onChange={(event) => void analyseCsv(event)} /></label>
            {csvSummary && <div className="mt-4 rounded-xl border border-sky/20 bg-sky/10 p-3"><p className="text-sm font-semibold text-sky">{tr(locale, 'Estimated monthly expenses', 'Dépenses mensuelles estimées')} : {new Intl.NumberFormat(locale, { style: 'currency', currency: csvSummary.currency, maximumFractionDigits: 0 }).format(csvSummary.averageMonthlyExpenses)}</p><p className="mt-1 text-xs text-muted">{csvSummary.transactions} {tr(locale, 'outgoing transactions across', 'transactions sortantes sur')} {csvSummary.months} {tr(locale, 'month(s).', 'mois.')}</p><div className="mt-3 space-y-1 text-xs text-mist">{csvSummary.categories.slice(0, 4).map((category) => <p key={category.name}>{category.name} · {new Intl.NumberFormat(locale, { style: 'currency', currency: csvSummary.currency, maximumFractionDigits: 0 }).format(category.total)}</p>)}</div></div>}
          </article>

          {profile && <article className="rounded-2xl border border-white/10 bg-white/5 p-4">
            <p className="text-sm font-medium text-mist">{tr(locale, 'Export my wealth', 'Exporter mon patrimoine')}</p>
            <p className="mt-1 text-xs leading-5 text-muted">{tr(locale, 'Download all of your financial data as a private JSON file.', 'Télécharge toutes vos données financières dans un fichier JSON privé.')}</p>
            <button className="ghost-button mt-3" onClick={() => void onExport('lifeledger-patrimoine.json')}>{tr(locale, 'Export my data', 'Exporter mes données')}</button>
          </article>}

          {profile && <article className="rounded-2xl border border-white/10 bg-white/5 p-4">
            <p className="text-sm font-medium text-mist">{tr(locale, 'Market-price chart', 'Graphique des cours')}</p>
            <p className="mt-1 text-xs leading-5 text-muted">{tr(locale, 'Quotes are stored only on your server. Resetting removes the chart points, not your assets.', 'Les cours restent sur votre serveur. La remise à zéro supprime les points du graphique, pas vos actifs.')}</p>
            <button className="ghost-button mt-3" onClick={() => void resetHistory()}>{tr(locale, 'Reset chart points', 'Réinitialiser les points')}</button>
          </article>}

          {profile && <article className="rounded-2xl border border-white/10 bg-white/5 p-4">
            <p className="text-sm font-medium text-mist">{tr(locale, 'Net-worth history', 'Historique du patrimoine')}</p>
            <p className="mt-1 text-xs leading-5 text-muted">{tr(locale, 'A point is saved locally each time LifeLedger starts for your reference scenario. Resetting removes only the history, not your financial entries.', 'Un point est enregistré localement à chaque démarrage de LifeLedger pour votre scénario de référence. La remise à zéro supprime uniquement l’historique, pas vos données financières.')}</p>
            <button className="ghost-button mt-3" onClick={() => void resetNetWorthHistory()}>{tr(locale, 'Reset net-worth history', 'Réinitialiser l’historique')}</button>
          </article>}

          <article className="rounded-2xl border border-white/10 bg-white/5 p-4">
            <p className="text-sm font-medium text-mist">{tr(locale, 'Backup', 'Sauvegarde')}</p>
            <p className="mt-1 text-xs leading-5 text-muted">{tr(locale, 'Keep a dated copy before changing your plan. Restoration replaces current data.', 'Conservez une copie datée avant un changement. La restauration remplace les données actuelles.')}</p>
            <div className="mt-3 flex flex-wrap items-center gap-3">
              {profile && <button className="ghost-button" onClick={() => void onExport(`lifeledger-backup-${new Date().toISOString().slice(0, 10)}.json`)}>{tr(locale, 'Create backup', 'Créer une sauvegarde')}</button>}
              <label className="ghost-button cursor-pointer">
                {restoring ? tr(locale, 'Restoring…', 'Restauration…') : tr(locale, 'Restore backup', 'Restaurer une sauvegarde')}
                <input accept="application/json,.json" className="sr-only" disabled={restoring} type="file" onChange={(event) => void restore(event)} />
              </label>
            </div>
            {!profile && <p className="mt-3 text-xs leading-5 text-muted">{tr(locale, 'Your ledger is empty. Restore a backup to continue.', 'Votre registre est vide. Restaurez une sauvegarde pour continuer.')}</p>}
          </article>

          {profile && <article className="rounded-2xl border border-danger/40 bg-danger/10 p-4">
            <p className="text-sm font-semibold text-danger">{tr(locale, 'Danger zone', 'Zone de danger')}</p>
            <p className="mt-1 text-sm font-medium text-mist">{tr(locale, 'Delete all data', 'Supprimer toutes les données')}</p>
            <p className="mt-1 text-xs leading-5 text-muted">{tr(locale, 'Permanently removes your local financial plan and the demo data. You will have to confirm twice.', 'Supprime définitivement votre plan financier local et les données de démonstration. Deux confirmations seront demandées.')}</p>
            <button className="mt-3 rounded-xl border border-danger/50 bg-danger/10 px-4 py-2.5 text-sm font-semibold text-danger transition hover:bg-danger/20 disabled:cursor-not-allowed disabled:opacity-60" disabled={deleting} onClick={() => void deleteAllData()}>
              {deleting ? tr(locale, 'Deleting…', 'Suppression…') : tr(locale, 'Delete all data', 'Supprimer toutes les données')}
            </button>
          </article>}

          {message && <p className="text-xs text-sky">{message}</p>}
        </div>
      </section>
    </div>
  )
}

/** Edits one personal category without losing its original identity during a rename. */
function AssetCategoryRow({ category, locale, busy, onRename, onDelete }: { category: AssetCategory; locale: Locale; busy: boolean; onRename: (currentName: string, name: string) => Promise<void>; onDelete: (name: string) => Promise<void> }) {
  const [name, setName] = useState(category.name)
  const changed = name.trim() !== category.name
  return <div className="rounded-xl border border-white/10 bg-white/5 p-3"><div className="flex flex-col gap-2 sm:flex-row sm:items-center"><input aria-label={tr(locale, `Edit ${category.name}`, `Modifier ${category.name}`)} className="field min-w-0" maxLength={80} value={name} onChange={(event) => setName(event.target.value)} /><div className="flex shrink-0 gap-2"><button className="ghost-button px-3 py-2" disabled={busy || !changed || !name.trim()} onClick={() => void onRename(category.name, name)}>{tr(locale, 'Save', 'Enregistrer')}</button><button className="rounded-xl border border-danger/30 px-3 py-2 text-xs text-danger transition hover:bg-danger/10 disabled:cursor-not-allowed disabled:opacity-50" disabled={busy || category.assetCount > 0} title={category.assetCount > 0 ? tr(locale, 'Reassign the assets before deleting this category.', 'Changez d’abord la catégorie des actifs concernés.') : undefined} onClick={() => void onDelete(category.name)}>{tr(locale, 'Delete', 'Supprimer')}</button></div></div><p className="mt-2 text-xs text-muted">{category.assetCount} {tr(locale, category.assetCount === 1 ? 'asset uses this category' : 'assets use this category', category.assetCount === 1 ? 'actif utilise cette catégorie' : 'actifs utilisent cette catégorie')}</p></div>
}
