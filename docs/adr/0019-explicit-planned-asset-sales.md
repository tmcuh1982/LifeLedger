# 0019 — Ventes futures d’actifs explicites

## Statut

Accepté — 18 juillet 2026.

## Contexte

La projection par catégorie fait croître la valeur d’un bien, mais une hausse non réalisée n’est ni un revenu passif ni du cash disponible. Une maison ou un portefeuille vendu dans le futur doit disparaître de sa catégorie et son produit net doit rejoindre explicitement une autre poche du patrimoine. Un simple événement de vie monétaire ne permet pas de connaître l’actif vendu, son coût d’acquisition, les dettes qui le financent ou la destination des fonds.

## Décision

- Une `PlannedAssetSale` appartient à un scénario et référence exactement un actif source. Un actif ne peut avoir qu’une vente planifiée.
- La valeur brute est soit la valeur de l’actif composée jusqu’au mois de vente, soit un prix nominal saisi dans une devise explicite.
- Le moteur déduit les frais de vente et un impôt configurable sur la plus-value positive par rapport au coût d’acquisition.
- À la demande de l’utilisateur, les soldes projetés des dettes allouées à l’actif sont remboursés avant le transfert du solde.
- Le produit net rejoint le cash projeté, un autre actif du même scénario ou un plan d’investissement du même scénario.
- La timeline expose le prix brut, les frais, l’impôt, la dette remboursée et le montant transféré. Le graphique conserve sa ventilation par catégorie avant et après la vente.
- Les ventes sont incluses dans l’export privé version 11. Le format de données applicatif passe en version 6 avec une migration sans transformation des scénarios existants.

## Conséquences

- Une vente devient une opération de bilan explicable et testable, distincte d’un revenu récurrent.
- La simulation peut représenter une réduction de logement, une sortie d’ETF ou la cession d’un objet sans inventer de liquidité avant la date prévue.
- Les taux restent des hypothèses personnelles et ne constituent pas un moteur fiscal par pays.
- Le prix manuel n’est pas automatiquement indexé : il représente l’estimation nominale saisie pour la date de vente.
