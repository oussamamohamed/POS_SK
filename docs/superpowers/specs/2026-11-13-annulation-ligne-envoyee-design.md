# Annulation d'une ligne envoyée en cuisine, correction d'une ligne non envoyée : conception

Statut : proposé (à approuver avant tout plan). Date : 2026-11-13.

## Intention

Un serveur se trompe : un plat envoyé en cuisine doit être retiré. Aujourd'hui rien ne le permet. Le serveur ne fait qu'ajouter des lignes (`IsDispatched` verrouille la ligne), le web et l'iPad affichent « ligne déjà envoyée », et le seul geste possible est l'offert (`compItem`), qui laisse l'article au ticket. `User.CanVoidItems()` (manager ou admin) existe et n'a aucun appelant.

Deux gestes distincts, décidés avec le responsable :

- **Annulation** : la ligne est **déjà envoyée** en cuisine. Réservée au manager ou à l'admin, avec motif, trace au JET, article de bon marqué annulé à l'écran cuisine et **bon d'annulation imprimé** sur l'imprimante du poste concerné.
- **Correction** : la ligne **n'est pas encore envoyée**. N'importe quel opérateur connecté, sans motif, sans bon, sans JET. La ligne est simplement retirée ou diminuée.

Valable pour une table comme pour un ticket direct (le comptoir est la table « Comptoir » : une seule route couvre les deux).

## Objectifs

- Retirer, en tout ou en partie (quantité), une ligne envoyée, avec motif et identité de l'opérateur conservés.
- Prévenir la cuisine par l'écran (KDS) et par un bon imprimé.
- Rendre possible la correction d'une ligne enregistrée mais pas encore envoyée (aucune route ne retire une ligne enregistrée aujourd'hui).
- Même fonction sur le serveur .NET + web, sur l'iPad en mode serveur et sur l'iPad en mode autonome (parité exigée par `CLAUDE.md`).
- Rendre utilisable l'annulation d'un reçu partiel (correction de M2 côté .NET et de E14 côté iPad autonome) : sans cela, le chemin « annuler le reçu puis la ligne » est impossible.

## Hors périmètre

- Retirer une ligne d'une commande qui a un reçu non annulé : refus 409 avec le numéro du reçu (voir « Commande avec reçu »). Le remboursement d'une carte reste manuel tant que le remboursement TPE n'est pas fait (point n° 2 de la revue des fonctions manquantes).
- Annulation par un serveur avec saisie du PIN d'un manager : possible plus tard avec `SupervisorPinGuard`, pas ici. Aujourd'hui le manager ou l'admin doit être l'opérateur connecté.
- Remboursement TPE, stock (la consommation a lieu au règlement : rien à restituer avant paiement, à vérifier dans `StockConsumption`).
- Les brouillons purement locaux (sans identifiant serveur) restent corrigés localement, comme aujourd'hui.

## À éviter absolument

- Effacer une ligne envoyée sans trace.
- Toucher à un reçu fiscal ou aux totaux fiscaux après coup : la ligne est retirée **avant** tout reçu, donc les fixtures fiscales ne changent pas.
- Une cuisine qui prépare un plat annulé sans le savoir.

## Contraintes

- NF525 : l'annulation est un événement du JET (`ORDER_LINE_VOIDED`), écrit dans la même transaction que le retrait.
- Schéma .NET sans migration EF : `ApplySchema` pour les colonnes et tables ajoutées. Côté iPad autonome, nouvelle migration (V17), jamais de modification d'un script publié, et mêmes tables et colonnes que `AppDbContext`.
- `KitchenTicketItem` n'a **aucun lien** vers la ligne de commande (le code retrouve le poste par `ProductId`, commentaire `ponytail` de `TicketDocumentBuilder`). Il faut donc un lien `OrderItemId` (nul pour l'historique) pour savoir quel article de bon marquer. Les lignes envoyées ne fusionnent jamais (condition `!IsDispatched` à la fusion) : une ligne égale un article de bon.
- Les états de bon (`TicketItemStatus`) sont décodés côté Swift : ajouter une valeur impose de mettre à jour le modèle Swift et les fixtures capturées.
- Impression : un nouveau type de document impose les clés `.resx` FR/EN/AR, `ios/scripts/gen_ticket_texts.py`, la régénération de `ticket_document_reference.json` (`TZ=Europe/Paris WRITE_TICKET_FIXTURE=1 dotnet test tests/RestaurantPos.Infrastructure.Tests --filter TicketDocumentReferenceFixtureTests`) et le report dans `ios/Packages/PosKit/Sources/PosKit/Printing/`. Aucun vrai transport d'imprimante en test UI. L'impression ne fait jamais échouer l'opération (`printQueued: false` sans imprimante).
- i18n FR/EN/AR (chiffres arabes), échappement HTML dans le web, jetons sur les hubs.

## Approches envisagées

1. **A. Retirer la ligne et garder une trace à part (retenue).** La ligne disparaît de la commande, donc totaux, TVA et fixtures fiscales ne bougent pas. Une copie (article, quantité, prix, motif, opérateur, date) va dans une table `OrderLineVoids` et dans le JET. Coût : une table, un état de bon, un document imprimé, une migration iPad.
2. **B. Ligne négative (contre-passation).** Aucune table et le circuit cuisine existant sert tel quel, mais le panier affiche l'article et son annulation côte à côte, et on ne sait pas si le code accepte partout une quantité négative. Écartée : sémantique floue.
3. **C. Drapeau « annulé » sur la ligne.** La trace reste dans la commande, mais chaque calcul (total, TVA, stock, rapports, miroirs Swift) doit l'ignorer. Écartée : trop de surface pour le bénéfice.

## Conception retenue

**Route.** `POST /api/orders/{orderId}/items/{itemId}/void` avec `{ quantity, reason }`, sur le modèle de `/comp` (`HospitalityEndpoints`).
- Ligne **non envoyée** : tout opérateur authentifié ; motif ignoré ; retrait de la quantité demandée, sans trace.
- Ligne **envoyée** : `CanVoidItems()` exigé (403 sinon), motif obligatoire, quantité entre 1 et la quantité de la ligne.
- Refus 409 si la commande a un reçu non annulé, si la commande n'est plus réglable, ou si la quantité dépasse celle de la ligne.

**Effets d'une annulation (une seule transaction) :**
1. Quantité de la ligne diminuée de `quantity`, ligne supprimée si elle tombe à zéro.
2. Ligne écrite dans `OrderLineVoids` ; événement JET `ORDER_LINE_VOIDED` (commande, table, article, quantité, prix, motif, opérateur, poste, identifiant du bon).
3. Article du bon cuisine : `CancelledQuantity` augmente ; état « annulé » quand tout est annulé ; mise à jour poussée à l'écran cuisine par le mécanisme existant.
4. Après le `COMMIT` : un bon d'annulation est mis en file sur la ou les imprimantes du poste de l'article (même résolution que l'envoi : article, produit, catégorie, `HOT_KITCHEN`).

**Bon d'annulation (croquis).**
```
*** ANNULATION ***
Table 12   Serveur : Sophie
Annulé par : Alexandre (Manager)   19:42
 -1  Entrecôte  (cuisson : saignant)
Motif : erreur de saisie
```
Langue : `KitchenTicketLanguage` de l'établissement, comme le bon cuisine. Pas de tiroir-caisse.

**Mode autonome de l'iPad.** Même logique dans `LocalPosAPI` (`+Orders`, `+Kitchen`, `+Discounts`), table `OrderLineVoids` et colonnes `KitchenTicketItems.OrderItemId` / `CancelledQuantity` par la migration V17, JET dans la même transaction, bon mis en file après le `COMMIT`.

**Clients.** Web et iPad : sur une ligne envoyée, un bouton « Annuler l'article » (manager ou admin) ouvre la saisie du motif et de la quantité, comme l'offert ; sur une ligne enregistrée non envoyée, le retrait ou la diminution n'affiche plus « ligne déjà envoyée » et appelle la route. Un serveur qui touche une ligne envoyée voit que l'annulation est réservée au manager.

## Commande avec reçu (choix A du responsable)

Un reçu verrouille la commande. L'annulation d'une ligne envoyée est refusée par un 409 (code `order_has_receipts`, numéros des reçus non annulés dans le message). Pour corriger : le manager annule le reçu depuis le poste qui l'a émis (JET `RECEIPT_VOIDED`), annule la ligne, puis ré-encaisse. Pourquoi pas autrement : un reçu partiel est une part en euros, sa TVA est répartie au prorata de la ventilation de la commande **au moment du reçu** (`ReceiptTaxCalculator`) ; retirer une ligne ensuite casserait « la somme des reçus colle à la commande », dont dépendent les fixtures fiscales et le FEC.

**Prérequis inclus dans ce lot : M2 et E14.** Aujourd'hui annuler un reçu, même partiel, passe toute la commande en `Cancelled` (`CheckoutPaymentService`, ligne 316 ; miroir `LocalPosAPI+Fiscal.voidReceipt`), et une commande `Cancelled` n'est plus encaissable. Sur l'iPad autonome, la table pointe en plus toujours dessus et bloque la clôture Z. Règle proposée :
- Le reçu annulé **couvrait toute la commande** (TTC du reçu égal au TTC de la commande, vente réglée d'un seul reçu) : la commande passe `Cancelled` comme aujourd'hui, et la table qui pointe dessus est libérée (E14, iPad).
- Le reçu annulé était **une part** (TTC inférieur à celui de la commande) : la commande revient à `BillRequested`, la table reste occupée, le reste dû est recalculé.
- Le contrôle des commandes ouvertes de la clôture Z ignore les commandes `Cancelled` (iPad ; le .NET le fait déjà).
- Le stock n'est restitué que si le responsable le demande (inchangé).

À vérifier par le réalisateur : que l'état de la commande n'entre dans aucune fixture fiscale (`fiscal_reference.json`, journée simulée `day_scenario.json` et `LocalDaySimulationTests` contiennent-ils des annulations ?), et que la libération de la table côté .NET se fait déjà.

## Mes choix, à vérifier

- Droit de rôle (`CanVoidItems`), sans PIN supplémentaire.
- Motif obligatoire pour l'annulation, absent pour la correction.
- Annulation partielle de la quantité autorisée.
- Refus s'il existe un reçu non annulé sur la commande (choix A du responsable).
- **À valider avec le responsable :** après l'annulation d'un reçu, la commande est `Cancelled` seulement si ce reçu couvrait toute la commande ; sinon elle redevient encaissable (`BillRequested`). La revue proposait « `Cancelled` si plus aucun reçu actif » ; je préfère ma règle, car annuler le premier règlement d'une note partagée ne doit pas annuler la vente des autres convives.
- Pas de trace au JET pour une correction (ligne jamais envoyée).
- Le bon d'annulation réutilise le type de job `KitchenTicket` (évite un nouveau type côté Swift), avec un document distinct.

## Laissé au réalisateur

- Noms exacts (types, colonnes, clés de texte) et mise en page du bon, dans l'esprit du croquis.
- Où placer le bouton dans chaque écran, en suivant l'offert.
- Que devient une commande dont toutes les lignes sont annulées (probablement : reste ouverte et vide, la table se libère par le chemin existant) : à vérifier dans `closeOrder` et `TableManagementService`.
- Vérifier qu'aucun parcours n'écrit de reçu entre le contrôle et le retrait (même transaction).
- Lignes envoyées avant cette fonction : leur article de bon n'a pas d'`OrderItemId` (nul). Proposition : le bon d'annulation s'imprime quand même et le JET est écrit, mais aucun article de bon n'est marqué annulé (pas de rapprochement par `ProductId`, trop ambigu).
- Envoi en cuisine et correction simultanés : relire `IsDispatched` dans la transaction pour trancher entre correction et annulation.

## Tests attendus

- .NET : service et route (annulation, correction, droits, reçu existant → 409, quantité partielle, JET, état du bon), fixture de ticket régénérée.
- Swift : miroir dans `Tests/PosKitTests/Local/` (annulation, JET, migration V17), `Printing/` (bon d'annulation contre la fixture .NET).
- M2 et E14 : annuler un reçu partiel rouvre la commande (.NET et Swift) ; annuler un reçu qui couvrait toute la commande l'annule ; sur l'iPad, règlement partiel, annulation, puis clôture Z qui passe ; annulation de ligne refusée tant que le reçu existe, puis acceptée une fois le reçu annulé.
- Web (Playwright) : annulation par un manager, refus d'un serveur, correction d'une ligne enregistrée.
- iPad : XCUITest du parcours manager ; contrat Swift pour le nouvel état de bon.
- Suite complète avant chaque tâche terminée ; suivi dans `docs/plans/annulation-ligne-progress.md` ; `docs/parite-dotnet-swift.md` mis à jour.
