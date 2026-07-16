# ADR 0004 — Versionner les données métier locales

- Statut : accepté
- Date : 2026-07-16

## Contexte

Les migrations Entity Framework font évoluer la structure SQLite ou PostgreSQL : tables, colonnes et index. Elles ne suffisent pas lorsque la signification ou la forme des données financières déjà saisies doit changer. Il faut pouvoir faire évoluer un patrimoine encodé avec une version A vers une version B, sans demander à la personne de tout réencoder.

## Décision

Une table `ApplicationSettings` conserve des métadonnées locales. La clé `data-schema-version` contient la version entière du format de données métier.

Au démarrage, LifeLedger applique dans cet ordre :

1. les migrations EF Core ;
2. les migrations métier `IDataSchemaMigration`, une version à la fois ;
3. les opérations normales de démarrage.

Une migration métier doit aller de `n` vers `n + 1`. Chaque étape s'exécute dans sa propre transaction et ne valide la nouvelle version qu'après la transformation complète. Une base sans marqueur est considérée comme étant au format initial `1`, afin d'adopter sans modification les patrimoines créés avant cette fonctionnalité.

Une version stockée plus récente que l'application, une valeur invalide ou une étape manquante arrête explicitement le démarrage. Il est préférable de ne rien modifier plutôt que risquer de corrompre les données.

## Conséquences

- Toute évolution nécessitant une conversion de données doit inclure une migration EF Core si nécessaire, puis une implémentation `IDataSchemaMigration` et l'incrément de `LatestVersion`.
- Les migrations doivent être idempotentes dans leur état cible et testées sur une copie réaliste d'ancienne base.
- `ApplicationSettings` pourra recevoir d'autres paramètres techniques sans mélanger cette information avec les réglages financiers du profil.
