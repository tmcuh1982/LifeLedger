import { useState } from 'react'
import { localeNames, locales, type Locale } from '../i18n'
import type { AssetProfileDefinition, AssetProfileDefinitionInput, AssetProfileFieldDefinition, AssetProfileFieldType, AssetProfileOptionDefinition } from '../types'

interface AssetProfileBuilderProps {
  locale: Locale
  definitions: AssetProfileDefinition[]
  onCreate: (definition: AssetProfileDefinitionInput) => Promise<void>
  onUpdate: (key: string, definition: AssetProfileDefinitionInput) => Promise<void>
  onDelete: (key: string) => Promise<void>
}

const copy = {
  en: { title: 'Custom asset sheets', intro: 'Create a simple reusable sheet for a bicycle, boat, jewellery, artwork or anything else you own.', add: 'Create a sheet', none: 'No custom sheet yet.', edit: 'Edit', remove: 'Delete', version: 'Version', name: 'Sheet name', language: 'Language being edited', fields: 'Fields', addField: 'Add a field', fieldName: 'Field name', type: 'Answer type', required: 'Required answer', choices: 'Choices, separated by commas', choicesHelp: 'Example: New, Good, Used', save: 'Save sheet', saving: 'Saving…', cancel: 'Cancel', deleteConfirm: 'Delete this unused sheet?', versionHelp: 'Saving an existing sheet creates a new version. Older asset data remains readable.', error: 'Unable to save this sheet.', deleteError: 'Unable to delete this sheet.', removeField: 'Remove field' },
  fr: { title: 'Fiches d’actifs personnalisées', intro: 'Créez une fiche simple et réutilisable pour un vélo, bateau, bijou, tableau ou tout autre bien.', add: 'Créer une fiche', none: 'Aucune fiche personnalisée.', edit: 'Modifier', remove: 'Supprimer', version: 'Version', name: 'Nom de la fiche', language: 'Langue en cours de modification', fields: 'Champs', addField: 'Ajouter un champ', fieldName: 'Nom du champ', type: 'Type de réponse', required: 'Réponse obligatoire', choices: 'Choix séparés par des virgules', choicesHelp: 'Exemple : Neuf, Bon état, Usagé', save: 'Enregistrer la fiche', saving: 'Enregistrement…', cancel: 'Annuler', deleteConfirm: 'Supprimer cette fiche inutilisée ?', versionHelp: 'Enregistrer une fiche existante crée une nouvelle version. Les anciennes données restent lisibles.', error: 'Impossible d’enregistrer cette fiche.', deleteError: 'Impossible de supprimer cette fiche.', removeField: 'Supprimer le champ' },
  pl: { title: 'Własne karty aktywów', intro: 'Utwórz prostą kartę dla roweru, łodzi, biżuterii, dzieła sztuki lub innego majątku.', add: 'Utwórz kartę', none: 'Brak własnych kart.', edit: 'Edytuj', remove: 'Usuń', version: 'Wersja', name: 'Nazwa karty', language: 'Edytowany język', fields: 'Pola', addField: 'Dodaj pole', fieldName: 'Nazwa pola', type: 'Typ odpowiedzi', required: 'Odpowiedź wymagana', choices: 'Opcje oddzielone przecinkami', choicesHelp: 'Przykład: Nowy, Dobry, Używany', save: 'Zapisz kartę', saving: 'Zapisywanie…', cancel: 'Anuluj', deleteConfirm: 'Usunąć tę nieużywaną kartę?', versionHelp: 'Zapisanie istniejącej karty tworzy nową wersję. Starsze dane pozostają czytelne.', error: 'Nie można zapisać tej karty.', deleteError: 'Nie można usunąć tej karty.', removeField: 'Usuń pole' },
  de: { title: 'Eigene Vermögensblätter', intro: 'Erstellen Sie ein einfaches Blatt für Fahrrad, Boot, Schmuck, Kunst oder andere Werte.', add: 'Blatt erstellen', none: 'Noch kein eigenes Blatt.', edit: 'Bearbeiten', remove: 'Löschen', version: 'Version', name: 'Name des Blatts', language: 'Bearbeitete Sprache', fields: 'Felder', addField: 'Feld hinzufügen', fieldName: 'Feldname', type: 'Antworttyp', required: 'Antwort erforderlich', choices: 'Auswahl, durch Kommas getrennt', choicesHelp: 'Beispiel: Neu, Gut, Gebraucht', save: 'Blatt speichern', saving: 'Speichern…', cancel: 'Abbrechen', deleteConfirm: 'Dieses ungenutzte Blatt löschen?', versionHelp: 'Beim Speichern wird eine neue Version erstellt. Ältere Daten bleiben lesbar.', error: 'Dieses Blatt konnte nicht gespeichert werden.', deleteError: 'Dieses Blatt konnte nicht gelöscht werden.', removeField: 'Feld entfernen' },
  nl: { title: 'Eigen bezitfiches', intro: 'Maak een eenvoudige fiche voor een fiets, boot, juweel, kunstwerk of ander bezit.', add: 'Fiche maken', none: 'Nog geen eigen fiche.', edit: 'Bewerken', remove: 'Verwijderen', version: 'Versie', name: 'Naam van de fiche', language: 'Taal die u bewerkt', fields: 'Velden', addField: 'Veld toevoegen', fieldName: 'Naam van het veld', type: 'Antwoordtype', required: 'Antwoord verplicht', choices: 'Keuzes, gescheiden door komma’s', choicesHelp: 'Voorbeeld: Nieuw, Goed, Gebruikt', save: 'Fiche opslaan', saving: 'Opslaan…', cancel: 'Annuleren', deleteConfirm: 'Deze ongebruikte fiche verwijderen?', versionHelp: 'Opslaan maakt een nieuwe versie. Oudere gegevens blijven leesbaar.', error: 'Deze fiche kon niet worden opgeslagen.', deleteError: 'Deze fiche kon niet worden verwijderd.', removeField: 'Veld verwijderen' },
} as const

const typeLabels: Record<Locale, Record<AssetProfileFieldType, string>> = {
  en: { Text: 'Short text', Number: 'Number', Date: 'Date', Boolean: 'Yes / no', Select: 'List of choices', Area: 'Area (m²)', Distance: 'Distance (km)', Condition: 'Condition from 1 to 5' },
  fr: { Text: 'Texte court', Number: 'Nombre', Date: 'Date', Boolean: 'Oui / non', Select: 'Liste de choix', Area: 'Surface (m²)', Distance: 'Distance (km)', Condition: 'État de 1 à 5' },
  pl: { Text: 'Krótki tekst', Number: 'Liczba', Date: 'Data', Boolean: 'Tak / nie', Select: 'Lista wyboru', Area: 'Powierzchnia (m²)', Distance: 'Odległość (km)', Condition: 'Stan od 1 do 5' },
  de: { Text: 'Kurzer Text', Number: 'Zahl', Date: 'Datum', Boolean: 'Ja / nein', Select: 'Auswahlliste', Area: 'Fläche (m²)', Distance: 'Entfernung (km)', Condition: 'Zustand von 1 bis 5' },
  nl: { Text: 'Korte tekst', Number: 'Getal', Date: 'Datum', Boolean: 'Ja / nee', Select: 'Keuzelijst', Area: 'Oppervlakte (m²)', Distance: 'Afstand (km)', Condition: 'Staat van 1 tot 5' },
}

const fieldTypes: AssetProfileFieldType[] = ['Text', 'Number', 'Date', 'Boolean', 'Select', 'Area', 'Distance', 'Condition']
const emptyLabels = () => Object.fromEntries(locales.map((entry) => [entry, ''])) as Record<string, string>
const translated = (labels: Record<string, string>, locale: Locale) => labels[locale] || labels.en || Object.values(labels).find(Boolean) || '—'

/** Builds a stable client-side key; the label can change without breaking stored asset values. */
function newField(): AssetProfileFieldDefinition {
  return { key: `field${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`, labels: emptyLabels(), type: 'Text', required: false }
}

/** Creates and versions custom schema-driven asset sheets from plain-language controls. */
export function AssetProfileBuilder({ locale, definitions, onCreate, onUpdate, onDelete }: AssetProfileBuilderProps) {
  const text = copy[locale]
  const custom = definitions.filter((definition) => definition.isCustom)
  const [editing, setEditing] = useState<AssetProfileDefinition | null | undefined>(undefined)
  const [labels, setLabels] = useState<Record<string, string>>(emptyLabels)
  const [fields, setFields] = useState<AssetProfileFieldDefinition[]>([])
  const [labelLocale, setLabelLocale] = useState<Locale>(locale)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string>()

  function begin(definition?: AssetProfileDefinition) {
    setEditing(definition ?? null)
    setLabels(definition ? { ...definition.labels } : emptyLabels())
    setFields(definition ? definition.fields.map((field) => ({ ...field, labels: { ...field.labels }, options: field.options?.map((option) => ({ ...option, labels: { ...option.labels } })) })) : [newField()])
    setLabelLocale(locale)
    setMessage(undefined)
  }

  function updateField(key: string, change: Partial<AssetProfileFieldDefinition>) {
    setFields((current) => current.map((field) => field.key === key ? { ...field, ...change } : field))
  }

  function updateChoices(field: AssetProfileFieldDefinition, value: string) {
    const names = value.split(',').map((entry) => entry.trim()).filter(Boolean)
    const options = names.map((name, index) => {
      const existing = field.options?.[index]
      return { value: existing?.value ?? `option${index + 1}`, labels: { ...(existing?.labels ?? emptyLabels()), [labelLocale]: name } } satisfies AssetProfileOptionDefinition
    })
    updateField(field.key, { options })
  }

  async function save() {
    try {
      setBusy(true); setMessage(undefined)
      const input = { labels, fields }
      if (editing) await onUpdate(editing.key, input)
      else await onCreate(input)
      setEditing(undefined)
    } catch (reason) { setMessage(reason instanceof Error ? reason.message : text.error) }
    finally { setBusy(false) }
  }

  async function remove(definition: AssetProfileDefinition) {
    if (!window.confirm(text.deleteConfirm)) return
    try { setBusy(true); setMessage(undefined); await onDelete(definition.key) }
    catch (reason) { setMessage(reason instanceof Error ? reason.message : text.deleteError) }
    finally { setBusy(false) }
  }

  const hasName = Object.values(labels).some((label) => label.trim())
  const fieldsAreValid = fields.length > 0 && fields.every((field) => Object.values(field.labels).some((label) => label.trim()) && (field.type !== 'Select' || Boolean(field.options?.length)))

  return <article className="rounded-2xl border border-white/10 bg-white/5 p-4">
    <div className="flex flex-wrap items-start justify-between gap-3"><div><p className="text-sm font-medium text-mist">{text.title}</p><p className="mt-1 max-w-xl text-xs leading-5 text-muted">{text.intro}</p></div>{editing === undefined && <button className="ghost-button shrink-0" onClick={() => begin()}>{text.add}</button>}</div>
    {message && <p className="mt-3 rounded-xl border border-danger/30 bg-danger/10 px-3 py-2 text-xs text-danger">{message}</p>}

    {editing === undefined ? <div className="mt-4 space-y-2">{custom.length === 0 ? <p className="text-xs text-muted">{text.none}</p> : custom.map((definition) => <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-white/10 bg-white/5 p-3" key={definition.key}><div><p className="text-sm font-medium text-mist">{translated(definition.labels, locale)}</p><p className="mt-1 text-xs text-muted">{text.version} {definition.version} · {definition.fields.length} {text.fields.toLowerCase()}</p></div><div className="flex gap-2"><button className="ghost-button px-3 py-2" disabled={busy} onClick={() => begin(definition)}>{text.edit}</button><button className="rounded-xl border border-danger/30 px-3 py-2 text-xs font-semibold text-danger transition hover:bg-danger/10" disabled={busy} onClick={() => void remove(definition)}>{text.remove}</button></div></div>)}</div> : <div className="mt-5 space-y-4 border-t border-white/10 pt-4">
      <div className="grid gap-3 sm:grid-cols-2"><label className="block text-sm text-mist">{text.language}<select className="field mt-2" value={labelLocale} onChange={(event) => setLabelLocale(event.target.value as Locale)}>{locales.map((entry) => <option className="bg-panel" key={entry} value={entry}>{localeNames[entry]}</option>)}</select></label><label className="block text-sm text-mist">{text.name}<input className="field mt-2" maxLength={80} value={labels[labelLocale] ?? ''} onChange={(event) => setLabels((current) => ({ ...current, [labelLocale]: event.target.value }))} /></label></div>
      {editing && <p className="rounded-xl bg-sky/10 px-3 py-2 text-xs leading-5 text-sky">{text.versionHelp}</p>}
      <div><div className="flex items-center justify-between gap-3"><p className="text-sm font-medium text-mist">{text.fields}</p><button className="ghost-button px-3 py-2" onClick={() => setFields((current) => [...current, newField()])}>{text.addField}</button></div><div className="mt-3 space-y-3">{fields.map((field) => <div className="rounded-xl border border-white/10 bg-panel/60 p-3" key={field.key}><div className="grid gap-3 sm:grid-cols-[1fr_180px]"><label className="block text-sm text-mist">{text.fieldName}<input className="field mt-2" maxLength={100} value={field.labels[labelLocale] ?? ''} onChange={(event) => updateField(field.key, { labels: { ...field.labels, [labelLocale]: event.target.value } })} /></label><label className="block text-sm text-mist">{text.type}<select className="field mt-2" value={field.type} onChange={(event) => { const type = event.target.value as AssetProfileFieldType; updateField(field.key, { type, options: type === 'Select' ? field.options?.length ? field.options : [{ value: 'option1', labels: emptyLabels() }] : undefined }) }}>{fieldTypes.map((type) => <option className="bg-panel" key={type} value={type}>{typeLabels[locale][type]}</option>)}</select></label></div>{field.type === 'Select' && <label className="mt-3 block text-sm text-mist">{text.choices}<input className="field mt-2" placeholder={text.choicesHelp} value={(field.options ?? []).map((option) => option.labels[labelLocale] || '').join(', ')} onChange={(event) => updateChoices(field, event.target.value)} /></label>}<div className="mt-3 flex flex-wrap items-center justify-between gap-3"><label className="flex items-center gap-2 text-xs text-mist"><input checked={field.required} type="checkbox" onChange={(event) => updateField(field.key, { required: event.target.checked })} />{text.required}</label><button className="text-xs font-semibold text-danger" disabled={fields.length === 1} onClick={() => setFields((current) => current.filter((candidate) => candidate.key !== field.key))}>{text.removeField}</button></div></div>)}</div></div>
      <div className="flex flex-wrap justify-end gap-3 border-t border-white/10 pt-4"><button className="ghost-button" disabled={busy} onClick={() => setEditing(undefined)}>{text.cancel}</button><button className="primary-button" disabled={busy || !hasName || !fieldsAreValid} onClick={() => void save()}>{busy ? text.saving : text.save}</button></div>
    </div>}
  </article>
}
