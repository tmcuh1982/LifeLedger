# 0014 — Rendements, déficits et revenu passif

## Statut

Accepté — 18 juillet 2026.

## Contexte

La hausse attendue d’un ETF augmente le patrimoine futur sans constituer un revenu encaissé. L’ancien tableau de bord ne montrait que les loyers, dividendes et royalties sous le libellé « revenu passif », ce qui rendait la capitalisation des actifs difficile à vérifier.

L’audit chiffré du moteur a également révélé quatre erreurs : un déficit était bloqué à zéro, un scénario sans actif recevait un rendement de 4 %, le cash à volatilité nulle utilisait la volatilité par défaut en Monte Carlo et le cycle historique commençait à son deuxième taux.

## Décision

- Le rendement d’un actif est capitalisé mensuellement dans le patrimoine projeté, après l’hypothèse d’impôt sur les gains.
- Le rendement du portefeuille reste pondé par la valeur actuelle de chaque actif. Sans actif, ce rendement vaut zéro.
- Un solde non financé peut devenir négatif. Il n’obtient aucun rendement positif tant qu’il reste négatif et il rend la trajectoire insolvable.
- Un actif `Cash` avec une volatilité nulle reste stable dans Monte Carlo. La volatilité par défaut ne s’applique qu’aux autres actifs non configurés.
- Le cycle historique illustratif commence à son premier couple rendement-inflation.
- L’indépendance financière est cherchée à partir de la première année projetée complète, puis son année civile est convertie en un décalage relatif à la date de départ.
- Le revenu passif encaissé désigne uniquement les loyers, dividendes et royalties nets de taxe. La croissance mensuelle attendue du portefeuille est affichée séparément et ne prétend pas être du cash disponible.

## Conséquences

- Les probabilités de réussite et avertissements de rupture de fonds peuvent maintenant détecter un patrimoine négatif sans dette explicite.
- Le tableau de bord explique pourquoi un ETF à rendement positif augmente la valeur finale sans augmenter le revenu passif encaissé.
- Les résultats historiques changent d’un cran pour respecter l’ordre documenté du cycle.
- Des tests indépendamment calculables verrouillent les actifs, revenus, taxes, inflation, dépenses, dettes et trois modes de simulation.
