# Ajouter un modèle de banque

Les imports bancaires sont locaux et extensibles. N’ajoutez jamais un relevé réel au dépôt.

## Banque CSV ou fichier délimité

1. Copiez une définition JSON dans `src/LifeLedger.Api/BankImporters/`.
2. Donnez-lui une clé stable et versionnée, par exemple `ma-banque-csv-v1`.
3. Configurez le séparateur, la ligne de métadonnées, les colonnes et la culture décimale.
4. Ajoutez un test avec des noms, comptes et montants entièrement fictifs.
5. Si la banque change son export, ajoutez `v2` au lieu de modifier l’interprétation historique de `v1`.

`erste-csv-v1.json` sert d’exemple complet.

## Banque PDF

Une mise en page PDF est rarement assez stable pour être décrite uniquement par des colonnes. Ajoutez une définition JSON avec une nouvelle valeur `format`, puis un Adapter implémentant `IBankStatementAdapter`. L’Adapter doit :

- extraire le compte et sa devise ;
- produire des montants signés ;
- conserver les dates d’opération et de valeur ;
- générer une référence stable pour la déduplication ;
- échouer clairement si la mise en page n’est plus reconnue ;
- ne jamais journaliser le texte intégral du relevé.

## Règles de sécurité

- Le fichier original est lu en mémoire et n’est jamais stocké.
- La devise est obligatoire et doit être confirmée.
- La base ne conserve qu’un identifiant de compte masqué et une empreinte à sens unique.
- Une opération importée reste un historique observé ; elle ne devient pas automatiquement une dépense future.

## Après l’import

Chaque opération validée reste modifiable. L’utilisateur peut changer sa nature et sa catégorie, la relier à un bien ou à un plan d’investissement, ou l’exclure de l’estimation des dépenses mensuelles sans supprimer l’historique.

Un coût lié à un bien n’augmente jamais automatiquement sa valeur. L’éditeur peut enregistrer une nouvelle valeur totale confirmée par l’utilisateur et sa date ; ce changement passe par l’historique de valorisation du bien afin de rester traçable.

## Moyennes mensuelles

LifeLedger calcule les moyennes par catégorie et par devise sur tous les mois civils couverts par les relevés importés. Un mois couvert sans achat d’une catégorie compte donc comme zéro : une assurance annuelle de 1 200 EUR observée sur douze mois vaut 100 EUR par mois, et non 1 200 EUR.

Seules les opérations classées comme dépenses et non exclues entrent dans ce calcul. Les remboursements positifs diminuent le total net. Les devises ne sont jamais additionnées entre elles.

Une moyenne observée ne modifie pas automatiquement la projection. Le bouton « Utiliser dans la simulation » crée une dépense récurrente mensuelle indexée sur l’inflation. Une nouvelle utilisation de la même catégorie et devise met à jour cette hypothèse sans créer de doublon.
