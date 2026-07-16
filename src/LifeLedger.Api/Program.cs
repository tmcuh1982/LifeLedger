using System.Diagnostics;
using System.Text.Json.Serialization;
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
var connectionString = builder.Configuration.GetConnectionString("LifeLedger") ?? "Data Source=data/lifeledger.db";
if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
{
    var sqlite = new SqliteConnectionStringBuilder(connectionString);
    if (!string.IsNullOrWhiteSpace(sqlite.DataSource) && sqlite.DataSource != ":memory:")
    {
        // Resolve relative SQLite paths under the content root, never the caller's working directory.
        sqlite.DataSource = Path.IsPathRooted(sqlite.DataSource)
            ? sqlite.DataSource
            : Path.Combine(builder.Environment.ContentRootPath, sqlite.DataSource);
        Directory.CreateDirectory(Path.GetDirectoryName(sqlite.DataSource)!);
        connectionString = sqlite.ToString();
    }
}
builder.Services.AddDbContext<LifeLedgerDbContext>(options =>
{
    if (provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase) || provider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase))
        options.UseNpgsql(connectionString);
    else
        options.UseSqlite(connectionString);
});
// The development frontend is explicitly allow-listed; deployments can replace this configuration.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173", "http://127.0.0.1:5173"];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddSingleton<ICountryCatalog, CountryCatalog>();
builder.Services.AddScoped<IScenarioRepository, ScenarioRepository>();
builder.Services.AddScoped<IDatabaseMigrator, DatabaseMigrator>();
builder.Services.AddScoped<IMarketDataService, MarketDataService>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ICurrencyService>(serviceProvider => new LocalCurrencyService(
    Path.Combine(builder.Environment.ContentRootPath, "data", "currency-rates.local.json"),
    serviceProvider.GetRequiredService<IHttpClientFactory>(),
    serviceProvider.GetRequiredService<ILogger<LocalCurrencyService>>()));

// Plugins are loaded once at startup and only from the server's local plugin directory.
var pluginRegistry = new PluginRegistry();
pluginRegistry.Load(Path.Combine(builder.Environment.ContentRootPath, builder.Configuration["Plugins:Directory"] ?? "plugins"), LoggerFactory.Create(logging => logging.AddConsole()).CreateLogger<PluginRegistry>());
builder.Services.AddSingleton(pluginRegistry);
builder.Services.AddSingleton<IProjectionEngine>(serviceProvider => new ProjectionEngine(serviceProvider.GetRequiredService<ICurrencyService>(), pluginRegistry.ProjectionModifiers));

var app = builder.Build();
// Capture request duration and failures in the local log without sending data to a third party.
app.Use(async (context, next) =>
{
    var startedAt = Stopwatch.GetTimestamp();
    try
    {
        await next(context);
    }
    catch (Exception exception)
    {
        app.Logger.LogError(exception, "Unhandled request {Method} {Path}", context.Request.Method, context.Request.Path);
        throw;
    }
    finally
    {
        var elapsed = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        var level = context.Response.StatusCode >= StatusCodes.Status400BadRequest ? LogLevel.Warning : LogLevel.Information;
        app.Logger.Log(level, "HTTP {Method} {Path} returned {StatusCode} in {ElapsedMs:0} ms", context.Request.Method, context.Request.Path, context.Response.StatusCode, elapsed);
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
    var db = scope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
    if (app.Configuration.GetValue("SeedDemoData", true)) await DemoDataSeeder.SeedAsync(db);
}

var api = app.MapGroup("/api");
api.MapGet("/health", (PluginRegistry plugins) => Results.Ok(new { status = "ok", storage = provider, plugins = plugins.Plugins }));
api.MapGet("/countries", (ICountryCatalog countries) => Results.Ok(countries.List()));
api.MapGet("/plugins", (PluginRegistry plugins) => Results.Ok(plugins.Plugins));
api.MapGet("/currencies", (ICurrencyService currencies) => Results.Ok(currencies.List()));
api.MapPost("/currencies/refresh", async (ICurrencyService currencies, CancellationToken ct) => Results.Ok(await currencies.RefreshAsync(ct)));
api.MapPut("/currencies/{code}", (string code, UpsertCurrencyRateRequest request, ICurrencyService currencies) =>
{
    try { return Results.Ok(currencies.SetManual(code, request.UnitsPerEuro)); }
    catch (ArgumentOutOfRangeException) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["unitsPerEuro"] = ["The rate must be greater than zero."] }); }
    catch (ArgumentException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["code"] = [exception.Message] }); }
});
api.MapPost("/market/refresh", async (IMarketDataService marketData, CancellationToken ct) => Results.Ok(await marketData.RefreshAsync(ct)));
api.MapGet("/assets/{id:guid}/history", async (Guid id, LifeLedgerDbContext db, CancellationToken ct) =>
    Results.Ok(await db.AssetQuoteSnapshots.AsNoTracking().Where(x => x.AssetId == id).OrderBy(x => x.CapturedAt)
        .Select(x => new { x.CapturedAt, x.Price, x.Currency, x.Source }).ToListAsync(ct)));
// Removing price history deliberately does not alter the current value of the user's assets.
api.MapDelete("/market/history", async (LifeLedgerDbContext db, CancellationToken ct) =>
{
    await db.AssetQuoteSnapshots.ExecuteDeleteAsync(ct);
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
    profile.DisplayName = input.DisplayName.Trim();
    profile.BirthDate = input.BirthDate;
    profile.HomeCountryCode = input.HomeCountryCode.ToUpperInvariant();
    profile.BaseCurrency = input.BaseCurrency.ToUpperInvariant();
    profile.ExpectedLifespan = Math.Clamp(input.ExpectedLifespan, 50, 130);
    profile.PartnerBirthYear = input.PartnerBirthYear;
    profile.ChildrenCount = Math.Max(0, input.ChildrenCount);
    // Career periods are replaced as one list to prevent deleted entries from remaining in the database.
    db.CareerPeriods.RemoveRange(profile.Careers);
    profile.Careers = input.Careers.Select(x => new CareerPeriod
    {
        CountryCode = x.CountryCode.ToUpperInvariant(), StartedOn = x.StartedOn, EndedOn = x.EndedOn,
        AnnualInsurableIncome = x.AnnualInsurableIncome, EstimatedMonthlyPublicPension = x.EstimatedMonthlyPublicPension,
        PensionAge = x.PensionAge, Notes = x.Notes
    }).ToList();
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
    return scenario is null ? Results.NotFound() : Results.Ok(new { scenario.Incomes, scenario.Assets, scenario.Liabilities, scenario.Expenses, scenario.Investments, scenario.Events });
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

MapCollection<IncomeStream>(api, "incomes", (db, id) => db.Incomes.FindAsync([id]), item => item.Id, (item, id) => item.Id = id, item => item.ScenarioId, (item, scenarioId) => item.ScenarioId = scenarioId);
MapCollection<Asset>(api, "assets", (db, id) => db.Assets.FindAsync([id]), item => item.Id, (item, id) => item.Id = id, item => item.ScenarioId, (item, scenarioId) => item.ScenarioId = scenarioId);
MapCollection<Liability>(api, "liabilities", (db, id) => db.Liabilities.FindAsync([id]), item => item.Id, (item, id) => item.Id = id, item => item.ScenarioId, (item, scenarioId) => item.ScenarioId = scenarioId);
MapCollection<Expense>(api, "expenses", (db, id) => db.Expenses.FindAsync([id]), item => item.Id, (item, id) => item.Id = id, item => item.ScenarioId, (item, scenarioId) => item.ScenarioId = scenarioId);
MapCollection<InvestmentPlan>(api, "investments", (db, id) => db.Investments.FindAsync([id]), item => item.Id, (item, id) => item.Id = id, item => item.ScenarioId, (item, scenarioId) => item.ScenarioId = scenarioId);
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

api.MapGet("/export", async (LifeLedgerDbContext db, CancellationToken ct) =>
{
    var profile = await db.Profiles.AsNoTracking().Include(x => x.Careers).OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct);
    if (profile is null) return Results.NotFound();
    var scenarios = await db.Scenarios.AsNoTracking().Where(x => x.ProfileId == profile.Id).AsSplitQuery()
        .Include(x => x.Assumptions).Include(x => x.Incomes).Include(x => x.Assets).Include(x => x.Liabilities).Include(x => x.Expenses).Include(x => x.Investments).Include(x => x.Events).ToListAsync(ct);
    return Results.Ok(new LifeLedgerExport(1, DateTimeOffset.UtcNow, profile, scenarios));
});

api.MapPost("/import", async (ImportRequest request, LifeLedgerDbContext db, CancellationToken ct) =>
{
    if (request.Document.SchemaVersion != 1) return Results.BadRequest(new { message = "Unsupported export schema." });
    // Replacement is explicit because imports can otherwise coexist with existing local data.
    if (request.ReplaceExisting)
    {
        await db.Scenarios.ExecuteDeleteAsync(ct);
        await db.Profiles.ExecuteDeleteAsync(ct);
    }
    var profile = request.Document.Profile;
    profile.Id = Guid.NewGuid();
    foreach (var career in profile.Careers) { career.Id = Guid.NewGuid(); career.ProfileId = profile.Id; career.Profile = null; }
    db.Profiles.Add(profile);
    foreach (var scenario in request.Document.Scenarios)
    {
        ResetScenarioIds(scenario, profile.Id);
        db.Scenarios.Add(scenario);
    }
    await db.SaveChangesAsync(ct);
    return Results.Created("/api/profiles", new { profile.Id });
});

app.MapFallbackToFile("index.html");
app.Run();

/// <summary>Creates an empty baseline for a new scenario owned by the supplied profile.</summary>
static FinancialScenario NewScenario(Profile profile, CreateScenarioRequest request) => new()
{
    ProfileId = profile.Id, Name = request.Name.Trim(), Description = request.Description?.Trim() ?? string.Empty,
    StartsOn = DateOnly.FromDateTime(DateTime.UtcNow), Assumptions = new SimulationAssumptions()
};

/// <summary>Copies editable financial entries from a parent scenario while creating new entity identities.</summary>
static FinancialScenario CloneScenario(FinancialScenario parent, CreateScenarioRequest request) => new()
{
    ProfileId = parent.ProfileId, ParentScenarioId = parent.Id, Name = request.Name.Trim(), Description = request.Description?.Trim() ?? parent.Description,
    StartsOn = parent.StartsOn,
    Assumptions = new SimulationAssumptions { InflationRate = parent.Assumptions.InflationRate, SalaryGrowthRate = parent.Assumptions.SalaryGrowthRate, SafeWithdrawalRate = parent.Assumptions.SafeWithdrawalRate, RetirementAge = parent.Assumptions.RetirementAge, MonteCarloRuns = parent.Assumptions.MonteCarloRuns, DefaultReturnVolatility = parent.Assumptions.DefaultReturnVolatility },
    Incomes = parent.Incomes.Select(x => new IncomeStream { Name = x.Name, Kind = x.Kind, MonthlyAmount = x.MonthlyAmount, AnnualGrowthRate = x.AnnualGrowthRate, StartsOn = x.StartsOn, EndsOn = x.EndsOn, IsTaxable = x.IsTaxable, TaxRate = x.TaxRate, TaxCountryCode = x.TaxCountryCode, Currency = x.Currency }).ToList(),
    Assets = parent.Assets.Select(x => new Asset { Name = x.Name, Kind = x.Kind, CurrentValue = x.CurrentValue, ExpectedAnnualReturn = x.ExpectedAnnualReturn, Volatility = x.Volatility, IsLiquid = x.IsLiquid, Ticker = x.Ticker, Quantity = x.Quantity, CapitalGainsTaxRate = x.CapitalGainsTaxRate, CapitalGainsTaxCountryCode = x.CapitalGainsTaxCountryCode, Currency = x.Currency }).ToList(),
    Liabilities = parent.Liabilities.Select(x => new Liability { Name = x.Name, Kind = x.Kind, OutstandingBalance = x.OutstandingBalance, InterestRate = x.InterestRate, MonthlyPayment = x.MonthlyPayment, PaidOffOn = x.PaidOffOn, Currency = x.Currency }).ToList(),
    Expenses = parent.Expenses.Select(x => new Expense { Name = x.Name, Kind = x.Kind, Frequency = x.Frequency, MonthlyAmount = x.MonthlyAmount, IndexedToInflation = x.IndexedToInflation, StartsOn = x.StartsOn, EndsOn = x.EndsOn, Currency = x.Currency }).ToList(),
    Investments = parent.Investments.Select(x => new InvestmentPlan { Name = x.Name, MonthlyContribution = x.MonthlyContribution, ExpectedAnnualReturn = x.ExpectedAnnualReturn, StartsOn = x.StartsOn, EndsOn = x.EndsOn }).ToList(),
    Events = parent.Events.Select(x => new ScenarioEvent { Name = x.Name, Kind = x.Kind, HappensOn = x.HappensOn, RecurrenceFrequency = x.RecurrenceFrequency, RecurrenceEndsOn = x.RecurrenceEndsOn, OneOffCashImpact = x.OneOffCashImpact, MonthlyCashImpact = x.MonthlyCashImpact, DurationMonths = x.DurationMonths, Notes = x.Notes }).ToList()
};

/// <summary>Assigns new local identities to imported data so it cannot overwrite existing records.</summary>
static void ResetScenarioIds(FinancialScenario scenario, Guid profileId)
{
    scenario.Id = Guid.NewGuid(); scenario.ProfileId = profileId; scenario.Profile = null; scenario.ParentScenarioId = null;
    scenario.Assumptions.Id = Guid.NewGuid(); scenario.Assumptions.ScenarioId = scenario.Id; scenario.Assumptions.Scenario = null;
    foreach (var item in scenario.Incomes) { item.Id = Guid.NewGuid(); item.ScenarioId = scenario.Id; item.Scenario = null; }
    foreach (var item in scenario.Assets) { item.Id = Guid.NewGuid(); item.ScenarioId = scenario.Id; item.Scenario = null; }
    foreach (var item in scenario.Liabilities) { item.Id = Guid.NewGuid(); item.ScenarioId = scenario.Id; item.Scenario = null; }
    foreach (var item in scenario.Expenses) { item.Id = Guid.NewGuid(); item.ScenarioId = scenario.Id; item.Scenario = null; }
    foreach (var item in scenario.Investments) { item.Id = Guid.NewGuid(); item.ScenarioId = scenario.Id; item.Scenario = null; }
    foreach (var item in scenario.Events) { item.Id = Guid.NewGuid(); item.ScenarioId = scenario.Id; item.Scenario = null; }
}

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
