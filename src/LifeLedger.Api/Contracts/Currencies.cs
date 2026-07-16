namespace LifeLedger.Api.Contracts;

/// <summary>Describes a locally cached exchange rate expressed as currency units per euro.</summary>
/// <param name="Code">ISO 4217 currency code.</param>
/// <param name="UnitsPerEuro">Number of currency units equivalent to one euro.</param>
/// <param name="UpdatedAt">UTC timestamp of the last update.</param>
/// <param name="Source">Provider or user action that supplied the rate.</param>
/// <param name="IsStale">Whether the rate is older than two days.</param>
public sealed record CurrencyRateResponse(string Code, decimal UnitsPerEuro, DateTimeOffset UpdatedAt, string Source, bool IsStale);

/// <summary>Requests a manually maintained exchange rate expressed as units per euro.</summary>
/// <param name="UnitsPerEuro">Number of currency units equivalent to one euro; must be positive.</param>
public sealed record UpsertCurrencyRateRequest(decimal UnitsPerEuro);
