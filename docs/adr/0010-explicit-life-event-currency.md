# 0010 — Devise explicite des événements de vie

## Statut

Accepté — 18 juillet 2026.

## Contexte

L’éditeur affichait une devise pour les événements de vie, mais le serveur ne la persistait pas. Le moteur interprétait donc implicitement tous les impacts dans la devise principale du profil. Cette ambiguïté empêchait de planifier correctement un achat répété, par exemple une voiture en PLN dans un profil en EUR.

Le libellé technique « impact ponctuel » obligeait aussi l’utilisateur à comprendre le signe du montant. Une dépense devait être négative, sans que l’interface l’explique clairement.

## Décision

- Chaque événement stocke une devise ISO 4217 explicite.
- Le moteur convertit les impacts ponctuels et mensuels dans la devise principale avant de les appliquer à la projection.
- Les événements déjà enregistrés reçoivent la devise principale de leur profil par une migration de données de la version 4 vers la version 5. Cela conserve exactement leur ancienne interprétation.
- Le format d’export passe à la version 6. Les exports plus anciens attribuent la devise du profil aux événements pendant l’import.
- `VehiclePurchase` représente l’achat d’une voiture. Dans l’interface, le coût est saisi comme un nombre positif, puis persisté comme impact négatif.
- Sélectionner un achat de voiture active par défaut la répétition `EveryFiveYears`, tout en laissant la fréquence et la date de fin modifiables.

## Conséquences

- Les achats répétés peuvent être exprimés dans une devise différente de celle du profil sans fausser le patrimoine projeté.
- L’utilisateur n’a plus besoin de saisir un signe négatif pour une voiture.
- La colonne de devise et la migration de données doivent être appliquées au démarrage avant toute projection.
- Les consommateurs d’un export version 6 doivent lire la devise propre à chaque événement.
