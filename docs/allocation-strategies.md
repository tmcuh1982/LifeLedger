# Allocation strategies / Stratégies d’allocation

## English

LifeLedger distinguishes the portfolio you currently hold from the portfolio you intend to hold.

- **Observed allocation** groups all assets included in portfolio allocation by their personal asset category, across every broker. For example, ETF World holdings from IBKR, Revolut, Fortis and Boursorama become one `ETF World` slice.
- An asset can remain in net worth while being excluded from the investable allocation—for example, a primary home, a vehicle or an emergency reserve you do not rebalance.
- A **strategy version** belongs to one scenario and has an effective date, optional end date, a target percentage, and a tolerance band for each category.
- Strategy versions cannot overlap. To change an investment thesis, end the current version and create the next dated version. Historical strategies remain intact.

The dashboard calculates each target category's actual percentage and marks it `WithinRange`, `Underweight`, or `Overweight`. Targets may total less than 100% when part of the portfolio is intentionally not prescribed; they may never exceed 100%.

REST endpoints:

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/api/scenarios/{scenarioId}/allocation-strategies` | List strategy versions |
| `POST` | `/api/scenarios/{scenarioId}/allocation-strategies` | Create a dated strategy version |
| `PUT` | `/api/allocation-strategies/{strategyId}` | Replace a version and its targets |
| `DELETE` | `/api/allocation-strategies/{strategyId}` | Delete a version |

Future LLM or market-data integrations must be opt-in and read-only by default. They may create sourced observations or recommendations, but must not edit asset categories, allocation strategies, or portfolio values without an explicit user review and confirmation.

## Français

LifeLedger distingue le portefeuille effectivement détenu du portefeuille que vous souhaitez détenir à long terme.

- L’**allocation observée** regroupe, par catégorie personnelle, les actifs inclus dans l’allocation investissable, tous courtiers confondus. Des ETF World détenus chez IBKR, Revolut, Fortis et Boursorama forment donc une seule part `ETF World`.
- Un actif peut rester dans le patrimoine tout en étant exclu de l’allocation investissable : résidence principale, véhicule ou réserve de sécurité que vous ne souhaitez pas rééquilibrer.
- Une **version de stratégie** appartient à un scénario et contient une date d’effet, une éventuelle date de fin, une cible et une marge de tolérance par catégorie.
- Les versions ne peuvent pas se chevaucher. Pour modifier une thèse d’investissement, clôturez la version actuelle puis créez la suivante avec sa date d’effet. Les stratégies passées restent visibles.

Le tableau de bord calcule la part effective de chaque catégorie et l’indique comme `WithinRange`, `Underweight` ou `Overweight` (respectivement dans la plage, sous-pondérée ou surpondérée). Les cibles peuvent totaliser moins de 100 % lorsqu’une partie du portefeuille n’est volontairement pas prescrite ; elles ne peuvent jamais dépasser 100 %.

Les futures intégrations LLM ou de données de marché devront être activées explicitement et rester en lecture seule par défaut. Elles pourront produire des observations sourcées ou des recommandations, mais ne devront jamais modifier les catégories, stratégies ou valorisations sans revue et confirmation explicites de l’utilisateur.
