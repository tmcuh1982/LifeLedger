# 0018 — Projection du patrimoine ventilée par catégorie

## Statut

Accepté — 18 juillet 2026.

## Contexte

Le moteur fusionnait tous les actifs dans un solde unique auquel il appliquait un rendement pondéré. Le patrimoine final était calculable, mais il était impossible d’expliquer quelle part venait de l’immobilier, des ETF, du cash ou d’une autre catégorie. Cette agrégation masquait aussi la différence entre une hausse de valeur non encaissée et une véritable rentrée d’argent.

## Décision

- Chaque actif conserve son propre solde projeté et compose selon son rendement, sa volatilité et son hypothèse d’impôt sur les gains.
- Les actifs sont regroupés par catégorie personnalisée, ou par type technique si aucune catégorie n’est renseignée, uniquement lors de la production de la timeline.
- Les futurs versements d’investissement, la trésorerie non affectée et les réserves pour dépenses planifiées sont exposés comme composantes séparées.
- La dette restante est exposée comme une composante négative.
- Un déficit de trésorerie vend automatiquement, à hauteur du besoin, le cash existant, puis les actifs liquides et les investissements futurs, et enfin les actifs non liquides. Cette liquidation abstraite ne change pas le patrimoine au moment du transfert.
- À chaque point, le patrimoine net total est exactement la somme de toutes les composantes.
- Le graphique affiche les composantes en zones colorées empilées, le patrimoine net total en ligne principale et la valeur corrigée de l’inflation en ligne secondaire.

## Conséquences

- Une maison peut prendre de la valeur sans être présentée comme un revenu passif ; sa valeur projetée reste comprise dans le patrimoine mobilisable par une vente future.
- Les rendements de catégories différentes ne se diluent plus dans un taux moyen unique au fil des années.
- Un déficit de trésorerie ne devient négatif qu’après épuisement de la valeur mobilisable ; avant cela, le graphique montre quelles catégories ont été consommées.
- Une vente réelle d’actif devra être modélisée plus tard comme un transfert explicite de l’actif vers la trésorerie, sans modifier le patrimoine au moment du transfert hors frais et taxes.
