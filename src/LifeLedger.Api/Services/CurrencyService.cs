using System.Text.Json;
using System.Xml.Linq;
using LifeLedger.Api.Contracts;

namespace LifeLedger.Api.Services;

/// <summary>Local cache of ECB reference rates. A refresh is explicit; all projections run from the local cache.</summary>
public interface ICurrencyService
{
    decimal Convert(decimal amount, string fromCurrency, string toCurrency);
    IReadOnlyList<CurrencyRateResponse> List();
    Task<IReadOnlyList<CurrencyRateResponse>> RefreshAsync(CancellationToken cancellationToken);
    IReadOnlyList<CurrencyRateResponse> SetManual(string code, decimal unitsPerEuro);
}

public sealed class LocalCurrencyService(string cachePath, IHttpClientFactory httpClientFactory, ILogger<LocalCurrencyService> logger) : ICurrencyService
{
    private const string EcbUrl = "https://www.ecb.europa.eu/stats/eurofxref/eurofxref-daily.xml";
    private readonly object _gate = new();
    private Dictionary<string, StoredRate> _rates = Load(cachePath);

    public decimal Convert(decimal amount, string fromCurrency, string toCurrency)
    {
        var from = GetRate(fromCurrency);
        var to = GetRate(toCurrency);
        return Math.Round(amount / from.UnitsPerEuro * to.UnitsPerEuro, 4);
    }

    public IReadOnlyList<CurrencyRateResponse> List()
    {
        lock (_gate) return _rates.Values.Select(ToResponse).OrderBy(x => x.Code).ToArray();
    }

    public async Task<IReadOnlyList<CurrencyRateResponse>> RefreshAsync(CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(nameof(LocalCurrencyService));
        var xml = await client.GetStringAsync(EcbUrl, cancellationToken);
        var document = XDocument.Parse(xml);
        var now = DateTimeOffset.UtcNow;
        var refreshed = new Dictionary<string, StoredRate>(StringComparer.OrdinalIgnoreCase)
        {
            ["EUR"] = new("EUR", 1m, now, "ECB")
        };
        foreach (var cube in document.Descendants().Where(x => x.Name.LocalName == "Cube" && x.Attribute("currency") is not null))
        {
            var code = cube.Attribute("currency")!.Value.ToUpperInvariant();
            if (!decimal.TryParse(cube.Attribute("rate")?.Value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var rate)) continue;
            refreshed[code] = new StoredRate(code, rate, now, "ECB");
        }
        if (refreshed.Count <= 1) throw new InvalidOperationException("The ECB response contained no currency rates.");
        lock (_gate) { _rates = refreshed; Save(cachePath, _rates); }
        logger.LogInformation("Refreshed {Count} local ECB currency rates", refreshed.Count);
        return List();
    }

    public IReadOnlyList<CurrencyRateResponse> SetManual(string code, decimal unitsPerEuro)
    {
        if (unitsPerEuro <= 0) throw new ArgumentOutOfRangeException(nameof(unitsPerEuro));
        code = code.Trim().ToUpperInvariant();
        if (code.Length != 3) throw new ArgumentException("Currency codes must use ISO 4217 three-letter format.", nameof(code));
        lock (_gate)
        {
            _rates[code] = new StoredRate(code, unitsPerEuro, DateTimeOffset.UtcNow, "Manual");
            Save(cachePath, _rates);
        }
        return List();
    }

    private StoredRate GetRate(string currency)
    {
        currency = string.IsNullOrWhiteSpace(currency) ? "EUR" : currency.Trim().ToUpperInvariant();
        lock (_gate)
        {
            if (_rates.TryGetValue(currency, out var rate)) return rate;
        }
        throw new InvalidOperationException($"No conversion rate is available for {currency}. Add it manually or refresh ECB rates.");
    }

    private static Dictionary<string, StoredRate> Load(string path)
    {
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<List<StoredRate>>(File.ReadAllText(path)) is { } saved)
                return saved.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        }
        catch { /* Keep the offline defaults if a cache is malformed. */ }
        var timestamp = DateTimeOffset.UtcNow;
        return new Dictionary<string, StoredRate>(StringComparer.OrdinalIgnoreCase)
        {
            ["EUR"] = new("EUR", 1m, timestamp, "Offline default"),
            ["USD"] = new("USD", 1.08m, timestamp, "Offline default"),
            ["PLN"] = new("PLN", 4.30m, timestamp, "Offline default")
        };
    }

    private static void Save(string path, Dictionary<string, StoredRate> rates)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(rates.Values.OrderBy(x => x.Code)));
    }

    private static CurrencyRateResponse ToResponse(StoredRate rate) => new(rate.Code, rate.UnitsPerEuro, rate.UpdatedAt, rate.Source, DateTimeOffset.UtcNow - rate.UpdatedAt > TimeSpan.FromDays(2));
    private sealed record StoredRate(string Code, decimal UnitsPerEuro, DateTimeOffset UpdatedAt, string Source);
}
