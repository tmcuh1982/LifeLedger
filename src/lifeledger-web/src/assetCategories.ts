import type { Locale } from './i18n'
import type { AssetCategory, LedgerItem } from './types'

/** Technical asset kinds whose labels are translated by the client. */
export const builtInAssetKinds = ['Cash', 'Etf', 'Stock', 'Crypto', 'RealEstate', 'Business', 'Collectible', 'Other'] as const

const labels: Record<Locale, Record<(typeof builtInAssetKinds)[number], string>> = {
  en: { Cash: 'Cash & savings', Etf: 'ETFs', Stock: 'Stocks', Crypto: 'Crypto', RealEstate: 'Real estate', Business: 'Businesses', Collectible: 'Collectibles', Other: 'Other' },
  fr: { Cash: 'Liquidités et épargne', Etf: 'ETF', Stock: 'Actions', Crypto: 'Crypto', RealEstate: 'Immobilier', Business: 'Entreprises', Collectible: 'Objets de collection', Other: 'Autres' },
  pl: { Cash: 'Gotówka i oszczędności', Etf: 'ETF-y', Stock: 'Akcje', Crypto: 'Kryptowaluty', RealEstate: 'Nieruchomości', Business: 'Firmy', Collectible: 'Przedmioty kolekcjonerskie', Other: 'Inne' },
  de: { Cash: 'Bargeld und Ersparnisse', Etf: 'ETFs', Stock: 'Aktien', Crypto: 'Krypto', RealEstate: 'Immobilien', Business: 'Unternehmen', Collectible: 'Sammlerstücke', Other: 'Sonstiges' },
  nl: { Cash: 'Contant geld en spaargeld', Etf: 'ETF’s', Stock: 'Aandelen', Crypto: 'Crypto', RealEstate: 'Vastgoed', Business: 'Bedrijven', Collectible: 'Verzamelobjecten', Other: 'Overige' },
}

/** Returns a localised label while keeping the stored technical kind stable. */
export function assetKindLabel(locale: Locale, kind: string) {
  return labels[locale][kind as (typeof builtInAssetKinds)[number]] ?? kind
}

/** Returns the personal category name when present, otherwise the translated built-in kind. */
export function assetCategoryLabel(locale: Locale, asset: LedgerItem, categories: AssetCategory[]) {
  const custom = String(asset.customCategory ?? '').trim()
  return categories.find((category) => category.name.toLocaleLowerCase() === custom.toLocaleLowerCase())?.name || custom || assetKindLabel(locale, String(asset.kind ?? 'Other'))
}
