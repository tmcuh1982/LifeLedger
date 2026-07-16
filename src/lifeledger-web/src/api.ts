import type { Dashboard, LifeLedgerExport, NetWorthSnapshot, Profile, ScenarioData, ScenarioSummary, Simulation, SimulationMode } from './types'

const baseUrl = import.meta.env.VITE_API_URL ?? '/api'

async function request<T>(path: string, options?: RequestInit): Promise<T> {
  const response = await fetch(`${baseUrl}${path}`, {
    headers: { 'Content-Type': 'application/json', ...options?.headers },
    ...options,
  })
  if (!response.ok) throw new Error((await response.json().catch(() => null))?.message ?? `Request failed (${response.status})`)
  return response.status === 204 ? (undefined as T) : response.json() as Promise<T>
}

export const api = {
  scenarios: () => request<ScenarioSummary[]>('/scenarios'),
  profiles: () => request<Profile[]>('/profiles'),
  profile: (id: string) => request<Profile>(`/profiles/${id}`),
  updateProfile: (profile: Profile) => request<Profile>(`/profiles/${profile.id}`, { method: 'PUT', body: JSON.stringify(profile) }),
  currencies: () => request<Array<{ code: string; unitsPerEuro: number; updatedAt: string; source: string; isStale: boolean }>>('/currencies'),
  refreshCurrencies: () => request<Array<{ code: string; unitsPerEuro: number; updatedAt: string; source: string; isStale: boolean }>>('/currencies/refresh', { method: 'POST' }),
  refreshMarketPrices: () => request<Array<{ assetId: string; ticker: string; updated: boolean; price?: number; currency?: string; error?: string }>>('/market/refresh', { method: 'POST' }),
  assetHistory: (assetId: string) => request<Array<{ capturedAt: string; price: number; currency: string; source: string }>>(`/assets/${assetId}/history`),
  resetMarketHistory: () => request<void>('/market/history', { method: 'DELETE' }),
  netWorthHistory: (scenarioId: string) => request<NetWorthSnapshot[]>(`/scenarios/${scenarioId}/net-worth-history`),
  resetNetWorthHistory: () => request<void>('/net-worth-history', { method: 'DELETE' }),
  deleteAllData: () => request<void>('/data', { method: 'DELETE' }),
  dashboard: (id: string) => request<Dashboard>(`/scenarios/${id}/dashboard`),
  scenarioData: (id: string) => request<ScenarioData>(`/scenarios/${id}/data`),
  simulation: (id: string, mode: SimulationMode) => request<Simulation>(`/scenarios/${id}/simulate`, { method: 'POST', body: JSON.stringify({ mode }) }),
  createScenario: (profileId: string, name: string, parentScenarioId?: string) => request<{ id: string }>('/scenarios', { method: 'POST', body: JSON.stringify({ profileId, name, parentScenarioId }) }),
  createItem: (scenarioId: string, resource: string, item: Record<string, unknown>) => request<void>(`/scenarios/${scenarioId}/${resource}`, { method: 'POST', body: JSON.stringify(item) }),
  updateItem: (resource: string, id: string, item: Record<string, unknown>) => request<void>(`/${resource}/${id}`, { method: 'PUT', body: JSON.stringify(item) }),
  deleteItem: (resource: string, id: string) => request<void>(`/${resource}/${id}`, { method: 'DELETE' }),
  exportData: () => request<LifeLedgerExport>('/export'),
  importData: (document: LifeLedgerExport, replaceExisting: boolean) => request<{ id: string }>('/import', { method: 'POST', body: JSON.stringify({ document, replaceExisting }) }),
}
