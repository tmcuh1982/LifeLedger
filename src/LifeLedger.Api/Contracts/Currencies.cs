namespace LifeLedger.Api.Contracts;

public sealed record CurrencyRateResponse(string Code, decimal UnitsPerEuro, DateTimeOffset UpdatedAt, string Source, bool IsStale);
public sealed record UpsertCurrencyRateRequest(decimal UnitsPerEuro);
