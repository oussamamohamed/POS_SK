# Feature Specification: Happy Hour Pricing & Schedule Management

**Feature Branch**: `019-happy-hour-pricing`

**Created**: 2026-09-13

**Status**: Draft

**Input**: User description: "je veux rajouter la gestion du happyhour"

## Clarifications

### Session 2026-09-13
- Q: Quelle méthode de calcul privilégiez-vous pour déterminer le prix Happy Hour d'un article ? → A: **Mixte** : Prix unitaire fixe dédié par produit (ex: pinte à 5,00 € au lieu de 7,50 €) OU pourcentage de remise appliqué par catégorie (ex: -20% sur la catégorie Cocktails), avec priorité donnée au prix fixe si les deux sont définis.
- Q: Quel comportement appliquer lorsqu'une table commandée pendant le Happy Hour règle sa note après l'heure de fin du créneau ? → A: **Verrouillage à la commande** : Les articles commandés et validés pendant le créneau conservent définitivement leur tarif Happy Hour. Tout article recommandé après l'expiration du créneau repasse au tarif standard, sur la même note.
- Q: Faut-il autoriser le gérant à déclencher ou prolonger manuellement un Happy Hour en dehors des plages programmées ? → A: **Oui avec code PIN superviseur** : Commandes tactiles rapides ("Prolonger de 30 min", "Activer 1 heure") protégées par code PIN de niveau Floor Manager / Admin et tracées avec horodatage et identifiant d'opérateur dans le journal d'audit technique (JET).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Détection Automatique et Tarification Happy Hour en Caisse (Priority: P1)

En tant que serveur, barman ou caissier,  
Je veux que le système détecte automatiquement le créneau Happy Hour actif selon l'heure et le jour de la semaine et applique immédiatement les tarifs préférentiels sur les boissons et articles éligibles lors de leur ajout au panier,  
Afin de servir rapidement les clients sans avoir à appliquer manuellement des remises ni calculer les prix de tête.

**Why this priority**: La détection automatique est le cœur de valeur de la fonctionnalité : elle élimine toute perte de temps pendant le rush d'après-travail (17h-20h) et prévient les erreurs de facturation au bar.

**Independent Test**: Peut être testé isolément en configurant un créneau Happy Hour couvrant l'heure courante (ex. 17h00 - 20h00 le jour J) avec une pinte de bière à 5,00 € au lieu de 7,00 €, en ajoutant l'article au panier, et en vérifiant que le prix appliqué est directement 5,00 € avec le prix normal barré.

**Acceptance Scenarios**:

1. **Given** un créneau Happy Hour actif configuré de 17h00 à 20h00 avec une bière artisanale à tarif Happy Hour 5,00 € (tarif standard 7,50 €),  
   **When** l'opérateur ajoute la bière au panier à 18h30,  
   **Then** la ligne est ajoutée avec un prix unitaire de 5,00 €, le récapitulatif du panier affiche l'économie réalisée, et un badge visuel indique l'application du tarif Happy Hour.
2. **Given** un article non éligible (ex. plat chaud, cocktail haut de gamme exclu),  
   **When** l'opérateur l'ajoute au panier pendant le créneau Happy Hour,  
   **Then** cet article est facturé à son prix standard normal sans altération.
3. **Given** un terminal POS fonctionnant hors-ligne (mode local),  
   **When** l'heure système entre dans la plage programmée,  
   **Then** le mode Happy Hour s'active localement de manière autonome et continue sans dépendre d'une connexion réseau externe.

---

### User Story 2 - Verrouillage du Prix à la Saisie de Commande (Priority: P1)

En tant que client et serveur en salle,  
Je veux que les consommations commandées et envoyées en production pendant le créneau Happy Hour conservent définitivement leur tarif Happy Hour, même si la table demande l'addition et règle sa note après l'expiration du créneau,  
Afin de respecter l'engagement commercial envers le client sans litige lors de l'encaissement.

**Why this priority**: Dans les bars et restaurants avec service à table, les clients commandent pendant le Happy Hour mais quittent souvent la table bien après. Recalculer le prix au plein tarif à l'encaissement provoquerait une contestation légitime et une rupture de confiance.

**Independent Test**: Peut être testé en ouvrant une table à 19h45, en ajoutant 2 pintes en Happy Hour, en envoyant la commande, en simulant le passage de l'horloge à 20h05 (fin du Happy Hour), puis en procédant à l'encaissement : le total dû doit rester basé sur les tarifs Happy Hour acquis lors de la commande.

**Acceptance Scenarios**:

1. **Given** une table ouverte avec des articles commandés pendant le Happy Hour à 19h50,  
   **When** l'heure courante passe à 20h01 (Happy Hour terminé) et que le serveur demande l'addition,  
   **Then** les lignes commandées avant 20h00 restent facturées au tarif Happy Hour négocié.
2. **Given** cette même table à 20h05,  
   **When** le client recommande une nouvelle tournée après la fin du créneau,  
   **Then** les nouveaux articles ajoutés sont facturés au tarif standard, tandis que les articles précédents conservent leur tarif Happy Hour sur la même note.
3. **Given** un article Happy Hour ajouté dans un panier temporaire non validé / non transmis à 19h59,  
   **When** la validation n'intervient qu'après expiration du créneau (ex. 20h02),  
   **Then** le système alerte l'opérateur de la fin du créneau et réévalue le panier selon la politique définie.

---

### User Story 3 - Visibilité Tactile et Signalétique en Caisse (Priority: P2)

En tant que serveur ou caissier sur écran tactile,  
Je veux identifier immédiatement au premier coup d'œil si le Happy Hour est actif, combien de temps il reste, et quels produits de la grille bénéficient d'un tarif réduit,  
Afin de renseigner les clients instantanément et d'optimiser les propositions de vente additionnelle.

**Why this priority**: L'ergonomie tactile en restauration exige une lisibilité instantanée (< 50ms) sans navigation complexe pour ne pas ralentir le personnel lors des rushs.

**Independent Test**: Peut être testé en observant l'interface de caisse : présence d'un bandeau ou badge d'état bien visible ("🎉 Happy Hour jusqu'à 20:00") et présence d'étiquettes de prix différenciées sur les tuiles des produits éligibles.

**Acceptance Scenarios**:

1. **Given** un créneau Happy Hour actif,  
   **When** l'opérateur consulte l'écran de prise de commande,  
   **Then** un indicateur visuel distinctif (badge lumineux / bandeau) signale "Happy Hour en cours" avec l'heure de fin prévue.
2. **Given** la grille des produits affichée en caisse,  
   **When** le Happy Hour est actif,  
   **Then** les tuiles des produits éligibles mettent en évidence le prix réduit avec un marqueur visuel clair (ex. prix réduit en surbrillance, prix standard barré ou badge "HH").
3. **Given** la fin du créneau Happy Hour atteinte en cours de service,  
   **When** l'heure de fin est dépassée,  
   **Then** l'indicateur d'état bascule automatiquement en mode standard et les prix affichés sur les tuiles redeviennent les prix normaux en temps réel.

---

### User Story 4 - Dérogation et Forçage Manuel par Superviseur (Priority: P2)

En tant que gérant d'établissement ou responsable de salle (Floor Manager),  
Je veux pouvoir forcer l'activation manuelle du Happy Hour, prolonger un créneau en cours de 30 minutes, ou suspendre temporairement l'opération lors d'un événement exceptionnel,  
Afin d'adapter dynamiquement la politique commerciale à la fréquentation réelle (retard de match, affluence imprévue).

**Why this priority**: La flexibilité opérationnelle est indispensable pour les établissements festifs ou sportifs, tout en exigeant une protection par code PIN pour éviter les abus de gratuité.

**Independent Test**: Peut être testé en saisissant le code PIN superviseur dans le menu de gestion rapide pour déclencher un Happy Hour forcé de 60 minutes en dehors des heures programmées, puis en vérifiant que les commandes appliquent immédiatement les tarifs réduits.

**Acceptance Scenarios**:

1. **Given** un gérant authentifié avec privilèges superviseur (PIN gérant),  
   **When** il choisit l'option "Activer Happy Hour exceptionnel (1h)",  
   **Then** le mode Happy Hour s'active immédiatement avec un horodatage d'expiration calculé, et l'événement est consigné au journal d'audit technique (JET).
2. **Given** un Happy Hour officiel programmé se terminant dans 5 minutes,  
   **When** le superviseur valide "Prolonger de 30 min",  
   **Then** l'heure de fin est décalée de 30 minutes supplémentaires pour l'ensemble des caisses connectées.
3. **Given** un serveur sans droits de gestion,  
   **When** il tente d'activer ou prolonger le Happy Hour manuellement,  
   **Then** le système exige obligatoirement la saisie d'un code PIN superviseur autorisé avant toute modification.

---

### User Story 5 - Configuration des Règles et Plages Horaires (Priority: P3)

En tant qu'administrateur ou propriétaire du restaurant,  
Je veux paramétrer les jours de la semaine (ex. du mardi au vendredi), les tranches horaires (ex. 17h00 - 19h30) et les règles de tarification (prix fixe spécifique par produit ou pourcentage de remise par catégorie),  
Afin d'automatiser entièrement la politique promotionnelle récurrente de mon établissement.

**Why this priority**: Permet au gestionnaire de préparer à l'avance sa programmation hebdomadaire sans intervention quotidienne.

**Independent Test**: Peut être testé dans l'écran de configuration en créant une règle "Afterwork Vendredi" (Vendredi 16h00-19h00, Catégorie Bières à -30%), puis en vérifiant sa persistance et son exécution au moment programmé.

**Acceptance Scenarios**:

1. **Given** l'écran de configuration du catalogue et des tarifs,  
   **When** l'administrateur crée une règle Happy Hour avec jours sélectionnés, heures début/fin et sélection de produits cibles,  
   **Then** la règle est sauvegardée, synchronisée sur les terminaux et affichée dans le récapitulatif des règles actives.
2. **Given** une règle existante,  
   **When** l'administrateur la désactive temporairement,  
   **Then** le système ignore le créneau correspondant sans supprimer sa configuration.

---

### Edge Cases

- **Chevauchement de créneaux** : Si deux règles Happy Hour se chevauchent sur un même produit, le système applique la règle la plus avantageuse pour le client (prix le plus bas).
- **Cumul avec remises commerciales** : Lorsqu'une remise globale (ex. 10% geste commercial) ou un article offert (comp) est appliqué sur un article déjà en tarif Happy Hour, le système calcule la remise additionnelle sur la base du prix Happy Hour effectif, avec traçabilité complète NF525.
- **Vente à Emporter vs Sur Place** : Le système permet de restreindre le Happy Hour au mode "Sur Place" uniquement (cas usuel des bars), ou de l'autoriser également à emporter selon la configuration de la règle.
- **Changement de destination en cours de commande** : Si une commande est basculée de "Sur Place" à "À Emporter" et que la règle Happy Hour exclut l'emporter, le système recalcule les prix et notifie le caissier.
- **Coupure réseau au moment du basculement d'heure** : Chaque terminal gère localement le minuteur de début et fin de Happy Hour sur son horloge interne sécurisée, garantissant une commutation sans latence même en cas d'indisponibilité du serveur central.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Le système DOIT permettre de définir des créneaux récurrents de Happy Hour avec jour(s) de la semaine, heure de début et heure de fin.
- **FR-002**: Le système DOIT activer et désactiver automatiquement la tarification Happy Hour dès que l'heure locale courante entre ou sort d'un créneau programmé actif.
- **FR-003**: Le système DOIT supporter deux modes de tarification Happy Hour : un prix unitaire fixe dédié par produit (ex. 5,00 € au lieu de 7,50 €) ou un pourcentage de réduction appliqué sur une catégorie / sélection d'articles (ex. -20% sur la catégorie Cocktails).
- **FR-004**: Le système DOIT afficher un indicateur visuel continu en caisse (badge / bandeau tactile) lorsque le Happy Hour est en cours, mentionnant l'heure de clôture du créneau.
- **FR-005**: Le système DOIT afficher sur les tuiles tactiles des produits éligibles le prix Happy Hour en vigueur de manière distincte du prix standard.
- **FR-006**: Le système DOIT figer et garantir le tarif Happy Hour sur toute ligne de commande enregistrée / envoyée en cuisine pendant le créneau, y compris si l'addition et le règlement interviennent après la fin du créneau.
- **FR-007**: Toute nouvelle ligne ajoutée après l'expiration du créneau DOIT être facturée au tarif standard, même si elle s'ajoute à une note ouverte durant le Happy Hour.
- **FR-008**: Le système DOIT permettre à un utilisateur doté d'un rôle superviseur/manager (authentifié par code PIN) d'activer manuellement un créneau exceptionnel, de prolonger un créneau existant ou d'interrompre le Happy Hour en cours.
- **FR-009**: Toute action manuelle de dérogation (forçage, prolongation, arrêt anticipé) DOIT être horodatée et enregistrée dans le journal d'audit technique (JET) avec l'identifiant de l'opérateur responsable.
- **FR-010**: Le système DOIT permettre de configurer si une règle Happy Hour s'applique à toutes les destinations de vente ou exclusivement au mode "Sur Place" (Eat-In).
- **FR-011**: Les calculs financiers relatifs aux tarifs Happy Hour DOIVENT utiliser des montants entiers en centimes avec ventilation exacte de TVA (10% et 20%), sans aucune tolérance d'arrondi à virgule flottante, en stricte conformité avec la norme fiscale NF525.
- **FR-012**: Le ticket de caisse et le récapitulatif fiscal DOIVENT mentionner clairement les lignes ayant bénéficié du tarif Happy Hour et le montant total de l'avantage accordé au client.
- **FR-013**: Le système DOIT fonctionner de manière autonome sur chaque terminal hors-ligne en s'appuyant sur l'horloge locale du terminal pour l'activation et la désactivation des créneaux.

### Key Entities

- **HappyHourSchedule** : Représente une période temporelle promotionnelle récurrente ou ponctuelle (Identifiant unique, Libellé, Jours de validité, Heure de début, Heure de fin, Statut actif/inactif, Éligibilité Sur Place uniquement ou universelle).
- **HappyHourPriceRule** : Règle tarifaire rattachée à un planning (Identifiant unique, Planning parent, Type de cible : Produit unitaire ou Catégorie, Type de tarification : Prix fixe ou Pourcentage de remise, Valeur monétaire ou taux).
- **HappyHourOverrideSession** : Journalise une dérogation manuelle déclenchée par un superviseur (Identifiant, OpérateurId, Heure de début forcée, Heure d'expiration prévue, Motif / Type de dérogation).
- **OrderLine (Extension)** : Propriétés d'audit sur la ligne de vente (Indicateur `IsHappyHourApplied`, Prix unitaire standard original, Montant de remise promotionnelle Happy Hour déduit, Horodatage de prise de commande).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: La détection et l'application automatique du tarif Happy Hour lors de l'ajout d'un article s'effectuent instantanément (temps de traitement < 50ms sur l'écran tactile).
- **SC-002**: 100% des lignes commandées pendant le créneau Happy Hour conservent leur tarif préférentiel lors d'un encaissement différé après l'heure limite, éliminant les réclamations clients liées aux changements de tranche horaire.
- **SC-003**: 100% des articles hors catalogue Happy Hour ou ajoutés après l'heure limite sont facturés au tarif standard sans dérive tarifaire non désirée.
- **SC-004**: Un gérant peut prolonger ou déclencher un Happy Hour d'urgence en moins de 10 secondes via son code PIN tactile.
- **SC-005**: La ventilation de TVA et le calcul de la note lors d'une vente Happy Hour présentent une exactitude arithmétique absolue (zéro centime d'écart) validée par les rapports fiscaux d'audit NF525.

## Assumptions

- Les terminaux de caisse disposent d'une horloge interne synchronisée (via protocole NTP ou synchronisation avec le serveur local du restaurant).
- Les produits disposent d'un prix de vente standard de référence servant de base si aucun tarif Happy Hour n'est configuré ou après la fin du créneau.
- Par défaut en restauration, les Happy Hours s'appliquent principalement aux boissons et tapas consommés sur place (Eat-In) ; la possibilité d'étendre à la vente à emporter est paramétrable.
- Le personnel de service dispose de profils caissier standard, tandis que les fonctions de dérogation nécessitent un profil Floor Manager ou Admin.
