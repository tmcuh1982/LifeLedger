# LifeLedger domain context

## Product purpose

LifeLedger is a local-first, self-hostable personal financial life simulator. It projects a person's financial life across scenarios; it is not a trading platform or tax adviser.

## Ubiquitous language

- **Profile**: a person or household, including home country, base currency, family details and careers.
- **Scenario**: an independent version of a profile's financial future. One scenario may be the baseline; other scenarios model alternatives without changing it.
- **Income stream**: a recurring source of gross income, with dates, currency, growth and optional tax assumptions.
- **Asset**: something owned. Assets may be liquid, may have an expected return and volatility, and can optionally track a public ticker and quantity.
- **Liability**: a debt with an outstanding balance, interest rate and payment schedule.
- **Expense**: a planned cost, either exceptional or recurring at a configured frequency. It may be inflation indexed.
- **Investment plan**: a regular contribution added to the portfolio projection.
- **Life event**: a one-time or repeating event that changes cash flow at a chosen date and frequency.
- **Projection**: the calculated annual financial timeline for a scenario.
- **Market-price snapshot**: a locally stored observation of a public ticker's price. It never contains user credentials or portfolio data.
- **Base currency**: the profile currency used to aggregate and present totals. Individual items can use another currency.

## Domain boundaries

- The API persists source data and runs projections. The React client presents and edits that data.
- Currency conversion uses locally cached exchange rates. A refresh is explicit.
- Public ticker refreshes are optional and best effort; an unavailable quote must never prevent the application from working.
- Tax rates are user-entered scenario assumptions. Country-specific tax engines are intentionally out of scope until country plugins are introduced.

## Safety rules

- Never treat projections, market data or tax estimates as financial, tax or investment advice.
- Do not send personal financial data to third parties. Public ticker and currency refreshes may make outbound requests only for the symbol or currency data required.
- Exclude local databases, backups, logs and secrets from Git.
