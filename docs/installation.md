# Installation et mise en place

Ce guide couvre tous les cas d'usage de la caisse : du poste de développement jusqu'au restaurant en production, avec des navigateurs web, des iPad reliés à un serveur, ou un iPad seul.

## 1. Choisir son architecture

| Cas | Qui fait quoi | Pour qui |
|---|---|---|
| **A. Développement local** | L'API tourne sur votre Mac, vous testez avec un navigateur ou le simulateur iPad. Données de démonstration. | Développeur |
| **B. Serveur + clients web** | Un ordinateur du restaurant héberge l'API. Les caisses sont des navigateurs (iPad Safari, PC, tablette) qui ouvrent `http://<ip-serveur>:5080`. | Restaurant sans app native |
| **C. Serveur + iPad natifs** | Même serveur. Chaque iPad installe l'app native et s'appaire au serveur. Cuisine (KDS), plusieurs caisses, imprimantes réseau. | Restaurant multi-postes |
| **D. iPad autonome** | Un seul iPad, sans serveur ni réseau. Base SQLite sur l'iPad, mêmes règles fiscales NF525. | Petit commerce, un poste |
| **E. Mixte** | Serveur + iPad natifs + navigateurs en même temps (B et C ensemble). | Cas courant |

Le serveur (cas B, C, E) est le **serveur local du restaurant** : tout le monde le joint par son adresse IP sur le réseau Wi-Fi/Ethernet, jamais depuis Internet. Il n'y a ni HTTPS ni CORS configurés : le client web est servi par l'API elle-même.

Un iPad choisit son mode au premier lancement (« Relié à un serveur » ou « iPad autonome ») et **ce choix est définitif tant que l'application n'est pas réinstallée**.

## 2. Prérequis

| Élément | Version | Nécessaire pour |
|---|---|---|
| .NET SDK | 10 | A, B, C, E (compiler et lancer l'API) |
| macOS + Xcode | récent (iPadOS 17+ visé) | construire l'app iPad (C, D, E) |
| XcodeGen | `brew install xcodegen` | générer le projet Xcode |
| Compte Apple Developer | gratuit ou payant | installer sur un **vrai** iPad (signature obligatoire) |
| Node.js | récent | tests Playwright seulement, pas pour l'installation |

Récupérez le code, puis depuis la racine du dépôt : `dotnet build RestaurantPos.slnx`.

## 3. Cas A : développement local

```bash
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://0.0.0.0:5080 \
Jwt__Secret="SuperSecretKeyForRestaurantPosSystemThatIsAtLeast32BytesLong!" \
dotnet run --project src/RestaurantPos.Api
```

- Le port 5000 est pris par AirPlay sur macOS : utilisez **5080**.
- En build **Debug** (le défaut de `dotnet run`), l'API crée la base `restaurantpos.db` et la remplit de données de démonstration.
- PIN de démonstration : `1234` responsable, `2468` serveuse, `5678` cuisine, `9999` admin.
- `Jwt__Secret` est **indispensable** pour se connecter, même en développement (sans lui : « 'Jwt:Secret' is not configured »). La valeur ci-dessus est une clé de développement, à ne jamais utiliser en production.
- Données plus riches : `ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/RestaurantPos.Api -- --seed-test-data`.
- Ouvrez `http://localhost:5080` dans un navigateur.
- Simulateur iPad : voir la section 7.

## 4. Serveur du restaurant (cas B, C, E)

### 4.1 Choisir la machine

Un Mac, un PC ou un mini-PC Linux allumé pendant le service, **branché en Ethernet si possible**, avec une **adresse IP fixe** (réservation DHCP dans la box ou le routeur). Si l'IP change, tous les iPad et favoris web doivent être reconfigurés.

### 4.2 Publier l'application

```bash
dotnet publish src/RestaurantPos.Api -c Release -o /opt/restaurantpos
```

Le dossier de sortie contient l'API et le client web (`wwwroot`). Copiez-le sur la machine serveur (ou publiez-y directement).

### 4.3 Configuration

Tout se règle par variables d'environnement (`__` remplace `:`).

| Variable | Obligatoire | Rôle |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | oui | `Production` en exploitation |
| `ASPNETCORE_URLS` | oui | ex. `http://0.0.0.0:5080` pour écouter sur le réseau |
| `Jwt__Secret` | **oui** | Clé de signature des sessions, 32 caractères minimum, secrète et propre au restaurant. Générer : `openssl rand -hex 32`. Ne pas commencer par `ProdSecretKey`. |
| `Bootstrap__AdminPin` | recommandé | PIN de l'administrateur au **premier** démarrage, exactement 4 chiffres. Sans lui, un PIN est généré et écrit **une seule fois** dans le journal. À changer après la première connexion. |
| `Backup__Passphrase` | **fortement recommandé** | Phrase de chiffrement des sauvegardes (8 caractères minimum). Sans elle, **aucune sauvegarde n'est faite** (avertissement au démarrage). **Conservez-la hors de la machine** : sans elle, une sauvegarde est illisible. |
| `Backup__Path` | non | Dossier des sauvegardes (défaut `backups/`). Pointez vers un disque externe ou un dossier synchronisé. |
| `ConnectionStrings__DefaultConnection` | non | Base SQLite (défaut `Data Source=restaurantpos.db`). |
| `Archives__Path` | non | Dossier des archives fiscales (défaut `archives`). |
| `Discovery__ServerName` | non | Nom annoncé aux iPad par Bonjour (défaut : nom de la machine). |

La base, `backups/` et `archives/` sont des chemins **relatifs au dossier depuis lequel l'API est lancée** : lancez-la toujours depuis le même dossier, ou donnez des chemins absolus.

### 4.4 Premier démarrage

```bash
cd /opt/restaurantpos
ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS=http://0.0.0.0:5080 \
Jwt__Secret="<votre clé>" Bootstrap__AdminPin=<4 chiffres> Backup__Passphrase="<votre phrase>" \
dotnet RestaurantPos.Api.dll
```

Vérifiez :

```bash
curl http://<ip-serveur>:5080/api/health      # {"status":"Healthy", ...}
```

En Production (build Release), la base est créée avec **un seul administrateur** et **aucune donnée de démonstration** : les PIN `1234`, `2468` etc. n'existent pas. Connectez-vous avec le PIN d'amorçage, puis créez les comptes dans Paramétrage → Serveurs & Codes PIN, et changez le PIN admin.

Au premier démarrage, des lignes `fail: ... ALTER TABLE ... ADD COLUMN ...` peuvent apparaître dans le journal : ce sont les mises à jour de schéma idempotentes, sans conséquence sur une base neuve.

### 4.5 Réseau et pare-feu

- Autorisez le port TCP **5080** (ou celui choisi) en entrée sur la machine serveur.
- iPad, navigateurs, serveur et imprimantes doivent être sur le **même réseau**.
- Bonjour (`_restaurantpos._tcp`) permet aux iPad de trouver le serveur : il fonctionne sur un même sous-réseau, pas à travers des VLAN isolés. Sinon saisissez l'adresse à la main.
- Ne publiez jamais ce port sur Internet.

### 4.6 Démarrage automatique

Le serveur doit redémarrer seul après une coupure de courant. Exemple **systemd** (Linux), non testé ici : adaptez les chemins.

```ini
# /etc/systemd/system/restaurantpos.service
[Unit]
Description=Restaurant POS
After=network-online.target

[Service]
WorkingDirectory=/opt/restaurantpos
ExecStart=/usr/bin/dotnet /opt/restaurantpos/RestaurantPos.Api.dll
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://0.0.0.0:5080
EnvironmentFile=/etc/restaurantpos.env
Restart=always

[Install]
WantedBy=multi-user.target
```

`/etc/restaurantpos.env` contient `Jwt__Secret=…`, `Backup__Passphrase=…` etc., lisible par root seul (`chmod 600`). Sur macOS, un agent `launchd` joue le même rôle.

## 5. Clients web (cas B et E)

1. Sur chaque appareil (Safari sur iPad, Chrome sur PC…), ouvrez `http://<ip-serveur>:5080`.
2. Ajoutez la page à l'écran d'accueil (« Ajouter à l'écran d'accueil ») pour un accès plein écran.
3. Saisissez le PIN opérateur.

**Appairage des caisses.** Les routes qui écrivent des reçus fiscaux (paiement, annulation, vente comptoir) exigent un **appareil appairé** (en-tête `X-Device-Token`). Dans le back-office : Paramétrage → **Appareils** → générer un code d'appairage (nom, rôle Caisse / Serveur / Cuisine / Back-office), puis l'entrer sur l'appareil. Un appareil non appairé qui tente d'encaisser voit l'écran d'appairage. Le serveur déduit le terminal (`T01`, `T02`…) de l'appareil.

Le même onglet permet de révoquer un appareil perdu.

## 6. Cas C : iPad natifs reliés au serveur

### 6.1 Construire l'app

```bash
brew install xcodegen
cd ios
xcodegen generate            # le .xcodeproj est généré, ne l'éditez jamais
open RestaurantPOS.xcodeproj
```

Dans Xcode, cible **RestaurantPOS** → Signing & Capabilities : choisissez votre **Team** (le champ `DEVELOPMENT_TEAM` de `project.yml` est vide). Branchez l'iPad, sélectionnez-le comme destination, Run.

> L'appairage et la découverte Bonjour exigent une build **signée**. Ne compilez jamais avec `CODE_SIGNING_ALLOWED=NO` pour les tester : ce réglage ne sert qu'au simulateur.

Pour distribuer à plusieurs iPad, utilisez TestFlight ou une distribution interne Apple. Les imprimantes Star et Epson en Bluetooth/USB demandent en plus l'enregistrement MFi avant l'App Store.

### 6.2 Premier lancement de chaque iPad

1. Autorisez l'accès au **réseau local** (demandé par iPadOS) et, pour l'appairage par QR, l'appareil photo.
2. Écran **« Comment cet iPad sera-t-il utilisé ? »** → **« Relié à un serveur »**.
3. L'iPad trouve le serveur par Bonjour, ou touchez **Changer** pour saisir `http://<ip-serveur>:5080`.
4. Appairez-le : scannez le QR affiché dans Paramétrage → Appareils, ou saisissez le code à la main.
5. Saisissez votre PIN.

Les réglages se trouvent ensuite dans Gestion → Réseau & terminal (adresse du serveur, test de connexion, file de synchronisation, sauvegarde du serveur pour les responsables).

## 7. Simulateur iPad (développement)

```bash
cd ios && xcodegen generate
xcodebuild -project RestaurantPOS.xcodeproj -scheme RestaurantPOS \
  -destination 'platform=iOS Simulator,name=iPad Pro 13-inch (M5)' \
  -derivedDataPath build/DerivedData CODE_SIGNING_ALLOWED=NO build
```

Puis `xcrun simctl install` / `xcrun simctl launch com.restaurantpos.ipad`. Arguments utiles : `-UITestMode` (backend en mémoire, sans serveur), `-UITestPaired`, `-UITestPin 1234`, `-UITestLocal` (mode autonome démo), `-UITestLocalBlank` (première configuration autonome), `-UITestModeChoice`. Le simulateur atteint un serveur de la même machine sur `localhost`.

## 8. Cas D : iPad autonome

Aucun serveur, aucun réseau requis. Tout reste sur l'iPad (base SQLite locale, mêmes règles fiscales NF525 que le serveur).

1. Construisez et installez l'app sur l'iPad comme en 6.1.
2. Au premier lancement : **« iPad autonome »**.
3. Écran **« Configurer cet iPad »** : nom du responsable, **PIN à 4 chiffres** (à confirmer), nom de l'établissement, adresse, **SIRET (14 chiffres)**, n° de TVA (facultatif) → **Créer et continuer**.
4. **Configurez la sauvegarde tout de suite** : Gestion → Réseau & terminal → section Sauvegarde. Choisissez une phrase (8 caractères minimum, **non récupérable si perdue**) et un dossier (Fichiers, iCloud Drive, clé USB…). L'iPad sauvegarde ensuite chaque jour s'il est en retard et garde 14 copies ; un export manuel est possible.
5. Le poste est toujours le terminal `T01`.

Un iPad autonome est un poste unique : il n'échange pas avec un serveur ni avec d'autres iPad.

## 9. Imprimantes

- **Avec serveur** : les tickets sont mis en file puis envoyés par le **serveur** aux imprimantes ESC/POS en TCP **9100**. Déclarez-les dans Paramétrage → Imprimantes réseau (adresse IP fixe, port 9100) et faites un ticket d'essai.
- **iPad autonome** : l'iPad imprime directement (réseau, Bluetooth ou USB selon le modèle Epson/Star).
- Détails, stations (cuisine chaude, bar…) et essai papier : `docs/impression.md` et `docs/impression-essai-papier.md`.

## 10. Sauvegarde et restauration

| | Serveur | iPad autonome |
|---|---|---|
| Quand | À chaque heure, si la dernière a plus de 24 h (donc au premier démarrage) | Chaque jour si en retard |
| Fichier | `pos-backup-<UTC>.posbak` chiffré | même format |
| Copies | 14 | 14 |
| Où | `Backup__Path` (défaut `backups/`) | dossier choisi |
| État | Paramétrage → Sauvegarde (web), Réseau & terminal (iPad) | Réseau & terminal |

**Restaurer sur le serveur** (arrêtez l'API) :

```bash
Backup__Passphrase="<phrase>" dotnet RestaurantPos.Api.dll \
  --restore-backup backups/pos-backup-<date>.posbak --to restaurant-restaure.db
```

La commande refuse d'écraser un fichier existant, vérifie les chaînes fiscales NF525 et ne laisse rien en cas d'échec. Remplacez ensuite `restaurantpos.db` par `restaurant-restaure.db` et supprimez `restaurantpos.db-wal` et `-shm`.

Par défaut, les sauvegardes sont sur **le même disque** que la base : une panne du disque perd tout. Pointez `Backup__Path` vers un disque externe ou un dossier synchronisé.

## 11. Mise à jour

1. Sauvegardez (bouton « Sauvegarder maintenant »).
2. Arrêtez l'API, publiez la nouvelle version au même endroit (la base reste intacte), relancez.
3. Le schéma de la base se met à jour tout seul au démarrage.
4. Mettez à jour l'app des iPad (Xcode, TestFlight).

## 12. Dépannage

| Symptôme | Cause probable |
|---|---|
| Connexion impossible, erreur serveur | `Jwt__Secret` absent |
| L'API refuse de démarrer en Production | `Jwt__Secret` vide ou commençant par `ProdSecretKey` ; `Bootstrap__AdminPin` pas à 4 chiffres |
| Aucun PIN ne fonctionne en Production | normal : pas de données de démo ; utilisez `Bootstrap__AdminPin` ou cherchez le PIN généré dans le journal du premier démarrage |
| iPad ne voit pas le serveur | autre sous-réseau/VLAN, pare-feu sur le port 5080, ou autorisation « réseau local » refusée sur l'iPad ; saisissez l'IP à la main |
| `401 device_not_paired` à l'encaissement | appareil non appairé ou révoqué : refaites l'appairage |
| Pas de sauvegarde, avertissement au démarrage | `Backup__Passphrase` absent ou de moins de 8 caractères |
| Données « disparues » après un redémarrage | l'API a été lancée depuis un autre dossier : la base `restaurantpos.db` est relative au dossier de lancement |

## 13. Liste de contrôle de mise en service

- [ ] Serveur à IP fixe, démarrage automatique après coupure
- [ ] `Jwt__Secret` propre au restaurant, `Bootstrap__AdminPin` défini puis PIN admin changé
- [ ] `Backup__Passphrase` définie **et rangée hors de la machine**, `Backup__Path` hors du disque de la base
- [ ] Une restauration d'essai réussie
- [ ] Appareils appairés, chacun testé en encaissement
- [ ] Imprimantes déclarées, ticket d'essai OK
- [ ] Informations légales renseignées (Paramétrage → Établissement & exercice fiscal : SIRET, TVA, date de début d'exercice)
- [ ] iPad autonome : sauvegarde configurée et un export testé
