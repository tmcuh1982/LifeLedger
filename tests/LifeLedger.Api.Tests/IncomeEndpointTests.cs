using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace LifeLedger.Api.Tests;

public sealed class IncomeEndpointTests : IClassFixture<LifeLedgerApiFactory>
{
    private readonly LifeLedgerApiFactory _factory;

    public IncomeEndpointTests(LifeLedgerApiFactory factory) => _factory = factory;

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
}
