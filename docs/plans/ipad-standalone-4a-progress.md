# iPad standalone 4a — suivi d'exécution (document et rendu des tickets)

Plan : `docs/superpowers/plans/2026-10-22-ipad-standalone-4a-rendu-tickets.md`

| Tâche | Statut | .NET | PosKit | Findings de revue ouverts |
|---|---|---|---|---|
| 1 Fixture .NET `ticket_document_reference.json` | fait | 768 (Domain 69 + Infrastructure 442 + Api 257) | 557 | aucun |
| 2 Modèle `TicketDocument` et JSON | fait | 768 (inchangé) | 562 | aucun |
| 3 Rendu texte ESC/POS et commandes | fait | 768 (inchangé, aucun changement .NET) | 569 | aucun |
| 4 Entrées, formats et textes localisés | fait | 768 (inchangé, aucun changement .NET) | 578 | aucun |
| 5 `TicketBuilder` | fait | 768 (inchangé, aucun changement .NET) | 587 | aucun |
| 6 Rendu raster CoreText | fait | 768 (inchangé, aucun changement .NET) | 597 | aucun |
| 7 Documentation et vérification finale | fait | 768 (Domain 69 + Infrastructure 442 + Api 257) | 597 | aucun |

## Contrôle visuel du raster (tâche 6)

Rendu à 576 points des scénarios `receipt_fr_full` et `receipt_ar` (PNG jetables, non commités) : texte lisible, à l'endroit et non inversé ; séparateurs pointillés ; colonnes alignées (étiquette à gauche, valeur à droite en français ; inversé en arabe) ; arabe lié et écrit de droite à gauche, latin et arabe mélangés correctement. Blocs alignés en haut comme le .NET : dans les colonnes arabes la ligne de base de l'étiquette arabe est plus basse que celle de la valeur latine (métriques plus hautes de Noto Sans Arabic ; rendu .NET non comparé visuellement). Les paragraphes RTL réordonnent le texte neutre selon l'algorithme bidi (`5 Rue des Lilas` affiché `Rue des Lilas 5`, JSON de TVA `{132:"10"}`), comportement attendu d'une direction de base RTL.

## Vérification finale (tâche 7)

- `dotnet test RestaurantPos.slnx` : 768 verts (69 + 442 + 257).
- `cd ios/Packages/PosKit && swift test` : 597 tests en 86 suites verts.
- `cd ios && xcodegen generate && ./scripts/test.sh unit` : 597 verts ; `xcodegen` ne modifie aucun fichier suivi.
- `xcodebuild -project RestaurantPOS.xcodeproj -scheme RestaurantPOS -destination 'generic/platform=iOS Simulator' build` : BUILD SUCCEEDED ; `PosKit_PosKit.bundle` dans l'app contient les 4 TTF Noto et `OFL.txt`.
- Suite XCUITest non relancée (aucune interface modifiée).
- Reste pour le lot 4b : calcul de `ReportPrintData`, transport et file d'impression, essai papier du raster.
