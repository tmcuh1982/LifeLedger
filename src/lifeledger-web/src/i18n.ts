export const locales = ['en', 'fr', 'pl', 'de', 'nl'] as const
export type Locale = (typeof locales)[number]

export const localeNames: Record<Locale, string> = {
  en: 'English', fr: 'Français', pl: 'Polski', de: 'Deutsch', nl: 'Nederlands',
}

type Copy = {
  overview: string; wealth: string; inputs: string; simulator: string; scenarios: string; privateData: string
  privateDetail: string; baseline: string; whatIf: string; runForecast: string; language: string
}

const copy: Record<Locale, Copy> = {
  en: { overview: 'Overview', wealth: 'Wealth', inputs: 'Life inputs', simulator: 'Simulator', scenarios: 'Scenarios', privateData: 'Your data', privateDetail: 'Private by default', baseline: 'Baseline scenario', whatIf: 'What-if scenario', runForecast: 'Run forecast', language: 'Language' },
  fr: { overview: 'Aperçu', wealth: 'Patrimoine', inputs: 'Données de vie', simulator: 'Simulateur', scenarios: 'Scénarios', privateData: 'Vos données', privateDetail: 'Privé par défaut', baseline: 'Scénario de référence', whatIf: 'Scénario alternatif', runForecast: 'Lancer la prévision', language: 'Langue' },
  pl: { overview: 'Przegląd', wealth: 'Majątek', inputs: 'Dane życiowe', simulator: 'Symulator', scenarios: 'Scenariusze', privateData: 'Twoje dane', privateDetail: 'Prywatność domyślnie', baseline: 'Scenariusz bazowy', whatIf: 'Scenariusz alternatywny', runForecast: 'Uruchom prognozę', language: 'Język' },
  de: { overview: 'Übersicht', wealth: 'Vermögen', inputs: 'Lebensdaten', simulator: 'Simulator', scenarios: 'Szenarien', privateData: 'Ihre Daten', privateDetail: 'Standardmäßig privat', baseline: 'Basisszenario', whatIf: 'Was-wäre-wenn-Szenario', runForecast: 'Prognose starten', language: 'Sprache' },
  nl: { overview: 'Overzicht', wealth: 'Vermogen', inputs: 'Levensgegevens', simulator: 'Simulator', scenarios: 'Scenario’s', privateData: 'Uw gegevens', privateDetail: 'Standaard privé', baseline: 'Basisscenario', whatIf: 'Wat-als-scenario', runForecast: 'Voorspelling starten', language: 'Taal' },
}

export function getCopy(locale: Locale): Copy { return copy[locale] }
