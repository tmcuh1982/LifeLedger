using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using LifeLedger.Api.Contracts;
using LifeLedger.Api.Data;
using LifeLedger.Api.Domain;
using LifeLedger.Api.Plugins;
using LifeLedger.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

// Support both `dotnet run` from the repository root and execution from the published project directory.
var workingDirectory = Directory.GetCurrentDirectory();
var sourceProjectDirectory = Path.Combine(workingDirectory, "src", "LifeLedger.Api");
var contentRoot = Directory.Exists(sourceProjectDirectory) ? sourceProjectDirectory : workingDirectory;
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = contentRoot });
builder.Logging.AddProvider(new LocalFileLoggerProvider(Path.Combine(contentRoot, "data", "logs")));
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});

// SQLite is the private, dependency-free default; PostgreSQL is selected only by explicit configuration.
var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
builder.Services.AddDbContext<LifeLedgerDbContext>((serviceProvider, options) =>
{
    // Resolve configuration when the context is created so test and deployment overrides are honoured.
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    var environment = serviceProvider.GetRequiredService<IHostEnvironment>();
    var selectedProvider = configuration["Database:Provider"] ?? "Sqlite";
    var selectedConnectionString = configuration.GetConnectionString("LifeLedger") ?? "Data Source=data/lifeledger.db";
    if (selectedProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase) || selectedProvider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
        options.UseNpgsql(selectedConnectionString);
    else
    {
        var sqlite = new SqliteConnectionStringBuilder(selectedConnectionString);
        if (!string.IsNullOrWhiteSpace(sqlite.DataSource) && sqlite.DataSource != ":memory:")
        {
            // Relative SQLite paths always belong to the active host, including isolated test hosts.
            sqlite.DataSource = Path.IsPathRooted(sqlite.DataSource) ? sqlite.DataSource : Path.Combine(environment.ContentRootPath, sqlite.DataSource);
            Directory.CreateDirectory(Path.GetDirectoryName(sqlite.DataSource)!);
        }
        options.UseSqlite(sqlite.ToString());
    }
});
// The development frontend is explicitly allow-listed; deployments can replace this configuration.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173", "http://127.0.0.1:5173"];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddSingleton<ICountryCatalog, CountryCatalog>();
builder.Services.AddScoped<IScenarioRepository, ScenarioRepository>();
builder.Services.AddSingleton<IIncomeScheduleService, IncomeScheduleService>();
builder.Services.AddSingleton<IExpenseScheduleService, ExpenseScheduleService>();
builder.Services.AddScoped<IDatabaseMigrator, DatabaseMigrator>();
builder.Services.AddScoped<IDataSchemaMigrationService, DataSchemaMigrationService>();
builder.Services.AddScoped<IDataSchemaMigration, AssetValuationDataMigration>();
builder.Services.AddScoped<IDataSchemaMigration, IncomeScheduleDataMigration>();
builder.Services.AddScoped<IDataSchemaMigration, BankingDataMigration>();
builder.Services.AddScoped<IDataSchemaMigration, EventCurrencyDataMigration>();
builder.Services.AddScoped<IDataSchemaMigration, PlannedAssetSaleDataMigration>();
builder.Services.AddScoped<IDataSchemaMigration, OwnershipDataMigration>();
builder.Services.AddScoped<IDataImportService, DataImportService>();
builder.Services.AddScoped<IAssetCategoryService, AssetCategoryService>();
builder.Services.AddScoped<AssetProfileCatalog>();
builder.Services.AddScoped<IAssetProfileCatalog>(services => services.GetRequiredService<AssetProfileCatalog>());
builder.Services.AddScoped<ICustomAssetProfileService>(services => services.GetRequiredService<AssetProfileCatalog>());
builder.Services.AddScoped<IAssetDossierService, AssetDossierService>();
builder.Services.AddScoped<ILiabilityService, LiabilityService>();
builder.Services.AddScoped<IAssetValuationHistoryService, AssetValuationHistoryService>();
builder.Services.AddScoped<IAllocationStrategyService, AllocationStrategyService>();
builder.Services.AddScoped<IPlannedAssetSaleService, PlannedAssetSaleService>();
builder.Services.AddScoped<IBankStatementImportModule, BankStatementImportModule>();
builder.Services.AddScoped<IMarketDataService, MarketDataService>();
builder.Services.AddScoped<INetWorthHistoryService, NetWorthHistoryService>();
builder.Services.AddHttpClient();
// Flex credentials are encrypted with keys kept beside the local database, never in source control or API responses.
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(contentRoot, "data", "protection-keys")));
builder.Services.AddScoped<IIbkrFlexService, IbkrFlexService>();
builder.Services.AddSingleton<ICurrencyService>(serviceProvider => new LocalCurrencyService(
    Path.Combine(builder.Environment.ContentRootPath, "data", "currency-rates.local.json"),
    serviceProvider.GetRequiredService<IHttpClientFactory>(),
    serviceProvider.GetRequiredService<ILogger<LocalCurrencyService>>()));

// Plugins are loaded once at startup and only from the server's local plugin directory.
var pluginRegistry = new PluginRegistry();
pluginRegistry.Load(Path.Combine(builder.Environment.ContentRootPath, builder.Configuration["Plugins:Directory"] ?? "plugins"), LoggerFactory.Create(logging => logging.AddConsole()).CreateLogger<PluginRegistry>());
builder.Services.AddSingleton(pluginRegistry);
builder.Services.AddSingleton<IProjectionEngine>(serviceProvider => new ProjectionEngine(
    serviceProvider.GetRequiredService<ICurrencyService>(),
    serviceProvider.GetRequiredService<IIncomeScheduleService>(),
    serviceProvider.GetRequiredService<IExpenseScheduleService>(),
    pluginRegistry.ProjectionModifiers));

var app = builder.Build();
// Capture request duration and failures in the local log without sending data to a third party.
app.Use(async (context, next) =>
{
    var startedAt = Stopwatch.GetTimestamp();
    var requestFailed = false;
    try
    {
        await next(context);
    }
    catch (Exception exception)
    {
        requestFailed = true;
        app.Logger.LogError(exception, "Unhandled request {Method} {Path}", context.Request.Method, context.Request.Path);
        throw;
    }
    finally
    {
        var elapsed = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        // The response still contains its initial 200 while an unhandled exception unwinds; log the effective server result instead.
        var effectiveStatusCode = requestFailed ? StatusCodes.Status500InternalServerError : context.Response.StatusCode;
        var level = effectiveStatusCode >= StatusCodes.Status400BadRequest ? LogLevel.Warning : LogLevel.Information;
        app.Logger.Log(level, "HTTP {Method} {Path} returned {StatusCode} in {ElapsedMs:0} ms", context.Request.Method, context.Request.Path, effectiveStatusCode, elapsed);
    }
});
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();

using (var scope = app.Services.CreateScope())
{
    // Schema is ready before seeding or serving any request.
    var migrator = scope.ServiceProvider.GetRequiredService<IDatabaseMigrator>();
    await migrator.ApplyAsync();
    await scope.ServiceProvider.GetRequiredService<IDataSchemaMigrationService>().EnsureCurrentAsync();
    var db = scope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
    if (app.Configuration.GetValue("SeedDemoData", true))
        await DemoDataSeeder.SeedAsync(db, app.Configuration["DemoDataDirectory"] ?? Path.Combine(app.Environment.ContentRootPath, "data"));
    await scope.ServiceProvider.GetRequiredService<INetWorthHistoryService>().CaptureAsync();
}

var api = app.MapGroup("/api");
api.MapGet("/health", (PluginRegistry plugins) => Results.Ok(new { status = "ok", storage = provider, plugins = plugins.Plugins }));
api.MapGet("/countries", (ICountryCatalog countries) => Results.Ok(countries.List()));
api.MapGet("/plugins", (PluginRegistry plugins) => Results.Ok(plugins.Plugins));
api.MapGet("/asset-profile-definitions", async (IAssetProfileCatalog profiles, CancellationToken ct) => Results.Ok(await profiles.ListAsync(ct)));
api.MapPost("/asset-profile-definitions", async (AssetProfileDefinitionRequest request, ICustomAssetProfileService profiles, CancellationToken ct) =>
{
    try { return Results.Created("/api/asset-profile-definitions", await profiles.AddAsync(request, ct)); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["definition"] = [exception.Message] }); }
});
api.MapPut("/asset-profile-definitions/{key}", async (string key, AssetProfileDefinitionRequest request, ICustomAssetProfileService profiles, CancellationToken ct) =>
{
    try { return Results.Ok(await profiles.UpdateAsync(key, request, ct)); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["definition"] = [exception.Message] }); }
    catch (KeyNotFoundException) { return Results.NotFound(); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
});
api.MapDelete("/asset-profile-definitions/{key}", async (string key, ICustomAssetProfileService profiles, CancellationToken ct) =>
{
    try { await profiles.DeleteAsync(key, ct); return Results.NoContent(); }
    catch (KeyNotFoundException) { return Results.NotFound(); }
    catch (AssetProfileDefinitionInUseException exception) { return Results.Conflict(new { message = exception.Message }); }
});
api.MapGet("/asset-categories", async (IAssetCategoryService categories, CancellationToken ct) => Results.Ok(await categories.ListAsync(ct)));
api.MapPost("/asset-categories", async (AssetCategoryNameRequest request, IAssetCategoryService categories, CancellationToken ct) =>
{
    try { return Results.Created("/api/asset-categories", await categories.AddAsync(request.Name, ct)); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = [exception.Message] }); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
});
api.MapPut("/asset-categories/{name}", async (string name, AssetCategoryNameRequest request, IAssetCategoryService categories, CancellationToken ct) =>
{
    try { return Results.Ok(await categories.RenameAsync(name, request.Name, ct)); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = [exception.Message] }); }
    catch (KeyNotFoundException) { return Results.NotFound(); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
});
api.MapDelete("/asset-categories/{name}", async (string name, IAssetCategoryService categories, CancellationToken ct) =>
{
    try { await categories.DeleteAsync(name, ct); return Results.NoContent(); }
    catch (KeyNotFoundException) { return Results.NotFound(); }
    catch (AssetCategoryInUseException exception) { return Results.Conflict(new { message = exception.Message }); }
});
MapBankingEndpoints(api);
api.MapGet("/currencies", (ICurrencyService currencies) => Results.Ok(currencies.List()));
api.MapPost("/currencies/refresh", async (ICurrencyService currencies, CancellationToken ct) => Results.Ok(await currencies.RefreshAsync(ct)));
api.MapPut("/currencies/{code}", (string code, UpsertCurrencyRateRequest request, ICurrencyService currencies) =>
{
    try { return Results.Ok(currencies.SetManual(code, request.UnitsPerEuro)); }
    catch (ArgumentOutOfRangeException) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["unitsPerEuro"] = ["The rate must be greater than zero."] }); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["code"] = [exception.Message] }); }
});
api.MapPost("/market/refresh", async (IMarketDataService marketData, CancellationToken ct) => Results.Ok(await marketData.RefreshAsync(ct)));
api.MapGet("/scenarios/{scenarioId:guid}/allocation-strategies", async (Guid scenarioId, IAllocationStrategyService strategies, CancellationToken ct) =>
    Results.Ok(await strategies.ListAsync(scenarioId, ct)));
api.MapPost("/scenarios/{scenarioId:guid}/allocation-strategies", async (Guid scenarioId, AllocationStrategyRequest request, IAllocationStrategyService strategies, CancellationToken ct) =>
{
    try { var strategy = await strategies.CreateAsync(scenarioId, request, ct); return Results.Created($"/api/allocation-strategies/{strategy.Id}", strategy); }
    catch (KeyNotFoundException) { return Results.NotFound(); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["strategy"] = [exception.Message] }); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
});
api.MapPut("/allocation-strategies/{strategyId:guid}", async (Guid strategyId, AllocationStrategyRequest request, IAllocationStrategyService strategies, CancellationToken ct) =>
{
    try { var strategy = await strategies.UpdateAsync(strategyId, request, ct); return strategy is null ? Results.NotFound() : Results.Ok(strategy); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["strategy"] = [exception.Message] }); }
    catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
});
api.MapDelete("/allocation-strategies/{strategyId:guid}", async (Guid strategyId, IAllocationStrategyService strategies, CancellationToken ct) =>
    await strategies.DeleteAsync(strategyId, ct) ? Results.NoContent() : Results.NotFound());
api.MapGet("/scenarios/{scenarioId:guid}/integrations/ibkr-flex", async (Guid scenarioId, IIbkrFlexService flex, CancellationToken ct) =>
    Results.Ok(await flex.GetStatusAsync(scenarioId, ct)));
api.MapPut("/scenarios/{scenarioId:guid}/integrations/ibkr-flex", async (Guid scenarioId, IbkrFlexConfigurationRequest request, IIbkrFlexService flex, CancellationToken ct) =>
{
    try { await flex.ConfigureAsync(scenarioId, request, ct); return Results.NoContent(); }
    catch (KeyNotFoundException) { return Results.NotFound(); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["configuration"] = [exception.Message] }); }
});
api.MapPost("/scenarios/{scenarioId:guid}/integrations/ibkr-flex/sync", async (Guid scenarioId, IIbkrFlexService flex, CancellationToken ct) =>
{
    try { return Results.Ok(await flex.SyncAsync(scenarioId, ct)); }
    catch (KeyNotFoundException) { return Results.NotFound(); }
    catch (InvalidOperationException exception) { return Results.BadRequest(new { message = exception.Message }); }
});
api.MapGet("/assets/{id:guid}/history", async (Guid id, LifeLedgerDbContext db, CancellationToken ct) =>
    Results.Ok((await db.AssetQuoteSnapshots.AsNoTracking().Where(x => x.AssetId == id)
        .Select(x => new { x.CapturedAt, x.Price, x.Currency, x.Source }).ToListAsync(ct))
        .OrderBy(x => x.CapturedAt)));
api.MapGet("/assets/{id:guid}/valuations", async (Guid id, LifeLedgerDbContext db, IAssetValuationHistoryService valuations, CancellationToken ct) =>
    await db.Assets.AnyAsync(asset => asset.Id == id, ct)
        ? Results.Ok(await valuations.ListAsync(id, ct))
        : Results.NotFound());
api.MapGet("/scenarios/{id:guid}/net-worth-history", async (Guid id, LifeLedgerDbContext db, CancellationToken ct) =>
{
    var profileId = await db.Scenarios.Where(scenario => scenario.Id == id).Select(scenario => (Guid?)scenario.ProfileId).FirstOrDefaultAsync(ct);
    if (profileId is null) return Results.NotFound();
    var history = (await db.NetWorthSnapshots.AsNoTracking()
        .Where(snapshot => snapshot.ProfileId == profileId)
        .Select(snapshot => new NetWorthSnapshotResponse(snapshot.CapturedAt, snapshot.NetWorth, snapshot.Currency))
        .ToListAsync(ct))
        .OrderBy(snapshot => snapshot.CapturedAt);
    return Results.Ok(history);
});
// Removing price history deliberately does not alter the current value of the user's assets.
api.MapDelete("/market/history", async (LifeLedgerDbContext db, CancellationToken ct) =>
{
    await db.AssetQuoteSnapshots.ExecuteDeleteAsync(ct);
    return Results.NoContent();
});
api.MapDelete("/net-worth-history", async (LifeLedgerDbContext db, CancellationToken ct) =>
{
    await db.NetWorthSnapshots.ExecuteDeleteAsync(ct);
    return Results.NoContent();
});

api.MapPost("/demo/restore", async (LifeLedgerDbContext db, IHostEnvironment environment, IConfiguration configuration, CancellationToken ct) =>
{
    // This explicit local action is destructive by design: the client confirms before replacing the current ledger.
    await DemoDataSeeder.RestoreAsync(db, configuration["DemoDataDirectory"] ?? Path.Combine(environment.ContentRootPath, "data"), ct);
    return Results.Ok(new
    {
        datasetVersion = DemoDataSeeder.DatasetVersion,
        profileId = DemoDataSeeder.ProfileId,
        scenarioId = DemoDataSeeder.BaselineScenarioId
    });
});

api.MapDelete("/data", async (LifeLedgerDbContext db, IHostEnvironment environment, IConfiguration configuration, CancellationToken ct) =>
{
    // Delete children first so this remains safe even if a deployment uses stricter foreign-key settings.
    await using var transaction = await db.Database.BeginTransactionAsync(ct);
    await db.BankTransactions.ExecuteDeleteAsync(ct);
    await db.BankStatementImports.ExecuteDeleteAsync(ct);
    await db.BankAccounts.ExecuteDeleteAsync(ct);
    await db.AssetQuoteSnapshots.ExecuteDeleteAsync(ct);
    await db.AssetValuationSnapshots.ExecuteDeleteAsync(ct);
    await db.NetWorthSnapshots.ExecuteDeleteAsync(ct);
    await db.Scenarios.ExecuteDeleteAsync(ct);
    await db.Profiles.ExecuteDeleteAsync(ct);
    await db.ApplicationSettings.Where(setting => setting.Key == AssetCategoryService.SettingKey || setting.Key == AssetProfileCatalog.SettingKey || setting.Key.StartsWith("flex:")).ExecuteDeleteAsync(ct);
    await transaction.CommitAsync(ct);

    // Keep the installation empty instead of recreating the sample plan on the next startup.
    await DemoDataSeeder.DisableFutureSeedingAsync(configuration["DemoDataDirectory"] ?? Path.Combine(environment.ContentRootPath, "data"), ct);
    return Results.NoContent();
});

api.MapGet("/profiles", async (LifeLedgerDbContext db, CancellationToken ct) =>
    await db.Profiles.AsNoTracking().Include(x => x.Careers).OrderBy(x => x.CreatedAt).ToListAsync(ct));

api.MapGet("/profiles/{id:guid}", async (Guid id, LifeLedgerDbContext db, CancellationToken ct) =>
    await db.Profiles.AsNoTracking().Include(x => x.Careers).FirstOrDefaultAsync(x => x.Id == id, ct) is { } profile
        ? Results.Ok(profile) : Results.NotFound());

api.MapPut("/profiles/{id:guid}", async (Guid id, Profile input, LifeLedgerDbContext db, CancellationToken ct) =>
{
    var profile = await db.Profiles.Include(x => x.Careers).FirstOrDefaultAsync(x => x.Id == id, ct);
    if (profile is null) return Results.NotFound();
    if (input.BirthDate > DateOnly.FromDateTime(DateTime.UtcNow))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["birthDate"] = ["The date of birth cannot be in the future."]
        });
    }
    profile.DisplayName = input.DisplayName.Trim();
    profile.BirthDate = input.BirthDate;
    // Older backups have no sex field and bind to Neutral; unknown enum values are also kept neutral.
    profile.Sex = Enum.IsDefined(input.Sex) ? input.Sex : ProfileSex.Neutral;
    profile.HomeCountryCode = input.HomeCountryCode.ToUpperInvariant();
    profile.BaseCurrency = input.BaseCurrency.ToUpperInvariant();
    profile.ExpectedLifespan = Math.Clamp(input.ExpectedLifespan, 50, 130);
    profile.PartnerBirthYear = input.PartnerBirthYear;
    profile.ChildrenCount = Math.Max(0, input.ChildrenCount);
    SynchronizeCareers(profile, input.Careers, db);
    await db.SaveChangesAsync(ct);
    return Results.Ok(profile);
});

api.MapGet("/scenarios", async (LifeLedgerDbContext db, CancellationToken ct) =>
    Results.Ok(await db.Scenarios.AsNoTracking().OrderByDescending(x => x.IsBaseline).ThenBy(x => x.Name)
        .Select(x => new { x.Id, x.ProfileId, x.Name, x.Description, x.IsBaseline, x.StartsOn, x.ParentScenarioId, x.UpdatedAt }).ToListAsync(ct)));

api.MapGet("/scenarios/{id:guid}", async (Guid id, IScenarioRepository repository, CancellationToken ct) =>
{
    var scenario = await repository.GetAsync(id, ct);
    return scenario is null ? Results.NotFound() : Results.Ok(new ScenarioDetail(scenario, scenario.Profile!));
});

api.MapGet("/scenarios/{id:guid}/data", async (Guid id, IScenarioRepository repository, CancellationToken ct) =>
{
    var scenario = await repository.GetAsync(id, ct);
    return scenario is null ? Results.NotFound() : Results.Ok(new { scenario.Incomes, scenario.Assets, scenario.Liabilities, scenario.Expenses, scenario.Investments, scenario.AssetSales, scenario.Events });
});

api.MapPost("/scenarios", async (CreateScenarioRequest request, LifeLedgerDbContext db, IScenarioRepository repository, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Name)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["A scenario name is required."] });
    var profile = await db.Profiles.FirstOrDefaultAsync(x => x.Id == request.ProfileId, ct);
    if (profile is null) return Results.NotFound(new { message = "Profile not found." });
    var parent = request.ParentScenarioId is { } parentId ? await repository.GetAsync(parentId, ct) : null;
    if (request.ParentScenarioId is not null && parent is null) return Results.NotFound(new { message = "Parent scenario not found." });
    var scenario = parent is null ? NewScenario(profile, request) : CloneScenario(parent, request);
    db.Scenarios.Add(scenario);
    await db.SaveChangesAsync(ct);
    return Results.Created($"/api/scenarios/{scenario.Id}", new { scenario.Id, scenario.Name });
});

api.MapPut("/scenarios/{id:guid}", async (Guid id, UpdateScenarioRequest request, LifeLedgerDbContext db, CancellationToken ct) =>
{
    var scenario = await db.Scenarios.Include(x => x.Assumptions).FirstOrDefaultAsync(x => x.Id == id, ct);
    if (scenario is null) return Results.NotFound();
    scenario.Name = request.Name.Trim();
    scenario.Description = request.Description.Trim();
    scenario.IsBaseline = request.IsBaseline;
    scenario.StartsOn = request.StartsOn;
    scenario.UpdatedAt = DateTimeOffset.UtcNow;
    scenario.Assumptions.InflationRate = request.Assumptions.InflationRate;
    scenario.Assumptions.SalaryGrowthRate = request.Assumptions.SalaryGrowthRate;
    scenario.Assumptions.SafeWithdrawalRate = request.Assumptions.SafeWithdrawalRate;
    scenario.Assumptions.RetirementAge = request.Assumptions.RetirementAge;
    scenario.Assumptions.MonteCarloRuns = Math.Clamp(request.Assumptions.MonteCarloRuns, 50, 5_000);
    scenario.Assumptions.DefaultReturnVolatility = request.Assumptions.DefaultReturnVolatility;
    // A profile can only have one baseline scenario at a time.
    if (scenario.IsBaseline)
        await db.Scenarios.Where(x => x.ProfileId == scenario.ProfileId && x.Id != scenario.Id).ExecuteUpdateAsync(x => x.SetProperty(y => y.IsBaseline, false), ct);
    await db.SaveChangesAsync(ct);
    return Results.Ok(scenario);
});

api.MapDelete("/scenarios/{id:guid}", async (Guid id, LifeLedgerDbContext db, CancellationToken ct) =>
{
    var scenario = await db.Scenarios.FirstOrDefaultAsync(x => x.Id == id, ct);
    if (scenario is null) return Results.NotFound();
    db.Scenarios.Remove(scenario);
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
});

MapIncomeEndpoints(api);
api.MapPost("/scenarios/{scenarioId:guid}/asset-dossiers", async (Guid scenarioId, AssetDossierRequest request, IAssetDossierService dossiers, CancellationToken ct) =>
{
    try { var dossier = await dossiers.CreateAsync(scenarioId, request, ct); return Results.Created($"/api/assets/{dossier.Asset.Id}/dossier", dossier); }
    catch (KeyNotFoundException) { return Results.NotFound(); }
    catch (AssetDossierValidationException exception) { return Results.ValidationProblem(exception.Errors); }
});
api.MapGet("/assets/{id:guid}/dossier", async (Guid id, IAssetDossierService dossiers, CancellationToken ct) =>
    await dossiers.GetAsync(id, ct) is { } dossier ? Results.Ok(dossier) : Results.NotFound());
api.MapPut("/assets/{id:guid}/dossier", async (Guid id, AssetDossierRequest request, IAssetDossierService dossiers, CancellationToken ct) =>
{
    try { return await dossiers.UpdateAsync(id, request, ct) is { } dossier ? Results.Ok(dossier) : Results.NotFound(); }
    catch (AssetDossierValidationException exception) { return Results.ValidationProblem(exception.Errors); }
});
api.MapDelete("/assets/{id:guid}", async (Guid id, LifeLedgerDbContext db, CancellationToken ct) =>
{
    var asset = await db.Assets.FindAsync([id], ct);
    if (asset is null) return Results.NotFound();
    db.Assets.Remove(asset);
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
});
api.MapPost("/scenarios/{scenarioId:guid}/liabilities", async (Guid scenarioId, LiabilityRequest request, ILiabilityService liabilities, CancellationToken ct) =>
{
    try { var liability = await liabilities.CreateAsync(scenarioId, request, ct); return Results.Created($"/api/liabilities/{liability.Id}", liability); }
    catch (KeyNotFoundException) { return Results.NotFound(); }
    catch (LiabilityValidationException exception) { return Results.ValidationProblem(exception.Errors); }
});
api.MapPut("/liabilities/{id:guid}", async (Guid id, LiabilityRequest request, ILiabilityService liabilities, CancellationToken ct) =>
{
    try { return await liabilities.UpdateAsync(id, request, ct) is { } liability ? Results.Ok(liability) : Results.NotFound(); }
    catch (LiabilityValidationException exception) { return Results.ValidationProblem(exception.Errors); }
});
api.MapDelete("/liabilities/{id:guid}", async (Guid id, LifeLedgerDbContext db, CancellationToken ct) =>
{
    var liability = await db.Liabilities.FindAsync([id], ct);
    if (liability is null) return Results.NotFound();
    db.Liabilities.Remove(liability);
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
});
MapExpenseEndpoints(api);
MapCollection<InvestmentPlan>(api, "investments", (db, id) => db.Investments.FindAsync([id]), item => item.Id, (item, id) => item.Id = id, item => item.ScenarioId, (item, scenarioId) => item.ScenarioId = scenarioId);
api.MapPost("/scenarios/{scenarioId:guid}/asset-sales", async (Guid scenarioId, PlannedAssetSaleRequest request, IPlannedAssetSaleService sales, CancellationToken ct) =>
{
    try { var sale = await sales.CreateAsync(scenarioId, request, ct); return Results.Created($"/api/asset-sales/{sale.Id}", sale); }
    catch (KeyNotFoundException) { return Results.NotFound(); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["assetSale"] = [exception.Message] }); }
});
api.MapPut("/asset-sales/{saleId:guid}", async (Guid saleId, PlannedAssetSaleRequest request, IPlannedAssetSaleService sales, CancellationToken ct) =>
{
    try { return await sales.UpdateAsync(saleId, request, ct) is { } sale ? Results.Ok(sale) : Results.NotFound(); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["assetSale"] = [exception.Message] }); }
});
api.MapDelete("/asset-sales/{saleId:guid}", async (Guid saleId, IPlannedAssetSaleService sales, CancellationToken ct) =>
    await sales.DeleteAsync(saleId, ct) ? Results.NoContent() : Results.NotFound());
MapCollection<ScenarioEvent>(api, "events", (db, id) => db.Events.FindAsync([id]), item => item.Id, (item, id) => item.Id = id, item => item.ScenarioId, (item, scenarioId) => item.ScenarioId = scenarioId);

api.MapGet("/scenarios/{id:guid}/dashboard", async (Guid id, IScenarioRepository repository, IProjectionEngine engine, CancellationToken ct) =>
{
    var scenario = await repository.GetAsync(id, ct);
    return scenario is null ? Results.NotFound() : Results.Ok(engine.BuildDashboard(scenario));
});

api.MapPost("/scenarios/{id:guid}/simulate", async (Guid id, SimulationRequest request, IScenarioRepository repository, IProjectionEngine engine, CancellationToken ct) =>
{
    var scenario = await repository.GetAsync(id, ct);
    return scenario is null ? Results.NotFound() : Results.Ok(engine.Simulate(scenario, request));
});

api.MapGet("/export", async (LifeLedgerDbContext db, IAssetProfileCatalog profileCatalog, CancellationToken ct) =>
{
    var profiles = await db.Profiles.AsNoTracking().Include(x => x.Careers).ToListAsync(ct);
    // SQLite cannot sort DateTimeOffset columns; the local-first store contains very few profiles, so order them safely in memory.
    var profile = profiles.OrderBy(x => x.CreatedAt).FirstOrDefault();
    if (profile is null) return Results.NotFound();
    var scenarios = await db.Scenarios.AsNoTracking().Where(x => x.ProfileId == profile.Id).AsSplitQuery()
        .Include(x => x.Assumptions).Include(x => x.Incomes).ThenInclude(x => x.MonthlyAllocations)
        .Include(x => x.Assets).ThenInclude(x => x.CharacteristicProfile)
        .Include(x => x.Assets).ThenInclude(x => x.LiabilityLinks)
        .Include(x => x.Assets).ThenInclude(x => x.ValuationSnapshots)
        .Include(x => x.Liabilities).Include(x => x.Expenses).ThenInclude(x => x.AmountChanges).Include(x => x.Investments).Include(x => x.AssetSales).Include(x => x.Events)
        .Include(x => x.AllocationStrategies).ThenInclude(x => x.Targets)
        .Include(x => x.BankAccounts).ThenInclude(x => x.Imports).ThenInclude(x => x.Transactions).ToListAsync(ct);
    return Results.Ok(new LifeLedgerExport(12, DateTimeOffset.UtcNow, profile, scenarios, await profileCatalog.ExportCustomHistoryAsync(ct)));
});

api.MapPost("/import", async (ImportRequest request, IDataImportService importer, CancellationToken ct) =>
{
    try
    {
        var profileId = await importer.ImportAsync(request, ct);
        return Results.Created("/api/profiles", new { id = profileId });
    }
    catch (ImportValidationException exception)
    {
        return Results.ValidationProblem(exception.Errors);
    }
});

app.MapFallbackToFile("index.html");
try
{
    app.Run();
}
catch (IOException exception) when (StartupDiagnostics.IsAddressAlreadyInUse(exception))
{
    var configuredUrls = builder.Configuration["urls"] ?? "the configured local address";
    Console.Error.WriteLine();
    Console.Error.WriteLine("LifeLedger could not start because its local address is already in use.");
    Console.Error.WriteLine($"Address: {configuredUrls}");
    Console.Error.WriteLine("LifeLedger may already be running. Open the address above instead of starting a second instance.");
    Console.Error.WriteLine("If another application owns this port, stop it or start LifeLedger with a different --urls value.");
    Environment.ExitCode = 2;
}

/// <summary>Creates an empty baseline for a new scenario owned by the supplied profile.</summary>
static FinancialScenario NewScenario(Profile profile, CreateScenarioRequest request) => new()
{
    ProfileId = profile.Id, Name = request.Name.Trim(), Description = request.Description?.Trim() ?? string.Empty,
    StartsOn = DateOnly.FromDateTime(DateTime.UtcNow), Assumptions = new SimulationAssumptions()
};

/// <summary>Maps the bank-import Module Interface while keeping multipart handling out of its domain logic.</summary>
static void MapBankingEndpoints(RouteGroupBuilder api)
{
    api.MapGet("/bank-importers", (IBankStatementImportModule module) => Results.Ok(module.ListImporters()));
    api.MapGet("/scenarios/{scenarioId:guid}/bank-accounts", async (Guid scenarioId, IBankStatementImportModule module, CancellationToken ct) => Results.Ok(await module.ListAccountsAsync(scenarioId, ct)));
    api.MapGet("/scenarios/{scenarioId:guid}/bank-transactions", async (Guid scenarioId, IBankStatementImportModule module, CancellationToken ct) => Results.Ok(await module.ListTransactionsAsync(scenarioId, ct)));
    api.MapGet("/scenarios/{scenarioId:guid}/bank-spending-averages", async (Guid scenarioId, IBankStatementImportModule module, CancellationToken ct) => Results.Ok(await module.ListSpendingAveragesAsync(scenarioId, ct)));
    api.MapPost("/scenarios/{scenarioId:guid}/bank-spending-averages/{category}/apply", async (Guid scenarioId, string category, ApplyBankSpendingAverageRequest request, IBankStatementImportModule module, CancellationToken ct) =>
    {
        try { return Results.Ok(await module.ApplySpendingAverageAsync(scenarioId, category, request, ct)); }
        catch (KeyNotFoundException) { return Results.NotFound(); }
        catch (ArgumentException exception) { return Results.BadRequest(new { message = exception.Message }); }
    });
    api.MapPut("/bank-transactions/{transactionId:guid}", async (Guid transactionId, UpdateBankTransactionRequest request, IBankStatementImportModule module, CancellationToken ct) =>
    {
        try { return Results.Ok(await module.UpdateTransactionAsync(transactionId, request, ct)); }
        catch (KeyNotFoundException) { return Results.NotFound(); }
        catch (ArgumentException exception) { return Results.BadRequest(new { message = exception.Message }); }
    });

    api.MapPost("/scenarios/{scenarioId:guid}/bank-statements/preview", async (Guid scenarioId, HttpRequest httpRequest, IBankStatementImportModule module, CancellationToken ct) =>
    {
        try
        {
            var form = await httpRequest.ReadFormAsync(ct);
            var file = form.Files.GetFile("file") ?? throw new ArgumentException("Choose a statement file.");
            var bankKey = form["bankKey"].ToString();
            var bytes = await ReadStatementAsync(file, ct);
            return Results.Ok(await module.PreviewAsync(scenarioId, bankKey, file.FileName, bytes, ct));
        }
        catch (KeyNotFoundException) { return Results.NotFound(); }
        catch (ArgumentException exception) { return Results.BadRequest(new { message = exception.Message }); }
        catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
    }).DisableAntiforgery();

    api.MapPost("/bank-statements/commit", async (HttpRequest httpRequest, IBankStatementImportModule module, CancellationToken ct) =>
    {
        try
        {
            var form = await httpRequest.ReadFormAsync(ct);
            var file = form.Files.GetFile("file") ?? throw new ArgumentException("Choose a statement file.");
            var requestJson = form["request"].ToString();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            options.Converters.Add(new JsonStringEnumConverter());
            var request = JsonSerializer.Deserialize<CommitBankStatementRequest>(requestJson, options) ?? throw new ArgumentException("The import review is missing.");
            var bytes = await ReadStatementAsync(file, ct);
            return Results.Created("/api/bank-statements", await module.CommitAsync(request, file.FileName, bytes, ct));
        }
        catch (KeyNotFoundException) { return Results.NotFound(); }
        catch (JsonException) { return Results.BadRequest(new { message = "The import review is invalid." }); }
        catch (ArgumentException exception) { return Results.BadRequest(new { message = exception.Message }); }
        catch (InvalidOperationException exception) { return Results.Conflict(new { message = exception.Message }); }
    }).DisableAntiforgery();
}

/// <summary>Reads a statement into memory with a conservative local upload limit.</summary>
static async Task<byte[]> ReadStatementAsync(IFormFile file, CancellationToken cancellationToken)
{
    const long maximumBytes = 25 * 1024 * 1024;
    if (file.Length is <= 0 or > maximumBytes) throw new ArgumentException("The statement must be between 1 byte and 25 MB.");
    await using var stream = new MemoryStream((int)file.Length);
    await file.CopyToAsync(stream, cancellationToken);
    return stream.ToArray();
}

/// <summary>Synchronizes submitted career periods without deleting tracked rows that remain in the profile.</summary>
static void SynchronizeCareers(Profile profile, IEnumerable<CareerPeriod> submittedCareers, LifeLedgerDbContext db)
{
    var pendingCareers = submittedCareers.ToList();

    foreach (var storedCareer in profile.Careers.ToList())
    {
        var submittedCareer = pendingCareers.FirstOrDefault(candidate => candidate.Id == storedCareer.Id);
        if (submittedCareer is null)
        {
            // Removing only careers absent from the request avoids delete-then-update conflicts in EF Core.
            db.CareerPeriods.Remove(storedCareer);
            profile.Careers.Remove(storedCareer);
            continue;
        }

        CopyCareerValues(storedCareer, submittedCareer);
        pendingCareers.Remove(submittedCareer);
    }

    foreach (var submittedCareer in pendingCareers)
    {
        var newCareer = new CareerPeriod();
        CopyCareerValues(newCareer, submittedCareer);
        profile.Careers.Add(newCareer);
    }
}

/// <summary>Copies the editable values of a career while preserving database ownership and identity.</summary>
static void CopyCareerValues(CareerPeriod target, CareerPeriod source)
{
    target.CountryCode = source.CountryCode.ToUpperInvariant();
    target.StartedOn = source.StartedOn;
    target.EndedOn = source.EndedOn;
    target.AnnualInsurableIncome = source.AnnualInsurableIncome;
    target.EstimatedMonthlyPublicPension = source.EstimatedMonthlyPublicPension;
    target.PensionAge = source.PensionAge;
    target.Notes = source.Notes;
}

/// <summary>Copies editable financial entries and preserves asset-to-liability relationships with new identities.</summary>
static FinancialScenario CloneScenario(FinancialScenario parent, CreateScenarioRequest request)
{
    var liabilities = parent.Liabilities.ToDictionary(
        source => source.Id,
        source => new Liability { Name = source.Name, Kind = source.Kind, OutstandingBalance = source.OutstandingBalance, ResponsibilityRate = source.ResponsibilityRate, InterestRate = source.InterestRate, MonthlyPayment = source.MonthlyPayment, PaidOffOn = source.PaidOffOn, Currency = source.Currency });
    var assets = parent.Assets.Select(source => new Asset
    {
        Name = source.Name, Kind = source.Kind, CustomCategory = source.CustomCategory, CurrentValue = source.CurrentValue, OwnershipRate = source.OwnershipRate,
        PurchasePrice = source.PurchasePrice, AcquisitionCosts = source.AcquisitionCosts, PurchasedOn = source.PurchasedOn,
        ValuedOn = source.ValuedOn, ValuationSource = source.ValuationSource, ExpectedAnnualReturn = source.ExpectedAnnualReturn,
        Volatility = source.Volatility, IsLiquid = source.IsLiquid, Ticker = source.Ticker, Quantity = source.Quantity,
        ExternalProvider = source.ExternalProvider, ExternalId = source.ExternalId, IsIncludedInPortfolioAllocation = source.IsIncludedInPortfolioAllocation,
        CapitalGainsTaxRate = source.CapitalGainsTaxRate, CapitalGainsTaxCountryCode = source.CapitalGainsTaxCountryCode, Currency = source.Currency,
        CharacteristicProfile = source.CharacteristicProfile is null ? null : new AssetCharacteristicProfile
        {
            DefinitionKey = source.CharacteristicProfile.DefinitionKey,
            DefinitionVersion = source.CharacteristicProfile.DefinitionVersion,
            ValuesJson = source.CharacteristicProfile.ValuesJson
        },
        ValuationSnapshots = source.ValuationSnapshots.Select(snapshot => new AssetValuationSnapshot
        {
            ValuedOn = snapshot.ValuedOn,
            Value = snapshot.Value,
            Currency = snapshot.Currency,
            Source = snapshot.Source,
            RecordedAt = snapshot.RecordedAt
        }).ToList(),
        // Link identities are remapped to the cloned liabilities instead of pointing back to the parent scenario.
        LiabilityLinks = source.LiabilityLinks.Where(link => liabilities.ContainsKey(link.LiabilityId))
            .Select(link => new AssetLiabilityLink { Liability = liabilities[link.LiabilityId], AllocationRate = link.AllocationRate }).ToList()
    }).ToList();
    // Source identifiers are mapped explicitly because two assets may legitimately have the same display name.
    var assetsBySourceId = parent.Assets.Zip(assets).ToDictionary(pair => pair.First.Id, pair => pair.Second);
    var investments = parent.Investments.Select(source => new InvestmentPlan { Name = source.Name, MonthlyContribution = source.MonthlyContribution, ExpectedAnnualReturn = source.ExpectedAnnualReturn, StartsOn = source.StartsOn, EndsOn = source.EndsOn }).ToList();
    var investmentsBySourceId = parent.Investments.Zip(investments).ToDictionary(pair => pair.First.Id, pair => pair.Second);

    return new FinancialScenario
    {
        ProfileId = parent.ProfileId, ParentScenarioId = parent.Id, Name = request.Name.Trim(), Description = request.Description?.Trim() ?? parent.Description,
        StartsOn = parent.StartsOn,
        Assumptions = new SimulationAssumptions { InflationRate = parent.Assumptions.InflationRate, SalaryGrowthRate = parent.Assumptions.SalaryGrowthRate, SafeWithdrawalRate = parent.Assumptions.SafeWithdrawalRate, RetirementAge = parent.Assumptions.RetirementAge, MonteCarloRuns = parent.Assumptions.MonteCarloRuns, DefaultReturnVolatility = parent.Assumptions.DefaultReturnVolatility },
        Incomes = parent.Incomes.Select(x => new IncomeStream
        {
            Name = x.Name, Kind = x.Kind, MonthlyAmount = x.MonthlyAmount, AmountMode = x.AmountMode, AnnualAmount = x.AnnualAmount,
            AnnualGrowthRate = x.AnnualGrowthRate, StartsOn = x.StartsOn, EndsOn = x.EndsOn, IsTaxable = x.IsTaxable,
            TaxRate = x.TaxRate, TaxCountryCode = x.TaxCountryCode, Currency = x.Currency,
            LinkedAsset = x.LinkedAssetId is { } assetId ? assetsBySourceId.GetValueOrDefault(assetId) : null,
            MonthlyAllocations = x.MonthlyAllocations.Select(allocation => new IncomeMonthlyAllocation { Month = allocation.Month, Share = allocation.Share }).ToList()
        }).ToList(),
        Assets = assets,
        AllocationStrategies = parent.AllocationStrategies.Select(strategy => new AllocationStrategy
        {
            Name = strategy.Name,
            Description = strategy.Description,
            EffectiveFrom = strategy.EffectiveFrom,
            EffectiveTo = strategy.EffectiveTo,
            Targets = strategy.Targets.Select(target => new AllocationStrategyTarget { Category = target.Category, TargetPercentage = target.TargetPercentage, TolerancePercentage = target.TolerancePercentage }).ToList()
        }).ToList(),
        Liabilities = liabilities.Values.ToList(),
        Expenses = parent.Expenses.Select(x => new Expense
        {
            Name = x.Name, Kind = x.Kind, Frequency = x.Frequency, MonthlyAmount = x.MonthlyAmount, IndexedToInflation = x.IndexedToInflation,
            SaveInAdvance = x.SaveInAdvance, SavingsStartsOn = x.SavingsStartsOn, StartsOn = x.StartsOn, EndsOn = x.EndsOn, Currency = x.Currency,
            LinkedAsset = x.LinkedAssetId is { } assetId ? assetsBySourceId.GetValueOrDefault(assetId) : null,
            ObservedBankCategory = x.ObservedBankCategory,
            AmountChanges = x.AmountChanges.Select(change => new ExpenseAmountChange { EffectiveOn = change.EffectiveOn, Amount = change.Amount }).ToList()
        }).ToList(),
        Investments = investments,
        AssetSales = parent.AssetSales.Where(sale => assetsBySourceId.ContainsKey(sale.AssetId)).Select(sale => new PlannedAssetSale
        {
            Name = sale.Name,
            Asset = assetsBySourceId[sale.AssetId],
            HappensOn = sale.HappensOn,
            UseProjectedValue = sale.UseProjectedValue,
            GrossSalePrice = sale.GrossSalePrice,
            SellingCosts = sale.SellingCosts,
            CapitalGainsTaxRate = sale.CapitalGainsTaxRate,
            CapitalGainsTaxCountryCode = sale.CapitalGainsTaxCountryCode,
            RepayLinkedLiabilities = sale.RepayLinkedLiabilities,
            Destination = sale.Destination,
            DestinationAsset = sale.DestinationAssetId is { } destinationAssetId ? assetsBySourceId.GetValueOrDefault(destinationAssetId) : null,
            DestinationInvestmentPlan = sale.DestinationInvestmentPlanId is { } destinationPlanId ? investmentsBySourceId.GetValueOrDefault(destinationPlanId) : null,
            Currency = sale.Currency,
            Notes = sale.Notes
        }).ToList(),
        Events = parent.Events.Select(x => new ScenarioEvent { Name = x.Name, Kind = x.Kind, HappensOn = x.HappensOn, RecurrenceFrequency = x.RecurrenceFrequency, RecurrenceEndsOn = x.RecurrenceEndsOn, OneOffCashImpact = x.OneOffCashImpact, MonthlyCashImpact = x.MonthlyCashImpact, Currency = x.Currency, DurationMonths = x.DurationMonths, Notes = x.Notes }).ToList()
    };
}

/// <summary>Maps income endpoints while replacing seasonal allocations atomically on update.</summary>
static void MapIncomeEndpoints(RouteGroupBuilder api)
{
    api.MapPost("/scenarios/{scenarioId:guid}/incomes", async (Guid scenarioId, IncomeStream income, LifeLedgerDbContext db, CancellationToken ct) =>
    {
        if (!await db.Scenarios.AnyAsync(scenario => scenario.Id == scenarioId, ct)) return Results.NotFound();
        if (!await LinkedAssetBelongsToScenarioAsync(db, income.LinkedAssetId, scenarioId, ct)) return InvalidLinkedAsset();
        income.ScenarioId = scenarioId;
        PrepareMonthlyAllocations(income);
        db.Incomes.Add(income);
        await db.SaveChangesAsync(ct);
        return Results.Created("/api/incomes", income);
    });

    api.MapPut("/incomes/{id:guid}", async (Guid id, IncomeStream update, LifeLedgerDbContext db, CancellationToken ct) =>
    {
        var existing = await db.Incomes.Include(income => income.MonthlyAllocations).FirstOrDefaultAsync(income => income.Id == id, ct);
        if (existing is null) return Results.NotFound();
        if (!await LinkedAssetBelongsToScenarioAsync(db, update.LinkedAssetId, existing.ScenarioId, ct)) return InvalidLinkedAsset();

        var allocations = update.MonthlyAllocations;
        update.Id = id;
        update.ScenarioId = existing.ScenarioId;
        db.Entry(existing).CurrentValues.SetValues(update);
        db.IncomeMonthlyAllocations.RemoveRange(existing.MonthlyAllocations);
        existing.MonthlyAllocations = allocations;
        PrepareMonthlyAllocations(existing);
        await db.SaveChangesAsync(ct);
        return Results.Ok(existing);
    });

    api.MapDelete("/incomes/{id:guid}", async (Guid id, LifeLedgerDbContext db, CancellationToken ct) =>
    {
        var income = await db.Incomes.FindAsync([id], ct);
        if (income is null) return Results.NotFound();
        db.Incomes.Remove(income);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    });
}

/// <summary>Maps expense endpoints and prevents links to assets from a different scenario.</summary>
static void MapExpenseEndpoints(RouteGroupBuilder api)
{
    api.MapPost("/scenarios/{scenarioId:guid}/expenses", async (Guid scenarioId, Expense expense, LifeLedgerDbContext db, CancellationToken ct) =>
    {
        if (!await db.Scenarios.AnyAsync(scenario => scenario.Id == scenarioId, ct)) return Results.NotFound();
        if (!await LinkedAssetBelongsToScenarioAsync(db, expense.LinkedAssetId, scenarioId, ct)) return InvalidLinkedAsset();
        var scheduleError = ValidateExpenseAmountChanges(expense);
        if (scheduleError is not null) return scheduleError;
        expense.ScenarioId = scenarioId;
        PrepareExpenseAmountChanges(expense);
        db.Expenses.Add(expense);
        await db.SaveChangesAsync(ct);
        return Results.Created("/api/expenses", expense);
    });

    api.MapPut("/expenses/{id:guid}", async (Guid id, Expense update, LifeLedgerDbContext db, CancellationToken ct) =>
    {
        var existing = await db.Expenses.Include(expense => expense.AmountChanges).FirstOrDefaultAsync(expense => expense.Id == id, ct);
        if (existing is null) return Results.NotFound();
        if (!await LinkedAssetBelongsToScenarioAsync(db, update.LinkedAssetId, existing.ScenarioId, ct)) return InvalidLinkedAsset();
        var scheduleError = ValidateExpenseAmountChanges(update);
        if (scheduleError is not null) return scheduleError;
        var amountChanges = update.AmountChanges;
        update.Id = id;
        update.ScenarioId = existing.ScenarioId;
        db.Entry(existing).CurrentValues.SetValues(update);
        db.ExpenseAmountChanges.RemoveRange(existing.AmountChanges);
        existing.AmountChanges = amountChanges;
        PrepareExpenseAmountChanges(existing);
        await db.SaveChangesAsync(ct);
        return Results.Ok(existing);
    });

    api.MapDelete("/expenses/{id:guid}", async (Guid id, LifeLedgerDbContext db, CancellationToken ct) =>
    {
        var expense = await db.Expenses.FindAsync([id], ct);
        if (expense is null) return Results.NotFound();
        db.Expenses.Remove(expense);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    });
}

/// <summary>Rejects ambiguous or impossible recurring-expense amount schedules.</summary>
static IResult? ValidateExpenseAmountChanges(Expense expense)
{
    if (expense.AmountChanges.Count == 0) return null;
    if (expense.Kind != ExpenseKind.Recurring)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["amountChanges"] = ["Only recurring expenses can have future amount changes."] });
    if (expense.AmountChanges.Any(change => change.Amount < 0 || change.EffectiveOn < expense.StartsOn || (expense.EndsOn is { } endsOn && change.EffectiveOn > endsOn)))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["amountChanges"] = ["Each future amount must be non-negative and effective while the expense is active."] });
    if (expense.AmountChanges.GroupBy(change => change.EffectiveOn).Any(group => group.Count() > 1))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["amountChanges"] = ["Only one future amount can start on the same date."] });
    return null;
}

/// <summary>Normalises ownership and ordering before EF Core stores an expense amount schedule.</summary>
static void PrepareExpenseAmountChanges(Expense expense)
{
    expense.AmountChanges = expense.Kind == ExpenseKind.Recurring
        ? expense.AmountChanges.OrderBy(change => change.EffectiveOn).ToList()
        : [];
    foreach (var change in expense.AmountChanges)
    {
        // Client-provided identities are never trusted when replacing an owned schedule.
        change.Id = Guid.NewGuid();
        change.ExpenseId = expense.Id;
        change.Expense = null;
    }
}

/// <summary>Normalises seasonal rows into one non-negative allocation per calendar month.</summary>
static void PrepareMonthlyAllocations(IncomeStream income)
{
    income.MonthlyAllocations = income.AmountMode == IncomeAmountMode.Seasonal
        ? income.MonthlyAllocations
            .Where(allocation => allocation.Month is >= 1 and <= 12 && allocation.Share > 0m)
            .GroupBy(allocation => allocation.Month)
            .Select(group => new IncomeMonthlyAllocation { IncomeStreamId = income.Id, Month = group.Key, Share = group.Sum(allocation => allocation.Share) })
            .OrderBy(allocation => allocation.Month)
            .ToList()
        : [];
}

/// <summary>Checks that an optional asset link remains inside the owning scenario aggregate.</summary>
static Task<bool> LinkedAssetBelongsToScenarioAsync(LifeLedgerDbContext db, Guid? assetId, Guid scenarioId, CancellationToken ct) =>
    assetId is null ? Task.FromResult(true) : db.Assets.AnyAsync(asset => asset.Id == assetId && asset.ScenarioId == scenarioId, ct);

/// <summary>Returns a safe field-level error for an invalid cross-scenario asset relationship.</summary>
static IResult InvalidLinkedAsset() => Results.ValidationProblem(new Dictionary<string, string[]> { ["linkedAssetId"] = ["The selected asset does not belong to this scenario."] });

/// <summary>Maps consistent create, update, and delete endpoints for a scenario-owned collection.</summary>
static void MapCollection<T>(RouteGroupBuilder api, string resource, Func<LifeLedgerDbContext, Guid, ValueTask<T?>> find, Func<T, Guid> getId, Action<T, Guid> setId, Func<T, Guid> getScenarioId, Action<T, Guid> setScenarioId) where T : class
{
    api.MapPost($"/scenarios/{{scenarioId:guid}}/{resource}", async (Guid scenarioId, T entity, LifeLedgerDbContext db, CancellationToken ct) =>
    {
        if (!await db.Scenarios.AnyAsync(x => x.Id == scenarioId, ct)) return Results.NotFound();
        setScenarioId(entity, scenarioId);
        db.Add(entity);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/{resource}", entity);
    });
    api.MapPut($"/{resource}/{{id:guid}}", async (Guid id, T update, LifeLedgerDbContext db, CancellationToken ct) =>
    {
        var existing = await find(db, id);
        if (existing is null) return Results.NotFound();
        setId(update, getId(existing));
        // Preserve ownership from the persisted entity so a payload cannot move it to another scenario.
        var scenarioId = getScenarioId(existing);
        db.Entry(existing).CurrentValues.SetValues(update);
        setScenarioId(existing, scenarioId);
        await db.SaveChangesAsync(ct);
        return Results.Ok(existing);
    });
    api.MapDelete($"/{resource}/{{id:guid}}", async (Guid id, LifeLedgerDbContext db, CancellationToken ct) =>
    {
        var entity = await find(db, id);
        if (entity is null) return Results.NotFound();
        db.Remove(entity);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    });
}

/// <summary>Marker type that enables in-process integration tests to host the application.</summary>
public partial class Program { }
