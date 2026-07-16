using System.Net;
using System.Net.Http.Json;
using LifeLedger.Api.Contracts;
using LifeLedger.Api.Data;
using LifeLedger.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LifeLedger.Api.Tests;

/// <summary>Verifies public API behaviour for income entries.</summary>
public sealed class IncomeEndpointTests : IClassFixture<LifeLedgerApiFactory>
{
    /// <summary>Hosts the API with an isolated temporary database.</summary>
    private readonly LifeLedgerApiFactory _factory;

    /// <summary>Creates the test class with its shared in-process API host.</summary>
    public IncomeEndpointTests(LifeLedgerApiFactory factory) => _factory = factory;

    /// <summary>Protects clients from receiving a misleading 405 response for a valid PUT route.</summary>
    [Fact]
    public async Task Updating_an_unknown_income_returns_not_found_not_method_not_allowed()
    {
        using var client = _factory.CreateClient();
        using var response = await client.PutAsJsonAsync($"/api/incomes/{Guid.NewGuid()}", new
        {
            name = "Salary", kind = "Salary", monthlyAmount = 1, annualGrowthRate = 0,
            startsOn = "2026-01-01", isTaxable = true, taxRate = 0, currency = "EUR"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Deletes every persisted financial record while leaving the API available for a future backup restore.</summary>
    [Fact]
    public async Task Deleting_all_data_removes_the_local_profile()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
            database.Profiles.Add(new Profile { DisplayName = "Temporary test profile" });
            await database.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        using var response = await client.DeleteAsync("/api/data");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var verificationDatabase = verificationScope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
        Assert.Equal(0, await verificationDatabase.Profiles.CountAsync());
    }

    /// <summary>Rejects an invalid replacement import before it can remove the current local profile.</summary>
    [Fact]
    public async Task Invalid_replacement_import_preserves_existing_data()
    {
        var existingProfileId = Guid.NewGuid();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
            database.Profiles.Add(new Profile { Id = existingProfileId, DisplayName = "Existing local profile" });
            await database.SaveChangesAsync();
        }

        var invalidDocument = new LifeLedgerExport(
            1,
            DateTimeOffset.UtcNow,
            new Profile { DisplayName = "", BaseCurrency = "EUR", ExpectedLifespan = 81 },
            []);
        using var client = _factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/import", new ImportRequest(invalidDocument, ReplaceExisting: true));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var verificationDatabase = verificationScope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
        Assert.True(await verificationDatabase.Profiles.AnyAsync(profile => profile.Id == existingProfileId));
    }
}
