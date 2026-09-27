# Multilangue (FR / EN / AR) — design

Date : 2026-09-27
Statut : validé en brainstorming, en attente de relecture

## Contexte et objectif

Le POS doit pouvoir être vendu hors de France. Aujourd'hui, tout est en français en dur :
web (`wwwroot/app.js`, `index.html`), iOS (`Text("...")` SwiftUI), messages d'erreur serveur
(~50 `{ Message = "..." }`) et tickets imprimés (`TakeawayTicketFormatter`). Aucune infrastructure
i18n n'existe.

Le projet global est découpé en deux sous-projets :

- **A. Langue** (ce document) : FR, EN, AR avec mise en page RTL.
- **B. Devise** (spec séparée, après A) : réglage restaurant « devise », devises à 2 décimales
  uniquement (EUR, MAD, DZD, USD, AED, SAR…). `Money` reste en centimes. Les devises à 3 décimales
  (TND, KWD…) sont exclues ; ce serait une extension future.

## Décisions

| Sujet | Décision |
|---|---|
| Langues | `en` (défaut et repli), `fr`, `ar` |
| Périmètre | UI des clients + messages serveur + tickets imprimés |
| Hors périmètre | Données saisies (produits, catégories, tables, opérateurs) jamais traduites ; adaptation fiscale par pays (NF525, FEC, TVA restent France, seulement traduits) ; devise (sous-projet B) |
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
- Impression (`TakeawayTicketFormatter`, tickets cuisine) : libellés via le localizer avec la
  culture `ReceiptLanguage`, indépendante de la langue de la requête. Le centrage par espaces en
  dur est remplacé par un centrage calculé sur la largeur du ticket (40 colonnes).
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

## Tickets imprimés en arabe

Le serveur produit du texte Unicode en ordre logique. Le rendu réel (liaison des lettres, sens
d'écriture) dépend de l'imprimante et du client qui imprime ; il est **hors périmètre**. Une
imprimante qui ne gère pas l'arabe doit garder `ReceiptLanguage` à `en` ou `fr`. Ce risque figure
dans la documentation du réglage.

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
  `PUT /api/settings` puis ticket formaté dans la langue choisie ; valeur invalide → 400 ;
  initialisation `fr` sur base existante et `en` sur base vide ; hash NF525 identique quelle que
  soit la langue ; chaque clé de `SharedResource.resx` existe dans `fr` et `ar`.
- **iOS unit** : les String Catalogs n'ont aucune entrée `fr`/`ar` manquante ou vide.
- **iOS UI** : lancement avec `-AppleLanguages (ar)`, vérification d'un libellé.
- **Contrat** : fixture `settings.json` + modèle Swift `RestaurantSettings` décodé par les tests de
  contrat.

## Ordre de livraison

1. Serveur : localization, `.resx`, `RestaurantSettings` + endpoints, tickets.
2. Web : `t()`, extraction des chaînes en clés, dictionnaires `en`/`fr`, RTL CSS, sélecteur.
3. iOS : String Catalogs, extraction en clés, `Accept-Language`, réglage langue tickets dans Admin,
   exceptions LTR.
4. Dictionnaires `ar` complets et scripts de vérification au vert.

Chaque étape est livrable seule. Les tests existants sont épinglés en français dès l'étape 2 et 3.
Une installation française existante garde ses tickets en français ; ses appareils affichent la
langue de leur système (français sur un appareil réglé en français).
