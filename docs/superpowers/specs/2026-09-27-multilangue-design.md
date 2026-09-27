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
| Langues | `fr` (source, défaut), `en`, `ar` |
| Périmètre | UI des clients + messages serveur + tickets imprimés |
| Hors périmètre | Données saisies (produits, catégories, tables, opérateurs) jamais traduites ; adaptation fiscale par pays (NF525, FEC, TVA restent France, seulement traduits) ; devise (sous-projet B) |
| Choix langue UI | Par appareil |
| Langue des tickets | Réglage restaurant unique, côté serveur |
| Erreurs serveur | Traduites par le serveur selon `Accept-Language` |
| Clés | Texte français source = clé, sur les 3 couches |
| Chiffres | Occidentaux (0-9), y compris en arabe |
| Arabe | Arabe standard moderne |

## Architecture

### Convention commune

- La clé d'une chaîne est son texte français exact.
- Paramètres par placeholder nommé : `"Aucune grille pour la catégorie {id}"`.
- Langue ou clé absente → texte français.

### Web (`src/RestaurantPos.Api/wwwroot/`)

- `i18n/en.json`, `i18n/ar.json` : objets `{ "texte FR": "traduction" }`. Pas de dictionnaire FR.
- `app.js` : fonction `t(fr, params)` qui cherche la traduction, remplace les `{nom}`, sinon renvoie
  `fr`. Toutes les chaînes UI en dur passent par `t('…')`.
- `index.html` : attributs `data-i18n` (texte), `data-i18n-placeholder`, `data-i18n-aria-label`,
  appliqués au démarrage.
- Langue courante : `localStorage` → sinon `navigator.language` (préfixe `fr`/`en`/`ar`) → sinon `fr`.
- Sélecteur de langue sur l'écran de connexion PIN (accessible avant login). Changer de langue
  recharge la page.
- Au démarrage : `<html lang="…" dir="ltr|rtl">`.
- Toute requête `fetch` vers l'API envoie `Accept-Language: <langue courante>`.
- Dates et nombres : `Intl` / `toLocaleString` avec la langue courante ; en arabe, locale
  `ar-u-nu-latn` pour forcer les chiffres occidentaux.

### iOS (`ios/`)

- `Localizable.xcstrings` (String Catalog) dans la cible app et un dans `PosKit` pour les chaînes
  produites par les stores et les erreurs. Langue de développement `fr`. `project.yml` déclare
  `fr`, `en`, `ar`.
- Langue par appareil : réglage natif iOS (Réglages → RestaurantPOS → Langue). Aucun sélecteur
  dans l'app.
- `HTTPPosAPI` pose `Accept-Language` à partir de `Bundle.main.preferredLocalizations.first`.
- `Money.swift` formate avec la locale courante (chiffres latins) ; la devise reste `EUR` jusqu'au
  sous-projet B.
- Écran Admin : réglage « Langue des tickets » (lecture/écriture de `/api/settings`).

### Serveur (.NET)

- `Program.cs` : `AddLocalization` + `UseRequestLocalization` avec cultures `fr`, `en`, `ar`,
  défaut `fr`.
- `IStringLocalizer<SharedResource>` avec `SharedResource.en.resx` et `SharedResource.ar.resx`
  dans `RestaurantPos.Api`. Clé = message français actuel ; une clé absente renvoie le français.
  Les ~50 messages d'erreur des endpoints passent par le localizer. Le contrat JSON des erreurs ne
  change pas.
- Nouvelle entité `RestaurantSettings` (ligne unique) : `ReceiptLanguage` (`fr` par défaut).
  Table créée par `EnsureCreated()` et par un `CREATE TABLE IF NOT EXISTS` dans le bloc de schéma
  de `Program.cs`. Le sous-projet B y ajoutera la devise.
- Endpoints `GET /api/settings` (authentifié) et `PUT /api/settings` (manager/admin). Valeurs
  acceptées : `fr`, `en`, `ar` ; autre valeur → 400.
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
imprimante qui ne gère pas l'arabe doit garder `ReceiptLanguage` à `fr` ou `en`. Ce risque figure
dans la documentation du réglage.

## Cas limites

- Clé absente d'un dictionnaire web → français affiché, `console.warn` en développement.
- iOS et serveur retombent nativement sur le texte source.
- `Accept-Language` non supporté (ex. `de`) → français.
- Placeholder `{x}` sans valeur → laissé tel quel, pas d'exception.

## Traductions

EN et AR sont rédigées pendant l'implémentation. La version arabe doit être relue par un
arabophone avant la mise en production (terminologie restauration locale).

## Tests

- **Web, complétude** : `scripts/i18n-check.mjs` extrait les clés `t('…')` et `data-i18n*` et
  signale les clés absentes de `en.json`/`ar.json` et les clés orphelines. Code de sortie non nul
  en cas d'écart.
- **Web E2E** : `tests/RestaurantPos.Web.E2ETests/tests/i18n.spec.ts` — langue `ar` →
  `<html dir="rtl">`, libellé arabe visible, pavé PIN en LTR ; langue `en` → libellé anglais.
- **Serveur** : `Accept-Language: en` → message d'erreur anglais ; `de` → français ;
  `PUT /api/settings` puis ticket formaté en anglais ; valeur invalide → 400 ; hash NF525 identique
  quelle que soit la langue.
- **iOS unit** : les String Catalogs n'ont aucune entrée `en`/`ar` manquante ou vide.
- **iOS UI** : lancement avec `-AppleLanguages (ar)`, vérification d'un libellé ; les tests
  existants restent en français.
- **Contrat** : fixture `settings.json` + modèle Swift `RestaurantSettings` décodé par les tests de
  contrat.

## Ordre de livraison

1. Serveur : localization, `.resx`, `RestaurantSettings` + endpoints, tickets.
2. Web : `t()`, extraction des chaînes, dictionnaires, RTL CSS, sélecteur.
3. iOS : String Catalogs, `Accept-Language`, réglage langue tickets dans Admin, exceptions LTR.
4. Traductions complètes et script de vérification au vert.

Chaque étape est livrable seule : le français reste la langue par défaut, donc aucune régression
visible.
