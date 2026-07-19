namespace LifeLedger.Api.Domain;

/// <summary>Classifies a source of income in a financial scenario.</summary>
public enum IncomeKind { Salary, Freelance, Rental, Dividends, Pension, Royalties, Other }

/// <summary>Defines whether an income is entered monthly, as an annual total, or with a seasonal monthly split.</summary>
public enum IncomeAmountMode { Monthly, Annual, Seasonal }

/// <summary>Classifies an asset for allocation and projection purposes.</summary>
public enum AssetKind { Cash, Etf, Stock, Crypto, RealEstate, Business, Collectible, Other }

/// <summary>Identifies where the net proceeds of a planned asset sale are transferred.</summary>
public enum AssetSaleDestination { Cash, Asset, InvestmentPlan }

/// <summary>Classifies a debt or financial obligation.</summary>
public enum LiabilityKind { Mortgage, Loan, Leasing, Credit, Other }

/// <summary>Indicates whether an expense repeats or is a one-off cost.</summary>
public enum ExpenseKind { Recurring, Exceptional }

/// <summary>Defines the cadence used by recurring expenses and events.</summary>
public enum RecurrenceFrequency { Daily, Weekly, EveryTwoWeeks, Monthly, Quarterly, Yearly, EveryFiveYears }

/// <summary>Classifies the life event applied to a financial scenario.</summary>
public enum EventKind { HousePurchase, NewChild, Inheritance, JobLoss, SalaryIncrease, BusinessCreation, EarlyRetirement, Relocation, Divorce, Custom, VehiclePurchase }

/// <summary>Defines the model used to project a scenario.</summary>
public enum SimulationMode { Deterministic, MonteCarlo, Historical }

/// <summary>Optional sex used solely to select transparent life-expectancy planning references.</summary>
public enum ProfileSex { Neutral, Female, Male }

/// <summary>Describes how an observed bank transaction should be interpreted without changing a forecast automatically.</summary>
public enum BankTransactionClassification { Uncategorized, Expense, Income, Transfer, Investment, AssetExpense, Ignored }
