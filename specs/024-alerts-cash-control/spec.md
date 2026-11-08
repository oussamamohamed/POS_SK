# Spécification de fonctionnalité : Alertes de pilotage et contrôle de caisse

**Branche**: `main` (pas de branche dédiée)

**Créé**: 2026-11-04

**Statut**: Brouillon

**Entrée**: « Un tableau de bord qui prévient le responsable : écart de caisse, remises et annulations anormales, ruptures, ticket moyen en baisse, pourboires par serveur. »

## Scénarios utilisateur et tests *(obligatoire)*

### Histoire 1 - Comptage de caisse et écart (Priorité : P1)

À l'ouverture, le serveur ou le responsable déclare le fond de caisse. À la clôture, il compte les espèces par coupures. Le système affiche l'écart entre l'espèce attendue (fond, ventes en espèces, sorties) et l'espèce comptée, et le conserve dans l'historique.

**Pourquoi P1** : base du contrôle et de la confiance du restaurateur ; utile seule.

**Test indépendant** : ouvrir avec 100 €, encaisser 50 € en espèces, compter 148 € ; l'écart de −2 € s'affiche et reste consultable.

**Scénarios d'acceptation** :

1. **Étant donné** une session de caisse ouverte, **quand** un paiement en espèces est encaissé, **alors** l'espèce attendue augmente du montant réellement gardé (rendu déduit).
2. **Étant donné** une sortie de caisse (achat de petite fourniture), **quand** elle est saisie avec un motif, **alors** elle réduit l'espèce attendue.
3. **Étant donné** un comptage à la clôture, **quand** l'écart dépasse le seuil configuré, **alors** une alerte est créée et le motif est demandé.
4. **Étant donné** un rapport Z, **quand** il est produit, **alors** le comptage et l'écart y figurent sans modifier la chaîne fiscale existante.

---

### Histoire 2 - Alertes sur comportements anormaux (Priorité : P1)

Le responsable règle des seuils : annulations, remises, offerts par serveur et par service. Quand un seuil est dépassé, une alerte apparaît dans la cloche « à traiter » avec le détail et le lien vers les tickets concernés.

**Pourquoi P1** : détecte pertes et fraudes, argument fort de démonstration.

**Test indépendant** : un serveur dépasse le seuil d'annulations ; une alerte nomme le serveur, le nombre et le montant, et ouvre la liste des avoirs.

**Scénarios d'acceptation** :

1. **Étant donné** un seuil de 3 annulations par service, **quand** un serveur en fait une quatrième, **alors** une alerte est créée une seule fois pour ce service.
2. **Étant donné** une remise supérieure au seuil en pourcentage, **quand** elle est appliquée, **alors** l'alerte mentionne la commande et l'auteur.
3. **Étant donné** un seuil désactivé, **quand** il est dépassé, **alors** aucune alerte n'est créée.
4. **Étant donné** une alerte traitée, **quand** le responsable la clôt avec une note, **alors** elle disparaît de la cloche mais reste dans l'historique.

---

### Histoire 3 - Tendances et comparaisons du jour (Priorité : P2)

Le tableau de bord signale les écarts de performance du jour face à la référence : ticket moyen, chiffre d'affaires cumulé à l'heure, panier par couvert, part de la vente à emporter. Chaque indicateur en écart notable devient une carte « à surveiller ».

**Pourquoi P2** : s'appuie sur les rapports d'encaissements existants.

**Test indépendant** : avec une semaine de ventes simulées et un jour à ticket moyen réduit de 20 %, la carte « ticket moyen en baisse » apparaît avec le pourcentage.

**Scénarios d'acceptation** :

1. **Étant donné** un chiffre d'affaires cumulé inférieur de plus de X % à celui du même jour de la semaine précédente à la même heure, **quand** le tableau de bord se charge, **alors** une carte l'indique.
2. **Étant donné** moins de 3 jours de données, **quand** le tableau de bord se charge, **alors** aucune alerte de tendance n'est créée.

---

### Histoire 4 - Rapport quotidien envoyé au responsable (Priorité : P3)

À la clôture, un résumé (chiffre d'affaires, couverts, ticket moyen, pourboires par serveur, écart de caisse, alertes ouvertes) est préparé, affichable et partageable (courriel ou export PDF).

**Pourquoi P3** : confort, dépend d'un canal d'envoi.

**Test indépendant** : clôturer la journée simulée ; le résumé affiche les mêmes totaux que le rapport Z.

**Scénarios d'acceptation** :

1. **Étant donné** une journée clôturée, **quand** le responsable ouvre le résumé, **alors** ses totaux sont identiques à ceux du rapport Z.
2. **Étant donné** un canal d'envoi non configuré, **quand** le résumé est prêt, **alors** il reste disponible dans l'application sans erreur.

---

### Cas limites

- Journée sans vente : pas d'alerte de tendance, comptage de caisse malgré tout possible.
- Fond de caisse jamais déclaré : l'espèce attendue part de zéro et l'écran le signale.
- Plusieurs postes : un comptage par poste, un résumé consolidé.
- Changement d'heure : les journées et les cumuls horaires gardent la tolérance d'une heure déjà en place.
- Alerte répétée à chaque rafraîchissement : interdite (une alerte par seuil et par période).
- Serveur qui contourne le seuil en répartissant ses annulations sur plusieurs services : visible dans le cumul de la semaine.
- Accès aux comptages et alertes : réservé aux responsables.

## Exigences *(obligatoire)*

### Exigences fonctionnelles

- **FR-001** : Le système DOIT gérer une session de caisse par poste, avec fond de caisse déclaré à l'ouverture.
- **FR-002** : Le système DOIT calculer l'espèce attendue à partir du fond, des règlements en espèces nets du rendu, des remboursements en espèces et des sorties de caisse.
- **FR-003** : Le système DOIT permettre la saisie d'une sortie ou d'une entrée de caisse avec motif obligatoire et auteur.
- **FR-004** : Le comptage de clôture DOIT accepter une saisie par coupure ou un total, et calculer l'écart en centimes entiers.
- **FR-005** : Le système DOIT conserver les sessions de caisse, comptages et écarts en historique non modifiable (ajout seulement).
- **FR-006** : Le système DOIT permettre de régler des seuils d'alerte : écart de caisse, annulations, remises, offerts, par service et par serveur.
- **FR-007** : Une alerte DOIT être créée au franchissement du seuil, une fois par seuil et par période, et DOIT nommer l'auteur, le nombre, le montant et les tickets concernés.
- **FR-008** : Les alertes DOIVENT apparaître dans la cloche « à traiter » et se clôturer avec une note, en gardant l'historique.
- **FR-009** : Le système DOIT détecter, dans les rapports d'encaissements, les écarts notables du jour : chiffre d'affaires cumulé à l'heure, ticket moyen, panier par couvert.
- **FR-010** : Les mesures de tendance DOIVENT s'abstenir sous 3 jours de données.
- **FR-011** : Le résumé de fin de journée DOIT présenter les mêmes totaux que le rapport Z et NE DOIT PAS altérer les écritures fiscales.
- **FR-012** : Toute action de caisse (ouverture, sortie, comptage, clôture d'alerte) DOIT figurer au journal technique.
- **FR-013** : L'accès DOIT être limité aux responsables ; un serveur ne voit que sa propre session et ses propres pourboires.
- **FR-014** : Les trois clients (web, iPad serveur, iPad autonome) DOIVENT calculer l'espèce attendue, l'écart et les seuils de la même manière, avec les mêmes arrondis.

### Entités principales

- **Session de caisse** : poste, ouverture (heure, auteur, fond), clôture (heure, auteur, comptage, écart).
- **Mouvement de caisse** : entrée ou sortie d'espèces hors vente, avec motif.
- **Seuil d'alerte** : type, valeur, portée (service, serveur, poste), activation.
- **Alerte** : seuil franchi, période, détail, état (ouverte, traitée) et note de traitement.
- **Résumé quotidien** : instantané de fin de journée à partir du rapport Z et des alertes.

## Critères de réussite *(obligatoire)*

### Résultats mesurables

- **SC-001** : Le comptage de caisse d'un service se saisit et se clôture en moins de 2 minutes.
- **SC-002** : Sur une journée simulée avec écart et annulations imposés, 100 % des alertes attendues sont créées et aucune n'est dupliquée.
- **SC-003** : L'espèce attendue calculée par chacun des trois clients est identique au centime sur les scénarios de la journée simulée.
- **SC-004** : Le responsable repère une anomalie (écart, annulations, remises) depuis la cloche en moins de 10 secondes.
- **SC-005** : Le résumé de fin de journée et le rapport Z affichent des totaux identiques dans 100 % des cas testés.

## Hypothèses

- Le fond de caisse et le comptage sont des données de gestion, hors chaîne NF525 ; le rapport Z existant garde son contenu signé.
- Un poste a une seule session de caisse ouverte à la fois.
- Les seuils par défaut sont proposés mais désactivés tant que le responsable ne les active pas.
- Le seuil « X % » de l'histoire 3 est un réglage du responsable, avec une valeur par défaut proposée à fixer au plan.
- L'envoi par courriel ou SMS suppose un fournisseur d'envoi à choisir ; le premier lot couvre l'affichage et l'export.
- Les prévisions d'affluence et l'assistant en langage naturel sont hors périmètre (spec ultérieure).
- Les pourboires par serveur réutilisent le calcul d'encaissements existant.
