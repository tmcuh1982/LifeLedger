import { FormEvent, useEffect, useMemo, useState } from 'react'
import { Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { api } from '../api'
import { assetCategoryLabel, assetKindLabel, builtInAssetKinds } from '../assetCategories'
import type { Locale } from '../i18n'
import type { AssetCategory, AssetProfileDefinition, LedgerItem, ScenarioData } from '../types'
import { AssetDossierEditor } from './AssetDossierEditor'
import { DateField } from './DateField'

type Resource = keyof ScenarioData
type Draft = Record<string, string | boolean>
type CurrencyRate = { code: string; unitsPerEuro: number; source: string; isStale: boolean }

interface PlannerProps {
  data: ScenarioData
  scenarioId: string
  assetCategories: AssetCategory[]
  profileDefinitions: AssetProfileDefinition[]
  currency: string
  locale: Locale
  onCreate: (resource: Resource, item: Record<string, unknown>) => Promise<void>
  onUpdate: (resource: Resource, id: string, item: Record<string, unknown>) => Promise<void>
  onDelete: (resource: Resource, id: string) => Promise<void>
  onImportBank: () => void
}

const translations = {
  en: { eyebrow: 'Life inputs', title: 'Build your financial picture.', intro: 'Enter the facts that shape your financial life. Every value stays on your server.', add: 'Add', edit: 'Edit', remove: 'Remove', none: 'Nothing added yet.', save: 'Save', saving: 'Saving…', cancel: 'Cancel', newInput: 'New entry', editInput: 'Edit entry', name: 'Name', currency: 'Currency', start: 'Start date', end: 'End date', amount: 'Amount', currentValue: 'Current value today', monthly: 'Monthly amount', annualAmount: 'Gross amount for the year', amountEntry: 'How do you know this income?', monthlyEntry: 'A monthly amount', annualEntry: 'A total for the year', seasonal: 'Income changes with the season', seasonalHelp: 'Split the annual total between months. Percentages are automatically normalised.', seasonalTotal: 'Entered distribution', linkedAsset: 'Related asset (optional)', noLinkedAsset: 'No related asset', expenseAmount: 'Amount each time', frequency: 'How often?', repeat: 'Repeat this event', category: 'Category', ticker: 'Ticker symbol', quantity: 'Number of shares', priceHistory: 'Price history', taxCountry: 'Country of taxation', taxRate: 'Tax rate (%)', capitalGainsTax: 'Tax on annual gains', return: 'Expected annual return (%)', growth: 'Annual growth (%)', volatility: 'Value can go up or down (%)', liquid: 'Available quickly', liquidHelp: 'Money you can use quickly, such as a bank account, savings or an investment you can sell easily.', taxable: 'Taxable income', payment: 'Monthly payment', balance: 'Outstanding balance', rate: 'Interest rate (%)', index: 'Index to inflation', date: 'Event date', oneOff: 'One-off impact', recurring: 'Monthly impact', duration: 'Duration (months, 0 = ongoing)', notes: 'Notes', monthlyContribution: 'Monthly contribution', expenseDate: 'Date of expense', saveInAdvance: 'Set money aside every month', saveInAdvanceHelp: 'LifeLedger reserves part of the amount each month and pays the expense from this envelope when it is due.', savingsStart: 'Start saving on', monthlyReserve: 'Estimated monthly envelope' },
  fr: { eyebrow: 'Données financières', title: 'Construisez votre situation financière.', intro: 'Saisissez les éléments qui façonnent votre vie financière. Toutes les données restent sur votre serveur.', add: 'Ajouter', edit: 'Modifier', remove: 'Supprimer', none: 'Aucune donnée pour le moment.', save: 'Enregistrer', saving: 'Enregistrement…', cancel: 'Annuler', newInput: 'Nouvelle entrée', editInput: 'Modifier l’entrée', name: 'Nom', currency: 'Devise', start: 'Date de début', end: 'Date de fin', amount: 'Montant', currentValue: 'Valeur actuelle aujourd’hui', monthly: 'Montant mensuel', annualAmount: 'Montant brut pour l’année', amountEntry: 'Comment connaissez-vous ce revenu ?', monthlyEntry: 'Un montant par mois', annualEntry: 'Un total pour l’année', seasonal: 'Le revenu change selon la saison', seasonalHelp: 'Répartissez le total annuel entre les mois. Les pourcentages sont automatiquement ajustés.', seasonalTotal: 'Répartition saisie', linkedAsset: 'Bien concerné (facultatif)', noLinkedAsset: 'Aucun bien lié', expenseAmount: 'Montant à chaque fois', frequency: 'À quelle fréquence ?', repeat: 'Répéter cet événement', category: 'Catégorie', ticker: 'Ticker', quantity: 'Nombre de titres', priceHistory: 'Historique du cours', taxCountry: 'Pays d’imposition', taxRate: 'Taux d’impôt (%)', capitalGainsTax: 'Impôt annuel sur les plus-values', return: 'Rendement annuel attendu (%)', growth: 'Croissance annuelle (%)', volatility: 'Valeur qui peut monter ou baisser (%)', liquid: 'Disponible rapidement', liquidHelp: 'Argent utilisable rapidement : compte bancaire, épargne ou placement facile à vendre.', taxable: 'Revenu imposable', payment: 'Mensualité', balance: 'Solde restant dû', rate: 'Taux d’intérêt (%)', index: 'Indexé sur l’inflation', date: 'Date de l’événement', oneOff: 'Impact ponctuel', recurring: 'Impact mensuel', duration: 'Durée (mois, 0 = permanent)', notes: 'Notes', monthlyContribution: 'Versement mensuel', expenseDate: 'Date de la dépense', saveInAdvance: 'Mettre de côté chaque mois', saveInAdvanceHelp: 'LifeLedger réserve une partie du montant chaque mois et paie la dépense depuis cette enveloppe à la date prévue.', savingsStart: 'Commencer à mettre de côté le', monthlyReserve: 'Enveloppe mensuelle estimée' },
} as const

const english = translations.en
const today = () => new Date().toISOString().slice(0, 10)
const fiftyYearsFromToday = () => { const date = new Date(); date.setFullYear(date.getFullYear() + 50); return date.toISOString().slice(0, 10) }
const fiveYearsAfter = (value: string) => { const date = new Date(`${value || today()}T12:00:00`); date.setFullYear(date.getFullYear() + 5); return date.toISOString().slice(0, 10) }
const calendarMonths = Array.from({ length: 12 }, (_, index) => index + 1)

const frequencyOptionsByLocale: Record<Locale, { value: string; label: string }[]> = {
  en: [{ value: 'Daily', label: 'Every day' }, { value: 'Weekly', label: 'Every week' }, { value: 'EveryTwoWeeks', label: 'Every 2 weeks' }, { value: 'Monthly', label: 'Every month' }, { value: 'Quarterly', label: 'Every 3 months' }, { value: 'Yearly', label: 'Every year' }, { value: 'EveryFiveYears', label: 'Every 5 years' }],
  fr: [{ value: 'Daily', label: 'Chaque jour' }, { value: 'Weekly', label: 'Chaque semaine' }, { value: 'EveryTwoWeeks', label: 'Toutes les 2 semaines' }, { value: 'Monthly', label: 'Chaque mois' }, { value: 'Quarterly', label: 'Tous les 3 mois' }, { value: 'Yearly', label: 'Chaque année' }, { value: 'EveryFiveYears', label: 'Tous les 5 ans' }],
  pl: [{ value: 'Daily', label: 'Codziennie' }, { value: 'Weekly', label: 'Co tydzień' }, { value: 'EveryTwoWeeks', label: 'Co 2 tygodnie' }, { value: 'Monthly', label: 'Co miesiąc' }, { value: 'Quarterly', label: 'Co 3 miesiące' }, { value: 'Yearly', label: 'Co rok' }, { value: 'EveryFiveYears', label: 'Co 5 lat' }],
  de: [{ value: 'Daily', label: 'Jeden Tag' }, { value: 'Weekly', label: 'Jede Woche' }, { value: 'EveryTwoWeeks', label: 'Alle 2 Wochen' }, { value: 'Monthly', label: 'Jeden Monat' }, { value: 'Quarterly', label: 'Alle 3 Monate' }, { value: 'Yearly', label: 'Jedes Jahr' }, { value: 'EveryFiveYears', label: 'Alle 5 Jahre' }],
  nl: [{ value: 'Daily', label: 'Elke dag' }, { value: 'Weekly', label: 'Elke week' }, { value: 'EveryTwoWeeks', label: 'Elke 2 weken' }, { value: 'Monthly', label: 'Elke maand' }, { value: 'Quarterly', label: 'Elke 3 maanden' }, { value: 'Yearly', label: 'Elk jaar' }, { value: 'EveryFiveYears', label: 'Elke 5 jaar' }]
}

const eventKindOptionsByLocale: Record<Locale, { value: string; label: string }[]> = {
  en: [{ value: 'HousePurchase', label: 'House purchase' }, { value: 'VehiclePurchase', label: 'Car purchase' }, { value: 'NewChild', label: 'New child' }, { value: 'Inheritance', label: 'Inheritance' }, { value: 'JobLoss', label: 'Job loss' }, { value: 'SalaryIncrease', label: 'Salary increase' }, { value: 'BusinessCreation', label: 'Business creation' }, { value: 'EarlyRetirement', label: 'Early retirement' }, { value: 'Relocation', label: 'Relocation' }, { value: 'Divorce', label: 'Separation or divorce' }, { value: 'Custom', label: 'Other event' }],
  fr: [{ value: 'HousePurchase', label: 'Achat d’un logement' }, { value: 'VehiclePurchase', label: 'Achat d’une voiture' }, { value: 'NewChild', label: 'Nouvel enfant' }, { value: 'Inheritance', label: 'Héritage' }, { value: 'JobLoss', label: 'Perte d’emploi' }, { value: 'SalaryIncrease', label: 'Augmentation de salaire' }, { value: 'BusinessCreation', label: 'Création d’entreprise' }, { value: 'EarlyRetirement', label: 'Retraite anticipée' }, { value: 'Relocation', label: 'Déménagement' }, { value: 'Divorce', label: 'Séparation ou divorce' }, { value: 'Custom', label: 'Autre événement' }],
  pl: [{ value: 'HousePurchase', label: 'Zakup mieszkania lub domu' }, { value: 'VehiclePurchase', label: 'Zakup samochodu' }, { value: 'NewChild', label: 'Narodziny dziecka' }, { value: 'Inheritance', label: 'Spadek' }, { value: 'JobLoss', label: 'Utrata pracy' }, { value: 'SalaryIncrease', label: 'Podwyżka wynagrodzenia' }, { value: 'BusinessCreation', label: 'Założenie firmy' }, { value: 'EarlyRetirement', label: 'Wcześniejsza emerytura' }, { value: 'Relocation', label: 'Przeprowadzka' }, { value: 'Divorce', label: 'Rozstanie lub rozwód' }, { value: 'Custom', label: 'Inne wydarzenie' }],
  de: [{ value: 'HousePurchase', label: 'Immobilienkauf' }, { value: 'VehiclePurchase', label: 'Autokauf' }, { value: 'NewChild', label: 'Geburt eines Kindes' }, { value: 'Inheritance', label: 'Erbschaft' }, { value: 'JobLoss', label: 'Arbeitslosigkeit' }, { value: 'SalaryIncrease', label: 'Gehaltserhöhung' }, { value: 'BusinessCreation', label: 'Unternehmensgründung' }, { value: 'EarlyRetirement', label: 'Vorruhestand' }, { value: 'Relocation', label: 'Umzug' }, { value: 'Divorce', label: 'Trennung oder Scheidung' }, { value: 'Custom', label: 'Anderes Ereignis' }],
  nl: [{ value: 'HousePurchase', label: 'Aankoop van een woning' }, { value: 'VehiclePurchase', label: 'Aankoop van een auto' }, { value: 'NewChild', label: 'Geboorte van een kind' }, { value: 'Inheritance', label: 'Erfenis' }, { value: 'JobLoss', label: 'Baanverlies' }, { value: 'SalaryIncrease', label: 'Salarisverhoging' }, { value: 'BusinessCreation', label: 'Een bedrijf starten' }, { value: 'EarlyRetirement', label: 'Vervroegd pensioen' }, { value: 'Relocation', label: 'Verhuizing' }, { value: 'Divorce', label: 'Scheiding' }, { value: 'Custom', label: 'Andere gebeurtenis' }]
}

const expenseEvolutionCopy: Record<Locale, { title: string; help: string; add: string; date: string; amount: string; remove: string; inflation: string }> = {
  en: { title: 'Future changes to this amount', help: 'Add a step when you expect this expense to change. Inflation continues from each new amount.', add: 'Add a future amount', date: 'From', amount: 'New amount', remove: 'Remove', inflation: 'The new amount becomes the inflation starting point on this date.' },
  fr: { title: 'Évolution future de ce montant', help: 'Ajoutez un palier lorsque vous pensez que cette dépense va changer. L’inflation continue ensuite à partir du nouveau montant.', add: 'Ajouter un futur montant', date: 'À partir du', amount: 'Nouveau montant', remove: 'Supprimer', inflation: 'Le nouveau montant devient la base de calcul de l’inflation à cette date.' },
  pl: { title: 'Przyszłe zmiany tej kwoty', help: 'Dodaj próg, gdy przewidujesz zmianę tego wydatku. Inflacja jest następnie liczona od nowej kwoty.', add: 'Dodaj przyszłą kwotę', date: 'Od', amount: 'Nowa kwota', remove: 'Usuń', inflation: 'Nowa kwota staje się podstawą inflacji od tej daty.' },
  de: { title: 'Zukünftige Änderungen dieses Betrags', help: 'Fügen Sie eine Stufe hinzu, wenn sich diese Ausgabe voraussichtlich ändert. Die Inflation läuft ab dem neuen Betrag weiter.', add: 'Zukünftigen Betrag hinzufügen', date: 'Ab', amount: 'Neuer Betrag', remove: 'Entfernen', inflation: 'Der neue Betrag wird ab diesem Datum zur Inflationsbasis.' },
  nl: { title: 'Toekomstige wijzigingen van dit bedrag', help: 'Voeg een stap toe wanneer u verwacht dat deze uitgave verandert. De inflatie loopt daarna verder vanaf het nieuwe bedrag.', add: 'Toekomstig bedrag toevoegen', date: 'Vanaf', amount: 'Nieuw bedrag', remove: 'Verwijderen', inflation: 'Het nieuwe bedrag wordt vanaf deze datum de basis voor inflatie.' }
}

const assetSaleCopy: Record<Locale, Record<string, string>> = {
  en: { title: 'Planned asset sales', description: 'Sell a property or investment and decide where the money goes.', asset: 'Asset to sell', date: 'Sale date', projected: 'Use its projected value on that date', projectedHelp: 'LifeLedger grows the asset until the sale date, then uses that value as the gross sale price.', manualPrice: 'Estimated gross sale price', costs: 'Selling costs', tax: 'Tax on the capital gain (%)', repay: 'Repay debts linked to this asset', repayHelp: 'The allocated outstanding balances are deducted from the proceeds and removed from future debt.', destination: 'Where should the remaining money go?', cash: 'Keep it as available cash', anotherAsset: 'Add it to another existing asset', investmentPlan: 'Add it to an investment plan', targetAsset: 'Asset receiving the money', targetPlan: 'Investment plan receiving the money', summary: 'The sold asset disappears from its category. Only fees, tax and a difference between its projected value and sale price change total wealth.', projectedPrice: 'Value calculated on the sale date' },
  fr: { title: 'Ventes d’actifs planifiées', description: 'Vendez un bien ou un placement et choisissez où va l’argent.', asset: 'Bien ou actif à vendre', date: 'Date de vente', projected: 'Utiliser sa valeur projetée à cette date', projectedHelp: 'LifeLedger fait évoluer le bien jusqu’à la date de vente, puis utilise cette valeur comme prix de vente brut.', manualPrice: 'Prix de vente brut estimé', costs: 'Frais de vente', tax: 'Impôt sur la plus-value (%)', repay: 'Rembourser les dettes liées à ce bien', repayHelp: 'Les soldes encore dus et affectés au bien sont retirés du produit de vente et des dettes futures.', destination: 'Que faire de l’argent restant ?', cash: 'Le conserver en trésorerie disponible', anotherAsset: 'L’ajouter à un autre actif existant', investmentPlan: 'L’ajouter à un plan d’investissement', targetAsset: 'Actif qui reçoit l’argent', targetPlan: 'Plan d’investissement qui reçoit l’argent', summary: 'Le bien vendu disparaît de sa catégorie. Seuls les frais, l’impôt et l’écart entre sa valeur projetée et son prix de vente modifient le patrimoine total.', projectedPrice: 'Valeur calculée à la date de vente' },
  pl: { title: 'Planowana sprzedaż aktywów', description: 'Sprzedaj nieruchomość lub inwestycję i wybierz przeznaczenie środków.', asset: 'Aktywo do sprzedaży', date: 'Data sprzedaży', projected: 'Użyj prognozowanej wartości w tym dniu', projectedHelp: 'LifeLedger zwiększa wartość aktywa do dnia sprzedaży i używa jej jako ceny brutto.', manualPrice: 'Szacowana cena sprzedaży brutto', costs: 'Koszty sprzedaży', tax: 'Podatek od zysku kapitałowego (%)', repay: 'Spłać długi powiązane z aktywem', repayHelp: 'Przypisane salda zadłużenia są potrącane z wpływów i usuwane z przyszłego długu.', destination: 'Gdzie przekazać pozostałe środki?', cash: 'Zachowaj jako dostępną gotówkę', anotherAsset: 'Dodaj do innego aktywa', investmentPlan: 'Dodaj do planu inwestycyjnego', targetAsset: 'Aktywo docelowe', targetPlan: 'Docelowy plan inwestycyjny', summary: 'Sprzedane aktywo znika ze swojej kategorii. Łączny majątek zmieniają tylko opłaty, podatek i różnica ceny.', projectedPrice: 'Wartość obliczona na dzień sprzedaży' },
  de: { title: 'Geplante Vermögensverkäufe', description: 'Verkaufen Sie eine Immobilie oder Anlage und bestimmen Sie die Verwendung des Geldes.', asset: 'Zu verkaufender Vermögenswert', date: 'Verkaufsdatum', projected: 'Prognostizierten Wert an diesem Datum verwenden', projectedHelp: 'LifeLedger entwickelt den Wert bis zum Verkauf und verwendet ihn als Bruttoverkaufspreis.', manualPrice: 'Geschätzter Bruttoverkaufspreis', costs: 'Verkaufskosten', tax: 'Steuer auf den Veräußerungsgewinn (%)', repay: 'Verknüpfte Schulden zurückzahlen', repayHelp: 'Zugeordnete Restschulden werden vom Erlös abgezogen und aus der Zukunftsrechnung entfernt.', destination: 'Wohin soll das restliche Geld fließen?', cash: 'Als verfügbare Liquidität behalten', anotherAsset: 'Einem anderen Vermögenswert hinzufügen', investmentPlan: 'Einem Anlageplan hinzufügen', targetAsset: 'Zielvermögenswert', targetPlan: 'Zielanlageplan', summary: 'Der verkaufte Vermögenswert verschwindet aus seiner Kategorie. Nur Kosten, Steuer und Preisabweichungen ändern das Gesamtvermögen.', projectedPrice: 'Am Verkaufsdatum berechneter Wert' },
  nl: { title: 'Geplande verkoop van activa', description: 'Verkoop vastgoed of een belegging en kies waar het geld naartoe gaat.', asset: 'Te verkopen actief', date: 'Verkoopdatum', projected: 'Gebruik de verwachte waarde op die datum', projectedHelp: 'LifeLedger laat de waarde groeien tot de verkoopdatum en gebruikt die als brutoverkoopprijs.', manualPrice: 'Geschatte brutoverkoopprijs', costs: 'Verkoopkosten', tax: 'Belasting op de meerwaarde (%)', repay: 'Gekoppelde schulden aflossen', repayHelp: 'Toegewezen openstaande schulden worden van de opbrengst afgetrokken en uit de toekomstige schuld verwijderd.', destination: 'Waar moet het resterende geld naartoe?', cash: 'Als beschikbare cash behouden', anotherAsset: 'Aan een ander actief toevoegen', investmentPlan: 'Aan een beleggingsplan toevoegen', targetAsset: 'Ontvangend actief', targetPlan: 'Ontvangend beleggingsplan', summary: 'Het verkochte actief verdwijnt uit zijn categorie. Alleen kosten, belasting en een prijsverschil veranderen het totale vermogen.', projectedPrice: 'Waarde berekend op de verkoopdatum' }
}

function copy(locale: Locale) { return locale === 'fr' ? translations.fr : english }
function number(value: string | boolean | undefined) { return Number(value || 0) }
function value(item: LedgerItem, key: string) { const raw = item[key]; return raw === undefined || raw === null ? '' : String(raw) }
function checked(item: LedgerItem, key: string, fallback = false) { return typeof item[key] === 'boolean' ? item[key] as boolean : fallback }
function percent(item: LedgerItem, key: string) { const raw = Number(item[key] ?? 0); return String(raw * 100) }
type ExpenseAmountChangeDraft = { effectiveOn: string; amount: string }
/** Reads the locally edited expense steps without allowing malformed draft JSON to break the form. */
function expenseAmountChanges(draft: Draft): ExpenseAmountChangeDraft[] {
  try { return JSON.parse(String(draft.amountChangesJson || '[]')) as ExpenseAmountChangeDraft[] } catch { return [] }
}
/** Selects an everyday cash direction while the API continues storing signed financial impacts. */
function eventCashDirection(amount: number) { return amount > 0 ? 'Income' : 'Expense' }
/** Applies the selected everyday direction to a positive amount before persistence. */
function signedEventAmount(value: string | boolean | undefined, direction: string | boolean | undefined) { const amount = Math.abs(number(value)); return direction === 'Income' ? amount : -amount }
/** Provides a safe initial direction when a life-event category has a conventional cash meaning. */
function defaultEventDirection(kind: string) { return kind === 'Inheritance' || kind === 'SalaryIncrease' ? 'Income' : 'Expense' }
/** Maps legacy human-readable currency names to values offered by the closed selector. */
function normaliseCurrency(currency: string) { const value = currency.trim().toUpperCase(); return ['ZLOTY', 'ZŁOTY', 'ZLOTYS', 'ZŁOTE'].includes(value) ? 'PLN' : ['EURO', 'EUROS'].includes(value) ? 'EUR' : ['DOLLAR', 'DOLLARS', 'US DOLLAR', 'US DOLLARS'].includes(value) ? 'USD' : value }
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
    { resource: 'assetSales' as const, title: assetSaleCopy[locale].title, description: assetSaleCopy[locale].description, symbol: '⇄' },
    { resource: 'events' as const, title: french ? 'Événements de vie' : 'Life events', description: french ? 'Logement, voiture, enfant, héritage ou changement de carrière.' : 'Homes, cars, children, inheritance, career changes and more.', symbol: '✦' },
  ]
}

function itemValue(item: LedgerItem, resource: Resource) {
  if (resource === 'assets') return Number(item.currentValue ?? 0) * Number(item.ownershipRate ?? 1)
  if (resource === 'liabilities') return -Number(item.outstandingBalance ?? 0) * Number(item.responsibilityRate ?? 1)
  if (resource === 'events') return item.oneOffCashImpact ?? 0
  if (resource === 'assetSales') return Number(item.grossSalePrice ?? 0)
  if (resource === 'incomes' && item.amountMode !== 'Monthly') return Number(item.annualAmount ?? 0)
  return item.monthlyAmount ?? Number(item.monthlyContribution ?? 0)
}

function money(amount: number, currency: string, locale: Locale) {
  return new Intl.NumberFormat(locale, { style: 'currency', currency, maximumFractionDigits: 0, signDisplay: amount < 0 ? 'always' : 'auto' }).format(amount)
}

/** Converts a displayed item amount through the locally cached units-per-euro rates. */
function convertMoney(amount: number, fromCurrency: string, toCurrency: string, rates: CurrencyRate[]) {
  if (fromCurrency === toCurrency) return amount
  const fromRate = rates.find((rate) => rate.code === fromCurrency)?.unitsPerEuro
  const toRate = rates.find((rate) => rate.code === toCurrency)?.unitsPerEuro
  return fromRate && toRate ? amount / fromRate * toRate : undefined
}

const assetGroupTranslations: Record<Locale, { subtotal: string; asset: string; assets: string; missingRate: string }> = {
  en: { subtotal: 'Subtotal', asset: 'asset', assets: 'assets', missingRate: 'Missing exchange rate' },
  fr: { subtotal: 'Sous-total', asset: 'actif', assets: 'actifs', missingRate: 'Taux de change manquant' },
  pl: { subtotal: 'Suma częściowa', asset: 'aktywo', assets: 'aktywa', missingRate: 'Brak kursu wymiany' },
  de: { subtotal: 'Zwischensumme', asset: 'Vermögenswert', assets: 'Vermögenswerte', missingRate: 'Wechselkurs fehlt' },
  nl: { subtotal: 'Subtotaal', asset: 'bezitting', assets: 'bezittingen', missingRate: 'Wisselkoers ontbreekt' },
}

/** Renders one editable ledger entry with its value in both original and profile currency. */
function PlannerItemRow({ item, resource, currency, locale, rates, categoryLabel, editLabel, removeLabel, monthlyContributionLabel, onEdit, onDelete }: { item: LedgerItem; resource: Resource; currency: string; locale: Locale; rates: CurrencyRate[]; categoryLabel?: string; editLabel: string; removeLabel: string; monthlyContributionLabel: string; onEdit: () => void; onDelete: () => void }) {
  const amount = Number(itemValue(item, resource))
  const itemCurrency = normaliseCurrency(String(item.currency ?? currency))
  const convertedAmount = convertMoney(amount, itemCurrency, currency, rates)
  const annualIncome = resource === 'incomes' && item.amountMode !== 'Monthly'
  const projectedSale = resource === 'assetSales' && Boolean(item.useProjectedValue)
  const baseDetail = categoryLabel ?? item.kind ?? (resource === 'investments' ? monthlyContributionLabel : resource === 'assetSales' ? new Date(String(item.happensOn)).toLocaleDateString(locale) : '')
  const personalShare = resource === 'assets' && Number(item.ownershipRate ?? 1) < 1
    ? `${Math.round(Number(item.ownershipRate) * 100)} % ${locale === 'fr' ? 'vous appartient' : 'owned by you'}`
    : resource === 'liabilities' && Number(item.responsibilityRate ?? 1) < 1
      ? `${Math.round(Number(item.responsibilityRate) * 100)} % ${locale === 'fr' ? 'à votre charge' : 'owed by you'}`
      : ''
  const detail = [baseDetail, personalShare].filter(Boolean).join(' · ')
  const displayedAmount = projectedSale ? assetSaleCopy[locale].projectedPrice : money(amount, itemCurrency, locale)

  return <div className="flex flex-col gap-3 py-3 sm:flex-row sm:items-center sm:justify-between sm:gap-4"><button className="min-w-0 text-left" onClick={onEdit}><p className="truncate text-sm font-medium text-mist">{item.name}</p><p className="mt-0.5 text-xs text-muted">{detail}{item.ticker ? ` · ${String(item.ticker)}` : ''}</p></button><div className="flex items-center justify-between gap-3 sm:shrink-0 sm:justify-end"><div className="text-left sm:text-right"><p className="text-sm text-sky">{displayedAmount}{annualIncome ? (locale === 'fr' ? ' / an' : ' / year') : ''}</p>{!projectedSale && itemCurrency !== currency && convertedAmount !== undefined && <p className="mt-0.5 text-xs text-muted">≈ {money(convertedAmount, currency, locale)}</p>}</div><div className="flex items-center gap-3"><button className="text-xs text-muted transition hover:text-mist" onClick={onEdit}>{editLabel}</button><button className="text-xs text-muted transition hover:text-danger" onClick={onDelete}>{removeLabel}</button></div></div></div>
}

/** Groups assets by built-in or personal category and calculates each subtotal in the profile currency. */
function AssetCategoryGroups({ assets, categories, currency, locale, rates, editLabel, removeLabel, monthlyContributionLabel, onEdit, onDelete }: { assets: LedgerItem[]; categories: AssetCategory[]; currency: string; locale: Locale; rates: CurrencyRate[]; editLabel: string; removeLabel: string; monthlyContributionLabel: string; onEdit: (item: LedgerItem) => void; onDelete: (item: LedgerItem) => void }) {
  const groupCopy = assetGroupTranslations[locale]
  const groups = Array.from(assets.reduce((result, asset) => {
    const customCategory = String(asset.customCategory ?? '').trim()
    const key = customCategory ? `custom:${customCategory.toLocaleLowerCase()}` : `builtin:${String(asset.kind ?? 'Other')}`
    const existing = result.get(key) ?? { key, label: assetCategoryLabel(locale, asset, categories), assets: [] as LedgerItem[], subtotal: 0, missingRate: false }
    const assetCurrency = normaliseCurrency(String(asset.currency ?? currency))
    const convertedValue = convertMoney(Number(asset.currentValue ?? 0) * Number(asset.ownershipRate ?? 1), assetCurrency, currency, rates)

    // A partial subtotal could look correct while silently excluding an unsupported currency.
    existing.missingRate ||= convertedValue === undefined
    existing.subtotal += convertedValue ?? 0
    existing.assets.push(asset)
    result.set(key, existing)
    return result
  }, new Map<string, { key: string; label: string; assets: LedgerItem[]; subtotal: number; missingRate: boolean }>()).values())
    .sort((left, right) => left.missingRate === right.missingRate ? right.subtotal - left.subtotal : left.missingRate ? 1 : -1)

  return <div className="mt-5 space-y-3">{groups.map((group) => <section className="overflow-hidden rounded-2xl border border-white/10 bg-white/[0.035]" key={group.key}><header className="flex items-start justify-between gap-4 border-b border-white/10 px-4 py-3"><div className="min-w-0"><h3 className="truncate text-sm font-semibold text-mist">{group.label}</h3><p className="mt-1 text-xs text-muted">{group.assets.length} {group.assets.length === 1 ? groupCopy.asset : groupCopy.assets}</p></div><div className="shrink-0 text-right"><p className="text-xs text-muted">{groupCopy.subtotal}</p>{group.missingRate ? <p className="mt-1 text-xs font-medium text-warning">{groupCopy.missingRate}</p> : <p className="mt-1 text-base font-semibold text-sky">{money(group.subtotal, currency, locale)}</p>}</div></header><div className="divide-y divide-white/10 px-4">{group.assets.sort((left, right) => Number(right.currentValue ?? 0) - Number(left.currentValue ?? 0)).map((asset) => <PlannerItemRow categoryLabel={assetCategoryLabel(locale, asset, categories)} currency={currency} editLabel={editLabel} item={asset} key={asset.id} locale={locale} monthlyContributionLabel={monthlyContributionLabel} rates={rates} removeLabel={removeLabel} resource="assets" onDelete={() => onDelete(asset)} onEdit={() => onEdit(asset)} />)}</div></section>)}</div>
}

function newDraft(resource: Resource, currency: string): Draft {
  const base = { name: '', currency, startsOn: today(), endsOn: '' }
  if (resource === 'incomes') return { ...base, kind: 'Salary', amountMode: 'Monthly', monthlyAmount: '', annualAmount: '', useSeasonality: false, linkedAssetId: '', annualGrowthRate: '2', isTaxable: true, taxRate: '0', taxCountryCode: '', ...Object.fromEntries(calendarMonths.map((month) => [`season:${month}`, '8.3333'])) }
  if (resource === 'assets') return { ...base, kind: 'Cash', assetCategory: 'builtin:Cash', customCategory: '', currentValue: '', ownershipRate: '100', purchasePrice: '0', acquisitionCosts: '0', purchasedOn: '', valuedOn: today(), valuationSource: '', profileDefinitionKey: '', profileDefinitionVersion: '', ticker: '', quantity: '0', capitalGainsTaxRate: '0', capitalGainsTaxCountryCode: '', expectedAnnualReturn: '0', volatility: '0', isLiquid: true, isIncludedInPortfolioAllocation: true }
  if (resource === 'liabilities') return { ...base, kind: 'Mortgage', outstandingBalance: '', responsibilityRate: '100', linkedAssetId: '', interestRate: '4', monthlyPayment: '', paidOffOn: '' }
  if (resource === 'expenses') return { ...base, kind: 'Recurring', monthlyAmount: '', frequency: 'Monthly', endsOn: fiftyYearsFromToday(), indexedToInflation: true, amountChangesJson: '[]', saveInAdvance: false, savingsStartsOn: today() }
  if (resource === 'investments') return { ...base, monthlyContribution: '', expectedAnnualReturn: '6' }
  if (resource === 'assetSales') return { ...base, assetId: '', happensOn: today(), useProjectedValue: true, grossSalePrice: '', sellingCosts: '0', capitalGainsTaxRate: '0', capitalGainsTaxCountryCode: '', repayLinkedLiabilities: true, destination: 'Cash', destinationAssetId: '', destinationInvestmentPlanId: '', notes: '' }
  return { ...base, kind: 'Custom', happensOn: today(), repeats: false, recurrenceFrequency: 'Monthly', recurrenceEndsOn: fiftyYearsFromToday(), cashFlowDirection: 'Expense', oneOffCashImpact: '', monthlyCashImpact: '0', durationMonths: '0', notes: '' }
}

function editDraft(resource: Resource, item: LedgerItem, currency: string, assets: LedgerItem[] = []): Draft {
  const base = { name: value(item, 'name'), currency: normaliseCurrency(value(item, 'currency') || currency), startsOn: value(item, 'startsOn'), endsOn: value(item, 'endsOn') }
  if (resource === 'incomes') {
    const storedMode = value(item, 'amountMode') || 'Monthly'
    const allocations = Array.isArray(item.monthlyAllocations) ? item.monthlyAllocations as Array<{ month: number; share: number }> : []
    return { ...base, kind: value(item, 'kind'), amountMode: storedMode === 'Seasonal' ? 'Annual' : storedMode, monthlyAmount: value(item, 'monthlyAmount'), annualAmount: value(item, 'annualAmount'), useSeasonality: storedMode === 'Seasonal', linkedAssetId: value(item, 'linkedAssetId'), annualGrowthRate: percent(item, 'annualGrowthRate'), isTaxable: checked(item, 'isTaxable', true), taxRate: percent(item, 'taxRate'), taxCountryCode: value(item, 'taxCountryCode'), ...Object.fromEntries(calendarMonths.map((month) => [`season:${month}`, String((allocations.find((allocation) => allocation.month === month)?.share ?? 0) * 100)])) }
  }
  if (resource === 'assets') {
    const profile = item.characteristicProfile as { definitionKey?: string; definitionVersion?: number; valuesJson?: string } | undefined
    let profileValues: Record<string, unknown> = {}
    try { profileValues = profile?.valuesJson ? JSON.parse(profile.valuesJson) as Record<string, unknown> : {} } catch { profileValues = {} }
    const links = Array.isArray(item.liabilityLinks) ? item.liabilityLinks as Array<{ liabilityId: string; allocationRate: number }> : []
    return {
      ...base, kind: value(item, 'kind'), assetCategory: item.customCategory ? `custom:${String(item.customCategory)}` : `builtin:${value(item, 'kind') || 'Other'}`, customCategory: value(item, 'customCategory'),
      currentValue: value(item, 'currentValue'), ownershipRate: item.ownershipRate == null ? '100' : percent(item, 'ownershipRate'), purchasePrice: value(item, 'purchasePrice'), acquisitionCosts: value(item, 'acquisitionCosts'), purchasedOn: value(item, 'purchasedOn'), valuedOn: value(item, 'valuedOn'), valuationSource: value(item, 'valuationSource'),
      profileDefinitionKey: profile?.definitionKey ?? '', profileDefinitionVersion: String(profile?.definitionVersion ?? ''),
      ...Object.fromEntries(Object.entries(profileValues).map(([key, entry]) => [`profile:${key}`, typeof entry === 'boolean' ? entry : String(entry ?? '')])),
      ...Object.fromEntries(links.map((link) => [`liability:${link.liabilityId}`, String(link.allocationRate * 100)])),
      ticker: value(item, 'ticker'), quantity: value(item, 'quantity'), capitalGainsTaxRate: percent(item, 'capitalGainsTaxRate'), capitalGainsTaxCountryCode: value(item, 'capitalGainsTaxCountryCode'), expectedAnnualReturn: percent(item, 'expectedAnnualReturn'), volatility: percent(item, 'volatility'), isLiquid: checked(item, 'isLiquid', true), isIncludedInPortfolioAllocation: checked(item, 'isIncludedInPortfolioAllocation', true)
    }
  }
  if (resource === 'liabilities') {
    const linkedAsset = assets.find((asset) => Array.isArray(asset.liabilityLinks) && (asset.liabilityLinks as Array<{ liabilityId: string }>).some((link) => link.liabilityId === item.id))
    return { ...base, kind: value(item, 'kind'), outstandingBalance: value(item, 'outstandingBalance'), responsibilityRate: item.responsibilityRate == null ? '100' : percent(item, 'responsibilityRate'), linkedAssetId: linkedAsset?.id ?? '', interestRate: percent(item, 'interestRate'), monthlyPayment: value(item, 'monthlyPayment'), paidOffOn: value(item, 'paidOffOn') }
  }
  if (resource === 'expenses') {
    const changes = Array.isArray(item.amountChanges) ? item.amountChanges as Array<{ effectiveOn: string; amount: number }> : []
    return { ...base, kind: value(item, 'kind'), monthlyAmount: value(item, 'monthlyAmount'), frequency: value(item, 'frequency') || 'Monthly', linkedAssetId: value(item, 'linkedAssetId'), indexedToInflation: checked(item, 'indexedToInflation', true), amountChangesJson: JSON.stringify(changes.map((change) => ({ effectiveOn: change.effectiveOn, amount: String(change.amount) }))), saveInAdvance: checked(item, 'saveInAdvance'), savingsStartsOn: value(item, 'savingsStartsOn') || today() }
  }
  if (resource === 'investments') return { ...base, monthlyContribution: value(item, 'monthlyContribution'), expectedAnnualReturn: percent(item, 'expectedAnnualReturn') }
  if (resource === 'assetSales') return { ...base, assetId: value(item, 'assetId'), happensOn: value(item, 'happensOn'), useProjectedValue: checked(item, 'useProjectedValue', true), grossSalePrice: value(item, 'grossSalePrice'), sellingCosts: value(item, 'sellingCosts'), capitalGainsTaxRate: percent(item, 'capitalGainsTaxRate'), capitalGainsTaxCountryCode: value(item, 'capitalGainsTaxCountryCode'), repayLinkedLiabilities: checked(item, 'repayLinkedLiabilities', true), destination: value(item, 'destination') || 'Cash', destinationAssetId: value(item, 'destinationAssetId'), destinationInvestmentPlanId: value(item, 'destinationInvestmentPlanId'), notes: value(item, 'notes') }
  return { ...base, kind: value(item, 'kind'), happensOn: value(item, 'happensOn'), repeats: item.recurrenceFrequency !== undefined && item.recurrenceFrequency !== null, recurrenceFrequency: value(item, 'recurrenceFrequency') || 'Monthly', recurrenceEndsOn: value(item, 'recurrenceEndsOn') || fiftyYearsFromToday(), cashFlowDirection: eventCashDirection(Number(item.oneOffCashImpact || item.monthlyCashImpact || 0)), oneOffCashImpact: String(Math.abs(Number(item.oneOffCashImpact ?? 0))), monthlyCashImpact: String(Math.abs(Number(item.monthlyCashImpact ?? 0))), durationMonths: value(item, 'durationMonths'), notes: value(item, 'notes') }
}

function payload(resource: Resource, draft: Draft, profileDefinitions: AssetProfileDefinition[]) {
  const common = { name: String(draft.name).trim(), currency: String(draft.currency).toUpperCase() }
  const dates = { startsOn: String(draft.startsOn || today()), endsOn: draft.endsOn ? String(draft.endsOn) : null }
  if (resource === 'incomes') {
    const usesAnnualAmount = draft.amountMode === 'Annual'
    const seasonal = usesAnnualAmount && Boolean(draft.useSeasonality)
    const annualAmount = usesAnnualAmount ? number(draft.annualAmount) : number(draft.monthlyAmount) * 12
    return { ...common, ...dates, kind: draft.kind, amountMode: seasonal ? 'Seasonal' : draft.amountMode, monthlyAmount: usesAnnualAmount ? annualAmount / 12 : number(draft.monthlyAmount), annualAmount, linkedAssetId: String(draft.linkedAssetId || '') || null, monthlyAllocations: seasonal ? calendarMonths.map((month) => ({ month, share: number(draft[`season:${month}`]) / 100 })) : [], annualGrowthRate: number(draft.annualGrowthRate) / 100, isTaxable: Boolean(draft.isTaxable), taxRate: number(draft.taxRate) / 100, taxCountryCode: String(draft.taxCountryCode || '').trim().toUpperCase() || null }
  }
  if (resource === 'assets') {
    const definition = profileDefinitions.find((candidate) => candidate.key === draft.profileDefinitionKey)
    const profileValues = Object.fromEntries((definition?.fields ?? []).flatMap((field) => {
      const raw = draft[`profile:${field.key}`]
      if (raw === undefined || raw === '') return []
      const numeric = ['Number', 'Area', 'Distance', 'Condition'].includes(field.type)
      return [[field.key, field.type === 'Boolean' ? Boolean(raw) : numeric ? number(raw) : String(raw)]]
    }))
    const liabilityAllocations = Object.entries(draft).filter(([key, raw]) => key.startsWith('liability:') && number(raw) > 0).map(([key, raw]) => ({ liabilityId: key.slice('liability:'.length), allocationRate: number(raw) / 100 }))
    return {
      ...common, kind: draft.kind, customCategory: String(draft.customCategory || '').trim() || null,
      currentValue: number(draft.currentValue), ownershipRate: number(draft.ownershipRate) / 100, purchasePrice: number(draft.purchasePrice), acquisitionCosts: number(draft.acquisitionCosts), purchasedOn: draft.purchasedOn ? String(draft.purchasedOn) : null, valuedOn: draft.valuedOn ? String(draft.valuedOn) : null, valuationSource: String(draft.valuationSource || '').trim() || null,
      profileDefinitionKey: definition?.key ?? null, profileDefinitionVersion: definition?.version ?? null, profileValues: definition ? profileValues : null, liabilityAllocations,
      ticker: String(draft.ticker || '').trim().toUpperCase() || null, quantity: number(draft.quantity), capitalGainsTaxRate: number(draft.capitalGainsTaxRate) / 100, capitalGainsTaxCountryCode: String(draft.capitalGainsTaxCountryCode || '').trim().toUpperCase() || null, expectedAnnualReturn: number(draft.expectedAnnualReturn) / 100, volatility: number(draft.volatility) / 100, isLiquid: Boolean(draft.isLiquid), isIncludedInPortfolioAllocation: Boolean(draft.isIncludedInPortfolioAllocation)
    }
  }
  if (resource === 'liabilities') return { ...common, kind: draft.kind, outstandingBalance: number(draft.outstandingBalance), responsibilityRate: number(draft.responsibilityRate) / 100, interestRate: number(draft.interestRate) / 100, monthlyPayment: number(draft.monthlyPayment), paidOffOn: draft.paidOffOn ? String(draft.paidOffOn) : null, assetAllocations: draft.linkedAssetId ? [{ assetId: String(draft.linkedAssetId), allocationRate: 1 }] : [] }
  if (resource === 'expenses') return { ...common, ...dates, kind: draft.kind, frequency: draft.frequency || 'Monthly', monthlyAmount: number(draft.monthlyAmount), linkedAssetId: String(draft.linkedAssetId || '') || null, indexedToInflation: Boolean(draft.indexedToInflation), amountChanges: draft.kind === 'Recurring' ? expenseAmountChanges(draft).filter((change) => change.effectiveOn && number(change.amount) >= 0).map((change) => ({ effectiveOn: change.effectiveOn, amount: number(change.amount) })) : [], saveInAdvance: draft.kind === 'Exceptional' && Boolean(draft.saveInAdvance), savingsStartsOn: draft.kind === 'Exceptional' && draft.saveInAdvance ? String(draft.savingsStartsOn || today()) : null }
  if (resource === 'investments') return { ...common, ...dates, monthlyContribution: number(draft.monthlyContribution), expectedAnnualReturn: number(draft.expectedAnnualReturn) / 100 }
  if (resource === 'assetSales') return { ...common, assetId: String(draft.assetId || ''), happensOn: String(draft.happensOn || today()), useProjectedValue: Boolean(draft.useProjectedValue), grossSalePrice: number(draft.grossSalePrice), sellingCosts: number(draft.sellingCosts), capitalGainsTaxRate: number(draft.capitalGainsTaxRate) / 100, capitalGainsTaxCountryCode: String(draft.capitalGainsTaxCountryCode || '').trim().toUpperCase() || null, repayLinkedLiabilities: Boolean(draft.repayLinkedLiabilities), destination: String(draft.destination || 'Cash'), destinationAssetId: draft.destination === 'Asset' ? String(draft.destinationAssetId || '') || null : null, destinationInvestmentPlanId: draft.destination === 'InvestmentPlan' ? String(draft.destinationInvestmentPlanId || '') || null : null, notes: String(draft.notes || '') }
  return { ...common, kind: draft.kind, happensOn: String(draft.happensOn || today()), recurrenceFrequency: draft.repeats ? draft.recurrenceFrequency || 'Monthly' : null, recurrenceEndsOn: draft.repeats ? String(draft.recurrenceEndsOn || fiftyYearsFromToday()) : null, oneOffCashImpact: signedEventAmount(draft.oneOffCashImpact, draft.cashFlowDirection), monthlyCashImpact: draft.repeats ? 0 : signedEventAmount(draft.monthlyCashImpact, draft.cashFlowDirection), durationMonths: draft.repeats ? 0 : number(draft.durationMonths), notes: String(draft.notes || '') }
}

export function Planner({ data, assetCategories, profileDefinitions, currency, locale, onCreate, onUpdate, onDelete, onImportBank }: PlannerProps) {
  const [active, setActive] = useState<{ resource: Resource; item?: LedgerItem } | null>(null)
  const [draft, setDraft] = useState<Draft>({})
  const [submitting, setSubmitting] = useState(false)
  const [rates, setRates] = useState<CurrencyRate[]>([])
  const [refreshingRates, setRefreshingRates] = useState(false)
  const t = copy(locale)
  const definitions = useMemo(() => resourceDefinitions(locale), [locale])
  const availableCurrencies = useMemo(() => Array.from(new Set(['EUR', 'USD', 'PLN', 'GBP', 'CHF', 'CAD', 'JPY', ...rates.map((rate) => rate.code), currency])).sort(), [rates, currency])

  useEffect(() => { void api.currencies().then(setRates).catch(() => setRates([])) }, [])
  async function refreshRates() { setRefreshingRates(true); try { setRates(await api.refreshCurrencies()) } finally { setRefreshingRates(false) } }

  function open(resource: Resource, item?: LedgerItem) { setActive({ resource, item }); setDraft(item ? editDraft(resource, item, currency, data.assets) : newDraft(resource, currency)) }
  function setField(name: string, next: string | boolean) {
    setDraft((current) => {
      if (active?.resource === 'assets' && name === 'assetCategory' && typeof next === 'string') {
        const isCustom = next.startsWith('custom:')
        const kind = isCustom ? 'Other' : next.slice('builtin:'.length)
        const customCategory = isCustom ? next.slice('custom:'.length) : ''
        const homeProfile = kind === 'RealEstate' && !current.profileDefinitionKey ? profileDefinitions.find((definition) => definition.key === 'home') : undefined
        const updated = { ...current, assetCategory: next, kind, customCategory, ...(homeProfile ? { profileDefinitionKey: homeProfile.key, profileDefinitionVersion: String(homeProfile.version) } : {}) }
        return kind === 'Cash' ? { ...updated, expectedAnnualReturn: '0', volatility: '0', isLiquid: true } : updated
      }
      if (active?.resource === 'assets' && name === 'profileDefinitionKey' && typeof next === 'string') {
        const definition = profileDefinitions.find((candidate) => candidate.key === next)
        const retained = Object.fromEntries(Object.entries(current).filter(([key]) => !key.startsWith('profile:')))
        return { ...retained, profileDefinitionKey: next, profileDefinitionVersion: definition ? String(definition.version) : '' }
      }
      if (active?.resource === 'assets' && name === 'kind' && next === 'Cash') {
        return { ...current, kind: 'Cash', expectedAnnualReturn: '0', volatility: '0', isLiquid: true }
      }
      if (active?.resource === 'expenses' && name === 'kind' && next === 'Exceptional') {
        return { ...current, kind: 'Exceptional', endsOn: '', saveInAdvance: true, savingsStartsOn: String(current.savingsStartsOn || today()) }
      }
      if (active?.resource === 'events' && name === 'kind' && typeof next === 'string') {
        if (next === 'VehiclePurchase') return { ...current, kind: next, repeats: true, recurrenceFrequency: 'EveryFiveYears', cashFlowDirection: 'Expense', recurrenceEndsOn: String(current.recurrenceEndsOn || fiftyYearsFromToday()) }
        return { ...current, kind: next, cashFlowDirection: defaultEventDirection(next) }
      }
      if (active?.resource === 'assetSales' && name === 'destination' && typeof next === 'string') {
        return { ...current, destination: next, destinationAssetId: '', destinationInvestmentPlanId: '' }
      }
      return { ...current, [name]: next }
    })
  }
  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!active || !String(draft.name).trim()) return
    setSubmitting(true)
    try { active.item ? await onUpdate(active.resource, active.item.id, payload(active.resource, draft, profileDefinitions)) : await onCreate(active.resource, payload(active.resource, draft, profileDefinitions)); setActive(null) }
    finally { setSubmitting(false) }
  }

  return <section className="space-y-5">
    <div><p className="eyebrow">{t.eyebrow}</p><h1 className="mt-2 text-3xl font-semibold text-white">{t.title}</h1><p className="mt-2 max-w-2xl text-sm leading-6 text-muted">{t.intro}</p></div>
    <article className="section-card"><div className="flex flex-col justify-between gap-4 sm:flex-row sm:items-center"><div><p className="eyebrow">{locale === 'fr' ? 'Devises' : 'Currencies'}</p><p className="mt-1 text-sm text-muted">{locale === 'fr' ? 'Taux enregistrés localement, exprimés pour 1 EUR.' : 'Rates cached locally, quoted per 1 EUR.'}</p></div><button className="ghost-button" disabled={refreshingRates} onClick={() => void refreshRates()}>{refreshingRates ? (locale === 'fr' ? 'Actualisation…' : 'Refreshing…') : (locale === 'fr' ? 'Actualiser les taux BCE' : 'Refresh ECB rates')}</button></div><div className="mt-4 flex flex-wrap gap-2">{rates.slice(0, 8).map((rate) => <span className="rounded-xl bg-white/5 px-3 py-2 text-xs text-mist" key={rate.code}>{rate.code} · {rate.unitsPerEuro.toFixed(4)} {rate.isStale ? '⚠' : ''}</span>)}</div></article>
    <div className="grid gap-4 xl:grid-cols-2">{definitions.map((definition) => <article className="section-card" key={definition.resource}>
      <div className="flex items-start justify-between gap-4"><div><span className="grid h-9 w-9 place-items-center rounded-xl bg-white/10 text-sky">{definition.symbol}</span><h2 className="mt-4 text-lg font-semibold text-white">{definition.title}</h2><p className="mt-1 text-sm leading-5 text-muted">{definition.description}</p></div><div className="flex shrink-0 flex-col gap-2 sm:flex-row">{definition.resource === 'expenses' && <button className="ghost-button" onClick={onImportBank}>{locale === 'fr' ? 'Importer une banque' : 'Import bank'}</button>}<button className="ghost-button" onClick={() => open(definition.resource)}>{t.add}</button></div></div>
      {data[definition.resource].length === 0
        ? <p className="mt-5 border-y border-white/10 py-4 text-sm text-muted">{t.none}</p>
        : definition.resource === 'assets'
          ? <AssetCategoryGroups assets={data.assets} categories={assetCategories} currency={currency} editLabel={t.edit} locale={locale} monthlyContributionLabel={t.monthlyContribution} rates={rates} removeLabel={t.remove} onDelete={(item) => void onDelete('assets', item.id)} onEdit={(item) => open('assets', item)} />
          : <div className="mt-5 divide-y divide-white/10 border-y border-white/10">{data[definition.resource].map((item) => <PlannerItemRow currency={currency} editLabel={t.edit} item={item} key={item.id} locale={locale} monthlyContributionLabel={t.monthlyContribution} rates={rates} removeLabel={t.remove} resource={definition.resource} onDelete={() => void onDelete(definition.resource, item.id)} onEdit={() => open(definition.resource, item)} />)}</div>}
    </article>)}</div>
    {active && <Editor assetId={active.item?.id} resource={active.resource} draft={draft} currencies={availableCurrencies} rates={rates} assetCategories={assetCategories} profileDefinitions={profileDefinitions} assets={data.assets} liabilities={data.liabilities} investments={data.investments} locale={locale} submitting={submitting} editing={Boolean(active.item)} onField={setField} onCancel={() => setActive(null)} onSubmit={submit} />}
  </section>
}

function Editor({ assetId, resource, draft, currencies, rates, assetCategories, profileDefinitions, assets, liabilities, investments, locale, submitting, editing, onField, onCancel, onSubmit }: { assetId?: string; resource: Resource; draft: Draft; currencies: string[]; rates: CurrencyRate[]; assetCategories: AssetCategory[]; profileDefinitions: AssetProfileDefinition[]; assets: LedgerItem[]; liabilities: LedgerItem[]; investments: LedgerItem[]; locale: Locale; submitting: boolean; editing: boolean; onField: (name: string, value: string | boolean) => void; onCancel: () => void; onSubmit: (event: FormEvent) => Promise<void> }) {
  const t = copy(locale)
  const evolutionCopy = expenseEvolutionCopy[locale]
  const futureExpenseAmounts = expenseAmountChanges(draft)
  const setFutureExpenseAmounts = (changes: ExpenseAmountChangeDraft[]) => onField('amountChangesJson', JSON.stringify(changes))
  const addFutureExpenseAmount = () => {
    const previousDate = futureExpenseAmounts.at(-1)?.effectiveOn || String(draft.startsOn || today())
    setFutureExpenseAmounts([...futureExpenseAmounts, { effectiveOn: fiveYearsAfter(previousDate), amount: String(draft.monthlyAmount || '') }])
  }
  const updateFutureExpenseAmount = (index: number, fieldName: keyof ExpenseAmountChangeDraft, next: string) => setFutureExpenseAmounts(futureExpenseAmounts.map((change, changeIndex) => changeIndex === index ? { ...change, [fieldName]: next } : change))
  const removeFutureExpenseAmount = (index: number) => setFutureExpenseAmounts(futureExpenseAmounts.filter((_, changeIndex) => changeIndex !== index))
  const field = (name: string, label: string, type = 'text', required = false, alignLabel = false) => type === 'date'
    ? <DateField label={label} locale={locale} required={required} value={String(draft[name] ?? '')} onChange={(next) => onField(name, next)} />
    : <label className={`block text-sm text-mist ${alignLabel ? 'flex min-w-0 flex-col' : ''}`}><span className={alignLabel ? 'flex min-h-10 items-end' : ''}>{label}</span><input className="field mt-2" max={name === 'responsibilityRate' ? 100 : undefined} min={type === 'number' ? 0 : undefined} required={required} step={type === 'number' ? 'any' : undefined} type={type} value={String(draft[name] ?? '')} onChange={(event) => onField(name, event.target.value)} /></label>
  const select = (name: string, label: string, options: string[]) => <label className="block text-sm text-mist">{label}<select className="field mt-2" value={String(draft[name] ?? '')} onChange={(event) => onField(name, event.target.value)}>{options.map((option) => <option className="bg-panel" key={option}>{option}</option>)}</select></label>
  const frequencyOptions = frequencyOptionsByLocale[locale]
  const frequencySelect = <label className="block text-sm text-mist">{t.frequency}<select className="field mt-2" value={String(draft.frequency ?? 'Monthly')} onChange={(event) => onField('frequency', event.target.value)}>{frequencyOptions.map((option) => <option className="bg-panel" key={option.value} value={option.value}>{option.label}</option>)}</select></label>
  const recurrenceFrequencySelect = <label className="block text-sm text-mist">{t.frequency}<select className="field mt-2" value={String(draft.recurrenceFrequency ?? 'Monthly')} onChange={(event) => onField('recurrenceFrequency', event.target.value)}>{frequencyOptions.map((option) => <option className="bg-panel" key={option.value} value={option.value}>{option.label}</option>)}</select></label>
  const eventKindOptions = eventKindOptionsByLocale[locale]
  const eventKindSelect = <label className="block text-sm text-mist">{t.category}<select className="field mt-2" value={String(draft.kind ?? 'Custom')} onChange={(event) => onField('kind', event.target.value)}>{eventKindOptions.map((option) => <option className="bg-panel" key={option.value} value={option.value}>{option.label}</option>)}</select></label>
  const eventCurrency = String(draft.currency || 'EUR')
  const eventDirectionSelect = <label className="block text-sm text-mist">{locale === 'fr' ? 'Effet sur votre argent' : 'Effect on your money'}<select className="field mt-2" value={String(draft.cashFlowDirection ?? 'Expense')} onChange={(event) => onField('cashFlowDirection', event.target.value)}><option className="bg-panel" value="Expense">{locale === 'fr' ? 'Une dépense — le montant sera retiré' : 'An expense — the amount will be deducted'}</option><option className="bg-panel" value="Income">{locale === 'fr' ? 'Une entrée d’argent — le montant sera ajouté' : 'Money received — the amount will be added'}</option></select></label>
  const eventAmountLabel = draft.kind === 'VehiclePurchase'
    ? (locale === 'fr' ? `Coût à chaque achat (${eventCurrency})` : `Cost of each purchase (${eventCurrency})`)
    : draft.repeats
      ? (locale === 'fr' ? `Montant à chaque occurrence (${eventCurrency})` : `Amount each time (${eventCurrency})`)
      : (locale === 'fr' ? `Montant ponctuel (${eventCurrency})` : `One-time amount (${eventCurrency})`)
  const eventMonthlyLabel = locale === 'fr' ? `Montant mensuel (${eventCurrency})` : `Monthly amount (${eventCurrency})`
  const toggle = (name: string, label: string, help?: string) => <label className="flex items-start gap-3 rounded-xl border border-white/15 bg-white/5 px-3 py-3 text-sm text-mist"><input className="mt-1" checked={Boolean(draft[name])} type="checkbox" onChange={(event) => onField(name, event.target.checked)} /><span><span className="block">{label}</span>{help && <span className="mt-1 block text-xs leading-5 text-muted">{help}</span>}</span></label>
  const currencyCombobox = <label className="block text-sm text-mist">{t.currency}<select className="field mt-2" required value={String(draft.currency ?? '')} onChange={(event) => onField('currency', event.target.value)}>{currencies.map((code) => <option className="bg-panel" key={code} value={code}>{code}</option>)}</select></label>
  const assetCategorySelect = <label className="block text-sm text-mist">{t.category}<select className="field mt-2" value={String(draft.assetCategory ?? 'builtin:Other')} onChange={(event) => onField('assetCategory', event.target.value)}><optgroup label={locale === 'fr' ? 'Catégories intégrées' : 'Built-in categories'}>{builtInAssetKinds.map((kind) => <option className="bg-panel" key={kind} value={`builtin:${kind}`}>{assetKindLabel(locale, kind)}</option>)}</optgroup>{assetCategories.length > 0 && <optgroup label={locale === 'fr' ? 'Mes catégories' : 'My categories'}>{assetCategories.map((category) => <option className="bg-panel" key={category.name} value={`custom:${category.name}`}>{category.name}</option>)}</optgroup>}</select></label>
  const countryField = (name: string, label: string) => <label className="block text-sm text-mist">{label}<input className="field mt-2" list="lifeledger-tax-countries" value={String(draft[name] ?? '')} onChange={(event) => onField(name, event.target.value.toUpperCase())} /><datalist id="lifeledger-tax-countries"><option value="PL">Pologne</option><option value="FR">France</option><option value="BE">Belgique</option><option value="DE">Allemagne</option><option value="NL">Pays-Bas</option><option value="US">États-Unis</option></datalist></label>
  const assetLinkSelect = (realEstateOnly = false) => {
    const choices = realEstateOnly ? assets.filter((asset) => asset.kind === 'RealEstate') : assets
    return <label className="block text-sm text-mist">{t.linkedAsset}<select className="field mt-2" value={String(draft.linkedAssetId ?? '')} onChange={(event) => onField('linkedAssetId', event.target.value)}><option className="bg-panel" value="">{t.noLinkedAsset}</option>{choices.map((asset) => <option className="bg-panel" key={asset.id} value={asset.id}>{asset.name}</option>)}</select></label>
  }
  const financedAssetSelect = <label className="block text-sm text-mist">{locale === 'fr' ? 'Bien financé par cette dette' : 'Asset financed by this debt'}<select className="field mt-2" value={String(draft.linkedAssetId ?? '')} onChange={(event) => onField('linkedAssetId', event.target.value)}><option className="bg-panel" value="">{locale === 'fr' ? 'Aucun bien lié' : 'No linked asset'}</option>{assets.map((asset) => <option className="bg-panel" key={asset.id} value={asset.id}>{asset.name}</option>)}</select><span className="mt-2 block text-xs leading-5 text-muted">{locale === 'fr' ? 'Cette liaison permet notamment de rembourser le bon crédit lors de la vente du bien.' : 'This relationship makes it possible to repay the right debt when the asset is sold.'}</span></label>
  const saleCopy = assetSaleCopy[locale]
  const saleAssetSelect = <label className="block text-sm text-mist">{saleCopy.asset}<select className="field mt-2" required value={String(draft.assetId ?? '')} onChange={(event) => onField('assetId', event.target.value)}><option className="bg-panel" value="">—</option>{assets.map((asset) => <option className="bg-panel" key={asset.id} value={asset.id}>{asset.name}</option>)}</select></label>
  const saleDestinationSelect = <label className="block text-sm text-mist">{saleCopy.destination}<select className="field mt-2" value={String(draft.destination ?? 'Cash')} onChange={(event) => onField('destination', event.target.value)}><option className="bg-panel" value="Cash">{saleCopy.cash}</option><option className="bg-panel" value="Asset">{saleCopy.anotherAsset}</option><option className="bg-panel" value="InvestmentPlan">{saleCopy.investmentPlan}</option></select></label>
  const destinationAssetSelect = <label className="block text-sm text-mist">{saleCopy.targetAsset}<select className="field mt-2" required value={String(draft.destinationAssetId ?? '')} onChange={(event) => onField('destinationAssetId', event.target.value)}><option className="bg-panel" value="">—</option>{assets.filter((asset) => asset.id !== draft.assetId).map((asset) => <option className="bg-panel" key={asset.id} value={asset.id}>{asset.name}</option>)}</select></label>
  const destinationPlanSelect = <label className="block text-sm text-mist">{saleCopy.targetPlan}<select className="field mt-2" required value={String(draft.destinationInvestmentPlanId ?? '')} onChange={(event) => onField('destinationInvestmentPlanId', event.target.value)}><option className="bg-panel" value="">—</option>{investments.map((plan) => <option className="bg-panel" key={plan.id} value={plan.id}>{plan.name}</option>)}</select></label>
  const common = <><div className="grid gap-4 sm:grid-cols-2">{field('name', t.name, 'text', true)}{currencyCombobox}</div></>
  const dated = <div className="grid gap-4 sm:grid-cols-2">{field('startsOn', t.start, 'date')}{field('endsOn', t.end, 'date')}</div>
  const monthlyReserve = reservePerMonth(draft)
  const seasonTotal = calendarMonths.reduce((total, month) => total + number(draft[`season:${month}`]), 0)
  const monthLabel = (month: number) => new Intl.DateTimeFormat(locale, { month: 'short' }).format(new Date(2026, month - 1, 1))
  const seasonalEditor = <div className="rounded-2xl border border-white/15 bg-white/5 p-4"><div className="flex items-center justify-between gap-4"><div><p className="text-sm font-medium text-mist">{t.seasonalTotal}</p><p className="mt-1 text-xs text-muted">{t.seasonalHelp}</p></div><span className={`rounded-xl px-3 py-2 text-sm font-semibold ${Math.abs(seasonTotal - 100) < 0.01 ? 'bg-sky/10 text-sky' : 'bg-warning/10 text-warning'}`}>{seasonTotal.toFixed(1)} %</span></div><div className="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-4">{calendarMonths.map((month) => field(`season:${month}`, `${monthLabel(month)} (%)`, 'number'))}</div></div>
  const expenseEvolutionEditor = <section className="rounded-2xl border border-white/15 bg-white/5 p-4"><div className="flex flex-col justify-between gap-3 sm:flex-row sm:items-start"><div><p className="text-sm font-medium text-mist">{evolutionCopy.title}</p><p className="mt-1 max-w-xl text-xs leading-5 text-muted">{evolutionCopy.help}</p></div><button className="ghost-button shrink-0" type="button" onClick={addFutureExpenseAmount}>＋ {evolutionCopy.add}</button></div>{futureExpenseAmounts.length > 0 && <div className="mt-4 space-y-3">{futureExpenseAmounts.map((change, index) => <div className="grid gap-3 rounded-xl border border-white/10 bg-white/[0.04] p-3 sm:grid-cols-[minmax(0,1fr)_minmax(0,1fr)_auto] sm:items-end" key={index}><DateField label={evolutionCopy.date} locale={locale} required value={change.effectiveOn} onChange={(next) => updateFutureExpenseAmount(index, 'effectiveOn', next)} /><label className="block text-sm text-mist">{evolutionCopy.amount} ({String(draft.currency || 'EUR')})<input className="field mt-2" min="0" required type="number" value={change.amount} onChange={(event) => updateFutureExpenseAmount(index, 'amount', event.target.value)} /></label><button className="ghost-button text-danger" type="button" onClick={() => removeFutureExpenseAmount(index)}>{evolutionCopy.remove}</button></div>)}</div>}{Boolean(draft.indexedToInflation) && futureExpenseAmounts.length > 0 && <p className="mt-3 text-xs leading-5 text-sky">{evolutionCopy.inflation}</p>}</section>
  if (resource === 'assetSales') {
    const saleContent = <>
      <div className="grid gap-4 sm:grid-cols-2">{field('name', t.name, 'text', true)}{currencyCombobox}</div>
      <div className="grid gap-4 sm:grid-cols-2">{saleAssetSelect}{field('happensOn', saleCopy.date, 'date', true)}</div>
      {toggle('useProjectedValue', saleCopy.projected, saleCopy.projectedHelp)}
      <div className={`grid gap-4 ${draft.useProjectedValue ? 'sm:grid-cols-2' : 'sm:grid-cols-3'}`}>
        {!draft.useProjectedValue && field('grossSalePrice', `${saleCopy.manualPrice} (${String(draft.currency || 'EUR')})`, 'number', true)}
        {field('sellingCosts', `${saleCopy.costs} (${String(draft.currency || 'EUR')})`, 'number')}
        {field('capitalGainsTaxRate', saleCopy.tax, 'number')}
      </div>
      {countryField('capitalGainsTaxCountryCode', t.taxCountry)}
      {toggle('repayLinkedLiabilities', saleCopy.repay, saleCopy.repayHelp)}
      {saleDestinationSelect}
      {draft.destination === 'Asset' && destinationAssetSelect}
      {draft.destination === 'InvestmentPlan' && destinationPlanSelect}
      <p className="rounded-2xl border border-sky/20 bg-sky/10 px-4 py-3 text-xs leading-5 text-sky">{saleCopy.summary}</p>
      <label className="block text-sm text-mist">{t.notes}<textarea className="field mt-2 min-h-24" value={String(draft.notes ?? '')} onChange={(event) => onField('notes', event.target.value)} /></label>
    </>
    return <div className="fixed inset-0 z-20 overflow-y-auto bg-inkDeep/70 p-4 backdrop-blur-sm"><form className="modal-surface mx-auto my-6 w-full max-w-2xl rounded-3xl p-6" onSubmit={(event) => void onSubmit(event)}><div className="flex items-start justify-between gap-4"><div><p className="eyebrow">{editing ? t.editInput : t.newInput}</p><h2 className="mt-2 text-xl font-semibold text-white">{String(draft.name || saleCopy.title)}</h2></div><button className="text-muted hover:text-white" type="button" onClick={onCancel}>✕</button></div><div className="mt-6 space-y-4">{saleContent}</div><div className="mt-7 flex justify-end gap-3"><button className="ghost-button" type="button" onClick={onCancel}>{t.cancel}</button><button className="primary-button" disabled={submitting}>{submitting ? t.saving : t.save}</button></div></form></div>
  }
  const content = resource === 'incomes' ? <>{common}{select('kind', t.category, ['Salary', 'Freelance', 'Rental', 'Dividends', 'Pension', 'Royalties', 'Other'])}{draft.kind === 'Rental' && assetLinkSelect(true)}<label className="block text-sm text-mist">{t.amountEntry}<select className="field mt-2" value={String(draft.amountMode ?? 'Monthly')} onChange={(event) => onField('amountMode', event.target.value)}><option className="bg-panel" value="Monthly">{t.monthlyEntry}</option><option className="bg-panel" value="Annual">{t.annualEntry}</option></select></label><div className="grid gap-4 sm:grid-cols-2">{draft.amountMode === 'Annual' ? field('annualAmount', t.annualAmount, 'number', true) : field('monthlyAmount', t.monthly, 'number', true)}{field('annualGrowthRate', t.growth, 'number')}</div>{draft.amountMode === 'Annual' && toggle('useSeasonality', t.seasonal, t.seasonalHelp)}{draft.amountMode === 'Annual' && draft.useSeasonality && seasonalEditor}{dated}{toggle('isTaxable', t.taxable)}{draft.isTaxable && <div className="grid gap-4 sm:grid-cols-2">{countryField('taxCountryCode', t.taxCountry)}{field('taxRate', t.taxRate, 'number')}</div>}</> : resource === 'assets' ? <>{common}{assetCategorySelect}{(draft.kind === 'Etf' || draft.kind === 'Stock') && <div className="grid gap-4 sm:grid-cols-2">{field('ticker', t.ticker, 'text')}{field('quantity', t.quantity, 'number', true)}</div>}<div className="grid gap-4 sm:grid-cols-3">{field('currentValue', t.currentValue, 'number', true, true)}{field('expectedAnnualReturn', t.return, 'number', false, true)}{field('volatility', t.volatility, 'number', false, true)}</div>{draft.kind !== 'Cash' && <div className="grid gap-4 sm:grid-cols-2">{countryField('capitalGainsTaxCountryCode', t.taxCountry)}{field('capitalGainsTaxRate', t.capitalGainsTax, 'number')}</div>}{draft.kind !== 'Cash' && toggle('isLiquid', t.liquid, t.liquidHelp)}{toggle('isIncludedInPortfolioAllocation', locale === 'fr' ? 'Inclure dans l’allocation investissable' : 'Include in investable allocation', locale === 'fr' ? 'Décochez pour conserver cet actif dans le patrimoine, mais pas dans la stratégie de répartition.' : 'Turn off to keep this asset in net worth while excluding it from target allocation.')}<AssetDossierEditor definitions={profileDefinitions} draft={draft} liabilities={liabilities} locale={locale} rates={rates} onField={onField} />{assetId && (draft.kind === 'Etf' || draft.kind === 'Stock') && <AssetPriceChart assetId={assetId} locale={locale} title={t.priceHistory} />}</> : resource === 'liabilities' ? <>{common}{select('kind', t.category, ['Mortgage', 'Loan', 'Leasing', 'Credit', 'Other'])}<section className="rounded-2xl border border-white/15 bg-white/5 p-4"><p className="text-sm font-medium text-mist">{locale === 'fr' ? 'Votre responsabilité dans cette dette' : 'Your responsibility for this debt'}</p><p className="mt-1 text-xs leading-5 text-muted">{locale === 'fr' ? 'Saisissez les montants du crédit complet, puis indiquez la part que vous devez réellement. Ce pourcentage est indépendant de votre part dans le bien.' : 'Enter the amounts for the complete loan, then the share you personally owe. This is independent from your ownership share in the asset.'}</p><div className="mt-4 grid gap-4 sm:grid-cols-2">{field('outstandingBalance', locale === 'fr' ? 'Solde total restant dû' : 'Total outstanding balance', 'number', true)}{field('responsibilityRate', locale === 'fr' ? 'Part de la dette à votre charge (%)' : 'Share of debt you owe (%)', 'number', true)}</div><div className="mt-4 grid gap-4 sm:grid-cols-2">{field('interestRate', t.rate, 'number')}{field('monthlyPayment', locale === 'fr' ? 'Mensualité totale' : 'Total monthly payment', 'number')}</div></section>{financedAssetSelect}{field('paidOffOn', t.end, 'date')}</> : resource === 'expenses' ? <>{common}{select('kind', t.category, ['Recurring', 'Exceptional'])}{assetLinkSelect()}{draft.kind === 'Recurring' ? <>{frequencySelect}{field('monthlyAmount', t.expenseAmount, 'number', true)}{dated}{toggle('indexedToInflation', t.index)}{expenseEvolutionEditor}</> : <><div className="grid gap-4 sm:grid-cols-2">{field('monthlyAmount', t.amount, 'number', true)}{field('startsOn', t.expenseDate, 'date', true)}</div>{toggle('saveInAdvance', t.saveInAdvance, t.saveInAdvanceHelp)}</>}{draft.kind === 'Exceptional' && draft.saveInAdvance && <><div className="grid gap-4 sm:grid-cols-2">{field('savingsStartsOn', t.savingsStart, 'date', true)}<div className="rounded-xl border border-sky/20 bg-sky/10 px-4 py-3 text-sm text-sky"><p className="font-medium">{t.monthlyReserve}</p><p className="mt-1 text-lg">{new Intl.NumberFormat(locale, { style: 'currency', currency: String(draft.currency || 'EUR') }).format(monthlyReserve)}</p></div></div><p className="rounded-xl bg-white/5 px-4 py-3 text-xs leading-5 text-muted">{locale === 'fr' ? 'Cette somme est réservée chaque mois jusqu’au mois de la dépense. Elle reste comprise dans votre patrimoine jusqu’au paiement.' : 'This amount is reserved every month through the month of the expense. It remains part of your net worth until the payment.'}</p></>}</> : resource === 'investments' ? <>{common}<div className="grid gap-4 sm:grid-cols-2">{field('monthlyContribution', t.monthlyContribution, 'number', true)}{field('expectedAnnualReturn', t.return, 'number')}</div>{dated}</> : <>{common}{eventKindSelect}<div className="grid gap-4 sm:grid-cols-2">{field('happensOn', draft.kind === 'VehiclePurchase' ? (locale === 'fr' ? 'Date du premier achat' : 'First purchase date') : t.date, 'date', true)}{toggle('repeats', draft.kind === 'VehiclePurchase' ? (locale === 'fr' ? 'Racheter une voiture régulièrement' : 'Buy another car regularly') : t.repeat)}</div>{draft.kind !== 'VehiclePurchase' && eventDirectionSelect}{draft.kind === 'VehiclePurchase' && <p className="rounded-xl border border-sky/20 bg-sky/10 px-4 py-3 text-xs leading-5 text-sky">{locale === 'fr' ? 'Saisissez un coût positif : LifeLedger le retirera automatiquement de votre argent à chaque achat.' : 'Enter a positive cost: LifeLedger will automatically deduct it at every purchase.'}</p>}{draft.repeats ? <><div className="grid gap-4 sm:grid-cols-2">{recurrenceFrequencySelect}{field('recurrenceEndsOn', locale === 'fr' ? 'Dernier achat au plus tard' : 'Last purchase by', 'date')}</div>{field('oneOffCashImpact', eventAmountLabel, 'number', true)}</> : <><div className="grid gap-4 sm:grid-cols-2">{field('oneOffCashImpact', eventAmountLabel, 'number', true)}{field('monthlyCashImpact', eventMonthlyLabel, 'number')}</div>{field('durationMonths', t.duration, 'number')}</>}<label className="block text-sm text-mist">{t.notes}<textarea className="field mt-2 min-h-24" value={String(draft.notes ?? '')} onChange={(event) => onField('notes', event.target.value)} /></label></>
  return <div className="fixed inset-0 z-20 overflow-y-auto bg-inkDeep/70 p-4 backdrop-blur-sm"><form className={`modal-surface mx-auto my-6 w-full rounded-3xl p-6 ${resource === 'assets' ? 'max-w-4xl' : 'max-w-2xl'}`} onSubmit={(event) => void onSubmit(event)}><div className="flex items-start justify-between gap-4"><div><p className="eyebrow">{editing ? t.editInput : t.newInput}</p><h2 className="mt-2 text-xl font-semibold text-white">{String(draft.name || '—')}</h2></div><button className="text-muted hover:text-white" type="button" onClick={onCancel}>✕</button></div><div className="mt-6 space-y-4">{content}</div><div className="mt-7 flex justify-end gap-3"><button className="ghost-button" type="button" onClick={onCancel}>{t.cancel}</button><button className="primary-button" disabled={submitting}>{submitting ? t.saving : t.save}</button></div></form></div>
}

function AssetPriceChart({ assetId, locale, title }: { assetId: string; locale: Locale; title: string }) {
  const [history, setHistory] = useState<Array<{ capturedAt: string; price: number; currency: string }>>([])
  useEffect(() => { void api.assetHistory(assetId).then(setHistory).catch(() => setHistory([])) }, [assetId])
  if (history.length === 0) return <p className="rounded-xl bg-white/5 px-3 py-3 text-xs text-muted">{locale === 'fr' ? 'Le graphique apparaîtra après la première mise à jour du cours.' : 'The chart appears after the first quote refresh.'}</p>
  const currency = history.at(-1)?.currency ?? 'EUR'
  const points = history.map((entry) => ({ ...entry, label: new Date(entry.capturedAt).toLocaleDateString(locale, { month: 'short', day: 'numeric' }) }))
  return <article className="rounded-2xl border border-white/10 bg-white/5 p-4"><p className="text-sm font-medium text-mist">{title}</p><div className="mt-3 h-44"><ResponsiveContainer width="100%" height="100%"><LineChart data={points}><XAxis dataKey="label" tick={{ fill: '#c4c7c8', fontSize: 11 }} /><YAxis tick={{ fill: '#c4c7c8', fontSize: 11 }} width={45} /><Tooltip formatter={(price) => new Intl.NumberFormat(locale, { style: 'currency', currency }).format(Number(price ?? 0))} /><Line dataKey="price" dot={false} stroke="#adc9eb" strokeWidth={2} type="monotone" /></LineChart></ResponsiveContainer></div></article>
}
