# iPad standalone 1a — suivi d'exécution

Plan : `docs/superpowers/plans/2026-10-06-ipad-standalone-1a-socle-sqlite.md`

Base de référence (avant tâche 1) : PosKit 154 · .NET 417 (15 Domain + 252 Infrastructure + 150 Api) · web <non exécuté, inchangé>

| Tâche | Statut | PosKit | .NET | Web | Findings de revue ouverts |
|---|---|---|---|---|---|
| 1 Enveloppe SQLite | fait | 160 | inchangé | inchangé | Mineurs différés (voir ci-dessous) : blob vide lié en NULL, pas de contrôle du nombre de paramètres, colonnes JOIN dupliquées, tests date/bool absents |
| 2 Schéma v1 et PinHasher | fait | 165 | inchangé | inchangé | Mineurs différés : test de schéma sans colonnes/index ni rollback (avec migration 2) ; dates ISO 8601 ≠ format EF Core |
| 3 Auth et personnel | fait | 182 | inchangé | inchangé | Mineurs différés : noms vides non validés (stores), verrouillage PIN en mémoire, `staff()` non authentifié (parité serveur) |
| 4 Catalogue | fait | 188 | inchangé | inchangé | Mineur différé : `updateCategory` sans trim du nom ni normalisation du poste |
| 5 Salle et réglages | fait | 193 | inchangé | inchangé | Mineurs différés : `saveSettings` sans validations .NET (SIRET, TVA, mois/jour), `createTable` sans trim/majuscules, langue par défaut `fr` |
| 6 Grille tactile | fait | 200 | inchangé | inchangé | Mineurs différés : 4×4 sans pagination/héritage des dimensions, cases vides non matérialisées, doublons/hors-plage → erreur d'index unique, swap source absente → 200 au lieu de 404, dimensions ≤ 0 non gardées |
| 7 Persistance et docs | fait | 203 | inchangé (build seul vérifié) | inchangé (build seul vérifié) | Mineur différé : commentaire sur les compteurs du test de persistance (cosmétique) |

## Détail des findings différés

- T1 : blob vide lié en NULL ; `try? ROLLBACK` voulu ; pas de contrôle du nombre de paramètres ; colonnes dupliquées d'un JOIN s'écrasent (alias utilisés) ; tests date/bool non exercés.
- T2 : test de schéma sans colonnes/index ni rollback de migration (à ajouter avec la migration 2) ; dates ISO 8601 différentes du format EF Core.
- T3 : noms vides non validés (les stores les rejettent) ; verrouillage PIN en mémoire seulement ; `staff()` volontairement non authentifié (parité serveur : GET /api/staff/operators est AllowAnonymous).
- T4 : `updateCategory` sans trim du nom ni normalisation du poste.
- T5 : `saveSettings` sans validations .NET (SIRET, TVA, mois/jour), `createTable` sans trim/majuscules, langue par défaut `fr`.
- T6 : génération 4×4 sans pagination ni héritage des dimensions ; `saveGridLayout` ne matérialise pas les cases vides, doublons/hors-plage → erreur d'index unique ; swap d'une source absente → 200 au lieu de 404 ; dimensions ≤ 0 non gardées.
- T7 : commentaire sur les compteurs du test de persistance (cosmétique).

## Conditions d'entrée du plan 1c

Non bloquantes pour 1a (le mode n'est pas encore sélectionnable).

1. Verrouillage PIN persistant : aujourd'hui `failedLogins`/`lockedUntil` sont en mémoire, relancer l'app remet le compteur à zéro.
2. Pas de comptes ni d'identité de démo sur une installation réelle (PIN 1234/9999, SIRET 88877766600012) : onboarding à la création.
3. Une seule instance SQLite par fichier, `sqlite3_busy_timeout`, et ne pas ouvrir de transaction quand il n'y a rien à migrer.

En plus : protection de fichier iOS, mapping `SQLiteError` → `APIError` (messages français), garde sur les dimensions de grille, sauvegarde WAL via `sqlite3_backup`/`VACUUM INTO`.
