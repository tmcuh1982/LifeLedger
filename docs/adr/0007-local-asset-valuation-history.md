# ADR 0007 — Historique local des valorisations d’actifs

- Statut : accepté
- Date : 2026-07-17

## Contexte

La valeur actuelle d’un actif ne suffit pas à expliquer l’évolution réelle du patrimoine. Les biens immobiliers et objets de valeur reçoivent des estimations manuelles espacées, tandis que les ETF et actions peuvent recevoir des cours plus fréquents. L’historique doit rester local, survivre aux évolutions du produit et être exportable sans dépendre d’un fournisseur externe.

## Décision

LifeLedger stocke un `AssetValuationSnapshot` qui représente la valeur totale observée d’un actif à une date, dans sa devise et avec sa source. Un actif ne possède qu’un point par jour : une correction effectuée le même jour remplace ce point.

`IAssetValuationHistoryService` constitue l’Interface commune utilisée par les dossiers manuels et l’Adapter de données de marché. Les cours unitaires restent séparés dans `AssetQuoteSnapshot` ; l’historique de valorisation contient la valeur totale réellement utilisée par le patrimoine.

La migration EF `AddAssetValuationHistory` ajoute la table et son index unique. La migration de données 1 → 2 initialise un premier point pour chaque actif existant à partir de `CurrentValue`, `ValuedOn`, `Currency` et `ValuationSource`. Le schéma d’export passe en version 3 et conserve l’import des versions 1 et 2.

## Conséquences

- Les graphiques peuvent comparer des observations réelles sans reconstruire le passé à partir d’hypothèses.
- La correction d’une estimation n’ajoute pas de doublon le même jour.
- Les clonages, exports et imports conservent les points en remappant l’identité de l’actif.
- Une future source comme PriceHubble devra écrire via l’Interface d’historique et identifier explicitement sa source.
- Les changements de devise historiques restent enregistrés avec la devise de chaque point ; leur conversion d’affichage dépend des taux locaux disponibles.
