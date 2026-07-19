# ADR 0008: Seasonal income and asset-linked cash flows

## Status

Accepted

## Context

Short-term rentals are often known as one annual gross total but paid unevenly across the year. Their recurring charges and occasional maintenance also belong to a specific property. Treating every rental as a fixed monthly amount hides seasonality and prevents future per-property profitability reporting.

## Decision

An income stream keeps backward-compatible monthly input and can alternatively use an annual total. Annual totals are either spread equally or allocated to calendar months through typed `IncomeMonthlyAllocation` rows. The projection engine delegates timing and growth to `IIncomeScheduleService`; imperfect seasonal percentages are normalised so they cannot alter the declared annual total.

Income and expense records may optionally reference an asset in the same scenario. Costs remain independent expense records, so monthly apartment charges, one-off maintenance, and other costs can use the existing recurrence and inflation rules without inventing mandatory fields on the property.

Portable export schema version 4 includes these schedules and links. Business-data version 3 initialises an annual reference amount for older monthly income records.

## Consequences

- Users can start with only an annual rental total and add seasonality later.
- Monthly charges and optional maintenance are included in the scenario cash flow and remain attributable to the apartment.
- Asset deletion sets links to null instead of deleting financial history.
- A future property profitability view can aggregate linked income, tax, expenses, financing, and valuation without changing the core records.
