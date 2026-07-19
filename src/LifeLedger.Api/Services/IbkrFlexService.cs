using System.Globalization;
using System.Xml.Linq;
using LifeLedger.Api.Contracts;
using LifeLedger.Api.Data;
using LifeLedger.Api.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace LifeLedger.Api.Services;

/// <summary>Configures and synchronizes read-only portfolio data from IBKR Flex Web Service.</summary>
public interface IIbkrFlexService
{
    /// <summary>Stores a protected access token and activity-query identifier for one scenario.</summary>
    Task ConfigureAsync(Guid scenarioId, IbkrFlexConfigurationRequest request, CancellationToken cancellationToken);
    /// <summary>Returns safe configuration metadata without exposing the access token.</summary>
    Task<IbkrFlexConfigurationStatus> GetStatusAsync(Guid scenarioId, CancellationToken cancellationToken);
    /// <summary>Retrieves an Activity Flex report and applies its open positions to the local scenario.</summary>
    Task<IbkrFlexSyncResult> SyncAsync(Guid scenarioId, CancellationToken cancellationToken);
}

/// <summary>Safe, user-facing state of an IBKR Flex connection.</summary>
public sealed record IbkrFlexConfigurationStatus(bool IsConfigured, long? ActivityQueryId);

/// <summary>Counts the positions created and updated by one successful IBKR Flex synchronization.</summary>
public sealed record IbkrFlexSyncResult(int CreatedAssets, int UpdatedAssets, int ReportedPositions);

/// <summary>Uses IBKR's two-step Flex Web Service and keeps its credential protected in the local database.</summary>
public sealed class IbkrFlexService(
    LifeLedgerDbContext db,
    IHttpClientFactory httpClients,
    IDataProtectionProvider dataProtectionProvider,
    IAssetValuationHistoryService valuationHistory) : FlexService(db, dataProtectionProvider, valuationHistory), IIbkrFlexService
{
    private const string BaseUrl = "https://ndcdyn.interactivebrokers.com/AccountManagement/FlexWebService";
    /// <inheritdoc />
    protected override string ConnectionKey => "ibkr";
    /// <inheritdoc />
    protected override string ProviderName => "IBKR Flex";

    /// <inheritdoc />
    public async Task ConfigureAsync(Guid scenarioId, IbkrFlexConfigurationRequest request, CancellationToken cancellationToken)
    {
        await LoadScenarioAsync(scenarioId, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.AccessToken))
            throw new ArgumentException("An IBKR Flex access token is required.", nameof(request));
        if (request.ActivityQueryId <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "The IBKR Activity Query ID must be positive.");

        await WriteConnectionSettingAsync(scenarioId, "token", CredentialProtector.Protect(request.AccessToken.Trim()), cancellationToken);
        await WriteConnectionSettingAsync(scenarioId, "query", request.ActivityQueryId.ToString(CultureInfo.InvariantCulture), cancellationToken);
        await Database.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IbkrFlexConfigurationStatus> GetStatusAsync(Guid scenarioId, CancellationToken cancellationToken)
    {
        var query = await ReadConnectionSettingAsync(scenarioId, "query", cancellationToken);
        var tokenExists = await ReadConnectionSettingAsync(scenarioId, "token", cancellationToken) is not null;
        return long.TryParse(query, CultureInfo.InvariantCulture, out var queryId) && tokenExists
            ? new IbkrFlexConfigurationStatus(true, queryId)
            : new IbkrFlexConfigurationStatus(false, null);
    }

    /// <inheritdoc />
    public async Task<IbkrFlexSyncResult> SyncAsync(Guid scenarioId, CancellationToken cancellationToken)
    {
        var scenario = await LoadScenarioAsync(scenarioId, cancellationToken);
        var tokenValue = await ReadConnectionSettingAsync(scenarioId, "token", cancellationToken) ?? throw new InvalidOperationException("IBKR Flex is not configured for this scenario.");
        var queryValue = await ReadConnectionSettingAsync(scenarioId, "query", cancellationToken) ?? throw new InvalidOperationException("IBKR Flex is not configured for this scenario.");
        if (!long.TryParse(queryValue, CultureInfo.InvariantCulture, out var queryId)) throw new InvalidOperationException("The stored IBKR Flex query ID is invalid.");

        string token;
        try { token = CredentialProtector.Unprotect(tokenValue); }
        catch (Exception exception) { throw new InvalidOperationException("The protected IBKR Flex token cannot be read on this installation. Configure the connection again.", exception); }

        var client = httpClients.CreateClient(nameof(IbkrFlexService));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("LifeLedger/1.0");
        var referenceDocument = await GetXmlAsync(client, "SendRequest", token, queryId.ToString(CultureInfo.InvariantCulture), cancellationToken);
        EnsureSuccess(referenceDocument);
        var referenceCode = referenceDocument.Descendants().FirstOrDefault(element => element.Name.LocalName == "ReferenceCode")?.Value;
        if (string.IsNullOrWhiteSpace(referenceCode)) throw new InvalidOperationException("IBKR did not return a Flex report reference code.");

        XDocument report;
        for (var attempt = 0; ; attempt++)
        {
            if (attempt > 12) throw new InvalidOperationException("IBKR did not finish generating the Flex report in time.");
            if (attempt > 0) await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            report = await GetXmlAsync(client, "GetStatement", token, referenceCode, cancellationToken);
            if (IsInProgress(report)) continue;
            EnsureSuccess(report);
            break;
        }

        var positions = ParseOpenPositions(report).ToArray();
        var created = 0;
        var updated = 0;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var position in positions)
        {
            var asset = scenario.Assets.SingleOrDefault(candidate => IsImportedAsset(candidate, position.Id));
            if (asset is null)
            {
                asset = new Asset { ScenarioId = scenarioId, ExternalProvider = ProviderName, ExternalId = position.Id, IsLiquid = true };
                scenario.Assets.Add(asset);
                created++;
            }
            else updated++;

            asset.Name = position.Name;
            asset.Kind = position.Kind;
            asset.Ticker = position.Symbol;
            asset.Quantity = position.Quantity;
            asset.CurrentValue = position.Value;
            asset.Currency = position.Currency;
            asset.ValuedOn = today;
            asset.ValuationSource = ProviderName;
            await ValuationHistory.RecordAsync(asset, today, ProviderName, cancellationToken);
        }
        await Database.SaveChangesAsync(cancellationToken);
        return new IbkrFlexSyncResult(created, updated, positions.Length);
    }

    private async Task<XDocument> GetXmlAsync(HttpClient client, string endpoint, string token, string query, CancellationToken cancellationToken)
    {
        var url = $"{BaseUrl}/{endpoint}?t={Uri.EscapeDataString(token)}&q={Uri.EscapeDataString(query)}&v=3";
        using var response = await client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
    }

    private static bool IsInProgress(XDocument document) => document.Descendants().Any(element => element.Name.LocalName == "ErrorCode" && element.Value == "1019");

    private static void EnsureSuccess(XDocument document)
    {
        var status = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "Status")?.Value;
        if (status is null or "Success") return;
        var error = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "ErrorMessage")?.Value ?? "IBKR Flex request failed.";
        throw new InvalidOperationException($"IBKR Flex: {error}");
    }

    private static IEnumerable<IbkrPosition> ParseOpenPositions(XDocument document) => document.Descendants()
        .Where(element => element.Name.LocalName == "OpenPosition")
        .Select(element =>
        {
            var id = Attribute(element, "conid") ?? Attribute(element, "conidex") ?? throw new InvalidOperationException("An IBKR open position is missing its instrument identifier.");
            var quantity = DecimalAttribute(element, "position");
            var value = DecimalAttribute(element, "positionValue");
            var currency = Attribute(element, "currency")?.ToUpperInvariant() ?? throw new InvalidOperationException("An IBKR open position is missing its currency.");
            return new IbkrPosition(id, Attribute(element, "symbol") ?? Attribute(element, "description") ?? id, Attribute(element, "symbol"), quantity, value, currency, ToAssetKind(Attribute(element, "assetCategory")));
        });

    private static string? Attribute(XElement element, string name) => element.Attribute(name)?.Value?.Trim();
    private static decimal DecimalAttribute(XElement element, string name) => decimal.TryParse(Attribute(element, name), NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var value) ? value : 0m;
    private static AssetKind ToAssetKind(string? category) => category?.ToUpperInvariant() switch
    {
        "STK" => AssetKind.Stock,
        "ETF" or "FUND" => AssetKind.Etf,
        "CASH" => AssetKind.Cash,
        "CRYPTO" => AssetKind.Crypto,
        _ => AssetKind.Other
    };
    private sealed record IbkrPosition(string Id, string Name, string? Symbol, decimal Quantity, decimal Value, string Currency, AssetKind Kind);
}
