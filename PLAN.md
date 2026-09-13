# Implementation Plan — Restaurant POS System (Tactile & Offline-First)

**Projet :** Système de Point de Vente (POS) Tactile pour Restaurant sur iPad / iOS 
**Statut :** Spécification Technique & Plan d'Exécution Complet  
**Cible Client :** iPadOS 17+ & iOS 17+ (Apple iPad 10.2", 10.9", iPad Pro 11"/13", iPad Mini)  
**Stack Frontend :** .NET MAUI (C# / XAML ou C# Markup) pour iOS/iPadOS  
**Stack Backend :** .NET 9 (C#) / ASP.NET Core (Clean Architecture + CQRS + SignalR)  
**Mode de Fonctionnement :** Local-First / Hybride (SQLite local sur iPad + Sync temps réel)  
**Conformité Fiscale :** Traçabilité inaltérable, Chaînage SHA-256 & Clôtures Z (Norme NF525)

## 1. Vision, Ergonomie iPadOS & Spécifications Matérielles

### 1.1 Objectif Général
Concevoir une application de caisse enregistreuse tactile, de prise de commande mobile et de gestion de salle fonctionnant nativement sur **iPad et terminaux iOS/iPadOS**. L'application garantit une réactivité instantanée, un fonctionnement hors-ligne complet (Local-First) et la communication avec les périphériques d'impression thermique et d'encaissement.

### 1.2 Cibles Matérielles & Écosystème Apple
* **Caisse Fixe Comptoir :** iPad 10.9" ou iPad Pro 11"/13" monté sur support antivol orientable avec alimentation permanente (mode paysage fixe).
* **Prise de Commande Mobile (Serveurs) :** iPad Mini 8.3" ou iPhone / iPod Touch renforcé (mode portrait/paysage dynamique, utilisation à une main).
* **Écrans Cuisine (KDS) :** iPad Pro 12.9"/13" ou tablette murale dédiée connectée en WebSocket temps réel.
* **Imprimantes Thermiques :** Imprimantes réseau Ethernet/Wi-Fi 80 mm et 58 mm (Epson, Star Micronics, Munbyn) pilotées via sockets TCP bruts (port 9100) ou protocoles ESC/POS réseau.
* **Tiroir-Caisse :** Déclenchement automatique 24V via le port RJ11 de l'imprimante thermique principale.
* **Terminaux de Paiement (TPE) :** 
  * TPE bancaire IP (Protocole Concert / TCP local).
  * Lecteurs mobiles Bluetooth Low Energy (ex: Stripe Terminal, Zettle, SumUp via SDKs iOS natifs).

### 1.3 Principes Ergonomiques & UX iPadOS
* **Touch Target Size :** Boutons d'articles d'au moins 54x54 pt (recommandation Apple HIG : $\ge 44\times 44\text{ pt}$), idéalement 68x68 pt pour les flux rapides en coup de feu.
* **Clavier Virtuel Tactile Sur-Mesure :** Pavé numérique fixe à l'écran pour éviter l'apparition du clavier virtuel système iOS qui masquerait le panier.
* **Gestuelle Naturelle iPadOS :**
  * *Swipe gauche* : suppression rapide ou mise en attente d'une ligne d'article.
  * *Long Press (Haptic Touch)* : ouverture de la pop-up de modificateurs (cuissons, sauces, suppléments, commentaires cuisine).
  * *Pinch to Zoom* : zoom/dézoom fluide sur le plan de salle 2D interactif.
* **Adaptabilité & Dark Mode :** Support complet des thèmes clair/sombre d'iPadOS (sombre pour les ambiances tamisées des bars, clair à fort contraste pour les terrasses).

---

## 2. Topologie de Déploiement & Architecture Technique

### 2.1 Modèles de Topologie Supportés

```text
┌─────────────────────────────────────────────────────────────────────────────────────────┐
│ OPTION A : Restaurant Multi-Postes (Architecture Recommandée)                          │
│                                                                                         │
│  ┌──────────────────────┐         ┌──────────────────────┐                              │
│  │ iPad Caisse Comptoir │         │ iPad Serveur Mobile  │                              │
│  │  (.NET MAUI Client)  │         │  (.NET MAUI Client)  │                              │
│  └──────────┬───────────┘         └──────────┬───────────┘                              │
│             │                                │                                          │
│             │       Réseau Wi-Fi Local       │                                          │
│             └────────────────┬───────────────┘                                          │
│                              ▼                                                          │
│                 ┌─────────────────────────┐                                             │
│                 │   Serveur Local .NET 9   │ (Mini-PC / Box Linux / Mac mini local)     │
│                 │ ASP.NET Core + SignalR  │ ➔ PostgreSQL + SQLite Backup                │
│                 │  + Hub mDNS / Zeroconf  │ ➔ Queue d'événements offline               │
│                 └────────────┬────────────┘                                             │
│                              │                                                          │
│              ┌───────────────┴───────────────┐                                          │
│              ▼                               ▼                                          │
│  ┌───────────────────────┐       ┌───────────────────────┐                              │
│  │ Imprimante ESC/POS 80mm│      │ TPE Bancaire Réseau   │                              │
│  │ (Cuisine / Bar / Note)│       │ (Protocole Concert/IP)│                              │
│  └───────────────────────┘       └───────────────────────┘                              │
└─────────────────────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────────────────────┐
│ OPTION B : Standalone iPad (Caisse Unique Autonome / Food-Truck)                        │
│                                                                                         │
│  ┌───────────────────────────────────────────────────────────┐                          │
│  │ iPad (.NET MAUI App)                                      │                          │
│  │ ├─ UI XAML Tactile                                        │                          │
│  │ ├─ SQLite Local Embarqué (App Sandbox)                    │                          │
│  │ └─ Moteur Métier & Fiscalité C# embarqué                  │                          │
│  └─────────────────────────────┬─────────────────────────────┘                          │
│                                │ (Wi-Fi Local / Bluetooth)                              │
│                                ▼                                                        │
│                    ┌───────────────────────┐                                            │
│                    │ Imprimante Ticket IP  │ (+ Tiroir-Caisse RJ11)                     │
│                    └───────────────────────┘                                            │
└─────────────────────────────────────────────────────────────────────────────────────────┘

##3. Configuration & Contraintes Spécifiques iOS / iPadOS
###3.1 Déclarations Info.plist Obligatoires
Pour permettre à l'application iPad de dialoguer avec les imprimantes locales et le serveur de caisse sans blocage de sécurité Apple

##4. Description Détaillée des 6 Phases d'Implémentation
Phase 0 : Cadrage Architectural, Contraintes Techniques & Standards de Développement
Objectif : Définir le cadre normatif, valider les règles de résilience réseau (Local-First), figer les contraintes d'exécution de l'écosystème Apple et initialiser la structure globale du repository.
Contraintes Techniques Majeures :
Sandbox iOS & Sécurité Réseau Apple : Prise en compte du sandboxing applicatif strict d'iOS (fichiers de données limités à AppDataDirectory), de la politique de consommation d'énergie en arrière-plan et de la demande explicite de permission pour scanner le réseau local (NSLocalNetworkUsageDescription).
Architecture Résiliente (Offline-First & Eventual Consistency) : Tolérance aux déconnexions Wi-Fi intempestives lors des déplacements des serveurs en salle. Implémentation d'une file d'attente locale (Outbox Pattern) garantissant zéro perte de commande avec réconciliation automatique via identifiants uniques décentralisés (UUIDv7 chronologiques).
Isolation Cryptographique & Intégrité Fiscale (NF525) : Définition stricte des règles d'inaltérabilité : impossibilité technique de modifier ou supprimer une ligne d'écriture comptable sans émettre un événement correctif chaîné en SHA-256.
Conventions & Standards de Code : Adopter la Clean Architecture stricte, le pattern MVVM via CommunityToolkit.Mvvm pour MAUI, CQRS avec MediatR et validation déclarative avec FluentValidation.
Livrables de Phase 0 :
Document d'architecture technique (DAT) validé.
Solution .sln initialisée avec configuration .editorconfig, Directory.Build.props et règles de linting C# strictes.
Matrice d'analyse des risques de déconnexion réseau et protocole de réconciliation de données.
Phase 1 : Socle Technique, Authentification PIN & Persistance HybrideObjectif : Établir la Clean Architecture C# partagée, configurer l'application .NET MAUI pour iPadOS et implémenter l'authentification express par code PIN.Spécifications Techniques :Solution multi-projets avec partage de code (Domain et Application compilés pour .NET 9 et .NET MAUI iOS).Configuration SQLite locale embarquée dans le sandbox de l'iPad (FileSystem.AppDataDirectory).Écran PinLockPage en XAML : saisie 4-6 chiffres avec retour haptique et validation < 50 ms.Synchronisation asynchrone des référentiels (Serveurs, Produits, Catégories, TVA) depuis le serveur maître vers le cache local SQLite de l'iPad.Critères d'Acceptation :L'application iPad se lance et s'authentifie instantanément même en mode avion (100% hors-ligne).
Phase 2 : Moteur de Prise de Commande Tactile & Panier (CQRS)Objectif : Offrir une expérience de prise de commande tactile fluide et rapide, adaptée aux doigts sur écran Retina iPad.Spécifications Techniques :Interface PosTerminalPage :Grille d'articles dynamique avec tuiles larges et filtres par catégorie.Panier dynamique à défilement tactile fluide avec calcul des totaux HT/TTC et ventilation TVA en temps réel.Pavé numérique tactile personnalisé intégré à l'écran (sans popup clavier iOS).Gestion des variantes et modificateurs : popup native iPadOS pour choix de cuisson, sauces, garnitures et commentaires cuisine.Validation stricte des règles métier et menus via FluentValidation.Critères d'Acceptation :Saisie d'une commande complète de 5 articles avec options en moins de 10 secondes.Calcul exact au centime près des taxes et totaux.Phase 3 : Plan de Salle Interactif & Dispatching Cuisine (SignalR)Objectif : Visualiser et manipuler le plan de salle tactile 2D sur iPad et router automatiquement les commandes vers les KDS et imprimantes de production.Spécifications Techniques :Composant graphique FloorPlanView en MAUI Graphics :Tables configurables en drag-and-drop avec indicateurs d'état colorés (Libre, Occupée, Addition, Encaissée).Chronomètres d'occupation par table pour surveiller les temps de service.Service SignalR client (SignalRClientService) gérant les reconnexions automatiques avec mise en file d'attente locale des événements lors des pertes de signal Wi-Fi.Routage des bons de commande par station de préparation (Chaud, Froid, Bar) et gestion des réclames ("Envoi Suite").Critères d'Acceptation :Tout changement d'état sur un iPad est répercuté sur l'ensemble des autres iPads en < 200 ms.
Phase 4 : Moteur d'Encaissement, Split de Note & Conformité NF525Objectif : Gérer les paiements multiples, le partage de note complexe et sceller cryptographiquement toutes les transactions.Spécifications Techniques :Modal de fractionnement de note SplitBillModal :Split à parts égales (calcul automatique par convive).Split à l'article (sélection tactile des lignes à régler).Règlement partiel libre par moyen de paiement (CB, Espèces avec calcul du rendu, Titres Restaurant).Moteur de conformité fiscale NF525 :Chaînage cryptographique SHA-256 : {Hash}_n = {SHA256}({Hash}_{n-1} + {HorodatageUtc} + {TotalTTC} + {VentilationTVA})$.Journal des Événements Techniques (JET) enregistrant les annulations, réimpressions et ouvertures manuelles de tiroir.Clôture journalière (Rapport Z) avec archivage inaltérable scellé.Critères d'Acceptation :Tolérance zéro sur les écarts de centimes lors du fractionnement.Intégrité absolue de la chaîne de hash vérifiable par audit.
Phase 5 : Pilotes Périphériques ESC/POS & Découverte Réseau iOSObjectif : Imprimer les tickets et ouvrir le tiroir-caisse directement depuis l'iPad via le réseau local Wi-Fi.Spécifications Techniques :Driver IosNetworkPrinterService utilisant les sockets TCP natifs .NET sur port 9100, conforme aux autorisations réseau d'iOS (NSLocalNetworkUsageDescription).Encodage ESC/POS direct : découpe papier (GS V 66 0), impulsion tiroir (ESC p 0 25 250), alignement et QR code fiscal.Découverte mDNS / Bonjour : détection automatique de l'adresse IP du serveur de caisse et des imprimantes sur le réseau local sans configuration manuelle d'IP.Critères d'Acceptation :Sortie du ticket sur imprimante thermique 80 mm et ouverture du tiroir en moins de 500 ms après validation sur l'iPad.
Phase 6 : Back-Office, Reporting Financier & Déploiement App Store / MDMObjectif : Fournir l'administration de la carte, les rapports financiers et configurer le déploiement sur la flotte d'iPads.Spécifications Techniques :Écrans d'administration et de gestion des prix/stocks.Tableaux de bord financiers (CA HT/TTC, panier moyen, statistiques serveurs, export comptable FEC).Packaging et distribution :Configuration Apple Business Manager (ABM) / MDM pour déploiement privé en entreprise (Custom Apps / Ad-Hoc).Mode Kiosque iOS (Single App Mode / Accès Guidé) pour verrouiller l'iPad sur l'application de caisse.Critères d'Acceptation :Application déployable et verrouillée en mode kiosque sur la flotte d'iPads.Export comptable normalisé validé.

## 5. Matériel Matériel Requis (Exemple de Configuration pour 2 Postes Caisse)

| Composant | Référence / Spécifications | Rôle | Quantité | Notes |
|-----------|-----------------------------|------|----------|-------|
| **Tablette POS** | iPad 10e génération ou iPad Air (10.9") - 64 Go minimum | Interface opérateur tactile (Front-of-House) | 2 | Support orientable vertical/horiz., protection écran antiglisse |
| **Imprimante Ticket** | Epson TM-T20III (ou équivalent 80mm USB/Ethernet) | Impression tickets de caisse et reçus | 2 | Connexion via réseau local (RJ45) |
| **Tiroir-caisse** | Déclenchement électronique via imprimante (RJ11) | Sécurité espèces | 2 | Optionnel si tiroir intégré |
| **Serveur Caisse** | mini-PC fanless (Intel Core i3/i5 10e gen, SSD NVMe 256Go, 8Go RAM, 2x port GbE) | Base de données, API REST, SignalR | 1 | Windows 11 Pro ou Linux |
| **Imprimante Cuisine** | Epson TM-T88 (ou équivalent thermique) | Impression bons de préparation | 1-2 | Selon nombre de postes cuisine |
| **Écran Cuisine** | Écran industriel tactile 15" (optionnel KDS) | Affichage commandes cuisine | 1-2 | Selon nombre de postes cuisine |
| **Routeur/Switch** | Switch Gigabit PoE 24 ports avec gestion VLAN | Réseau local professionnel sécurisé | 1 | Pour séparer réseau invités et réseau caisse |
| **Connectique** | Câbles Ethernet Cat 6, supports iPad, protections IP55 | Installation physique | |

## 6. Matrice des tâches & Statut d'Exécution

| Task ID | Phase | Couche / Fichier Cible | Responsabilité Technique | Dépendances | Statut |
| :--- | :--- | :--- | :--- | :--- | :---: |
| **[T001]** | Phase 1 | `RestaurantPos.Domain/Entities/` | Définition des entités C# partagées (Product, Category, TaxRate, User) | - | **[X] Terminé** |
| **[T002]** | Phase 1 | `RestaurantPos.Infrastructure/Persistence/` | Setup EF Core multi-provider (AppDbContext, SQLite / Postgres) | [T001] | **[X] Terminé** |
| **[T003]** | Phase 1 | `RestaurantPos.Client.Maui/Platforms/iOS/` | Configuration Info.plist, entitlements réseau local et orientation iPad | - | **[X] Terminé** |
| **[T004]** | Phase 1 | `RestaurantPos.Client.Maui/Views/PinLockPage.xaml` | Écran tactile de verrouillage et authentification express par PIN | [T003] | **[X] Terminé** |
| **[T005]** | Phase 2 | `RestaurantPos.Domain/ValueObjects/` | Implémentation des Value Objects Money (centimes) et TaxBreakdown | [T001] | **[X] Terminé** |
| **[T006]** | Phase 2 | `RestaurantPos.Application/Features/Orders/` | Handlers MediatR CreateOrderCommand, AddOrderItemCommand | [T005] | **[X] Terminé** |
| **[T007]** | Phase 2 | `RestaurantPos.Client.Maui/Views/PosTerminalPage.xaml` | Vue principale de vente tactile avec pavé numérique intégré pour iPad | [T004], [T006] | **[X] Terminé** |
| **[T008]** | Phase 3 | `RestaurantPos.Domain/Entities/DiningTable.cs` | Modélisation des tables et transitions d'états de salle | [T001] | **[X] Terminé** |
| **[T009]** | Phase 3 | `RestaurantPos.Api/Hubs/PosHub.cs` | Hub SignalR pour la synchro temps réel du plan de salle et KDS | [T008] | **[X] Terminé** |
| **[T010]** | Phase 3 | `RestaurantPos.Client.Maui/Views/FloorPlanPage.xaml` | Vue graphique 2D du plan de salle interactive avec MAUI Graphics | [T008], [T009] | **[X] Terminé** |
| **[T011]** | Phase 3 | `RestaurantPos.Client.Maui/Views/KitchenKdsPage.xaml` | Écran KDS tactile pour la cuisine avec dispatching par station | [T009] | **[X] Terminé** |
| **[T012]** | Phase 4 | `RestaurantPos.Application/Features/Payments/` | Logique CQRS de paiement et fractionnement de note (égale, article, montant) | [T006] | **[X] Terminé** |
| **[T013]** | Phase 4 | `RestaurantPos.Client.Maui/Views/SplitBillModal.xaml` | Interface tactile iPad pour le partage d'addition à l'article ou parts égales | [T012] | **[X] Terminé** |
| **[T014]** | Phase 4 | `RestaurantPos.Infrastructure/Fiscal/` | Intercepteur EF Core de chaînage cryptographique SHA-256 (Norme NF525) | [T012] | **[X] Terminé** |
| **[T015]** | Phase 4 | `RestaurantPos.Application/Features/FiscalReports/` | Moteur de génération du Rapport X et de clôture journalière Z | [T014] | **[X] Terminé** |
| **[T016]** | Phase 5 | `RestaurantPos.Client.Maui/Services/IosNetworkPrinterService.cs` | Service socket TCP brut (port 9100) pour impression thermique depuis iPad | [T003], [T012] | **[X] Terminé** |
| **[T017]** | Phase 5 | `RestaurantPos.Infrastructure/Discovery/` | Service de découverte mDNS / Bonjour pour appairage automatique des iPads | [T003] | **[X] Terminé** |
| **[T018]** | Feature 014 | `RestaurantPos.Api/Endpoints/AuthEndpoints.cs` | Authentification JWT, RBAC multi-rôles, TestAuthHandler et limitation de débit | [T001] | **[X] Terminé** |
| **[T019]** | Tests | `tests/` | Suite complète de tests unitaires & d'intégration (112 tests réussis, 0 échec) | - | **[X] Terminé** |
| **[T020]** | Phase 6 | `RestaurantPos.Infrastructure/Services/FecExportService.cs` | Export comptable officiel FEC (Art. A.47 A-1 LPF), 18 colonnes DGFIP et équilibre Débit/Crédit | [T015] | **[X] Terminé** |
| **[T021]** | Phase 6 | `RestaurantPos.Infrastructure/Services/FinancialDashboardService.cs` | Tableaux de bord financiers, KPIs temps réel, services Midi/Soir et palmarès des ventes | [T020] | **[X] Terminé** |