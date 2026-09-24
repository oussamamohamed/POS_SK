# Phase 0 Research: Fonctions Populaires d'Encaissement Mobile & Tablette (Restauration & Hôtellerie)

**Feature**: `012-mobile-hospitality-pos-features`  
**Date**: 2026-09-02

---

## 1. Analyse des besoins & Ergonomie Mobile / Tactile

### Problématique
Dans les établissements modernes de restauration et d'hôtellerie (bars, restaurants traditionnels, terrasses, room-service, hôtels-restaurants), le personnel de salle et de réception utilise de plus en plus de tablettes et terminaux mobiles pour fluidifier le service à table. Les opérations les plus fréquentes et génératrices de valeur ajoutée comprennent :
1. **Transferts & Fusions de tables** lors des mouvements de clients en salle ou terrasse.
2. **Remises, Gestes commerciaux & Articles offerts** avec traçabilité comptable et fiscale stricte.
3. **Gestion des Temps de Service & Réclames cuisine (« Direct », « Suite », « Dessert », « Réclamer Suite »)** pour coordonner la cuisine en temps réel sans allers-retours au passe-plat.
4. **Encaissement au bout de table avec Pourboires suggérés & Split intelligent** (division en N parts égales sans perte de centime d'arrondi).
5. **Facturation sur Chambre d'Hôtel / Compte Client Folio** avec signature tactile sur écran pour les clients de l'hôtel.

---

## 2. Décisions d'Architecture

### Décision 1 : Transfert & Fusion atomique de Tables
- **Choix** : Enrichir `TableManagementService` avec les méthodes `TransferTableAsync` et `MergeTablesAsync`.
- **Mécanisme** :
  - *Transfert* : Réassignation atomique de `ActiveOrderId` vers la table cible et passage de la table source à `TableStatus.Free`.
  - *Fusion* : Rapatriement de toutes les lignes d'articles de la table source vers la commande active de la table cible, recalcul du montant TTC total, archivage dans `TableTransferLog` et libération de la table source.
- **Justification** : Préserve l'intégrité transactionnelle et met à jour instantanément les vues FloorPlan et KDS.

### Décision 2 : Remises, Comps (Offerts) et Traçabilité Fiscale NF525
- **Choix** : Stocker les remises soit au niveau de la ligne (`OrderItem.DiscountPercent`, `OrderItem.IsComp`, `OrderItem.CompReason`), soit au niveau global de la commande (`Order.DiscountType`, `Order.DiscountValue`, `Order.DiscountReason`).
- **Calcul** :
  - Remise pourcentage ligne : $\text{Prix Net} = \text{UnitPrice} \times (1 - \frac{\text{DiscountPercent}}{100})$.
  - Article offert : $\text{Prix Net} = 0.00 \, €$, avec conservation du prix catalogue brut et taux de TVA pour le calcul des statistiques de pertes / gestes commerciaux.
  - Remise globale : Répartition proportionnelle de la réduction sur les bases taxables hors taxes pour garantir une ventilation exacte de la TVA.
- **Justification** : Règle fiscale NF525 interdisant les suppressions silencieuses ou les prix modifiés à la volée sans trace d'audit.

### Décision 3 : Temps de Service & Signal de Réclame Cuisine (Kitchen Course Sequencing)
- **Choix** : Ajouter un attribut `CourseType` (`Direct` = Entrée, `Suite` = Plat principal, `Dessert` = Dessert / Café, `OnDemand` = Boisson / À la demande) sur chaque ligne `OrderItem`.
- **Réclame** : Un endpoint `POST /api/tables/{tableNumber}/fire-suite` émet un événement SignalR `SuiteClaimed` sur le hub `/hubs/kitchen` et génère un bon d'impression « 🔔 RÉCLAME SUITE » vers les imprimantes réseau des stations chaudes.
- **Justification** : Réduction du temps d'attente à table et synchronisation temps réel entre salle et cuisine.

### Décision 4 : Pourboires Tactiles & Split Bill équilibré au centime
- **Choix** : Proposer 4 boutons de pourboire prédéfinis (`0%`, `5%`, `10%`, `15%`, `Montant Libre`). Le pourboire (`TipAmount`) est comptabilisé comme encaissement extra-commercial reversé au personnel, sans assujettissement à la TVA de restauration.
- **Algorithme de Split équilibré** :
  $$\text{PartBase} = \lfloor \frac{\text{TotalCents}}{N} \rfloor$$
  $$\text{Reste} = \text{TotalCents} \pmod N$$
  Les $\text{Reste}$ premiers convives paient $(\text{PartBase} + 1) \, \text{cents}$, et les suivants paient $\text{PartBase} \, \text{cents}$. La somme totale est garantie égale à 100.00% du montant dû.

### Décision 5 : Facturation sur Chambre d'Hôtel / Compte PMS
- **Choix** : Étendre l'énumération `PaymentMethod` avec `PaymentMethod.RoomCharge` (valeur `4`) et créer un service `IRoomBillingService` / table de simulation PMS `HotelRoomResident`.
- **Workflow** : Saisie du numéro de chambre (ex: `204`), vérification du statut actif (`IsOccupied = true`), saisie de la signature tactile sur canvas HTML5 / MAUI, création du relevé `RoomFolioCharge` et clôture fiscale de la note de restaurant.

---

## 3. Matrice de validation des choix techniques

| Fonctionnalité | Endpoints REST | Couche Service Domain/App | Impact Interface Tactile |
|---|---|---|---|
| **Transfert / Fusion Tables** | `POST /api/tables/{src}/transfer`, `POST /api/tables/{src}/merge` | `ITableManagementService` | Modale tactile de sélection de table cible sur le plan de salle |
| **Remises & Offerts** | `POST /api/orders/{id}/discount`, `POST /api/orders/{id}/items/{itemId}/comp` | `IOrderDiscountService` | Bouton `% Remise` et `🎁 Offert` sur le panier tactile |
| **Réclame Cuisine** | `POST /api/tables/{tableNumber}/fire-course` | `IKitchenRoutingService` | Bouton `🔔 Réclamer la Suite` sur la commande active |
| **Pourboires & Split** | `POST /api/checkout/pay`, `POST /api/checkout/split-preview` | `ICheckoutPaymentService` | Boutons de pourboire rapide et pavé de division convives |
| **Facturation Chambre** | `GET /api/hotel/rooms/{number}`, `POST /api/hotel/room-charge` | `IRoomBillingService` | Mode de règlement `🏨 Chambre d'Hôtel` avec zone de signature |
