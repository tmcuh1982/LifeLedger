# 0002 — Atomic data imports

- Status: accepted
- Date: 2026-07-16

## Context

Restoring a backup can replace the local financial plan. A sequence of independent delete and insert operations could leave the database empty or incomplete when a document is invalid or a write fails.

## Decision

All imports run through `IDataImportService`. The import document is validated before any destructive operation. If replacement is requested, dependent records, scenarios and profiles are deleted and the new profile graph is saved inside one database transaction.

## Consequences

- Invalid backups return field-level validation errors while preserving the current plan.
- A failed replacement is rolled back by the database.
- Import identity reassignment is isolated from HTTP routing and can be tested independently.
