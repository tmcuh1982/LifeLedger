namespace LifeLedger.Api.Domain;

/// <summary>Classifies a source of income in a financial scenario.</summary>
public enum IncomeKind { Salary, Freelance, Rental, Dividends, Pension, Royalties, Other }

/// <summary>Classifies an asset for allocation and projection purposes.</summary>
public enum AssetKind { Cash, Etf, Stock, Crypto, RealEstate, Business, Collectible, Other }

/// <summary>Classifies a debt or financial obligation.</summary>
public enum LiabilityKind { Mortgage, Loan, Leasing, Credit, Other }

/// <summary>Indicates whether an expense repeats or is a one-off cost.</summary>
public enum ExpenseKind { Recurring, Exceptional }

/// <summary>Defines the cadence used by recurring expenses and events.</summary>
public enum RecurrenceFrequency { Daily, Weekly, EveryTwoWeeks, Monthly, Quarterly, Yearly, EveryFiveYears }

/// <summary>Classifies the life event applied to a financial scenario.</summary>
public enum EventKind { HousePurchase, NewChild, Inheritance, JobLoss, SalaryIncrease, BusinessCreation, EarlyRetirement, Relocation, Divorce, Custom }

/// <summary>Defines the model used to project a scenario.</summary>
public enum SimulationMode { Deterministic, MonteCarlo, Historical }

/// <summary>Optional sex used solely to select transparent life-expectancy planning references.</summary>
public enum ProfileSex { Neutral, Female, Male }
