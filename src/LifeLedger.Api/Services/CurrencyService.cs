using System.Text.Json;
using System.Xml.Linq;
using LifeLedger.Api.Contracts;

namespace LifeLedger.Api.Services;

/// <summary>Local cache of ECB reference rates. A refresh is explicit; all projections run from the local cache.</summary>
public interface ICurrencyService
{
    /// <summary>Converts an amount between ISO 4217 currencies using locally stored rates.</summary>
    decimal Convert(decimal amount, string fromCurrency, string toCurrency);
    /// <summary>Returns every locally available exchange rate.</summary>
    IReadOnlyList<CurrencyRateResponse> List();
    /// <summary>Downloads current ECB reference rates and replaces the local cache.</summary>
    Task<IReadOnlyList<CurrencyRateResponse>> RefreshAsync(CancellationToken cancellationToken);
    /// <summary>Adds or replaces a local user-maintained exchange rate.</summary>
    IReadOnlyList<CurrencyRateResponse> SetManual(string code, decimal unitsPerEuro);
}

/// <summary>Stores ECB and manually entered exchange rates locally for offline-safe projections.</summary>
public sealed class LocalCurrencyService(string cachePath, IHttpClientFactory httpClientFactory, ILogger<LocalCurrencyService> logger) : ICurrencyService
{
    /// <summary>Public XML endpoint used only after the user explicitly requests a refresh.</summary>
    private const string EcbUrl = "https://www.ecb.europa.eu/stats/eurofxref/eurofxref-daily.xml";
    /// <summary>Synchronises access to the in-memory cache and its local file.</summary>
    private readonly object _gate = new();
    /// <summary>Rates expressed as units per euro, indexed by ISO currency code.</summary>
    private Dictionary<string, StoredRate> _rates = Load(cachePath);

    /// <inheritdoc />
    public decimal Convert(decimal amount, string fromCurrency, string toCurrency)
    {
        var from = GetRate(fromCurrency);
        var to = GetRate(toCurrency);
        return Math.Round(amount / from.UnitsPerEuro * to.UnitsPerEuro, 4);
    }

    /// <inheritdoc />
    public IReadOnlyList<CurrencyRateResponse> List()
    {
        lock (_gate) return _rates.Values.Select(ToResponse).OrderBy(x => x.Code).ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CurrencyRateResponse>> RefreshAsync(CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(nameof(LocalCurrencyService));
        var xml = await client.GetStringAsync(EcbUrl, cancellationToken);
        var document = XDocument.Parse(xml);
        var now = DateTimeOffset.UtcNow;
        // ECB rates are quoted against EUR, so the base rate must always be present.
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

    /// <inheritdoc />
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

    /// <summary>Retrieves a validated rate or explains how the user can make it available.</summary>
    private StoredRate GetRate(string currency)
    {
        currency = string.IsNullOrWhiteSpace(currency) ? "EUR" : currency.Trim().ToUpperInvariant();
        lock (_gate)
        {
            if (_rates.TryGetValue(currency, out var rate)) return rate;
        }
        throw new InvalidOperationException($"No conversion rate is available for {currency}. Add it manually or refresh ECB rates.");
    }

    /// <summary>Loads the last valid local cache and falls back to a small offline starter set.</summary>
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

    /// <summary>Persists the sorted cache so projections remain available without a network connection.</summary>
    private static void Save(string path, Dictionary<string, StoredRate> rates)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(rates.Values.OrderBy(x => x.Code)));
    }

    /// <summary>Maps an internal rate to the API response and flags stale data.</summary>
    private static CurrencyRateResponse ToResponse(StoredRate rate) => new(rate.Code, rate.UnitsPerEuro, rate.UpdatedAt, rate.Source, DateTimeOffset.UtcNow - rate.UpdatedAt > TimeSpan.FromDays(2));

    /// <summary>Internal local representation of one exchange rate.</summary>
    private sealed record StoredRate(string Code, decimal UnitsPerEuro, DateTimeOffset UpdatedAt, string Source);
}
