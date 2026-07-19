using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LifeLedger.Api.Contracts;
using LifeLedger.Api.Data;
using LifeLedger.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LifeLedger.Api.Tests;

/// <summary>Verifies bank-specific preview, currency preservation, persistence, and duplicate protection.</summary>
public sealed class BankingImportTests : IClassFixture<LifeLedgerApiFactory>
{
    private readonly LifeLedgerApiFactory _factory;

    /// <summary>Creates the test class with an isolated in-process API host.</summary>
    public BankingImportTests(LifeLedgerApiFactory factory) => _factory = factory;

    /// <summary>Imports the configured Erste layout without treating decimal commas as CSV separators.</summary>
    [Fact]
    public async Task Erste_csv_is_previewed_and_committed_in_its_account_currency()
    {
        var scenarioId = await SeedScenarioAsync();
        const string csv = "18-07-2026;01-07-2026;PL001234567890;Test Person;PLN;1000,00;500,00;2;;\n" +
                           "17-07-2026;17-07-2026;Grocery shop;Local Market;PL999;-125,50;874,50;101;;\n" +
                           "18-07-2026;18-07-2026;Salary;Test Employer;PL888;3000,00;3874,50;102;;\n";

        using var client = _factory.CreateClient();
        using var previewResponse = await client.PostAsync($"/api/scenarios/{scenarioId}/bank-statements/preview", StatementForm("erste-csv-v1", csv, "statement.csv"));

        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        var preview = await previewResponse.Content.ReadFromJsonAsync<BankStatementPreviewResponse>(jsonOptions);
        Assert.NotNull(preview);
        Assert.Equal("PLN", preview.DetectedCurrency);
        Assert.Equal(2, preview.Transactions.Count);
        Assert.Equal(-125.50m, preview.Transactions[0].Amount);

        var reviews = preview.Transactions.Select(item => new BankTransactionReview(item.Fingerprint, item.Amount < 0 ? BankTransactionClassification.Expense : BankTransactionClassification.Income, item.Amount < 0 ? "food" : "income", null, null)).ToArray();
        var commitRequest = new CommitBankStatementRequest(scenarioId, "erste-csv-v1", "Polish account", "PLN", null, reviews);
        using var commitForm = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        commitForm.Add(fileContent, "file", "statement.csv");
        commitForm.Add(new StringContent(JsonSerializer.Serialize(commitRequest, new JsonSerializerOptions(JsonSerializerDefaults.Web))), "request");
        using var commitResponse = await client.PostAsync("/api/bank-statements/commit", commitForm);

        Assert.Equal(HttpStatusCode.Created, commitResponse.StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
        var account = await database.BankAccounts.Include(item => item.Imports).ThenInclude(item => item.Transactions).SingleAsync(item => item.ScenarioId == scenarioId);
        Assert.Equal("PLN", account.Currency);
        Assert.EndsWith("7890", account.MaskedIdentifier);
        Assert.Equal(2, account.Imports.Single().Transactions.Count);
    }

    /// <summary>Rejects a confirmation that would silently reinterpret the statement in another currency.</summary>
    [Fact]
    public async Task Commit_rejects_a_currency_different_from_the_statement()
    {
        var scenarioId = await SeedScenarioAsync();
        const string csv = "18-07-2026;01-07-2026;PL009999;Test Person;PLN;1000,00;500,00;1;;\n17-07-2026;17-07-2026;Shop;;;-10,00;990,00;1;;\n";
        var request = new CommitBankStatementRequest(scenarioId, "erste-csv-v1", "Account", "EUR", null, []);
        using var form = StatementForm("erste-csv-v1", csv, "statement.csv");
        form.Add(new StringContent(JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web))), "request");

        using var response = await _factory.CreateClient().PostAsync("/api/bank-statements/commit", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Reclassifies major property work, excludes it from monthly spending, and records an explicit new property value.</summary>
    [Fact]
    public async Task Imported_operation_can_be_reassigned_to_an_asset_after_import()
    {
        var scenarioId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
            var account = new BankAccount
            {
                ScenarioId = scenarioId,
                BankKey = "fortis-pdf-v1",
                Name = "Renovation account",
                MaskedIdentifier = "••••1234",
                IdentifierHash = Guid.NewGuid().ToString("N"),
                Currency = "EUR"
            };
            database.Scenarios.Add(new FinancialScenario
            {
                Id = scenarioId,
                Profile = new Profile { DisplayName = "Property work profile" },
                Name = "Reference",
                Assumptions = new SimulationAssumptions(),
                Assets = [new Asset { Id = assetId, Name = "Family home", Kind = AssetKind.RealEstate, CurrentValue = 260_000m, Currency = "EUR" }],
                BankAccounts = [account]
            });
            account.Imports.Add(new BankStatementImport
            {
                SourceFileName = "roof.pdf",
                SourceFingerprint = Guid.NewGuid().ToString("N"),
                ImporterKey = "fortis-pdf-v1",
                Transactions = [new BankTransaction { Id = transactionId, Fingerprint = Guid.NewGuid().ToString("N"), BookedOn = new DateOnly(2026, 7, 1), Description = "Roof replacement", Amount = -35_000m, Currency = "EUR", Classification = BankTransactionClassification.Expense }]
            });
            await database.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        using var response = await client.PutAsJsonAsync($"/api/bank-transactions/{transactionId}", new
        {
            classification = "AssetExpense",
            category = "home_improvement",
            linkedAssetId = assetId,
            linkedInvestmentPlanId = (Guid?)null,
            isExcludedFromSpendingAnalysis = true,
            newLinkedAssetValue = 285_000m,
            assetValuedOn = "2026-07-18"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var verificationDatabase = verificationScope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
        var savedTransaction = await verificationDatabase.BankTransactions.SingleAsync(item => item.Id == transactionId);
        Assert.Equal(BankTransactionClassification.AssetExpense, savedTransaction.Classification);
        Assert.True(savedTransaction.IsExcludedFromSpendingAnalysis);
        Assert.Equal(assetId, savedTransaction.LinkedAssetId);
        var savedAsset = await verificationDatabase.Assets.SingleAsync(asset => asset.Id == assetId);
        Assert.Equal(285_000m, savedAsset.CurrentValue);
        Assert.Equal("Bank operation: Roof replacement", savedAsset.ValuationSource);
        Assert.Equal(285_000m, await verificationDatabase.AssetValuationSnapshots.Where(snapshot => snapshot.AssetId == assetId).Select(snapshot => snapshot.Value).SingleAsync());
    }

    /// <summary>Averages regular spending over every covered month and only promotes it into projections on request.</summary>
    [Fact]
    public async Task Spending_average_uses_the_complete_statement_period_and_can_update_the_simulation()
    {
        var scenarioId = Guid.NewGuid();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
            database.Scenarios.Add(new FinancialScenario
            {
                Id = scenarioId,
                Profile = new Profile { DisplayName = "Spending average profile" },
                Name = "Reference",
                Assumptions = new SimulationAssumptions(),
                BankAccounts =
                [
                    new BankAccount
                    {
                        BankKey = "erste-csv-v1", Name = "Daily account", MaskedIdentifier = "•••1234", IdentifierHash = Guid.NewGuid().ToString("N"), Currency = "EUR",
                        Imports =
                        [
                            new BankStatementImport
                            {
                                SourceFileName = "quarter.csv", SourceFingerprint = Guid.NewGuid().ToString("N"), ImporterKey = "erste-csv-v1",
                                PeriodStartsOn = new DateOnly(2026, 1, 1), PeriodEndsOn = new DateOnly(2026, 3, 31),
                                Transactions =
                                [
                                    Transaction(new DateOnly(2026, 1, 4), -200m, "food"),
                                    Transaction(new DateOnly(2026, 3, 4), -100m, "food"),
                                    Transaction(new DateOnly(2026, 2, 4), -90m, "fuel"),
                                    Transaction(new DateOnly(2026, 1, 10), -35_000m, "home_improvement", BankTransactionClassification.AssetExpense),
                                    new BankTransaction { Fingerprint = Guid.NewGuid().ToString("N"), BookedOn = new DateOnly(2026, 2, 12), Description = "Excluded outlier", Amount = -600m, Currency = "EUR", Classification = BankTransactionClassification.Expense, Category = "exceptional", IsExcludedFromSpendingAnalysis = true }
                                ]
                            }
                        ]
                    }
                ]
            });
            await database.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        var averages = await client.GetFromJsonAsync<BankSpendingAverageResponse[]>($"/api/scenarios/{scenarioId}/bank-spending-averages");
        Assert.NotNull(averages);
        var food = Assert.Single(averages, average => average.Category == "food");
        Assert.Equal(100m, food.AverageMonthlyAmount);
        Assert.Equal(3, food.ObservedMonths);
        Assert.Equal(30m, Assert.Single(averages, average => average.Category == "fuel").AverageMonthlyAmount);
        Assert.DoesNotContain(averages, average => average.Category is "home_improvement" or "exceptional");

        using var firstApply = await client.PostAsJsonAsync($"/api/scenarios/{scenarioId}/bank-spending-averages/food/apply", new { currency = "EUR", name = "Observed food", indexedToInflation = true });
        using var secondApply = await client.PostAsJsonAsync($"/api/scenarios/{scenarioId}/bank-spending-averages/food/apply", new { currency = "EUR", name = "Observed food updated", indexedToInflation = true });
        Assert.Equal(HttpStatusCode.OK, firstApply.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondApply.StatusCode);

        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var expenses = await verificationScope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>().Expenses.Where(expense => expense.ScenarioId == scenarioId).ToListAsync();
        var plannedExpense = Assert.Single(expenses);
        Assert.Equal(100m, plannedExpense.MonthlyAmount);
        Assert.Equal("food", plannedExpense.ObservedBankCategory);
        Assert.Equal(RecurrenceFrequency.Monthly, plannedExpense.Frequency);
        Assert.True(plannedExpense.IndexedToInflation);
    }

    /// <summary>Creates one imported operation for a monthly-average test.</summary>
    private static BankTransaction Transaction(DateOnly bookedOn, decimal amount, string category, BankTransactionClassification classification = BankTransactionClassification.Expense) => new()
    {
        Fingerprint = Guid.NewGuid().ToString("N"), BookedOn = bookedOn, Description = category, Amount = amount, Currency = "EUR", Classification = classification, Category = category
    };

    /// <summary>Creates a minimal scenario directly in the isolated test database.</summary>
    private async Task<Guid> SeedScenarioAsync()
    {
        var scenarioId = Guid.NewGuid();
        await using var scope = _factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
        database.Scenarios.Add(new FinancialScenario { Id = scenarioId, Profile = new Profile { DisplayName = $"Bank test {scenarioId:N}" }, Name = "Reference", Assumptions = new SimulationAssumptions() });
        await database.SaveChangesAsync();
        return scenarioId;
    }

    /// <summary>Builds the multipart body used by statement preview endpoints.</summary>
    private static MultipartFormDataContent StatementForm(string bankKey, string text, string fileName)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(bankKey), "bankKey");
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(text));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(fileContent, "file", fileName);
        return form;
    }
}
