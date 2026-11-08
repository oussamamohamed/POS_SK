# Recherche : Stock, fiches techniques et allergènes

Toutes les inconnues de la spec sont tranchées ici.

## D1. Représentation de la rupture manuelle
- **Décision** : réutiliser `Product.IsAvailable` (existe déjà, éditable dans Gestion) et ajouter un endpoint d'action rapide, un contrôle serveur à l'ajout de ligne et un événement SignalR.
- **Raison** : la spec 007 décrit un état « OutOfStock » et le code porte déjà `IsAvailable` ; une seconde notion serait une source d'écarts. Le code actuel ne le vérifie pas à la commande (seule la lecture du catalogue le sert) : le contrôle manquant est le vrai travail.
- **Alternatives** : nouvelle colonne `OutOfStock` (rejetée : doublon) ; rupture par lot de stock (rejetée : lot C seulement).

## D2. Stockage des allergènes
- **Décision** : masque de 14 bits dans `Products.AllergenMask` (entier) plus `AllergensDeclared` (booléen). Non déclaré = « non renseigné » ; déclaré avec un bit à 0 = « absent ».
- **Raison** : lisible par tous les postes dans la réponse du catalogue, sans jointure ; l'ordre des bits est figé dans `Allergens` et reflété en Swift et en JS.
- **Écart avec la spec** : FR-001 évoque trois états par allergène. Le plan les porte au niveau du produit (renseigné ou non, puis présent ou absent par allergène), ce qui couvre le besoin légal « ne pas affirmer l'absence sans déclaration ». À confirmer ; un état inconnu par allergène serait un second masque.
- **Alternatives** : table de liaison (rejetée : lourde pour 14 valeurs) ; JSON (rejetée : filtrage moins simple).

## D3. Monnaie et quantités sans virgule flottante
- **Décision** : quantités en entiers de **millièmes d'unité de base** (g, ml, pièce) ; prix d'achat en **milli-centimes par unité de base** (entier 64 bits). Coût d'une ligne = `quantité × prix / 1000` en milli-centimes ; coût du plat = somme, puis arrondi unique en centimes, au plus fort reste demi-supérieur (mêmes règles que `Money.FromDecimal` et son miroir Swift).
- **Raison** : 1 kg à 12,50 € donne 1,25 centime par gramme, donc impossible en centimes entiers ; les milli-centimes évitent tout `Decimal` et tout `Double` et se reproduisent à l'identique en Swift.
- **Alternatives** : `Decimal` .NET et `Decimal` Swift (rejetée : fixtures de parité plus fragiles) ; prix au kilo avec division tardive (rejetée : arrondis divergents).

## D4. Sous-recettes
- **Décision** : un ingrédient peut être de type « préparation » avec sa propre fiche (rendement en unité de base). Le coût unitaire d'une préparation se déduit de sa fiche et de son rendement. Détection de cycle à l'enregistrement, profondeur maximale 5.
- **Raison** : sauce ou fond utilisés dans plusieurs plats ; reste un seul modèle de fiche.

## D5. Moment de la consommation du stock
- **Décision** : à l'**encaissement soldé** de la commande (reçu qui ramène le solde à zéro, vente comptoir comprise), dans la même transaction, une fois par commande.
- **Raison** : un seul point d'accroche, idempotent, cohérent avec les rapports ; les lignes offertes ou remisées sont bien consommées. Une consommation à l'envoi en cuisine imposerait de gérer les annulations de lignes avant paiement.
- **Limite connue** : le stock baisse avec un retard de la durée du repas ; la rupture automatique s'en ressent. Un lot ultérieur pourrait consommer à l'envoi en cuisine.
- **Alternatives** : envoi en cuisine (rejetée pour l'instant) ; fin de service par lot (rejetée : retard excessif).

## D6. Avoir et restitution du stock
- **Décision** : un avoir ne restitue le stock que si le responsable coche « produit non consommé » au moment de l'annulation ; sinon l'avoir ne change rien (produit jeté ou consommé).
- **Raison** : conforme à l'histoire 4 scénario 4 ; la mention est portée par un mouvement `SaleReversal`.

## D7. Immutabilité des mouvements de stock
- **Décision** : déclencheurs SQL `BEFORE UPDATE` et `BEFORE DELETE` qui lèvent une erreur, serveur et iPad. Pas d'intercepteur EF fiscal (il est réservé aux entités fiscales).
- **Raison** : le stock n'est pas une écriture fiscale ; un déclencheur protège sans élargir le périmètre de `FiscalImmutabilityInterceptor` et s'applique identiquement en SQLite local.
- **Alternatives** : intercepteur EF dédié (rejetée : ne protège pas l'iPad).

## D8. Rupture automatique
- **Décision** : option globale `AutoUnavailableOnStockOut` ; elle bascule `IsAvailable` à faux et pose `AutoUnavailable = 1`. À la réception d'un stock positif, seuls les produits `AutoUnavailable = 1` redeviennent disponibles. Une rupture manuelle n'est jamais levée automatiquement.
- **Raison** : sépare la décision du responsable de l'automatisme.

## D9. Diffusion et mode autonome
- **Décision** : événement `OnProductAvailabilityChanged(productId, isAvailable)` sur `/hubs/pos`, abonné côté web et iPad (`RealtimeEvent.productAvailabilityChanged`). En mode autonome, pas de temps réel : l'écran se met à jour localement.
- **Raison** : respect de la contrainte « noms d'événements = contrat partagé » de la constitution.

## D10. Parité .NET / Swift
- **Décision** : `recipe_cost_reference.json` (fiches, prix, coûts attendus, allergènes déduits) généré par un test .NET avec `WRITE_RECIPE_FIXTURE=1` et rejoué par Swift, comme les autres fixtures du projet. La journée simulée ajoute des fiches, des consommations et un stock initial, avec un oracle pour le stock final.

## D11. Allergènes sur le ticket de cuisine
- **Décision** : option `PrintAllergensOnKitchenTicket` (désactivée par défaut). L'activer modifie `TicketDocumentBuilder` : à traiter comme une évolution de l'impression (régénérer `ticket_document_reference.json`, reporter dans `Printing/`). Hors lot A ; prévu en fin de lot C.

## D12. Libellés des allergènes
- **Décision** : les 14 noms sont stables en interne (`gluten`, `crustaceans`, `eggs`, `fish`, `peanuts`, `soy`, `milk`, `nuts`, `celery`, `mustard`, `sesame`, `sulphites`, `lupin`, `molluscs`) et traduits en en/fr/ar sur chaque support. Les icônes sont des symboles système, sans image.
