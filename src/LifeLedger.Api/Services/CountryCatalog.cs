using LifeLedger.Api.Contracts;

namespace LifeLedger.Api.Services;

/// <summary>Provides country defaults used to initialise financial assumptions.</summary>
public interface ICountryCatalog
{
    /// <summary>Returns every built-in country definition.</summary>
    IReadOnlyList<CountryInfo> List();
    /// <summary>Returns a country definition or the custom fallback when no code matches.</summary>
    CountryInfo Get(string countryCode);
}

/// <summary>In-memory catalogue of the first supported countries and their defaults.</summary>
public sealed class CountryCatalog : ICountryCatalog
{
    // This is intentionally local, deterministic starter data rather than a remote country service.
    private static readonly CountryInfo[] Countries =
    [
        new("BE", "Belgium", 0.022m, 67, "EUR"),
        new("FR", "France", 0.022m, 64, "EUR"),
        new("DE", "Germany", 0.022m, 67, "EUR"),
        new("NL", "Netherlands", 0.022m, 67, "EUR"),
        new("PL", "Poland", 0.030m, 65, "PLN"),
        new("GB", "United Kingdom", 0.022m, 67, "GBP"),
        new("US", "United States", 0.025m, 67, "USD"),
        new("OTHER", "Other / custom", 0.025m, 65, "EUR")
    ];

    /// <inheritdoc />
    public IReadOnlyList<CountryInfo> List() => Countries;

    /// <inheritdoc />
    public CountryInfo Get(string countryCode) => Countries.FirstOrDefault(x => x.Code.Equals(countryCode, StringComparison.OrdinalIgnoreCase)) ?? Countries[^1];
}
