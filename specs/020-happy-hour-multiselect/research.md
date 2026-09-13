# Research: Happy Hour Multi-Select Configuration (Articles & Familles)

## 1. Ergonomie Tactile & Sélection Multiple (UX)

### Decision
Créer deux vues spécialisées au sein de la gestion du planning Happy Hour :
- **Sous-onglet 1 : Familles Éligibles** : Liste de cartes/tuiles tactiles représentant les catégories (`CAT-DRINKS`, `CAT-MAINS`, etc.) avec checkbox tactile intégrée, nombre d'articles dans la famille, et barre d'action commune (taux de remise % + bouton d'application).
- **Sous-onglet 2 : Articles Spécifiques** : Grille d'articles filtrable par catégorie active et recherche textuelle instantanée, avec cases à cocher tactiles grand format, compteur dynamique de sélection, et sélecteur de mode tarifaire (Prix Fixe € vs Remise %).

### Rationale
- Les écrans de caisse tactile en restauration ne sont pas adaptés aux menus déroulants HTML `<select multiple>` qui nécessitent souvent des touches `Ctrl` ou un défilement fastidieux.
- Des tuiles de sélection de 48px minimum permettent une sélection rapide au doigt en plein rush.
- Le bouton *"Tout sélectionner"* permet de cibler en une demi-seconde toutes les bières ou tous les cocktails de la catégorie affichée.

### Alternatives Considered
- *Dropdown multi-select avec tags* : Trop petit pour les doigts sur écran tactile 10", risque d'erreurs de pointage.
- *Modale popup arborescente séparée* : Ajoute une étape de navigation superflue alors que l'écran de configuration a déjà tout l'espace disponible.

---

## 2. Conception de l'API Groupée (Batch API)

### Decision
Implémenter un endpoint atomique :
- `POST /api/happy-hour/schedules/{scheduleId}/rules/batch`
- Payload :
  ```json
  {
    "targetType": 0, // 0 = Product, 1 = Category
    "targetIds": ["<id1>", "<id2>", "<id3>"],
    "pricingMode": 0, // 0 = FixedPrice, 1 = PercentageDiscount
    "fixedPrice": 5.00,
    "discountPercent": null
  }
  ```
- Endpoint de suppression groupée :
  - `DELETE /api/happy-hour/schedules/{scheduleId}/rules/batch`
  - Payload : `{ "ruleIds": ["<id1>", "<id2>"] }`

### Rationale
- Envoi d'une seule requête HTTP pour 10 ou 50 articles.
- Exécution dans une transaction Entity Framework Core unique (`SaveChangesAsync`).
- Remplacement / mise à jour (Upsert) pour éviter les doublons de règles sur un même article au sein d'un même créneau.

### Alternatives Considered
- *Boucle d'appels `POST /api/happy-hour/schedules/{id}/rules` depuis le client JS* : Rejetée car lente, génère des états partiels en cas de micro-coupure réseau, et surcharge le serveur.

---

## 3. Synchronisation Temps Réel & Cache

### Decision
À la fin de l'opération batch, notifier immédiatement les caisses connectées via `IPosHub.Clients.All.OnHappyHourStatusChanged()` et invalider le cache du statut Happy Hour.

### Rationale
- Garantit que tout terminal connecté recharge instantanément sa grille tarifaire dès qu'une modification de groupe est enregistrée.
