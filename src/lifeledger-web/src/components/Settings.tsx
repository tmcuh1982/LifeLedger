import { ChangeEvent, useState } from 'react'
import type { Locale } from '../i18n'
import type { LifeLedgerExport, Profile } from '../types'

const currencies = ['EUR', 'USD', 'PLN', 'GBP', 'CHF', 'CAD', 'JPY']
const tr = (locale: Locale, english: string, french: string) => locale === 'fr' ? french : english

interface SettingsProps {
  locale: Locale
  profile: Profile
  saving: boolean
  onClose: () => void
  onSaveCurrency: (currency: string) => Promise<void>
  onExport: (fileName: string) => Promise<void>
  onRestore: (document: LifeLedgerExport) => Promise<void>
  onResetMarketHistory: () => Promise<void>
}

export function Settings({ locale, profile, saving, onClose, onSaveCurrency, onExport, onRestore, onResetMarketHistory }: SettingsProps) {
  const [currency, setCurrency] = useState(profile.baseCurrency)
  const [restoring, setRestoring] = useState(false)
  const [message, setMessage] = useState<string>()

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
    } catch (reason) { setMessage(reason instanceof Error ? reason.message : tr(locale, 'Unable to restore this backup.', 'Impossible de restaurer cette sauvegarde.')) }
    finally { setRestoring(false); event.target.value = '' }
  }

  async function resetHistory() {
    if (!window.confirm(tr(locale, 'Delete every locally stored market-price point?', 'Supprimer tous les points de cours enregistrés localement ?'))) return
    await onResetMarketHistory()
    setMessage(tr(locale, 'Price history reset.', 'Historique des cours réinitialisé.'))
  }

  return <div className="fixed inset-0 z-30 overflow-y-auto bg-inkDeep/70 p-4 backdrop-blur-sm"><section aria-modal="true" aria-label={tr(locale, 'Settings', 'Paramètres')} className="glass mx-auto my-6 w-full max-w-xl rounded-3xl p-6" role="dialog"><div className="flex items-start justify-between gap-4"><div><p className="eyebrow">{tr(locale, 'Settings', 'Paramètres')}</p><h2 className="mt-2 text-xl font-semibold text-white">{tr(locale, 'Your private LifeLedger', 'Votre LifeLedger privé')}</h2></div><button className="text-muted transition hover:text-white" type="button" onClick={onClose}>✕</button></div><div className="mt-6 space-y-4"><article className="rounded-2xl border border-white/10 bg-white/5 p-4"><label className="block text-sm text-mist">{tr(locale, 'Default currency', 'Devise par défaut')}<select className="field mt-2" value={currency} onChange={(event) => setCurrency(event.target.value)}>{currencies.map((entry) => <option className="bg-panel" key={entry} value={entry}>{entry}</option>)}</select></label><p className="mt-2 text-xs leading-5 text-muted">{tr(locale, 'All totals and forecasts are shown in this currency.', 'Tous les totaux et prévisions seront affichés dans cette devise.')}</p><button className="ghost-button mt-3" disabled={saving || currency === profile.baseCurrency} onClick={() => void onSaveCurrency(currency)}>{saving ? tr(locale, 'Saving…', 'Enregistrement…') : tr(locale, 'Save currency', 'Enregistrer la devise')}</button></article><article className="rounded-2xl border border-white/10 bg-white/5 p-4"><p className="text-sm font-medium text-mist">{tr(locale, 'Export my wealth', 'Exporter mon patrimoine')}</p><p className="mt-1 text-xs leading-5 text-muted">{tr(locale, 'Download all of your financial data as a private JSON file.', 'Télécharge toutes vos données financières dans un fichier JSON privé.')}</p><button className="ghost-button mt-3" onClick={() => void onExport('lifeledger-patrimoine.json')}>{tr(locale, 'Export my data', 'Exporter mes données')}</button></article><article className="rounded-2xl border border-white/10 bg-white/5 p-4"><p className="text-sm font-medium text-mist">{tr(locale, 'Market-price chart', 'Graphique des cours')}</p><p className="mt-1 text-xs leading-5 text-muted">{tr(locale, 'Quotes are stored only on your server. Resetting removes the chart points, not your assets.', 'Les cours restent sur votre serveur. La remise à zéro supprime les points du graphique, pas vos actifs.')}</p><button className="ghost-button mt-3" onClick={() => void resetHistory()}>{tr(locale, 'Reset chart points', 'Réinitialiser les points')}</button></article><article className="rounded-2xl border border-white/10 bg-white/5 p-4"><p className="text-sm font-medium text-mist">{tr(locale, 'Backup', 'Sauvegarde')}</p><p className="mt-1 text-xs leading-5 text-muted">{tr(locale, 'Keep a dated copy before changing your plan. Restoration replaces current data.', 'Conservez une copie datée avant un changement. La restauration remplace les données actuelles.')}</p><div className="mt-3 flex flex-wrap items-center gap-3"><button className="ghost-button" onClick={() => void onExport(`lifeledger-backup-${new Date().toISOString().slice(0, 10)}.json`)}>{tr(locale, 'Create backup', 'Créer une sauvegarde')}</button><label className="ghost-button cursor-pointer">{restoring ? tr(locale, 'Restoring…', 'Restauration…') : tr(locale, 'Restore backup', 'Restaurer une sauvegarde')}<input accept="application/json,.json" className="sr-only" disabled={restoring} type="file" onChange={(event) => void restore(event)} /></label></div>{message && <p className="mt-3 text-xs text-sky">{message}</p>}</article></div></section></div>
}
