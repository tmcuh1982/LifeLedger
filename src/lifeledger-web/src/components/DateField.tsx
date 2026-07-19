import { useEffect, useMemo, useState } from 'react'
import type { Locale } from '../i18n'

interface DateFieldProps {
  label: string
  value?: string
  locale: Locale
  onChange: (value: string) => void
  required?: boolean
  min?: string
  max?: string
  minYear?: number
  maxYear?: number
}

type DateParts = { day: string; month: string; year: string }

const partLabels: Record<Locale, { day: string; month: string; year: string; empty: string }> = {
  en: { day: 'Day', month: 'Month', year: 'Year', empty: 'Choose' },
  fr: { day: 'Jour', month: 'Mois', year: 'Année', empty: 'Choisir' },
  pl: { day: 'Dzień', month: 'Miesiąc', year: 'Rok', empty: 'Wybierz' },
  de: { day: 'Tag', month: 'Monat', year: 'Jahr', empty: 'Auswählen' },
  nl: { day: 'Dag', month: 'Maand', year: 'Jaar', empty: 'Kiezen' },
}

/** Splits the ISO date used by the API into values suitable for closed selectors. */
export function splitIsoDate(value?: string): DateParts {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value ?? '')
  return match ? { year: match[1], month: String(Number(match[2])), day: String(Number(match[3])) } : { year: '', month: '', day: '' }
}

/** Builds a validated ISO date only when all three calendar parts are selected. */
export function joinIsoDate(parts: DateParts): string | undefined {
  if (!parts.year || !parts.month || !parts.day) return undefined
  const year = Number(parts.year)
  const month = Number(parts.month)
  const day = Number(parts.day)
  if (month < 1 || month > 12 || day < 1 || day > daysInMonth(year, month)) return undefined
  return `${String(year).padStart(4, '0')}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`
}

/** Renders an unambiguous day/month/year selector while keeping the API's ISO date contract. */
export function DateField({ label, value = '', locale, onChange, required = false, min, max, minYear = 1900, maxYear = new Date().getFullYear() + 100 }: DateFieldProps) {
  const [parts, setParts] = useState<DateParts>(() => splitIsoDate(value))
  const text = partLabels[locale]
  const selectedYear = Number(parts.year) || new Date().getFullYear()
  const selectedMonth = Number(parts.month) || 1
  const maximumDay = daysInMonth(selectedYear, selectedMonth)
  const years = useMemo(() => {
    const existingYear = Number(splitIsoDate(value).year)
    const lower = Math.min(minYear, existingYear || minYear)
    const upper = Math.max(maxYear, existingYear || maxYear)
    return Array.from({ length: upper - lower + 1 }, (_, index) => upper - index)
  }, [maxYear, minYear, value])

  useEffect(() => {
    // External changes such as opening another record replace the local partial selection.
    const next = splitIsoDate(value)
    if (joinIsoDate(parts) !== value) setParts(next)
    // The selected parts intentionally stay local until they form a complete date.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [value])

  function update(part: keyof DateParts, nextValue: string) {
    const candidate = { ...parts, [part]: nextValue }
    if (!nextValue) {
      setParts(candidate)
      onChange('')
      return
    }
    if (candidate.day && candidate.month && candidate.year)
    {
      // Changing month or year from a 31-day month keeps the closest valid calendar day.
      candidate.day = String(Math.min(Number(candidate.day), daysInMonth(Number(candidate.year), Number(candidate.month))))
    }
    setParts(candidate)
    const iso = joinIsoDate(candidate)
    if (!iso) return
    if (min && iso < min) {
      setParts(splitIsoDate(min))
      onChange(min)
    } else if (max && iso > max) {
      setParts(splitIsoDate(max))
      onChange(max)
    } else onChange(iso)
  }

  function outsideBounds(year: number, month: number, day: number) {
    const iso = joinIsoDate({ year: String(year), month: String(month), day: String(day) })
    return !iso || Boolean(min && iso < min) || Boolean(max && iso > max)
  }

  return <fieldset className="min-w-0">
    <legend className="text-sm text-mist">{label}</legend>
    <div className="mt-2 grid grid-cols-[0.8fr_1.35fr_1fr] gap-2">
      <label className="min-w-0 text-xs text-muted"><span className="mb-1 block">{text.day}</span><select aria-label={`${label} — ${text.day}`} className="field" required={required} value={parts.day} onChange={(event) => update('day', event.target.value)}><option className="bg-panel" value="">—</option>{Array.from({ length: maximumDay }, (_, index) => index + 1).map((day) => <option className="bg-panel" disabled={Boolean(parts.year && parts.month && outsideBounds(Number(parts.year), Number(parts.month), day))} key={day} value={day}>{String(day).padStart(2, '0')}</option>)}</select></label>
      <label className="min-w-0 text-xs text-muted"><span className="mb-1 block">{text.month}</span><select aria-label={`${label} — ${text.month}`} className="field" required={required} value={parts.month} onChange={(event) => update('month', event.target.value)}><option className="bg-panel" value="">{text.empty}</option>{Array.from({ length: 12 }, (_, index) => index + 1).map((month) => <option className="bg-panel" disabled={Boolean(parts.year && monthOutsideBounds(Number(parts.year), month, min, max))} key={month} value={month}>{String(month).padStart(2, '0')} · {new Intl.DateTimeFormat(locale, { month: 'short' }).format(new Date(2024, month - 1, 1))}</option>)}</select></label>
      <label className="min-w-0 text-xs text-muted"><span className="mb-1 block">{text.year}</span><select aria-label={`${label} — ${text.year}`} className="field" required={required} value={parts.year} onChange={(event) => update('year', event.target.value)}><option className="bg-panel" value="">—</option>{years.map((year) => <option className="bg-panel" disabled={yearOutsideBounds(year, min, max)} key={year} value={year}>{year}</option>)}</select></label>
    </div>
  </fieldset>
}

/** Returns the number of valid days in a Gregorian calendar month. */
function daysInMonth(year: number, month: number) { return new Date(year, month, 0).getDate() }

/** Prevents choosing a year that lies completely outside the configured date range. */
function yearOutsideBounds(year: number, min?: string, max?: string) {
  return Boolean(min && `${year}-12-31` < min) || Boolean(max && `${year}-01-01` > max)
}

/** Prevents choosing a month that lies completely outside the configured date range. */
function monthOutsideBounds(year: number, month: number, min?: string, max?: string) {
  const prefix = `${year}-${String(month).padStart(2, '0')}`
  const first = `${prefix}-01`
  const last = `${prefix}-${String(daysInMonth(year, month)).padStart(2, '0')}`
  return Boolean(min && last < min) || Boolean(max && first > max)
}
