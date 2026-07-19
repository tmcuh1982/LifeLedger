# 0012 — Reclassement des opérations bancaires et valorisation explicite

## Statut

Accepté — 18 juillet 2026.

## Contexte

La classification effectuée pendant l’import bancaire n’est pas définitive. Une opération peut être importée sans catégorie, puis identifiée plus tard. Un paiement exceptionnel, comme 35 000 EUR de toiture, ne doit pas fausser l’estimation du coût de vie mensuel.

Des travaux peuvent aussi préserver ou augmenter la valeur d’un bien. Leur coût ne mesure cependant pas automatiquement la valeur créée : une facture de 35 000 EUR ne prouve pas une hausse immobilière identique.

## Décision

- Chaque opération importée reste modifiable après validation : classification, catégorie, bien lié, plan d’investissement lié et exclusion de l’analyse des dépenses.
- `IsExcludedFromSpendingAnalysis` est indépendant de la classification. Une dépense peut donc rester correctement décrite et visible tout en étant absente de l’estimation mensuelle.
- Seules les opérations classées `Expense` et non exclues entrent dans l’estimation mensuelle observée. Les transferts, investissements, revenus, opérations ignorées et coûts liés à un bien n’y entrent jamais.
- Une opération `AssetExpense` peut être reliée à un bien.
- LifeLedger n’ajoute jamais automatiquement le montant payé à la valeur du bien.
- L’utilisateur peut saisir une nouvelle valeur totale estimée et sa date. L’API met alors à jour l’actif et écrit un point dans son historique de valorisation avec l’opération bancaire comme source.
- Le schéma d’export passe à la version 8. Les anciennes sauvegardes restent compatibles et considèrent les opérations comme non exclues par défaut.

## Conséquences

- Les erreurs de classement ne nécessitent plus de supprimer et réimporter un relevé.
- Les achats exceptionnels ne déforment plus les estimations de dépenses courantes.
- Le lien entre travaux et patrimoine reste explicite et auditable sans supposer une relation coût-valeur incorrecte.
- Une colonne booléenne et une migration EF Core sont nécessaires ; aucune transformation sémantique des anciennes opérations n’est requise.
