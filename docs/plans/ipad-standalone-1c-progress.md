# iPad standalone 1c — suivi d'exécution

Plan : `docs/superpowers/plans/2026-10-07-ipad-standalone-1c-paiement-comptoir.md`

Base de référence (avant tâche 1) : PosKit 244 · .NET et web inchangés (aucun fichier .NET ni web modifié)

| Tâche | Statut | PosKit | Findings de revue ouverts |
|---|---|---|---|
| 1 Migration v3 et dépôts | fait | 251 | Différés acceptés : `HotelRooms.RoomNumber` non unique (M6, à traiter avec l'accueil) ; force-unwrap UUID au décodage des attentes. |
| 2 Garde de statut et PIN partagé | fait | 252 | Un login réussi remet à zéro les échecs de PIN superviseur (M1, comme .NET) : voir condition 1. |
| 3 Paiement des tables | fait | 259 | Terminal par défaut `LOCAL` vs `POS_MAIN_TERM` à la mise en attente (M5) : unifier avec `T01` (condition 4). |
| 4 Comptoir et mise en attente | fait | 266 | Format du snapshot d'attente différent du .NET (synchronisation ultérieure). |
| 5 Encaissement comptoir | fait | 274 | Nouveau numéro de retrait à chaque encaissement partiel (M2, comme .NET) ; seul le premier titre-restaurant validé ; écarts .NET non commentés (200 à montants nuls, opérateur du pourboire, `PickupScheduledAtUtc`). |
| 6 Chambres d'hôtel | fait | 278 | `chargeRoom` sans ligne `NonFiscalPayments` ni pourboire stocké (I6, condition 7) ; chambres seedées seulement sur base vide. |
| Correctifs de la revue finale | fait | 288 | C1, I1–I5, M3, M4, M7 corrigés ; I3 partiel (clôture à solde nul : condition 6). |

Conditions d'entrée du plan 1d (le mode devient sélectionnable : ces points ne sont plus « sans visiteur ») :
1. Verrouillage des PIN persistant (aujourd'hui en mémoire : relancer l'app remet le compteur à zéro) — reprendre `docs/plans/ipad-standalone-1a-progress.md`.
2. Pas de comptes ni d'identité de démonstration sur une installation réelle (PIN 1234 et 9999, SIRET de démo, chambres de démo) : création d'un responsable et de l'identité de l'établissement au premier lancement.
3. Une seule instance SQLite par fichier, `sqlite3_busy_timeout`, protection de fichier iOS, sauvegarde sûre avec le mode WAL.
4. Le terminal autonome s'appelle `T01` : les numéros de reçu `NF-T01-…` et de retrait `#A-…` en dépendent.
5. Les règlements `NonFiscalPayments` sont provisoires : le sous-projet 2 les remplace par `FiscalReceipts`/`PaymentTenders` signés (migration dédiée), et l'interface doit marquer le mode « non fiscal » tant que ce n'est pas fait.
6. Clôture d'une commande à solde nul (100 % offerte ou remisée) à concevoir : aujourd'hui tout règlement ≤ 0 est refusé et la table reste occupée.
7. `chargeRoom` n'écrit pas de ligne `NonFiscalPayments` (ni numéro `NF-…`) et ne prend pas le pourboire déjà stocké sur la commande : à reprendre au sous-projet 2 (reçus fiscaux).
