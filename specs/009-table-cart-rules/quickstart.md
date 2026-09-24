# Quickstart Validation: Règles de Vidage du Panier et Réinitialisation post-Envoi Cuisine

**Feature**: `009-table-cart-rules`  
**Date**: 2026-08-30

---

## 1. Prérequis

- Le serveur `RestaurantPos.Api` est démarré sur `http://localhost:5000`.
- Les tables de démonstration sont initialisées (Table `T2` avec commande active).

---

## 2. Scénarios de Test et Validation Utilisateur

### Scénario 1 : Vidage sélectif sur table occupée (Protection des articles en cuisine)
1. Ouvrir `http://localhost:5000` dans votre navigateur.
2. Aller sur l'onglet **🗺️ Plan de Salle** et cliquer sur la **Table T2** (2x Salades César + 1x Burger Rossini déjà en cuisine, `38.50 €`).
3. Ajouter 2 desserts : cliquer sur **Tiramisu Spéculos Maison** ($\times 2$, total $53.50 €$).
4. Observer les badges :
   - Les Salades et le Burger affichent le badge vert `👨‍🍳 En Cuisine`.
   - Les Tiramisus affichent le badge ambré `➕ Nouveau`.
5. Cliquer sur l'icône de corbeille / bouton **Vider le panier** (`btnClearCart`).
6. **Vérification** :
   - Les 2 Tiramisus sont immédiatement supprimés du panier.
   - Les 2 Salades et le Burger restent présents et intacts dans le panier.
   - Le total revient à **38.50 €**.
   - Un toast d'information s'affiche : *"1 nouvel/nouveaux article(s) retiré(s). Les articles en cuisine sont conservés."*.

---

### Scénario 2 : Tentative de vidage sur articles 100% en cuisine
1. Toujours sur la **Table T2** avec uniquement les articles en cuisine (Salades + Burger).
2. Cliquer sur **Vider le panier**.
3. **Vérification** :
   - Aucun article n'est effacé.
   - Un toast rouge / avertissement s'affiche : *"Les articles déjà transmis en cuisine ne peuvent pas être vidés."*.

---

### Scénario 3 : Envoi en cuisine et réinitialisation / transition d'écran
1. Sur la **Table T2**, ajouter 1 **Fondant Chocolat Valrhona**.
2. Cliquer sur le bouton **📤 Envoyer Cuisine** (`btnSendKitchen`).
3. **Vérification** :
   - Le dessert est persisté et le ticket KDS est émis.
   - Le panier de saisie est réinitialisé.
   - L'écran effectue une transition automatique fluide vers la vue **🗺️ Plan de Salle**.
   - Un toast vert de succès confirme : *"Commande T2 envoyée en cuisine ! 👨‍🍳"*.
4. Cliquer à nouveau sur la **Table T2** depuis le plan de salle $\to$ Tous les articles (y compris le Fondant) s'affichent désormais avec le badge `👨‍🍳 En Cuisine`.

---

### Scénario 4 : Vidage sur nouvelle commande (0 article en cuisine)
1. Aller sur le **Plan de Salle** et cliquer sur la **Table T1** (Libre).
2. Ajouter 1 Entrecôte et 1 Bière (aucun article envoyé).
3. Cliquer sur **Vider le panier**.
4. **Vérification** : Le panier est intégralement vidé (0 article) et affiche le toast *"Panier vidé"*.
