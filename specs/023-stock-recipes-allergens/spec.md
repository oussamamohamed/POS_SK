# Spécification de fonctionnalité : Stock, fiches techniques et allergènes

**Branche**: `main` (pas de branche dédiée)

**Créé**: 2026-11-04

**Statut**: Brouillon

**Entrée**: « Gérer les ingrédients, les recettes (fiches techniques), le décrément du stock à la vente, les ruptures en direct, le coût matière réel et les 14 allergènes réglementaires. »

## Scénarios utilisateur et tests *(obligatoire)*

### Histoire 1 - Allergènes sur les produits (Priorité : P1)

Le responsable associe à chaque produit les allergènes parmi les 14 de la réglementation européenne. Les serveurs les voient en prenant la commande, et ils figurent sur la carte client et les tickets.

**Pourquoi P1** : obligation légale d'information du consommateur, utile seule et sans stock.

**Test indépendant** : cocher « gluten » et « lait » sur un produit ; l'icône apparaît sur le web et l'iPad, et le filtre « sans gluten » masque le produit.

**Scénarios d'acceptation** :

1. **Étant donné** un produit avec allergènes, **quand** le serveur l'ajoute à une commande, **alors** les allergènes sont visibles dans le ticket en cours.
2. **Étant donné** un client allergique, **quand** le serveur filtre la carte par allergène, **alors** seuls les produits sans cet allergène restent proposés.
3. **Étant donné** un produit sans fiche d'allergènes renseignée, **quand** il est affiché, **alors** il est marqué « allergènes non renseignés » plutôt que « aucun ».

---

### Histoire 2 - Ruptures en direct (Priorité : P1)

Un serveur ou le responsable marque un produit en rupture depuis n'importe quel poste. Le produit disparaît ou se grise sur tous les postes, sur la carte client et dans les commandes en cours de saisie.

**Pourquoi P1** : valeur immédiate, sans dépendre des recettes.

**Test indépendant** : passer un produit en rupture sur un iPad ; il est grisé sur le web et un autre iPad en moins de 2 secondes.

**Scénarios d'acceptation** :

1. **Étant donné** un produit en rupture, **quand** un serveur tente de l'ajouter, **alors** l'ajout est refusé avec un message clair.
2. **Étant donné** une rupture levée, **quand** elle est enregistrée, **alors** le produit redevient disponible partout.
3. **Étant donné** un produit déjà envoyé en cuisine, **quand** il passe en rupture, **alors** la commande existante n'est pas modifiée.

---

### Histoire 3 - Ingrédients, recettes et coût matière réel (Priorité : P2)

Le responsable crée les ingrédients (unité, prix d'achat HT) et la fiche technique de chaque plat (quantité par ingrédient). Le coût de revient du plat se calcule alors tout seul à partir des prix d'achat, et alimente l'analyse du menu à la place du coût saisi à la main.

**Pourquoi P2** : demande de la saisie, mais donne une marge réelle, argument fort en démonstration.

**Test indépendant** : créer une recette de 3 ingrédients ; le coût de revient du plat est la somme des quantités par prix unitaires, et la classe Star/Plowhorse/Puzzle/Dog de l'analyse du menu le prend en compte.

**Scénarios d'acceptation** :

1. **Étant donné** une fiche technique, **quand** un prix d'achat change, **alors** le coût de revient des plats concernés est recalculé.
2. **Étant donné** un plat avec un coût saisi à la main et une fiche technique, **quand** l'analyse du menu s'exécute, **alors** le coût calculé de la fiche prime.
3. **Étant donné** une fiche avec un sous-plat (par exemple une sauce), **quand** le coût est calculé, **alors** le coût du sous-plat est inclus.
4. **Étant donné** des allergènes sur les ingrédients, **quand** la fiche technique est enregistrée, **alors** les allergènes du plat sont déduits des ingrédients, sauf surcharge explicite par le responsable.

---

### Histoire 4 - Décrément du stock à la vente et alertes (Priorité : P3)

Chaque vente consomme les ingrédients de la fiche. Le stock théorique baisse, une alerte apparaît sous un seuil, et un produit dont un ingrédient est épuisé peut passer en rupture automatiquement. Le responsable saisit réceptions et inventaires pour recaler le stock.

**Pourquoi P3** : le plus lourd (réceptions, inventaires), à livrer après les trois autres histoires.

**Test indépendant** : vendre 3 plats consommant 200 g chacun ; le stock de l'ingrédient baisse de 600 g, et une annulation par avoir le rend.

**Scénarios d'acceptation** :

1. **Étant donné** un stock sous le seuil, **quand** une vente le fait passer en dessous, **alors** une alerte « à traiter » est créée.
2. **Étant donné** une réception de marchandise, **quand** elle est saisie, **alors** le stock augmente de la quantité reçue et le prix moyen d'achat est mis à jour.
3. **Étant donné** un inventaire, **quand** le comptage diffère du stock théorique, **alors** l'écart est enregistré avec sa valeur.
4. **Étant donné** une vente annulée par avoir, **quand** l'avoir est émis, **alors** le stock n'est restitué que si le responsable l'a choisi (produit non consommé).

---

### Cas limites

- Produit vendu sans fiche technique : aucune consommation, aucune erreur.
- Stock théorique négatif : autorisé et signalé, jamais bloquant pour la vente.
- Modificateurs qui changent la consommation (sans oignon, double fromage) : voir FR-010.
- Changement d'unité d'un ingrédient en cours de vie : refusé s'il existe des mouvements.
- Suppression d'un ingrédient utilisé dans une fiche : refusée, avec la liste des plats concernés.
- Deux postes qui vendent le même produit simultanément : le stock reste cohérent.
- Mode autonome de l'iPad : le stock est local à l'iPad, comme le reste de ses données.

## Exigences *(obligatoire)*

### Exigences fonctionnelles

- **FR-001** : Le système DOIT gérer les 14 allergènes réglementaires sur les produits et les ingrédients, avec trois états : présent, absent, non renseigné.
- **FR-002** : Les allergènes DOIVENT être visibles à la prise de commande, sur la carte client, et facultativement sur le ticket de cuisine.
- **FR-003** : Le système DOIT permettre le filtrage de la carte par allergène.
- **FR-004** : Le système DOIT gérer la rupture manuelle d'un produit, diffusée en temps réel à tous les clients (web, iPad, QR), et refuser son ajout aux commandes.
- **FR-005** : Une rupture NE DOIT PAS modifier les lignes déjà envoyées en cuisine.
- **FR-006** : Le système DOIT gérer des ingrédients avec unité, prix d'achat HT et seuil d'alerte.
- **FR-007** : Une fiche technique DOIT lister des ingrédients (ou sous-recettes) avec quantité, sans cycle autorisé.
- **FR-008** : Le coût de revient d'un plat DOIT se calculer à partir de sa fiche et des prix d'achat, en centimes entiers, et DOIT primer sur le coût saisi à la main dans l'analyse du menu.
- **FR-009** : La vente d'un produit avec fiche DOIT décrémenter le stock des ingrédients, dans la même transaction que la commande, sans jamais empêcher la vente.
- **FR-010** : Les modificateurs DOIVENT pouvoir porter un ajustement de consommation (ajout, retrait, multiple).
- **FR-011** : Le système DOIT permettre la saisie de réceptions et d'inventaires, avec historique des mouvements non modifiable (ajout seulement).
- **FR-012** : Le système DOIT signaler dans la cloche « à traiter » les stocks sous seuil et les écarts d'inventaire importants.
- **FR-013** : Le responsable DOIT pouvoir choisir la rupture automatique d'un produit dont un ingrédient est épuisé.
- **FR-014** : Les fonctions de stock et de coût DOIVENT être réservées au responsable ; les allergènes et les ruptures sont lisibles par tous les postes.
- **FR-015** : Le client web, l'iPad serveur et l'iPad autonome DOIVENT donner les mêmes résultats pour le coût, la consommation et les allergènes (règles partagées, arrondis identiques).

### Entités principales

- **Allergène** : l'une des 14 catégories réglementaires.
- **Ingrédient** : article acheté (nom, unité, prix d'achat HT moyen, seuil, allergènes).
- **Fiche technique** : composition d'un produit (lignes ingrédient ou sous-recette avec quantité).
- **Mouvement de stock** : consommation, réception, inventaire ou perte, daté, avec quantité et valeur ; ajout seulement.
- **État de rupture** : indicateur manuel ou automatique rattaché à un produit.

## Critères de réussite *(obligatoire)*

### Résultats mesurables

- **SC-001** : Une rupture saisie sur un poste est visible sur tous les autres en moins de 2 secondes.
- **SC-002** : 100 % des produits d'une carte de démonstration affichent des allergènes ou la mention « non renseigné ».
- **SC-003** : Le coût de revient d'un plat calculé sur une journée simulée correspond, au centime, à la somme des consommations de l'oracle de test.
- **SC-004** : Après une journée simulée, le stock théorique de chaque ingrédient est égal à l'initial moins les consommations, y compris avoirs et remises.
- **SC-005** : Saisir la fiche technique d'un plat de 6 ingrédients prend moins de 3 minutes.

## Hypothèses

- Les ingrédients sont gérés en un seul dépôt par établissement ; le multi-dépôt est hors périmètre.
- Unités gérées : gramme, kilogramme, millilitre, litre, pièce, avec conversion simple au sein d'une même famille.
- Le prix d'achat suit une moyenne pondérée sur les réceptions ; les autres méthodes sont hors périmètre.
- La consommation des menus et formules se calcule par les produits composants.
- Les allergènes saisis par produit valent « mention du restaurateur » ; le restaurateur reste responsable de leur exactitude.
- Le décrément du stock n'est pas une écriture fiscale : il ne touche pas aux chaînes NF525.
- La spec 007 décrit un état « OutOfStock » pour les articles ; à rapprocher du code existant avant le plan, pour ne pas créer deux notions de rupture.
- Livraison en trois lots : allergènes et ruptures, puis coût matière, puis stock complet.
