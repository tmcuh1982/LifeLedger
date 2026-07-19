# 0020 — Jeu de démonstration déterministe et restaurable

## Statut

Accepté — 19 juillet 2026.

## Contexte

Le petit exemple créé au premier démarrage dépendait de la date du jour et ne couvrait qu’une partie du produit. Il ne pouvait donc pas servir de référence fiable pour les captures d’écran, les démonstrations publiques ou les tests de non-régression après des opérations d’ajout, de modification et de suppression.

## Décision

- Le jeu de démonstration devient une fixture produit explicitement versionnée.
- Ses identifiants, dates, horodatages, montants et historiques sont fixes.
- Il couvre les principales frontières du domaine : plusieurs devises et pays, revenus mensuels et saisonniers taxés, toutes les familles d’actifs, dossiers caractéristiques, dettes allouées, inflation et paliers de dépenses, épargne anticipée, banque, investissement, événements répétés, vente future et comparaison de scénarios.
- `POST /api/demo/restore` supprime les données financières locales puis reconstruit la fixture dans une transaction unique.
- Le client avertit clairement que la restauration remplace les données courantes et conseille un export préalable.
- Les paramètres techniques nécessaires aux migrations et les taux de change locaux sont préservés.

## Conséquences

- Les tests et captures commencent toujours sur le même état après une restauration.
- Une évolution volontaire du contenu de référence exige l’incrément de `DatasetVersion` et la mise à jour des assertions structurelles.
- La restauration ne doit jamais être déclenchée implicitement sur une base contenant des données.
- Le mode démo reste entièrement local et n’utilise aucun compte, fichier personnel ou service distant.
