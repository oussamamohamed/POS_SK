# Restaurant POS — Tests d'Acceptation UI / Navigateur (Playwright E2E)

Suite de tests automatisés de bout en bout pour l'interface tactile Web POS (`http://localhost:5000`).

## 📋 Scénarios Couverts

1. **Authentification Tactile & Clavier PIN (`auth-flow.spec.ts`)** :
   - Saisie PIN sur pavé tactile numérique.
   - Rejet immédiat d'un code PIN erroné.
   - Connexion réussie (PIN `1234`) avec mise à jour du profil opérateur (Alexandre Dupont - Manager).
   - Verrouillage de session et ré-authentification.

2. **Plan de Salle & Tables (`floorplan-cart.spec.ts`)** :
   - Affichage dynamique des tables de restaurant et statuts.
   - Sélection d'une table et basculement automatique sur la caisse avec badge actif.

3. **Modificateurs & Suppléments Payants (`modifiers-pricing.spec.ts`)** :
   - Déclenchement du modal tactile sur un produit avec options (`Burger Gourmet Rossini`).
   - Sélection de cuisson obligatoire (`Saignant`) et d'extras multiples (`Double Cheddar +2.00 €`, `Bacon Fumé +1.50 €`).
   - Calcul dynamique en temps réel du total (19.50 € + 3.50 € = **23.00 € TTC**).
   - Ajout au panier avec affichage des options et décomposition TVA (10%).

4. **Encaissement & Règlement Fiscal (`checkout-payment.spec.ts`)** :
   - Ouverture du modal d'encaissement tactile.
   - Règlement par Carte Bancaire (CB).
   - Validation fiscale et remise à zéro du panier.

5. **Remises Commerciales & Articles Offerts (`discounts-comps.spec.ts`)** :
   - Ouverture du modal Remise.
   - Application d'une remise globale (10%) avec motif NF525.
   - Calcul et réinitialisation de la remise.

6. **Partage de l'Addition (`split-bill.spec.ts`)** :
   - Découpage équitable selon le nombre de convives (stepper tactile 2 ➔ 3 ➔ 2).
   - Calcul dynamique de la quote-part et bascule en encaissement fractionné.

7. **Transfert & Fusion de Tables (`table-transfer.spec.ts`)** :
   - Ouverture du modal de transfert.
   - Sélection de la table cible et contrôle des options.

8. **Facturation Chambre d'Hôtel PMS (`hotel-room-charge.spec.ts`)** :
   - Sélection de la chambre résidente et plafond de crédit disponible.
   - Signature tactile sur canvas avec effacement et ré-émission.
   - Enregistrement de la note folio et vidage du panier.

9. **Écran Cuisine KDS (`kds-workflow.spec.ts`)** :
   - Visualisation des trois colonnes de production (*En Attente*, *En Préparation*, *Prêt à Servir*).
   - Validation de l'affichage des bons.

10. **Fiscalité NF525 & Clôture Z (`fiscal-zreport.spec.ts`)** :
   - Consultation du Rapport X en direct.
   - Présence des boutons de Clôture Journalière (Rapport Z) et du scellement SHA-256.
   - Formulaire et génération de l'Export Comptable FEC réglementaire (Article A.47 A-1 LPF).

---

## 🚀 Exécution des Tests

```bash
# Se placer dans le dossier de test
cd tests/RestaurantPos.Web.E2ETests

# Lancer tous les tests en mode headless
npm test

# Lancer en mode interactif avec interface UI Playwright
npm run test:ui

# Lancer avec navigateur visible (headed)
npm run test:headed
```
