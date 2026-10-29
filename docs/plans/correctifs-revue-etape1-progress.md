# Correctifs de la revue totale, étape 1 : suivi

Plan : `docs/superpowers/plans/2026-10-28-correctifs-revue-etape1.md`. Branche : `fix/revue-etape1` (base `fe4dc18`). Statut : **terminé, en attente de fusion** (revue finale propre après une vague de corrections).

## Tâches

| Tâche | Constats | Commits | Tours de correction |
|---|---|---|---|
| 1. Web : plus de connexion implicite, verrou réel, JWT limité à l'origine | C1, E8 | a5ca0f3, 68f0016, 55bd168, 13900cc | 3 |
| 2. Web : échappement des `innerHTML` | C2 | e24961a | 0 |
| 3. Backend : `RequireAdmin`, contrôle des rôles | E1 | da88fb7, 0e666ff | 1 |
| 4. Backend : amorçage Admin sans PIN connu, scripts de production | C1, E1 | 8caa9c8, e1a5ad3, f50528c, b68d7aa | 3 |
| 5. Backend : garde de PIN superviseur, limiteur à paliers | E2, E3, E4 | 980cf25, a38dab6 | 1 |
| Vague finale (K1, I1 à I4) | revue finale | 2f7b2fd | 1 |

## Tests (état final)

- .NET : Domain 69/69, Api 293/293, Infrastructure 444/445.
- Web (Playwright) : 87 réussis, 1 échec.
- Échecs hors périmètre : `ReportAccuracyTests.FinancialDashboard_DiscountedOrders…` (dépend de la date, échoue aussi sur la base) ; `happy-hour-multiselect.spec.ts` US4 (création de planning, préexistant).

## Décisions (rulings)

- Le PIN d'amorçage fait 4 chiffres exactement (configuré ou généré), car les deux claviers envoient le PIN à 4 chiffres. Le généré n'est jamais 0000, 1234, 2468, 5678 ni 9999. Il est journalisé une fois (Warning) dans un journal en droits 600. À changer après la première connexion.
- Le seed CLI (`--seed-prod-data`, `--seed-test-data`) est refusé hors Development. Le repli CLI du script de seed est supprimé. Le seed de démo se lance à la main : `ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/RestaurantPos.Api -- --seed-test-data`.
- `POST /api/auth/logout` ajouté (supprime le cookie HttpOnly), sinon `/api/auth/me` reste 200 après le verrou.
- Corps de rejet de `/api/auth/override` : `{Message}` seul. Aucun client ne l'utilise.
- Remise à zéro du limiteur par un PIN valide : conservée (exigée par le plan). Un porteur de PIN manager peut alterner échecs et succès.

## Ouvert, à traiter ensuite

- **I5 (non corrigé)** : un manager peut deviner le PIN d'un Admin par l'erreur « PIN déjà utilisé » (création ou modification de personnel), sans limite, avec un 500. Préexistant. Demande une décision sur la règle d'unicité des PIN.
- PIN de 4 à 6 chiffres sur les deux claviers (web : bouton OK ; iPad : `SessionStore.pinLength`), puis relever la longueur d'amorçage.
- `#if DEBUG` seede 1234/2468/9999 quel que soit `ASPNETCORE_ENVIRONMENT` : remplacer par une garde d'environnement.
- Un Admin peut se désactiver ou rétrograder le dernier Admin.
- Parité du mode autonome iPad : `LocalPinGuard` sans escalade (M7, étape 2).
- Déploiements à IP partagée (NAT, proxy, navigateur sur le serveur en 127.0.0.1) : un seul opérateur verrouille tout le restaurant ; un redémarrage du serveur lève les verrous.
- Mineurs listés dans le ledger local (`.superpowers/sdd/2026-10-28-correctifs-revue-etape1/progress.md`, non versionné).
