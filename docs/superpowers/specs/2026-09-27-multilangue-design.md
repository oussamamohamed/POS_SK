# Multilangue (FR / EN / AR) — design

Date : 2026-09-27
Statut : validé en brainstorming, en attente de relecture

## Contexte et objectif

Le POS doit pouvoir être vendu hors de France. Aujourd'hui, tout est en français en dur :
web (`wwwroot/app.js`, `index.html`), iOS (`Text("...")` SwiftUI), messages d'erreur serveur
(~50 `{ Message = "..." }`) et gabarits de tickets (`TakeawayTicketFormatter`). Aucune infrastructure
i18n n'existe.

Constat : aucun ticket n'est réellement imprimé aujourd'hui. `TakeawayTicketFormatter` n'est
appelé nulle part ; le paiement comptoir renvoie seulement les indicateurs `printPickupVoucher` et
`printFiscalReceipt` ; le seul envoi réel vers une imprimante est l'impression de test
(`PrinterConfigurationService.SendTestPrintAsync`, ESC/POS brut en TCP 9100).

Le projet global est découpé en trois sous-projets, livrés dans l'ordre A → C → B :

- **A. Langue** (ce document) : FR, EN, AR avec mise en page RTL.
- **C. Impression** (spec séparée, après A) : voir la section « Impression » ci-dessous.
- **B. Devise** (spec séparée, après C) : réglage restaurant « devise », devises à 2 décimales
  uniquement (EUR, MAD, DZD, USD, AED, SAR…). `Money` reste en centimes. Les devises à 3 décimales
  (TND, KWD…) sont exclues ; ce serait une extension future.

## Décisions

| Sujet | Décision |
|---|---|
| Langues | `en` (défaut et repli), `fr`, `ar` |
| Périmètre | UI des clients + messages serveur + tickets imprimés |
| Hors périmètre | Données saisies (produits, catégories, tables, opérateurs) jamais traduites ; adaptation fiscale par pays (NF525, FEC, TVA restent France, seulement traduits) ; impression réelle des tickets (sous-projet C) ; devise (sous-projet B) |
| Choix langue UI | Par appareil |
| Langue des tickets | Réglage restaurant unique, côté serveur |
| Erreurs serveur | Traduites par le serveur selon `Accept-Language` |
| Clés | Clés sémantiques uniques (`payment.submit`), sur les 3 couches |
| Chiffres | Occidentaux (0-9), y compris en arabe |
| Arabe | Arabe standard moderne |

## Architecture

### Convention commune

- Chaque chaîne a une clé sémantique unique : `<zone>.<nom>` en `snake_case`, par exemple
  `payment.submit`, `order.hold_empty_cart`, `errors.order_not_found`, `receipt.pickup_title`.
- Les zones suivent les écrans et domaines existants (`login`, `floor`, `order`, `payment`,
  `kitchen`, `fiscal`, `admin`, `receipt`, `errors`, `common`).
- Une même clé désigne le même texte sur le web, l'iPad et le serveur quand le texte est partagé
  (ex. `errors.*`), mais chaque couche a ses propres fichiers de traduction.
- Paramètres par placeholder nommé : `"No grid for category {id}"`.
- Chaque clé a une valeur dans les trois langues. Langue non supportée ou valeur absente → anglais.
  Clé absente même en anglais → la clé elle-même est affichée (erreur visible, détectée par les
  tests de complétude).

### Web (`src/RestaurantPos.Api/wwwroot/`)

- `i18n/en.json`, `i18n/fr.json`, `i18n/ar.json` : objets plats `{ "payment.submit": "Pay" }`.
- `app.js` : fonction `t(key, params)` qui cherche la clé dans la langue courante, sinon en
  anglais, sinon renvoie la clé ; puis remplace les `{nom}`. Toutes les chaînes UI en dur sont
  remplacées par `t('zone.nom')`.
- `index.html` : attributs `data-i18n` (texte), `data-i18n-placeholder`, `data-i18n-aria-label`
  contenant la clé, appliqués au démarrage.
- Chargement : le dictionnaire de la langue courante et `en.json` (repli) sont chargés avant le
  premier rendu.
- Langue courante : `localStorage` → sinon `navigator.language` (préfixe `en`/`fr`/`ar`) → sinon
  `en`.
- Sélecteur de langue sur l'écran de connexion PIN (accessible avant login). Changer de langue
  recharge la page.
- Au démarrage : `<html lang="…" dir="ltr|rtl">`.
- Toute requête `fetch` vers l'API envoie `Accept-Language: <langue courante>`.
- Dates et nombres : `Intl` / `toLocaleString` avec la langue courante ; en arabe, locale
  `ar-u-nu-latn` pour forcer les chiffres occidentaux.

### iOS (`ios/`)

- `Localizable.xcstrings` (String Catalog) dans la cible app et un dans `PosKit` pour les chaînes
  produites par les stores et les erreurs. Langue de développement `en`. `project.yml` déclare
  `en`, `fr`, `ar`.
- Les vues utilisent les clés : `Text("payment.submit")`, `String(localized: "payment.submit")`.
  Le catalogue porte la valeur anglaise de chaque clé, plus `fr` et `ar`.
- Langue par appareil : réglage natif iOS (Réglages → RestaurantPOS → Langue). Aucun sélecteur
  dans l'app.
- `HTTPPosAPI` pose `Accept-Language` à partir de `Bundle.main.preferredLocalizations.first`.
- `Money.swift` formate avec la locale courante (chiffres latins) ; la devise reste `EUR` jusqu'au
  sous-projet B.
- Écran Admin : réglage « Langue des tickets » (lecture/écriture de `/api/settings`).

### Serveur (.NET)

- `Program.cs` : `AddLocalization` + `UseRequestLocalization` avec cultures `en`, `fr`, `ar`,
  défaut `en`.
- `IStringLocalizer<SharedResource>` avec `SharedResource.resx` (neutre = anglais),
  `SharedResource.fr.resx` et `SharedResource.ar.resx` dans `RestaurantPos.Api`. Une valeur absente
  dans `fr`/`ar` retombe sur l'anglais.
- Les ~50 messages d'erreur des endpoints deviennent `localizer["errors.xxx"]`. Le contrat JSON
  des erreurs ne change pas (`Message`/`message` reste un texte lisible, déjà traduit).
- Nouvelle entité `RestaurantSettings` (ligne unique) : `ReceiptLanguage`. Table créée par
  `EnsureCreated()` et par un `CREATE TABLE IF NOT EXISTS` dans le bloc de schéma de
  `Program.cs`. Le sous-projet B y ajoutera la devise.
- Initialisation de `ReceiptLanguage` quand la ligne n'existe pas encore : `fr` si la base
  contient déjà des commandes (installation française existante, aucun changement visible sur ses
  tickets), sinon `en` (nouvelle installation).
- Endpoints `GET /api/settings` (authentifié) et `PUT /api/settings` (manager/admin). Valeurs
  acceptées : `en`, `fr`, `ar` ; autre valeur → 400.
- Tickets : voir la section « Impression ».
- Chaîne NF525 : aucun changement. Le hash ne contient aucun libellé.

## RTL

### Web

- Les 13 occurrences `left`/`right` de `styles.css` et les 14 styles inline de `app.js`/`index.html`
  passent en propriétés logiques (`inset-inline-start`, `margin-inline-end`, `text-align: start`…).
  Flexbox et grid s'inversent seuls.
- Icônes directionnelles (retour, chevrons) : `transform: scaleX(-1)` sous `[dir="rtl"]`.
- Montants, PIN, numéros de ticket et pavés numériques : `dir="ltr"` forcé.

### iOS

- Miroir automatique SwiftUI.
- Pavés numériques et montants : `.environment(\.layoutDirection, .leftToRight)`.

## Impression

### Dans ce sous-projet (A)

- `TakeawayTicketFormatter` (texte brut, code mort) est remplacé par un **modèle de ticket
  structuré**, indépendant de la façon d'imprimer : `TicketDocument` = liste de lignes, chaque
  ligne étant soit un texte (alignement début/centre/fin, gras, grande taille), soit deux colonnes
  (libellé à gauche, valeur à droite, inversées en RTL), soit un séparateur.
- Un constructeur produit ce modèle pour le bon de retrait et le ticket de caisse, avec les
  libellés `receipt.*` résolus par le localizer dans la culture `ReceiptLanguage`, indépendante de
  la langue de la requête. `TicketDocument` porte sa direction (`rtl` si `ar`).
- Rien n'est envoyé à une imprimante dans A.

### Décisions reportées au sous-projet C (Impression)

- Rendu du `TicketDocument` en **image** côté serveur, pour toutes les langues : l'arabe (liaison
  des lettres, sens d'écriture) et les accents français s'impriment correctement sans driver ni
  page de caractères. Le code actuel envoie de l'UTF-8 que les imprimantes ESC/POS ne comprennent
  pas.
- Bibliothèques : SkiaSharp pour le dessin, Topten.RichTextKit (HarfBuzz) pour la mise en forme
  bidirectionnelle (arabe mêlé de chiffres et de noms latins). Police arabe (famille Noto, licence
  OFL) fournie avec l'application.
- Envoi en ESC/POS image (`GS v 0`) **par bandes** (~256 lignes) pour ne pas saturer la mémoire
  des imprimantes d'entrée de gamme ; tiroir-caisse et coupe papier inchangés.
- Estimation : ~115 Ko par ticket de caisse en 80 mm (576 points de large), transfert < 10 ms en
  100 Mbit/s ; vitesse identique au texte sur Epson TM-T20III/TM-T88 ou Star TSP143, 30 à 50 %
  plus lente possible sur Xprinter et génériques.
- À valider sur une imprimante d'entrée de gamme. Si trop lent : mode texte avec bonne page de
  caractères pour `en`/`fr`, image seulement pour `ar`.
- C couvre aussi : quels documents imprimer et quand (bon de retrait, ticket de caisse, ticket
  cuisine), acheminement vers l'imprimante du poste (`AssignedStationIds`), gestion des pannes
  (imprimante éteinte, papier vide, relance).

## Cas limites

- Valeur absente dans la langue courante → anglais ; `console.warn` en développement sur le web.
- Clé absente partout → la clé est affichée telle quelle (bug visible, bloqué par les tests).
- `Accept-Language` non supporté (ex. `de`) → anglais.
- Placeholder `{x}` sans valeur → laissé tel quel, pas d'exception.

## Traductions

Les valeurs `en` et `fr` sont rédigées pendant l'extraction (le `fr` reprend les textes actuels).
La version arabe doit être relue par un arabophone avant la mise en production (terminologie
restauration locale).

## Tests

- **Web, complétude** : `scripts/i18n-check.mjs` extrait les clés `t('…')` et `data-i18n*` et
  signale les clés absentes de l'un des trois dictionnaires, les valeurs vides et les clés
  orphelines. Code de sortie non nul en cas d'écart.
- **Tests existants** : les tests E2E web et UI iOS vérifient des libellés français. Ils sont
  épinglés en français : `locale: 'fr-FR'` dans `playwright.config.ts`, `-AppleLanguages (fr)` dans
  les lancements XCUITest.
- **Web E2E** : `tests/RestaurantPos.Web.E2ETests/tests/i18n.spec.ts` — langue `ar` →
  `<html dir="rtl">`, libellé arabe visible, pavé PIN en LTR ; langue `en` → libellé anglais ;
  langue navigateur `de` → anglais.
- **Serveur** : `Accept-Language: fr` → message d'erreur français ; `de` → anglais ;
  `PUT /api/settings` puis `TicketDocument` construit avec les libellés de la langue choisie et
  direction `rtl` en arabe ; valeur invalide → 400 ;
  initialisation `fr` sur base existante et `en` sur base vide ; hash NF525 identique quelle que
  soit la langue ; chaque clé de `SharedResource.resx` existe dans `fr` et `ar`.
- **iOS unit** : les String Catalogs n'ont aucune entrée `fr`/`ar` manquante ou vide.
- **iOS UI** : lancement avec `-AppleLanguages (ar)`, vérification d'un libellé.
- **Contrat** : fixture `settings.json` + modèle Swift `RestaurantSettings` décodé par les tests de
  contrat.

## Ordre de livraison

1. Serveur : localization, `.resx`, `RestaurantSettings` + endpoints, `TicketDocument`.
2. Web : `t()`, extraction des chaînes en clés, dictionnaires `en`/`fr`, RTL CSS, sélecteur.
3. iOS : String Catalogs, extraction en clés, `Accept-Language`, réglage langue tickets dans Admin,
   exceptions LTR.
4. Dictionnaires `ar` complets et scripts de vérification au vert.

Chaque étape est livrable seule. Les tests existants sont épinglés en français dès l'étape 2 et 3.
Une installation française existante garde ses tickets en français ; ses appareils affichent la
langue de leur système (français sur un appareil réglé en français).
