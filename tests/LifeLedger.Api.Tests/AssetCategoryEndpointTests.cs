using System.Net;
using System.Net.Http.Json;
using LifeLedger.Api.Data;
using LifeLedger.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LifeLedger.Api.Tests;

/// <summary>Verifies the lifecycle of user-defined asset categories.</summary>
public sealed class AssetCategoryEndpointTests : IClassFixture<LifeLedgerApiFactory>
{
    /// <summary>Hosts the API with an isolated temporary database.</summary>
    private readonly LifeLedgerApiFactory _factory;

    /// <summary>Creates the test class with its shared in-process API host.</summary>
    public AssetCategoryEndpointTests(LifeLedgerApiFactory factory) => _factory = factory;

    /// <summary>Renaming a category also updates every asset already assigned to it.</summary>
    [Fact]
    public async Task Renaming_a_custom_category_updates_assigned_assets()
    {
        using var client = _factory.CreateClient();
        using var createResponse = await client.PostAsJsonAsync("/api/asset-categories", new { name = "Vehicles" });
        Assert.True(createResponse.StatusCode == HttpStatusCode.Created, await createResponse.Content.ReadAsStringAsync());

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
            var profile = new Profile { DisplayName = "Category profile" };
            var scenario = new FinancialScenario
            {
                Profile = profile,
                Name = "Reference",
                Assumptions = new SimulationAssumptions(),
                Assets = [new Asset { Name = "Family car", Kind = AssetKind.Other, CurrentValue = 10_000m, CustomCategory = "Vehicles" }]
            };
            database.Scenarios.Add(scenario);
            await database.SaveChangesAsync();
        }

        using var renameResponse = await client.PutAsJsonAsync("/api/asset-categories/Vehicles", new { name = "Transport" });

        Assert.Equal(HttpStatusCode.OK, renameResponse.StatusCode);
        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var verificationDatabase = verificationScope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
        Assert.Equal("Transport", await verificationDatabase.Assets.Where(asset => asset.Name == "Family car").Select(asset => asset.CustomCategory).SingleAsync());
    }

    /// <summary>Prevents deleting a category while assets still depend on it.</summary>
    [Fact]
    public async Task Deleting_an_assigned_category_returns_conflict()
    {
        using var client = _factory.CreateClient();
        using var createResponse = await client.PostAsJsonAsync("/api/asset-categories", new { name = "Collectibles perso" });
        Assert.True(createResponse.StatusCode == HttpStatusCode.Created, await createResponse.Content.ReadAsStringAsync());

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
            var profile = new Profile { DisplayName = "Protected category profile" };
            database.Scenarios.Add(new FinancialScenario
            {
                Profile = profile,
                Name = "Reference",
                Assumptions = new SimulationAssumptions(),
                Assets = [new Asset { Name = "Watch", Kind = AssetKind.Other, CurrentValue = 1_000m, CustomCategory = "Collectibles perso" }]
            });
            await database.SaveChangesAsync();
        }

        using var deleteResponse = await client.DeleteAsync("/api/asset-categories/Collectibles%20perso");

        Assert.Equal(HttpStatusCode.Conflict, deleteResponse.StatusCode);
    }
}
