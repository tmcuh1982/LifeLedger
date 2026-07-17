# ADR 0005 — Séparer le type financier et la catégorie personnalisée d’un actif

- Statut : accepté
- Date : 2026-07-17

## Contexte

Les valeurs intégrées de `AssetKind` pilotent des règles financières : le cash impose par exemple un rendement et une volatilité nuls, tandis que les ETF et actions peuvent utiliser un ticker. Les exposer directement comme catégories visibles empêche une traduction correcte et ne permet pas à une personne de classer son patrimoine selon ses besoins.

## Décision

`AssetKind` reste le type technique stable utilisé par les calculs. Une colonne nullable `Asset.CustomCategory`, limitée à 80 caractères, contient le classement personnel facultatif.

Les catégories intégrées sont traduites côté client sans modifier leur valeur persistée. La liste personnelle est conservée en JSON sous la clé `asset-categories` de `ApplicationSettings`. Le service `IAssetCategoryService` réunit cette liste et les catégories présentes sur les actifs, afin qu’une restauration de sauvegarde puisse reconstruire le catalogue.

Un renommage met à jour la liste et tous les actifs associés dans la même unité de travail. La suppression d’une catégorie encore utilisée est refusée pour éviter une perte silencieuse de classement.

## Conséquences

- La migration EF `AddCustomAssetCategories` est additive et ne transforme pas les actifs existants.
- Les sauvegardes JSON transportent la catégorie avec chaque actif grâce à la propriété nullable ajoutée au modèle.
- Une catégorie personnalisée utilise le type technique `Other`; elle n’invente donc aucune règle de rendement, de marché ou de liquidité.
- De nouveaux types techniques nécessiteront toujours une évolution explicite du domaine, indépendamment des catégories d’affichage.
