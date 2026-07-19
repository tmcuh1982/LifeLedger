import { FormEvent, useEffect, useMemo, useState } from 'react'
import { api } from './api'
import { ActualNetWorthChart, AllocationChart, CashFlowChart, NetWorthChart } from './components/Charts'
import { MetricCard } from './components/MetricCard'
import { Planner } from './components/Planner'
import { Settings } from './components/Settings'
import { Wealth } from './components/Wealth'
import { Banking } from './components/Banking'
import { getCopy, localeNames, locales, type Locale } from './i18n'
import type { AllocationStrategy, AssetCategory, AssetProfileDefinition, AssetProfileDefinitionInput, Dashboard, LifeLedgerExport, NetWorthSnapshot, Profile, ScenarioData, ScenarioSummary, Simulation, SimulationMode, SimulationWarning } from './types'

type Page = 'dashboard' | 'wealth' | 'planner' | 'banking' | 'simulator' | 'scenarios' | 'compare'
const navigation: Array<{ id: Page; label: keyof ReturnType<typeof getCopy>; icon: string }> = [
  { id: 'dashboard', label: 'overview', icon: '◫' },
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
  const [allocationStrategies, setAllocationStrategies] = useState<AllocationStrategy[]>([])
  const [data, setData] = useState<ScenarioData>()
  const [simulation, setSimulation] = useState<Simulation>()
  const [netWorthHistory, setNetWorthHistory] = useState<NetWorthSnapshot[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string>()
  const [creating, setCreating] = useState(false)
  const [scenarioName, setScenarioName] = useState('')
  const [settingsOpen, setSettingsOpen] = useState(false)
  const [profile, setProfile] = useState<Profile>()
  const [assetCategories, setAssetCategories] = useState<AssetCategory[]>([])
  const [assetProfileDefinitions, setAssetProfileDefinitions] = useState<AssetProfileDefinition[]>([])
  const [savingSettings, setSavingSettings] = useState(false)
  const [bankingImportOpen, setBankingImportOpen] = useState(false)

  const selected = useMemo(() => scenarios.find((scenario) => scenario.id === selectedId), [scenarios, selectedId])
  const currency = dashboard?.currency ?? 'EUR'
  const text = getCopy(locale)

  function changeLocale(nextLocale: Locale) {
    setLocale(nextLocale)
    localStorage.setItem('lifeledger.locale', nextLocale)
  }

  async function loadScenario(id: string) {
    const [nextDashboard, nextData, nextNetWorthHistory, nextAllocationStrategies] = await Promise.all([api.dashboard(id), api.scenarioData(id), api.netWorthHistory(id), api.allocationStrategies(id)])
    setDashboard(nextDashboard); setData(nextData); setNetWorthHistory(nextNetWorthHistory); setAllocationStrategies(nextAllocationStrategies); setSimulation(undefined)
  }

  async function refresh() {
    try {
      setLoading(true); setError(undefined)
      const [nextScenarios, nextAssetCategories, nextAssetProfileDefinitions] = await Promise.all([api.scenarios(), api.assetCategories(), api.assetProfileDefinitions()])
      setScenarios(nextScenarios)
      setAssetCategories(nextAssetCategories)
      setAssetProfileDefinitions(nextAssetProfileDefinitions)
      const id = selectedId && nextScenarios.some((scenario) => scenario.id === selectedId)
        ? selectedId
        : nextScenarios.find((scenario) => scenario.isBaseline)?.id ?? nextScenarios[0]?.id
      setSelectedId(id)
      if (id) await loadScenario(id)
      else { setDashboard(undefined); setData(undefined); setNetWorthHistory([]); setAllocationStrategies([]); setSimulation(undefined) }
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
    try { resource === 'assets' ? await api.createAssetDossier(selectedId, item) : await api.createItem(selectedId, resource, item); await loadScenario(selectedId) }
    catch (reason) { setError(reason instanceof Error ? reason.message : 'Could not save input.') }
  }

  async function deleteItem(resource: keyof ScenarioData, id: string) {
    try { await api.deleteItem(resource, id); if (selectedId) await loadScenario(selectedId) }
    catch (reason) { setError(reason instanceof Error ? reason.message : 'Could not remove input.') }
  }

  async function updateItem(resource: keyof ScenarioData, id: string, item: Record<string, unknown>) {
    try { resource === 'assets' ? await api.updateAssetDossier(id, item) : await api.updateItem(resource, id, item); if (selectedId) await loadScenario(selectedId) }
    catch (reason) { setError(reason instanceof Error ? reason.message : 'Could not update input.') }
  }

  async function runSimulation(mode: SimulationMode) {
    if (!selectedId) return
    try { setLoading(true); setSimulation(await api.simulation(selectedId, mode)) }
    catch (reason) { setError(reason instanceof Error ? reason.message : 'Could not run simulation.') }
    finally { setLoading(false) }
  }

  async function saveAllocationStrategy(strategyId: string | undefined, value: Record<string, unknown>) {
    if (!selectedId) return
    try { strategyId ? await api.updateAllocationStrategy(strategyId, value) : await api.createAllocationStrategy(selectedId, value); await loadScenario(selectedId) }
    catch (reason) { setError(reason instanceof Error ? reason.message : 'Could not save allocation strategy.') }
  }

  async function removeAllocationStrategy(strategyId: string) {
    try { await api.deleteAllocationStrategy(strategyId); if (selectedId) await loadScenario(selectedId) }
    catch (reason) { setError(reason instanceof Error ? reason.message : 'Could not remove allocation strategy.') }
  }

  async function openSettings() {
    try {
      setError(undefined)
      const [nextProfile, nextAssetCategories, nextAssetProfileDefinitions] = await Promise.all([selected ? api.profile(selected.profileId) : Promise.resolve(undefined), api.assetCategories(), api.assetProfileDefinitions()])
      setProfile(nextProfile); setAssetCategories(nextAssetCategories); setAssetProfileDefinitions(nextAssetProfileDefinitions); setSettingsOpen(true)
    }
    catch (reason) { setError(reason instanceof Error ? reason.message : 'Could not load settings.') }
  }

  async function createAssetCategory(name: string) { await api.createAssetCategory(name); const categories = await api.assetCategories(); setAssetCategories(categories); return categories }
  async function renameAssetCategory(currentName: string, name: string) { await api.renameAssetCategory(currentName, name); const categories = await api.assetCategories(); setAssetCategories(categories); if (selectedId) await loadScenario(selectedId); return categories }
  async function deleteAssetCategory(name: string) { await api.deleteAssetCategory(name); const categories = await api.assetCategories(); setAssetCategories(categories); return categories }
  async function createAssetProfileDefinition(definition: AssetProfileDefinitionInput) { await api.createAssetProfileDefinition(definition); const profiles = await api.assetProfileDefinitions(); setAssetProfileDefinitions(profiles) }
  async function updateAssetProfileDefinition(key: string, definition: AssetProfileDefinitionInput) { await api.updateAssetProfileDefinition(key, definition); const profiles = await api.assetProfileDefinitions(); setAssetProfileDefinitions(profiles) }
  async function deleteAssetProfileDefinition(key: string) { await api.deleteAssetProfileDefinition(key); const profiles = await api.assetProfileDefinitions(); setAssetProfileDefinitions(profiles) }

  async function saveProfileSettings(updatedProfile: Profile) {
    if (!profile) return
    try {
      setSavingSettings(true)
      setProfile(await api.updateProfile(updatedProfile))
      // A settings-triggered reload must reject back to the modal instead of creating a hidden page error.
      if (selectedId) await loadScenario(selectedId)
    } finally { setSavingSettings(false) }
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

  async function restoreDemo() {
    try {
      setLoading(true)
      const restored = await api.restoreDemo()
      setSelectedId(restored.scenarioId)
      setProfile(undefined)
      setSettingsOpen(false)
      await refresh()
      setPage('dashboard')
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : 'Could not restore demo data.')
    } finally { setLoading(false) }
  }

  return (
    <main className="min-h-screen p-3 sm:p-5 lg:p-6">
      <div className="mx-auto grid min-h-[calc(100vh-1.5rem)] max-w-[1560px] lg:grid-cols-[244px_1fr]">
        <aside className="glass mb-4 flex rounded-3xl p-3 lg:mb-0 lg:flex-col lg:rounded-r-none lg:border-r-0 lg:p-5">
          <div className="flex items-center gap-3 px-2 py-1"><span className="grid h-10 w-10 place-items-center rounded-xl bg-white text-lg font-black text-ink">L</span><div className="hidden sm:block"><p className="font-semibold tracking-tight text-white">LifeLedger</p><p className="text-xs text-muted">financial life simulator</p></div></div>
          <nav className="ml-auto flex gap-1 overflow-x-auto lg:ml-0 lg:mt-12 lg:block lg:space-y-1" aria-label="Primary navigation">
            {navigation.slice(0, 1).map((item) => <button key={item.id} className={`nav-item shrink-0 ${page === item.id ? 'nav-item-active' : ''}`} onClick={() => setPage(item.id)}><span className="text-base">{item.icon}</span><span className="hidden sm:inline lg:inline">{text[item.label]}</span></button>)}
            <div className={`rounded-2xl ${['wealth', 'planner', 'banking'].includes(page) ? 'bg-white/5' : ''}`}><button className={`nav-item shrink-0 ${['wealth', 'planner', 'banking'].includes(page) ? 'nav-item-active' : ''}`} onClick={() => setPage('wealth')}><span className="text-base">◆</span><span className="hidden sm:inline lg:inline">{text.wealth}</span></button><div className={`${['wealth', 'planner', 'banking'].includes(page) ? 'flex' : 'hidden'} gap-1 pb-2 pl-2 lg:block lg:space-y-1 lg:pl-9`}><button className={`block w-full whitespace-nowrap rounded-lg px-3 py-1.5 text-left text-xs ${page === 'wealth' ? 'text-white' : 'text-muted hover:text-mist'}`} onClick={() => setPage('wealth')}>{tr(locale, 'Summary', 'Synthèse')}</button><button className={`block w-full whitespace-nowrap rounded-lg px-3 py-1.5 text-left text-xs ${page === 'planner' ? 'text-white' : 'text-muted hover:text-mist'}`} onClick={() => setPage('planner')}>{tr(locale, 'Assets, income & expenses', 'Actifs, revenus et dépenses')}</button><button className={`block w-full whitespace-nowrap rounded-lg px-3 py-1.5 text-left text-xs ${page === 'banking' ? 'text-white' : 'text-muted hover:text-mist'}`} onClick={() => setPage('banking')}>{tr(locale, 'Bank operations', 'Opérations bancaires')}</button></div></div>
            {navigation.slice(1).map((item) => <button key={item.id} className={`nav-item shrink-0 ${page === item.id ? 'nav-item-active' : ''}`} onClick={() => setPage(item.id)}><span className="text-base">{item.icon}</span><span className="hidden sm:inline lg:inline">{text[item.label]}</span></button>)}
          </nav>
          <div className="glass mt-auto hidden rounded-2xl p-4 lg:block"><p className="eyebrow">{text.privateData}</p><p className="mt-2 text-sm font-medium text-white">{text.privateDetail}</p><p className="mt-1 text-xs leading-5 text-muted">{tr(locale, 'Saved locally in your own database. No accounts, trackers or remote calls.', 'Enregistrées localement dans votre base. Aucun compte, suivi ou appel distant.')}</p></div>
        </aside>

        <div className="glass min-w-0 rounded-3xl p-4 sm:p-6 lg:rounded-l-none lg:border-l-0 lg:p-8">
          <header className="mb-8 flex flex-col justify-between gap-4 sm:flex-row sm:items-center">
            <div><p className="eyebrow">{selected?.isBaseline ? text.baseline : text.whatIf}</p><p className="mt-1 text-sm text-muted">{selected?.description || tr(locale, 'Your financial life, projected locally.', 'Votre vie financière, projetée localement.')}</p></div>
            <div className="flex flex-wrap items-center gap-2"><label className="sr-only" htmlFor="language-select">{text.language}</label><select className="field max-w-28 py-2.5" id="language-select" value={locale} onChange={(event) => changeLocale(event.target.value as Locale)}>{locales.map((entry) => <option className="bg-panel text-mist" key={entry} value={entry}>{localeNames[entry]}</option>)}</select><label className="sr-only" htmlFor="scenario-select">{tr(locale, 'Scenario', 'Scénario')}</label><select className="field max-w-60 py-2.5" id="scenario-select" value={selectedId ?? ''} onChange={(event) => void selectScenario(event.target.value)}>{scenarios.map((scenario) => <option className="bg-panel text-mist" key={scenario.id} value={scenario.id}>{scenario.name}{scenario.isBaseline ? ` · ${text.baseline}` : ''}</option>)}</select><button aria-label={tr(locale, 'Settings', 'Paramètres')} className="ghost-button px-4" onClick={() => void openSettings()}>⚙ <span className="hidden sm:inline">{tr(locale, 'Settings', 'Paramètres')}</span></button><button className="primary-button whitespace-nowrap" onClick={() => setPage('simulator')}>{text.runForecast}</button></div>
          </header>

          {error && <div className="mb-6 flex items-center justify-between gap-4 rounded-2xl border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger"><span>{error}</span><button className="text-xs font-bold uppercase" onClick={() => setError(undefined)}>{tr(locale, 'Dismiss', 'Fermer')}</button></div>}
          {loading && !dashboard ? <Loading locale={locale} /> : !selected || !dashboard ? <Empty locale={locale} /> : page === 'dashboard' ? <><DashboardPage allocationStrategies={allocationStrategies} dashboard={dashboard} locale={locale} onDeleteStrategy={removeAllocationStrategy} onPlan={() => setPage('planner')} onSaveStrategy={saveAllocationStrategy} /><ActualNetWorthHistorySection history={netWorthHistory} currency={dashboard.currency} locale={locale} /></> : page === 'wealth' && data ? <Wealth assets={data.assets} assetCategories={assetCategories} baseCurrency={currency} locale={locale} onEdit={() => setPage('planner')} /> : page === 'planner' && data ? <Planner data={data} scenarioId={selected.id} assetCategories={assetCategories} profileDefinitions={assetProfileDefinitions} currency={currency} locale={locale} onCreate={createItem} onUpdate={updateItem} onDelete={deleteItem} onImportBank={() => { setPage('banking'); setBankingImportOpen(true) }} /> : page === 'banking' && data ? <Banking scenarioId={selected.id} data={data} locale={locale} openImport={bankingImportOpen} onImportOpened={() => setBankingImportOpen(false)} onDataChanged={() => loadScenario(selected.id)} /> : page === 'simulator' ? <SimulatorPage dashboard={dashboard} simulation={simulation} locale={locale} onRun={runSimulation} /> : page === 'compare' ? <ScenarioComparisonPage scenarios={scenarios} locale={locale} onBack={() => setPage('scenarios')} /> : <ScenariosPage scenarios={scenarios} selectedId={selected.id} name={scenarioName} creating={creating} locale={locale} onName={setScenarioName} onSubmit={createScenario} onSelect={selectScenario} onCompare={() => setPage('compare')} />}
          {settingsOpen && <Settings locale={locale} profile={profile} assetCategories={assetCategories} assetProfileDefinitions={assetProfileDefinitions} saving={savingSettings} onClose={() => setSettingsOpen(false)} onSaveProfile={saveProfileSettings} onCreateAssetCategory={createAssetCategory} onRenameAssetCategory={renameAssetCategory} onDeleteAssetCategory={deleteAssetCategory} onCreateAssetProfileDefinition={createAssetProfileDefinition} onUpdateAssetProfileDefinition={updateAssetProfileDefinition} onDeleteAssetProfileDefinition={deleteAssetProfileDefinition} onExport={downloadExport} onRestore={restoreBackup} onRestoreDemo={restoreDemo} onResetMarketHistory={resetMarketHistory} onResetNetWorthHistory={resetNetWorthHistory} onDeleteAllData={deleteAllData} />}
        </div>
      </div>
    </main>
  )
}

function DashboardPage({ allocationStrategies, dashboard, locale, onPlan, onSaveStrategy, onDeleteStrategy }: { allocationStrategies: AllocationStrategy[]; dashboard: Dashboard; locale: Locale; onPlan: () => void; onSaveStrategy: (strategyId: string | undefined, value: Record<string, unknown>) => Promise<void>; onDeleteStrategy: (strategyId: string) => Promise<void> }) {
  const now = dashboard.timeline[0]
  const inTenYears = dashboard.timeline.find((row) => row.year >= now.year + 10) ?? dashboard.timeline.at(-1)!
  const [timelineScale, setTimelineScale] = useState<'year' | 'age'>('age')
  const scaleControl = <div className="flex rounded-xl bg-white/5 p-1 text-xs"><button className={`rounded-lg px-3 py-1.5 ${timelineScale === 'age' ? 'bg-white text-ink' : 'text-muted hover:text-mist'}`} onClick={() => setTimelineScale('age')}>{tr(locale, 'Age', 'Âge')}</button><button className={`rounded-lg px-3 py-1.5 ${timelineScale === 'year' ? 'bg-white text-ink' : 'text-muted hover:text-mist'}`} onClick={() => setTimelineScale('year')}>{tr(locale, 'Year', 'Année')}</button></div>
  return <section className="space-y-5"><div className="flex flex-col justify-between gap-5 xl:flex-row xl:items-end"><div><p className="eyebrow">{tr(locale, 'Long-term clarity', 'Vision à long terme')}</p><h1 className="mt-2 max-w-2xl text-3xl font-semibold tracking-tight text-white sm:text-4xl">{tr(locale, 'See where your current life could lead.', 'Découvrez où votre mode de vie actuel peut vous mener.')}</h1><p className="mt-3 max-w-2xl text-sm leading-6 text-muted">{tr(locale, 'A living model of your income, assets, debt, expenses and life events—across the next fifty years.', 'Un modèle vivant de vos revenus, actifs, dettes, dépenses et événements de vie sur les cinquante années à venir.')}</p></div><button className="ghost-button" onClick={onPlan}>{tr(locale, 'Edit life inputs', 'Modifier les données')}</button></div><div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-5"><MetricCard label={tr(locale, 'Current net worth', 'Patrimoine net actuel')} value={formatMoney(dashboard.currentNetWorth, dashboard.currency, true, locale)} detail={tr(locale, 'Assets less liabilities today', 'Actifs moins dettes aujourd’hui')} icon="◈" /><MetricCard label={tr(locale, '10-year net worth', 'Patrimoine net à 10 ans')} value={formatMoney(inTenYears.netWorth, dashboard.currency, true, locale)} detail={`${tr(locale, 'At age', 'À')} ${inTenYears.age} {tr(locale, 'years', 'ans')}`} icon="↗" tone="success" /><MetricCard label={tr(locale, 'Passive cash received', 'Revenu passif encaissé')} value={formatMoney(dashboard.passiveMonthlyIncome, dashboard.currency, true, locale)} detail={tr(locale, 'Rent, dividends and royalties—not asset appreciation', 'Loyers, dividendes et redevances — hors hausse des actifs')} icon="⌁" /><MetricCard label={tr(locale, 'Estimated portfolio growth', 'Croissance estimée du portefeuille')} value={formatMoney(dashboard.expectedMonthlyPortfolioGrowth, dashboard.currency, true, locale)} detail={tr(locale, 'Average monthly appreciation—not cash received', 'Hausse mensuelle moyenne — non encaissée')} icon="↗" tone="success" /><MetricCard label={tr(locale, 'Success probability', 'Probabilité de réussite')} value={`${Math.round(dashboard.probabilityOfSuccess * 100)}%`} detail={tr(locale, 'Monte Carlo solvency rate', 'Taux de solvabilité Monte Carlo')} icon="◌" tone={dashboard.probabilityOfSuccess >= .8 ? 'success' : 'warning'} /></div><div className="grid gap-4 xl:grid-cols-[1.65fr_1fr]"><article className="section-card"><div className="flex flex-wrap items-start justify-between gap-4"><div><p className="eyebrow">{tr(locale, 'Wealth timeline', 'Évolution du patrimoine')}</p><h2 className="mt-1 text-lg font-semibold text-white">{tr(locale, 'Total wealth and value by category', 'Patrimoine total et détail par catégorie')}</h2><p className="mt-2 max-w-2xl text-sm leading-6 text-muted">{tr(locale, 'Each coloured area follows what you own. Property appreciation increases wealth without becoming cash income; its projected sale value is already included in the total line.', 'Chaque zone colorée suit ce que vous possédez. La hausse d’un bien immobilier augmente votre patrimoine sans devenir un revenu encaissé ; sa valeur de revente projetée est déjà comprise dans la ligne totale.')}</p></div><div className="flex items-center gap-3"><span className="rounded-full bg-sky/15 px-3 py-1 text-xs font-medium text-sky">{tr(locale, 'to age', 'jusqu’à')} {dashboard.timeline.at(-1)?.age} {tr(locale, 'years', 'ans')}</span>{scaleControl}</div></div><div className="mt-5"><NetWorthChart timeline={dashboard.timeline} currency={dashboard.currency} locale={locale} scale={timelineScale} /></div></article><article className="section-card"><p className="eyebrow">{tr(locale, 'Financial independence', 'Indépendance financière')}</p><p className="mt-2 text-2xl font-semibold text-white">{formatDate(dashboard.financialIndependenceDate, locale)}</p><p className="mt-2 text-sm leading-6 text-muted">{tr(locale, 'The first point where your projected wealth can support planned annual spending at your safe withdrawal rate.', 'Le premier moment où votre patrimoine projeté peut financer vos dépenses annuelles prévues selon votre taux de retrait sûr.')}</p><dl className="mt-6 space-y-3 border-t border-white/10 pt-5 text-sm"><div className="flex justify-between gap-4"><dt className="text-muted">{tr(locale, 'Retirement income', 'Revenu à la retraite')}</dt><dd className="font-medium text-mist">{formatMoney(dashboard.estimatedRetirementIncome, dashboard.currency, false, locale)}/{tr(locale, 'mo', 'mois')}</dd></div><div className="flex justify-between gap-4"><dt className="text-muted">{tr(locale, 'Real wealth change', 'Évolution réelle du patrimoine')}</dt><dd className={dashboard.inflationAdjustedPurchasingPowerChange >= 0 ? 'font-medium text-success' : 'font-medium text-warning'}>{dashboard.inflationAdjustedPurchasingPowerChange >= 0 ? '+' : ''}{Math.round(dashboard.inflationAdjustedPurchasingPowerChange)}%</dd></div></dl></article></div><div className="grid gap-4 xl:grid-cols-2"><article className="section-card"><p className="eyebrow">{tr(locale, 'Portfolio allocation', 'Répartition du portefeuille')}</p><h2 className="mt-1 text-lg font-semibold text-white">{tr(locale, 'What you own today', 'Ce que vous possédez aujourd’hui')}</h2><div className="mt-4"><AllocationChart allocation={dashboard.allocation} currency={dashboard.currency} /></div></article><AllocationStrategyCard allocation={dashboard.allocation} assessment={dashboard.allocationStrategy} locale={locale} strategies={allocationStrategies} onDelete={onDeleteStrategy} onSave={onSaveStrategy} /></div><article className="section-card"><div className="flex flex-wrap items-start justify-between gap-3"><div><p className="eyebrow">{tr(locale, 'Cash flow timeline', 'Évolution de la trésorerie')}</p><h2 className="mt-1 text-lg font-semibold text-white">{tr(locale, 'Annual surplus after planned saving', 'Excédent annuel après épargne prévue')}</h2></div>{scaleControl}</div><div className="mt-4"><CashFlowChart timeline={dashboard.timeline} currency={dashboard.currency} locale={locale} scale={timelineScale} /></div></article><Warnings items={dashboard.warnings} locale={locale} /></section>
}

function AllocationStrategyCard({ allocation, assessment, locale, strategies, onSave, onDelete }: { allocation: Dashboard['allocation']; assessment: Dashboard['allocationStrategy']; locale: Locale; strategies: AllocationStrategy[]; onSave: (strategyId: string | undefined, value: Record<string, unknown>) => Promise<void>; onDelete: (strategyId: string) => Promise<void> }) {
  const [editing, setEditing] = useState(false)
  const [strategyId, setStrategyId] = useState<string>()
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [effectiveFrom, setEffectiveFrom] = useState(new Date().toISOString().slice(0, 10))
  const [effectiveTo, setEffectiveTo] = useState('')
  const [targets, setTargets] = useState<Array<{ category: string; targetPercentage: string; tolerancePercentage: string }>>([])
  const begin = (strategy?: AllocationStrategy) => {
    setStrategyId(strategy?.id); setName(strategy?.name ?? ''); setDescription(strategy?.description ?? ''); setEffectiveFrom(strategy?.effectiveFrom ?? new Date().toISOString().slice(0, 10)); setEffectiveTo(strategy?.effectiveTo ?? '')
    setTargets(strategy?.targets.map(target => ({ category: target.category, targetPercentage: String(target.targetPercentage), tolerancePercentage: String(target.tolerancePercentage) })) ?? allocation.map(slice => ({ category: slice.name, targetPercentage: String(slice.percentage), tolerancePercentage: '5' })))
    setEditing(true)
  }
  const save = async () => { await onSave(strategyId, { name, description: description || null, effectiveFrom, effectiveTo: effectiveTo || null, targets: targets.filter(target => target.category.trim()).map(target => ({ category: target.category.trim(), targetPercentage: Number(target.targetPercentage), tolerancePercentage: Number(target.tolerancePercentage) })) }); setEditing(false) }
  const outOfRange = assessment?.targets.filter(target => target.state !== 'WithinRange').length ?? 0
  return <article className="section-card"><div className="flex items-start justify-between gap-3"><div><p className="eyebrow">{tr(locale, 'Target allocation', 'Allocation cible')}</p><h2 className="mt-1 text-lg font-semibold text-white">{assessment?.name ?? tr(locale, 'No active strategy', 'Aucune stratégie active')}</h2></div><button className="ghost-button px-3 py-2 text-xs" onClick={() => begin(assessment ? strategies.find(strategy => strategy.name === assessment.name && strategy.effectiveFrom === assessment.effectiveFrom) : undefined)}>{tr(locale, 'Manage', 'Gérer')}</button></div>{assessment ? <><p className={`mt-2 text-xs font-medium ${outOfRange ? 'text-warning' : 'text-success'}`}>{outOfRange ? tr(locale, `${outOfRange} category${outOfRange > 1 ? 'ies are' : ' is'} outside its band.`, `${outOfRange} catégorie${outOfRange > 1 ? 's sont' : ' est'} hors de sa plage.`) : tr(locale, 'Every target category is within its tolerance band.', 'Toutes les catégories cibles sont dans leur plage de tolérance.')}</p><div className="mt-4 space-y-2">{assessment.targets.map(target => <div className="rounded-xl bg-white/5 px-3 py-2.5" key={target.category}><div className="flex justify-between gap-3 text-sm"><span className="font-medium text-mist">{target.category}</span><span className={target.state === 'WithinRange' ? 'text-success' : 'text-warning'}>{target.actualPercentage}%</span></div><p className="mt-1 text-xs text-muted">{tr(locale, 'Target', 'Cible')} {target.targetPercentage}% ± {target.tolerancePercentage}% · {target.differencePercentage >= 0 ? '+' : ''}{target.differencePercentage} {tr(locale, 'pts', 'pts')}</p></div>)}</div></> : <p className="mt-3 text-sm leading-6 text-muted">{tr(locale, 'Define dated category targets and tolerance bands to monitor portfolio drift.', 'Définissez des cibles datées par catégorie et leurs marges de tolérance pour suivre les écarts.')}</p>}{editing && <div className="mt-5 border-t border-white/10 pt-4"><div className="grid gap-3 sm:grid-cols-2"><label className="text-xs text-muted">{tr(locale, 'Strategy name', 'Nom de la stratégie')}<input className="field mt-1" value={name} onChange={event => setName(event.target.value)} /></label><label className="text-xs text-muted">{tr(locale, 'Effective from', 'Effective à partir du')}<input className="field mt-1" type="date" value={effectiveFrom} onChange={event => setEffectiveFrom(event.target.value)} /></label><label className="text-xs text-muted">{tr(locale, 'End date', 'Date de fin')}<input className="field mt-1" type="date" value={effectiveTo} onChange={event => setEffectiveTo(event.target.value)} /></label><label className="text-xs text-muted">{tr(locale, 'Rationale (optional)', 'Raison (facultatif)')}<input className="field mt-1" value={description} onChange={event => setDescription(event.target.value)} /></label></div><div className="mt-4 space-y-2">{targets.map((target, index) => <div className="grid grid-cols-[1fr_72px_72px_auto] gap-2" key={`${target.category}-${index}`}><input aria-label={tr(locale, 'Category', 'Catégorie')} className="field" placeholder={tr(locale, 'Category', 'Catégorie')} value={target.category} onChange={event => setTargets(items => items.map((item, itemIndex) => itemIndex === index ? { ...item, category: event.target.value } : item))} /><input aria-label={tr(locale, 'Target percentage', 'Pourcentage cible')} className="field" min="0" max="100" type="number" value={target.targetPercentage} onChange={event => setTargets(items => items.map((item, itemIndex) => itemIndex === index ? { ...item, targetPercentage: event.target.value } : item))} /><input aria-label={tr(locale, 'Tolerance percentage', 'Tolérance')} className="field" min="0" max="100" type="number" value={target.tolerancePercentage} onChange={event => setTargets(items => items.map((item, itemIndex) => itemIndex === index ? { ...item, tolerancePercentage: event.target.value } : item))} /><button className="text-xs text-danger" onClick={() => setTargets(items => items.filter((_, itemIndex) => itemIndex !== index))}>×</button></div>)}</div><div className="mt-3 flex flex-wrap gap-2"><button className="ghost-button px-3 py-2 text-xs" onClick={() => setTargets(items => [...items, { category: '', targetPercentage: '0', tolerancePercentage: '5' }])}>{tr(locale, 'Add category', 'Ajouter une catégorie')}</button><button className="primary-button px-4 py-2 text-xs" disabled={!name.trim() || !targets.some(target => target.category.trim())} onClick={() => void save()}>{tr(locale, 'Save strategy', 'Enregistrer la stratégie')}</button><button className="ghost-button px-3 py-2 text-xs" onClick={() => setEditing(false)}>{tr(locale, 'Cancel', 'Annuler')}</button>{strategyId && <button className="ml-auto text-xs text-danger" onClick={() => void onDelete(strategyId).then(() => setEditing(false))}>{tr(locale, 'Delete version', 'Supprimer la version')}</button>}</div></div>}</article>
}

function ActualNetWorthHistorySection({ history, currency, locale }: { history: NetWorthSnapshot[]; currency: string; locale: Locale }) {
  return <article className="section-card"><div className="flex items-start justify-between gap-4"><div><p className="eyebrow">{tr(locale, 'Actual history', 'Historique réel')}</p><h2 className="mt-1 text-lg font-semibold text-white">{tr(locale, 'Your observed net worth over time', 'Votre patrimoine net observé dans le temps')}</h2></div><span className="rounded-full bg-success/10 px-3 py-1 text-xs font-medium text-success">{history.length} {tr(locale, 'point(s)', 'point(s)')}</span></div>{history.length ? <div className="mt-5"><ActualNetWorthChart history={history} currency={currency} locale={locale} /></div> : <p className="mt-5 rounded-xl bg-white/5 px-4 py-5 text-sm leading-6 text-muted">{tr(locale, 'The first point will be saved the next time LifeLedger starts.', 'Le premier point sera enregistré au prochain démarrage de LifeLedger.')}</p>}</article>
}

/** Explains each projection model in plain language without hiding the calculation behind financial jargon. */
function SimulationGuide({ mode, locale, onModeChange }: { mode: SimulationMode; locale: Locale; onModeChange: (mode: SimulationMode) => void }) {
  const details: Record<SimulationMode, { title: string; summary: string; question: string; formula: string; formulaMeaning: string; use: string; caution: string }> = {
    Deterministic: {
      title: tr(locale, 'Planned path', 'Trajectoire prévue'),
      summary: tr(locale, 'One future where your central assumptions happen as entered.', 'Un seul futur dans lequel vos hypothèses centrales se réalisent comme vous les avez saisies.'),
      question: tr(locale, '“What happens if everything broadly goes as planned?”', '« Que se passe-t-il si tout se déroule globalement comme prévu ? »'),
      formula: tr(locale, 'New capital = previous capital × (1 + monthly return) + money in − money out', 'Nouveau capital = capital précédent × (1 + rendement mensuel) + entrées − sorties'),
      formulaMeaning: tr(locale, 'The same expected return and configured inflation are used throughout the projection.', 'Le même rendement attendu et l’inflation configurée sont utilisés pendant toute la projection.'),
      use: tr(locale, 'Best for understanding your reference budget and checking the effect of a life decision.', 'Idéal pour comprendre votre budget de référence et mesurer l’effet d’une décision de vie.'),
      caution: tr(locale, 'It is easy to read, but real markets never follow one perfectly regular path.', 'Il est facile à lire, mais les marchés réels ne suivent jamais une trajectoire parfaitement régulière.')
    },
    MonteCarlo: {
      title: 'Monte Carlo',
      summary: tr(locale, 'Many possible futures, including good years, bad years and their order.', 'De nombreux futurs possibles, avec de bonnes années, de mauvaises années et un ordre différent.'),
      question: tr(locale, '“Does my plan still work when life and markets are less predictable?”', '« Mon plan tient-il encore quand la vie et les marchés sont moins prévisibles ? »'),
      formula: tr(locale, 'Return for one year = expected return + random variation × volatility', 'Rendement d’une année = rendement attendu + variation aléatoire × volatilité'),
      formulaMeaning: tr(locale, 'Success probability = paths where your net worth never falls below zero ÷ all tested paths.', 'Probabilité de réussite = trajectoires où votre patrimoine ne passe jamais sous zéro ÷ toutes les trajectoires testées.'),
      use: tr(locale, 'Best for measuring risk and seeing whether your safety margin is sufficient.', 'Idéal pour mesurer le risque et voir si votre marge de sécurité est suffisante.'),
      caution: tr(locale, 'A high percentage is not a guarantee: the result depends on the returns and volatility you entered.', 'Un pourcentage élevé n’est pas une garantie : le résultat dépend des rendements et de la volatilité saisis.')
    },
    Historical: {
      title: tr(locale, 'Illustrative historical cycle', 'Cycle historique illustratif'),
      summary: tr(locale, 'One future using a built-in sequence of changing returns and inflation.', 'Un seul futur utilisant une suite intégrée de rendements et d’inflation variables.'),
      question: tr(locale, '“How would my plan react to a repeating cycle of rises and falls?”', '« Comment mon plan réagit-il à un cycle de hausses et de baisses ? »'),
      formula: tr(locale, 'Return for year N = rate N in the 12-year cycle; after year 12, the cycle starts again', 'Rendement de l’année N = taux N du cycle de 12 ans ; après l’année 12, le cycle recommence'),
      formulaMeaning: tr(locale, 'Inflation follows the paired illustrative cycle instead of your constant inflation assumption.', 'L’inflation suit le cycle illustratif associé au lieu de votre hypothèse d’inflation constante.'),
      use: tr(locale, 'Useful as a simple stress test with alternating strong and difficult years.', 'Utile comme test de résistance simple avec une alternance d’années favorables et difficiles.'),
      caution: tr(locale, 'This is not yet the exact history of a real stock index and it does not predict the next cycle.', 'Ce n’est pas encore l’historique exact d’un indice boursier réel et cela ne prédit pas le prochain cycle.')
    }
  }
  const selected = details[mode]
  return <div className="mt-6 border-t border-white/10 pt-6">
    <div className="grid gap-3 lg:grid-cols-3">{(['Deterministic', 'MonteCarlo', 'Historical'] as SimulationMode[]).map((item) => <button aria-pressed={mode === item} className={`rounded-2xl border p-4 text-left transition ${mode === item ? 'border-sky/50 bg-sky/10 ring-1 ring-sky/20' : 'border-white/10 bg-white/5 hover:bg-white/10'}`} key={item} onClick={() => onModeChange(item)}><div className="flex items-start justify-between gap-3"><p className="font-semibold text-white">{details[item].title}</p><span className={`mt-1 h-2.5 w-2.5 shrink-0 rounded-full ${mode === item ? 'bg-sky' : 'bg-white/20'}`} /></div><p className="mt-2 text-sm leading-6 text-muted">{details[item].summary}</p></button>)}</div>
    <div className="mt-4 rounded-2xl border border-white/10 bg-white/5 p-5"><p className="text-base font-medium text-mist">{selected.question}</p><div className="mt-5 grid gap-4 lg:grid-cols-2"><div><p className="eyebrow">{tr(locale, 'The simple calculation', 'Le calcul simplement')}</p><p className="mt-2 rounded-xl bg-ink/40 px-4 py-3 text-sm font-medium leading-6 text-white">{selected.formula}</p><p className="mt-2 text-xs leading-5 text-muted">{selected.formulaMeaning}</p></div><div className="space-y-3 text-sm leading-6"><p><span className="font-medium text-mist">{tr(locale, 'When to use it:', 'Quand l’utiliser :')}</span> <span className="text-muted">{selected.use}</span></p><p><span className="font-medium text-warning">{tr(locale, 'Keep in mind:', 'À garder en tête :')}</span> <span className="text-muted">{selected.caution}</span></p></div></div><p className="mt-4 border-t border-white/10 pt-4 text-xs leading-5 text-muted">{tr(locale, 'LifeLedger calculates month by month, converts every amount into your default currency, then subtracts the remaining debts to obtain net worth.', 'LifeLedger calcule mois par mois, convertit chaque montant dans votre devise par défaut, puis retire les dettes restantes pour obtenir le patrimoine net.')}</p></div>
  </div>
}

function SimulatorPage({ dashboard, simulation, locale, onRun }: { dashboard: Dashboard; simulation?: Simulation; locale: Locale; onRun: (mode: SimulationMode) => Promise<void> }) {
  const [mode, setMode] = useState<SimulationMode>('MonteCarlo')
  const [timelineScale, setTimelineScale] = useState<'year' | 'age'>('age')
  const active = simulation?.mode === mode ? simulation : undefined
  return <section className="space-y-5"><div><p className="eyebrow">{tr(locale, 'Scenario engine', 'Moteur de scénarios')}</p><h1 className="mt-2 text-3xl font-semibold text-white">{tr(locale, 'Test the resilience of your plan.', 'Testez la solidité de votre plan.')}</h1><p className="mt-2 max-w-2xl text-sm leading-6 text-muted">{tr(locale, 'Run separate models against the same private data. Assumptions remain transparent.', 'Exécutez différents modèles sur les mêmes données privées. Les hypothèses restent transparentes.')}</p></div><div className="section-card"><div className="flex flex-col justify-between gap-5 md:flex-row md:items-center"><div className="flex flex-wrap gap-2">{(['Deterministic', 'MonteCarlo', 'Historical'] as SimulationMode[]).map((item) => <button className={`rounded-xl px-4 py-2.5 text-sm font-medium transition ${mode === item ? 'bg-white text-ink' : 'bg-white/5 text-muted hover:bg-white/10 hover:text-mist'}`} key={item} onClick={() => setMode(item)}>{item === 'MonteCarlo' ? 'Monte Carlo' : item === 'Historical' ? tr(locale, 'Historical', 'Historique') : tr(locale, 'Deterministic', 'Déterministe')}</button>)}</div><button className="primary-button" onClick={() => void onRun(mode)}>{tr(locale, 'Run', 'Lancer')} {mode === 'MonteCarlo' ? 'Monte Carlo' : tr(locale, 'simulation', 'la simulation')}</button></div><SimulationGuide mode={mode} locale={locale} onModeChange={setMode} /><div className="mt-8 grid gap-4 md:grid-cols-3"><div className="rounded-2xl bg-white/5 p-4"><p className="eyebrow">{tr(locale, 'Model', 'Modèle')}</p><p className="mt-2 text-lg font-medium text-white">{mode === 'MonteCarlo' ? tr(locale, 'Variable market paths', 'Trajectoires de marché variables') : mode === 'Historical' ? tr(locale, 'Historical return cycle', 'Cycle de rendements historiques') : tr(locale, 'Expected returns', 'Rendements attendus')}</p></div><div className="rounded-2xl bg-white/5 p-4"><p className="eyebrow">{tr(locale, 'Horizon', 'Horizon')}</p><p className="mt-2 text-lg font-medium text-white">{tr(locale, 'to age', 'jusqu’à')} {dashboard.timeline.at(-1)?.age} {tr(locale, 'years', 'ans')}</p></div><div className="rounded-2xl bg-white/5 p-4"><p className="eyebrow">{tr(locale, 'Status', 'État')}</p><p className="mt-2 text-lg font-medium text-white">{active ? `${active.runs.toLocaleString(locale)} ${tr(locale, 'paths complete', 'trajectoires terminées')}` : tr(locale, 'Ready to run', 'Prêt à lancer')}</p></div></div></div>{active ? <><div className="grid gap-4 md:grid-cols-3"><MetricCard label={tr(locale, 'Probability of success', 'Probabilité de réussite')} value={`${Math.round(active.probabilityOfSuccess * 100)}%`} detail={tr(locale, 'Paths that never turn negative', 'Trajectoires restant positives')} icon="◌" tone={active.probabilityOfSuccess >= .8 ? 'success' : 'warning'} /><MetricCard label={tr(locale, 'Median terminal value', 'Valeur finale médiane')} value={formatMoney(active.terminalNetWorths[Math.floor(active.terminalNetWorths.length / 2)] ?? 0, dashboard.currency, true, locale)} detail={tr(locale, 'Across simulated outcomes', 'Tous scénarios simulés confondus')} icon="◇" /><MetricCard label={tr(locale, 'Simulation runs', 'Simulations')} value={active.runs.toLocaleString(locale)} detail={tr(locale, 'Deterministic random sampling', 'Échantillonnage aléatoire déterministe')} icon="⌁" /></div><article className="section-card"><div className="flex flex-wrap items-start justify-between gap-3"><div><p className="eyebrow">{tr(locale, 'Projected path', 'Trajectoire projetée')}</p><h2 className="mt-1 text-lg font-semibold text-white">{tr(locale, 'Reference timeline for this simulation', 'Trajectoire de référence')}</h2></div><div className="flex rounded-xl bg-white/5 p-1 text-xs"><button className={`rounded-lg px-3 py-1.5 ${timelineScale === 'age' ? 'bg-white text-ink' : 'text-muted hover:text-mist'}`} onClick={() => setTimelineScale('age')}>{tr(locale, 'Age', 'Âge')}</button><button className={`rounded-lg px-3 py-1.5 ${timelineScale === 'year' ? 'bg-white text-ink' : 'text-muted hover:text-mist'}`} onClick={() => setTimelineScale('year')}>{tr(locale, 'Year', 'Année')}</button></div></div><div className="mt-4"><NetWorthChart timeline={active.timeline} currency={dashboard.currency} locale={locale} scale={timelineScale} /></div></article><Warnings items={active.warnings} locale={locale} /></> : <article className="section-card text-center"><p className="text-2xl">⌁</p><h2 className="mt-3 font-semibold text-white">{tr(locale, 'Choose a model and run it.', 'Choisissez un modèle puis lancez-le.')}</h2><p className="mx-auto mt-2 max-w-md text-sm leading-6 text-muted">{tr(locale, 'Monte Carlo uses your portfolio return and volatility assumptions. It runs entirely in the API process without a third-party service.', 'Monte Carlo utilise vos hypothèses de rendement et de volatilité. Il s’exécute entièrement dans l’API, sans service tiers.')}</p></article>}</section>
}

function ScenariosPage({ scenarios, selectedId, name, creating, locale, onName, onSubmit, onSelect, onCompare }: { scenarios: ScenarioSummary[]; selectedId: string; name: string; creating: boolean; locale: Locale; onName: (name: string) => void; onSubmit: (event: FormEvent) => Promise<void>; onSelect: (id: string) => Promise<void>; onCompare: () => void }) {
  return <section className="space-y-5"><div className="flex flex-wrap items-end justify-between gap-4"><div><p className="eyebrow">{tr(locale, 'Unlimited scenarios', 'Scénarios illimités')}</p><h1 className="mt-2 text-3xl font-semibold text-white">{tr(locale, 'Compare possible futures.', 'Comparez les futurs possibles.')}</h1><p className="mt-2 max-w-2xl text-sm leading-6 text-muted">{tr(locale, 'New scenarios fork from the selected plan so you can model a relocation, early retirement, new child or business without touching the baseline.', 'Chaque scénario est une copie du plan sélectionné : modélisez un déménagement, une retraite anticipée, un enfant ou une entreprise sans modifier votre base.')}</p></div><button className="ghost-button" disabled={scenarios.length < 2} onClick={onCompare}>{tr(locale, 'Compare scenarios', 'Comparer les scénarios')}</button></div><form className="section-card flex flex-col gap-3 sm:flex-row" onSubmit={(event) => void onSubmit(event)}><input className="field" value={name} onChange={(event) => onName(event.target.value)} placeholder={tr(locale, 'Name a new what-if scenario', 'Nommez le nouveau scénario')} /><button className="primary-button shrink-0" disabled={creating || !name.trim()}>{creating ? tr(locale, 'Creating…', 'Création…') : tr(locale, 'Fork scenario', 'Créer une variante')}</button></form><div className="grid gap-4 md:grid-cols-2">{scenarios.map((scenario) => <button className={`section-card glass-hover text-left ${scenario.id === selectedId ? 'ring-1 ring-sky/70' : ''}`} key={scenario.id} onClick={() => void onSelect(scenario.id)}><div className="flex items-start justify-between gap-4"><div><p className="text-lg font-semibold text-white">{scenario.name}</p><p className="mt-2 text-sm leading-6 text-muted">{scenario.description || tr(locale, 'No description yet.', 'Aucune description pour le moment.')}</p></div>{scenario.isBaseline && <span className="rounded-full bg-sky/15 px-2.5 py-1 text-xs font-semibold text-sky">{tr(locale, 'Baseline', 'Référence')}</span>}</div><p className="mt-6 text-xs font-semibold uppercase tracking-[0.1em] text-muted">{tr(locale, 'Starts', 'Débute le')} {new Date(scenario.startsOn).toLocaleDateString(locale)}</p></button>)}</div></section>
}

function ScenarioComparisonPage({ scenarios, locale, onBack }: { scenarios: ScenarioSummary[]; locale: Locale; onBack: () => void }) {
  const [selectedIds, setSelectedIds] = useState<string[]>(() => scenarios.slice(0, 2).map((scenario) => scenario.id))
  const [dashboards, setDashboards] = useState<Dashboard[]>([])
  const [loadingComparison, setLoadingComparison] = useState(true)
  useEffect(() => { void (async () => { setLoadingComparison(true); setDashboards(await Promise.all(selectedIds.map((id) => api.dashboard(id))).catch(() => [])); setLoadingComparison(false) })() }, [selectedIds])
  function toggle(id: string) { setSelectedIds((current) => current.includes(id) ? current.filter((item) => item !== id) : current.length < 4 ? [...current, id] : current) }
  return <section className="space-y-5"><div className="flex flex-wrap items-end justify-between gap-4"><div><p className="eyebrow">{tr(locale, 'Scenario comparison', 'Comparaison de scénarios')}</p><h1 className="mt-2 text-3xl font-semibold text-white">{tr(locale, 'See the cost of each choice.', 'Voyez le coût de chaque choix.')}</h1></div><button className="ghost-button" onClick={onBack}>{tr(locale, 'Back to scenarios', 'Retour aux scénarios')}</button></div><article className="section-card"><p className="text-sm text-muted">{tr(locale, 'Select two to four scenarios.', 'Sélectionnez de deux à quatre scénarios.')}</p><div className="mt-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">{scenarios.map((scenario) => <label className={`rounded-xl border p-3 text-sm ${selectedIds.includes(scenario.id) ? 'border-sky/50 bg-sky/10 text-mist' : 'border-white/10 bg-white/5 text-muted'}`} key={scenario.id}><input checked={selectedIds.includes(scenario.id)} className="mr-2" type="checkbox" onChange={() => toggle(scenario.id)} />{scenario.name}</label>)}</div></article>{loadingComparison ? <Loading locale={locale} /> : <div className="grid gap-4 xl:grid-cols-2">{dashboards.map((item) => <article className="section-card" key={item.scenarioId}><p className="eyebrow">{item.scenarioName}</p><div className="mt-4 grid grid-cols-2 gap-4 text-sm"><div><p className="text-muted">{tr(locale, 'Future net worth', 'Patrimoine futur')}</p><p className="mt-1 text-lg font-semibold text-white">{formatMoney(item.futureNetWorth, item.currency, true, locale)}</p></div><div><p className="text-muted">{tr(locale, 'Success', 'Réussite')}</p><p className="mt-1 text-lg font-semibold text-white">{Math.round(item.probabilityOfSuccess * 100)}%</p></div><div><p className="text-muted">{tr(locale, 'Financial independence', 'Indépendance financière')}</p><p className="mt-1 font-medium text-mist">{formatDate(item.financialIndependenceDate, locale)}</p></div><div><p className="text-muted">{tr(locale, 'Final age', 'Âge final')}</p><p className="mt-1 font-medium text-mist">{item.timeline.at(-1)?.age} {tr(locale, 'years', 'ans')}</p></div></div><div className="mt-5"><NetWorthChart timeline={item.timeline} currency={item.currency} locale={locale} scale="age" /></div></article>)}</div>}</section>
}

const warningCopy: Record<Locale, { heading: string; empty: string }> = {
  en: { heading: 'Signals to review', empty: 'No risk signals triggered by the current rules.' },
  fr: { heading: 'Points à surveiller', empty: 'Aucun signal de risque déclenché par les règles actuelles.' },
  pl: { heading: 'Punkty wymagające uwagi', empty: 'Obecne reguły nie wykryły żadnych sygnałów ryzyka.' },
  de: { heading: 'Zu beachtende Punkte', empty: 'Die aktuellen Regeln haben keine Risikosignale ausgelöst.' },
  nl: { heading: 'Aandachtspunten', empty: 'De huidige regels hebben geen risicosignalen opgeleverd.' }
}

function warningText(warning: SimulationWarning, locale: Locale) {
  const value = new Intl.NumberFormat(locale, { maximumFractionDigits: 0 }).format(warning.value ?? 0)
  const translations: Record<SimulationWarning['code'], Record<Locale, string>> = {
    'insolvency-age': {
      en: `You may run out of money at age ${value}.`, fr: `Vous risquez de manquer d’argent à ${value} ans.`,
      pl: `Możesz wyczerpać środki w wieku ${value} lat.`, de: `Ihr Geld könnte im Alter von ${value} Jahren aufgebraucht sein.`, nl: `Mogelijk raakt uw geld op wanneer u ${value} jaar bent.`
    },
    'purchasing-power-drop': {
      en: `Your purchasing power drops by ${value}% in real terms.`, fr: `Votre pouvoir d’achat baisse de ${value} % en valeur réelle.`,
      pl: `Siła nabywcza spada realnie o ${value}%.`, de: `Ihre Kaufkraft sinkt real um ${value} %.`, nl: `Uw koopkracht daalt reëel met ${value}%.`
    },
    'low-emergency-fund': {
      en: 'Your available emergency fund covers less than three months of current expenses.', fr: 'Votre épargne de sécurité disponible couvre moins de trois mois de dépenses actuelles.',
      pl: 'Dostępna poduszka finansowa pokrywa mniej niż trzy miesiące obecnych wydatków.', de: 'Ihre verfügbare Notfallreserve deckt weniger als drei Monate der aktuellen Ausgaben.', nl: 'Uw beschikbare noodbuffer dekt minder dan drie maanden van de huidige uitgaven.'
    },
    'high-debt-payments': {
      en: 'Debt payments exceed 40% of your declared monthly income.', fr: 'Les remboursements de dettes dépassent 40 % de vos revenus mensuels déclarés.',
      pl: 'Spłaty zadłużenia przekraczają 40% zadeklarowanego miesięcznego dochodu.', de: 'Die Schuldentilgungen übersteigen 40 % Ihres angegebenen Monatseinkommens.', nl: 'De schuldaflossingen bedragen meer dan 40% van uw opgegeven maandinkomen.'
    },
    'low-monte-carlo-success': {
      en: `Only ${value}% of simulated paths remain solvent through the planned horizon.`, fr: `Seulement ${value} % des trajectoires simulées restent solvables jusqu’à la fin de la période prévue.`,
      pl: `Tylko ${value}% symulowanych ścieżek pozostaje wypłacalnych do końca planowanego okresu.`, de: `Nur ${value} % der simulierten Verläufe bleiben bis zum Ende des Planungshorizonts zahlungsfähig.`, nl: `Slechts ${value}% van de gesimuleerde trajecten blijft solvabel tot het einde van de geplande periode.`
    }
  }
  return translations[warning.code][locale]
}

function Warnings({ items, locale }: { items: SimulationWarning[]; locale: Locale }) { return <article className="section-card"><p className="eyebrow">{warningCopy[locale].heading}</p><div className="mt-4 space-y-3">{items.length ? items.map((item) => <div className="flex gap-3 rounded-xl bg-warning/10 px-4 py-3 text-sm leading-6 text-warning" key={`${item.code}:${item.value ?? ''}`}><span>!</span><p>{warningText(item, locale)}</p></div>) : <div className="rounded-xl bg-success/10 px-4 py-3 text-sm text-success">{warningCopy[locale].empty}</div>}</div></article> }
function Loading({ locale }: { locale: Locale }) { return <div className="grid min-h-80 place-items-center"><p className="text-sm text-muted">{tr(locale, 'Loading your local ledger…', 'Chargement de votre registre local…')}</p></div> }
function Empty({ locale }: { locale: Locale }) { return <div className="grid min-h-80 place-items-center text-center"><div><p className="text-3xl">◈</p><h1 className="mt-3 text-xl font-semibold text-white">{tr(locale, 'No scenario available', 'Aucun scénario disponible')}</h1><p className="mt-2 text-sm text-muted">{tr(locale, 'Start the API with demo seeding enabled or import a private export.', 'Activez les données de démonstration ou importez une sauvegarde privée.')}</p></div></div> }
