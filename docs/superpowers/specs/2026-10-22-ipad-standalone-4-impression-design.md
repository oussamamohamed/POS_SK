# iPad autonome, sous-projet 4 : impression

Suite de `2026-10-06-ipad-standalone-design.md` (ordre de construction, ligne 4) et de `2026-10-13-ipad-standalone-3-clotures-archives-design.md`. Référence .NET : `docs/impression.md`, `docs/superpowers/specs/2026-09-29-impression-design.md` et `2026-09-30-impression-texte-rapports-design.md`.

## Objectif

Le mode autonome de l'iPad imprime sans serveur les mêmes documents que le .NET : tickets de caisse, bons de retrait, bons cuisine par poste, duplicata, rapports X et Z, clôtures de période, page de test. Imprimantes LAN (ESC/POS, TCP 9100), Star (StarIO10) et Epson (ePOS SDK). L'impression ne fait jamais échouer une vente. Critère de sortie (spec maître) : un ticket réel imprimé sur chaque marque.

Acquis avant ce sous-projet : CRUD des imprimantes (`LocalPrinterRepository`, table `PrinterConfigurations`, V4), écran Gestion partagé avec le mode serveur, duplicata numéroté et consigné au JET (`printQueued` toujours faux), clôtures et archives (sous-projet 3).

## Hors périmètre

- Impression des archives ZIP et du FEC (ce sont des fichiers partagés, pas des tickets).
- Back-office local complet et écran de restauration (sous-projet 5). Seul l'écran Imprimantes est complété ici.
- AirPrint et impression système.
- Essai papier automatisé : il se fait sur le matériel de l'utilisateur.

## Décisions

- **Preuve de parité.** Le rendu raster du .NET utilise SkiaSharp, HarfBuzz et Noto ; CoreText ne produira pas les mêmes octets (lissage, seuil 1 bit). La parité porte donc sur :
  1. le `TicketDocument` (lignes, montants, mentions, langue, sens RTL), comparé en JSON à une fixture **générée par le .NET** (`TicketDocumentJson` existe déjà) ;
  2. le rendu **texte** ESC/POS, identique octet pour octet ;
  3. le raster : dimensions, commandes `GS v 0`, découpes ; l'aspect se valide sur papier.
- **Polices.** Noto Sans et Noto Sans Arabic (mêmes fichiers `.ttf` que le .NET, licence OFL) embarqués dans l'app et enregistrés pour CoreText.
- **Colonnes propres à l'iPad** dans `PrinterConfigurations` : type de transport (`Lan` par défaut, `Star`, `Epson`) et identifiant du périphérique (Bluetooth, USB, ou adresse du SDK). Absentes de l'entité .NET, comme `DailyFiscalClosures.CreatedAtUtc` ; les imprimantes LAN restent lisibles par le serveur. À consigner dans `CLAUDE.md`.
- **Table `PrintJobs`** : mêmes colonnes que l'entité `PrintJob` du .NET. Migration V10, jamais de modification d'un script publié. Pas de trigger d'immuabilité (ce n'est pas une pièce fiscale).
- **SDK.** `PosKit` reste sans dépendance externe. Star et Epson vivent dans un module SPM séparé `PosPrinters` qui implémente le protocole `PrinterTransport` de `PosKit`. StarIO10 vient de SPM ; le SDK Epson ePOS n'a pas de distribution SPM publique, son xcframework est pris en local (licence à vérifier). Si Epson bloque, 4c livre Star et laisse Epson derrière un protocole prêt.
- **Reprise.** Au lancement, les jobs `Pending` sont repris. Imprimante désactivée ou supprimée : ses jobs `Pending` passent `Cancelled`.

## Découpage en lots

Un lot = une branche, un plan `docs/superpowers/plans/…-4x-…`, un fichier de suivi `docs/plans/ipad-standalone-4x-progress.md`, une revue. Ordre imposé.

### 4a. Document et rendu

- Port de `TicketDocumentBuilder` (ticket, bon cuisine, bon de retrait, X, Z, duplicata, clôture de période, test), `TicketDocument` et `TicketDocumentJson`.
- Rendu texte : port de `EscPosTextRenderer` et `EscPosCommands`.
- Rendu raster : `TicketRasterRenderer` (CoreText + CoreGraphics, bidi et liaison arabe), `MonoImage`, découpes `GS V`, 384 ou 576 points.
- **Preuve** : `ticket_document_reference.json` généré par le .NET (`WRITE_TICKET_FIXTURE=1 dotnet test … --filter …`), rejoué par les tests Swift ; octets texte identiques ; tests raster sur dimensions et commandes.

### 4b. File et moteur

- Migration V10 : `PrintJobs` et colonnes de transport.
- Port de `PrintDispatcher` : résolution du poste (ligne, article, famille, `HOT_KITCHEN`), imprimante de ticket, un bon par poste, une copie par imprimante, tiroir si espèces et imprimante avec tiroir.
- Port de `PrintQueueProcessor` : FIFO par imprimante, un job en attente de relance bloque les suivants de la même imprimante ; relances +5 s, +15 s, +30 s puis 60 s ; échéance 30 min ; purge quotidienne à 7 jours (les `Failed` restent).
- Protocole `PrinterTransport` ; transport LAN (Network.framework, connexion 3 s, écriture 10 s) ; suivi d'état (`PrinterStatusTracker`).
- Branchement de `printQueued` sur paiement à table, vente comptoir, envoi cuisine, duplicata, rapports X et Z, clôtures. `testPrinter`, `printerStatuses`, `printJobs`, `retryPrintJob`, `cancelPrintJob` deviennent réels.
- **Preuve** : tests de file avec faux transport et horloge injectée ; scénarios du .NET (`docs/impression.md` §3) rejoués ; l'échec d'impression ne change pas le résultat d'une vente.

### 4c. Star et Epson

- Module `PosPrinters` : `StarTransport` (StarIO10) et `EpsonTransport` (ePOS), même protocole que le LAN.
- Recherche des périphériques (réseau, Bluetooth, USB/MFi), permissions Bluetooth dans `Info.plist`.
- **Preuve** : tests avec faux SDK derrière un protocole d'abstraction ; build de l'app avec les deux modules ; essai papier par l'utilisateur sur chaque marque.

### 4d. Interface

- Écran Imprimantes complété : type de transport, recherche, identifiant du périphérique, état en ligne ou hors ligne, jobs en attente et en échec avec relance et annulation, impression de test.
- Chaînes FR / EN / AR (jeux de clés identiques) et XCUITest en `-UITestLocal`.
- **Preuve** : tests PosKit des stores ; XCUITest : ajouter une imprimante, imprimer la page de test avec un transport factice, voir le job passer à envoyé.

## Risques

- **SDK Epson** : distribution binaire locale, licence, taille de l'app. Plan B décrit dans les décisions.
- **Raster CoreText** : aspect différent du .NET, notamment sur l'arabe ; mitigation : polices Noto identiques et essai papier avant livraison de 4a.
- **Matériel** : Star et Epson ne sont validés que sur le matériel de l'utilisateur ; le code est livré testé avec faux transport.
- **Bluetooth iOS** : seuls BLE et MFi sont autorisés pour les accessoires tiers ; les SDK gèrent ces cas.

## Règle de suivi .NET / Swift

Toute modification de `TicketDocumentBuilder`, `EscPosTextRenderer`, `PrintDispatcher`, `PrintQueueProcessor` ou de l'entité `PrintJob` côté .NET impose de régénérer `ticket_document_reference.json` et de reporter le changement dans `Local/`. À ajouter à `CLAUDE.md` à la fin du lot 4d.
