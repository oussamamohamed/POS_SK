# iPad standalone 3b — suivi d'exécution (archives fiscales)

Spec : `docs/superpowers/specs/2026-10-13-ipad-standalone-3-clotures-archives-design.md` · Plan : `docs/superpowers/plans/2026-10-14-ipad-standalone-3b-archives.md`

| Tâche | Statut | .NET | PosKit | Tests UI | Findings de revue ouverts |
|---|---|---|---|---|---|
| 1 Fixture .NET `archive_reference.json` | fait | 434 (Infrastructure.Tests) | - | - | aucun |
| 2 ZIP stocké (`ZipStoreWriter` / `ZipStoreReader`) | fait (1 tour de correction) | - | non relevé | - | aucun |
| 3 Signature d'archive | fait | - | non relevé | - | aucun |
| 4 Migration V9 (`FiscalArchives`) | fait | - | non relevé | - | aucun |
| 5 Contenu de l'archive (`IndentedJSON`, sélection) | fait | - | non relevé | - | aucun |
| 6 Dépôt : chaîne, liste, vérification | fait | - | non relevé | - | aucun |
| 7 API `LocalPosAPI+Archives` | fait | - | 483 | - | aucun |
| 8 Vérification globale et documentation | fait | 762 (Domain 69, Infrastructure 436, Api 257) | 483 (68 suites) | - | aucun |

Vérification finale (tâche 8) : `dotnet test RestaurantPos.slnx` 762/762, `swift test` 483/483, `xcodebuild` générique simulateur : BUILD SUCCEEDED. Le commit 4671fc9 (hors lot) n'a causé aucun échec.

Mineurs reportés : couverture de la fixture (avril, borne annuelle) ; chemin `tooLarge` du ZIP non testé ; tests d'unicité V9 sans contrainte précise ; ordre des paiements `rowid` vs `Id` .NET (UUID v4) et clause legacy `POS_MAIN_TERM` non reprise ; `archiveRecord` renvoie nil sur ligne illisible (risque de fork de chaîne) ; tests de ZIP corrompu et de manifeste non-GUID manquants ; échec de `moveItem` après commit laisse un `.tmp` (409 au retry, 404 au fetch).
