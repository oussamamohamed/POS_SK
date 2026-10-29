# Revue totale, contre-vérifiée (26/10/2026)

Code revu : `main` à `fe4dc18` (fusion du lot 4d), sans changement depuis la revue du 25/10.
Méthode : les 39 constats des trois rapports du 25/10 ont été relus. Chaque constat Critique ou Élevé a été revérifié ligne par ligne dans le code ; les Moyens et Faibles l'ont été par échantillon. J'ai cherché en plus ce qui avait été manqué (routes anonymes, validation JWT, relais SignalR). Lecture seule : rien n'a été compilé, exécuté ni corrigé.

Légende du statut : **✔ confirmé** (vu dans le code) · **~ plausible** (la cause est dans le code, l'effet n'a pas été observé) · **↻ nuancé** (le constat d'origine est corrigé ici) · **★ nouveau**.

---

## 1. Critique

| # | Constat | Où | Statut |
|---|---|---|---|
| C1 | **Connexion manager implicite avec le PIN `1234`**. `ensureAuthToken()` est appelée 28 fois. Le bouton de verrouillage ouvre seulement la modale et garde le JWT. Le compte `1234` est créé aussi en Release (branche `#else`). | `app.js:967-987`, `app.js:433-437`, `Program.cs:441` | ✔ |
| C2 | **XSS stockée** : nom de produit, modificateurs, commentaire cuisine et libellé d'attente vont dans `innerHTML` sans échappement. `escapeHtml` n'est utilisé que 17 fois. | `app.js:1222, 1233-1234, 2229` (et ~20 autres) | ✔ |
| C3 | **Prix et TVA fournis par le client** : avec `IsHappyHourApplied`, le serveur reprend `UnitPrice`. Pour un produit inconnu, le taux de TVA vient du client. `ModifiersPriceExtra` vient toujours du client. `/checkout/pay` et `/sync/receipt` créent une « Vente » à **10 % codé en dur**. | `TableManagementService.cs:380-400`, `CheckoutEndpoints.cs:83`, `SyncEndpoints.cs:118` | ✔ |
| C4 | **Chaîne fiscale en SHA-256 sans clé** : quiconque peut écrire dans la base recalcule la chaîne. Les règlements, le HT, le numéro et le type d'opération ne sont pas signés. Le défaut existe côté .NET et côté iPad. | `FiscalHashing.cs:23`, `Local/FiscalHashing.swift` | ✔ (choix de conception : à arbitrer avec le certificateur) |

## 2. Élevé

| # | Constat | Où | Statut |
|---|---|---|---|
| E1 | **Le rôle Admin ne protège rien** : une seule politique `RequireManagerOrAdmin`. Un manager peut créer un Admin (le rôle est lu du corps), et un rôle invalide devient Waiter. `POST /api/admin/seed-test-data` (manager) lance `force: true`, qui fait `RemoveRange` sur les tables et recrée l'Admin `9999`. | `Program.cs:171-177, 467-476, 691-694`, `StaffEndpoints.cs:48-60` | ✔ |
| E2 | **Oracle de PIN superviseur** : `held/{id}/void` n'a pas de limiteur. Un PIN faux répond 403, un PIN bon répond 404. | `CounterSaleEndpoints.cs:188-205` | ✔ |
| E3 | **★ Second oracle de PIN, anonyme** : `POST /api/happy-hour/override/activate` et `/stop` sont `AllowAnonymous`. Ils valident un PIN superviseur avec le même limiteur faible (5 essais, 30 s, par IP), et un succès remet le compteur à zéro. Un poste du LAN sans compte peut donc chercher un PIN manager, puis activer le happy hour, ce qui débloque C3. | `HappyHourEndpoints.cs:42-94` | ★ ✔ |
| E4 | **Login par force brute et DoS** : 5 échecs puis 30 s, en mémoire, par IP. Chaque requête anonyme calcule PBKDF2 pour chaque compte actif. Chaque échec écrit une entrée dans le JET. | `PinRateLimiterService.cs:9-11`, `AuthEndpoints.cs` | ✔ |
| E5 | **Facturation chambre** : aucune exigence `RequirePairedDevice`. Le terminal est codé en dur (`POS_MAIN_TERM`). `Amount` négatif est accepté et crédite le folio. Le résultat du reçu est ignoré, et le folio et le reçu passent dans deux transactions. | `HospitalityEndpoints.cs:169-195`, `RoomBillingService.cs:50-55` | ✔ |
| E6 | **Z web sur `POS_MAIN_TERM`** : les reçus des postes appairés (`T01`…) ne sont jamais clôturés depuis le web. `TerminalId`, `ManagerId` et `ManagerName` de `/fiscal/z-closure` viennent du corps de la requête. | `FiscalEndpoints.cs:34-53`, `NF525FiscalAuditService.cs:50-63, 408-410`, `app.js:3806, 4011, 4074, 4239, 4447` | ✔ |
| E7 | **`/sync/receipt` non idempotent** : `SyncReceiptDto` n'a pas de clé. Un rejeu d'un paiement partiel produit un 2ᵉ reçu signé. La table est libérée sans vérifier qu'il ne reste rien à payer. | `SyncEndpoints.cs:17-23, 138-147` | ✔ |
| E8 | **Le JWT part vers une URL saisie** : le wrapper `fetch` ajoute `Authorization` avant de vérifier l'origine (seul `X-Device-Token` est filtré). « Tester la connexion » appelle `fetch(\`${url}/api/health\`)`. | `app.js:73-100, 4674` | ✔ |
| E9 | **Double tap sur Payer** : ni garde « en cours » ni clé d'idempotence. En note partagée, cela fait deux reçus signés. | `app.js:1952, 2328` | ✔ (client) / ~ (effet) |
| E10 | **Web : `/hubs/kitchen` sans jeton**, alors que le hub exige `RequireKitchenOrAdmin`. `OnHappyHourStatusChanged` est écouté sur `/hubs/kitchen` mais diffusé sur `PosHub`. | `app.js:5515, 5540`, `KitchenHub.cs:11`, `HappyHourEndpoints.cs:65` | ✔ (code) / ~ (401 non observé) |
| E11 | **Jetons dans `localStorage`, pas de CSP, SignalR depuis un CDN sans SRI, CORS `AllowAnyOrigin`, JWT lu en query string (`token`, `access_token`) et en cookie sur toutes les routes.** | `index.html:1822`, `Program.cs:146-156, 189` | ✔ |
| E12 | **Transport en clair** : le script de production lance en HTTP et réamorce les données de test. Sur l'iPad, `NSAllowsArbitraryLoads` est `true` et la redécouverte Bonjour se fait sans authentifier le serveur. | `deploy_production.sh:41, 56`, `Info.plist:33-34`, `project.yml:64`, `ServerBrowser.swift:52` | ✔ |
| E13 | **iPad : aucune restauration `.posbak` dans l'app**. `restore` est `internal`, et son seul usage est sa définition. | `LocalBackupService.swift:53` | ✔ |
| E14 | **iPad : l'annulation d'un règlement partiel bloque la Z**. `voidReceipt` passe la commande en `Cancelled` sans libérer la table. `activeOrder(on:)` ne filtre pas le statut, donc le contrôle Z trouve un reste dû permanent. Le pourboire est ignoré (le .NET le compte). | `LocalPosAPI+Fiscal.swift:24, 104-114`, `LocalPosAPI+Orders.swift:19-25` | ✔ à la lecture, non exécuté |
| E15 | **iPad : horloge non monotone**. Les reçus d'une Z sont sélectionnés par `CreatedAtUtc > fin du dernier Z`, donc reculer l'horloge les exclut. ↻ Pour le verrou PIN, reculer l'horloge le lève aussi (la garde `until <= now + 30 s` le considère expiré). Avancer l'horloge le lève aussi, mais c'est inhérent à toute horloge murale. | `LocalFiscalRepository.swift:277, 302, 335`, `LocalPinGuard.swift:14` | ✔ / ↻ |

## 3. Moyen

| # | Constat | Statut |
|---|---|---|
| M1 | Remise, offert et destination (donc TVA) modifiables après paiement ; duplicata reconstruit depuis la commande actuelle (`OrderDiscountService`, `/destination`, `PrintDispatcher`) | ✔ |
| M2 | .NET : l'annulation d'un reçu partiel met toute la commande en `Cancelled` (`CheckoutPaymentService.cs:309-313`). Même cause que E14. | ✔ |
| M3 | Titres-restaurant : seul le **premier** est contrôlé (`.FirstOrDefault` / `.first(where:)`), .NET et iPad ; la table iPad (`pay`) n'en contrôle **aucun** | ✔ |
| M4 | Relais `PosHub` (`UpdateTableStatus`, `DispatchKitchenTicket`, `BumpTicket`) appelables par tout authentifié : faux tickets et faux statuts | ✔ |
| M5 | Schéma : une soixantaine de `try { ALTER } catch { }` ; immuabilité .NET seulement par l'intercepteur EF, sans trigger SQL (l'iPad en a) | ✔ |
| M6 | Z .NET : une commande en attente déclenche une `InvalidOperationException` (500 au lieu de 409) ; contrôle des commandes ouvertes sur tous les terminaux et hors transaction ; fuseau de l'hôte | ✔ (échantillon) |
| M7 | iPad : PIN à 4 chiffres, verrou sans escalade, oracle « PIN déjà attribué », repli SHA-256 historique sans limite de durée, `backup(to:)` public et non chiffré | ✔ |
| M8 | iPad : `lock()` garde `token` et `api.setToken` ; aucun verrouillage automatique (pas de minuteur d'inactivité, pas de verrouillage en arrière-plan) | ✔ |
| M9 | iPad : un délai d'écriture d'impression relance un ticket **original** (pas de DUPLICATA) | ~ |
| M10 | iPad : acteur unique bloqué par PBKDF2 × N comptes, la sauvegarde `VACUUM INTO` + AES de toute la base, et `connect()` Epson synchrone | ~ |
| M11 | Web : reconnexion SignalR limitée à ~42 s, sans resynchronisation ; brouillons envoyés en double (`pagehide` + `visibilitychange`) ; HT/TVA du panier en flottants sans la remise globale ; encaissement comptoir sans vérifier que le ticket est enregistré (`app.js:2335`) | ✔ / ~ |
| M12 | Web : `app.js` est une closure unique de 5 789 lignes ; la logique monétaire n'est testable que par Playwright | ✔ |

## 4. Faible

Avoirs sans transaction (double usage possible) ; numéros de retrait en mémoire statique ; arrondi bancaire `Math.Round(x*100)` contre `AwayFromZero` ; lectures anonymes (`/api/tables` avec le nom des serveurs, `/api/printers` avec les IP, `/api/kds/tickets`, `/api/happy-hour/schedules`, catalogue, grilles) ; `/sync/batch` non borné ; reconnexion SignalR iPad bloquée à 30 s ; Keychain en « supprimer puis ajouter » ; signet de dossier périmé ignoré ; `staff()` et `printers()` iPad sans `requireAuth` ; `Date()` au lieu de `clock()` dans `Local/`.

## 5. Ce qui tient (vérifié)

Argent en centimes `long`. Transactions sérialisées. Index uniques sur les séquences. Intercepteur d'immuabilité EF et triggers SQL côté iPad. Fixtures de parité .NET → Swift (142 scénarios fiscaux, FEC, archives, tickets). JWT validé : émetteur, audience, durée et clé. PIN hachés en PBKDF2 et comparés en temps constant (`FixedTimeEquals`). Jeton d'appareil haché. `X-Device-Token` limité à l'origine du serveur. Aucune injection SQL, côté .NET comme côté iPad. AES-GCM correct et secrets dans le Keychain.

---

## Notes de maturité

| Composant | Note | Écart avec le 25/10 |
|---|---|---|
| Backend .NET | **5/10** | inchangée ; E3 (oracle anonyme) aggrave le volet authentification |
| Client web | **4/10** | inchangée |
| App iPad | **6,5/10** | inchangée ; E15 nuancé |
| **Global** | **5/10** | Cœur fiscal sérieux, frontière de confiance poreuse. Pas prêt pour une mise en production réelle. |

## Ordre de correction proposé

1. **Authentification** (sans incidence fiscale, gain immédiat) : C1 (supprimer le login implicite, vrai verrouillage), E8, E3 + E2 + E4 (une seule vérification de PIN superviseur, authentifiée et limitée), E1 (politique `RequireAdmin`, `seed-test-data` hors Release), M8.
2. **XSS** : C2. Échapper par défaut, ajouter un test Playwright `<img onerror>`, puis E11 (CSP, SRI, jetons hors `localStorage`, CORS).
3. **Intégrité des ventes** (touche le .NET, l'iPad et les fixtures, **à valider avant** : règles de TVA) : C3, E5, E7, E9, M3.
4. **Fiscal** (à valider avec l'expert-comptable ou le certificateur) : E6, E14 + M2, E15, M1, puis C4 (signature à clé, format versionné).
5. **Exploitation** : E12 (HTTPS et épinglage, retrait d'`ArbitraryLoads`), E13 (restauration `.posbak`), M5.
