# 0003 — Local net-worth history

- Status: accepted
- Date: 2026-07-16

## Context

The projection timeline describes a possible future, but it does not show how the user's actual patrimony changes over time. This historical information must remain local and must not require a bank or broker connection.

## Decision

At application startup, LifeLedger captures assets minus liabilities from each baseline scenario, converts the result into the profile's base currency using the local currency cache, and stores one `NetWorthSnapshot`. The history is displayed on the dashboard and can be cleared independently in Settings.

## Consequences

- The history grows only when the local server starts; no background service or external account is required.
- A missing local exchange rate skips that capture and logs a warning instead of preventing startup.
- Resetting history never changes current financial entries, quotes, or scenarios.
