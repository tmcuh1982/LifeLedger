import type { AllocationStrategy, AssetCategory, AssetDossierResponse, AssetProfileDefinition, AssetProfileDefinitionInput, AssetValuation, BankAccount, BankImporterDefinition, BankSpendingAverage, BankStatementPreview, BankTransaction, BankTransactionReview, Dashboard, LifeLedgerExport, NetWorthSnapshot, Profile, ScenarioData, ScenarioSummary, Simulation, SimulationMode, UpdateBankTransaction } from './types'

const baseUrl = import.meta.env.VITE_API_URL ?? '/api'

async function request<T>(path: string, options?: RequestInit): Promise<T> {
  const isForm = options?.body instanceof FormData
  const response = await fetch(`${baseUrl}${path}`, {
    headers: { ...(!isForm ? { 'Content-Type': 'application/json' } : {}), ...options?.headers },
    ...options,
  })
  if (!response.ok) throw new Error((await response.json().catch(() => null))?.message ?? `Request failed (${response.status})`)
  if (response.status === 204) return undefined as T
  if (!response.headers.get('content-type')?.includes('application/json'))
    throw new Error('The local server returned an outdated page. Restart LifeLedger, then reload this page.')
  return response.json() as Promise<T>
}

export const api = {
  scenarios: () => request<ScenarioSummary[]>('/scenarios'),
  profiles: () => request<Profile[]>('/profiles'),
  profile: (id: string) => request<Profile>(`/profiles/${id}`),
  updateProfile: (profile: Profile) => request<Profile>(`/profiles/${profile.id}`, { method: 'PUT', body: JSON.stringify(profile) }),
  currencies: () => request<Array<{ code: string; unitsPerEuro: number; updatedAt: string; source: string; isStale: boolean }>>('/currencies'),
  assetCategories: () => request<AssetCategory[]>('/asset-categories'),
  assetProfileDefinitions: () => request<AssetProfileDefinition[]>('/asset-profile-definitions'),
  createAssetProfileDefinition: (definition: AssetProfileDefinitionInput) => request<AssetProfileDefinition>('/asset-profile-definitions', { method: 'POST', body: JSON.stringify(definition) }),
  updateAssetProfileDefinition: (key: string, definition: AssetProfileDefinitionInput) => request<AssetProfileDefinition>(`/asset-profile-definitions/${encodeURIComponent(key)}`, { method: 'PUT', body: JSON.stringify(definition) }),
  deleteAssetProfileDefinition: (key: string) => request<void>(`/asset-profile-definitions/${encodeURIComponent(key)}`, { method: 'DELETE' }),
  createAssetCategory: (name: string) => request<AssetCategory>('/asset-categories', { method: 'POST', body: JSON.stringify({ name }) }),
  renameAssetCategory: (currentName: string, name: string) => request<AssetCategory>(`/asset-categories/${encodeURIComponent(currentName)}`, { method: 'PUT', body: JSON.stringify({ name }) }),
  deleteAssetCategory: (name: string) => request<void>(`/asset-categories/${encodeURIComponent(name)}`, { method: 'DELETE' }),
  refreshCurrencies: () => request<Array<{ code: string; unitsPerEuro: number; updatedAt: string; source: string; isStale: boolean }>>('/currencies/refresh', { method: 'POST' }),
  refreshMarketPrices: () => request<Array<{ assetId: string; ticker: string; updated: boolean; price?: number; currency?: string; error?: string }>>('/market/refresh', { method: 'POST' }),
  assetHistory: (assetId: string) => request<Array<{ capturedAt: string; price: number; currency: string; source: string }>>(`/assets/${assetId}/history`),
  assetDossier: (assetId: string) => request<AssetDossierResponse>(`/assets/${assetId}/dossier`),
  assetValuations: (assetId: string) => request<AssetValuation[]>(`/assets/${assetId}/valuations`),
  createAssetDossier: (scenarioId: string, dossier: Record<string, unknown>) => request<AssetDossierResponse>(`/scenarios/${scenarioId}/asset-dossiers`, { method: 'POST', body: JSON.stringify(dossier) }),
  updateAssetDossier: (assetId: string, dossier: Record<string, unknown>) => request<AssetDossierResponse>(`/assets/${assetId}/dossier`, { method: 'PUT', body: JSON.stringify(dossier) }),
  resetMarketHistory: () => request<void>('/market/history', { method: 'DELETE' }),
  netWorthHistory: (scenarioId: string) => request<NetWorthSnapshot[]>(`/scenarios/${scenarioId}/net-worth-history`),
  resetNetWorthHistory: () => request<void>('/net-worth-history', { method: 'DELETE' }),
  restoreDemo: () => request<{ datasetVersion: number; profileId: string; scenarioId: string }>('/demo/restore', { method: 'POST' }),
  deleteAllData: () => request<void>('/data', { method: 'DELETE' }),
  dashboard: (id: string) => request<Dashboard>(`/scenarios/${id}/dashboard`),
  allocationStrategies: (scenarioId: string) => request<AllocationStrategy[]>(`/scenarios/${scenarioId}/allocation-strategies`),
  createAllocationStrategy: (scenarioId: string, strategy: Record<string, unknown>) => request<AllocationStrategy>(`/scenarios/${scenarioId}/allocation-strategies`, { method: 'POST', body: JSON.stringify(strategy) }),
  updateAllocationStrategy: (strategyId: string, strategy: Record<string, unknown>) => request<AllocationStrategy>(`/allocation-strategies/${strategyId}`, { method: 'PUT', body: JSON.stringify(strategy) }),
  deleteAllocationStrategy: (strategyId: string) => request<void>(`/allocation-strategies/${strategyId}`, { method: 'DELETE' }),
  scenarioData: (id: string) => request<ScenarioData>(`/scenarios/${id}/data`),
  simulation: (id: string, mode: SimulationMode) => request<Simulation>(`/scenarios/${id}/simulate`, { method: 'POST', body: JSON.stringify({ mode }) }),
  createScenario: (profileId: string, name: string, parentScenarioId?: string) => request<{ id: string }>('/scenarios', { method: 'POST', body: JSON.stringify({ profileId, name, parentScenarioId }) }),
  createItem: (scenarioId: string, resource: string, item: Record<string, unknown>) => request<void>(`/scenarios/${scenarioId}/${resource === 'assetSales' ? 'asset-sales' : resource}`, { method: 'POST', body: JSON.stringify(item) }),
  updateItem: (resource: string, id: string, item: Record<string, unknown>) => request<void>(`/${resource === 'assetSales' ? 'asset-sales' : resource}/${id}`, { method: 'PUT', body: JSON.stringify(item) }),
  deleteItem: (resource: string, id: string) => request<void>(`/${resource === 'assetSales' ? 'asset-sales' : resource}/${id}`, { method: 'DELETE' }),
  exportData: () => request<LifeLedgerExport>('/export'),
  importData: (document: LifeLedgerExport, replaceExisting: boolean) => request<{ id: string }>('/import', { method: 'POST', body: JSON.stringify({ document, replaceExisting }) }),
  bankImporters: () => request<BankImporterDefinition[]>('/bank-importers'),
  bankAccounts: (scenarioId: string) => request<BankAccount[]>(`/scenarios/${scenarioId}/bank-accounts`),
  bankTransactions: (scenarioId: string) => request<BankTransaction[]>(`/scenarios/${scenarioId}/bank-transactions`),
  bankSpendingAverages: (scenarioId: string) => request<BankSpendingAverage[]>(`/scenarios/${scenarioId}/bank-spending-averages`),
  applyBankSpendingAverage: (scenarioId: string, category: string, currency: string, name: string, indexedToInflation = true) => request<BankSpendingAverage>(`/scenarios/${scenarioId}/bank-spending-averages/${encodeURIComponent(category)}/apply`, { method: 'POST', body: JSON.stringify({ currency, name, indexedToInflation }) }),
  updateBankTransaction: (transactionId: string, update: UpdateBankTransaction) => request<BankTransaction>(`/bank-transactions/${transactionId}`, { method: 'PUT', body: JSON.stringify({ ...update, linkedAssetId: update.linkedAssetId || null, linkedInvestmentPlanId: update.linkedInvestmentPlanId || null, newLinkedAssetValue: update.newLinkedAssetValue ?? null, assetValuedOn: update.assetValuedOn || null }) }),
  previewBankStatement: (scenarioId: string, bankKey: string, file: File) => {
    const body = new FormData(); body.append('bankKey', bankKey); body.append('file', file)
    return request<BankStatementPreview>(`/scenarios/${scenarioId}/bank-statements/preview`, { method: 'POST', body })
  },
  commitBankStatement: (scenarioId: string, bankKey: string, accountName: string, confirmedCurrency: string, linkedAssetId: string | undefined, reviews: BankTransactionReview[], file: File) => {
    const body = new FormData()
    body.append('file', file)
    body.append('request', JSON.stringify({ scenarioId, bankKey, accountName, confirmedCurrency, linkedAssetId: linkedAssetId || null, reviews: reviews.map((review) => ({ ...review, linkedAssetId: review.linkedAssetId || null, linkedInvestmentPlanId: review.linkedInvestmentPlanId || null })) }))
    return request<{ bankAccountId: string; importId: string; importedTransactions: number; skippedDuplicates: number }>('/bank-statements/commit', { method: 'POST', body })
  },
}
