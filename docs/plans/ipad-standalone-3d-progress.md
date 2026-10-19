# iPad standalone 3d — suivi d'exécution (vérification et interface)

Spec : `docs/superpowers/specs/2026-10-13-ipad-standalone-3-clotures-archives-design.md` · Plan : `docs/superpowers/plans/2026-10-15-ipad-standalone-3d-verification-interface.md`

Base : .NET 766 (Domain 69, Infrastructure 440, Api 257) · PosKit 521 (73 suites)

| Tâche | Statut | .NET | PosKit | Tests UI | Findings de revue ouverts |
|---|---|---|---|---|---|
| 1 Chaînes `period_closures` et `archives` | fait | - | 526 (74 suites) | - | - |
| 2 JET `CHAIN_BREAK_DETECTED` | fait | - | 528 (74 suites) | - | - |
| 3 Refus métier localisés | fait | - | 529 (74 suites) | - | - |
| 4 Archives : ligne illisible, voisin illisible | fait | - | 531 (74 suites) | - | - |
| 5 `archiveFile` : récupération du fichier | fait | - | 535 (74 suites) | - | - |
| 6 FEC : filtre SQL, ordre des séquences | fait | - | 539 (74 suites) | - | - |
| 7 FEC : SIREN, société, charge utile du JET | fait | - | 544 (74 suites) | - | - |
| 8 `FiscalStore` : partage, verdict, plafond, noms sûrs | fait | - | 553 (76 suites) | - | - |
| 9 Écran Fiscal autonome + XCUITest | fait | - | 553 (76 suites) | 3 lancés, réussis | - |
| 11 Corrections de la revue finale : I1 sauvegardes anciennes (tables absentes), I2 SIREN par défaut vide, M1 encodage de `+` | fait | - | 557 (76 suites) | non relancés | - |
| 10 Documentation et vérification finale | fait | 766 | 554 (76 suites) | 52/53 (1 échec isolé non reproductible) | - |

Vérification finale (tâche 10) : `dotnet test RestaurantPos.slnx` 766/766 (Domain 69, Infrastructure 440, Api 257), `swift test` 554/554 (76 suites), tests UI 52/53 : `OperationsUITests.testAdminCreatesStaffWhoCanLogIn` a échoué une fois (« pin.4 » introuvable, hors périmètre du lot) et passe seul (relancé, 1/1) ; les 12 tests du mode autonome passent.

Reports explicites (hors 3d) : en mode serveur, les refus de clôture restent le message anglais du .NET (le code n'est pas transmis par `APIError`) ; mineurs 3b (couverture de la fixture : avril, borne annuelle ; chemin `tooLarge` du ZIP ; unicité V9 sans contrainte précise ; ordre des règlements `rowid` vs `Id` .NET ; clause legacy `POS_MAIN_TERM` ; ZIP corrompu et manifeste non-GUID non testés) ; mineurs 3c (remise de commande et destination absentes de la fixture ; fin de ligne ; tests de format sans montant négatif ; `parseIntObject` et clés dupliquées ; parseur de taux ; reçu HT=0/TVA=0 et plusieurs règlements avec pourboire) ; pas de bouton « vérifier la dernière archive » (le fichier passerait par `archiveFile`, qui journalise `ARCHIVE_EXPORTED`) ; chaîne `receipts` vide listée `T01` sur l'iPad, absente côté .NET ; ordre des types de clôture (mensuelle puis annuelle) au lieu de l'ordre d'apparition du .NET, sans effet sauf double rupture ; impression des clôtures (sous-projet 4).

Risques pour l'expert-comptable : dates d'écriture au jour UTC ; 580000 à deux libellés ; 411000 (chambre) seulement dans le FEC iPad ; 468600 (pourboires). Corrigé en fin de lot : I1 (restauration d'une sauvegarde d'avant V8/V9 refusée par « no such table »), I2 (SIREN par défaut « 123456789 » dans le nom du fichier FEC, désormais vide), M1 (`+` non encodé dans les requêtes HTTP).

Mineurs reportés par tâche (revues) :
- Tâche 1 : lignes à `PeriodType`/`TerminalId` NULL non vérifiées (colonnes NOT NULL) ; ordre des types ascendant vs ordre d'apparition .NET ; pas de cas multi-postes.
- Tâche 2 : chaque vérification d'une chaîne rompue ajoute une entrée `CHAIN_BREAK_DETECTED` (comme le .NET).
- Tâche 3 : les autres erreurs de `Local/` (Counter, Payments, Fec) restent en français codé en dur ; tests de textes localisés construits avec `L10n` (une mauvaise traduction n'est détectée que par `LocalizationTests`).
- Tâche 4 : paramètre d'injection `previousArchive:` sans commentaire de test seam ; repli sur la vraie lecture si la closure renvoie nil.
- Tâche 5 : course entre deux `archiveFile` simultanés (rename/`withoutOverwriting` jette au lieu de servir le fichier du gagnant ; écriture non atomique) ; pas de test de concurrence ni de cas `.tmp` + final présents.
- Tâche 6 : GLOB scanne tous les reçus même hors période (fail-closed) ; `upper()` empêche l'index sur `PaymentTenders` ; borne de fin non épinglée par un test ; message « Date illisible » dupliqué.
- Tâche 7 : SIREN du journal garde les espaces pour un appel direct à l'API ; `InMemoryPosAPI` ignore société et validation SIREN ; `+` non encodé corrigé (M1) ; clés des 3 `.strings` à comparer.
- Tâche 8 : fichiers temporaires non nettoyés (nom du FEC sans partie unique : un export concurrent écrase) ; `Data(contentsOf:)` jusqu'à 64 Mo sur le main actor ; `maxArchiveFileBytes` public seulement pour les tests.
- Tâche 9 : XCUITest à l'horloge réelle (sensible à une graine avec des reçus du mois précédent ; fragile le 1er du mois) ; texte du toast FR seulement ; verdict `unknown_archive` affiche le texte générique ; cartes en colonne fixe 360 pt ; champ société prérempli une fois ; clés orphelines `fiscal.verification_success/failed` ; bouton de vérification petit (cible tactile).
