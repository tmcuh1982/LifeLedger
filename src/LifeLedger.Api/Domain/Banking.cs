namespace LifeLedger.Api.Domain;

/// <summary>Represents one local bank account whose observed history belongs to a scenario.</summary>
public sealed class BankAccount
{
    /// <summary>Stable identifier of the account.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Scenario whose observed history owns this account.</summary>
    public Guid ScenarioId { get; set; }
    /// <summary>Navigation to the owning scenario.</summary>
    public FinancialScenario? Scenario { get; set; }
    /// <summary>Stable key of the bank importer used for this account.</summary>
    public string BankKey { get; set; } = string.Empty;
    /// <summary>User-facing account name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Masked identifier safe to display, such as the last four IBAN characters.</summary>
    public string MaskedIdentifier { get; set; } = string.Empty;
    /// <summary>One-way hash used to match the same account without storing its complete identifier.</summary>
    public string IdentifierHash { get; set; } = string.Empty;
    /// <summary>Mandatory ISO 4217 account currency.</summary>
    public string Currency { get; set; } = string.Empty;
    /// <summary>Optional cash asset represented by this account.</summary>
    public Guid? LinkedAssetId { get; set; }
    /// <summary>Navigation to the optional cash asset.</summary>
    public Asset? LinkedAsset { get; set; }
    /// <summary>Imports committed for this account.</summary>
    public List<BankStatementImport> Imports { get; set; } = [];
}

/// <summary>Records one committed statement import without retaining the source document.</summary>
public sealed class BankStatementImport
{
    /// <summary>Stable identifier of the import.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Account that received the imported operations.</summary>
    public Guid BankAccountId { get; set; }
    /// <summary>Navigation to the account.</summary>
    public BankAccount? BankAccount { get; set; }
    /// <summary>Original filename retained only for local audit readability.</summary>
    public string SourceFileName { get; set; } = string.Empty;
    /// <summary>One-way fingerprint of the source bytes used to detect a repeated file.</summary>
    public string SourceFingerprint { get; set; } = string.Empty;
    /// <summary>Versioned bank-template key used to interpret the file.</summary>
    public string ImporterKey { get; set; } = string.Empty;
    /// <summary>Earliest booking date found in the statement.</summary>
    public DateOnly? PeriodStartsOn { get; set; }
    /// <summary>Latest booking date found in the statement.</summary>
    public DateOnly? PeriodEndsOn { get; set; }
    /// <summary>UTC time at which the reviewed import was committed.</summary>
    public DateTimeOffset ImportedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Observed transactions retained from this import.</summary>
    public List<BankTransaction> Transactions { get; set; } = [];
}

/// <summary>Represents one historical, observed bank operation kept separate from planned expenses.</summary>
public sealed class BankTransaction
{
    /// <summary>Stable identifier of the operation.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Statement import that introduced the operation.</summary>
    public Guid BankStatementImportId { get; set; }
    /// <summary>Navigation to the statement import.</summary>
    public BankStatementImport? BankStatementImport { get; set; }
    /// <summary>Stable local fingerprint used to suppress overlaps between statements.</summary>
    public string Fingerprint { get; set; } = string.Empty;
    /// <summary>Date on which the bank booked the operation.</summary>
    public DateOnly BookedOn { get; set; }
    /// <summary>Optional bank value date.</summary>
    public DateOnly? ValueOn { get; set; }
    /// <summary>Statement wording describing the operation.</summary>
    public string Description { get; set; } = string.Empty;
    /// <summary>Optional counterparty label extracted from the statement.</summary>
    public string? Counterparty { get; set; }
    /// <summary>Signed amount: negative is money paid, positive is money received.</summary>
    public decimal Amount { get; set; }
    /// <summary>ISO 4217 booked currency, never inferred silently.</summary>
    public string Currency { get; set; } = string.Empty;
    /// <summary>Optional balance shown after the operation.</summary>
    public decimal? BalanceAfter { get; set; }
    /// <summary>User-reviewed interpretation of the operation.</summary>
    public BankTransactionClassification Classification { get; set; }
    /// <summary>Stable local category key; labels are translated by the client.</summary>
    public string Category { get; set; } = "other";
    /// <summary>Whether this operation is omitted from observed recurring-spending estimates.</summary>
    public bool IsExcludedFromSpendingAnalysis { get; set; }
    /// <summary>Optional asset affected by this operation.</summary>
    public Guid? LinkedAssetId { get; set; }
    /// <summary>Navigation to the linked asset.</summary>
    public Asset? LinkedAsset { get; set; }
    /// <summary>Optional investment plan funded by this operation.</summary>
    public Guid? LinkedInvestmentPlanId { get; set; }
    /// <summary>Navigation to the linked investment plan.</summary>
    public InvestmentPlan? LinkedInvestmentPlan { get; set; }
}
