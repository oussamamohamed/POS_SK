# Phase 0 Research: Règles de Vidage du Panier et Réinitialisation post-Envoi Cuisine

**Feature**: `009-table-cart-rules`  
**Date**: 2026-08-30

---

## 1. Problématiques & Analyse des flux utilisateurs

### Problématique 1 : Vidage accidentel d'articles déjà envoyés
- **Comportement non désiré** : Lorsque le serveur ajoute un dessert par erreur sur une table en cours de repas et clique sur « Vider le panier », si le panier vide TOUT, le serveur a l'impression d'avoir effacé la totalité de la commande de la table.
- **Règle cible** : Le bouton « Vider le panier » ne doit purger que les articles temporaires / non validés (`isDispatched == false`).
- **Gestion des cas limites** :
  1. *Uniquement des articles non envoyés* $\to$ Panier entièrement vidé.
  2. *Mélange articles envoyés + non envoyés* $\to$ Supprime les non envoyés, conserve les envoyés, recalcule le total et notifie : *"Nouveaux articles retirés. Les articles en cuisine sont conservés."*
  3. *Uniquement des articles envoyés* $\to$ Ne supprime rien et notifie : *"Les articles déjà en cuisine ne peuvent pas être vidés."*

### Problématique 2 : Enchaînement rapide après envoi cuisine
- **Comportement non désiré** : Après avoir appuyé sur « Envoyer Cuisine », la commande reste affichée dans la caisse, ce qui oblige le serveur à faire un retour manuel vers le plan de salle ou risque d'entraîner une confusion avec la table suivante.
- **Règle cible** : Dès que l'envoi cuisine est validé :
  1. Les nouveaux articles sont persistés en base et routés vers le KDS.
  2. Le panier de caisse se réinitialise.
  3. L'écran bascule immédiatement sur la vue **Plan de Salle (Floor Plan)**.
  4. Un toast de succès vert confirme : *"Commande Table {N} envoyée en cuisine ! 👨‍🍳"*.

---

## 2. Décisions Techniques & Algorithmes

### Algorithme de vidage sélectif (`ClearCart`)

```javascript
function clearCart() {
    const undispatchedCount = state.cart.filter(item => !item.isDispatched).length;
    const dispatchedCount = state.cart.filter(item => item.isDispatched).length;

    if (state.cart.length === 0) return;

    if (undispatchedCount > 0 && dispatchedCount > 0) {
        // Purge only undispatched items
        state.cart = state.cart.filter(item => item.isDispatched);
        renderCart();
        showToast(`${undispatchedCount} nouvel/nouveaux article(s) retiré(s). Les articles en cuisine sont conservés.`, 'info');
    } else if (undispatchedCount > 0 && dispatchedCount === 0) {
        // Full clear
        state.cart = [];
        renderCart();
        showToast('Panier vidé', 'info');
    } else if (undispatchedCount === 0 && dispatchedCount > 0) {
        // Protection alert
        showToast('Les articles déjà transmis en cuisine ne peuvent pas être vidés.', 'error');
    }
}
```

### Algorithme de post-envoi cuisine (`SendKitchenAndReset`)

```javascript
async function sendKitchenAndReset() {
    if (state.cart.length === 0) return;

    // 1. Dispatch only new lines to API
    const undispatched = state.cart.filter(item => !item.isDispatched);
    if (undispatched.length > 0) {
        await persistNewItems(state.activeTable, undispatched);
    }
    await triggerDispatch(state.activeTable);

    // 2. Clear current session & return to floor plan
    state.cart = [];
    state.activeOrderId = null;
    renderCart();
    switchView('floorPlanView');
    showToast(`Commande ${state.activeTable} envoyée en cuisine ! 👨‍🍳`, 'success');
}
```
