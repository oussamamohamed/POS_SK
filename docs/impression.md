# Impression des tickets

Spec : `docs/superpowers/specs/2026-09-29-impression-design.md`.

## 1. Principe

- Après un paiement ou un envoi en cuisine, `PrintDispatcher` met des jobs en file dans la table `PrintJobs`.
  La mise en file ne peut jamais faire échouer la vente ; en cas d'erreur elle est journalisée.
- `PrintWorker` (service d'arrière-plan, absent en environnement `Testing`) traite chaque imprimante à part, dans
  l'ordre de création (FIFO). Un job en attente de relance bloque les suivants de la même imprimante ; les autres imprimantes
  continuent. Le rendu est une image 1 bit (`Infrastructure/Printing/EscPosRasterRenderer.cs`) envoyée en ESC/POS
  par TCP (port 9100 par défaut ; connexion 3 s, écriture 10 s).
- Relances après échec : +5 s, +15 s, +30 s, puis toutes les 60 s. Échéance : 30 min après la création (ou la
  relance manuelle), ensuite le job passe `Failed`.
- Au démarrage du serveur, les jobs `Pending` sont repris. Imprimante désactivée ou supprimée : ses jobs
  `Pending` passent `Cancelled`.
- Purge quotidienne des jobs `Sent` et `Cancelled` de plus de 7 jours. Les `Failed` sont conservés.

## 2. Configuration

- **Imprimantes** (Gestion → Imprimantes) : IP fixe, port 9100, largeur 58 ou 80 mm, option « ouvrir le tiroir
  ». Une imprimante peut être rattachée à plusieurs postes.
- **Postes** : `RECEIPT` pour les imprimantes de caisse ; `HOT_KITCHEN`, `COLD`, `GRILL`, `DESSERT`, `BAR` pour
  la préparation. Chaque imprimante active d'un poste de préparation reçoit une copie de chaque bon.
- **Poste d'une ligne** : ligne de commande, sinon article, sinon famille, sinon `HOT_KITCHEN`. Régler le poste
  des familles (Gestion → Catalogue) ; laisser vide le poste d'un article pour qu'il suive sa famille.
- **Imprimante de ticket d'une caisse** : Gestion → Appareils (web), sélecteur « Imprimante de ticket ». Vide =
  première imprimante active du poste `RECEIPT` (ordre alphabétique).
- **Langues** : Gestion → Réglages, « Langue des tickets » (`en`/`fr`/`ar`) et « Langue des bons cuisine »
  (par défaut celle des tickets).

## 3. Ce qui s'imprime quand

| Situation | Document | Tiroir |
|---|---|---|
| Vente comptoir, ticket non demandé | Bon de retrait | Si espèces et imprimante avec tiroir |
| Vente comptoir, ticket demandé | Ticket de caisse + bon de retrait (un seul job) | Idem |
| Paiement à table, case « Imprimer le ticket de caisse » cochée et commande entièrement réglée | Ticket de caisse | Idem |
| Paiement à table, case décochée ou paiement partiel | Rien | Non |
| Envoi en cuisine (direct, suite, dessert, réclame) | Un bon par poste, une copie par imprimante du poste | Non |

## 4. Dépannage

- **Gestion → Imprimantes** : état de chaque imprimante (en ligne / hors ligne depuis HH:MM / inconnu), jobs en
  attente et en échec, boutons Relancer (`Failed` → `Pending`, nouvelle échéance +30 min) et Annuler.
- API : `GET /api/printers/{id}/jobs?status=`, `POST /api/print-jobs/{id}/retry` et
  `POST /api/print-jobs/{id}/cancel` exigent manager/admin ; `GET /api/printers/status` et
  `POST /api/printers/{id}/test` (impression de test) sont ouverts à tout utilisateur authentifié.
- `printQueued: false` dans la réponse d'un paiement : aucune imprimante résolue (caisse sans imprimante de ticket
  et aucune imprimante active sur `RECEIPT`), erreur de mise en file (voir les journaux du serveur), paiement
  partiel (comptoir ou table), ou paiement à table sans ticket demandé.
- Bons cuisine absents : le poste n'a aucune imprimante active (avertissement dans les journaux), ou le poste
  résolu n'est pas celui attendu (vérifier famille et article).
- Notification « hors ligne — n bons en attente » : l'imprimante ne répond plus ; les bons sortent dans l'ordre
  au rétablissement. Le temps réel passe par `OnPrinterStatusChanged` sur `/hubs/pos` (hub authentifié : le
  client web ouvre cette connexion après la connexion de l'opérateur, en plus de `/hubs/kitchen`).

### Essai local sans imprimante

Déclarer une imprimante sur `127.0.0.1:9100`, puis `nc -lk 9100 > /tmp/print.bin`, lancer une vente et vérifier
`xxd /tmp/print.bin | head -2` : le flux commence par `1b40 1d76 30` (`ESC @`, `GS v 0`). Procédure vérifiée
lors du développement (Task 8) ; cela ne remplace pas l'essai ci-dessous.

## 5. Mode texte

- Case « Mode texte (fr/en, plus rapide) » par imprimante (Gestion → Imprimantes). Décochée (défaut) : rendu image, comme décrit en 1.
  Cochée : le ticket part en texte ESC/POS (page de code PC858), nettement plus rapide sur les imprimantes lentes.
- Langues `fr` et `en` seulement. L'arabe (`ar`, écriture de droite à gauche) reste toujours en image, même case
  cochée : il exige la mise en forme des lettres liées. Le choix se fait par document (`PrinterTransport.BuildPayload`).
- Colonnes : 32 en 58 mm, 48 en 80 mm. Les libellés longs sont repliés sur plusieurs lignes, sans perte.
- Caractère absent de PC858 : imprimé `?`. Si les accents ou « € » sortent faux, la page de code de l'imprimante ne
  correspond pas : décocher la case et vérifier avec l'impression de test.

## 6. Rapports imprimés

- **Rapport X** : bouton « Imprimer le rapport X » (web et iPad), `POST /api/fiscal/x-report/print`.
- **Clôture Z** : imprimée automatiquement à la clôture (`PrintQueued` dans la réponse) ; bouton « Réimprimer la dernière clôture » pour
  la dernière Z du poste (`POST /api/fiscal/latest-closure/print`). Manager/admin.
- **Imprimante** : celle du poste (Gestion → Appareils), sinon première imprimante active du poste `RECEIPT`.
  Aucune imprimante : `printQueued: false`. Pas de tiroir. Langue des tickets.
- **Contenu** : poste, période, nombre de tickets, total TTC et HT, TVA par taux, règlements par moyen, cumul
  perpétuel ; puis articles vendus par famille avec sous-totaux (famille inconnue : « Autres »), puis pourboires par
  serveur avec total. Z : gestionnaire et signature en plus.
- Articles et pourboires sont informatifs : ils n'entrent ni dans les totaux fiscaux ni dans la signature Z.
  Articles en TTC, offerts à 0. Une remise sur la note entière n'est pas répartie : les montants articles sont avant
  remise, et le rapport l'indique.

## 7. Règles de clôture

- **Tout encaisser avant la Z** : tant qu'une commande avec des lignes reste à encaisser (table ou panier en attente),
  `POST /api/fiscal/z-closure` répond `409 {"code":"open_orders"}` avec un message listant tables et paniers et les
  montants restants. Ne comptent pas : commandes vides, payées ou annulées, paniers annulés au code superviseur,
  commandes entièrement offertes.
- **Pas d'annulation après Z** : un ticket d'une période déjà clôturée ne peut plus être annulé
  (`409 {"code":"void_after_closure"}`). La Z du terminal principal couvre les tickets de tous les terminaux : un ticket
  d'iPad couvert par cette Z n'est plus annulable non plus. Corriger par avoir.

## 8. Pourboire à table

- Saisi sur le paiement qui solde la note, pas sur une part de partage : sinon
  refus 400 « Le pourboire ne peut être ajouté qu'au paiement qui solde la note » (les règlements doivent couvrir le
  reste dû plus le pourboire).
- Attribué au serveur de la table (opérateur de la commande). Au comptoir : à l'opérateur qui encaisse.
- Le rapport X/Z le regroupe par serveur (section 6).

## 9. Essai manuel sur imprimante réelle (à dérouler par l'exploitant)

Non exécuté pendant le développement (pas de matériel). Noter modèle et résultats.

Imprimante : ______  Largeur : ______

- [ ] Impression de test en `fr` : accents corrects ; en `ar` : lettres liées, alignement à droite.
- [ ] Bandes : ticket long (> 256 lignes de points) sans raccord visible entre les bandes.
- [ ] Vente comptoir espèces, sans ticket : bon de retrait + ouverture du tiroir.
- [ ] Vente comptoir carte, ticket demandé : ticket de caisse, coupe, bon de retrait ; pas de tiroir.
- [ ] Paiement à table, case cochée : ticket de caisse seul ; case décochée : rien.
- [ ] Envoi en cuisine d'une table de 6 lignes sur 2 postes : un bon par poste, une copie par imprimante du poste.
- [ ] Coupe du papier après chaque document.
- [ ] Débrancher l'imprimante cuisine, envoyer 3 bons : notification « hors ligne — 3 bons en attente » ;
      rebrancher : les 3 bons sortent dans l'ordre, notification « rétablie ».
- [ ] Chronométrer un ticket de caisse de 15 lignes. Modèle : ______ Durée : ______ s. Au-delà de ~3 s, ouvrir le
      sous-projet « mode texte `en`/`fr` » prévu par la spec A.
- [ ] Impression de test en mode texte, `fr` : accents et « € » corrects ; `ar` : sortie en image.
- [ ] Même ticket de caisse de 15 lignes en image puis en texte : noter les deux durées et le modèle d'imprimante.
- [ ] Rapport X en 58 mm et en 80 mm : colonnes alignées, signature et libellés longs repliés sans perte.
- [ ] Clôture Z avec une table ouverte : refus et liste ; après encaissement : Z imprimée automatiquement.

## 10. Licence des polices

Noto Sans et Noto Sans Arabic, licence OFL : `src/RestaurantPos.Infrastructure/Printing/Fonts/OFL.txt`.
