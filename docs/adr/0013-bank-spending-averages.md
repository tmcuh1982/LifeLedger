# 0013 — Moyennes bancaires et promotion explicite dans la simulation

## Statut

Accepté — 18 juillet 2026.

## Contexte

Les relevés bancaires permettent d’estimer les dépenses courantes telles que l’alimentation, le carburant ou les assurances. Une moyenne calculée uniquement sur les mois contenant une opération surestime cependant les paiements rares : une assurance annuelle apparaîtrait comme une charge mensuelle complète.

L’historique observé peut aussi être incomplet ou mal classé. Il ne doit donc jamais modifier silencieusement une projection.

## Décision

- Le serveur calcule chaque moyenne par catégorie et par devise.
- Le dénominateur est l’ensemble des mois civils couverts par les relevés importés dans cette devise, y compris les mois sans opération de la catégorie.
- Seules les opérations `Expense` non exclues sont comptées. Les montants positifs de la catégorie sont traités comme des remboursements et diminuent le total net.
- Les coûts liés à un actif et les opérations explicitement exclues restent hors du calcul.
- Une moyenne observée n’entre dans la projection qu’après une action explicite de l’utilisateur.
- Cette action crée une dépense mensuelle récurrente indexée sur l’inflation. Le lien `ObservedBankCategory` associé à la devise permet de mettre à jour cette hypothèse sans doublon tout en conservant ses dates et futurs paliers.
- Le schéma d’export passe à la version 9.

## Conséquences

- Les charges annuelles ou irrégulières produisent une moyenne mensuelle cohérente lorsque la période importée est suffisante.
- Les petits historiques restent visibles avec leur nombre de mois afin que l’utilisateur juge leur fiabilité.
- Les hypothèses de projection restent auditables et modifiables dans le module Dépenses.
- Une colonne nullable et une migration EF Core sont nécessaires sur les dépenses existantes.
