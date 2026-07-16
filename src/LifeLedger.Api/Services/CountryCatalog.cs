using LifeLedger.Api.Contracts;

namespace LifeLedger.Api.Services;

public interface ICountryCatalog
{
    IReadOnlyList<CountryInfo> List();
    CountryInfo Get(string countryCode);
}

public sealed class CountryCatalog : ICountryCatalog
{
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

    public IReadOnlyList<CountryInfo> List() => Countries;
    public CountryInfo Get(string countryCode) => Countries.FirstOrDefault(x => x.Code.Equals(countryCode, StringComparison.OrdinalIgnoreCase)) ?? Countries[^1];
}
