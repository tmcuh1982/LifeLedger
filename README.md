# LifeLedger

**LifeLedger is a private, local-first simulator for your entire financial life.** It models the question: _“If I continue living this way, what will my financial life look like in 5, 10, 20, 30 and 50 years?”_

It is not a retirement calculator. It brings income, assets, liabilities, expenses, investments, life events, inflation, retirement income and uncertainty into one transparent model.

> LifeLedger is educational modelling software, not financial, investment, tax or legal advice. Its estimates are only as good as the assumptions you provide.

## Why LifeLedger

- Private by default: no accounts, telemetry, advertising or third-party API calls.
- Local first: SQLite is the default data store; PostgreSQL is supported for self-hosted deployments.
- Self-hostable: run the API and static client on your own computer, server or container.
- Explainable: every number comes from declared data and configurable assumptions.
- International: multiple career periods and public pension estimates can be entered by country.
- Multilingual foundation: persisted English, French, Polish, German and Dutch locale selection; content is structured for progressive translation.
- Extensible: compiled plugins can add country, tax or projection rules without changing the core.
- MIT licensed and built in the open.

## What it includes today

| Area | Included |
| --- | --- |
| Profile | Age, home country, base currency, expected lifespan, partner, children, multi-country careers |
| Income | Salary, freelance, rental, dividends, pensions, royalties and other recurring income |
| Balance sheet | Cash, ETFs, stock, crypto, real estate, business, collectibles; mortgages, loans and leasing |
| Spending and investment | Recurring/exceptional expenses, inflation indexing and monthly investment plans |
| Scenarios | Unlimited scenario branches, inherited inputs and life events |
| Simulation | Deterministic projection, historical return cycle and seeded Monte Carlo runs |
| Dashboard | Net worth, cash flow, allocation, passive income, retirement income, FI date, purchasing power, probability and warnings |
| Data ownership | JSON export/import and an ordinary local SQLite file |

## Architecture

See [the architecture guide](docs/ARCHITECTURE.md) for diagrams and calculation flow.

```text
React + TypeScript + Tailwind + Recharts
                  │ REST /api
ASP.NET Core 9 minimal API
                  │
Projection engine + plugin modifiers
                  │
Entity Framework Core → SQLite (default) / PostgreSQL
```

## Run locally

Prerequisites: [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) and Node.js 20+.

```bash
# Terminal 1: API, database and demo ledger
dotnet restore
dotnet run --project src/LifeLedger.Api --urls http://localhost:5078

# Terminal 2: React development server
cd src/lifeledger-web
npm install
npm run dev
```

Open `http://localhost:5173`. The first launch creates `data/lifeledger.db` and a non-personal sample plan so the dashboard is immediately useful. Set `SeedDemoData` to `false` in `src/LifeLedger.Api/appsettings.json` for an empty installation.

### Run the compiled app as one service

```bash
cd src/lifeledger-web
npm install
npm run build
cd ../..
dotnet run --project src/LifeLedger.Api --urls http://localhost:5078
```

The frontend build is copied to `src/LifeLedger.Api/wwwroot`; browsing `http://localhost:5078` then serves both the client and API. Node.js is only needed to build the frontend—there is no external service required at runtime.

### Use PostgreSQL

Set the provider and connection string with environment variables:

```bash
export Database__Provider=PostgreSql
export ConnectionStrings__LifeLedger='Host=localhost;Database=lifeledger;Username=lifeledger;Password=change-me'
dotnet run --project src/LifeLedger.Api
```

The default database provider is SQLite. The EF Core model deliberately uses portable types and relationships so either provider can be used.

### Docker / self-hosting

```bash
docker compose up --build
```

The Compose file persists SQLite data in a named local volume and exposes the application at `http://localhost:8080`. For PostgreSQL, use the environment variables above and supply your own managed or local instance; no cloud account is inherent to LifeLedger.

## REST API

The API is intentionally simple and local. Common endpoints:

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/api/health` | Local service and loaded plugins |
| `GET` | `/api/profiles` | Profiles and career history |
| `GET/POST` | `/api/scenarios` | List or create scenario branches |
| `GET` | `/api/scenarios/{id}/data` | Income, assets, liabilities, expenses, investments and events |
| `POST` | `/api/scenarios/{id}/simulate` | Run `Deterministic`, `MonteCarlo` or `Historical` simulation |
| `GET` | `/api/scenarios/{id}/dashboard` | Dashboard-ready financial indicators |
| `GET` / `POST` | `/api/export` / `/api/import` | Portable JSON backup and restore |

`POST /api/scenarios/{id}/simulate` accepts:

```json
{ "mode": "MonteCarlo", "years": 50, "runs": 1000 }
```

The complete endpoint behavior and financial rules are documented in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Plugin development

Place a compiled `.dll` in the configured `plugins` directory (default: `src/LifeLedger.Api/plugins`). Implement `ILifeLedgerPlugin` and register one or more `IProjectionModifier` instances. A modifier receives one annual `ProjectionContext` and can apply income, expense or net-worth adjustments.

```csharp
public sealed class LocalTaxPlugin : ILifeLedgerPlugin
{
    public string Id => "org.example.local-tax";
    public string DisplayName => "Example local tax";
    public Version Version => new(1, 0, 0);

    public void Configure(PluginContext context) =>
        context.AddProjectionModifier(new LocalTaxModifier());
}
```

Plugins run in-process and therefore must be trusted code. Treat the plugin directory like application source, not user-upload storage.

## Financial model notes

- All displayed values are currently treated as the profile base currency. Currency conversion is intentionally not guessed; model values after converting them yourself or add an exchange-rate plugin.
- Salaries stop at the configured retirement age unless their end date says otherwise. Rental, dividend and royalty streams remain active while their date range is active.
- Public retirement income is the sum of the monthly estimates entered for career periods. Country plugins can replace this with detailed entitlement rules.
- Monte Carlo uses a deterministic seed per run, the portfolio-weighted expected return and volatility; its results are reproducible for the same input.
- Historical mode uses a deliberately transparent representative return/inflation cycle. It is not a source of market data and should be replaced or enhanced by an opted-in data plugin for serious planning.

## Project map

```text
src/LifeLedger.Api/        ASP.NET Core API, EF model, simulation services and plugins
src/lifeledger-web/        React dashboard and planning UI
docs/                      Architecture and product planning
.github/workflows/         Continuous-integration definition
```

## Contributing

Please read [CONTRIBUTING.md](CONTRIBUTING.md). We welcome country modules, tax plugins, tests, accessibility improvements, documentation and thoughtful model review.

## Roadmap

The version plan is in [ROADMAP.md](ROADMAP.md). The near-term focus is reliable editing, simulations and transparent country-specific extensions rather than opaque predictions.

## License

[MIT](LICENSE) © 2026 LifeLedger contributors.
