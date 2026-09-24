# Restaurant POS — application iPad native (SwiftUI)

Client iPad natif pour `RestaurantPos.Api`, avec la parité fonctionnelle du client web
(`src/RestaurantPos.Api/wwwroot`). Il remplace les essais MAUI : aucune couche
d'abstraction entre le code et UIKit/SwiftUI, et chaque écran est couvert par des tests
automatiques.

## Architecture

```
ios/
├── project.yml                 # XcodeGen : source de vérité du projet (le .xcodeproj est généré)
├── Packages/PosKit/            # Toute la logique, sans SwiftUI → testable en quelques secondes
│   ├── Core/                   # Money (centimes), OrderMath (TVA, remises, split), options, paiement
│   ├── Models/                 # DTO Codable alignés sur l'API .NET
│   ├── Networking/             # Protocole PosAPI + HTTPPosAPI (URLSession)
│   ├── Realtime/               # Client SignalR minimal (WebSocket, protocole JSON)
│   ├── Stores/                 # État @Observable : session, catalogue, ticket, salle, KDS, fiscal, admin
│   └── Testing/InMemoryPosAPI  # Backend en mémoire (données d'amorçage identiques au serveur)
├── RestaurantPOS/              # App SwiftUI (vues uniquement)
│   ├── App/                    # Point d'entrée, verrouillage PIN, navigation
│   ├── DesignSystem/           # Thème, pavé numérique, boutons, toasts
│   └── Features/               # Caisse, Paiement, Salle, Cuisine, Clôture, Gestion
└── RestaurantPOSUITests/       # XCUITest : parcours utilisateur complets
```

Les vues n'appellent jamais le réseau directement : elles passent par les stores de
`PosKit`, qui dépendent du protocole `PosAPI`. En test, `InMemoryPosAPI` remplace
`HTTPPosAPI` : pas de serveur, des données identiques à chaque lancement, des tests
déterministes.

### Choix importants

- **Brouillon local du ticket.** L'API sait seulement *ajouter* des lignes. Les articles
  saisis restent donc modifiables sur l'iPad (quantité, service, suppression) et ne sont
  envoyés au serveur qu'au moment utile : envoi cuisine, paiement, remise, attente,
  transfert ou changement de table.
- **Montants en centimes entiers**, arrondis comme `Money.FromDecimal` côté .NET. Le
  partage à parts égales ne perd ni ne crée aucun centime.
- **Sur place / à emporter.** Côté serveur, `Takeaway = 0` et `EatIn = 1`. Les commandes
  de table sont créées « à emporter » par défaut, donc l'iPad force « sur place » (sinon
  la cuisine voyait `[À EMPORTER] T1`).

## Fonctionnalités (parité web)

PIN opérateur · plan de salle (ouverture avec couverts, création de tables, chronomètres) ·
grille tactile paginée (balayage) · touches rapides · options et suppléments avec
validation · services Direct/Suite/Dessert · envoi cuisine et « réclame suite » ·
remises globales et articles offerts · transfert et fusion de tables · encaissement
(CB, espèces avec rendu, titres-restaurant) · pourboires · partage à parts égales ·
facturation chambre avec signature · comptoir / vente à emporter (TVA réduite, n° de
retrait, buzzer, espèces express, commandes en attente, annulation sous PIN superviseur,
avoirs titres-restaurant) · Happy Hour (bandeau, compte à rebours, dérogations) · KDS
3 colonnes · rapports X / clôture Z NF525 · export FEC · tableau de bord · gestion du
catalogue, de la grille (glisser-déposer, formats, pages), de l'équipe, des imprimantes,
des plages Happy Hour · réglages réseau et synchronisation · temps réel SignalR.

## Démarrer

```bash
brew install xcodegen
cd ios
xcodegen generate
open RestaurantPOS.xcodeproj      # cible « RestaurantPOS », simulateur iPad
```

Au premier lancement, touchez **Changer** sur l'écran PIN pour saisir l'adresse du
serveur (par défaut `http://localhost:5080`). PIN de démonstration : `1234` (responsable),
`2468` (serveuse), `5678` (cuisine), `9999` (admin).

Pour lancer l'API localement (le port 5000 est pris par AirPlay sur macOS) :

```bash
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://0.0.0.0:5080 \
Jwt__Secret="SuperSecretKeyForRestaurantPosSystemThatIsAtLeast32BytesLong!" \
dotnet run --project src/RestaurantPos.Api
```

Mode démo sans serveur : lancez l'app avec l'argument `-UITestMode` (schéma Xcode →
*Run → Arguments*).

## Tests

```bash
./scripts/test.sh unit       # ~1 s   : 93 tests Swift Testing (calculs, contrats JSON, stores, HTTP, SignalR)
./scripts/test.sh ui         # ~8 min : 27 tests XCUITest sur simulateur iPad
./scripts/test.sh            # les deux
POS_API_URL=http://localhost:5080 ./scripts/test.sh contract   # parcours réel contre l'API .NET
```

| Niveau | Ce qui est vérifié |
|---|---|
| Calculs | TVA par taux, remises, articles offerts, split au centime, pourboire, rendu |
| Contrats | Décodage de vraies réponses capturées sur l'API (`Tests/PosKitTests/Fixtures`) |
| Stores | Commande, envoi cuisine, paiement, split, transfert, comptoir, attente, Happy Hour, KDS, Z, back-office |
| HTTP | Chemins, méthodes, corps JSON, jeton Bearer, erreurs 401/403/404/429/500 |
| UI | Connexion, table → cuisine → paiement, options, split, transfert, comptoir, attente, KDS, Happy Hour, X/Z, FEC, back-office |
| Contrat live | Parcours table complet contre le vrai serveur |

En cas d'échec, les tests UI joignent une capture d'écran au rapport `.xcresult`.

Arguments de lancement réservés aux tests : `-UITestMode`, `-UITestPin 1234`,
`-UITestSection floor|kitchen|fiscal|admin`, `-UITestHappyHour`.
