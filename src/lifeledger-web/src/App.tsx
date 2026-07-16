import { FormEvent, useEffect, useMemo, useState } from 'react'
import { api } from './api'
import { ActualNetWorthChart, AllocationChart, CashFlowChart, NetWorthChart } from './components/Charts'
import { MetricCard } from './components/MetricCard'
import { Planner } from './components/Planner'
import { Settings } from './components/Settings'
import { getCopy, localeNames, locales, type Locale } from './i18n'
import type { Dashboard, LifeLedgerExport, NetWorthSnapshot, Profile, ScenarioData, ScenarioSummary, Simulation, SimulationMode } from './types'

type Page = 'dashboard' | 'planner' | 'simulator' | 'scenarios'
const navigation: Array<{ id: Page; label: keyof ReturnType<typeof getCopy>; icon: string }> = [
  { id: 'dashboard', label: 'overview', icon: '◫' },
  { id: 'planner', label: 'inputs', icon: '◈' },
  { id: 'simulator', label: 'simulator', icon: '⌁' },
  { id: 'scenarios', label: 'scenarios', icon: '◇' },
]

function tr(locale: Locale, english: string, french: string) { return locale === 'fr' ? french : english }

function formatMoney(value: number, currency: string, compact = false, locale: Locale = 'en') {
  return new Intl.NumberFormat(locale, { style: 'currency', currency, notation: compact ? 'compact' : 'standard', maximumFractionDigits: compact ? 1 : 0 }).format(value)
}

function formatDate(date: string | undefined, locale: Locale) {
  return date ? new Intl.DateTimeFormat(locale, { year: 'numeric', month: 'short' }).format(new Date(date)) : tr(locale, 'Not yet reached', 'Pas encore atteint')
}

export function App() {
  const [page, setPage] = useState<Page>('dashboard')
  const [locale, setLocale] = useState<Locale>(() => (localStorage.getItem('lifeledger.locale') as Locale) || 'en')
  const [scenarios, setScenarios] = useState<ScenarioSummary[]>([])
  const [selectedId, setSelectedId] = useState<string>()
  const [dashboard, setDashboard] = useState<Dashboard>()
  const [data, setData] = useState<ScenarioData>()
  const [simulation, setSimulation] = useState<Simulation>()
  const [netWorthHistory, setNetWorthHistory] = useState<NetWorthSnapshot[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string>()
  const [creating, setCreating] = useState(false)
  const [scenarioName, setScenarioName] = useState('')
  const [settingsOpen, setSettingsOpen] = useState(false)
  const [profile, setProfile] = useState<Profile>()
  const [savingSettings, setSavingSettings] = useState(false)

  const selected = useMemo(() => scenarios.find((scenario) => scenario.id === selectedId), [scenarios, selectedId])
  const currency = dashboard?.currency ?? 'EUR'
  const text = getCopy(locale)

  function changeLocale(nextLocale: Locale) {
    setLocale(nextLocale)
    localStorage.setItem('lifeledger.locale', nextLocale)
  }

  async function loadScenario(id: string) {
    const [nextDashboard, nextData, nextNetWorthHistory] = await Promise.all([api.dashboard(id), api.scenarioData(id), api.netWorthHistory(id)])
    setDashboard(nextDashboard); setData(nextData); setNetWorthHistory(nextNetWorthHistory); setSimulation(undefined)
  }

  async function refresh() {
    try {
      setLoading(true); setError(undefined)
      const nextScenarios = await api.scenarios()
      setScenarios(nextScenarios)
      const id = selectedId && nextScenarios.some((scenario) => scenario.id === selectedId)
        ? selectedId
        : nextScenarios.find((scenario) => scenario.isBaseline)?.id ?? nextScenarios[0]?.id
      setSelectedId(id)
      if (id) await loadScenario(id)
      else { setDashboard(undefined); setData(undefined); setNetWorthHistory([]); setSimulation(undefined) }
    } catch (reason) { setError(reason instanceof Error ? reason.message : 'Could not load your ledger.') }
    finally { setLoading(false) }
  }

  useEffect(() => { void (async () => { await api.refreshMarketPrices().catch(() => undefined); await refresh() })() }, [])

  async function selectScenario(id: string) {
    setSelectedId(id); setLoading(true)
    try { await loadScenario(id) } catch (reason) { setError(reason instanceof Error ? reason.message : 'Could not load scenario.') } finally { setLoading(false) }
  }

  async function createScenario(event: FormEvent) {
    event.preventDefault()
    if (!scenarioName.trim() || !selected) return
    try {
      setCreating(true)
      const created = await api.createScenario(selected.profileId, scenarioName, selected.id)
      setScenarioName(''); setCreating(false)
      const nextScenarios = await api.scenarios(); setScenarios(nextScenarios)
      await selectScenario(created.id)
    } catch (reason) { setError(reason instanceof Error ? reason.message : 'Could not create scenario.'); setCreating(false) }
  }

  async function createItem(resource: keyof ScenarioData, item: Record<string, unknown>) {
    if (!selectedId) return
    try { await api.createItem(selectedId, resource, item); await loadScenario(selectedId) }
    catch (reason) { setError(reason instanceof Error ? reason.message : 'Could not save input.') }
  }

  async function deleteItem(resource: keyof ScenarioData, id: string) {
    try { await api.deleteItem(resource, id); if (selectedId) await loadScenario(selectedId) }
    catch (reason) { setError(reason instanceof Error ? reason.message : 'Could not remove input.') }
  }

  async function updateItem(resource: keyof ScenarioData, id: string, item: Record<string, unknown>) {
    try { await api.updateItem(resource, id, item); if (selectedId) await loadScenario(selectedId) }
    catch (reason) { setError(reason instanceof Error ? reason.message : 'Could not update input.') }
  }

  async function runSimulation(mode: SimulationMode) {
    if (!selectedId) return
    try { setLoading(true); setSimulation(await api.simulation(selectedId, mode)) }
    catch (reason) { setError(reason instanceof Error ? reason.message : 'Could not run simulation.') }
    finally { setLoading(false) }
  }

  async function openSettings() {
    try { setProfile(selected ? await api.profile(selected.profileId) : undefined); setSettingsOpen(true) }
    catch (reason) { setError(reason instanceof Error ? reason.message : 'Could not load settings.') }
  }

  async function saveProfileSettings(baseCurrency: string, expectedLifespan: number) {
    if (!profile) return
    try {
      setSavingSettings(true)
      setProfile(await api.updateProfile({ ...profile, baseCurrency, expectedLifespan }))
      await refresh()
    } catch (reason) { setError(reason instanceof Error ? reason.message : 'Could not save profile settings.') }
    finally { setSavingSettings(false) }
  }

  async function downloadExport(fileName: string) {
    try {
      const exportDocument = await api.exportData()
      const url = URL.createObjectURL(new Blob([JSON.stringify(exportDocument, null, 2)], { type: 'application/json' }))
      const anchor = document.createElement('a')
      anchor.href = url; anchor.download = fileName; anchor.click()
      URL.revokeObjectURL(url)
    } catch (reason) { setError(reason instanceof Error ? reason.message : 'Could not export data.') }
  }

  async function restoreBackup(document: LifeLedgerExport) {
    try { await api.importData(document, true); setSettingsOpen(false); await refresh() }
    catch (reason) { setError(reason instanceof Error ? reason.message : 'Could not restore backup.') }
  }

  async function resetMarketHistory() {
    try { await api.resetMarketHistory() }
    catch (reason) { setError(reason instanceof Error ? reason.message : 'Could not reset price history.') }
  }

  async function resetNetWorthHistory() {
    try {
      await api.resetNetWorthHistory()
      setNetWorthHistory([])
    } catch (reason) { setError(reason instanceof Error ? reason.message : 'Could not reset net-worth history.') }
  }

  async function deleteAllData() {
    try {
      await api.deleteAllData()
      setProfile(undefined)
      setSettingsOpen(false)
      await refresh()
    } catch (reason) { setError(reason instanceof Error ? reason.message : 'Could not delete local data.') }
  }

  return (
    <main className="min-h-screen p-3 sm:p-5 lg:p-6">
      <div className="mx-auto grid min-h-[calc(100vh-1.5rem)] max-w-[1560px] lg:grid-cols-[244px_1fr]">
        <aside className="glass mb-4 flex rounded-3xl p-3 lg:mb-0 lg:flex-col lg:rounded-r-none lg:border-r-0 lg:p-5">
          <div className="flex items-center gap-3 px-2 py-1"><span className="grid h-10 w-10 place-items-center rounded-xl bg-white text-lg font-black text-ink">L</span><div className="hidden sm:block"><p className="font-semibold tracking-tight text-white">LifeLedger</p><p className="text-xs text-muted">financial life simulator</p></div></div>
          <nav className="ml-auto flex gap-1 overflow-x-auto lg:ml-0 lg:mt-12 lg:block lg:space-y-1" aria-label="Primary navigation">
            {navigation.map((item) => <button key={item.id} className={`nav-item shrink-0 ${page === item.id ? 'nav-item-active' : ''}`} onClick={() => setPage(item.id)}><span className="text-base">{item.icon}</span><span className="hidden sm:inline lg:inline">{text[item.label]}</span></button>)}
          </nav>
          <div className="glass mt-auto hidden rounded-2xl p-4 lg:block"><p className="eyebrow">{text.privateData}</p><p className="mt-2 text-sm font-medium text-white">{text.privateDetail}</p><p className="mt-1 text-xs leading-5 text-muted">{tr(locale, 'Saved locally in your own database. No accounts, trackers or remote calls.', 'Enregistrées localement dans votre base. Aucun compte, suivi ou appel distant.')}</p></div>
        </aside>

        <div className="glass min-w-0 rounded-3xl p-4 sm:p-6 lg:rounded-l-none lg:border-l-0 lg:p-8">
          <header className="mb-8 flex flex-col justify-between gap-4 sm:flex-row sm:items-center">
            <div><p className="eyebrow">{selected?.isBaseline ? text.baseline : text.whatIf}</p><p className="mt-1 text-sm text-muted">{selected?.description || tr(locale, 'Your financial life, projected locally.', 'Votre vie financière, projetée localement.')}</p></div>
            <div className="flex flex-wrap items-center gap-2"><label className="sr-only" htmlFor="language-select">{text.language}</label><select className="field max-w-28 py-2.5" id="language-select" value={locale} onChange={(event) => changeLocale(event.target.value as Locale)}>{locales.map((entry) => <option className="bg-panel text-mist" key={entry} value={entry}>{localeNames[entry]}</option>)}</select><label className="sr-only" htmlFor="scenario-select">{tr(locale, 'Scenario', 'Scénario')}</label><select className="field max-w-60 py-2.5" id="scenario-select" value={selectedId ?? ''} onChange={(event) => void selectScenario(event.target.value)}>{scenarios.map((scenario) => <option className="bg-panel text-mist" key={scenario.id} value={scenario.id}>{scenario.name}{scenario.isBaseline ? ` · ${text.baseline}` : ''}</option>)}</select><button aria-label={tr(locale, 'Settings', 'Paramètres')} className="ghost-button px-4" onClick={() => void openSettings()}>⚙ <span className="hidden sm:inline">{tr(locale, 'Settings', 'Paramètres')}</span></button><button className="primary-button whitespace-nowrap" onClick={() => setPage('simulator')}>{text.runForecast}</button></div>
          </header>

          {error && <div className="mb-6 flex items-center justify-between gap-4 rounded-2xl border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger"><span>{error}</span><button className="text-xs font-bold uppercase" onClick={() => setError(undefined)}>{tr(locale, 'Dismiss', 'Fermer')}</button></div>}
          {loading && !dashboard ? <Loading locale={locale} /> : !selected || !dashboard ? <Empty locale={locale} /> : page === 'dashboard' ? <><DashboardPage dashboard={dashboard} locale={locale} onPlan={() => setPage('planner')} /><ActualNetWorthHistorySection history={netWorthHistory} currency={dashboard.currency} locale={locale} /></> : page === 'planner' && data ? <Planner data={data} scenarioId={selected.id} currency={currency} locale={locale} onCreate={createItem} onUpdate={updateItem} onDelete={deleteItem} /> : page === 'simulator' ? <SimulatorPage dashboard={dashboard} simulation={simulation} locale={locale} onRun={runSimulation} /> : <ScenariosPage scenarios={scenarios} selectedId={selected.id} name={scenarioName} creating={creating} locale={locale} onName={setScenarioName} onSubmit={createScenario} onSelect={selectScenario} />}
          {settingsOpen && <Settings locale={locale} profile={profile} saving={savingSettings} onClose={() => setSettingsOpen(false)} onSaveProfile={saveProfileSettings} onExport={downloadExport} onRestore={restoreBackup} onResetMarketHistory={resetMarketHistory} onResetNetWorthHistory={resetNetWorthHistory} onDeleteAllData={deleteAllData} />}
        </div>
      </div>
    </main>
  )
}

function DashboardPage({ dashboard, locale, onPlan }: { dashboard: Dashboard; locale: Locale; onPlan: () => void }) {
  const now = dashboard.timeline[0]
  const inTenYears = dashboard.timeline.find((row) => row.year >= now.year + 10) ?? dashboard.timeline.at(-1)!
  return <section className="space-y-5"><div className="flex flex-col justify-between gap-5 xl:flex-row xl:items-end"><div><p className="eyebrow">{tr(locale, 'Long-term clarity', 'Vision à long terme')}</p><h1 className="mt-2 max-w-2xl text-3xl font-semibold tracking-tight text-white sm:text-4xl">{tr(locale, 'See where your current life could lead.', 'Découvrez où votre mode de vie actuel peut vous mener.')}</h1><p className="mt-3 max-w-2xl text-sm leading-6 text-muted">{tr(locale, 'A living model of your income, assets, debt, expenses and life events—across the next fifty years.', 'Un modèle vivant de vos revenus, actifs, dettes, dépenses et événements de vie sur les cinquante prochaines années.')}</p></div><button className="ghost-button" onClick={onPlan}>{tr(locale, 'Edit life inputs', 'Modifier les données')}</button></div><div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4"><MetricCard label={tr(locale, 'Current net worth', 'Patrimoine net actuel')} value={formatMoney(dashboard.currentNetWorth, dashboard.currency, true, locale)} detail={tr(locale, 'Assets less liabilities today', 'Actifs moins dettes aujourd’hui')} icon="◈" /><MetricCard label={tr(locale, '10-year net worth', 'Patrimoine net à 10 ans')} value={formatMoney(inTenYears.netWorth, dashboard.currency, true, locale)} detail={`${tr(locale, 'At age', 'À')} ${inTenYears.age} ${tr(locale, 'years', 'ans')}`} icon="↗" tone="success" /><MetricCard label={tr(locale, 'Passive monthly income', 'Revenu passif mensuel')} value={formatMoney(dashboard.passiveMonthlyIncome, dashboard.currency, true, locale)} detail={tr(locale, 'Rent, dividends and royalties', 'Loyers, dividendes et redevances')} icon="⌁" /><MetricCard label={tr(locale, 'Success probability', 'Probabilité de réussite')} value={`${Math.round(dashboard.probabilityOfSuccess * 100)}%`} detail={tr(locale, 'Monte Carlo solvency rate', 'Taux de solvabilité Monte Carlo')} icon="◌" tone={dashboard.probabilityOfSuccess >= .8 ? 'success' : 'warning'} /></div><div className="grid gap-4 xl:grid-cols-[1.65fr_1fr]"><article className="section-card"><div className="flex items-start justify-between gap-4"><div><p className="eyebrow">{tr(locale, 'Wealth timeline', 'Évolution du patrimoine')}</p><h2 className="mt-1 text-lg font-semibold text-white">{tr(locale, 'Nominal vs. today’s purchasing power', 'Valeur nominale et pouvoir d’achat actuel')}</h2></div><span className="rounded-full bg-sky/15 px-3 py-1 text-xs font-medium text-sky">{tr(locale, 'to age', 'jusqu’à')} {dashboard.timeline.at(-1)?.age} {tr(locale, 'years', 'ans')}</span></div><div className="mt-5"><NetWorthChart timeline={dashboard.timeline} currency={dashboard.currency} locale={locale} /></div></article><article className="section-card"><p className="eyebrow">{tr(locale, 'Financial independence', 'Indépendance financière')}</p><p className="mt-2 text-2xl font-semibold text-white">{formatDate(dashboard.financialIndependenceDate, locale)}</p><p className="mt-2 text-sm leading-6 text-muted">{tr(locale, 'The first point where your projected wealth can support planned annual spending at your safe withdrawal rate.', 'Le premier moment où votre patrimoine projeté peut financer vos dépenses annuelles prévues selon votre taux de retrait sûr.')}</p><dl className="mt-6 space-y-3 border-t border-white/10 pt-5 text-sm"><div className="flex justify-between gap-4"><dt className="text-muted">{tr(locale, 'Retirement income', 'Revenu à la retraite')}</dt><dd className="font-medium text-mist">{formatMoney(dashboard.estimatedRetirementIncome, dashboard.currency, false, locale)}/{tr(locale, 'mo', 'mois')}</dd></div><div className="flex justify-between gap-4"><dt className="text-muted">{tr(locale, 'Real wealth change', 'Évolution réelle du patrimoine')}</dt><dd className={dashboard.inflationAdjustedPurchasingPowerChange >= 0 ? 'font-medium text-success' : 'font-medium text-warning'}>{dashboard.inflationAdjustedPurchasingPowerChange >= 0 ? '+' : ''}{Math.round(dashboard.inflationAdjustedPurchasingPowerChange)}%</dd></div></dl></article></div><div className="grid gap-4 xl:grid-cols-2"><article className="section-card"><p className="eyebrow">{tr(locale, 'Portfolio allocation', 'Répartition du portefeuille')}</p><h2 className="mt-1 text-lg font-semibold text-white">{tr(locale, 'What you own today', 'Ce que vous possédez aujourd’hui')}</h2><div className="mt-4"><AllocationChart allocation={dashboard.allocation} currency={dashboard.currency} /></div></article><article className="section-card"><p className="eyebrow">{tr(locale, 'Cash flow timeline', 'Évolution de la trésorerie')}</p><h2 className="mt-1 text-lg font-semibold text-white">{tr(locale, 'Annual surplus after planned saving', 'Excédent annuel après épargne prévue')}</h2><div className="mt-4"><CashFlowChart timeline={dashboard.timeline} currency={dashboard.currency} locale={locale} /></div></article></div><Warnings items={dashboard.warnings} locale={locale} /></section>
}

function ActualNetWorthHistorySection({ history, currency, locale }: { history: NetWorthSnapshot[]; currency: string; locale: Locale }) {
  return <article className="section-card"><div className="flex items-start justify-between gap-4"><div><p className="eyebrow">{tr(locale, 'Actual history', 'Historique réel')}</p><h2 className="mt-1 text-lg font-semibold text-white">{tr(locale, 'Your observed net worth over time', 'Votre patrimoine net observé dans le temps')}</h2></div><span className="rounded-full bg-success/10 px-3 py-1 text-xs font-medium text-success">{history.length} {tr(locale, 'point(s)', 'point(s)')}</span></div>{history.length ? <div className="mt-5"><ActualNetWorthChart history={history} currency={currency} locale={locale} /></div> : <p className="mt-5 rounded-xl bg-white/5 px-4 py-5 text-sm leading-6 text-muted">{tr(locale, 'The first point will be saved the next time LifeLedger starts.', 'Le premier point sera enregistré au prochain démarrage de LifeLedger.')}</p>}</article>
}

function SimulatorPage({ dashboard, simulation, locale, onRun }: { dashboard: Dashboard; simulation?: Simulation; locale: Locale; onRun: (mode: SimulationMode) => Promise<void> }) {
  const [mode, setMode] = useState<SimulationMode>('MonteCarlo')
  const active = simulation?.mode === mode ? simulation : undefined
  return <section className="space-y-5"><div><p className="eyebrow">{tr(locale, 'Scenario engine', 'Moteur de scénarios')}</p><h1 className="mt-2 text-3xl font-semibold text-white">{tr(locale, 'Test the resilience of your plan.', 'Testez la solidité de votre plan.')}</h1><p className="mt-2 max-w-2xl text-sm leading-6 text-muted">{tr(locale, 'Run separate models against the same private data. Assumptions remain transparent.', 'Exécutez différents modèles sur les mêmes données privées. Les hypothèses restent transparentes.')}</p></div><div className="section-card"><div className="flex flex-col justify-between gap-5 md:flex-row md:items-center"><div className="flex flex-wrap gap-2">{(['Deterministic', 'MonteCarlo', 'Historical'] as SimulationMode[]).map((item) => <button className={`rounded-xl px-4 py-2.5 text-sm font-medium transition ${mode === item ? 'bg-white text-ink' : 'bg-white/5 text-muted hover:bg-white/10 hover:text-mist'}`} key={item} onClick={() => setMode(item)}>{item === 'MonteCarlo' ? 'Monte Carlo' : item === 'Historical' ? tr(locale, 'Historical', 'Historique') : tr(locale, 'Deterministic', 'Déterministe')}</button>)}</div><button className="primary-button" onClick={() => void onRun(mode)}>{tr(locale, 'Run', 'Lancer')} {mode === 'MonteCarlo' ? 'Monte Carlo' : tr(locale, 'simulation', 'la simulation')}</button></div><div className="mt-8 grid gap-4 md:grid-cols-3"><div className="rounded-2xl bg-white/5 p-4"><p className="eyebrow">{tr(locale, 'Model', 'Modèle')}</p><p className="mt-2 text-lg font-medium text-white">{mode === 'MonteCarlo' ? tr(locale, 'Variable market paths', 'Trajectoires de marché variables') : mode === 'Historical' ? tr(locale, 'Historical return cycle', 'Cycle de rendements historiques') : tr(locale, 'Expected returns', 'Rendements attendus')}</p></div><div className="rounded-2xl bg-white/5 p-4"><p className="eyebrow">{tr(locale, 'Horizon', 'Horizon')}</p><p className="mt-2 text-lg font-medium text-white">{tr(locale, 'to age', 'jusqu’à')} {dashboard.timeline.at(-1)?.age} {tr(locale, 'years', 'ans')}</p></div><div className="rounded-2xl bg-white/5 p-4"><p className="eyebrow">{tr(locale, 'Status', 'État')}</p><p className="mt-2 text-lg font-medium text-white">{active ? `${active.runs.toLocaleString(locale)} ${tr(locale, 'paths complete', 'trajectoires terminées')}` : tr(locale, 'Ready to run', 'Prêt à lancer')}</p></div></div></div>{active ? <><div className="grid gap-4 md:grid-cols-3"><MetricCard label={tr(locale, 'Probability of success', 'Probabilité de réussite')} value={`${Math.round(active.probabilityOfSuccess * 100)}%`} detail={tr(locale, 'Paths that never turn negative', 'Trajectoires restant positives')} icon="◌" tone={active.probabilityOfSuccess >= .8 ? 'success' : 'warning'} /><MetricCard label={tr(locale, 'Median terminal value', 'Valeur finale médiane')} value={formatMoney(active.terminalNetWorths[Math.floor(active.terminalNetWorths.length / 2)] ?? 0, dashboard.currency, true, locale)} detail={tr(locale, 'Across simulated outcomes', 'Tous scénarios simulés confondus')} icon="◇" /><MetricCard label={tr(locale, 'Simulation runs', 'Simulations')} value={active.runs.toLocaleString(locale)} detail={tr(locale, 'Deterministic random sampling', 'Échantillonnage aléatoire déterministe')} icon="⌁" /></div><article className="section-card"><p className="eyebrow">{tr(locale, 'Projected path', 'Trajectoire projetée')}</p><h2 className="mt-1 text-lg font-semibold text-white">{tr(locale, 'Reference timeline for this simulation', 'Trajectoire de référence')}</h2><div className="mt-4"><NetWorthChart timeline={active.timeline} currency={dashboard.currency} /></div></article><Warnings items={active.warnings} locale={locale} /></> : <article className="section-card text-center"><p className="text-2xl">⌁</p><h2 className="mt-3 font-semibold text-white">{tr(locale, 'Choose a model and run it.', 'Choisissez un modèle puis lancez-le.')}</h2><p className="mx-auto mt-2 max-w-md text-sm leading-6 text-muted">{tr(locale, 'Monte Carlo uses your portfolio return and volatility assumptions. It runs entirely in the API process without a third-party service.', 'Monte Carlo utilise vos hypothèses de rendement et de volatilité. Il s’exécute entièrement dans l’API, sans service tiers.')}</p></article>}</section>
}

function ScenariosPage({ scenarios, selectedId, name, creating, locale, onName, onSubmit, onSelect }: { scenarios: ScenarioSummary[]; selectedId: string; name: string; creating: boolean; locale: Locale; onName: (name: string) => void; onSubmit: (event: FormEvent) => Promise<void>; onSelect: (id: string) => Promise<void> }) {
  return <section className="space-y-5"><div><p className="eyebrow">{tr(locale, 'Unlimited scenarios', 'Scénarios illimités')}</p><h1 className="mt-2 text-3xl font-semibold text-white">{tr(locale, 'Compare possible futures.', 'Comparez les futurs possibles.')}</h1><p className="mt-2 max-w-2xl text-sm leading-6 text-muted">{tr(locale, 'New scenarios fork from the selected plan so you can model a relocation, early retirement, new child or business without touching the baseline.', 'Chaque scénario est une copie du plan sélectionné : modélisez un déménagement, une retraite anticipée, un enfant ou une entreprise sans modifier votre base.')}</p></div><form className="section-card flex flex-col gap-3 sm:flex-row" onSubmit={(event) => void onSubmit(event)}><input className="field" value={name} onChange={(event) => onName(event.target.value)} placeholder={tr(locale, 'Name a new what-if scenario', 'Nommez le nouveau scénario')} /><button className="primary-button shrink-0" disabled={creating || !name.trim()}>{creating ? tr(locale, 'Creating…', 'Création…') : tr(locale, 'Fork scenario', 'Créer une variante')}</button></form><div className="grid gap-4 md:grid-cols-2">{scenarios.map((scenario) => <button className={`section-card glass-hover text-left ${scenario.id === selectedId ? 'ring-1 ring-sky/70' : ''}`} key={scenario.id} onClick={() => void onSelect(scenario.id)}><div className="flex items-start justify-between gap-4"><div><p className="text-lg font-semibold text-white">{scenario.name}</p><p className="mt-2 text-sm leading-6 text-muted">{scenario.description || tr(locale, 'No description yet.', 'Aucune description pour le moment.')}</p></div>{scenario.isBaseline && <span className="rounded-full bg-sky/15 px-2.5 py-1 text-xs font-semibold text-sky">{tr(locale, 'Baseline', 'Référence')}</span>}</div><p className="mt-6 text-xs font-semibold uppercase tracking-[0.1em] text-muted">{tr(locale, 'Starts', 'Débute le')} {new Date(scenario.startsOn).toLocaleDateString(locale)}</p></button>)}</div></section>
}

function Warnings({ items, locale }: { items: string[]; locale: Locale }) { return <article className="section-card"><p className="eyebrow">{tr(locale, 'Signals to review', 'Points à surveiller')}</p><div className="mt-4 space-y-3">{items.length ? items.map((item) => <div className="flex gap-3 rounded-xl bg-warning/10 px-4 py-3 text-sm leading-6 text-warning" key={item}><span>!</span><p>{item}</p></div>) : <div className="rounded-xl bg-success/10 px-4 py-3 text-sm text-success">{tr(locale, 'No risk signals triggered by the current rules.', 'Aucun signal de risque déclenché par les règles actuelles.')}</div>}</div></article> }
function Loading({ locale }: { locale: Locale }) { return <div className="grid min-h-80 place-items-center"><p className="text-sm text-muted">{tr(locale, 'Loading your local ledger…', 'Chargement de votre registre local…')}</p></div> }
function Empty({ locale }: { locale: Locale }) { return <div className="grid min-h-80 place-items-center text-center"><div><p className="text-3xl">◈</p><h1 className="mt-3 text-xl font-semibold text-white">{tr(locale, 'No scenario available', 'Aucun scénario disponible')}</h1><p className="mt-2 text-sm text-muted">{tr(locale, 'Start the API with demo seeding enabled or import a private export.', 'Activez les données de démonstration ou importez une sauvegarde privée.')}</p></div></div> }
