# ADR 0016 — Extensible read-only broker Flex connections / Connexions Flex de courtiers extensibles et en lecture seule

- Status / Statut: accepted / accepté
- Date: 2026-07-18

## Context / Contexte

LifeLedger needs to retrieve an Interactive Brokers portfolio without running TWS or IB Gateway and without gaining order authority. Broker report formats and authentication differ, but imported holdings must remain local, auditable and separate from manually maintained assets.

LifeLedger doit récupérer un portefeuille Interactive Brokers sans exécuter TWS ou IB Gateway et sans obtenir le droit de passer des ordres. Les formats de rapport et l’authentification diffèrent selon les courtiers, mais les positions importées doivent rester locales, auditables et séparées des actifs gérés manuellement.

## Decision / Décision

LifeLedger introduces the native abstract `FlexService` connector base. It owns protected per-scenario settings, loading of the scenario aggregate and stable identity rules for imported assets. `IbkrFlexService` inherits from it and implements IBKR's two-step Flex Web Service request, report parsing and Activity-position mapping.

Imported assets receive `ExternalProvider` and `ExternalId`. The unique `(ScenarioId, ExternalProvider, ExternalId)` index makes synchronizations idempotent and prevents an external holding from matching a manual asset by display name or ticker. The `AddExternalAssetIdentity` EF Core migration adds those nullable columns and index.

`FlexService` natively serves as the abstract connector base. It gère les paramètres protégés par scénario, le chargement de l’agrégat de scénario et les règles d’identité stable des actifs importés. `IbkrFlexService` en hérite et implémente la requête Flex Web Service IBKR en deux étapes, l’analyse du rapport et le mapping des positions Activity.

Les actifs importés reçoivent `ExternalProvider` et `ExternalId`. L’index unique `(ScenarioId, ExternalProvider, ExternalId)` rend les synchronisations idempotentes et évite de rapprocher une position externe d’un actif manuel par son nom ou son ticker. La migration EF Core `AddExternalAssetIdentity` ajoute ces colonnes facultatives et cet index.

## Consequences / Conséquences

- The first connector is read-only and imports only open positions; it cannot place, modify or cancel orders.
- A future broker can inherit from `FlexService` without changing existing IBKR behaviour or the local asset model.
- Credentials are not part of portable exports and require the local data-protection key directory to remain available after backup or restore.
- Activity reports are delayed reporting data, not a live market-data integration.

- Le premier connecteur est en lecture seule et importe uniquement les positions ouvertes ; il ne peut passer, modifier ou annuler aucun ordre.
- Un futur courtier peut hériter de `FlexService` sans modifier le comportement IBKR ni le modèle local des actifs.
- Les identifiants ne font pas partie des exports portables et le dossier local de clés de protection doit rester disponible après une sauvegarde ou une restauration.
- Les rapports Activity sont des données de reporting différées, pas une intégration de données de marché temps réel.
