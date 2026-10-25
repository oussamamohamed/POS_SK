# iPad standalone 4c — suivi d'exécution (imprimantes Star et Epson)

Plan : `docs/superpowers/plans/` (lot 4c)

Base de référence : PosKit 689

| Tâche | Statut | PosKit | PosPrinters | Findings de revue ouverts |
|---|---|---|---|---|
| 1 Paquet `PosPrinters`, adresse, session et transport générique | fait | 689 | 17 (`swift test` et `./scripts/test.sh unit`) | aucun |
| 2 Adaptateur Star (StarIO10) et recherche | fait | 689 | 22 | aucun |
| 3 Adaptateur Epson (ePOS2), installation du SDK, recherche | fait | 689 | 26 (+ `test_install_epson_sdk.sh`, + 1 test de constantes sur simulateur avec SDK) | aucun |
| 4 Branchement dans l'app | fait | 689 | 31 (`./scripts/test.sh unit`) ; UI : `StandaloneUITests/testModeChoiceThenFirstRunThenUnlock` passé (Fake transport) | aucun |
| 5 Guide d'essai papier, documentation, vérification finale | fait | 689 | 31 | aucun |

## Vérification finale (lot 4c)

- `dotnet test RestaurantPos.slnx` : Domain 69, Infrastructure 441, Api 258, 0 échec (exécuté le 2026-10-25 vers 20 h 45 CET, hors plage minuit-2 h où `ReportAccuracyTests.FinancialDashboard_DiscountedOrders_AccuratelyReflectsNetTotals` est instable).
- PosKit `swift test` 689, PosPrinters `swift test` 31, `./scripts/test.sh unit` vert (689 + 31 + `test_install_epson_sdk.sh`), build simulateur de l'app (`xcodebuild -project RestaurantPOS.xcodeproj -scheme RestaurantPOS -destination 'generic/platform=iOS Simulator' -derivedDataPath build/DerivedData build`) réussi.
- SDK Epson : **installé** (Vendor/libepos2.xcframework, 2.37.1) pendant ces vérifications. Adaptateurs Star et Epson non testés sur matériel : voir `docs/impression-essai-papier.md`.

## Constats

- Tâche 1 : identité du paquet Star retenue dans `Package.swift` : `.package(url: "https://github.com/star-micronics/StarXpand-SDK-iOS", exact: "2.14.1")` ; SPM la nomme `starxpand-sdk-ios` (dérivée de l'URL) et le produit se référence par `.product(name: "StarIO10", package: "StarXpand-SDK-iOS", condition: .when(platforms: [.iOS]))`. `swift test` sur macOS résout le paquet sans le lier.
- Tâche 1 : la course délai/session de `SessionPrinterTransport` n'utilise pas `withThrowingTaskGroup` (il attendrait un enfant qui ignore l'annulation) : continuation à premier arrivé, tâches annulées mais non attendues. Les tests de session muette affichent l'avertissement Swift « continuation leaked » (voulu : la session factice ne reprend jamais).
- Tâche 2 : API StarIO10 2.14.1 vérifiée dans le `.swiftinterface` (erreur `StarIO10Error` enum : `notFound`, `inUse`, `unprintable(message:errorCode:status:)`, `communication`, délai d'impression = `StarIO10ErrorCode.printingTimeout`). Pas de cas « timeout » dédié à l'ouverture : un délai d'ouverture arrive en `communication` (traduit en « Erreur Star ») mais `SessionPrinterTransport` applique de toute façon son propre délai de 3 s. Compilation iOS validée (`xcodebuild -scheme PosPrinters-Package -destination 'generic/platform=iOS Simulator' build`) ; adaptateur non testable sans matériel.
- Tâche 3 : SDK Epson 2.37.1 (et non 2.32.0) : `libepos2.xcframework` est un framework DYNAMIQUE (tranches `ios-arm64`, `ios-arm64_x86_64-simulator`, `module.modulemap` fourni : `import libepos2`). Exigences d'édition de liens/embarquement (README + `ePOS_SDK_iOS_Static_to_Dynamic_Migration_Guide_en.md` ; le PDF du manuel n'a pas été lu) : aucune bibliothèque système à lier (`xml2` et consorts valaient pour l'ancien statique) ; l'app doit EMBARQUER et signer le framework (« Embed & Sign », runpath), à vérifier à l'intégration dans `project.yml` (tâche 5) ; ne jamais lier en même temps `libepos2-static`. Le paquet n'ajoute donc aucun `linkerSettings`.
- Tâche 3 : `Vendor/` est ignoré par git ; installé en local par `ios/scripts/install_epson_sdk.sh <zip>`. `Package.swift` ajoute la cible binaire seulement si `Vendor/libepos2.xcframework` existe. `swift test` (PosKit et PosPrinters) et le build iOS passent avec et sans SDK. Test des constantes (`PosPrintersEpsonTests`, cible séparée : la dépendance `libepos2` n'était pas visible depuis `PosPrintersTests`) : tourne seulement sur simulateur iOS avec le SDK (`xcodebuild test -scheme PosPrinters-Package -destination 'platform=iOS Simulator,name=iPad Pro 13-inch (M5)'`), passé.
- Tâche 3 : constantes du `ePOS2.h` 2.37.1 : retours `EPOS2_ERR_CONNECT=2, TIMEOUT=3, NOT_FOUND=7, IN_USE=8, DEVICE_BUSY=16` ; réception `CODE_ERR_TIMEOUT=1, NOT_FOUND=2, COVER_OPEN=4, EMPTY=7, DEVICE_BUSY=33, IN_USE=35, CONNECT=36` ; séries `TM_M30=1, T20=6, T82=10, T88=12, M30II=21, M50=23, T88VII=24, M30III=29`. TM-T88VI → `TM_T88` (pas de série VI dans le SDK) ; TM-T88III hors liste du SDK : réseau brut 9100 seulement. Adaptateur Epson non testé sur matériel.
- Tâche 4 : aucune écriture conditionnelle dans `project.yml` : Xcode embarque et signe tout seul le framework dynamique d'un `binaryTarget` SPM (`Frameworks/libepos2.framework` présent avec SDK, absent sans SDK, build vert dans les deux cas). Signature ad hoc sur simulateur. Pas de point d'injection existant pour la recherche : `AppEnvironment.printerDiscovery` (Star + Epson combinées) créé, non utilisé avant le lot 4d. `openLocal` n'utilise plus `openStandalone` (même ouverture vierge, transport en plus).
- Tâche 4 : cache global de manifestes SwiftPM (`~/Library/Caches/org.swift.swiftpm`) : après avoir retiré/remis `Vendor/`, `xcodebuild` échoue (« does not contain a binary artifact ») ; supprimer `.build` et la DerivedData du projet ne suffit pas. Contournement par état sans purge globale : lancer `xcodebuild` avec une variable d'environnement quelconque nouvelle (`env POS_X=$RANDOM xcodebuild ...`), qui change la clé du cache. Appairage et découverte non testés (build simulateur).

## Reporté au lot 4d

- Découverte Star LAN : `StarDiscovery` prend `settings.identifier` comme `ipAddress` ; utiliser `printer.information?.detail.lan.ipAddress` (à vérifier dans le `.swiftinterface` 2.14.1).
- La découverte par diffusion UDP peut exiger l'entitlement `com.apple.developer.networking.multicast` (+ `NSBonjourServices`) sur iOS 14+ ; l'impression vers une IP connue n'en a pas besoin.
- `EpsonDiscovery.Delegate` est libéré si `stop()` répond encore « en cours » après 20 essais : le garder vivant jusqu'à la réussite de `stop()`, ou journaliser.
- Le doc du protocole `PrinterDiscovery` promet un dédoublonnage par source que `StarDiscovery` ne fait pas (corriger le doc ou activer `isEnabledFindSameDevice`), et `Combined` rend l'ordre des sources, pas celui d'arrivée (corriger le doc).
