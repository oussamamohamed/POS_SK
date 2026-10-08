# iPad standalone 1c — suivi d'exécution

Plan : `docs/superpowers/plans/2026-10-07-ipad-standalone-1c-paiement-comptoir.md`

Base de référence (avant tâche 1) : PosKit 244 · .NET et web inchangés (aucun fichier .NET ni web modifié)

| Tâche | Statut | PosKit | Findings de revue ouverts |
|---|---|---|---|
| 1 Migration v3 et dépôts | fait | 251 | |
| 2 Garde de statut et PIN partagé | fait | 252 | |
| 3 Paiement des tables | fait | 259 | |
| 4 Comptoir et mise en attente | fait | 266 | |
| 5 Encaissement comptoir | fait | 274 | |
| 6 Chambres d'hôtel | fait | 278 | |

Conditions d'entrée du plan 1d (le mode devient sélectionnable : ces points ne sont plus « sans visiteur ») :
1. Verrouillage des PIN persistant (aujourd'hui en mémoire : relancer l'app remet le compteur à zéro) — reprendre `docs/plans/ipad-standalone-1a-progress.md`.
2. Pas de comptes ni d'identité de démonstration sur une installation réelle (PIN 1234 et 9999, SIRET de démo, chambres de démo) : création d'un responsable et de l'identité de l'établissement au premier lancement.
3. Une seule instance SQLite par fichier, `sqlite3_busy_timeout`, protection de fichier iOS, sauvegarde sûre avec le mode WAL.
4. Le terminal autonome s'appelle `T01` : les numéros de reçu `NF-T01-…` et de retrait `#A-…` en dépendent.
5. Les règlements `NonFiscalPayments` sont provisoires : le sous-projet 2 les remplace par `FiscalReceipts`/`PaymentTenders` signés (migration dédiée), et l'interface doit marquer le mode « non fiscal » tant que ce n'est pas fait.
