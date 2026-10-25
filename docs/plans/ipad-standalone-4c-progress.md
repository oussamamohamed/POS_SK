# iPad standalone 4c — suivi d'exécution (imprimantes Star et Epson)

Plan : `docs/superpowers/plans/` (lot 4c)

Base de référence : PosKit 689

| Tâche | Statut | PosKit | PosPrinters | Findings de revue ouverts |
|---|---|---|---|---|
| 1 Paquet `PosPrinters`, adresse, session et transport générique | fait | 689 | 17 (`swift test` et `./scripts/test.sh unit`) | aucun |
| 2 Adaptateur Star (StarIO10) et recherche | fait | 689 | 22 | aucun |

## Constats

- Tâche 1 : identité du paquet Star retenue dans `Package.swift` : `.package(url: "https://github.com/star-micronics/StarXpand-SDK-iOS", exact: "2.14.1")` ; SPM la nomme `starxpand-sdk-ios` (dérivée de l'URL) et le produit se référence par `.product(name: "StarIO10", package: "StarXpand-SDK-iOS", condition: .when(platforms: [.iOS]))`. `swift test` sur macOS résout le paquet sans le lier.
- Tâche 1 : la course délai/session de `SessionPrinterTransport` n'utilise pas `withThrowingTaskGroup` (il attendrait un enfant qui ignore l'annulation) : continuation à premier arrivé, tâches annulées mais non attendues. Les tests de session muette affichent l'avertissement Swift « continuation leaked » (voulu : la session factice ne reprend jamais).
- Tâche 2 : API StarIO10 2.14.1 vérifiée dans le `.swiftinterface` (erreur `StarIO10Error` enum : `notFound`, `inUse`, `unprintable(message:errorCode:status:)`, `communication`, délai d'impression = `StarIO10ErrorCode.printingTimeout`). Pas de cas « timeout » dédié à l'ouverture : un délai d'ouverture arrive en `communication` (traduit en « Erreur Star ») mais `SessionPrinterTransport` applique de toute façon son propre délai de 3 s. Compilation iOS validée (`xcodebuild -scheme PosPrinters-Package -destination 'generic/platform=iOS Simulator' build`) ; adaptateur non testable sans matériel.
