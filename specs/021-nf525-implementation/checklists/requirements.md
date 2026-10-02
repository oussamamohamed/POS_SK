# Checklist qualité de la spec : NF525 — compléter la conformité

**Objet** : valider la complétude et la qualité de la spec avant les tâches
**Créée** : 2026-09-30 (révisée après clarifications et plan)
**Spec** : [spec.md](../spec.md)

## Qualité du contenu

- [x] Centrée sur la valeur métier (contrôle, clôtures, preuve)
- [x] Sections obligatoires complètes
- [x] Sans détail d'implémentation — volontairement non : « Contexte » et invariants citent le code existant pour empêcher de le refaire ou de casser les chaînes

## Complétude des exigences

- [x] Aucun marqueur [NEEDS CLARIFICATION] (4 clarifications du 2026-09-30 intégrées)
- [x] Exigences testables
- [x] Critères de succès mesurables
- [x] Scénarios d'acceptation définis
- [x] Cas limites identifiés
- [x] Périmètre borné (section « Hors périmètre »)
- [x] Références réglementaires vérifiées (research R9) : attestation individuelle rétablie par la LF 2026, conservation 6 ans
- [x] Table officielle des codes d'événements JET (NF525 R19) et exigences de signature des archives : à obtenir auprès de l'organisme certificateur avant US2 et US5

## Prête pour les tâches

- [x] Plan aligné sur l'architecture réelle (SQLite, Minimal API, services, SQL idempotent dans `Program.cs`)
- [x] Numéro de certificat : facultatif tant que la certification n'est pas obtenue, imprimé dès qu'il est renseigné (spec US7.2 et research R7 alignés)

## Notes
- Plan, research, data-model, contracts et quickstart réécrits le 2026-09-30 sur le code réel (la version générée reprenait PostgreSQL, MediatR et une entité de clôture unique qui cassait la chaîne Z).
