# LifeLedger domain context

## Product purpose

LifeLedger is a local-first, self-hostable personal financial life simulator. It projects a person's financial life across scenarios; it is not a trading platform or tax adviser.

## Ubiquitous language

- **Profile**: a person or household, including home country, base currency, family details and careers.
- **Scenario**: an independent version of a profile's financial future. One scenario may be the baseline; other scenarios model alternatives without changing it.
- **Income stream**: a source of gross income entered monthly or as an annual total, with optional seasonal calendar-month allocation, dates, currency, growth, tax assumptions and a link to the asset that generates it.
- **Asset**: something owned. Assets may be liquid, may have an expected return and volatility, and can optionally track a public ticker and quantity.
- **Imported broker asset**: an Asset owned by a read-only broker connector and identified by its provider plus stable external identifier. It is updated idempotently by that connector and never matched to a manual asset by name or ticker.
- **Allocation category**: a user-defined Asset category used to aggregate included holdings across brokers; built-in Asset kinds are only the fallback category.
- **Allocation strategy**: a dated, scenario-owned version of target category weights and tolerances. It records an investor’s thesis and is assessed against, but never automatically changes, the portfolio.
- **Asset dossier**: the complete editable record of an asset: typed financial facts, an optional versioned characteristic profile and allocated liabilities.
- **Asset characteristic profile**: a schema-driven sheet such as Home, Vehicle or Watch. Its version and values are stored with the asset, but its fields never enter financial calculations unless a future explicit mapping says so.
- **Custom asset profile**: a locally created characteristic profile. Editing it creates an immutable new version; older versions remain available to interpret existing assets and backups.
- **Acquisition basis**: purchase price plus acquisition costs, expressed in the asset currency.
- **Asset-liability allocation**: the fraction of a liability that finances one asset. Allocations of the same liability cannot exceed 100% across assets.
- **Net equity**: current asset value minus linked outstanding debt after currency conversion.
- **Asset valuation snapshot**: one locally stored total value for an asset on a calendar date, with its currency and source. A same-day correction replaces the existing point.
- **Liability**: a debt with an outstanding balance, interest rate and payment schedule.
- **Expense**: a planned cost, either exceptional or recurring at a configured frequency. It may be inflation indexed, linked to the asset that causes it and contain dated future amount steps.
- **Expense amount step**: a user-entered amount that replaces a recurring expense amount from a chosen date. When inflation indexing is enabled, inflation restarts from this explicit nominal amount instead of being counted twice.
- **Investment plan**: a regular contribution added to the portfolio projection.
- **Life event**: a one-time or repeating event that changes cash flow at a chosen date and frequency. Its impact has an explicit currency; a vehicle purchase can repeat on the five-year schedule and is stored as a negative cash impact.
- **Projection**: the calculated annual financial timeline for a scenario.
- **Risk signal**: a language-neutral warning code with an optional numeric value. The API calculates it and the client turns it into a complete sentence in the selected language.
- **Projected wealth component**: one category-level contribution to net worth at a timeline point: an owned asset category, future investment balance, projected cash, planned-expense reserve or negative outstanding debt.
- **Planned asset sale**: an explicit future disposal of one asset using either its value projected to the sale date or a manual gross price, less selling costs, capital-gains tax and optionally the outstanding debt allocated to that asset. The remaining proceeds are transferred to cash, another asset or an investment plan.
- **Demo dataset**: a versioned, deterministic and entirely fictional local ledger with stable identifiers, dates and amounts. Restoring it replaces user-owned financial data so CRUD tests, visual reviews and screenshots always start from the same state.
- **Passive cash income**: after-tax rent, dividends and royalties actually received. Unrealised asset appreciation is excluded and reported separately as expected portfolio growth.
- **Market-price snapshot**: a locally stored observation of a public ticker's price. It never contains user credentials or portfolio data.
- **Base currency**: the profile currency used to aggregate and present totals. Individual items can use another currency.
- **Bank account**: a locally registered account in one mandatory ISO currency, optionally linked to the Cash asset it represents. Only a masked identifier and a one-way matching hash are stored.
- **Bank statement import**: an auditable record of a reviewed PDF or delimited-file import. It stores the source filename, template version and fingerprint, but never the original document bytes.
- **Bank transaction**: a historical operation observed on a statement. It remains separate from a planned Expense and never changes a Projection automatically. Its classification, category, analysis exclusion and links remain editable after import.
- **Transaction classification**: the user-reviewed meaning of an observed operation: expense, income, transfer, investment, asset expense, ignored or still uncategorised.
- **Spending-analysis exclusion**: an independent user choice that keeps an observed expense in history while omitting it from recurring monthly-spending estimates.
- **Observed monthly spending average**: the net total of one bank category and currency divided by every distinct calendar month covered by its imported statements, including covered months with no operation in that category.

## Domain boundaries

- The API persists source data and runs projections. The React client presents and edits that data.
- Currency conversion uses locally cached exchange rates. A refresh is explicit.
- Public ticker refreshes are optional and best effort; an unavailable quote must never prevent the application from working.
- Tax rates are user-entered scenario assumptions. Country-specific tax engines are intentionally out of scope until country plugins are introduced.
- The asset-dossier Module is accessed through one transactional Interface. Profile JSON is a persistence Implementation detail and is never read by the projection engine.
- Built-in characteristic profiles use their own versions independently of the application data-schema version.
- Custom profile definitions and all historical versions are included in private exports and restored with their assets.
- Manual estimates and optional market refreshes write through the same asset-valuation history Interface; unit market quotes remain a separate technical record.
- Income timing is calculated through `IIncomeScheduleService`; seasonal shares affect cash-flow timing but are normalised so they never change the declared annual total.
- Recurring expense timing is calculated through `IExpenseScheduleService`; explicit amount steps take priority and become the new inflation anchor on their effective date.
- Deterministic asset growth is current-value weighted and compounded monthly after the declared capital-gains tax assumption. A scenario with no assets has no invented return, and unfunded cash-flow deficits remain negative so solvency cannot stop artificially at zero.
- Projection warnings cross the API boundary as structured risk signals, never as server-authored display sentences. The React client owns their wording and number formatting in every supported language.
- Owned assets compound independently from their own expected return, volatility and capital-gains assumption before being grouped for display. The projected total reconciles exactly to the sum of asset categories, investment-plan balances, projected cash, planned-expense reserves and negative debt. A cash deficit liquidates cash assets first, then liquid investments and finally non-liquid assets; the transfer changes the split but not total wealth.
- Asset appreciation increases net worth but is not passive cash income. A projected asset value represents value available through a future sale, while an actual sale remains an explicit future transaction rather than an automatic liquidation.
- A planned sale removes the source asset only in its scheduled month. Its gross proceeds, fees, estimated capital-gains tax and linked-debt repayment are calculated in the profile base currency and exposed separately in the projection timeline. A sale is a balance-sheet transfer except for a manual price difference, fees and tax.
- Cash keeps zero volatility in Monte Carlo. Other assets with no configured volatility use the scenario fallback. The illustrative historical model begins at the first element of its documented twelve-year cycle.
- Life-event impacts are converted from their own currency into the profile base currency before projection. Existing events created before explicit event currencies are migrated to the profile base currency to preserve their former meaning.
- The bank-statement import Module owns preview, parsing, privacy safeguards, deduplication and commit. Versioned JSON definitions configure delimited Adapters; complex PDF layouts use code Adapters behind the same Interface.
- Imported operations may link to an Asset or Investment plan for analysis, but turning observed history into a future planning assumption requires a separate explicit action.
- An observed monthly average stays separated by currency and becomes a recurring, inflation-indexed Expense only when the user explicitly promotes it. Promoting the same category and currency again updates that linked assumption without duplicating it.
- Asset-related work is excluded from monthly living-cost estimates. Its paid cost never changes an asset value automatically; the user may explicitly record a new absolute valuation, which also creates a valuation-history point.
- Demo restoration is an explicit destructive action confirmed by the client. It preserves technical schema and exchange-rate settings, never calls an external service, and reconstructs the canonical dataset rather than trying to undo individual edits.

## Safety rules

- Never treat projections, market data or tax estimates as financial, tax or investment advice.
- Do not send personal financial data to third parties. Public ticker and currency refreshes may make outbound requests only for the symbol or currency data required.
- Exclude local databases, backups, logs and secrets from Git.
