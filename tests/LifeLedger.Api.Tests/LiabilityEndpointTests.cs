using System.Net;
using System.Net.Http.Json;
using LifeLedger.Api.Contracts;
using LifeLedger.Api.Data;
using LifeLedger.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LifeLedger.Api.Tests;

/// <summary>Protects personal debt shares and explicit liability-to-asset relationships.</summary>
public sealed class LiabilityEndpointTests : IClassFixture<LifeLedgerApiFactory>
{
    private readonly LifeLedgerApiFactory _factory;

    /// <summary>Creates the endpoint suite with the application's isolated test host.</summary>
    public LiabilityEndpointTests(LifeLedgerApiFactory factory) => _factory = factory;

    /// <summary>Stores the personal debt share independently and links the mortgage to the selected property.</summary>
    [Fact]
    public async Task Create_liability_persists_responsibility_and_financed_asset()
    {
        Guid scenarioId;
        Guid assetId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
            var scenario = new FinancialScenario
            {
                Profile = new Profile { DisplayName = $"Debt test {Guid.NewGuid():N}", BaseCurrency = "EUR" },
                Name = "Reference", Assumptions = new SimulationAssumptions()
            };
            var asset = new Asset { Scenario = scenario, Name = "Shared home", Kind = AssetKind.RealEstate, CurrentValue = 300_000m, OwnershipRate = 0.5m, Currency = "EUR" };
            db.AddRange(scenario, asset);
            await db.SaveChangesAsync();
            scenarioId = scenario.Id;
            assetId = asset.Id;
        }

        var request = new LiabilityRequest(
            "Mortgage", LiabilityKind.Mortgage, 120_000m, 0.75m, 0.03m, 800m,
            new DateOnly(2045, 1, 1), "EUR", [new LiabilityAssetAllocationRequest(assetId, 1m)]);
        using var response = await _factory.CreateClient().PostAsJsonAsync($"/api/scenarios/{scenarioId}/liabilities", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
        var saved = await verificationDb.Liabilities.Include(liability => liability.AssetLinks).SingleAsync(liability => liability.ScenarioId == scenarioId);
        Assert.Equal(0.75m, saved.ResponsibilityRate);
        Assert.Equal(assetId, Assert.Single(saved.AssetLinks).AssetId);
        Assert.Equal(1m, saved.AssetLinks[0].AllocationRate);
    }
}
