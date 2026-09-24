# Feature Specification: Fonctions Populaires d'Encaissement Mobile & Tablette (Restauration & Hôtellerie)

**Feature Branch**: `012-mobile-hospitality-pos-features`  
**Created**: 2026-09-02  
**Status**: Draft  
**Input**: User description: "je veux implémenté les fonctions populaire pour un système d'encaissement sur tablette / mobil pour la restauration et hôtellerie"

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Transfert et Fusion de Tables / Déplacement de Commande (Priority: P1) 🎯 MVP

En tant que serveur ou chef de rang sur tablette mobile, je dois pouvoir déplacer l'intégralité ou une partie de la commande d'une table vers une autre table (ex: déplacement des clients de la terrasse vers l'intérieur en cas d'averse), ou fusionner deux tables occupées qui décident de partager l'addition, afin de maintenir le plan de salle et les notes synchronisés en direct.

**Why this priority**: Le déplacement de clients et le regroupement d'additions sont des opérations quotidiennes en salle et terrasse, évitant les erreurs de service et les confusions de facturation.

**Independent Test**: Ouvrir une commande sur la table `T1` avec 3 articles $\to$ cliquer sur « Transférer / Déplacer » $\to$ sélectionner la table libre `T4` $\to$ valider $\to$ vérifier que la table `T1` devient libre, que la table `T4` devient occupée avec les 3 articles et que le plan de salle reflète le statut immédiatement.

**Acceptance Scenarios**:

1. **Given** une table source occupée avec une commande active (ex: `T1`), **When** le serveur appuie sur l'action « Transférer Table » et sélectionne une table de destination libre (ex: `T4`), **Then** la commande et ses articles sont réaffectés à la table de destination, la table source repasse à l'état `Libre`, et la table de destination passe à `Occupée`.
2. **Given** deux tables occupées (ex: `T2` et `T3`), **When** le serveur choisit l'action « Fusionner Tables » de `T2` vers `T3`, **Then** les lignes d'articles de `T2` sont ajoutées à la commande de `T3`, la table `T2` est libérée, et le total de `T3` est recalculé en incluant l'ensemble des articles consolidés.
3. **Given** une tentative de transfert vers une table inexistante ou déjà occupée sans option de fusion, **When** l'action est déclenchée, **Then** un message d'alerte tactile empêche l'écrasement accidentel.

---

### User Story 2 - Gestion des Remises, Codes Promo et Articles Offerts (Priority: P1)

En tant que responsable de salle ou serveur habilité, je dois pouvoir appliquer une remise en pourcentage ou en valeur monétaire fixe sur un article ou sur l'ensemble de l'addition (ex: Remise Personnel 20%, Geste commercial 10 €, Offert / « Sur le compte de la maison »), avec saisie obligatoire d'un motif, afin de fidéliser la clientèle tout en conservant une traçabilité fiscale stricte.

**Why this priority**: Les gestes commerciaux, remises fidélité et articles offerts font partie intégrante de l'accueil en restauration et doivent être gérés de façon fluide et contrôlée sans bloquer le service.

**Independent Test**: Sur une commande de 45.00 €, sélectionner la pizza à 14.00 € $\to$ appuyer sur « Offert / Geste Commercial » $\to$ motif « Erreur de cuisson » $\to$ vérifier que la ligne passe à 0.00 € avec la mention « Offert », que le sous-total est recalculé à 31.00 € et que le motif est archivé dans le journal d'audit.

**Acceptance Scenarios**:

1. **Given** une commande avec plusieurs articles dans le panier, **When** le serveur sélectionne un article et clique sur « Offert », puis saisit le motif justificatif, **Then** le prix net de l'article passe à 0.00 €, le total TTC de la commande diminue d'autant, et la ventilation de TVA est mise à jour avec précision.
2. **Given** une addition globale, **When** le manager applique une remise de 10% sur l'ensemble de la note avec le motif « Guide Touristique », **Then** une ligne de déduction globale apparaît, le montant total restant dû est recalculé et le détail de la remise est imprimable sur la note proforma.
3. **Given** un serveur sans privilège d'administration tentant d'appliquer une remise supérieure au seuil autorisé (ex: > 20%), **When** la remise est demandée, **Then** un code PIN superviseur est requis pour valider l'opération.

---

### User Story 3 - Prise de Commande par Temps de Service (« Direct », « Suite », « Réclamer Suite ») (Priority: P2)

En tant que serveur prenant la commande à table sur terminal mobile, je dois pouvoir spécifier pour chaque plat son temps de service (Entrée / Direct, Plat / Suite, Dessert / À la demande), puis envoyer un signal tactile « 🔔 Réclamer la Suite » au moment propice pour déclencher la production en cuisine sans devoir me déplacer au passe-plat.

**Why this priority**: Le cadencement des envois en cuisine est crucial en restauration assise pour éviter que les plats chauds ne soient préparés trop tôt et refroidissent pendant les entrées.

**Independent Test**: Saisir 2 entrées (« Salade César ») et 2 plats (« Entrecôte Grillée ») sur la table `T5` $\to$ envoyer la commande $\to$ la cuisine reçoit le bon avec mention « DIRECT » pour les entrées et « SUITE » pour les plats $\to$ quand les clients ont terminé leurs entrées, appuyer sur le bouton « 🔔 Réclamer Suite T5 » $\to$ le KDS et l'imprimante cuisine reçoivent l'ordre de cuisson immédiat pour la suite.

**Acceptance Scenarios**:

1. **Given** la saisie des plats sur la tablette, **When** le serveur affecte les temps « Entrée », « Plat » ou « Dessert », **Then** les articles sont visuellement groupés par ordre de service dans le panier tactile et sur les bons de cuisine.
2. **Given** une table ayant déjà commandé ses plats avec mention « SUITE », **When** le serveur appuie sur le bouton rapide « 🔔 Réclamer la Suite » depuis le plan de salle ou la fiche table, **Then** une notification sonore et visuelle s'affiche instantanément sur le KDS cuisine et un ticket de réclame est émis sur l'imprimante du poste chaud.

---

### User Story 4 - Encaissement Mobile au Bout de Table avec Pourboire Tactile et Partage Intelligent (Priority: P2)

En tant que serveur effectuant l'encaissement directement à table avec le client, je dois pouvoir présenter une interface tactile de paiement clair intégrant des suggestions de pourboires rapides (ex: 5%, 10%, 15%, Montant libre) et un découpage simplifié par convives (partage égal en N parts ou sélection d'articles individuels), afin de fluidifier le règlement au moment de l'addition.

**Why this priority**: L'encaissement direct au bout de table réduit les allers-retours vers la caisse centrale, augmente les montants de pourboires perçus par le personnel et accélère la rotation des tables.

**Independent Test**: Sur une table `T2` avec une note de 80.00 €, ouvrir l'écran d'encaissement $\to$ sélectionner « Partager en 4 parts égales (20.00 € / part) » $\to$ encaisser la première part de 20.00 € par Carte Bancaire avec 2.00 € de pourboire $\to$ vérifier que le solde restant passe à 60.00 €, que le pourboire est enregistré séparément pour le serveur et que le reçu intermédiaire est généré.

**Acceptance Scenarios**:

1. **Given** une addition présentée au client sur la tablette, **When** le client choisit un bouton de pourboire suggéré (ex: 10%), **Then** le montant du pourboire s'ajoute de manière transparente au total à encaisser sans fausser la ventilation de TVA sur les ventes.
2. **Given** une note à diviser entre plusieurs convives, **When** le serveur appuie sur « Division en N parts » (ex: 3), **Then** chaque part est précalculée au centime près et chaque convive peut payer selon son mode de règlement préféré (CB, Sans Contact, Espèces, Titre Restaurant).

---

### User Story 5 - Facturation sur Chambre d'Hôtel / Compte Client Folio (Hôtellerie / PMS) (Priority: P3)

En tant que serveur de restaurant d'hôtel ou room-service, je dois pouvoir imputer une addition de restaurant ou de bar directement sur le compte de la chambre d'un client de l'hôtel (ex: Chambre 204 - M. Dupont), avec contrôle du statut de la chambre et signature tactile du client sur l'écran, afin d'assurer une consolidation transparente sur la facture globale de fin de séjour.

**Why this priority**: Fonctionnalité reine pour les établissements mixtes hôtel-restaurant (boutique hôtels, resorts, room-service), évitant au client d'avoir à sortir un moyen de paiement immédiat lors de ses consommations au bar ou restaurant.

**Independent Test**: Ouvrir une commande Bar de 28.00 € $\to$ passer en règlement $\to$ choisir le mode « 🏨 Imputer sur Chambre » $\to$ taper le numéro de chambre « 204 » $\to$ vérifier l'affichage du nom du résident (« M. Dupont - Check-in Actif ») $\to$ recueillir la signature sur l'écran tactile $\to$ valider $\to$ vérifier la clôture de la table et l'émission du folio de charge chambre.

**Acceptance Scenarios**:

1. **Given** un client séjournant à l'hôtel commandant au restaurant, **When** le serveur sélectionne le mode de règlement « Chambre d'Hôtel » et saisit le numéro de chambre, **Then** le système vérifie la validité du séjour, affiche le nom du titulaire et prépare la pièce de charge.
2. **Given** la pièce de charge chambre affichée, **When** le client valide son accord avec sa signature tactile sur la tablette, **Then** la transaction est clôturée avec le libellé « Facturation Chambre », la table est libérée et la créance est transférée vers le grand livre client / PMS.

---

### Edge Cases

- **Transfert de table avec plats en cours de préparation** : Lors du transfert d'une table vers une nouvelle localisation, les écrans KDS et imprimantes doivent mettre à jour immédiatement l'identifiant de table pour que les serveurs apportent les plats au nouvel emplacement sans confusion.
- **Remise supérieure au montant total** : Le système doit strictement interdire toute remise conduisant à un solde négatif.
- **Arrondi lors de la division en parts inégales** : Pour une note de 10.00 € divisée en 3 parts (3.33 € + 3.33 € + 3.34 €), l'écart d'arrondi de 1 centime est automatiquement attribué sur la première ou dernière transaction pour garantir que la somme exacte des encaissements égale 100% du total TTC.
- **Chambre d'hôtel déjà check-out** : Si une chambre a déjà effectué son départ ou n'a pas d'autorisation de crédit ouverte, le système refuse l'imputation et invite le serveur à demander un autre mode de paiement.
- **Perte de réseau Wi-Fi pendant l'encaissement à table** : La transaction doit être enregistrée dans le journal local sécurisé et synchronisée automatiquement dès le rétablissement du réseau sans doubler les débits.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Le système DOIT permettre le transfert intégral d'une commande active d'une table source vers une table cible libre en un clic tactile.
- **FR-002**: Le système DOIT permettre la fusion de deux tables occupées en combinant les articles et en libérant la table source.
- **FR-003**: Le système DOIT permettre d'appliquer des remises en pourcentage (%) ou en montant fixe (€) au niveau d'un article ou de la commande globale avec obligation d'indiquer un motif.
- **FR-004**: Le système DOIT supporter le marquage d'un article en « Offert » (prix net 0.00 €) avec archivage du motif pour l'audit de gestion.
- **FR-005**: Le système DOIT permettre de classifier chaque article de commande selon un temps de service (« Entrée / Direct », « Plat / Suite », « Dessert »).
- **FR-006**: Le système DOIT proposer un bouton tactile d'action rapide « 🔔 Réclamer la Suite » émettant une notification instantanée vers les postes de cuisine KDS et imprimantes de production.
- **FR-007**: Le module d'encaissement DOIT proposer des touches de pourboires rapides paramétrables (ex: 5%, 10%, 15%, Montant libre) sans impacter le chiffre d'affaires assujetti à la TVA.
- **FR-008**: Le système DOIT supporter le paiement fractionné (« Split Bill ») par division égale en N parts et par sélection granulaire d'articles avec gestion rigoureuse des centimes d'arrondi.
- **FR-009**: Le système DOIT proposer un mode de règlement « 🏨 Facturation sur Chambre » avec recherche de chambre, validation du nom de client et enregistrement de signature tactile.
- **FR-010**: Toutes les opérations monétaires, remises, pourboires et imputations DOIVENT être tracées de façon immuable dans le journal d'événements techniques conformément à la réglementation fiscale.

---

### Key Entities

- **TableTransferLog**: `Id`, `SourceTableNumber`, `TargetTableNumber`, `OrderId`, `OperatorId`, `TimestampUtc`, `IsMerge`.
- **OrderDiscount**: `Id`, `OrderId`, `OrderItemId` (optionnel), `DiscountType` (Percent, Amount, Comp/Offert), `Value`, `Reason`, `AuthorizedByOperatorId`.
- **CourseItem**: `Id`, `OrderItemId`, `CourseType` (Direct, Suite, Dessert, OnDemand), `FiredAtUtc`, `FireStatus` (Pending, Fired, Served).
- **TipRecord**: `Id`, `OrderId`, `PaymentTransactionId`, `OperatorId`, `Amount` (`Money`), `Method`, `TimestampUtc`.
- **RoomFolioCharge**: `Id`, `OrderId`, `RoomNumber`, `GuestName`, `Amount` (`Money`), `SignatureData`, `FolioStatus` (Pending, Transferred, Billed).

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: L'opération de transfert ou fusion de table s'effectue en moins de 3 interactions tactiles et met à jour le plan de salle en moins de 100ms.
- **SC-002**: L'envoi du signal « 🔔 Réclamer Suite » notifie les postes cuisine KDS et imprimantes de production en moins de 200ms.
- **SC-003**: 100% des divisions d'addition en parts égales ou par articles aboutissent à une somme des paiements strictement égale au centime près au total TTC de la commande ($0 \text{ centime d'écart}$).
- **SC-004**: 100% des remises et articles offerts sont audités avec leur motif et opérateur responsable dans le journal de conformité.
- **SC-005**: Le temps moyen d'encaissement d'une table avec pourboire ou partage ne dépasse pas 45 secondes par table.
- **SC-006**: 100% des tests automatisés de domaine, d'application et d'interfaces passent avec succès (0 warning, 0 error).

---

## Assumptions

- Les tablettes et terminaux mobiles fonctionnent sous environnement tactile (iPadOS, Android ou navigateur tactile grand écran) avec des dimensions de boutons adaptées au travail à une main ou deux mains en service actif.
- Les chambres d'hôtel disposent d'un registre local ou connecté simulant le PMS (Property Management System) pour la validation des numéros de chambre et résidents actifs.
- Les remises managériales au-delà d'un seuil prédéfini requièrent une validation par code PIN d'un utilisateur ayant le rôle `FloorManager` ou `Admin`.
