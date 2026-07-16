namespace LifeLedger.Api.Domain;

public enum IncomeKind { Salary, Freelance, Rental, Dividends, Pension, Royalties, Other }
public enum AssetKind { Cash, Etf, Stock, Crypto, RealEstate, Business, Collectible, Other }
public enum LiabilityKind { Mortgage, Loan, Leasing, Credit, Other }
public enum ExpenseKind { Recurring, Exceptional }
public enum RecurrenceFrequency { Daily, Weekly, EveryTwoWeeks, Monthly, Quarterly, Yearly, EveryFiveYears }
public enum EventKind { HousePurchase, NewChild, Inheritance, JobLoss, SalaryIncrease, BusinessCreation, EarlyRetirement, Relocation, Divorce, Custom }
public enum SimulationMode { Deterministic, MonteCarlo, Historical }
