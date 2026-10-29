# Correctifs de la revue totale, étape 1 : authentification, rôles, XSS

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fermer les constats C1, C2, E1, E2, E3, E8 et le défaut de verrouillage web de la revue du 26/10/2026, sans toucher aux règles fiscales ni à la parité .NET/Swift.

**Architecture:** Corrections locales et indépendantes. Web : plus de connexion implicite, verrou réel, JWT limité à l'origine du serveur, échappement systématique des données dans `innerHTML`. Backend : politique `RequireAdmin`, plus de comptes aux PIN connus hors Development, un seul garde de PIN superviseur limité en débit sur toutes les routes qui valident un PIN.

**Tech Stack:** .NET 9 Minimal API (xUnit, `WebApplicationFactory`, en-tête de test `X-Test-Role`), client web vanilla JS, Playwright.

**Spec:** `docs/revues/2026-10-26-revue-totale.md` (constats C1, C2, E1, E2, E3, E4 partiel, E8).

## Global Constraints

- Aucune modification de `FiscalHashing`, des reçus, des clôtures, du JET ou des fixtures : rien à régénérer, rien à reporter dans `ios/`.
- `Directory.Build.props` : `TreatWarningsAsErrors` + `EnforceCodeStyleInBuild`. Lancer `dotnet build RestaurantPos.slnx` sans avertissement ; ne pas laisser `dotnet format` toucher des fichiers hors périmètre.
- Textes d'interface et messages d'erreur : français, accents corrects ; toute clé ajoutée l'est dans les trois `.resx` (fr/en/ar) côté .NET et dans les trois dictionnaires `i18n` côté web, avec les mêmes noms de clé.
- Le contrat HTTP existant ne change pas pour les clients iPad (`HTTPPosAPI`) : mêmes routes, mêmes corps, mêmes codes pour les cas valides.
- PIN de démonstration (`1234`, `2468`, `5678`, `9999`) : conservés en Development et en `Testing` seulement.
- À la fin de chaque tâche : `dotnet build RestaurantPos.slnx`, `dotnet test RestaurantPos.slnx` ; pour les tâches web, aussi `POS_WEB_URL=http://localhost:5080 npm test` dans `tests/RestaurantPos.Web.E2ETests` (API démarrée). Mettre à jour `docs/plans/correctifs-revue-etape1-progress.md` (statut, nombre de tests .NET/web, constats ouverts).

## Review Focus

1. Un 401 ou un jeton expiré ne doit plus reconnecter personne en silence : l'écran de PIN s'ouvre et reste ouvert, aucune requête `/api/auth/login` ne part sans geste de l'opérateur (test Playwright, Task 1).
2. Nom de produit, modificateur, commentaire cuisine, libellé d'attente, numéro de table, nom d'opérateur contenant `<img src=x onerror=...>` : aucun script ne s'exécute dans aucune vue (Task 2).
3. Un manager ne peut ni créer ni promouvoir un Admin, et un rôle invalide est refusé au lieu de devenir Waiter silencieusement (Task 3).
4. Démarrage en Release / Production sur base vide : aucun compte au PIN connu n'existe ; le PIN d'amorçage vient de la configuration, ou est généré et journalisé une fois (Task 4).
5. Un PIN superviseur faux essayé en rafale sur `held/{id}/void`, `/api/auth/override` ou le happy hour finit en 429 pour la même clé, et un succès sur une route ne remet pas à zéro le compteur d'une autre (Task 5).

---

### Task 1: Web, plus de connexion implicite, verrou réel, JWT limité à l'origine

**Files:**
- Modify: `src/RestaurantPos.Api/wwwroot/app.js` (`ensureAuthToken` ~967, gestionnaire `btnLockTerminal` ~433, wrapper `fetch` ~73-100, appel de test de connexion ~4674, connexions SignalR existantes)
- Test: `tests/RestaurantPos.Web.E2ETests/tests/auth-flow.spec.ts`

**Interfaces:**
- Produces: `ensureAuthToken(): Promise<string|null>` renvoie `state.token` ou `null` ; ne fait plus aucune requête. `lockTerminal(): void` (nouvelle fonction de la closure) : vide `state.token`, `state.operator`, `localStorage['pos_jwt_token']`, arrête les connexions SignalR ouvertes, ouvre la modale de PIN.

- [ ] **Step 1: Tests Playwright (échouent d'abord)** dans `auth-flow.spec.ts` :
  - `no implicit login at startup` : sans jeton en `localStorage`, charger la page, attendre la modale de PIN visible ; `page.on('request')` n'enregistre aucun `POST /api/auth/login`.
  - `lock clears the session` : se connecter par PIN `1234`, cliquer `#btnLockTerminal`, vérifier `localStorage.getItem('pos_jwt_token') === null` et la modale visible ; un appel `fetch('/api/auth/me')` depuis la page renvoie 401.
  - `jwt never leaves the origin` : une requête `window.fetch` vers `http://example.invalid/api/health` (interceptée par `page.route`) n'a pas d'en-tête `authorization`.
- [ ] **Step 2: Lancer, constater l'échec** : `npx playwright test tests/auth-flow.spec.ts --project='iPad Pro 11'`.
- [ ] **Step 3: Implémenter.**
  - `ensureAuthToken` : `return state.token || null;` (supprimer le `fetch('/api/auth/login')`). Vérifier que chacun des 28 appelants tolère `null` (le 401 rouvre la modale) sans les réécrire.
  - Bouton de verrou : appeler `lockTerminal()`.
  - Wrapper `fetch` : ne poser `Authorization` que si `target.origin === location.origin` (calculer `target` avant le bloc du jeton).
- [ ] **Step 4: Relancer le fichier de test puis toute la suite web** : attendu, tout vert.
- [ ] **Step 5: Commit** `fix(web): suppression de la connexion implicite 1234, verrou réel, JWT limité à l'origine (C1, E8)`.

---

### Task 2: Web, échapper toutes les données dans `innerHTML`

**Files:**
- Modify: `src/RestaurantPos.Api/wwwroot/app.js` (90 occurrences de `innerHTML` ; modèles dont l'interpolation contient une donnée du serveur ou de l'opérateur : lignes ~495-540, 754, 802, 837, 1219-1234, 1475, 1766, 2205-2229, 2510, 2551, 2705-2766, 2790-2860, 2989, 3025, 3092, 3158, 3205, 3254-3312, 4132-4146, 4280-4322, 4518, 4593, 5171-5310)
- Test: `tests/RestaurantPos.Web.E2ETests/tests/xss.spec.ts` (nouveau)

**Interfaces:**
- Consumes: `escapeHtml(value: unknown): string` existante (~4576, fonction hissée).
- Produces: règle : toute valeur qui ne vient pas d'une clé `t(...)` ni d'un nombre formaté passe par `escapeHtml`, attributs (`value="${...}"`, `data-*`) compris.

- [ ] **Step 1: Test `xss.spec.ts`** : via l'API (jeton Admin par PIN `9999`), créer une catégorie et un produit nommés `<img src=x onerror="window.__xss=1">`, un modificateur de même nom, puis envoyer une ligne avec le commentaire cuisine `<img src=x onerror="window.__xss=1">`. Parcourir : comptoir (grille, touches rapides, panier), salle, KDS, attente, Gestion → Catalogue / Personnel / Imprimantes / Appareils / Happy hour. Assertion après chaque vue : `await page.evaluate(() => window.__xss) === undefined`. Nettoyer les données créées.
- [ ] **Step 2: Lancer, constater l'échec** (au moins grille, panier, KDS).
- [ ] **Step 3: Implémenter** : passer chaque interpolation de donnée dans `escapeHtml` ; pour un modèle réduit à du texte, préférer `textContent`. Pas de refactor au-delà. Lister dans le rapport de tâche les occurrences laissées telles quelles et pourquoi (clés `t(...)`, nombres, constantes).
- [ ] **Step 4: Relancer `xss.spec.ts`, puis toute la suite web** : attendu, tout vert.
- [ ] **Step 5: Commit** `fix(web): échappement des données affichées via innerHTML (C2)`.

---

### Task 3: Backend, politique `RequireAdmin` et contrôle des rôles

**Files:**
- Modify: `src/RestaurantPos.Api/Program.cs` (politiques ~171-178, route `seed-test-data` ~467-476), `src/RestaurantPos.Api/Endpoints/StaffEndpoints.cs` (POST ~48, PUT ~58), les trois `.resx` (clé `errors.invalid_role`)
- Test: `tests/RestaurantPos.Api.Tests/StaffRolesTests.cs` (nouveau)

**Interfaces:**
- Produces: politique `"RequireAdmin"` = `RequireRole("Admin")`. Règle de handler : seul un Admin peut attribuer le rôle `Admin` (création ou modification) ou modifier un membre déjà Admin ; un rôle absent de l'énumération donne `400 { Message }` (`errors.invalid_role`) au lieu de `Waiter`.

- [ ] **Step 1: Tests** (`X-Test-Role`, comme `DeviceEndpointsTests`) :
  - `Manager_cannot_create_admin` : `POST /api/staff` rôle `Admin` avec `FloorManager` → 403.
  - `Admin_can_create_admin` → 200.
  - `Manager_cannot_promote_to_admin` : `PUT /api/staff/{id}` rôle `Admin` → 403.
  - `Manager_cannot_edit_existing_admin` → 403.
  - `Unknown_role_is_rejected` : rôle `Root` → 400.
  - `Seed_test_data_is_admin_only` : `POST /api/admin/seed-test-data` avec `FloorManager` → 403 ; avec `Admin` en `Testing` → 200.
- [ ] **Step 2: Lancer** `dotnet test tests/RestaurantPos.Api.Tests --filter FullyQualifiedName~StaffRolesTests`, constater l'échec.
- [ ] **Step 3: Implémenter.** Ajouter la politique ; dans les handlers `staff`, remplacer le repli `Waiter` par le 400 et refuser 403 selon la règle ci-dessus (`ClaimsPrincipal.IsInRole("Admin")`). `seed-test-data` : `RequireAuthorization("RequireAdmin")`, monté seulement en `Development` ou `Testing`.
- [ ] **Step 4: Tests de la tâche, puis `dotnet test RestaurantPos.slnx`** (adapter les tests existants qui créaient des Admin avec un rôle manager, sans affaiblir leurs assertions).
- [ ] **Step 5: Commit** `fix(api): politique RequireAdmin, rôles contrôlés, seed de test réservé (E1)`.

---

### Task 4: Backend, plus de comptes au PIN connu hors Development

**Files:**
- Modify: `src/RestaurantPos.Api/Program.cs` (bloc `#if DEBUG` / `#else` ~421-446), `deploy_production.sh` (étape de seed de test, ~41-56)
- Test: `tests/RestaurantPos.Api.Tests/BootstrapAdminTests.cs` (nouveau)

**Interfaces:**
- Produces: `internal static async Task BootstrapAdminAsync(AppDbContext db, IStaffManagementService staff, IConfiguration cfg, ILogger log)` dans `Program.cs`. Si `db.Users` est vide : crée un seul compte `Admin Système` (Admin) avec le PIN de `Bootstrap:AdminPin` (6 chiffres ou plus, sinon exception de démarrage), ou, clé absente, un PIN de 8 chiffres tiré de `RandomNumberGenerator`, écrit une seule fois dans le journal applicatif (Warning), jamais dans le JET ni la base. Aucun autre compte. Appelée à la place de la branche `#else`.

- [ ] **Step 1: Tests** :
  - `Empty_db_creates_single_admin_with_configured_pin` : l'authentification avec ce PIN réussit ; `1234`, `2468`, `9999` échouent ; un seul utilisateur.
  - `Missing_pin_generates_one` : PIN de 8 chiffres, présent une fois dans le journal.
  - `Short_configured_pin_fails_startup` : `Bootstrap:AdminPin = "1234"` → exception.
  - `Non_empty_db_is_untouched`.
- [ ] **Step 2: Lancer, constater l'échec.**
- [ ] **Step 3: Implémenter** la méthode et la brancher dans le `#else` ; garder `#if DEBUG` pour la démonstration. `deploy_production.sh` : retirer le réamorçage de données de test et de PIN par défaut, afficher un rappel de renseigner `Bootstrap__AdminPin`. Rien d'autre dans le script (HTTPS : E12, plus tard).
- [ ] **Step 4: Tests de la tâche, puis `dotnet test RestaurantPos.slnx`.**
- [ ] **Step 5: Commit** `fix(api): amorçage administrateur sans PIN connu hors Development (C1, E1)`.

---

### Task 5: Backend, un seul garde de PIN superviseur, limité en débit

**Files:**
- Create: `src/RestaurantPos.Api/Security/SupervisorPinGuard.cs`
- Modify: `src/RestaurantPos.Api/Endpoints/CounterSaleEndpoints.cs` (`held/{id}/void` ~188), `src/RestaurantPos.Api/Endpoints/HappyHourEndpoints.cs` (`override/activate` et `override/stop` ~42-94, passer à `RequireAuthorization()`), `src/RestaurantPos.Api/Endpoints/AuthEndpoints.cs` (`/override` ~100, utiliser le garde), `src/RestaurantPos.Infrastructure/Security/PinRateLimiterService.cs` (escalade : 5 échecs → 30 s, 10 → 5 min, 20 → 30 min, fenêtre d'une heure)
- Test: `tests/RestaurantPos.Api.Tests/SupervisorPinGuardTests.cs` (nouveau), `tests/RestaurantPos.Infrastructure.Tests/PinRateLimiterTests.cs` (nouveau ou étendu)

**Interfaces:**
- Produces: `SupervisorPinGuard.CheckAsync(string? pin, HttpContext ctx, IOperatorAuthenticationService auth, IPinRateLimiterService limiter, CancellationToken ct): Task<SupervisorPinResult>` avec `SupervisorPinResult(bool Allowed, IResult? Rejection, Guid? OperatorId, string? OperatorName, UserRole? Role)`. Clé du limiteur : `"supervisor:" + IP`, partagée par `held void`, `/api/auth/override` et le happy hour. Échec ou rôle insuffisant → `RecordFailedAttempt` + 403 ; verrou → 429 (`errors.supervisor_rate_limited`) ; succès → `ResetAttempts` ; PIN vide → 400 (`errors.supervisor_pin_required`). Rôle exigé : `FloorManager` ou `Admin`. Le service happy hour (`HappyHourPricingService` ~242, ~312) valide encore le PIN : le garde passe en amont, le service reste inchangé.

- [ ] **Step 1: Tests** :
  - `Held_void_wrong_pin_burst_gets_429` : 5 PIN faux → 6ᵉ appel 429, même avec un bon PIN.
  - `Held_void_unknown_hold` : PIN faux → 403, PIN bon → 404 (inchangé), les deux comptés par le limiteur.
  - `Happy_hour_override_requires_auth` : sans jeton → 401.
  - `Shared_key_across_routes` : 3 échecs sur `held void` + 2 sur `/api/auth/override` → verrou.
  - `Waiter_pin_is_403_and_counts`.
  - Limiteur : escalade 30 s / 5 min / 30 min et expiration de la fenêtre (durées lues dans des constantes `internal` ou une horloge injectable).
- [ ] **Step 2: Lancer, constater l'échec.**
- [ ] **Step 3: Implémenter** le garde ; remplacer les trois blocs dupliqués ; passer les deux routes happy hour à `RequireAuthorization()` (vérifier dans `app.js` et `HTTPPosAPI` que les appels envoient déjà le JWT). Limiteur en mémoire : commentaire `shortcut: compteurs perdus au redémarrage, persister si le serveur est exposé hors LAN`.
- [ ] **Step 4: Tests de la tâche, `dotnet test RestaurantPos.slnx`, specs web `happy-hour*.spec.ts`** : attendu, tout vert.
- [ ] **Step 5: Commit** `fix(api): garde de PIN superviseur unique, limité en débit (E2, E3, E4)`.

---

## Auto-revue

- Couverture : C1 → T1 + T4 ; C2 → T2 ; E1 → T3 + T4 ; E2, E3, E4 (volet oracle, pas le coût PBKDF2) → T5 ; E8 → T1. Hors périmètre, renvoyés aux étapes suivantes : M8 (verrou iPad), E11 (CSP, SRI, jetons hors `localStorage`, CORS), E12 (HTTPS), C3 et suivants.
- Cohérence : `RequireAdmin` (T3) est la seule nouvelle politique ; `SupervisorPinGuard` (T5) est le seul garde ; `lockTerminal` (T1) est défini avant usage.
- Proportion : 5 tâches pour 7 constats, corps de code laissés à l'implémenteur.
