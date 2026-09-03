# Quickstart Validation: Modification des Objets dans le Module de Configuration

**Feature**: `011-edit-configuration-objects`  
**Date**: 2026-08-31

---

## 1. Prérequis

- Le serveur `RestaurantPos.Api` est démarré sur `http://localhost:5000`.

---

## 2. Scénarios de Test et Validation Utilisateur

### Scénario 1 : Modification d'un Article du Catalogue
1. Ouvrir `http://localhost:5000` et se connecter avec le code PIN `1234`.
2. Aller dans l'onglet **⚙️ Paramétrage POS** $\to$ onglet **🏷️ Familles & Articles**.
3. Dans la liste des articles à droite, repérer « Burger Maison & Frites » (16.50 €).
4. Cliquer sur le bouton **✏️ Modifier**.
5. Modifier le prix à **18.00 €** et cocher **Épingler dans les Touches Rapides**.
6. Cliquer sur **Enregistrer les modifications**.
7. **Vérification** :
   - Un toast vert confirme : *"Article 'Burger Maison & Frites' mis à jour avec succès !"*.
   - La liste d'administration affiche le nouveau prix de 18.00 €.
   - Retourner sur l'onglet **🛒 Caisse** $\to$ le bouton « Burger Maison & Frites » affiche 18.00 € et apparaît dans la barre de Coup de feu supérieure.

---

### Scénario 2 : Modification d'un Code PIN Employé
1. Aller sur l'onglet **⚙️ Paramétrage POS** $\to$ onglet **👥 Serveurs & Codes PIN**.
2. Sur la ligne de « Sophie Martin », cliquer sur **✏️ Modifier**.
3. Changer le code PIN pour **`3333`** et le rôle pour **Manager**.
4. Enregistrer.
5. Verrouiller la caisse (bouton cadenas / déconnexion).
6. Taper `3333` sur le pavé numérique $\to$ La session s'ouvre avec le profil Manager.
