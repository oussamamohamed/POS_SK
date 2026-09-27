# Restaurant POS — application iPad native (SwiftUI)

Application de caisse **100 % native iPadOS** (SwiftUI, iOS 17+), branchée sur l'API existante
`RestaurantPos.Api`, avec la même couverture fonctionnelle de service que le site web :

| Écran | Fonctions |
|---|---|
| **Verrouillage** | PIN opérateur sur pavé à l'écran (validation auto à 4 chiffres), secousse + retour haptique en cas d'erreur, réglages accessibles |
| **Salle** | Tables en temps réel (libre / occupée / addition), couverts, serveur, durée d'occupation, montant en cours, filtre, ouverture avec choix des couverts |
| **Prise de commande** | Favoris + catégories colorées, recherche sans accents, tuiles ≥ 104 pt, options (cuissons, suppléments, règles min/max/obligatoire), note cuisine, envois (Direct / Suite / Dessert / À la demande), quantités, glisser pour supprimer |
| **Encaissement** | Espèces avec rendu monnaie et billets suggérés, CB, titre-restaurant, carte cadeau, pavé de montant, partage à parts égales au centime près, règlement convive par convive, signature NF525 |
| **Comptoir** | Vente directe à emporter / sur place, mise en attente et reprise, annulation protégée par PIN responsable, multi-règlement, numéro de retrait, bipeur, politique titre-restaurant |
| **Cuisine (KDS)** | 3 colonnes (à préparer / en préparation / prêt), filtre par poste, bons en retard signalés, un toucher = étape suivante |
| **Rapports fiscaux** | Rapport X en direct, dernière clôture Z scellée, clôture Z avec confirmation (réservé aux responsables) |
| **Réglages** | Adresse du serveur, identifiant terminal, test de connexion, **mode démonstration** (sans serveur) |

L'administration (carte, personnel, imprimantes, happy hour, grilles) reste sur le back-office web.

## Architecture

```
ios/
├── POSKit/                 Package Swift : TOUTE la logique, testable sans iPad (macOS et Linux)
│   ├── Models/             DTO alignés sur l'API .NET (enums entiers, montants décimaux, dates .NET)
│   ├── Domain/             Money (centimes), TVA, partage, saisie montant/PIN, modificateurs
│   ├── Networking/         POSAPI (protocole) + POSAPIClient (HTTP, jeton Bearer, erreurs en français)
│   ├── Features/           Modèles d'écran @Observable : AppModel, OrderModel, CheckoutModel…
│   └── Backend/            InMemoryPOSBackend : serveur simulé (mode démo, aperçus, tests UI)
├── POSApp/                 Interface SwiftUI uniquement (vues fines, aucune règle métier)
├── POSAppUITests/          Tests UI XCUITest : parcours réels sur simulateur iPad
├── project.yml             Description du projet (XcodeGen) — RestaurantPOS.xcodeproj en est généré
└── scripts/                test-kit.sh, test-ui.sh
```

Pourquoi cette organisation règle les problèmes des essais précédents :

- **L'interface ne contient pas de logique** : chaque règle (fusion des lignes, reste à payer,
  partage, droits par rôle, expiration de session) est dans `POSKit` et couverte par des tests
  qui tournent en une seconde, sans simulateur.
- **Contrat API vérifié sur des réponses réelles** : les fixtures ont été capturées sur la vraie
  API. Cela a déjà révélé que `POST /api/kds/tickets/{id}/bump` renvoie `ticketId` au lieu de `id`
  (géré), et un bug serveur corrigé dans ce lot : les commandes de table étaient « à emporter »
  par défaut (libellé `[À EMPORTER] T2` en cuisine et risque de TVA à emporter).
- **Tests UI déterministes** : lancée avec `-uiTesting`, l'app utilise le serveur simulé ;
  chaque test repart de données connues (T3 occupée à 36,00 €, bons cuisine en cours…).
- **Saisie hors-ligne tolérante** : les articles touchés restent sur l'iPad jusqu'à l'envoi en
  cuisine, l'encaissement, la mise en attente ou le changement de table, puis sont enregistrés.
  En cas d'erreur réseau, rien n'est perdu et un message clair s'affiche.

## Lancer l'app

Prérequis : Mac avec **Xcode 16 ou plus récent**.

1. Ouvrir `ios/RestaurantPOS.xcodeproj`, choisir un simulateur iPad (ou un iPad) et lancer (⌘R).
2. Au premier lancement l'app est en **mode démonstration** : codes `1234` (responsable),
   `2468` (serveuse), `5678` (cuisine), `9999` (admin).
3. Pour le vrai serveur : roue dentée de l'écran PIN → désactiver le mode démo → saisir l'adresse
   (ex. `192.168.1.20:5000`) → « Tester la connexion » → Enregistrer.

Pour un iPad physique, choisir son équipe dans *Signing & Capabilities* (identifiant
`com.restaurantpos.ipad`).

> Si `project.yml` est modifié : `brew install xcodegen && cd ios && xcodegen generate`.

## Tests automatiques

| Commande | Contenu | Où |
|---|---|---|
| `ios/scripts/test-kit.sh` | 80 tests Swift Testing : montants, dates .NET, TVA, partage, contrat API (fixtures réelles), client HTTP, prise de commande, encaissement, comptoir, cuisine, fiscal, session | macOS ou Linux |
| `POS_API_URL=http://127.0.0.1:5080 ios/scripts/test-kit.sh` | + scénario complet contre un vrai serveur (table, options, cuisine, partage CB/espèces, comptoir, attente, rapport X) | idem, API démarrée |
| `ios/scripts/test-ui.sh` | 18 tests XCUITest sur simulateur iPad, avec captures d'écran dans `ios/build/UITests.xcresult` | macOS + Xcode |

La CI GitHub Actions (`.github/workflows/ios.yml`) exécute les trois à chaque push touchant
`ios/` ou l'API : tests Linux, scénario sur l'API démarrée dans la CI, et tests UI sur
simulateur iPad (`macos-15`) avec le rapport `.xcresult` en artefact.

Démarrer l'API en local pour le test d'intégration :

```bash
Jwt__Secret='UneCleDeTestDAuMoins32CaracteresIci!' ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS=http://127.0.0.1:5080 dotnet run --project src/RestaurantPos.Api -c Debug
```

(`Jwt:Secret` est obligatoire : sans lui l'API renvoie une erreur 500 dès la première requête,
même en Development.)
