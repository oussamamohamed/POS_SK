# Découverte et appairage des appareils — design

Date : 2026-09-25 · Phase 1 de « Architecture multi-iPad POS » (doc Claude Docs `78bb59ac-bf60-40fe-ae0e-d2e7a12c20c6`).

## But

Chaque poste qui écrit dans la chaîne NF525 (iPad ou caisse web) est appairé au serveur et reçoit un `terminalId` attribué par le serveur. Les iPads trouvent le serveur sans saisie d'adresse IP.

Critères de succès :

1. Un gérant génère un code dans le back-office ; un iPad neuf scanne le QR et peut encaisser, sans saisir d'URL ni d'identifiant de terminal.
2. Deux postes ne peuvent plus écrire dans la même chaîne : chaque appareil a son `TerminalId` unique (`T01`, `T02`…).
3. Un appareil révoqué ne peut plus encaisser (`401`).
4. Le protocole (code, jeton, Bonjour) ne dépend pas de l'implémentation serveur, pour garder ouverte l'option « iPad maître » (option 2 du doc d'architecture).

## Décisions

| Sujet | Décision |
|---|---|
| Architecture cible | Indécise entre option 1 (boîtier .NET) et option 2 (iPad maître) ; ce design ne tranche pas |
| Appairage | Code à usage unique généré dans le back-office, affiché en QR + texte |
| Portée du contrôle | Jeton d'appareil exigé uniquement sur les appels qui écrivent un ticket fiscal ; le reste reste en PIN/JWT seul |
| Découverte | Bonjour `_restaurantpos._tcp` via `Makaretu.Dns.Multicast` ; le beacon UDP 45454 est supprimé |
| QR | Image PNG générée côté serveur avec `QRCoder` |

## État actuel (constaté dans le code)

- `NetworkDiscoveryBeaconService` répond `http://localhost:5000`, inutilisable depuis un autre appareil.
- `terminalId` est un texte libre côté client (`POS_A` par défaut sur iOS et web, `POS_MAIN_TERM` en repli serveur). Deux postes peuvent partager une chaîne.
- La chaîne de reçus est déjà par terminal : `CheckoutPaymentService` chaîne sur le dernier reçu du même `TerminalId`. Un nouveau `T01` démarre donc de `GenesisHash` ; le format de hachage ne change pas.
- Les rapports X/Z traitent `POS_MAIN_TERM` (ou vide) comme « tous terminaux » (`NF525FiscalAuditService`).

## Serveur

### Modèle

Nouvelles entités dans `Domain/Entities`, `DbSet` dans `AppDbContext`, et SQL brut `CREATE TABLE IF NOT EXISTS` dans le bloc idempotent de `Program.cs`.

`Device`

| Champ | Type | Note |
|---|---|---|
| `Id` | Guid (UuidV7) | |
| `Name` | string | ex. « Caisse comptoir » |
| `Role` | enum `DeviceRole` | `Caisse`, `Serveur`, `Cuisine`, `BackOffice` |
| `TerminalId` | string, unique | `T01`, `T02`… attribué séquentiellement, jamais réutilisé |
| `TokenHash` | string | SHA-256 hex du jeton |
| `PairedAtUtc` | DateTimeOffset | |
| `LastSeenUtc` | DateTimeOffset? | mis à jour par le filtre |
| `RevokedAtUtc` | DateTimeOffset? | |

`DevicePairingCode`

| Champ | Type | Note |
|---|---|---|
| `Id` | Guid | |
| `CodeHash` | string | SHA-256 hex du code |
| `Name`, `Role` | | copiés dans `Device` à l'appairage |
| `ExpiresAtUtc` | DateTimeOffset | création + 10 min |
| `UsedAtUtc` | DateTimeOffset? | usage unique |
| `CreatedByOperatorId` | Guid | |

Code : 8 caractères dans un alphabet sans ambiguïté (`ABCDEFGHJKMNPQRSTUVWXYZ23456789`), tiré avec `RandomNumberGenerator`. Jeton : 32 octets aléatoires, base64url. Seuls les hachages sont stockés ; comparaison via `CryptographicOperations.FixedTimeEquals`.

### Service et endpoints

`IDeviceService` (Application) / `DeviceService` (Infrastructure). Endpoints dans `Api/Endpoints/DeviceEndpoints.cs`, groupe `/api/devices` :

| Méthode | Route | Accès | Réponse |
|---|---|---|---|
| POST | `/pairing-codes` `{name, role}` | `RequireManagerOrAdmin` | `{code, expiresAt, qrPayload}` |
| GET | `/pairing-codes/{code}/qr.png` | `RequireManagerOrAdmin` | PNG (`QRCoder`) de `qrPayload` |
| POST | `/pair` `{code}` | anonyme, limité par `IPinRateLimiterService` (clé IP) | `{deviceId, token, terminalId, name, role}` |
| GET | `/` | `RequireManagerOrAdmin` | `DeviceDto[]` (sans hachage) |
| POST | `/{id}/revoke` | `RequireManagerOrAdmin` | `204` |

`qrPayload` = `posdevice://pair?url=<URL LAN du serveur>&code=<code>`. L'URL LAN est dérivée de la requête (`Request.Scheme` + `Request.Host`) : le gérant consulte le back-office depuis le réseau local, donc l'hôte vu est joignable par l'iPad.

### Filtre `RequireDevice`

Filtre d'endpoint (`IEndpointFilter`) posé sur :

- `POST /api/checkout/pay`
- `POST /api/checkout/void/{receiptId}`
- `POST /api/orders/counter/checkout`

Comportement :

1. Lit l'en-tête `X-Device-Token`, hache, cherche un `Device` non révoqué.
2. Absent, inconnu ou révoqué → `401` `{ "code": "device_not_paired" }`.
3. Sinon met `Device` dans `HttpContext.Items["Device"]` et met à jour `LastSeenUtc`.

Les trois handlers prennent `terminalId` depuis `HttpContext.Items["Device"]` et ignorent `req.TerminalId`. Le JWT opérateur reste exigé comme aujourd'hui. Les services métier ne changent pas.

Hors filtre, inchangés : rapports X/Z, `latest-closure`, FEC, commandes en attente (`hold`/`held`). `POS_MAIN_TERM` garde son sens « tous terminaux » pour les rapports.

### Découverte

- Supprimer `NetworkDiscoveryBeaconService` et son enregistrement dans `Program.cs`.
- Nouveau `BonjourAdvertiserService` (hosted service) : `Makaretu.Dns.Multicast`, instance `_restaurantpos._tcp`, port = port HTTP réel (lu depuis `IServer` / `IServerAddressesFeature` après démarrage), TXT `name=<nom configuré>`, `version=<version assembly>`.
- Nom configurable `Discovery:ServerName` (défaut : nom de machine).
- Échec d'ouverture du port 5353 → log `Warning`, l'API continue.
- Désactivé en environnement `Testing`.

## Client iOS

### PosKit

- `DeviceCredentials` (Codable) : `serverURL`, `deviceId`, `token`, `terminalId`, `name`, `role`.
- `DeviceCredentialStore` : protocole + implémentation Keychain + fausse implémentation pour tests.
- `AppSettings.serverURL` / `terminalId` remplacés par les credentials. Migration au premier lancement : `pos.serverURL` sert de pré-remplissage à l'écran d'appairage ; `pos.terminalId` est abandonné.
- `PosAPI` gagne `pair(code:) async throws -> PairResponse`.
- `HTTPPosAPI` ajoute `X-Device-Token` à toutes les requêtes quand un jeton existe. Réponse `401` avec `code == "device_not_paired"` → erreur typée `PosAPIError.deviceNotPaired`.
- `AppContext` : sur `deviceNotPaired`, efface les credentials et passe à l'état « non appairé ». Le ticket brouillon local est conservé.
- `ServerBrowser` : enveloppe `NWBrowser` (`_restaurantpos._tcp`), publie la liste `[DiscoveredServer(name, url)]`. Sur échec de connexion d'un iPad appairé, relance la recherche et remplace `serverURL` par le serveur au `name` identique.
- `InMemoryPosAPI` : `pair` accepte le code `TESTCODE`.

### App

- `PairingScreen`, affiché quand aucun credential n'existe :
  1. liste « Serveurs trouvés » (`ServerBrowser`) ;
  2. bouton « Scanner le QR » (`DataScannerViewController`, VisionKit) qui lit `posdevice://pair?...` ;
  3. saisie manuelle URL + code.
  Succès → credentials enregistrés → écran PIN existant.
- `RootView` : les champs réglages URL/terminal deviennent en lecture seule (nom, rôle, `terminalId`) + bouton « Dissocier cet iPad ».
- `project.yml` : `NSLocalServiceUsageDescription`, `NSBonjourServices = [_restaurantpos._tcp]`, `NSCameraUsageDescription`.
- Arguments de test : `-UITestPaired` pré-remplit des credentials factices ; sans lui, `-UITestMode` démarre sur `PairingScreen`.

## Client web (`app.js`)

- Supprimer l'UI de « scan » UDP.
- `state.device` (`{token, terminalId, name}`) persisté dans `localStorage` (clé `pos_device`), lu avec `try/catch`.
- Envoyer `X-Device-Token` sur `/api/checkout/pay`, `/api/checkout/void/*`, `/api/orders/counter/checkout`.
- Retirer `terminalId` des corps de ces trois appels.
- Sur `401 device_not_paired` : modale « Coupler ce poste » (saisie du code) → `POST /api/devices/pair` → enregistrement → l'utilisateur relance le paiement.
- Nouvelle page back-office « Appareils » (gérant/admin) : formulaire nom + rôle → affiche code + `<img src="/api/devices/pairing-codes/{code}/qr.png">` + compte à rebours ; liste des appareils (nom, rôle, `terminalId`, dernière activité, état) ; bouton « Révoquer ».

## Contrat inter-clients

- DTO .NET : `CreatePairingCodeRequest`, `PairingCodeResponse`, `PairRequest`, `PairResponse`, `DeviceDto`, `DeviceRole` (sérialisé en chaîne).
- Modèles Swift équivalents dans `PosKit/Models`.
- Fixtures capturées : `pair-response.json`, `devices.json` dans `PosKitTests/Fixtures`.
- Supprimer le test Swift sur `discoveryPort == 45454` et le champ associé.

## Erreurs

| Cas | Réponse serveur | Client |
|---|---|---|
| Code expiré, utilisé ou inconnu | `400 {code:"pairing_code_invalid"}` (message unique) | « Code invalide ou expiré » |
| Trop d'essais sur `/pair` | `429` + délai | affiche le délai |
| Jeton absent/inconnu/révoqué sur appel fiscal | `401 {code:"device_not_paired"}` | iOS : retour à l'appairage, brouillon conservé ; web : modale |
| Bonjour ne trouve rien | — | QR et saisie manuelle restent disponibles |
| mDNS ne démarre pas | log `Warning` | appairage par QR/URL fonctionne |

## Tests

.NET (`tests/RestaurantPos.Api.Tests`, environnement `Testing`) :

- Cycle complet : code → `pair` → `pay` avec jeton → reçu `TerminalId == "T01"`, `PreviousSignatureHash == GenesisHash`.
- `terminalId: "POS_A"` dans le corps est ignoré.
- `pay` sans jeton, avec jeton inconnu, avec appareil révoqué → `401 device_not_paired`.
- Code expiré, code réutilisé → `400 pairing_code_invalid`.
- Deux appareils → `T01`, `T02`, chaînes indépendantes.
- `pairing-codes` et `revoke` refusés sans rôle gérant/admin.
- Tests existants (`CheckoutE2ETests`, vente comptoir) : helper d'appairage dans le setup.

iOS :

- Unit (`PosKit`) : store de credentials (faux Keychain), en-tête `X-Device-Token` présent, `401 device_not_paired` → état non appairé, décodage des nouvelles fixtures, parsing de `posdevice://pair`.
- UI : un test d'appairage (code `TESTCODE` sur `InMemoryPosAPI`) ; les autres tests passent `-UITestPaired`.
- Contract (`./scripts/test.sh contract`) : étape d'appairage via un code créé avec le PIN gérant.

Web E2E (Playwright) : helper `pairTill()` utilisé par les specs qui encaissent ; une spec pour la modale « Coupler ce poste » et la page « Appareils ».

Vérification manuelle (pas de mDNS fiable en CI) : sur un iPad réel, le serveur apparaît dans « Serveurs trouvés » ; après changement d'IP du serveur, l'iPad reprend sans intervention.

## Hors périmètre

- Signal de vie, batterie, version, config à distance → phase 2.
- Cache et outbox hors-ligne → phase 3.
- Appairage obligatoire pour le back-office web hors caisse.
- Consolidation des rapports X/Z par appareil.
