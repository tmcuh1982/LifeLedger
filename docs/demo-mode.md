# Mode démonstration

Le mode démonstration fournit un foyer entièrement fictif et reproductible. Il est destiné aux démonstrations publiques, aux tests manuels, aux captures d’écran et aux tests de non-régression.

## Restaurer l’état de référence

1. Exporter une sauvegarde si les données courantes doivent être conservées.
2. Ouvrir **Paramètres**.
3. Dans **Mode démonstration**, choisir **Restaurer les données de démo**.
4. Confirmer le remplacement des données locales.

Vous pouvez ensuite ajouter, modifier ou supprimer n’importe quelle entrée. Une nouvelle restauration reconstruit exactement la version de référence.

## Contenu de la version 1

- un foyer avec deux enfants et des carrières en France, Pologne et Belgique ;
- un scénario complet et une alternative de retraite anticipée ;
- revenus en EUR, USD et PLN, dont une activité saisonnière et un loyer taxé ;
- cash, ETF, actions, crypto, immobilier, véhicule, entreprise et objet de collection ;
- dossiers détaillés de maison, appartement, véhicule et montre ;
- crédits immobiliers et automobile reliés aux biens ;
- dépenses quotidiennes, hebdomadaires, mensuelles et annuelles, paliers futurs et vacances financées à l’avance ;
- investissement mensuel, achat de voiture tous les cinq ans et vente future d’un appartement ;
- historique fictif de valorisations, cours, patrimoine et opérations bancaires.

## Captures d’écran reproductibles

Restaurez la démo immédiatement avant une série de captures, utilisez le scénario **Démonstration complète**, puis conservez la même langue et la même taille de fenêtre. Les données et graphiques applicatifs seront identiques. Les éléments dépendant du navigateur, comme le rendu des polices, peuvent encore varier légèrement entre plateformes.

## Contrat de régression

La fixture expose des identifiants fixes et une propriété `DatasetVersion`. Les tests d’intégration vérifient sa couverture structurelle, ses principaux résultats financiers et sa restauration après des opérations CRUD. Toute modification volontaire du contenu doit créer une nouvelle version documentée.
