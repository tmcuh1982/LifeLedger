using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LifeLedger.Api.Contracts;
using LifeLedger.Api.Data;
using LifeLedger.Api.Domain;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LifeLedger.Api.Tests;

/// <summary>Protects the safe configuration boundary of the read-only IBKR Flex connector.</summary>
public sealed class IbkrFlexEndpointTests : IClassFixture<LifeLedgerApiFactory>
{
    private readonly LifeLedgerApiFactory _factory;

    /// <summary>Creates the test suite with the isolated API host.</summary>
    public IbkrFlexEndpointTests(LifeLedgerApiFactory factory) => _factory = factory;

    /// <summary>Ensures a Flex token is accepted for local protection but never returned by the configuration API.</summary>
    [Fact]
    public async Task Configuration_returns_only_safe_connection_metadata()
    {
        var scenarioId = await SeedScenarioAsync();
        using var client = _factory.CreateClient();

        var put = await client.PutAsJsonAsync($"/api/scenarios/{scenarioId}/integrations/ibkr-flex", new IbkrFlexConfigurationRequest("sensitive-flex-token", 123456));
        using var get = await client.GetAsync($"/api/scenarios/{scenarioId}/integrations/ibkr-flex");
        var document = JsonDocument.Parse(await get.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.True(document.RootElement.GetProperty("isConfigured").GetBoolean());
        Assert.Equal(123456, document.RootElement.GetProperty("activityQueryId").GetInt64());
        Assert.DoesNotContain("sensitive-flex-token", document.RootElement.GetRawText(), StringComparison.Ordinal);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
        var storedToken = await db.ApplicationSettings.FindAsync($"flex:ibkr:{scenarioId:N}:token");
        Assert.NotNull(storedToken);
        Assert.DoesNotContain("sensitive-flex-token", storedToken.Value, StringComparison.Ordinal);
    }

    /// <summary>Creates the minimum valid scenario directly in the isolated database.</summary>
    private async Task<Guid> SeedScenarioAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
        var profile = new Profile { DisplayName = $"IBKR test {Guid.NewGuid():N}" };
        var scenario = new FinancialScenario { Profile = profile, Name = "IBKR scenario", Assumptions = new SimulationAssumptions() };
        db.Scenarios.Add(scenario);
        await db.SaveChangesAsync();
        return scenario.Id;
    }
}
