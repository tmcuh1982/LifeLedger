using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LifeLedger.Api.Contracts;
using LifeLedger.Api.Data;
using LifeLedger.Api.Domain;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace LifeLedger.Api.Services;

/// <summary>Exposes the complete local bank-statement workflow behind a small extensible interface.</summary>
public interface IBankStatementImportModule
{
    /// <summary>Lists the versioned statement templates installed with this LifeLedger instance.</summary>
    IReadOnlyList<BankImporterDefinitionResponse> ListImporters();
    /// <summary>Parses source bytes in memory and returns a reviewable preview without storing the document.</summary>
    Task<BankStatementPreviewResponse> PreviewAsync(Guid scenarioId, string bankKey, string fileName, byte[] content, CancellationToken cancellationToken = default);
    /// <summary>Parses the source again and atomically stores only reviewed operations and audit metadata.</summary>
    Task<CommitBankStatementResponse> CommitAsync(CommitBankStatementRequest request, string fileName, byte[] content, CancellationToken cancellationToken = default);
    /// <summary>Lists local accounts registered for a scenario.</summary>
    Task<IReadOnlyList<BankAccountResponse>> ListAccountsAsync(Guid scenarioId, CancellationToken cancellationToken = default);
    /// <summary>Lists observed operations for a scenario without changing its forecast.</summary>
    Task<IReadOnlyList<BankTransactionResponse>> ListTransactionsAsync(Guid scenarioId, CancellationToken cancellationToken = default);
    /// <summary>Reclassifies one imported operation and optionally records a user-confirmed asset valuation.</summary>
    Task<BankTransactionResponse> UpdateTransactionAsync(Guid transactionId, UpdateBankTransactionRequest request, CancellationToken cancellationToken = default);
    /// <summary>Calculates monthly category averages across every month covered by imported statements.</summary>
    Task<IReadOnlyList<BankSpendingAverageResponse>> ListSpendingAveragesAsync(Guid scenarioId, CancellationToken cancellationToken = default);
    /// <summary>Creates or updates one recurring simulation expense from its current observed average.</summary>
    Task<BankSpendingAverageResponse> ApplySpendingAverageAsync(Guid scenarioId, string category, ApplyBankSpendingAverageRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Represents a bank-specific parser behind the common import Module Interface.</summary>
internal interface IBankStatementAdapter
{
    /// <summary>Parses one supported statement into a provider-neutral result.</summary>
    ParsedBankStatement Parse(BankImporterDefinition definition, string fileName, byte[] content);
}

/// <summary>Implements local preview, deduplication, validation, and persistence for bank statements.</summary>
public sealed class BankStatementImportModule : IBankStatementImportModule
{
    private readonly LifeLedgerDbContext _db;
    private readonly IAssetValuationHistoryService _valuationHistory;
    private readonly IReadOnlyDictionary<string, BankImporterDefinition> _definitions;
    private readonly IReadOnlyDictionary<string, IBankStatementAdapter> _adapters;

    /// <summary>Loads versioned templates and selects their parsing Adapters.</summary>
    public BankStatementImportModule(LifeLedgerDbContext db, IHostEnvironment environment, IAssetValuationHistoryService valuationHistory)
    {
        _db = db;
        _valuationHistory = valuationHistory;
        var directory = Path.Combine(environment.ContentRootPath, "BankImporters");
        _definitions = Directory.EnumerateFiles(directory, "*.json")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => JsonSerializer.Deserialize<BankImporterDefinition>(File.ReadAllText(path), JsonOptions())
                ?? throw new InvalidOperationException($"Bank importer definition '{path}' is empty."))
            // Some development hosts can retain a suffixed copy of an output file. The stable template key
            // identifies the importer, so identical build artefacts must not prevent the API from starting.
            .GroupBy(definition => definition.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        _adapters = new Dictionary<string, IBankStatementAdapter>(StringComparer.OrdinalIgnoreCase)
        {
            ["Delimited"] = new ConfigurableDelimitedBankAdapter(),
            ["FortisPdf"] = new FortisPdfBankStatementAdapter()
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<BankImporterDefinitionResponse> ListImporters() => _definitions.Values
        .OrderBy(definition => definition.BankName)
        .Select(definition => new BankImporterDefinitionResponse(definition.Key, definition.BankName, definition.Format, definition.Version, definition.AcceptedExtensions))
        .ToArray();

    /// <inheritdoc />
    public async Task<BankStatementPreviewResponse> PreviewAsync(Guid scenarioId, string bankKey, string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        await EnsureScenarioAsync(scenarioId, cancellationToken);
        var (definition, parsed) = Parse(bankKey, fileName, content);
        var identifierHash = Fingerprint(Encoding.UTF8.GetBytes(parsed.AccountIdentifier));
        var importedFingerprints = await _db.BankTransactions.AsNoTracking()
            .Where(transaction => transaction.BankStatementImport!.BankAccount!.ScenarioId == scenarioId && transaction.BankStatementImport.BankAccount.IdentifierHash == identifierHash)
            .Select(transaction => transaction.Fingerprint)
            .ToHashSetAsync(cancellationToken);
        return ToPreview(definition, parsed, Fingerprint(content), importedFingerprints);
    }

    /// <inheritdoc />
    public async Task<CommitBankStatementResponse> CommitAsync(CommitBankStatementRequest request, string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        await EnsureScenarioAsync(request.ScenarioId, cancellationToken);
        var (definition, parsed) = Parse(request.BankKey, fileName, content);
        var confirmedCurrency = NormalizeCurrency(request.ConfirmedCurrency);
        if (!string.Equals(parsed.Currency, confirmedCurrency, StringComparison.Ordinal))
            throw new ArgumentException($"The confirmed account currency {confirmedCurrency} does not match the statement currency {parsed.Currency}.", nameof(request));

        await ValidateLinksAsync(request, cancellationToken);
        var sourceFingerprint = Fingerprint(content);
        var identifierHash = Fingerprint(Encoding.UTF8.GetBytes(parsed.AccountIdentifier));
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var account = await _db.BankAccounts.FirstOrDefaultAsync(candidate => candidate.ScenarioId == request.ScenarioId && candidate.IdentifierHash == identifierHash, cancellationToken);
        if (account is null)
        {
            account = new BankAccount
            {
                ScenarioId = request.ScenarioId,
                BankKey = definition.Key,
                Name = string.IsNullOrWhiteSpace(request.AccountName) ? definition.BankName : request.AccountName.Trim(),
                MaskedIdentifier = MaskIdentifier(parsed.AccountIdentifier),
                IdentifierHash = identifierHash,
                Currency = confirmedCurrency,
                LinkedAssetId = request.LinkedAssetId
            };
            _db.BankAccounts.Add(account);
        }
        else
        {
            if (!string.Equals(account.Currency, confirmedCurrency, StringComparison.Ordinal))
                throw new InvalidOperationException("This account already exists with a different currency.");
            account.Name = string.IsNullOrWhiteSpace(request.AccountName) ? account.Name : request.AccountName.Trim();
            account.LinkedAssetId = request.LinkedAssetId;
        }

        if (await _db.BankStatementImports.AnyAsync(candidate => candidate.BankAccountId == account.Id && candidate.SourceFingerprint == sourceFingerprint, cancellationToken))
            throw new InvalidOperationException("This exact statement has already been imported.");

        var existing = await _db.BankTransactions.AsNoTracking()
            .Where(item => item.BankStatementImport!.BankAccountId == account.Id)
            .Select(item => item.Fingerprint)
            .ToHashSetAsync(cancellationToken);
        var reviews = request.Reviews.ToDictionary(review => review.Fingerprint, StringComparer.Ordinal);
        var statementImport = new BankStatementImport
        {
            BankAccount = account,
            SourceFileName = Path.GetFileName(fileName),
            SourceFingerprint = sourceFingerprint,
            ImporterKey = definition.Key,
            PeriodStartsOn = parsed.Transactions.MinBy(item => item.BookedOn)?.BookedOn,
            PeriodEndsOn = parsed.Transactions.MaxBy(item => item.BookedOn)?.BookedOn
        };

        foreach (var item in parsed.Transactions.Where(item => !existing.Contains(item.Fingerprint)))
        {
            reviews.TryGetValue(item.Fingerprint, out var review);
            statementImport.Transactions.Add(new BankTransaction
            {
                Fingerprint = item.Fingerprint,
                BookedOn = item.BookedOn,
                ValueOn = item.ValueOn,
                Description = item.Description,
                Counterparty = item.Counterparty,
                Amount = item.Amount,
                Currency = parsed.Currency,
                BalanceAfter = item.BalanceAfter,
                Classification = review?.Classification ?? Suggest(item.Amount).Classification,
                Category = NormalizeCategory(review?.Category ?? Suggest(item.Amount).Category),
                IsExcludedFromSpendingAnalysis = review?.IsExcludedFromSpendingAnalysis ?? false,
                LinkedAssetId = review?.LinkedAssetId,
                LinkedInvestmentPlanId = review?.LinkedInvestmentPlanId
            });
        }

        _db.BankStatementImports.Add(statementImport);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CommitBankStatementResponse(account.Id, statementImport.Id, statementImport.Transactions.Count, parsed.Transactions.Count - statementImport.Transactions.Count);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BankAccountResponse>> ListAccountsAsync(Guid scenarioId, CancellationToken cancellationToken = default) =>
        await _db.BankAccounts.AsNoTracking().Where(account => account.ScenarioId == scenarioId).OrderBy(account => account.Name)
            .Select(account => new BankAccountResponse(account.Id, account.BankKey, account.Name, account.MaskedIdentifier, account.Currency, account.LinkedAssetId, account.Imports.Count, account.Imports.Sum(statement => statement.Transactions.Count)))
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<BankTransactionResponse>> ListTransactionsAsync(Guid scenarioId, CancellationToken cancellationToken = default) =>
        await _db.BankTransactions.AsNoTracking().Where(item => item.BankStatementImport!.BankAccount!.ScenarioId == scenarioId)
            .OrderByDescending(item => item.BookedOn).ThenByDescending(item => item.Id)
            .Select(item => new BankTransactionResponse(item.Id, item.BankStatementImport!.BankAccountId, item.BookedOn, item.ValueOn, item.Description, item.Counterparty, item.Amount, item.Currency, item.BalanceAfter, item.Classification, item.Category, item.IsExcludedFromSpendingAnalysis, item.LinkedAssetId, item.LinkedInvestmentPlanId))
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<BankTransactionResponse> UpdateTransactionAsync(Guid transactionId, UpdateBankTransactionRequest request, CancellationToken cancellationToken = default)
    {
        var transaction = await _db.BankTransactions
            .Include(item => item.BankStatementImport).ThenInclude(statement => statement!.BankAccount)
            .FirstOrDefaultAsync(item => item.Id == transactionId, cancellationToken)
            ?? throw new KeyNotFoundException("Bank operation not found.");
        var scenarioId = transaction.BankStatementImport!.BankAccount!.ScenarioId;

        var linkedAssetId = request.Classification == BankTransactionClassification.AssetExpense ? request.LinkedAssetId : null;
        var linkedInvestmentPlanId = request.Classification == BankTransactionClassification.Investment ? request.LinkedInvestmentPlanId : null;
        Asset? linkedAsset = null;
        if (linkedAssetId is { } assetId)
        {
            linkedAsset = await _db.Assets.FirstOrDefaultAsync(asset => asset.Id == assetId && asset.ScenarioId == scenarioId, cancellationToken)
                ?? throw new ArgumentException("The selected asset does not belong to this scenario.", nameof(request));
        }
        if (linkedInvestmentPlanId is { } planId && !await _db.Investments.AnyAsync(plan => plan.Id == planId && plan.ScenarioId == scenarioId, cancellationToken))
            throw new ArgumentException("The selected investment plan does not belong to this scenario.", nameof(request));
        if (request.NewLinkedAssetValue is < 0m)
            throw new ArgumentException("The new asset value cannot be negative.", nameof(request));
        if (request.NewLinkedAssetValue is not null && linkedAsset is null)
            throw new ArgumentException("Choose the affected asset before updating its value.", nameof(request));

        transaction.Classification = request.Classification;
        transaction.Category = NormalizeCategory(request.Category);
        transaction.IsExcludedFromSpendingAnalysis = request.IsExcludedFromSpendingAnalysis;
        transaction.LinkedAssetId = linkedAssetId;
        transaction.LinkedInvestmentPlanId = linkedInvestmentPlanId;

        if (linkedAsset is not null && request.NewLinkedAssetValue is { } newAssetValue)
        {
            // The user supplies an absolute valuation because a repair cost is not automatically equal to value created.
            linkedAsset.CurrentValue = newAssetValue;
            linkedAsset.ValuedOn = request.AssetValuedOn ?? DateOnly.FromDateTime(DateTime.UtcNow);
            linkedAsset.ValuationSource = $"Bank operation: {transaction.Description}";
            await _valuationHistory.RecordAsync(linkedAsset, linkedAsset.ValuedOn, linkedAsset.ValuationSource, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new BankTransactionResponse(transaction.Id, transaction.BankStatementImport.BankAccountId, transaction.BookedOn, transaction.ValueOn, transaction.Description, transaction.Counterparty, transaction.Amount, transaction.Currency, transaction.BalanceAfter, transaction.Classification, transaction.Category, transaction.IsExcludedFromSpendingAnalysis, transaction.LinkedAssetId, transaction.LinkedInvestmentPlanId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BankSpendingAverageResponse>> ListSpendingAveragesAsync(Guid scenarioId, CancellationToken cancellationToken = default)
    {
        await EnsureScenarioAsync(scenarioId, cancellationToken);
        var imports = await _db.BankStatementImports.AsNoTracking()
            .Where(statement => statement.BankAccount!.ScenarioId == scenarioId)
            .Select(statement => new { statement.BankAccount!.Currency, statement.PeriodStartsOn, statement.PeriodEndsOn })
            .ToListAsync(cancellationToken);
        var transactions = await _db.BankTransactions.AsNoTracking()
            .Where(item => item.BankStatementImport!.BankAccount!.ScenarioId == scenarioId)
            .Select(item => new { item.BookedOn, item.Amount, item.Currency, item.Classification, item.Category, item.IsExcludedFromSpendingAnalysis })
            .ToListAsync(cancellationToken);

        var coveredMonths = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var statement in imports.Where(statement => statement.PeriodStartsOn.HasValue && statement.PeriodEndsOn.HasValue))
        {
            var months = coveredMonths.GetValueOrDefault(statement.Currency) ?? [];
            foreach (var month in MonthKeysBetween(statement.PeriodStartsOn!.Value, statement.PeriodEndsOn!.Value)) months.Add(month);
            coveredMonths[statement.Currency] = months;
        }
        // A transaction month is a safe fallback for old or incomplete import audit rows.
        foreach (var transaction in transactions)
        {
            var months = coveredMonths.GetValueOrDefault(transaction.Currency) ?? [];
            months.Add(MonthKey(transaction.BookedOn));
            coveredMonths[transaction.Currency] = months;
        }

        var linkedExpenses = await _db.Expenses.AsNoTracking()
            .Where(expense => expense.ScenarioId == scenarioId && expense.ObservedBankCategory != null)
            .Select(expense => new { expense.Id, expense.ObservedBankCategory, expense.Currency })
            .ToListAsync(cancellationToken);

        return transactions
            .Where(item => item.Classification == BankTransactionClassification.Expense && !item.IsExcludedFromSpendingAnalysis)
            .GroupBy(item => new { Category = NormalizeCategory(item.Category), Currency = NormalizeCurrency(item.Currency) })
            .Select(group =>
            {
                var months = coveredMonths.GetValueOrDefault(group.Key.Currency) ?? [];
                var observedMonths = Math.Max(1, months.Count);
                // Positive amounts within an expense category are treated as refunds and reduce observed spending.
                var observedTotal = Math.Max(0m, group.Sum(item => -item.Amount));
                var firstMonth = months.Count == 0 ? MonthKey(group.Min(item => item.BookedOn)) : months.Min();
                var lastMonth = months.Count == 0 ? MonthKey(group.Max(item => item.BookedOn)) : months.Max();
                var linkedExpenseId = linkedExpenses.FirstOrDefault(expense =>
                    string.Equals(expense.ObservedBankCategory, group.Key.Category, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(expense.Currency, group.Key.Currency, StringComparison.OrdinalIgnoreCase))?.Id;
                return new BankSpendingAverageResponse(
                    group.Key.Category,
                    group.Key.Currency,
                    Math.Round(observedTotal / observedMonths, 2),
                    Math.Round(observedTotal, 2),
                    group.Count(),
                    observedMonths,
                    MonthStart(firstMonth),
                    MonthEnd(lastMonth),
                    linkedExpenseId);
            })
            .Where(average => average.AverageMonthlyAmount > 0m)
            .OrderByDescending(average => average.AverageMonthlyAmount)
            .ThenBy(average => average.Category)
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<BankSpendingAverageResponse> ApplySpendingAverageAsync(Guid scenarioId, string category, ApplyBankSpendingAverageRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedCategory = NormalizeCategory(category);
        var currency = NormalizeCurrency(request.Currency);
        var average = (await ListSpendingAveragesAsync(scenarioId, cancellationToken)).FirstOrDefault(candidate =>
            string.Equals(candidate.Category, normalizedCategory, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.Currency, currency, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException("No observed spending average matches this category and currency.");
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("A planning expense name is required.", nameof(request));

        var expense = await _db.Expenses.FirstOrDefaultAsync(candidate =>
            candidate.ScenarioId == scenarioId && candidate.ObservedBankCategory == normalizedCategory && candidate.Currency == currency,
            cancellationToken);
        if (expense is null)
        {
            var startsOn = DateOnly.FromDateTime(DateTime.UtcNow);
            expense = new Expense
            {
                ScenarioId = scenarioId,
                Name = request.Name.Trim(),
                Kind = ExpenseKind.Recurring,
                Frequency = RecurrenceFrequency.Monthly,
                MonthlyAmount = average.AverageMonthlyAmount,
                IndexedToInflation = request.IndexedToInflation,
                StartsOn = startsOn,
                EndsOn = startsOn.AddYears(50),
                Currency = currency,
                ObservedBankCategory = normalizedCategory
            };
            _db.Expenses.Add(expense);
        }
        else
        {
            // Reapplying refreshes the observed assumption without erasing user-adjusted dates or future steps.
            expense.Name = request.Name.Trim();
            expense.MonthlyAmount = average.AverageMonthlyAmount;
            expense.IndexedToInflation = request.IndexedToInflation;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return average with { LinkedExpenseId = expense.Id };
    }

    /// <summary>Returns inclusive calendar-month keys for one imported statement period.</summary>
    private static IEnumerable<int> MonthKeysBetween(DateOnly startsOn, DateOnly endsOn)
    {
        var current = new DateOnly(startsOn.Year, startsOn.Month, 1);
        var final = new DateOnly(endsOn.Year, endsOn.Month, 1);
        while (current <= final)
        {
            yield return MonthKey(current);
            current = current.AddMonths(1);
        }
    }

    /// <summary>Converts a date into a sortable calendar-month key.</summary>
    private static int MonthKey(DateOnly date) => date.Year * 12 + date.Month - 1;

    /// <summary>Converts a calendar-month key into its first day.</summary>
    private static DateOnly MonthStart(int key) => new(key / 12, key % 12 + 1, 1);

    /// <summary>Converts a calendar-month key into its final day.</summary>
    private static DateOnly MonthEnd(int key)
    {
        var start = MonthStart(key);
        return new DateOnly(start.Year, start.Month, DateTime.DaysInMonth(start.Year, start.Month));
    }

    /// <summary>Parses and validates a file using the Adapter selected by its versioned definition.</summary>
    private (BankImporterDefinition Definition, ParsedBankStatement Parsed) Parse(string bankKey, string fileName, byte[] content)
    {
        if (!_definitions.TryGetValue(bankKey, out var definition)) throw new ArgumentException("Unknown bank importer.", nameof(bankKey));
        if (content.Length == 0) throw new ArgumentException("The statement file is empty.", nameof(content));
        var extension = Path.GetExtension(fileName);
        if (!definition.AcceptedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException($"This template expects: {string.Join(", ", definition.AcceptedExtensions)}.", nameof(fileName));
        if (!_adapters.TryGetValue(definition.Format, out var adapter)) throw new InvalidOperationException($"No Adapter is installed for '{definition.Format}'.");
        var parsed = adapter.Parse(definition, fileName, content);
        if (parsed.Transactions.Count == 0) throw new ArgumentException("No bank operation could be read from this statement.", nameof(content));
        parsed.Currency = NormalizeCurrency(parsed.Currency);
        return (definition, parsed);
    }

    /// <summary>Rejects cross-scenario links before any observed operation is written.</summary>
    private async Task ValidateLinksAsync(CommitBankStatementRequest request, CancellationToken cancellationToken)
    {
        var assetIds = request.Reviews.Select(review => review.LinkedAssetId).Append(request.LinkedAssetId).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray();
        if (assetIds.Length > 0 && await _db.Assets.CountAsync(asset => asset.ScenarioId == request.ScenarioId && assetIds.Contains(asset.Id), cancellationToken) != assetIds.Length)
            throw new ArgumentException("An asset link does not belong to the selected scenario.", nameof(request));
        var planIds = request.Reviews.Where(review => review.LinkedInvestmentPlanId.HasValue).Select(review => review.LinkedInvestmentPlanId!.Value).Distinct().ToArray();
        if (planIds.Length > 0 && await _db.Investments.CountAsync(plan => plan.ScenarioId == request.ScenarioId && planIds.Contains(plan.Id), cancellationToken) != planIds.Length)
            throw new ArgumentException("An investment link does not belong to the selected scenario.", nameof(request));
    }

    /// <summary>Builds a safe preview and marks overlaps already present in the selected scenario.</summary>
    private static BankStatementPreviewResponse ToPreview(BankImporterDefinition definition, ParsedBankStatement parsed, string sourceFingerprint, HashSet<string> imported) =>
        new(definition.Key, definition.BankName, sourceFingerprint, MaskIdentifier(parsed.AccountIdentifier), parsed.Currency,
            parsed.Transactions.MinBy(item => item.BookedOn)?.BookedOn, parsed.Transactions.MaxBy(item => item.BookedOn)?.BookedOn,
            parsed.Transactions.Select(item =>
            {
                var suggestion = Suggest(item.Amount);
                return new BankTransactionPreview(item.Fingerprint, item.BookedOn, item.ValueOn, item.Description, item.Counterparty, item.Amount, parsed.Currency, item.BalanceAfter, suggestion.Classification, suggestion.Category, imported.Contains(item.Fingerprint));
            }).ToArray());

    /// <summary>Uses direction only for a conservative initial suggestion that remains editable.</summary>
    private static (BankTransactionClassification Classification, string Category) Suggest(decimal amount) => amount < 0
        ? (BankTransactionClassification.Expense, "other")
        : (BankTransactionClassification.Income, "income");

    /// <summary>Ensures the requested scenario exists.</summary>
    private async Task EnsureScenarioAsync(Guid scenarioId, CancellationToken cancellationToken)
    {
        if (!await _db.Scenarios.AnyAsync(scenario => scenario.Id == scenarioId, cancellationToken)) throw new KeyNotFoundException("Scenario not found.");
    }

    /// <summary>Normalises a required ISO-style code without inventing a default.</summary>
    private static string NormalizeCurrency(string currency)
    {
        var normalized = currency.Trim().ToUpperInvariant();
        if (normalized.Length != 3 || !normalized.All(char.IsAsciiLetter)) throw new ArgumentException("A three-letter account currency is required.");
        return normalized;
    }

    /// <summary>Constrains editable category keys to a portable local format.</summary>
    private static string NormalizeCategory(string category)
    {
        var normalized = Regex.Replace(category.Trim().ToLowerInvariant(), "[^a-z0-9-]", "-");
        return string.IsNullOrWhiteSpace(normalized) ? "other" : normalized[..Math.Min(normalized.Length, 80)];
    }

    /// <summary>Creates a stable SHA-256 hexadecimal fingerprint.</summary>
    private static string Fingerprint(byte[] content) => Convert.ToHexString(SHA256.HashData(content));

    /// <summary>Retains only the last four identifier characters for display.</summary>
    private static string MaskIdentifier(string identifier)
    {
        var compact = new string(identifier.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return compact.Length <= 4 ? $"•••• {compact}" : $"•••• {compact[^4..]}";
    }

    /// <summary>Uses case-insensitive JSON properties so definition files remain contributor-friendly.</summary>
    private static JsonSerializerOptions JsonOptions() => new() { PropertyNameCaseInsensitive = true };
}

/// <summary>Parses semicolon or comma-delimited bank exports according to a versioned JSON definition.</summary>
internal sealed class ConfigurableDelimitedBankAdapter : IBankStatementAdapter
{
    /// <inheritdoc />
    public ParsedBankStatement Parse(BankImporterDefinition definition, string fileName, byte[] content)
    {
        var rows = ParseRows(Encoding.UTF8.GetString(content), definition.Delimiter.Single());
        if (rows.Count <= definition.FirstTransactionRow) throw new ArgumentException("The CSV does not contain transaction rows.");
        var metadata = rows[definition.MetadataRow];
        var identifier = Value(metadata, definition.AccountIdentifierColumn, "account identifier");
        var currency = Value(metadata, definition.AccountCurrencyColumn, "account currency").Trim().ToUpperInvariant();
        var culture = CultureInfo.GetCultureInfo(definition.DecimalCulture);
        var transactions = new List<ParsedBankTransaction>();
        for (var index = definition.FirstTransactionRow; index < rows.Count; index++)
        {
            var row = rows[index];
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            if (!DateOnly.TryParseExact(Value(row, definition.BookingDateColumn, "booking date"), definition.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var bookedOn))
                throw new ArgumentException($"Invalid booking date on CSV row {index + 1}.");
            DateOnly? valueOn = DateOnly.TryParseExact(Value(row, definition.ValueDateColumn, "value date"), definition.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedValueOn) ? parsedValueOn : null;
            if (!decimal.TryParse(Value(row, definition.AmountColumn, "amount"), NumberStyles.Number | NumberStyles.AllowLeadingSign, culture, out var amount))
                throw new ArgumentException($"Invalid amount on CSV row {index + 1}.");
            decimal? balance = decimal.TryParse(Value(row, definition.BalanceColumn, "balance"), NumberStyles.Number | NumberStyles.AllowLeadingSign, culture, out var parsedBalance) ? parsedBalance : null;
            var description = Value(row, definition.DescriptionColumn, "description").Trim();
            var counterparty = OptionalValue(row, definition.CounterpartyColumn);
            var bankReference = OptionalValue(row, definition.ReferenceColumn);
            transactions.Add(ParsedBankTransaction.Create(bookedOn, valueOn, description, counterparty, amount, balance, bankReference));
        }
        return new ParsedBankStatement(identifier, currency, transactions);
    }

    /// <summary>Reads quoted delimiters without requiring a remote or format-specific library.</summary>
    private static List<string[]> ParseRows(string text, char delimiter)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            if (current == '"')
            {
                if (quoted && index + 1 < text.Length && text[index + 1] == '"') { field.Append('"'); index++; }
                else quoted = !quoted;
            }
            else if (current == delimiter && !quoted) { row.Add(field.ToString()); field.Clear(); }
            else if ((current == '\n' || current == '\r') && !quoted)
            {
                if (current == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
                row.Add(field.ToString()); field.Clear();
                if (row.Any(value => value.Length > 0)) rows.Add(row.ToArray());
                row.Clear();
            }
            else field.Append(current);
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row.ToArray()); }
        return rows;
    }

    private static string Value(string[] row, int column, string label) => column >= 0 && column < row.Length ? row[column] : throw new ArgumentException($"Missing {label} column in the CSV.");
    private static string? OptionalValue(string[] row, int column) => column >= 0 && column < row.Length && !string.IsNullOrWhiteSpace(row[column]) ? row[column].Trim() : null;
}

/// <summary>Extracts BNP Paribas Fortis statement text locally and interprets its paginated layout.</summary>
internal sealed partial class FortisPdfBankStatementAdapter : IBankStatementAdapter
{
    /// <inheritdoc />
    public ParsedBankStatement Parse(BankImporterDefinition definition, string fileName, byte[] content)
    {
        var pages = new List<string>();
        using (var document = PdfDocument.Open(content))
            pages.AddRange(document.GetPages().Select(page => ContentOrderTextExtractor.GetText(page)));
        var allText = string.Join("\n", pages);
        var account = AccountRegex().Match(allText);
        if (!account.Success) throw new ArgumentException("The Fortis account identifier and currency could not be read.");

        var transactions = new List<ParsedBankTransaction>();
        DateOnly? bookingDate = null;
        var block = new List<string>();
        foreach (var raw in allText.Split('\n'))
        {
            var line = raw.Trim();
            if (DateOnly.TryParseExact(line, "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                if (block.Count == 0 || block.Any(candidate => AmountRegex().IsMatch(candidate)))
                {
                    Flush(block, bookingDate, transactions);
                    bookingDate = date;
                }
                else
                {
                    // Card operations can contain their own full transaction date before the bank amount footer.
                    block.Add(line);
                }
                continue;
            }
            if (TransactionStartRegex().IsMatch(line)) Flush(block, bookingDate, transactions);
            if (block.Count > 0 || TransactionStartRegex().IsMatch(line)) block.Add(line);
        }
        Flush(block, bookingDate, transactions);
        return new ParsedBankStatement(account.Groups[1].Value, account.Groups[2].Value, transactions);
    }

    /// <summary>Completes one transaction block when its value-date and signed amount footer are present.</summary>
    private static void Flush(List<string> block, DateOnly? bookingDate, List<ParsedBankTransaction> target)
    {
        if (block.Count == 0) return;
        var footerIndex = block.FindLastIndex(line => AmountRegex().IsMatch(line));
        if (bookingDate is null || footerIndex < 0) { block.Clear(); return; }
        var footer = AmountRegex().Match(block[footerIndex]);
        var amountText = footer.Groups[2].Value.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.');
        if (!decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)) { block.Clear(); return; }
        if (footer.Groups[3].Value == "-") amount = -amount;
        var month = int.Parse(footer.Groups[1].Value.AsSpan(3, 2), CultureInfo.InvariantCulture);
        var year = bookingDate.Value.Year + (bookingDate.Value.Month == 1 && month == 12 ? -1 : bookingDate.Value.Month == 12 && month == 1 ? 1 : 0);
        var valueOn = DateOnly.ParseExact($"{footer.Groups[1].Value}-{year}", "dd-MM-yyyy", CultureInfo.InvariantCulture);
        var reference = block.Select(line => BankReferenceRegex().Match(line)).FirstOrDefault(match => match.Success)?.Groups[1].Value;
        var descriptionLines = block.Take(footerIndex).Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("...", StringComparison.Ordinal)).ToArray();
        var description = descriptionLines.Length > 0 ? TransactionStartRegex().Replace(descriptionLines[0], string.Empty).Trim() : "Bank operation";
        var details = string.Join(" · ", descriptionLines.Skip(1).Where(line => !line.StartsWith("Référence banque", StringComparison.OrdinalIgnoreCase)).Take(3));
        if (!string.IsNullOrWhiteSpace(details)) description = $"{description} — {details}";
        target.Add(ParsedBankTransaction.Create(bookingDate.Value, valueOn, description, null, amount, null, reference));
        block.Clear();
    }

    [GeneratedRegex(@"\b([A-Z]{2}\d{2}(?:\s?\d{4}){3,5})\s+([A-Z]{3})\b")]
    private static partial Regex AccountRegex();
    [GeneratedRegex(@"^\d{4}\s+")]
    private static partial Regex TransactionStartRegex();
    [GeneratedRegex(@"(\d{2}-\d{2})\s+([\d.]+,\d{2})\s*([+-])$")]
    private static partial Regex AmountRegex();
    [GeneratedRegex(@"Référence banque\s*:\s*(\S+)", RegexOptions.IgnoreCase)]
    private static partial Regex BankReferenceRegex();
}

/// <summary>Internal provider-neutral representation returned by every bank Adapter.</summary>
internal sealed class ParsedBankStatement(string accountIdentifier, string currency, IReadOnlyList<ParsedBankTransaction> transactions)
{
    /// <summary>Full identifier used only in memory for matching and masking.</summary>
    public string AccountIdentifier { get; } = accountIdentifier;
    /// <summary>Currency is normalised once at the Module seam.</summary>
    public string Currency { get; set; } = currency;
    /// <summary>Provider-neutral observed transactions.</summary>
    public IReadOnlyList<ParsedBankTransaction> Transactions { get; } = transactions;
}

/// <summary>Internal provider-neutral representation of one parsed bank operation.</summary>
internal sealed record ParsedBankTransaction(DateOnly BookedOn, DateOnly? ValueOn, string Description, string? Counterparty, decimal Amount, decimal? BalanceAfter, string Fingerprint)
{
    /// <summary>Builds a deterministic overlap fingerprint from statement facts or a bank reference.</summary>
    public static ParsedBankTransaction Create(DateOnly bookedOn, DateOnly? valueOn, string description, string? counterparty, decimal amount, decimal? balanceAfter, string? bankReference)
    {
        var identity = !string.IsNullOrWhiteSpace(bankReference)
            ? bankReference.Trim()
            : string.Join('|', bookedOn.ToString("O", CultureInfo.InvariantCulture), valueOn?.ToString("O", CultureInfo.InvariantCulture), amount.ToString(CultureInfo.InvariantCulture), description.Trim(), counterparty?.Trim());
        return new ParsedBankTransaction(bookedOn, valueOn, description.Trim(), counterparty?.Trim(), amount, balanceAfter, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))));
    }
}

/// <summary>JSON-backed definition for a bank statement template and its column mapping.</summary>
internal sealed class BankImporterDefinition
{
    /// <summary>Stable versioned identifier selected by clients.</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>Public bank label shown in the import wizard.</summary>
    public string BankName { get; set; } = string.Empty;
    /// <summary>Adapter format key.</summary>
    public string Format { get; set; } = string.Empty;
    /// <summary>Definition schema version.</summary>
    public int Version { get; set; }
    /// <summary>Extensions accepted by this definition.</summary>
    public string[] AcceptedExtensions { get; set; } = [];
    /// <summary>Single field separator used by delimited files.</summary>
    public string Delimiter { get; set; } = ";";
    /// <summary>Zero-based row containing account metadata.</summary>
    public int MetadataRow { get; set; }
    /// <summary>Zero-based account identifier column.</summary>
    public int AccountIdentifierColumn { get; set; }
    /// <summary>Zero-based account currency column.</summary>
    public int AccountCurrencyColumn { get; set; }
    /// <summary>Zero-based first transaction row.</summary>
    public int FirstTransactionRow { get; set; }
    /// <summary>Zero-based booking date column.</summary>
    public int BookingDateColumn { get; set; }
    /// <summary>Zero-based value date column.</summary>
    public int ValueDateColumn { get; set; }
    /// <summary>Zero-based description column.</summary>
    public int DescriptionColumn { get; set; }
    /// <summary>Zero-based counterparty column.</summary>
    public int CounterpartyColumn { get; set; }
    /// <summary>Zero-based signed amount column.</summary>
    public int AmountColumn { get; set; }
    /// <summary>Zero-based resulting balance column.</summary>
    public int BalanceColumn { get; set; }
    /// <summary>Zero-based bank reference column.</summary>
    public int ReferenceColumn { get; set; }
    /// <summary>Exact date pattern used by the statement.</summary>
    public string DateFormat { get; set; } = "dd-MM-yyyy";
    /// <summary>Culture used to interpret decimal separators.</summary>
    public string DecimalCulture { get; set; } = "en-US";
}
