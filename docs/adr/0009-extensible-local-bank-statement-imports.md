# ADR 0009 — Imports bancaires locaux et extensibles

- Statut : accepté
- Date : 2026-07-18

## Contexte

Les banques ne proposent pas un format commun. Erste exporte un CSV sans en-tête dont les métadonnées de compte occupent la première ligne. BNP Paribas Fortis fournit un PDF paginé, avec une mise en page et des références propres à la banque. Un parseur CSV générique ne peut ni préserver correctement la devise, ni gérer ces variantes de façon fiable.

Les relevés contiennent en outre des données personnelles sensibles. Une opération passée observée ne doit pas être confondue avec une dépense future utilisée par le simulateur.

## Décision

Créer un Module `IBankStatementImportModule` responsable de la prévisualisation, de la validation, de la déduplication et de l’enregistrement atomique.

- Chaque format passe par un Adapter derrière cette Interface.
- Les formats délimités utilisent des définitions JSON versionnées dans `BankImporters/`. Une nouvelle banque CSV peut ainsi être ajoutée sans modifier la logique du Module.
- Les PDF dont la mise en page est spécifique utilisent un Adapter de code. L’extraction de texte reste locale via PdfPig, sous licence Apache-2.0.
- La devise du compte est extraite du relevé puis obligatoirement confirmée. Aucun défaut silencieux vers EUR n’est autorisé.
- Le fichier original est traité en mémoire puis oublié. La base conserve seulement le nom du fichier, une empreinte SHA-256, le modèle utilisé, le compte masqué et les opérations validées.
- Une `BankTransaction` est un fait historique observé. Elle reste distincte d’une `Expense`, qui est une hypothèse de planification future.
- Une opération peut être classée et reliée à un `Asset` ou un `InvestmentPlan`, sans modifier automatiquement la projection.
- Les empreintes du fichier et des opérations empêchent la réimportation exacte et les doublons provenant de périodes qui se chevauchent.

## Conséquences

- Le menu Patrimoine contient un espace dédié aux opérations bancaires ; l’import n’est plus un réglage technique.
- Les contributeurs peuvent ajouter un modèle CSV par configuration. Un nouveau PDF nécessite généralement un Adapter et des tests de non-régression avec des données anonymisées.
- Les relevés réels ne doivent jamais être ajoutés au dépôt, aux journaux ou aux jeux de tests.
- Une future fonctionnalité pourra proposer de créer une hypothèse de dépense à partir de l’historique, mais uniquement après confirmation explicite.
