# Faire évoluer les données déjà encodées

LifeLedger distingue deux mécanismes complémentaires :

- les migrations EF Core modifient la structure de la base (table, colonne, index) ;
- les migrations métier modifient les valeurs ou le sens des données déjà enregistrées.

La version métier est enregistrée localement dans `ApplicationSettings`, sous la clé `data-schema-version`. Elle commence à `1`.

## Ajouter une version B après une version A

1. Ajouter la migration EF Core si la structure de la base doit changer.
2. Créer une classe qui implémente `IDataSchemaMigration` avec `FromVersion = 1` et `ToVersion = 2`.
3. Dans `ApplyAsync`, convertir les données existantes avec des requêtes explicites et commentées.
4. Enregistrer cette classe dans l'injection de dépendances.
5. Passer `DataSchemaMigrationService.LatestVersion` à `2`.
6. Ajouter un test partant d'une base de version `1`, puis vérifier les données obtenues en version `2`.

Au démarrage, l'application applique les étapes `1 → 2 → 3` dans l'ordre. Chaque étape est transactionnelle : si une conversion échoue, la version et les données de cette étape sont annulées.

## Règles importantes

- Ne jamais modifier une migration historique : créer une nouvelle étape.
- Ne jamais supprimer ou écraser une valeur sans stratégie de conversion documentée.
- Une base issue d'une version plus récente que l'application doit rester intacte : LifeLedger refuse de démarrer jusqu'à la mise à jour de l'application.
- Tester la migration avec une copie anonyme de données réelles avant toute livraison.
