# IBKR Flex portfolio synchronization / Synchronisation de portefeuille IBKR Flex

## English

LifeLedger connects to Interactive Brokers through the read-only **Flex Web Service**. It does not use TWS, IB Gateway, Client Portal Gateway, or an order endpoint.

### Set up in IBKR

1. In IBKR Client Portal, open **Reporting → Flex Queries**.
2. Create an **Activity Flex Query** that includes the **Open Positions** section. Include at least `conid`, symbol or description, asset category, position, position value and currency.
3. Open **Flex Web Service Configuration**, enable the service, and generate a token. Restrict it to the LifeLedger host IP when practical.
4. Copy the Activity Query ID and token into LifeLedger's `PUT /api/scenarios/{scenarioId}/integrations/ibkr-flex` endpoint.

```json
{
  "accessToken": "IBKR_FLEX_TOKEN",
  "activityQueryId": 123456
}
```

Call `POST /api/scenarios/{scenarioId}/integrations/ibkr-flex/sync` to run a manual synchronization. A safe `GET` on the configuration endpoint shows only whether it is configured and its query ID; it never returns the token.

### Behaviour and limits

- IBKR generates the report before LifeLedger retrieves it. The connector retries a report that IBKR still marks as in progress.
- The connector creates or updates only assets tagged with `ExternalProvider = IBKR Flex` and the IBKR contract identifier. It never changes manual assets.
- Each imported holding writes a local valuation point tagged `IBKR Flex` for the day of synchronization.
- It imports open positions only. It does not import cash, transactions, tax lots, orders, dividends, or live prices yet.
- Activity Flex reports update daily after IBKR's reporting close; this is not a real-time portfolio feed. Use one daily synchronization in normal operation.
- The IBKR access token is protected with installation-local ASP.NET Core Data Protection keys stored beside the local database. It is excluded from data export, never returned by the API, and removed when all LifeLedger data is deleted. Protect the database and key directory together when making backups.

The implementation is intentionally extensible: native `FlexService` owns protected local connector settings, scenario loading, and imported-asset identity. `IbkrFlexService` is its IBKR-specific subclass. A future broker connector can inherit from `FlexService` while retaining its own authentication and report parsing.

## Français

LifeLedger se connecte à Interactive Brokers avec le **Flex Web Service**, exclusivement en lecture seule. Cette intégration n’utilise ni TWS, ni IB Gateway, ni Client Portal Gateway, ni point d’entrée permettant de passer des ordres.

### Configuration dans IBKR

1. Dans le Client Portal IBKR, ouvrez **Reporting → Flex Queries**.
2. Créez une **Activity Flex Query** incluant la section **Open Positions**. Ajoutez au minimum `conid`, le symbole ou la description, la catégorie d’actif, la quantité, la valeur de position et la devise.
3. Ouvrez **Flex Web Service Configuration**, activez le service puis générez un jeton. Restreignez-le à l’adresse IP de l’hôte LifeLedger lorsque c’est possible.
4. Enregistrez l’identifiant de la requête Activity et le jeton avec `PUT /api/scenarios/{scenarioId}/integrations/ibkr-flex`.

Lancez une synchronisation manuelle avec `POST /api/scenarios/{scenarioId}/integrations/ibkr-flex/sync`. Le `GET` de configuration n’expose que l’état et l’identifiant de requête, jamais le jeton.

### Comportement et limites

- IBKR doit générer le rapport avant que LifeLedger puisse le télécharger ; le connecteur réessaie tant qu’IBKR le signale en cours de génération.
- Seuls les actifs portant `ExternalProvider = IBKR Flex` et l’identifiant de contrat IBKR sont créés ou mis à jour. Les actifs manuels ne sont jamais modifiés.
- Chaque position importée ajoute ou corrige le point de valorisation local du jour, avec la source `IBKR Flex`.
- La première version importe uniquement les positions ouvertes. Elle n’importe pas encore le cash, les transactions, lots fiscaux, ordres, dividendes ni cours temps réel.
- Les rapports Activity sont mis à jour quotidiennement après la clôture de reporting d’IBKR : ce n’est pas un flux temps réel. Une synchronisation quotidienne suffit normalement.
- Le jeton est chiffré avec les clés ASP.NET Core Data Protection locales, stockées à côté de la base de données. Il est exclu des exports, jamais renvoyé par l’API et supprimé avec toutes les données LifeLedger. Les sauvegardes doivent conserver la base et le dossier de clés ensemble.

L’architecture est extensible : le service natif `FlexService` gère les paramètres locaux protégés, le chargement du scénario et l’identité des actifs importés. `IbkrFlexService` en est la spécialisation IBKR. Un futur connecteur de courtier pourra hériter de `FlexService` avec son propre mécanisme d’authentification et son propre parseur de rapport.
