using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LifeLedger.Api.Contracts;
using LifeLedger.Api.Data;
using LifeLedger.Api.Domain;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LifeLedger.Api.Tests;

/// <summary>Protects creation of portable local backups on the default SQLite provider.</summary>
public sealed class BackupEndpointTests : IClassFixture<LifeLedgerApiFactory>
{
    private readonly LifeLedgerApiFactory _factory;

    /// <summary>Creates the backup endpoint suite with the application's SQLite test host.</summary>
    public BackupEndpointTests(LifeLedgerApiFactory factory) => _factory = factory;

    /// <summary>Returns the oldest local profile without asking SQLite to order a DateTimeOffset value.</summary>
    [Fact]
    public async Task Export_returns_a_portable_document_on_sqlite()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
            var liability = new Liability { Name = "Mortgage", Kind = LiabilityKind.Mortgage, OutstandingBalance = 80_000m, ResponsibilityRate = 1m, Currency = "EUR" };
            var asset = new Asset { Name = "Shared home", Kind = AssetKind.RealEstate, CurrentValue = 200_000m, OwnershipRate = 0.5m, Currency = "EUR" };
            asset.LiabilityLinks.Add(new AssetLiabilityLink { Liability = liability, AllocationRate = 1m });
            db.Scenarios.Add(new FinancialScenario
            {
                Profile = new Profile { DisplayName = $"Backup test {Guid.NewGuid():N}", BaseCurrency = "EUR" },
                Name = "Reference", Assumptions = new SimulationAssumptions(), Assets = [asset], Liabilities = [liability]
            });
            await db.SaveChangesAsync();
        }

        using var response = await _factory.CreateClient().GetAsync("/api/export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        var document = await response.Content.ReadFromJsonAsync<LifeLedgerExport>(jsonOptions);
        Assert.NotNull(document);
        Assert.Equal(12, document.SchemaVersion);
        Assert.NotEmpty(document.Scenarios);
    }
}
