# Revue des fonctions manquantes (13/11/2026)

Méthode : recherche lexicale dans le code (.NET, web, iPad) et lecture des docs. Rien n'a été exécuté. Une fonction est dite manquante si elle est introuvable dans le code ou si la doc la déclare « pas faite ». Un mot différent dans le code peut en cacher une existante : la fusion de tables côté web est le cas le plus incertain.

## Manques qui gênent le service

| # | Fonction manquante | Preuve |
|---|---|---|
| 1 | Annuler une ligne déjà envoyée en cuisine (motif, droit manager, bon d'annulation, JET) | Le serveur ne fait qu'ajouter des lignes. Seul `compItem` (offert) existe. `User.CanVoidItems()` n'a aucun appelant. Conception : `docs/superpowers/specs/2026-11-13-annulation-ligne-envoyee-design.md`. |
| 2 | Remboursement automatique de la carte à l'annulation d'un reçu | `docs/tpe.md` : « non fait ». |
| 3 | Plafond de la carte au dû, côté .NET | `docs/tpe.md` : « à faire ». |
| 4 | Restauration d'une sauvegarde `.posbak` depuis l'iPad autonome | `LocalBackupService.restore` est interne, aucun écran (constat E13). |
| 5 | Fusion de tables côté web | `MergeTables` en .NET et une fois côté iPad, aucune occurrence dans `app.js` : à confirmer à l'écran. |

## Manques commerciaux

Facture nominative (client ou société, n° de TVA du client ; seul le n° de l'établissement figure sur le ticket). Ticket par e-mail ou SMS. Fiche client et fidélité. Export des ventes en CSV ou Excel hors FEC. Commande et paiement par QR à la table (spec 022 en brouillon, sans code). Livraison (l'enum `OrderDestination.Delivery` existe, sans parcours). Pointage et planning du personnel.

## Manques documentés et assumés

Stock : un seul dépôt, pas de commandes fournisseur ni de DLC et lots, consommation au règlement et non à l'envoi en cuisine. TPE : un seul lecteur, un encaissement à la fois, pas de terminaux réseau (Concert/Nexo), Stripe seulement en mode autonome.

## Priorité faible

Afficheur client, multi-établissement, notifications push, répartition des pourboires (le tableau de bord les affiche par serveur).

## Constats de la revue du 26/10 encore ouverts (revérifiés le 13/11)

E5 facturation chambre sans appareil appairé ; E7 `/sync/receipt` sans clé d'idempotence ; E12 `NSAllowsArbitraryLoads` dans `project.yml` ; E14 sur l'iPad, annuler un règlement partiel passe la commande en `Cancelled` sans libérer la table ; M8 pas de verrouillage automatique de l'iPad ; C3 le serveur lit encore `input.UnitPrice` pour les lignes créées à la volée. Non revérifiés : E9, E15, C4, M3.

Réglé depuis : étape 1 (C1, C2, E1 à E4, E8), hub cuisine avec jeton (E10), rapports X et Z du terminal appairé, plan de salle sous connexion.

## Ordre proposé

1. Annulation de ligne envoyée. 2. iPad autonome : E14 puis restauration de sauvegarde. 3. Remboursement TPE et plafond. 4. Backlog sécurité E5, E7, C3 (touche la TVA : à valider avant). 5. Manques commerciaux selon la demande (facture nominative en premier).
