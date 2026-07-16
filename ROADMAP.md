# LifeLedger roadmap

The roadmap is an intention, not a promise. Privacy, explainability and data ownership are release gates for every version.

## v0.1 — MVP

- [x] Local SQLite-backed profile, multi-country career and scenario model
- [x] Income, assets, liabilities, expenses, investments and life events
- [x] Baseline dashboard with net worth, allocation, cash flow and warnings
- [x] Deterministic long-term projection and public-pension estimates
- [x] React responsive interface and JSON backup/restore
- [ ] Full detail editing for every entity from the UI
- [ ] Calculation unit tests and seeded example fixtures
- [x] Persisted English, French, Polish, German and Dutch locale selection for global UI controls
- [ ] Complete translated content and locale-aware financial-date formatting

## v0.5 — Simulation

- [x] Monte Carlo and historical-cycle simulation foundations
- [ ] Simulation controls, percentile bands, heatmaps and comparison reports
- [ ] Scenario diff and side-by-side dashboard
- [ ] Real currency conversion with opt-in rate providers or manual rate tables
- [ ] Import/export CSV and Excel workbook export
- [ ] Printable PDF reports
- [ ] User-selectable country inflation history and documented data sources
- [ ] Accessibility audit, dark/light theme preference and complete translations

## v1.0 — Stable

- [ ] EF Core migrations, safe upgrade path and backup assistant
- [ ] Comprehensive test suite and formula verification coverage
- [ ] Stable plugin SDK with version compatibility policy
- [ ] Signed release artifacts and Docker image documentation
- [ ] Production CORS/authentication deployment guide
- [ ] Country plugin directory and maintainership rules
- [ ] Performance and privacy review for large self-hosted ledgers

## Beyond v1.0

- AI financial advisor that is opt-in, explainable and works with local/on-device models where possible
- Country plugins for public pension, social security and benefits systems
- Tax plugins for transparent jurisdiction-specific calculations
- Open Banking / bank import connectors, always opt-in and self-hosted
- Broker import connectors
- Robust CSV import/export mappings
- PDF reports and Excel export templates
- Household planning, shared scenario permissions and encrypted backups
- More detailed historical stress tests, goal planning and retirement withdrawal strategies
