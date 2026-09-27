# Fixtures de contrat API

Réponses JSON **réelles** de `RestaurantPos.Api` (mode Development, données de démo),
capturées en rejouant un service complet : connexion PIN 1234, ouverture de T2 (3 couverts),
2 burgers avec options + 1 boisson, envoi cuisine, bump KDS, paiement CB partiel puis espèces,
vente comptoir (ouverture, sur place, mise en attente, rappel, encaissement), rapport X et clôture Z.

Seul le jeton JWT de `login.json` a été remplacé par `test-token`.

Les tests `ContractTests` décodent ces fichiers : si un DTO change côté serveur, ils échouent
avant que l'iPad ne casse. Pour les régénérer, démarrer l'API puis rejouer les appels décrits
dans `ContractTests.swift` (ou lancer `LiveServerTests`, qui exerce le même scénario en direct).
