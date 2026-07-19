# ADR 0017 — Versioned portfolio allocation strategies / Stratégies d’allocation de portefeuille versionnées

- Status / Statut: accepted / accepté
- Date: 2026-07-18

## Context / Contexte

An allocation by individual holdings obscures the real exposure shared across brokers and cannot express an investor's intended long-term mix. Investment themes and risk views change over time, so a single immutable allocation target would misrepresent prior decisions.

Une allocation par ligne d’actif masque l’exposition réelle répartie entre les courtiers et ne permet pas d’exprimer une répartition cible à long terme. Les thèmes et la vision du risque évoluent dans le temps ; une cible unique et immuable fausserait les décisions passées.

## Decision / Décision

Portfolio allocation groups only assets explicitly included in portfolio allocation, using `CustomCategory` first and the technical asset kind as a fallback. The user may exclude an asset from allocation without removing it from net worth.

`AllocationStrategy` is a dated, scenario-owned strategy version. `AllocationStrategyTarget` stores category, target percentage and symmetric tolerance in percentage points. Versions cannot overlap; targets may sum to less than 100% but never more. The dashboard computes drift without performing rebalancing or changing user data.

L’allocation du portefeuille regroupe uniquement les actifs explicitement inclus dans l’allocation, en utilisant d’abord `CustomCategory` puis le type technique comme repli. L’utilisateur peut exclure un actif de l’allocation sans le retirer du patrimoine net.

`AllocationStrategy` est une version de stratégie datée et rattachée au scénario. `AllocationStrategyTarget` stocke la catégorie, la cible et la tolérance symétrique en points de pourcentage. Les versions ne se chevauchent pas ; les cibles peuvent totaliser moins de 100 %, mais jamais davantage. Le tableau de bord calcule les écarts sans rééquilibrer ni modifier les données utilisateur.

## Consequences / Conséquences

- The same category can aggregate positions from any current or future broker connector.
- New allocation versions preserve the historical thesis instead of overwriting it.
- LLM and external market data can later propose reviewed observations and recommendations; the strategy remains user-owned and changes only through explicit actions.

- Une même catégorie peut agréger les positions de tout connecteur de courtier actuel ou futur.
- Les nouvelles versions préservent la thèse historique au lieu de l’écraser.
- Les LLM et les données de marché externes pourront proposer des observations et recommandations à valider ; la stratégie reste la propriété de l’utilisateur et ne change que par une action explicite.
