using System.Globalization;
using System.Text.Json;
using LifeLedger.Api.Data;
using LifeLedger.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LifeLedger.Api.Services;

/// <summary>Describes the outcome of refreshing one public market ticker.</summary>
public sealed record MarketRefreshResult(Guid AssetId, string Ticker, bool Updated, decimal? Price, string? Currency, string? Error);

/// <summary>Refreshes optional public ETF and stock quotes while keeping financial data local.</summary>
public interface IMarketDataService
{
    /// <summary>Refreshes all tracked ETF and stock tickers and records their local price snapshots.</summary>
    Task<IReadOnlyList<MarketRefreshResult>> RefreshAsync(CancellationToken cancellationToken);
}

/// <summary>Optional, best-effort market quotes. Only public tickers leave the local server.</summary>
public sealed class MarketDataService(LifeLedgerDbContext db, IHttpClientFactory httpClientFactory, ILogger<MarketDataService> logger) : IMarketDataService
{
    /// <summary>Public chart endpoint used solely with a user-entered ticker.</summary>
    private const string YahooChartUrl = "https://query1.finance.yahoo.com/v8/finance/chart/";

    /// <inheritdoc />
    public async Task<IReadOnlyList<MarketRefreshResult>> RefreshAsync(CancellationToken cancellationToken)
    {
        // The query deliberately excludes all personal fields; only tickers are sent outside the server.
        var assets = await db.Assets.Where(x => (x.Kind == AssetKind.Etf || x.Kind == AssetKind.Stock) && !string.IsNullOrWhiteSpace(x.Ticker)).ToListAsync(cancellationToken);
        var results = new List<MarketRefreshResult>();
        var client = httpClientFactory.CreateClient(nameof(MarketDataService));
        client.Timeout = TimeSpan.FromSeconds(8);

        foreach (var asset in assets)
        {
            var ticker = asset.Ticker!.Trim().ToUpperInvariant();
            try
            {
                // Escape the ticker before composing the request path to avoid malformed external requests.
                var url = $"{YahooChartUrl}{Uri.EscapeDataString(ticker)}?range=5d&interval=1d";
                using var response = await client.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();
                using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
                var meta = json.RootElement.GetProperty("chart").GetProperty("result")[0].GetProperty("meta");
                if (!meta.TryGetProperty("regularMarketPrice", out var priceElement) || !priceElement.TryGetDecimal(out var price) || price <= 0)
                    throw new InvalidOperationException("No market price was returned.");
                var currency = meta.TryGetProperty("currency", out var currencyElement) ? currencyElement.GetString()?.ToUpperInvariant() : null;
                if (string.IsNullOrWhiteSpace(currency)) currency = asset.Currency;

                asset.Ticker = ticker;
                asset.Currency = currency;
                // The quantity entered by the user remains authoritative; only its current value is refreshed.
                asset.CurrentValue = Math.Round(asset.Quantity * price, 4);
                db.AssetQuoteSnapshots.Add(new AssetQuoteSnapshot { AssetId = asset.Id, Price = price, Currency = currency });
                results.Add(new MarketRefreshResult(asset.Id, ticker, true, price, currency, null));
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
            {
                logger.LogInformation(exception, "Unable to refresh market data for {Ticker}", ticker);
                results.Add(new MarketRefreshResult(asset.Id, ticker, false, null, null, "Quote unavailable"));
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        return results;
    }
}
