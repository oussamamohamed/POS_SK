# iPad standalone — design

Date : 2026-10-06 · Statut : en attente de relecture

## Objectif

Un **seul iPad autonome** encaisse en vrai, sans serveur .NET sur le réseau. Tout vit sur l'iPad :
données, fiscal NF525, clôtures, back-office, impression directe.

## Périmètre

Inclus :
- Prise de commande, paiement, tables, catalogue (parité avec le mode serveur).
- Tickets fiscaux NF525 : reçus chaînés SHA-256, annulations en négatif, duplicata, journal technique (JET), immuabilité.
- Clôtures Z (jour, mois, an), archive fiscale, export FEC.
- Back-office local (Gestion) : produits, catégories, tarifs, opérateurs, réglages.
- Impression directe : LAN (TCP 9100), Star (StarIO10), Epson (ePOS SDK).

Hors périmètre : plusieurs iPad, écran cuisine séparé, synchronisation vers un serveur.

## Architecture

```
SwiftUI (inchangé) → stores PosKit → protocole PosAPI
                                      ├─ HTTPPosAPI      (mode serveur, existant)
                                      ├─ InMemoryPosAPI  (tests, existant)
                                      └─ LocalPosAPI     (nouveau, standalone)
                                           ├─ LocalStore (SQLite)
                                           ├─ FiscalEngine (CryptoKit)
                                           ├─ ClosureEngine
                                           └─ PrintService → PrinterTransport
                                                              ├─ LAN TCP 9100
                                                              ├─ StarTransport
                                                              └─ EpsonTransport
```

- Au premier lancement, l'app propose « Serveur » ou « Autonome ». Les deux modes coexistent ; le code serveur n'est pas modifié.
- `PosKit` reste sans dépendance externe. Les SDK Star et Epson vivent dans un module séparé `PosPrinters`.
- Le rendu raster ESC/POS est commun aux trois transports.

## Données

- SQLite, mêmes tables et noms de colonnes que `Domain/Entities`. Choix GRDB ou SQLite brut tranché dans le plan du sous-projet 1.
- Montants en centimes entiers. `LocalPosAPI` réutilise le port Swift existant de `Money.FromDecimal` et les règles de répartition au centime.
- Migrations versionnées par `PRAGMA user_version` (liste ordonnée de scripts SQL, échec explicite, pas de `try/catch` silencieux).
- Terminal : l'iPad est son propre terminal (`T01`), identifiant fixé à l'installation, stocké dans le Keychain. Pas d'appairage.
- Clé de signature : générée au premier lancement, Keychain `kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly`, jamais incluse dans l'export.

## Fiscal

- Une table par chaîne (reçus, clôtures journalières, JET, clôtures de période, archives), insertion seule.
- Triggers SQL qui rejettent `UPDATE` et `DELETE` (équivalent de `FiscalImmutabilityInterceptor`).
- Champs de signature, ordre et formatage : identiques à `docs/fiscal.md`. Départ : `GenesisHash`.
- Annulation : nouveau reçu signé en négatif lié par `VoidedReceiptId`. Duplicata : `DuplicateNumber` + mention « DUPLICATA n°N » + entrée JET.
- **Sauvegarde obligatoire :** l'iPad est le seul dépositaire de la chaîne. Export manuel chiffré de la base et sauvegarde quotidienne automatique vers Fichiers/iCloud.

## Impression

- Protocole `PrinterTransport` ; adaptateurs LAN, Star, Epson.
- Résolution de station inchangée : ligne → produit → catégorie → `HOT_KITCHEN`.
- L'impression ne fait jamais échouer une vente (`printQueued: false` si aucune imprimante résolue), file d'attente locale.
- Bluetooth : iOS n'autorise que BLE ou MFi pour les accessoires tiers ; les SDK Star/Epson gèrent ces cas. Permissions Bluetooth à déclarer dans `Info.plist`.

## Ordre de construction

Un spec, un plan et un cycle d'implémentation par sous-projet.

| # | Sous-projet | Livre | Condition de sortie |
|---|---|---|---|
| 1 | Socle local | `LocalStore`, `LocalPosAPI` sans fiscal, choix Serveur/Autonome | Parcours table complet en mode autonome (tests UI adaptés) |
| 2 | Fiscal NF525 | `FiscalEngine`, reçus, annulations, duplicata, JET, triggers | Hashes identiques au .NET sur la même séquence |
| 3 | Clôtures et archives | Z jour/mois/an, archive, FEC, export via partage | Totaux et signatures identiques au .NET |
| 4 | Impression | `PrinterTransport`, LAN, Star, Epson, rendu ESC/POS | Ticket réel imprimé sur chaque marque |
| 5 | Back-office local | Écrans Gestion branchés sur `LocalPosAPI` | Création produit/opérateur sans serveur |

Tant que le sous-projet 2 n'est pas terminé, le mode autonome est marqué « non fiscal » et ne doit pas servir à encaisser.

## Tests

- Unitaires PosKit (Swift Testing) : logique pure, arrondi, répartition.
- **Parité .NET/Swift** : jeu de scénarios JSON rejoué des deux côtés, séquences fiscales de référence générées par le .NET, comparaison des hashes. Base : `Tests/PosKitTests/Fixtures`.
- Immuabilité : tentatives `UPDATE`/`DELETE` sur les tables fiscales, rejet attendu.
- UI : XCUITest en mode autonome, sur le modèle de `-UITestMode`.
- Impression : comparaison d'octets du rendu raster ; transports Star/Epson testés avec un faux transport ; essais papier sur matériel réel.

## Risques

- **Divergence .NET/Swift :** toute modification fiscale ou d'arrondi côté .NET impose la mise à jour du Swift et des fixtures. À consigner dans `CLAUDE.md`.
- **Certification NF525 :** l'attestation doit couvrir le mode autonome ; à vérifier avant tout usage réel.
- **SDK imprimantes :** dépendances binaires, permissions Bluetooth, matériel MFi pour l'USB.
- **Perte de l'iPad :** sans sauvegarde, la chaîne fiscale est perdue.

## Questions ouvertes (à trancher dans les plans)

- GRDB ou SQLite brut.
- Cible des sauvegardes automatiques (iCloud Drive ou autre).
- Modèles précis Star et Epson à valider sur matériel.
