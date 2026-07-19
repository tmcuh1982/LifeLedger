using LifeLedger.Api.Domain;

namespace LifeLedger.Api.Contracts;

/// <summary>Describes one versioned, locally installed bank statement template.</summary>
public sealed record BankImporterDefinitionResponse(string Key, string BankName, string Format, int Version, IReadOnlyList<string> AcceptedExtensions);

/// <summary>Represents one extracted operation awaiting user review.</summary>
public sealed record BankTransactionPreview(
    string Fingerprint,
    DateOnly BookedOn,
    DateOnly? ValueOn,
    string Description,
    string? Counterparty,
    decimal Amount,
    string Currency,
    decimal? BalanceAfter,
    BankTransactionClassification SuggestedClassification,
    string SuggestedCategory,
    bool AlreadyImported);

/// <summary>Returns an in-memory statement preview; source bytes are never persisted.</summary>
public sealed record BankStatementPreviewResponse(
    string ImporterKey,
    string BankName,
    string SourceFingerprint,
    string MaskedAccountIdentifier,
    string DetectedCurrency,
    DateOnly? PeriodStartsOn,
    DateOnly? PeriodEndsOn,
    IReadOnlyList<BankTransactionPreview> Transactions);

/// <summary>Contains the user-reviewed interpretation and optional links for one extracted operation.</summary>
public sealed record BankTransactionReview(
    string Fingerprint,
    BankTransactionClassification Classification,
    string Category,
    Guid? LinkedAssetId,
    Guid? LinkedInvestmentPlanId,
    bool IsExcludedFromSpendingAnalysis = false);

/// <summary>Updates the interpretation of an imported operation and optionally records a new explicit asset valuation.</summary>
public sealed record UpdateBankTransactionRequest(
    BankTransactionClassification Classification,
    string Category,
    Guid? LinkedAssetId,
    Guid? LinkedInvestmentPlanId,
    bool IsExcludedFromSpendingAnalysis,
    decimal? NewLinkedAssetValue,
    DateOnly? AssetValuedOn);

/// <summary>Supplies the review choices used when a statement is committed.</summary>
public sealed record CommitBankStatementRequest(
    Guid ScenarioId,
    string BankKey,
    string AccountName,
    string ConfirmedCurrency,
    Guid? LinkedAssetId,
    IReadOnlyList<BankTransactionReview> Reviews);

/// <summary>Summarises a committed statement and any overlapping operations that were skipped.</summary>
public sealed record CommitBankStatementResponse(Guid BankAccountId, Guid ImportId, int ImportedTransactions, int SkippedDuplicates);

/// <summary>Returns one persisted bank account with aggregate import counts.</summary>
public sealed record BankAccountResponse(Guid Id, string BankKey, string Name, string MaskedIdentifier, string Currency, Guid? LinkedAssetId, int Imports, int Transactions);

/// <summary>Returns one persisted observed operation for review and reporting.</summary>
public sealed record BankTransactionResponse(Guid Id, Guid BankAccountId, DateOnly BookedOn, DateOnly? ValueOn, string Description, string? Counterparty, decimal Amount, string Currency, decimal? BalanceAfter, BankTransactionClassification Classification, string Category, bool IsExcludedFromSpendingAnalysis, Guid? LinkedAssetId, Guid? LinkedInvestmentPlanId);

/// <summary>Summarises one observed expense category as a monthly amount over the complete imported coverage period.</summary>
public sealed record BankSpendingAverageResponse(
    string Category,
    string Currency,
    decimal AverageMonthlyAmount,
    decimal ObservedTotal,
    int IncludedTransactions,
    int ObservedMonths,
    DateOnly PeriodStartsOn,
    DateOnly PeriodEndsOn,
    Guid? LinkedExpenseId);

/// <summary>Creates or refreshes a recurring planning expense from an observed bank-category average.</summary>
public sealed record ApplyBankSpendingAverageRequest(string Currency, string Name, bool IndexedToInflation = true);
