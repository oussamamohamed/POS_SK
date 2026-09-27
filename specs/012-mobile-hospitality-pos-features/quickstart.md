# Quickstart Validation: Fonctions Populaires d'Encaissement Mobile & Tablette (Restauration & Hôtellerie)

**Feature**: `012-mobile-hospitality-pos-features`  
**Date**: 2026-09-02

---

## 1. Prérequis

- Le serveur backend `RestaurantPos.Api` est démarré sur `http://localhost:5000`.
- Deux tables ont été créées ou chargées dans la base (ex: `T1`, `T4`, `T5`).

---

## 2. Scénarios de Validation Pas à Pas

### Scénario 1 : Transfert de Table en 2 clics tactiles (US1)
1. Ouvrir l'application sur `http://localhost:5000` et se connecter avec le code PIN `1234`.
2. Ouvrir la table `T1`, ajouter 2 articles au panier et cliquer sur **Valider Commande**.
3. Depuis la vue commande ou plan de salle, appuyer sur le bouton **🔀 Transférer / Déplacer Table**.
4. Choisir la table cible **`T4`** et valider.
5. **Vérification** :
   - Un toast vert confirme : *"Commande de T1 transférée avec succès vers T4 !"*.
   - Le plan de salle affiche `T1` en **Libre** (vert) et `T4` en **Occupée** (bleu) avec le panier complet.

---

### Scénario 2 : Application d'une Remise & Article Offert (US2)
1. Sur la table `T4`, sélectionner un article (ex: « Tarte Tatin »).
2. Cliquer sur l'action tactile **🎁 Offert / Comp**.
3. Renseigner le motif : *"Geste commercial fidélité"* et valider.
4. **Vérification** :
   - La ligne affiche **0.00 € (Offert)**.
   - Le sous-total de la note est ajusté sans rompre la ventilation de TVA.

---

### Scénario 3 : Envoi de la Réclame Cuisine « Suite » (US3)
1. Sur la table `T5` ayant commandé entrées et plats chauds.
2. Cliquer sur le bouton tactile **🔔 Réclamer la Suite**.
3. **Vérification** :
   - Un signal sonore et visuel s'affiche sur le KDS Cuisine Chaude avec la mention clignotante *"RÉCLAME SUITE T5"*.

---

### Scénario 4 : Règlement par Facturation Chambre d'Hôtel & Signature (US5)
1. Sur la table `T4`, cliquer sur **💳 Encaisser**.
2. Sélectionner le mode de paiement **🏨 Imputer sur Chambre**.
3. Saisir le numéro de chambre **`204`** $\to$ le système affiche *"Alexandre Dupont (Chambre Occupée)"*.
4. Signer sur le canvas tactile et valider.
5. **Vérification** :
   - Le ticket est clôturé fiscalement avec la mention *Paiement : Facturation Chambre (Ch. 204)*.
   - La table `T4` repasse immédiatement à l'état **Libre**.
