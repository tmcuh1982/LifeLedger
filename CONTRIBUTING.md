# Contributing to LifeLedger

Thank you for helping build a trustworthy, open personal-finance simulator.

## Principles

- Privacy is a feature. Do not add telemetry, advertising identifiers, hidden remote calls or data collection.
- Make assumptions explicit. Financial formulas must name their inputs and have tests before they become defaults.
- Preserve modular boundaries. Keep calculations in services, persistence in `Data`, and HTTP/UI concerns at the edges.
- Prefer understandable outcomes to artificial precision. State uncertainty in user-facing copy.
- Keep the default installation self-hosted and free of required external services.

## Development setup

Install .NET 9 SDK and Node.js 20+, then follow the local run instructions in [README.md](README.md). The API starts with non-personal sample data; do not commit database files or private exports.

Before opening a pull request:

```bash
dotnet build LifeLedger.sln
cd src/lifeledger-web && npm install && npm run lint && npm run build
```

## Pull requests

1. Keep each PR focused on one behavior or module.
2. Explain the user outcome, assumptions and verification in the PR description.
3. Add or update tests for changes to calculations, API behavior or data migrations.
4. Update README, architecture notes or roadmap when the public behavior changes.
5. Do not include real financial data, screenshots containing it, secrets or generated database files.

## Financial-model changes

Document the formula, source or rationale, valid jurisdictions and known limitations. A country or tax-specific rule belongs in a plugin unless it is broadly applicable core behavior. Do not present outputs as advice or guarantees.

## Code style

- C#: nullable enabled, strong types, small services and asynchronous I/O.
- TypeScript: strict mode, accessible semantics and responsive layouts.
- UI: use the defined glass design tokens and 8px spacing rhythm; retain contrast and keyboard access.
- Commits: short imperative subject lines, for example `Add retirement income projection`.

## Reporting security issues

Do not open a public issue with sensitive personal finance data or an exploit. Follow the private reporting process in [SECURITY.md](SECURITY.md) and use synthetic data for every reproduction.
