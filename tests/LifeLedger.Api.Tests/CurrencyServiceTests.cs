using LifeLedger.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LifeLedger.Api.Tests;

/// <summary>Verifies user-friendly currency aliases used by locally entered financial records.</summary>
public sealed class CurrencyServiceTests
{
    /// <summary>Accepts the common Polish currency name when the stored reference rate uses ISO code PLN.</summary>
    [Fact]
    public void Zloty_alias_is_converted_with_the_pln_rate()
    {
        var cachePath = Path.Combine(Path.GetTempPath(), $"lifeledger-currency-{Guid.NewGuid():N}.json");
        var service = new LocalCurrencyService(cachePath, new TestHttpClientFactory(), NullLogger<LocalCurrencyService>.Instance);

        var euros = service.Convert(430m, "ZLOTY", "EUR");

        Assert.Equal(100m, euros);
    }

    /// <summary>Creates HTTP clients for tests that exercise only the offline currency cache.</summary>
    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        /// <inheritdoc />
        public HttpClient CreateClient(string name) => new();
    }
}
