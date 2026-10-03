# Passation — NF525 (021)

**Branche** : `021-nf525-implementation` (depuis `main` @ abb7b78). T001 fait.
**Base verte** (build 0 warning/0 erreur ; tests Domain 15, Infrastructure 214, Api 110 : tous OK).

## Lire dans cet ordre
1. `/CLAUDE.md` (source de vérité ; la constitution `.specify/` est périmée)
2. `spec.md` (invariants INV-1..4) → `plan.md` → `research.md` → `data-model.md` → `contracts/nf525-api.md` → `tasks.md`

## Où commencer
Lot A (P1) : Phase 2 (T004–T006) puis US2 → US3 → garde INV-3 → US1. TDD : test rouge, puis code.

## Pièges
- Jamais modifier la formule de hash des reçus/Z (INV-1) : T005 le verrouille.
- Pas de migrations : chaque colonne/table = SQL idempotent dans `Program.cs`.
- `dotnet format RestaurantPos.slnx` avant build ; il touche ~5 fichiers sans rapport (dérive existante) : les revert.
- Garde anti-modification (R5) seulement APRÈS US3 (plus aucune écriture de `IsVoid`).
- Environnement `Testing` = EF InMemory : pas de SQL brut dans le chemin nominal.
- Parité web (`app.js`, i18n en/fr/ar) + iPad (PosKit, fixtures recapturées sur base jetable).

## Bloquants externes
- T002 : table officielle des codes JET + exigences de signature d'archive (organisme certificateur). Bloque T012 et T061 ; utiliser les noms de `data-model.md` en attendant.
- T003 : noms réels des tables SQLite à relever.

## Hors de cette branche
PR du fix `fix/tableau-de-bord-annulations` (poussée, PR non ouverte).
