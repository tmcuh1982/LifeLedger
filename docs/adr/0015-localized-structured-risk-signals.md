# 0015 — Alertes de simulation structurées et traduisibles

## Statut

Accepté — 18 juillet 2026.

## Contexte

Le moteur de projection produisait des phrases complètes en anglais pour signaler une rupture de fonds, une perte de pouvoir d’achat, une épargne de précaution insuffisante, une dette élevée ou un faible taux de réussite Monte Carlo. L’interface affichait directement ces phrases : changer la langue de LifeLedger ne pouvait donc pas les traduire.

Traduire ou analyser ces phrases après leur génération aurait couplé les calculs financiers à une formulation anglaise fragile.

## Décision

- Le moteur produit un signal structuré composé d’un code stable et, si nécessaire, d’une valeur numérique.
- L’API ne transporte aucune phrase destinée à l’affichage pour ces alertes.
- Le client React associe chaque code à une phrase complète dans les cinq langues prises en charge et applique le format numérique de la langue choisie.

## Conséquences

- Toutes les alertes suivent immédiatement le choix de langue de l’utilisateur.
- Les calculs et les traductions peuvent évoluer indépendamment.
- L’ajout d’un nouveau signal impose d’étendre le contrat TypeScript et les traductions de chaque langue.
- Le contrat REST des alertes passe d’une liste de chaînes à une liste d’objets `{ code, value }`.
