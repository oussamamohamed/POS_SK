# Spécification de fonctionnalité : Commande et paiement à la table par QR code

**Branche**: `main` (pas de branche dédiée)

**Créé**: 2026-11-04

**Statut**: Brouillon

**Entrée**: « Le client scanne un QR code posé sur la table, consulte la carte, commande, paie et divise l'addition, sans attendre le serveur. »

## Scénarios utilisateur et tests *(obligatoire)*

### Histoire 1 - Consulter la carte depuis la table (Priorité : P1)

Le client scanne le QR code de sa table avec son téléphone. Une page s'ouvre sans installation ni compte : la carte du restaurant, par catégorie, avec prix, photo facultative et allergènes. Les produits en rupture n'apparaissent pas ou sont grisés.

**Pourquoi P1** : c'est la base de tout le parcours, et elle apporte déjà de la valeur seule (carte à jour, allergènes affichés, moins de cartes papier à imprimer).

**Test indépendant** : ouvrir l'adresse du QR code d'une table sur un téléphone ; la carte s'affiche dans la langue du navigateur (FR/EN/AR) avec les prix et les ruptures à jour.

**Scénarios d'acceptation** :

1. **Étant donné** une table active, **quand** le client ouvre son QR code, **alors** la carte s'affiche et indique le numéro de la table.
2. **Étant donné** un produit en rupture, **quand** la carte est affichée, **alors** le produit ne peut pas être ajouté au panier.
3. **Étant donné** un happy hour en cours, **quand** la carte est affichée, **alors** les prix affichés sont ceux du happy hour.

---

### Histoire 2 - Commander depuis la table (Priorité : P1)

Le client compose un panier (avec modificateurs et notes), puis envoie la commande. Elle arrive en cuisine comme une commande du serveur, et le serveur la voit sur la table. Le restaurateur peut choisir que chaque commande client soit d'abord validée par un serveur.

**Pourquoi P1** : c'est le gain principal (prise de commande sans serveur, moins d'erreurs).

**Test indépendant** : passer une commande depuis la page client ; un bon de cuisine apparaît (ou une demande de validation si l'option est active) et la table affiche les lignes.

**Scénarios d'acceptation** :

1. **Étant donné** un panier valide, **quand** le client envoie, **alors** le bon part vers la station du produit (cuisine, bar…) et le client voit « commande envoyée ».
2. **Étant donné** l'option « validation par le serveur », **quand** le client envoie, **alors** la commande attend dans la cloche « à traiter » et ne part en cuisine qu'à la validation.
3. **Étant donné** un produit passé en rupture entre l'ajout et l'envoi, **quand** le client envoie, **alors** la ligne est refusée avec un message clair et le reste du panier est conservé.
4. **Étant donné** une table déjà ouverte par un serveur, **quand** le client commande, **alors** les lignes s'ajoutent à la même commande.

---

### Histoire 3 - Voir l'addition et payer (Priorité : P2)

Le client demande l'addition depuis la page, voit le détail (lignes, TVA, remises, happy hour), choisit un pourboire proposé et paie par carte en ligne. Le ticket fiscal est émis au même titre qu'un paiement en caisse.

**Pourquoi P2** : très démonstratif, mais dépend d'un prestataire de paiement.

**Test indépendant** : avec un prestataire en mode test, payer l'addition entière ; la commande est soldée, un reçu fiscal signé existe et la table passe en « encaissée ».

**Scénarios d'acceptation** :

1. **Étant donné** une commande ouverte, **quand** le client demande l'addition, **alors** la table passe en « addition demandée » et le serveur est notifié.
2. **Étant donné** un paiement accepté, **quand** le prestataire confirme, **alors** un reçu fiscal est émis avec un règlement carte et le client reçoit son reçu (affichage, courriel facultatif).
3. **Étant donné** un paiement refusé ou abandonné, **quand** le client revient, **alors** la commande reste ouverte et aucun reçu n'est émis.
4. **Étant donné** un double envoi de confirmation du prestataire, **quand** il est reçu deux fois, **alors** un seul reçu est émis.

---

### Histoire 4 - Diviser l'addition entre convives (Priorité : P3)

Plusieurs clients de la même table scannent le même QR code, choisissent les lignes qu'ils paient (ou un partage égal, ou un montant libre) et paient chacun leur part.

**Pourquoi P3** : forte valeur perçue, réutilise les règles de partage à l'euro près déjà présentes.

**Test indépendant** : deux téléphones paient chacun une part ; la somme des reçus égale le total exact de la commande, au centime.

**Scénarios d'acceptation** :

1. **Étant donné** quatre convives et un partage égal d'un total non divisible, **quand** chacun paie, **alors** les parts diffèrent d'un centime au plus et leur somme est exacte.
2. **Étant donné** une ligne déjà payée par un convive, **quand** un autre tente de la payer, **alors** la ligne est refusée.

---

### Cas limites

- QR code d'une table inexistante, désactivée ou restaurant fermé : message clair, aucune commande possible.
- Client qui scanne le QR code d'une autre table photographié à distance : voir FR-009.
- Pic de commandes simultanées sur une même table : aucune ligne perdue ni doublée.
- Perte de connexion du téléphone en cours d'envoi : pas de commande doublée au renvoi.
- Serveur injoignable (mode autonome de l'iPad) : la fonction est indisponible et la page l'indique.
- Commande client sur une table déjà soldée ou libérée : refusée.
- Allergènes : la carte affiche ceux du produit mais ne garantit pas l'absence de traces (mention légale configurable).

## Exigences *(obligatoire)*

### Exigences fonctionnelles

- **FR-001** : Le système DOIT générer un QR code par table, imprimable depuis Gestion, qui mène à la page client de cette table.
- **FR-002** : La page client DOIT fonctionner sur un navigateur mobile sans installation ni compte, en FR, EN et AR (y compris chiffres arabes et sens d'écriture).
- **FR-003** : La carte DOIT refléter en temps réel les prix (dont happy hour), les ruptures et les produits masqués.
- **FR-004** : Le client DOIT pouvoir choisir les modificateurs du produit, avec les mêmes règles de validation que le serveur.
- **FR-005** : Une commande client DOIT suivre le même routage de stations (produit, catégorie, cuisine chaude par défaut) que celle d'un serveur.
- **FR-006** : Le restaurateur DOIT pouvoir exiger la validation de chaque commande client par un serveur.
- **FR-007** : Le serveur DOIT voir en direct les commandes client sur la table et dans la cloche « à traiter ».
- **FR-008** : Le client DOIT pouvoir demander l'addition, ce qui positionne la table en « addition demandée ».
- **FR-009** : Le système DOIT empêcher l'usage abusif d'un QR code : jeton de table non devinable, expiration de la session de table à la libération de la table, limitation de débit par session.
- **FR-010** : Le paiement en ligne DOIT émettre un reçu fiscal signé par le même chemin que le paiement en caisse, rattaché à un poste de caisse désigné, avec règlement carte.
- **FR-011** : Un événement de paiement reçu plusieurs fois DOIT produire un seul reçu (idempotence).
- **FR-012** : Le partage de l'addition DOIT garantir une somme exacte au centime et refuser le paiement d'une ligne déjà réglée.
- **FR-013** : Le système NE DOIT PAS enregistrer ni transmettre de données de carte bancaire : elles restent chez le prestataire de paiement.
- **FR-014** : Chaque action client (commande, demande d'addition, paiement) DOIT apparaître au journal technique avec l'origine « client QR ».
- **FR-015** : Les pourboires proposés au paiement DOIVENT alimenter le total et le détail par serveur existants.

### Entités principales

- **Jeton de table** : identifiant opaque rattaché à une table, valable tant que la table est occupée ; permet d'ouvrir la page client.
- **Session client** : visite d'un téléphone sur une table ; porte le panier non envoyé et la limitation de débit.
- **Commande client** : lignes envoyées depuis la page, avec statut (à valider, envoyée, refusée) et origine.
- **Paiement en ligne** : tentative de règlement (montant, lignes visées, statut, référence du prestataire) menant à zéro ou un reçu fiscal.

## Critères de réussite *(obligatoire)*

### Résultats mesurables

- **SC-001** : Un client passe de l'ouverture du QR code à l'envoi d'une commande de 3 produits en moins de 2 minutes.
- **SC-002** : 95 % des commandes client arrivent en cuisine en moins de 5 secondes après l'envoi.
- **SC-003** : 100 % des paiements en ligne confirmés produisent exactement un reçu fiscal, et la chaîne NF525 reste valide.
- **SC-004** : Pour tout partage entre 2 et 10 convives, la somme des parts est égale au total de la commande, au centime.
- **SC-005** : Pendant un service de démonstration, au moins la moitié des commandes d'une table test peuvent être passées sans serveur.

## Hypothèses

- Le prestataire de paiement en ligne est à choisir (Stripe, SumUp, Adyen…) ; la spec reste neutre et prévoit un mode test.
- La page client est servie par le même serveur que le client web. Le mode autonome de l'iPad n'est pas concerné : sans serveur, pas de QR code.
- Les allergènes sont fournis par la spec 023 (stock, fiches techniques et allergènes) ; sans elle, la carte s'affiche sans allergènes.
- Les photos de produit sont facultatives et hors périmètre du premier lot.
- La langue de la page suit le navigateur, avec le français par défaut.
- Le périmètre exclut la fidélité, les cartes cadeaux et la commande à emporter en ligne (listés pour une spec ultérieure).
