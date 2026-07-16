export type SimulationMode = 'Deterministic' | 'MonteCarlo' | 'Historical'

export interface ScenarioSummary {
  id: string
  profileId: string
  name: string
  description: string
  isBaseline: boolean
  startsOn: string
  parentScenarioId?: string
}

export interface ProjectionYear {
  year: number
  age: number
  netWorth: number
  cashFlow: number
  income: number
  expenses: number
  passiveIncome: number
  inflationAdjustedNetWorth: number
}

export interface NetWorthSnapshot {
  capturedAt: string
  netWorth: number
  currency: string
}

export interface AllocationSlice {
  name: string
  kind: string
  value: number
  percentage: number
}

export interface Dashboard {
  scenarioId: string
  scenarioName: string
  currency: string
  currentNetWorth: number
  futureNetWorth: number
  passiveMonthlyIncome: number
  estimatedRetirementIncome: number
  financialIndependenceDate?: string
  inflationAdjustedPurchasingPowerChange: number
  probabilityOfSuccess: number
  timeline: ProjectionYear[]
  allocation: AllocationSlice[]
  warnings: string[]
}

export interface Simulation {
  mode: SimulationMode
  runs: number
  probabilityOfSuccess: number
  timeline: ProjectionYear[]
  terminalNetWorths: number[]
  warnings: string[]
}

export interface ScenarioData {
  incomes: LedgerItem[]
  assets: LedgerItem[]
  liabilities: LedgerItem[]
  expenses: LedgerItem[]
  investments: LedgerItem[]
  events: LedgerItem[]
}

export interface LedgerItem {
  id: string
  name: string
  kind?: string
  monthlyAmount?: number
  currentValue?: number
  outstandingBalance?: number
  oneOffCashImpact?: number
  currency?: string
  [key: string]: unknown
}

export interface Profile {
  id: string
  displayName: string
  baseCurrency: string
  birthDate: string
  sex: 'Neutral' | 'Female' | 'Male'
  homeCountryCode: string
  expectedLifespan: number
  partnerBirthYear?: number
  childrenCount: number
  careers: Array<Record<string, unknown>>
}

export interface LifeLedgerExport {
  schemaVersion: number
  exportedAt: string
  profile: Profile
  scenarios: unknown[]
}
