import { ChangeEvent, useState } from 'react'
import { assetKindLabel, builtInAssetKinds } from '../assetCategories'
import type { Locale } from '../i18n'
import type { AssetCategory, AssetProfileDefinition, AssetProfileDefinitionInput, LifeLedgerExport, Profile } from '../types'
import { AssetProfileBuilder } from './AssetProfileBuilder'
import { DateField } from './DateField'

const currencies = ['EUR', 'USD', 'PLN', 'GBP', 'CHF', 'CAD', 'JPY']
const tr = (locale: Locale, english: string, french: string) => locale === 'fr' ? french : english
type Theme = 'dark' | 'light'
type SettingsSection = 'profile' | 'appearance' | 'assets' | 'data' | 'maintenance'

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
  assetProfileDefinitions: AssetProfileDefinition[]
  theme: Theme
  saving: boolean
  onThemeChange: (theme: Theme) => void
  onSaveProfile: (profile: Profile) => Promise<void>
  onCreateAssetCategory: (name: string) => Promise<AssetCategory[]>
  onRenameAssetCategory: (currentName: string, name: string) => Promise<AssetCategory[]>
  onDeleteAssetCategory: (name: string) => Promise<AssetCategory[]>
  onCreateAssetProfileDefinition: (definition: AssetProfileDefinitionInput) => Promise<void>
  onUpdateAssetProfileDefinition: (key: string, definition: AssetProfileDefinitionInput) => Promise<void>
  onDeleteAssetProfileDefinition: (key: string) => Promise<void>
  onExport: (fileName: string) => Promise<void>
  onRestore: (document: LifeLedgerExport) => Promise<void>
  onRestoreDemo: () => Promise<void>
  onResetMarketHistory: () => Promise<void>
  onResetNetWorthHistory: () => Promise<void>
  onDeleteAllData: () => Promise<void>
}

export function Settings({ locale, profile, assetCategories, assetProfileDefinitions, theme, saving, onThemeChange, onSaveProfile, onCreateAssetCategory, onRenameAssetCategory, onDeleteAssetCategory, onCreateAssetProfileDefinition, onUpdateAssetProfileDefinition, onDeleteAssetProfileDefinition, onExport, onRestore, onRestoreDemo, onResetMarketHistory, onResetNetWorthHistory, onDeleteAllData }: SettingsProps) {
  const [section, setSection] = useState<SettingsSection>('profile')
  const [themeDraft, setThemeDraft] = useState<Theme>(theme)
  const [currency, setCurrency] = useState(profile?.baseCurrency ?? 'EUR')
  const [birthDate, setBirthDate] = useState(profile?.birthDate ?? '')
  const [sex, setSex] = useState<Profile['sex']>(profile?.sex ?? 'Neutral')
  const [expectedLifespan, setExpectedLifespan] = useState(profile?.expectedLifespan ?? 82)
  const [lifespanReference, setLifespanReference] = useState<'neutral' | 'female' | 'male' | 'custom'>(referenceFor(profile?.expectedLifespan, profile?.sex))
  const [restoring, setRestoring] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const [restoringDemo, setRestoringDemo] = useState(false)
  const [message, setMessage] = useState<string>()
  const [profileSaveError, setProfileSaveError] = useState<string>()
  const [customCategories, setCustomCategories] = useState(assetCategories)
  const [newCategory, setNewCategory] = useState('')
  const [categoryBusy, setCategoryBusy] = useState(false)

  const currentAge = ageOnToday(birthDate)
  const canSaveProfile = profile && (currency !== profile.baseCurrency || birthDate !== profile.birthDate || sex !== profile.sex || expectedLifespan !== profile.expectedLifespan)

  function setReference(reference: 'neutral' | 'female' | 'male' | 'custom') {
    setProfileSaveError(undefined)
    setLifespanReference(reference)
    if (reference !== 'custom') setExpectedLifespan(referenceAge(reference))
  }

  function changeSex(nextSex: Profile['sex']) {
    setProfileSaveError(undefined)
    setSex(nextSex)
    if (lifespanReference !== 'custom') setReference(referenceFromSex(nextSex))
  }

  async function saveProfile() {
    if (!profile) return
    setProfileSaveError(undefined)
    try {
      await onSaveProfile({ ...profile, baseCurrency: currency, birthDate, sex, expectedLifespan })
    } catch (reason) {
      const technicalMessage = reason instanceof Error ? reason.message : ''
      const status = technicalMessage.match(/\((\d{3})\)/)?.[1]
      setProfileSaveError(tr(
        locale,
        `Unable to save these settings${status ? ` (server error ${status})` : ''}. Your changes are still here; you can try again.`,
        `Impossible d’enregistrer ces paramètres${status ? ` (erreur serveur ${status})` : ''}. Vos modifications sont conservées dans cette fenêtre ; vous pouvez réessayer.`
      ))
    }
  }

  async function restore(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    if (!file) return

    try {
      const document = JSON.parse(await file.text()) as LifeLedgerExport
      if (document.schemaVersion < 1 || document.schemaVersion > 12) throw new Error(tr(locale, 'This file is not a compatible LifeLedger backup.', 'Ce fichier n’est pas une sauvegarde LifeLedger compatible.'))
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

  async function restoreDemo() {
    const warning = tr(
      locale,
      'Restore the complete demo? This replaces your current local data with a fixed fictional household. Export a backup first if you need the current data.',
      'Restaurer la démonstration complète ? Vos données locales actuelles seront remplacées par un foyer fictif fixe. Exportez d’abord une sauvegarde si vous souhaitez conserver vos données.'
    )
    if (!window.confirm(warning)) return
    try {
      setRestoringDemo(true)
      setMessage(undefined)
      await onRestoreDemo()
    } catch (reason) {
      setMessage(reason instanceof Error ? reason.message : tr(locale, 'Unable to restore the demo.', 'Impossible de restaurer la démonstration.'))
      setRestoringDemo(false)
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
    <section className="space-y-6" aria-label={tr(locale, 'Settings', 'Paramètres')}>
      <header><p className="eyebrow">{tr(locale, 'Settings', 'Paramètres')}</p><h1 className="mt-2 text-3xl font-semibold text-white">{tr(locale, 'Configure your LifeLedger', 'Configurer votre LifeLedger')}</h1><p className="mt-2 max-w-2xl text-sm leading-6 text-muted">{tr(locale, 'Settings are grouped by purpose. Each section saves independently, so changing the appearance never changes your financial profile.', 'Les paramètres sont regroupés par usage. Chaque section s’enregistre séparément : modifier l’apparence ne modifie jamais votre profil financier.')}</p></header>
      <div className="grid gap-5 lg:grid-cols-[220px_minmax(0,1fr)]">
        <nav className="section-card flex gap-2 overflow-x-auto p-3 lg:sticky lg:top-6 lg:block lg:h-fit lg:space-y-1" aria-label={tr(locale, 'Settings sections', 'Sections des paramètres')}>
          {([
            ['profile', '◎', tr(locale, 'Profile & simulation', 'Profil et simulation')],
            ['appearance', '◐', tr(locale, 'Appearance', 'Apparence')],
            ['assets', '◇', tr(locale, 'Assets configuration', 'Configuration des actifs')],
            ['data', '⇩', tr(locale, 'Data & backups', 'Données et sauvegardes')],
            ['maintenance', '⚠', tr(locale, 'Maintenance', 'Maintenance')],
          ] as Array<[SettingsSection, string, string]>).map(([id, icon, label]) => <button className={`nav-item min-w-max ${section === id ? 'nav-item-active' : ''}`} key={id} type="button" onClick={() => { setMessage(undefined); setSection(id) }}><span>{icon}</span><span>{label}</span></button>)}
        </nav>
        <div className="min-w-0 space-y-4">
          {section === 'profile' && profile && <article className="section-card">
            <label className="block text-sm text-mist">
              {tr(locale, 'Default currency', 'Devise par défaut')}
              <select className="field mt-2" value={currency} onChange={(event) => { setProfileSaveError(undefined); setCurrency(event.target.value) }}>
                {currencies.map((entry) => <option className="bg-panel" key={entry} value={entry}>{entry}</option>)}
              </select>
            </label>
            <p className="mt-2 text-xs leading-5 text-muted">{tr(locale, 'All totals and forecasts are shown in this currency.', 'Tous les totaux et prévisions seront affichés dans cette devise.')}</p>
            <div className="mt-5 border-t border-white/10 pt-4">
              <p className="text-sm font-medium text-mist">{tr(locale, 'About you', 'À propos de vous')}</p>
              <p className="mt-1 text-xs leading-5 text-muted">{tr(locale, 'Your date of birth gives every forecast both a calendar year and an age. Sex is optional and only guides the life-expectancy reference; it never changes your finances automatically.', 'Votre date de naissance donne à chaque prévision une année civile et un âge. Le sexe est facultatif et sert uniquement de référence pour l’espérance de vie ; il ne modifie jamais vos finances automatiquement.')}</p>
              <div className="mt-3 grid gap-3 sm:grid-cols-2">
                <div><DateField label={tr(locale, 'Date of birth', 'Date de naissance')} locale={locale} max={new Date().toISOString().slice(0, 10)} minYear={1900} value={birthDate} onChange={(nextBirthDate) => { setProfileSaveError(undefined); setBirthDate(nextBirthDate) }} />{currentAge !== undefined && <span className="mt-2 block text-xs text-sky">{tr(locale, 'Current age:', 'Âge actuel :')} {currentAge} {tr(locale, 'years', 'ans')}</span>}</div>
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
                  <input className="field mt-2" max="130" min="50" type="number" value={expectedLifespan} onChange={(event) => { setProfileSaveError(undefined); setLifespanReference('custom'); setExpectedLifespan(Math.max(50, Math.min(130, Number(event.target.value) || 50))) }} />
                </label>
              </div>
              <p className="mt-3 text-xs leading-5 text-muted">{tr(locale, 'European references are rounded planning values: 79 for men, 84 for women and 82 for the neutral reference. They are editable assumptions, not a prediction.', 'Les références européennes sont des hypothèses arrondies : 79 ans pour les hommes, 84 ans pour les femmes et 82 ans pour la référence neutre. Elles sont modifiables et ne constituent pas une prédiction.')}</p>
            </div>
            {profileSaveError && <div aria-live="assertive" className="mt-4 rounded-xl border border-danger/40 bg-danger/10 px-4 py-3 text-sm leading-5 text-danger" role="alert"><p className="font-semibold">{tr(locale, 'Settings not saved', 'Paramètres non enregistrés')}</p><p className="mt-1 text-xs leading-5">{profileSaveError}</p></div>}
            <button className="ghost-button mt-4" disabled={saving || !canSaveProfile || !birthDate} onClick={() => void saveProfile()}>{saving ? tr(locale, 'Saving…', 'Enregistrement…') : tr(locale, 'Save settings', 'Enregistrer les paramètres')}</button>
          </article>}

          {section === 'appearance' && <article className="section-card">
            <p className="text-sm font-semibold text-white">{tr(locale, 'Visual theme', 'Thème visuel')}</p>
            <p className="mt-1 text-xs leading-5 text-muted">{tr(locale, 'Choose the most comfortable contrast. This preference stays only in this browser and does not enter your financial backup.', 'Choisissez le contraste le plus confortable. Cette préférence reste uniquement dans ce navigateur et ne fait pas partie de votre sauvegarde financière.')}</p>
            <div className="mt-5 grid gap-3 sm:grid-cols-2">
              <button aria-pressed={themeDraft === 'dark'} className={`rounded-2xl border p-4 text-left transition ${themeDraft === 'dark' ? 'border-sky/60 bg-sky/10 ring-1 ring-sky/20' : 'border-white/15 bg-white/5 hover:bg-white/10'}`} type="button" onClick={() => setThemeDraft('dark')}><span className="block text-lg">◐</span><span className="mt-3 block font-medium text-mist">{tr(locale, 'Dark theme', 'Thème sombre')}</span><span className="mt-1 block text-xs leading-5 text-muted">{tr(locale, 'Deep blue surfaces for reduced glare.', 'Surfaces bleu profond pour limiter l’éblouissement.')}</span></button>
              <button aria-pressed={themeDraft === 'light'} className={`rounded-2xl border p-4 text-left transition ${themeDraft === 'light' ? 'border-sky/60 bg-sky/10 ring-1 ring-sky/20' : 'border-white/15 bg-white/5 hover:bg-white/10'}`} type="button" onClick={() => setThemeDraft('light')}><span className="block text-lg">◑</span><span className="mt-3 block font-medium text-mist">{tr(locale, 'Light theme', 'Thème clair')}</span><span className="mt-1 block text-xs leading-5 text-muted">{tr(locale, 'Bright opaque glass with dark readable text.', 'Verre clair et opaque avec un texte sombre lisible.')}</span></button>
            </div>
            <button className="primary-button mt-5" disabled={themeDraft === theme} type="button" onClick={() => { onThemeChange(themeDraft); setMessage(tr(locale, 'Appearance saved.', 'Apparence enregistrée.')) }}>{tr(locale, 'Save appearance', 'Enregistrer l’apparence')}</button>
          </article>}

          {section === 'assets' && <article className="section-card">
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
          </article>}

          {section === 'assets' && <AssetProfileBuilder definitions={assetProfileDefinitions} locale={locale} onCreate={onCreateAssetProfileDefinition} onUpdate={onUpdateAssetProfileDefinition} onDelete={onDeleteAssetProfileDefinition} />}

          {section === 'data' && <article className="rounded-2xl border border-sky/20 bg-sky/10 p-4">
            <div className="flex flex-col justify-between gap-4 sm:flex-row sm:items-start">
              <div>
                <p className="text-sm font-semibold text-sky">{tr(locale, 'Demo mode', 'Mode démonstration')}</p>
                <p className="mt-1 text-sm font-medium text-mist">{tr(locale, 'Restore the complete fictional household', 'Restaurer le foyer fictif complet')}</p>
                <p className="mt-1 max-w-xl text-xs leading-5 text-muted">{tr(locale, 'Loads the same dated scenarios, currencies, assets, debts, expenses, bank operations and future events every time. You can freely add, edit and delete entries, then restore this exact state for regression tests and screenshots.', 'Recharge toujours les mêmes scénarios datés, devises, actifs, dettes, dépenses, opérations bancaires et événements futurs. Vous pouvez librement ajouter, modifier et supprimer, puis retrouver exactement cet état pour les tests de non-régression et les captures d’écran.')}</p>
              </div>
              <span className="shrink-0 rounded-full border border-sky/20 bg-white/5 px-3 py-1.5 text-xs font-semibold text-sky">v1</span>
            </div>
            <button className="ghost-button mt-4 border-sky/20 bg-white/10" disabled={restoringDemo} onClick={() => void restoreDemo()}>
              {restoringDemo ? tr(locale, 'Restoring demo…', 'Restauration de la démo…') : tr(locale, 'Restore demo data', 'Restaurer les données de démo')}
            </button>
          </article>}

          {section === 'data' && profile && <article className="section-card">
            <p className="text-sm font-medium text-mist">{tr(locale, 'Export my wealth', 'Exporter mon patrimoine')}</p>
            <p className="mt-1 text-xs leading-5 text-muted">{tr(locale, 'Download all of your financial data as a private JSON file.', 'Télécharge toutes vos données financières dans un fichier JSON privé.')}</p>
            <button className="ghost-button mt-3" onClick={() => void onExport('lifeledger-patrimoine.json')}>{tr(locale, 'Export my data', 'Exporter mes données')}</button>
          </article>}

          {section === 'maintenance' && profile && <article className="section-card">
            <p className="text-sm font-medium text-mist">{tr(locale, 'Market-price chart', 'Graphique des cours')}</p>
            <p className="mt-1 text-xs leading-5 text-muted">{tr(locale, 'Quotes are stored only on your server. Resetting removes the chart points, not your assets.', 'Les cours restent sur votre serveur. La remise à zéro supprime les points du graphique, pas vos actifs.')}</p>
            <button className="ghost-button mt-3" onClick={() => void resetHistory()}>{tr(locale, 'Reset chart points', 'Réinitialiser les points')}</button>
          </article>}

          {section === 'maintenance' && profile && <article className="section-card">
            <p className="text-sm font-medium text-mist">{tr(locale, 'Net-worth history', 'Historique du patrimoine')}</p>
            <p className="mt-1 text-xs leading-5 text-muted">{tr(locale, 'A point is saved locally each time LifeLedger starts for your reference scenario. Resetting removes only the history, not your financial entries.', 'Un point est enregistré localement à chaque démarrage de LifeLedger pour votre scénario de référence. La remise à zéro supprime uniquement l’historique, pas vos données financières.')}</p>
            <button className="ghost-button mt-3" onClick={() => void resetNetWorthHistory()}>{tr(locale, 'Reset net-worth history', 'Réinitialiser l’historique')}</button>
          </article>}

          {section === 'data' && <article className="section-card">
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
          </article>}

          {section === 'maintenance' && profile && <article className="rounded-2xl border border-danger/40 bg-danger/10 p-5">
            <p className="text-sm font-semibold text-danger">{tr(locale, 'Danger zone', 'Zone de danger')}</p>
            <p className="mt-1 text-sm font-medium text-mist">{tr(locale, 'Delete all data', 'Supprimer toutes les données')}</p>
            <p className="mt-1 text-xs leading-5 text-muted">{tr(locale, 'Permanently removes your local financial plan and the demo data. You will have to confirm twice.', 'Supprime définitivement votre plan financier local et les données de démonstration. Deux confirmations seront demandées.')}</p>
            <button className="mt-3 rounded-xl border border-danger/50 bg-danger/10 px-4 py-2.5 text-sm font-semibold text-danger transition hover:bg-danger/20 disabled:cursor-not-allowed disabled:opacity-60" disabled={deleting} onClick={() => void deleteAllData()}>
              {deleting ? tr(locale, 'Deleting…', 'Suppression…') : tr(locale, 'Delete all data', 'Supprimer toutes les données')}
            </button>
          </article>}

          {message && <p className="text-xs text-sky">{message}</p>}
        </div>
      </div>
    </section>
  )
}

/** Edits one personal category without losing its original identity during a rename. */
function AssetCategoryRow({ category, locale, busy, onRename, onDelete }: { category: AssetCategory; locale: Locale; busy: boolean; onRename: (currentName: string, name: string) => Promise<void>; onDelete: (name: string) => Promise<void> }) {
  const [name, setName] = useState(category.name)
  const changed = name.trim() !== category.name
  return <div className="rounded-xl border border-white/10 bg-white/5 p-3"><div className="flex flex-col gap-2 sm:flex-row sm:items-center"><input aria-label={tr(locale, `Edit ${category.name}`, `Modifier ${category.name}`)} className="field min-w-0" maxLength={80} value={name} onChange={(event) => setName(event.target.value)} /><div className="flex shrink-0 gap-2"><button className="ghost-button px-3 py-2" disabled={busy || !changed || !name.trim()} onClick={() => void onRename(category.name, name)}>{tr(locale, 'Save', 'Enregistrer')}</button><button className="rounded-xl border border-danger/30 px-3 py-2 text-xs text-danger transition hover:bg-danger/10 disabled:cursor-not-allowed disabled:opacity-50" disabled={busy || category.assetCount > 0} title={category.assetCount > 0 ? tr(locale, 'Reassign the assets before deleting this category.', 'Changez d’abord la catégorie des actifs concernés.') : undefined} onClick={() => void onDelete(category.name)}>{tr(locale, 'Delete', 'Supprimer')}</button></div></div><p className="mt-2 text-xs text-muted">{category.assetCount} {tr(locale, category.assetCount === 1 ? 'asset uses this category' : 'assets use this category', category.assetCount === 1 ? 'actif utilise cette catégorie' : 'actifs utilisent cette catégorie')}</p></div>
}
