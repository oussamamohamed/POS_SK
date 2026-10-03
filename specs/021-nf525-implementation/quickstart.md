# Validation manuelle : NF525

Toujours sur une **base jetable** : les reçus, clôtures et archives ne s'effacent pas.

```bash
DB=/tmp/nf525-check.db
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://0.0.0.0:5080 \
ConnectionStrings__DefaultConnection="Data Source=$DB" \
Jwt__Secret="SuperSecretKeyForRestaurantPosSystemThatIsAtLeast32BytesLong!" \
dotnet run --project src/RestaurantPos.Api -- --seed-test-data
```

Connexion gérant : PIN `1234`. Appairer l'appareil depuis Gestion → Appareils avant d'encaisser.

## 1. Vérification et altération (US1, SC-001)

1. Encaisser deux tickets, puis Fiscal → « Vérifier l'intégrité » : tout est intègre.
2. Altérer un montant :
   ```bash
   sqlite3 $DB "UPDATE FiscalReceipts SET TotalTtcAmount = TotalTtcAmount + 1 WHERE SequenceNumber = 1;"
   ```
3. Relancer la vérification : chaîne `receipts` invalide, `kind = signature_mismatch`, référence du reçu 1 ; une entrée `CHAIN_BREAK_DETECTED` apparaît dans le journal.

## 2. JET (US2)

Se connecter, changer un taux de TVA, faire une Z : trois entrées chaînées (`LOGIN_SUCCEEDED`, `TAX_RATE_CHANGED`, `Z_CLOSURE`). Puis :

```bash
sqlite3 $DB "UPDATE TransactionJournalEntries SET PayloadJson='{}' WHERE ChainSequence=2;"
```

La vérification signale la chaîne `journal`.

## 3. Annulation (US3)

Noter la ligne du reçu (`sqlite3 $DB "SELECT * FROM FiscalReceipts WHERE SequenceNumber=1;"`), l'annuler depuis l'appareil émetteur, relire la ligne : identique. Rapport X et tableau de bord : mêmes totaux qu'avant ce lot pour le même scénario.

## 4. Clôture mensuelle (US4, SC-003)

Sur une base avec trois Z datées du mois précédent : clôturer le mois → totaux = somme des trois Z au centime. Recommencer → `409 period_already_closed`. Sur une nouvelle base où un jour avec reçus n'a pas de Z → `409 missing_daily_closures` avec ce jour.

## 5. Duplicata (US6)

Réimprimer deux fois un ticket : « DUPLICATA n°1 » puis « n°2 », deux entrées `DUPLICATE_PRINTED`.

## 6. Archive (US5, SC-004)

Archiver la clôture mensuelle, télécharger le ZIP, le vérifier → intègre. Modifier un octet :

```bash
printf '\x00' | dd of=archive.zip bs=1 seek=100 conv=notrunc
```

La vérification répond `hash_mismatch`.

## 7. Identité (US7)

Changer la raison sociale dans Gestion → l'en-tête du prochain ticket la reprend ; entrée `FISCAL_SETTINGS_CHANGED`.

## 8. FEC (SC-006)

Exporter un mois (`GET /api/fiscal/fec?from=…&to=…`) et le passer dans l'outil DGFiP « Test Compta Demat ».
