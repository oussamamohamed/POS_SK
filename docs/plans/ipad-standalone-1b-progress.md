# iPad standalone 1b — suivi d'exécution

Plan : `docs/superpowers/plans/2026-10-07-ipad-standalone-1b-commandes-cuisine.md`

Base de référence (avant tâche 1) : PosKit 203 · .NET et web inchangés (aucun fichier .NET ni web modifié)

| Tâche | Statut | PosKit | Findings de revue ouverts |
|---|---|---|---|
| 1 Migration v2 et dépôts | fait | 208 | aucun |
| 2 Commandes de salle | fait | 219 | aucun |
| 3 Envoi cuisine et bons | fait | 227 | aucun |
| 4 Transfert et fusion | fait | 233 | aucun |
| 5 Remises et gratuités | fait | 240 | aucun bloquant (voir différés) |

Tests : PosKit 240 · .NET build OK (aucun fichier .NET modifié) · web inchangé.

Findings de revue différés (connus) :
- Expiration du verrouillage PIN (plan 1a) non portée.
- Atomicité sous panne disque réelle : couverte par les transactions, non simulée.
- Normalisation `T01 ↔ T1` non portée.
- `removeDiscount` sur commande inconnue : 404 (le .NET lève une exception non gérée), écart assumé.
