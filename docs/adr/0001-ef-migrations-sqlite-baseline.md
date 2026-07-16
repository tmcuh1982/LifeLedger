# 0001 — EF Core migrations and SQLite baseline

- Status: accepted
- Date: 2026-07-16

## Context

Early local installations created their SQLite schema with `EnsureCreated` and incremental SQLite-specific alterations. That approach cannot reliably evolve a schema or provide a repeatable deployment history.

## Decision

LifeLedger uses EF Core migrations for all new databases. At startup, a legacy SQLite database is detected by the presence of the `Profiles` table and the absence of EF's `__EFMigrationsHistory` table. It is then marked as having applied the initial migration without changing financial data. Future migrations are applied normally.

## Consequences

- New installations and automated tests have a reproducible schema.
- Existing local SQLite data remains usable when upgrading to this version.
- The initial migration is a baseline: do not modify it after publication; create a new migration for each schema change.
- PostgreSQL remains supported at the application level. Provider-specific migration sets will be introduced before PostgreSQL is promoted as a production storage path.
