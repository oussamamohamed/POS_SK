# Feature Specification: Direct Sales and Takeaway Checkout with Complex Scenarios

**Feature Branch**: `018-takeaway-direct-sales`

**Created**: 2026-09-13

**Status**: Draft

**Input**: User description: "gestion de l'encaissement a emporter ou vente direct avec des senarios complexe"

## Clarifications

### Session 2026-09-13
- Q: Pour le mode de vente directe par défaut au comptoir, quelle doit être la destination de consommation initiale affectée au panier avant un éventuel basculement ? → A: Configurable par terminal (défaut initial "À Emporter"), avec bascule instantanée "Sur Place / À Emporter" via un toggle tactile permanent visible dans l'en-tête du panier.
- Q: En cas de règlement par Titre-Restaurant papier dont la valeur faciale dépasse le montant éligible restant dû sur la note (surpaiement), quel comportement le système doit-il appliquer ? → A: Configurable au niveau des paramètres du terminal avec trois politiques au choix : (A) Plafonnement strict au solde dû sans rendu de monnaie (défaut), (B) Refus strict de tout surpaiement bloquant la saisie, ou (C) Émission automatique d'un avoir/bon d'achat pour le reliquat excédentaire.
- Q: Pour l'attribution des numéros d'ordre de retrait client à emporter (ex. #A-14), quelle stratégie de numérotation séquentielle journalière doit être adoptée ? → A: Préfixe alphabétique par terminal de caisse (ex. Caisse 1 = #A-01 à #A-99, Caisse 2 = #B-01 à #B-99) réinitialisé chaque matin, assurant l'unicité et le fonctionnement 100% autonome hors-ligne.
- Q: En conformité avec la réglementation anti-gaspillage (fin de l'impression automatique obligatoire du ticket de caisse), comment l'édition du coupon de retrait et de la facturette doit-elle se comporter lors d'un encaissement à emporter ? → A: Afficher une invite tactile demandant si le client souhaite une facturette : si Oui, impression du ticket combiné complet (facturette fiscale NF525 complète + coupon de retrait détachable) ; si Non (par défaut), impression uniquement du coupon de retrait compact avec le numéro d'ordre #A-XX et le récapitulatif.
- Q: Quel niveau d'autorisation doit être requis pour annuler ou abandonner un panier de vente directe au comptoir ou une commande placée en attente ? → A: Annulation libre par l'équipier de caisse tant qu'aucun paiement n'a été enregistré (avec traçabilité au journal d'audit technique JET), mais code PIN superviseur/manager obligatoire dès qu'un premier paiement partiel a été validé ou pour supprimer définitivement une commande mise en attente.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Vente Directe Express au Comptoir (Priority: P1)

En tant qu'équipier ou caissier au comptoir,  
Je veux que le mode "Vente Directe / Comptoir" soit le mode actif par défaut dès l'ouverture de la caisse sans aucune étape de sélection, afin de saisir immédiatement les articles et encaisser le client en un minimum de manipulations tactiles,  
Afin de fluidifier le flux de clients pendant le coup de feu et réduire la file d'attente à la caisse.

**Why this priority**: La vente directe au comptoir représente le mode d'exploitation standard et par défaut de la restauration rapide, du bar et de la vente à emporter. Éliminer toute étape de sélection préalable pour démarrer une vente garantit une vélocité maximale au comptoir.

**Independent Test**: Peut être testé isolément dès l'écran de caisse (déjà positionné en vente directe par défaut) en touchant 2 articles du catalogue, en validant le paiement immédiat (ex. carte bancaire sans contact) et en constatant l'émission du ticket fiscal NF525 et le réarmement automatique d'un nouveau panier de vente directe vierge en moins de 3 interactions tactiles.

**Acceptance Scenarios**:

1. **Given** un caissier authentifié sur le terminal de caisse ou revenant après une clôture de vente,  
   **When** il se trouve sur l'écran principal de prise de commande,  
   **Then** le système est automatiquement positionné en mode "Vente Directe / Comptoir" par défaut, avec un panier vierge immédiatement prêt à recevoir des articles, sans exiger de sélection de mode ni de passage par le plan de salle.
2. **Given** un panier de vente directe avec 3 articles ajoutés pour un total de 18,50 €,  
   **When** le caissier touche le bouton de raccourci d'encaissement direct "CB Directe",  
   **Then** la vente est clôturée immédiatement, un ticket client et une souche fiscale sécurisée sont générés, le tiroir-caisse ne s'ouvre pas, et l'écran revient à un panier vierge prêt pour le client suivant.
3. **Given** un panier de vente directe avec un total de 12,00 €,  
   **When** le caissier sélectionne "Espèces" et appuie sur le bouton rapide "Billet 20 €",  
   **Then** le système calcule et affiche instantanément la monnaie à rendre (8,00 €), commande l'ouverture du tiroir-caisse et affiche un indicateur de rendu grand format.

---

### User Story 2 - Commande à Emporter avec Recalcul Automatique de TVA Différenciée (Priority: P1)

En tant que restaurateur assujetti aux règles fiscales de la restauration,  
Je veux que le système applique automatiquement la fiscalité légale relative à la vente à emporter (re-ventilation de TVA entre produits scellés à 5,5%, produits préparés consommables immédiatement à 10% et boissons alcoolisées à 20%) dès que le mode "À Emporter" est activé ou commuté,  
Afin de respecter scrupuleusement la réglementation fiscale NF525 tout en garantissant une totale conformité sans calcul manuel par le personnel.

**Why this priority**: La requalification fiscale de la TVA selon la destination de consommation (Sur Place vs À Emporter) est une obligation légale stricte. Une erreur de taux de TVA expose l'établissement à des redressements fiscaux majeurs lors des contrôles.

**Independent Test**: Peut être testé en composant un panier mixte (boisson scellée, sandwich chaud, bière alcoolisée), en basculant le mode de "Sur Place" vers "À Emporter", et en vérifiant la ventilation des montants HT/TVA dans le récapitulatif du panier et sur le ticket d'encaissement.

**Acceptance Scenarios**:

1. **Given** un panier contenant une canette de soda fermée (produit alimentaire conservable scellé), un burger préparé chaud et une bouteille de bière,  
   **When** le mode de la commande est réglé sur "Sur Place",  
   **Then** la canette et le burger sont taxés à 10,0% et la bière est taxée à 20,0%.
2. **Given** le même panier composé précédemment,  
   **When** le caissier bascule le mode de commande sur "À Emporter",  
   **Then** le système recalcule instantanément la ventilation fiscale : la canette scellée passe automatiquement à 5,5%, le burger reste à 10,0% (consommation immédiate) et la bière reste à 20,0%.
3. **Given** une commande enregistrée à emporter,  
   **When** le paiement est finalisé,  
   **Then** le ticket de caisse et le journal d'audit fiscal consignent explicitement les sous-totaux ventilés par taux de TVA (assiette 5,5%, assiette 10%, assiette 20%) et le mode de vente "À EMPORTER".

---

### User Story 3 - Multi-Règlement Complexe et Titres-Restaurant Plafonnés (Priority: P2)

En tant que client et caissier,  
Je veux pouvoir fractionner un paiement complexe au comptoir en combinant des Titres-Restaurant (papier ou dématérialisé) plafonnés au montant légal maximum et éligibles uniquement sur les produits alimentaires, complétés par une carte bancaire ou des espèces avec pourboire,  
Afin de clore les ventes rapidement sans risque d'erreur d'imputation ou de dépassement des plafonds légaux.

**Why this priority**: L'usage mixte carte restaurant + carte bleue / espèces est le scénario d'encaissement le plus fréquent le midi dans la restauration rapide et à emporter, et présente la plus forte source d'erreurs d'encaissement pour les équipiers.

**Independent Test**: Peut être testé en créant une commande de 35,00 € comprenant 25,00 € de nourriture et 10,00 € d'alcool, en appliquant un paiement Titre-Restaurant de 25,00 € (plafond journalier légal atteint), puis en complétant le solde restant de 10,00 € en espèces avec 2,00 € de pourboire.

**Acceptance Scenarios**:

1. **Given** une commande à emporter de 30,00 € dont 22,00 € d'articles éligibles aux Titres-Restaurant et 8,00 € d'articles non-éligibles (boisson alcoolisée),  
   **When** le caissier sélectionne le mode "Titre-Restaurant",  
   **Then** le système suggère par défaut le montant éligible maximum de 22,00 € et interdit la saisie d'un montant supérieur au montant éligible ou au plafond journalier réglementaire (25,00 €).
2. **Given** un premier règlement partiel de 20,00 € validé par Titre-Restaurant sur une note de 35,00 €,  
   **When** le paiement partiel est validé,  
   **Then** le solde restant dû (15,00 €) est affiché en évidence sur l'écran tactile et sur l'afficheur client.
3. **Given** un solde restant dû de 15,00 €,  
   **When** le client règle par carte bancaire et demande à ajouter 2,00 € de pourboire,  
   **Then** le montant total débité sur la carte est de 17,00 €, le solde de la note passe à zéro, et le ticket ventile distinctement le chiffre d'affaires commercial (15,00 €) et le pourboire au comptoir (2,00 €).
4. **Given** un paiement fractionné en cours (ex. 10 € en espèces déjà enregistrés),  
   **When** le client s'aperçoit qu'il n'a pas son autre moyen de paiement et souhaite annuler la transaction,  
   **Then** le système permet au superviseur d'annuler ou de modifier la ligne de paiement déjà saisie avant la signature fiscale définitive de la transaction.

---

### User Story 4 - File d'Attente Comptoir : Mise en Attente (Hold) et Rappel (Recall) (Priority: P2)

En tant qu'équipier de caisse lors d'un rush,  
Je veux pouvoir mettre en attente temporaire le panier d'un client qui hésite ou cherche son portefeuille, afin d'encaisser immédiatement le client suivant sans perdre les articles saisis, puis rappeler la commande mise en attente en un geste,  
Afin de maximiser le débit de la caisse et d'éviter les frustrations dans la file d'attente.

**Why this priority**: Dans un contexte de vente au comptoir, l'attente d'un seul client bloque l'intégralité de la file si le panier en cours ne peut pas être garé temporairement.

**Independent Test**: Peut être testé en saisissant un panier, en cliquant sur "Mettre en attente", en saisissant et encaissant une autre commande, puis en rappelant le premier panier depuis la liste des ventes en attente pour le modifier et l'encaisser.

**Acceptance Scenarios**:

1. **Given** un panier de vente directe en cours avec 4 articles,  
   **When** le caissier appuie sur l'action tactile "Mettre en attente",  
   **Then** le panier est sauvegardé avec une référence visuelle (numéro d'attente ou nom facultatif du client), le terminal libère immédiatement l'écran pour un nouveau panier, et un badge indicateur affiche le nombre de ventes en attente (ex. "Attente (1)").
2. **Given** deux commandes placées en attente au comptoir,  
   **When** le caissier touche le badge "Ventes en attente",  
   **Then** un tiroir visuel affiche la liste ordonnée chronologiquement des commandes avec l'heure de mise en attente, le montant et les articles.
3. **Given** une commande rappelée depuis la liste d'attente,  
   **When** le caissier l'ouvre,  
   **Then** tous les articles, remises et modificateurs sont fidèlement restaurés et le caissier peut compléter la saisie ou procéder à l'encaissement.

---

### User Story 5 - Identification Client, Bipeur de Retrait et Bon de Préparation Takeaway (Priority: P3)

En tant que préparateur de commande et client à emporter,  
Je veux qu'un numéro d'ordre séquentiel visible (ex. `#A-23`) ou un numéro de bipeur/pager soit attribué à chaque commande à emporter et imprimé sur un coupon de retrait client distinct,  
Afin que le client puisse attendre dans l'espace dédié et être appelé sans confusion lorsque son sac de commande est prêt.

**Why this priority**: En vente à emporter, la remise du sac au bon client sans erreur d'inversion est cruciale pour la satisfaction client et la fluidité du comptoir de retrait.

**Independent Test**: Peut être testé en activant l'option bipeur lors d'une commande à emporter, en renseignant le numéro "12", et en vérifiant que le bon de retrait client et la fiche KDS emballage portent distinctement la mention `#A-23 (Bipeur 12)`.

**Acceptance Scenarios**:

1. **Given** une commande finalisée en mode "À Emporter",  
   **When** l'encaissement est validé,  
   **Then** le système génère automatiquement un numéro séquentiel journalier de retrait (ex. `#A-01` réinitialisé chaque jour) imprimé en très gros caractères sur un coupon de retrait pour le client.
2. **Given** un restaurant équipé de bipeurs vibrants de retrait,  
   **When** le caissier coche l'option "Bipeur" avant ou pendant l'encaissement et saisit le numéro "7",  
   **Then** le numéro de bipeur est associé à la commande et transmis à la fois sur le coupon client et sur les écrans de préparation en cuisine/emballage.
3. **Given** une commande à emporter avec heure de retrait programmée (ex. retrait prévu à 12h45),  
   **When** la commande est enregistrée à 11h30,  
   **Then** le ticket mentionne l'heure de retrait convenue et la commande n'apparaît en alerte urgente sur le KDS qu'au moment opportun calculé selon le délai de préparation standard.

---

### User Story 6 - Routage KDS Spécifique Emballage et Consignes Écologiques (Priority: P3)

En tant que préparateur au poste d'emballage (Packing Station) et gestionnaire éco-responsable,  
Je veux qu'une commande à emporter déclenche un affichage spécifique au poste d'emballage avec la liste des contenants réutilisables consignés ou des sacs nécessaires,  
Afin de préparer convenablement les sacs hermétiques et d'enregistrer les dépôts/restitutions de consignes.

**Why this priority**: L'emballage des commandes à emporter nécessite une logistique différente de la salle (couvercles étanches, couverts nomades, sacs isothermes, gestion des contenants consignés).

**Independent Test**: Peut être testé en ajoutant un plat consigné dans une commande à emporter, en vérifiant la ligne de consigne ajoutée à la note (ex. +1,00 €) et l'affichage des instructions d'emballage sur le poste KDS emballage.

**Acceptance Scenarios**:

1. **Given** une commande validée en mode "À Emporter",  
   **When** la commande est transmise en cuisine,  
   **Then** le poste KDS "Emballage / Takeaway" reçoit un ticket digital avec un badge visuel distinctif jaune/orange `[À EMPORTER]` et la mention des accessoires requis (ex. couverts bois, serviettes, sac grand format).
2. **Given** un article configuré avec une consigne de boîte réutilisable,  
   **When** il est commandé en mode "À Emporter",  
   **Then** une ligne de consigne au montant forfaitaire défini (ex. 2,00 €, non assujetti à la TVA ou taxé selon la règle fiscale applicable) est ajoutée au panier.
3. **Given** un client qui rapporte un contenant consigné au comptoir,  
   **When** le caissier touche l'action "Retour Consigne",  
   **Then** le système déduit le montant de la consigne du total de la vente ou émet un avoir/remboursement immédiat.

---

### Edge Cases

- **Changement de destination en cours de paiement partiel** : Si un client a déjà versé 10 € en espèces sur une note "Sur Place" et demande soudainement à emporter sa commande, le système recalcule l'assiette de TVA sans altérer le montant déjà perçu et ajuste le solde restant.
- **Rupture réseau lors d'une vente directe express** : Le terminal de vente directe doit fonctionner à 100% hors ligne (Local-First). L'encaissement en espèces ou TPE autonome continue sans interruption, les numéros de retrait séquentiels sont réservés localement, et la synchronisation avec le serveur s'opère en arrière-plan dès le rétablissement de la liaison.
- **Dépassement du plafond légal de Titre-Restaurant** : Si le caissier tente de forcer un encaissement Titre-Restaurant supérieur à 25,00 € (ou montant de référence légal en vigueur), le système bloque la validation avec un message clair et propose d'imputer le reliquat sur un autre moyen de paiement.
- **Tentative de mise en attente d'un panier vide** : L'action de mise en attente est désactivée si le panier ne comporte aucun article.
- **Abandon de commande en attente à la clôture de caisse (Rapport Z)** : Lors de la clôture journalière de la caisse, si des commandes restent "En attente", le système avertit le gérant et impose soit l'annulation explicite des paniers abandonnés, soit leur validation avant de signer le rapport Z fiscal.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Le mode "Vente Directe / Comptoir" DOIT être le mode opératoire actif par défaut à l'ouverture de la caisse et après chaque clôture de commande, avec un panier immédiatement prêt à la saisie d'articles sans aucune étape ou action de sélection préalable.
- **FR-002**: Le système DOIT permettre de configurer la destination de consommation par défaut du terminal (avec "À Emporter" comme valeur par défaut initiale pour la vente au comptoir), tout en intégrant un sélecteur tactile permanent (toggle "Sur Place / À Emporter") dans l'en-tête du panier permettant une bascule instantanée à tout moment.
- **FR-003**: Le système DOIT recalculer instantanément l'ensemble des taux et montants de TVA des lignes du panier lors du changement de destination de consommation ("Sur Place" vs "À Emporter").
- **FR-004**: Pour le mode "À Emporter", le système DOIT appliquer le taux réduit de TVA (5,5% en France) aux produits alimentaires et boissons non alcoolisées conditionnés dans des contenants scellés permettant une conservation différée (boissons en canettes/bouteilles fermées, yaourts, viennoiseries emballées), le taux intermédiaire (10%) aux préparations alimentaires à consommation immédiate (sandwichs, pizzas, salades préparées, cafés), et le taux standard (20%) aux boissons alcoolisées.
- **FR-005**: Le système DOIT prendre en charge le multi-règlement fractionné sur une même vente (combinaison de Carte Bancaire, Espèces, Titres-Restaurant papier, Titres-Restaurant dématérialisés, Chèques Vacances, Avoirs/Bons d'achat).
- **FR-006**: Le système DOIT contrôler pour les Titres-Restaurant que le montant saisi ne dépasse ni le montant total des articles alimentaires éligibles du panier, ni le plafond légal journalier unitaire en vigueur. En cas de surpaiement par titre-restaurant papier (valeur faciale supérieure au solde restant dû), le système DOIT appliquer la politique configurée au niveau des paramètres du terminal parmi :
  - **Politique A (défaut)** : Plafonnement automatique au solde restant dû, interdiction stricte de rendu de monnaie (0,00 € rendu) et consommation du titre pour la note.
  - **Politique B** : Refus strict et blocage de la saisie si le montant du titre dépasse le solde restant dû.
  - **Politique C** : Consommation de la valeur faciale totale et génération automatique d'un avoir/bon d'achat pour le montant excédentaire non consommé.
- **FR-007**: Le système DOIT calculer et afficher instantanément la monnaie à rendre lors d'un règlement en espèces avec boutons de coupures rapides (5 €, 10 €, 20 €, 50 €) et déclencher l'ouverture du tiroir-caisse uniquement lors des règlements impliquant des espèces.
- **FR-008**: Le système DOIT permettre l'ajout d'un pourboire distinct lors de l'encaissement et l'enregistrer séparément du chiffre d'affaires des ventes dans le journal d'audit fiscal.
- **FR-009**: Le système DOIT offrir une fonction tactile "Mettre en attente" (Hold) permettant de garer un panier en cours non finalisé, avec assignation d'un identifiant temporaire, et de libérer la caisse pour les clients suivants.
- **FR-010**: Le système DOIT fournir une vue des commandes en attente (Recall) permettant de reprendre n'importe quelle commande gardée pour la modifier, la compléter ou l'encaisser en un toucher.
- **FR-011**: Le système DOIT attribuer automatiquement à chaque commande à emporter validée un numéro d'ordre séquentiel de retrait client préfixé par l'identifiant du terminal de caisse émetteur (ex. format `#A-01` à `#A-99` pour le Terminal A, `#B-01` à `#B-99` pour le Terminal B) et réinitialisé au début de chaque journée d'exploitation, garantissant l'unicité locale sans collision même en fonctionnement autonome déconnecté (Local-First).
- **FR-012**: À la clôture de l'encaissement d'une commande à emporter, le système DOIT afficher une invite tactile demandant si le client souhaite sa facturette fiscale :
  - Si le client répond **Non** (ou validation par défaut rapide) : le système imprime uniquement le coupon de retrait compact portant le numéro d'ordre grand format (#A-XX), l'heure et les articles.
  - Si le client répond **Oui** : le système imprime le ticket combiné complet incluant la facturette fiscale NF525 et le coupon de retrait détachable.
- **FR-013**: Le système DOIT permettre la saisie optionnelle d'un identifiant de bipeur/vibreur physique ou du nom du client pour faciliter la remise de la commande au comptoir.
- **FR-014**: Le système DOIT router les commandes à emporter vers les postes KDS appropriés avec un repère visuel distinctif et un affichage spécifique sur la station d'emballage (Packing Station) récapitulant les sacs et contenants requis.
- **FR-015**: Le système DOIT permettre la planification d'un horaire de retrait convenu pour la commande à emporter et ajuster l'apparition ou la priorisation de la commande sur les écrans de préparation en conséquence.
- **FR-016**: Le système DOIT supporter la tarification des contenants consignés et leur restitution (déduction ou remboursement de consigne) avec traçabilité comptable.
- **FR-017**: Toute transaction de vente directe ou à emporter DOIT être scellée cryptographiquement dans la chaîne d'audit fiscal NF525 avec horodatage UTC, grand total TTC, ventilation TVA et signature électronique inaltérable.
- **FR-018**: Lors du processus de clôture journalière (Rapport Z), le système DOIT bloquer la clôture ou notifier le gérant s'il subsiste des commandes en attente non soldées, et exiger leur annulation motivée ou leur encaissement.
- **FR-019**: L'abandon ou l'annulation d'un panier de vente directe DOIT être autorisé librement pour l'équipier de caisse tant qu'aucun paiement n'a été validé (avec consignation systématique d'un événement au journal d'audit JET NF525). En revanche, la saisie d'un code PIN superviseur/manager valide DOIT être obligatoirement exigée dès qu'un premier paiement partiel a été saisi ou pour supprimer définitivement une commande gardée en attente.

---

### Key Entities *(include if feature involves data)*

- **DirectOrder (Commande Directe / À Emporter)** :
  - Identifiant unique universel (chronologique).
  - Type de commande : `CounterDirectSale` (Vente directe comptoir) ou `Takeaway` (À emporter).
  - Numéro d'ordre de retrait client (ex. `#A-14`).
  - Identifiant de bipeur ou libellé client optionnel.
  - État de la commande : `Draft` (En cours), `OnHold` (En attente), `Paid` (Payée/Scellée), `InPreparation` (En cuisine), `ReadyForPickup` (Prête au comptoir), `Completed` (Remise au client), `Voided` (Annulée).
  - Horodatage de création, heure de retrait souhaitée, horodatage de paiement.
  - Lignes d'articles avec quantité, prix unitaire, options/modificateurs et code taxe appliqué.
  - Ventilation fiscale dynamique des taxes (HT, TVA 5,5%, TVA 10%, TVA 20%, TTC).
- **HeldOrderQueue (File des Ventes en Attente)** :
  - Liste des commandes temporairement garées au comptoir.
  - Horodatage de mise en attente, motif optionnel, terminal de saisie d'origine, opérateur responsable.
- **PaymentSplitLine (Ligne de Règlement Fractionné)** :
  - Moyen de paiement utilisé (Espèces, Carte Bancaire, Titre-Restaurant Papier, Titre-Restaurant Dématérialisé, Autre).
  - Montant imputé (en centimes entiers).
  - Montant du pourboire associé (en centimes entiers).
  - Données complémentaires éventuelles (ex. référence transaction TPE, nombre de titres papier).
- **TakeawayPackagingProfile (Profil d'Emballage et Consignes)** :
  - Règles d'attribution de sacs et couverts jetables ou réutilisables selon le volume d'articles.
  - Gestion des consignes associées aux contenants avec montant de consigne débité/crédité.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Une vente directe au comptoir avec règlement par carte bancaire sans contact ou espèces exactes peut être finalisée en moins de 3 interactions tactiles et moins de 5 secondes par le caissier.
- **SC-002**: Le basculement entre le mode "Sur Place" et "À Emporter" recalcule et affiche la nouvelle ventilation de TVA et le total en moins de 50 millisecondes sur le terminal tactile.
- **SC-003**: La mise en attente (Hold) d'une commande et le retour immédiat à un panier vierge s'effectuent en un seul toucher tactile (< 1 seconde).
- **SC-004**: 100% des transactions à emporter enregistrées respectent l'exactitude des ventilations de TVA françaises (5,5% sur produits scellés, 10% sur plats préparés consommables immédiatement, 20% sur alcools) avec une tolérance d'écart d'arrondi de 0 centime.
- **SC-005**: 100% des bons de retrait client à emporter affichent clairement le numéro d'ordre séquentiel de retrait, lisible à plus de 1 mètre de distance.
- **SC-006**: Aucune transaction de vente directe ne peut contourner la chaîne d'intégrité NF525 (chaînage cryptographique SHA-256 et JET).

---

## Assumptions

- Les règles de TVA appliquées par défaut sont celles de la législation française (taux de 5,5%, 10% et 20%) ; le catalogue de produits porte un attribut indiquant si un article est une "préparation consommable immédiatement" ou un "produit alimentaire scellé conservable".
- Le matériel de caisse dispose soit d'une imprimante thermique de tickets pour l'édition des coupons de retrait client, soit d'un écran d'affichage dynamique de retrait (Pick-up Display) connecté au système.
- Le terminal de paiement électronique (TPE) peut fonctionner soit en mode autonome (saisie manuelle du montant sur le TPE et acquittement sur la caisse), soit en mode connecté (protocole de concertation caisse-TPE).
- Le plafond d'acceptation unitaire légal des Titres-Restaurant est configuré par défaut à 25,00 € par jour et par personne, paramétrable dans la configuration de l'établissement en cas d'évolution législative.
- Les terminaux de caisse fonctionnent en mode Local-First avec capacité de générer des identifiants séquentiels de commande et d'encaisser les clients même en cas de coupure temporaire de la connexion au serveur central ou à internet.
