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
  plannedExpenseSavings: number
  plannedExpenseFundBalance: number
  wealthComponents: ProjectionWealthComponent[]
  assetSales: ProjectedAssetSale[]
}

export type AssetSaleDestination = 'Cash' | 'Asset' | 'InvestmentPlan'

export interface ProjectedAssetSale {
  saleId: string
  assetId: string
  name: string
  happensOn: string
  grossProceeds: number
  sellingCosts: number
  capitalGainsTax: number
  debtRepaid: number
  netProceeds: number
  currency: string
  destination: AssetSaleDestination
}

export type ProjectionWealthComponentType = 'Asset' | 'Investment' | 'ProjectedCash' | 'PlannedExpenseReserve' | 'Liability'

export interface ProjectionWealthComponent {
  key: string
  category: string
  kind?: string
  type: ProjectionWealthComponentType
  value: number
}

export interface NetWorthSnapshot {
  capturedAt: string
  netWorth: number
  currency: string
}

export interface AssetCategory {
  name: string
  assetCount: number
}

export type AssetProfileFieldType = 'Text' | 'Number' | 'Date' | 'Boolean' | 'Select' | 'Area' | 'Distance' | 'Condition'

export interface AssetProfileOptionDefinition {
  value: string
  labels: Record<string, string>
}

export interface AssetProfileFieldDefinition {
  key: string
  labels: Record<string, string>
  type: AssetProfileFieldType
  required: boolean
  options?: AssetProfileOptionDefinition[]
}

export interface AssetProfileDefinition {
  key: string
  version: number
  labels: Record<string, string>
  fields: AssetProfileFieldDefinition[]
  isCustom: boolean
}

export interface AssetProfileDefinitionInput {
  labels: Record<string, string>
  fields: AssetProfileFieldDefinition[]
}

export interface AssetPerformance {
  currency: string
  acquisitionBasis: number
  grossGain: number
  gainRate?: number
  linkedDebt: number
  netEquity: number
}

export interface AssetDossierResponse {
  asset: LedgerItem
  performance: AssetPerformance
}

export interface AssetValuation {
  id: string
  assetId: string
  valuedOn: string
  value: number
  currency: string
  source: string
  recordedAt: string
}

export interface AllocationSlice {
  name: string
  kind: string
  value: number
  percentage: number
}

export type AllocationTargetState = 'WithinRange' | 'Underweight' | 'Overweight'

export interface AllocationTargetAssessment {
  category: string
  targetPercentage: number
  tolerancePercentage: number
  actualPercentage: number
  differencePercentage: number
  state: AllocationTargetState
}

export interface AllocationStrategyAssessment {
  name: string
  effectiveFrom: string
  effectiveTo?: string
  totalTargetPercentage: number
  targets: AllocationTargetAssessment[]
}

export interface AllocationStrategyTarget {
  id?: string
  category: string
  targetPercentage: number
  tolerancePercentage: number
}

export interface AllocationStrategy {
  id: string
  scenarioId: string
  name: string
  description?: string
  effectiveFrom: string
  effectiveTo?: string
  targets: AllocationStrategyTarget[]
}

export interface Dashboard {
  scenarioId: string
  scenarioName: string
  currency: string
  currentNetWorth: number
  futureNetWorth: number
  passiveMonthlyIncome: number
  expectedMonthlyPortfolioGrowth: number
  estimatedRetirementIncome: number
  financialIndependenceDate?: string
  inflationAdjustedPurchasingPowerChange: number
  probabilityOfSuccess: number
  timeline: ProjectionYear[]
  allocation: AllocationSlice[]
  allocationStrategy?: AllocationStrategyAssessment
  warnings: SimulationWarning[]
}

export interface Simulation {
  mode: SimulationMode
  runs: number
  probabilityOfSuccess: number
  timeline: ProjectionYear[]
  terminalNetWorths: number[]
  warnings: SimulationWarning[]
}

export interface SimulationWarning {
  code: 'insolvency-age' | 'purchasing-power-drop' | 'low-emergency-fund' | 'high-debt-payments' | 'low-monte-carlo-success'
  value?: number
}

export interface ScenarioData {
  incomes: LedgerItem[]
  assets: LedgerItem[]
  liabilities: LedgerItem[]
  expenses: LedgerItem[]
  investments: LedgerItem[]
  assetSales: LedgerItem[]
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
  assetProfileDefinitions?: AssetProfileDefinition[]
}

export type BankTransactionClassification = 'Uncategorized' | 'Expense' | 'Income' | 'Transfer' | 'Investment' | 'AssetExpense' | 'Ignored'

export interface BankImporterDefinition {
  key: string
  bankName: string
  format: string
  version: number
  acceptedExtensions: string[]
}

export interface BankSpendingAverage {
  category: string
  currency: string
  averageMonthlyAmount: number
  observedTotal: number
  includedTransactions: number
  observedMonths: number
  periodStartsOn: string
  periodEndsOn: string
  linkedExpenseId?: string
}

export interface BankTransactionPreview {
  fingerprint: string
  bookedOn: string
  valueOn?: string
  description: string
  counterparty?: string
  amount: number
  currency: string
  balanceAfter?: number
  suggestedClassification: BankTransactionClassification
  suggestedCategory: string
  alreadyImported: boolean
}

export interface BankStatementPreview {
  importerKey: string
  bankName: string
  sourceFingerprint: string
  maskedAccountIdentifier: string
  detectedCurrency: string
  periodStartsOn?: string
  periodEndsOn?: string
  transactions: BankTransactionPreview[]
}

export interface BankTransactionReview {
  fingerprint: string
  classification: BankTransactionClassification
  category: string
  isExcludedFromSpendingAnalysis?: boolean
  linkedAssetId?: string
  linkedInvestmentPlanId?: string
}

export interface UpdateBankTransaction {
  classification: BankTransactionClassification
  category: string
  isExcludedFromSpendingAnalysis: boolean
  linkedAssetId?: string
  linkedInvestmentPlanId?: string
  newLinkedAssetValue?: number
  assetValuedOn?: string
}

export interface BankAccount {
  id: string
  bankKey: string
  name: string
  maskedIdentifier: string
  currency: string
  linkedAssetId?: string
  imports: number
  transactions: number
}

export interface BankTransaction {
  id: string
  bankAccountId: string
  bookedOn: string
  valueOn?: string
  description: string
  counterparty?: string
  amount: number
  currency: string
  balanceAfter?: number
  classification: BankTransactionClassification
  category: string
  isExcludedFromSpendingAnalysis: boolean
  linkedAssetId?: string
  linkedInvestmentPlanId?: string
}
