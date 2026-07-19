# ADR 0006 — Fiches d’actifs hybrides et versionnées

- Statut : accepté
- Date : 2026-07-17

## Contexte

Un actif ne se résume pas à un montant. Un logement a une adresse, une surface, un DPE et un état ; un véhicule a un modèle, un kilométrage et une motorisation ; une montre a une référence et un état. Ces informations devront accueillir de nouvelles fiches personnalisées et, plus tard, des estimations externes comme PriceHub. Les calculs financiers doivent néanmoins rester typés, prévisibles et indépendants de champs JSON variables.

## Décision

LifeLedger adopte un modèle hybride derrière le Module `IAssetDossierService` :

1. les faits financiers communs restent des colonnes typées sur `Asset` : valeur actuelle, prix et frais d’achat, dates et source d’estimation ;
2. `AssetCharacteristicProfile` conserve la clé de définition, sa version et les valeurs validées de la fiche ;
3. `AssetLiabilityLink` relie plusieurs dettes et actifs avec une part d’affectation, sans dupliquer les soldes ;
4. `IAssetProfileCatalog` est la Seam qui fournit des définitions de champs typées et multilingues ; un Adapter contient les fiches Maison, Véhicule et Montre, et un second conserve les fiches personnalisées dans `ApplicationSettings` ;
5. une seule Interface REST crée ou remplace le dossier complet dans une transaction ;
6. les performances dérivées sont calculées par le Module : coût total d’achat, gain, taux de gain, dette liée convertie et valeur après dette.

La somme des parts d’une dette ne peut pas dépasser 100 %. Une dette doit appartenir au même scénario que l’actif. Le moteur de projection ne lit jamais les caractéristiques JSON. Une future intégration PriceHub devra être un Adapter de valorisation et ne pourra modifier une valeur locale qu’après une action ou une configuration explicite de l’utilisateur.

La version de la fiche est distincte de `data-schema-version`. Le schéma d’export passe en version 2 ; les sauvegardes version 1 restent importables.

Le constructeur dans Paramètres manipule des libellés traduisibles et des types fermés. Une modification ne réécrit jamais une définition : elle ajoute une version immuable. Toutes les versions personnalisées sont incluses dans l’export privé, même lorsqu’une version plus récente est la seule proposée pour les nouveaux actifs.

## Conséquences

- Les calculs conservent une forte Locality autour des données financières typées.
- De nouvelles fiches peuvent être ajoutées sans ajouter une colonne pour chaque caractéristique.
- Une évolution de définition doit fournir une migration de valeurs entre deux versions de fiche.
- Le clonage de scénario et l’import doivent remapper les identifiants des liens dette–actif.
- Une fiche personnalisée utilisée par un actif ne peut pas être supprimée ; elle peut continuer à évoluer par ajout de versions.
