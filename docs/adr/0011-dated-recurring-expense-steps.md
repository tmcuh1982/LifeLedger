# 0011 — Paliers datés des dépenses récurrentes

## Statut

Accepté — 18 juillet 2026.

## Contexte

Une dépense récurrente ne suit pas toujours une simple courbe d’inflation. Un utilisateur peut estimer avoir besoin de 2 000 EUR par mois aujourd’hui, puis prévoir un changement de mode de vie qui porte ce besoin à 3 000 EUR dans cinq ans. Créer plusieurs dépenses avec des périodes adjacentes serait possible, mais difficile à comprendre et à maintenir.

Il faut aussi éviter de calculer cinq années d’inflation sur 3 000 EUR lorsque ce montant représente déjà l’estimation nominale de l’utilisateur à la date future.

## Décision

- Une dépense récurrente peut posséder une liste illimitée de `ExpenseAmountChange`.
- Chaque palier contient une date d’effet et un nouveau montant par occurrence, dans la devise de la dépense.
- `IExpenseScheduleService` choisit le dernier palier applicable au mois projeté.
- Si l’indexation est activée, le montant initial suit l’inflation depuis le début de la dépense. À chaque palier explicite, le nouveau montant devient la nouvelle base et l’inflation repart de cette date.
- Deux paliers ne peuvent pas commencer à la même date. Les paliers doivent appartenir à la période active d’une dépense récurrente.
- Le schéma d’export passe à la version 7. Les sauvegardes plus anciennes restent compatibles et reçoivent simplement une liste de paliers vide.

## Conséquences

- Un changement de niveau de vie peut être encodé dans une seule dépense compréhensible.
- Plusieurs évolutions futures peuvent être planifiées sans dupliquer les dépenses.
- Les projections évitent de compter deux fois l’inflation avant un montant futur déjà estimé.
- Une table enfant et une migration EF Core sont nécessaires, mais aucune migration sémantique des anciennes données n’est requise.
