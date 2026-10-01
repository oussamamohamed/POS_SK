<!--
Sync Impact Report
- Version change: 1.3.0 → 2.0.0 (MAJOR: suppression de MediatR/CQRS, .NET MAUI, PostgreSQL, TableHub et des abstractions matérielles qui n'existent pas dans le code)
- Principes modifiés:
  * II. Clean Architecture & Backend Multi-POS Centralisé (services simples au lieu de MediatR; clients web + SwiftUI au lieu de MAUI)
  * III. Intégrité transactionnelle et opérations offline (SQLite côté serveur, outbox/sync réellement présents)
  * IV. Périphériques, découverte réseau & temps réel (PrintDispatcher/PrintWorker, Bonjour, hubs /hubs/pos et /hubs/kitchen)
  * V. Test-First, Immutabilité & Traçabilité fiscale (formule de hash réelle, JET décrit tel qu'implémenté)
- Sections modifiées: Topologies de déploiement, Qualité & conformité
- Suivi: le journal technique (JET) n'est pas chaîné (EntryHash non cryptographique); ouverture de tiroir, réimpression et changement d'opérateur ne sont pas journalisés. Écarts listés dans specs/006.../spec.md.
- Source de vérité: le code (CLAUDE.md).
-->

# Restaurant POS System Constitution

## Core Principles

### I. Touch-First Ergonomics & iPadOS Tactile Design
Toutes les interfaces de salle MUST être conçues pour écrans tactiles (iPad 10.9", iPad Pro 11"/13", iPad Mini 8.3"). Les cibles tactiles MUST respecter au minimum 54x54 pt (68x68 pt pour les articles à forte cadence). Le clavier système MUST être évité pendant la prise de commande: un pavé numérique intégré et fixe est obligatoire pour ne pas masquer le panier ni les totaux. Gestes naturels (balayage, appui long, pincement sur le plan de salle) et thèmes clair/sombre adaptatifs sont requis. Le retour visuel/haptique MUST rester immédiat (< 50 ms).

### II. Clean Architecture & Backend Multi-POS Centralisé
La solution MUST respecter la Clean Architecture avec les couches `Domain` ← `Application` ← `Infrastructure` ← `Api` (.NET 9, ASP.NET Core Minimal API, `RestaurantPos.slnx`). Il n'y a ni MediatR ni CQRS: les endpoints appellent directement des services injectés (`I*Service` dans `Application/Common/Interfaces`), enregistrés dans `Program.cs`. Le Domain ne dépend d'aucune couche de transport, de base de données ou d'interface. Le backend central est l'orchestrateur faisant autorité (catalogue, TVA, opérateurs, rapports). Les clients sont le client web tactile (`wwwroot/app.js`) et l'app native SwiftUI (`ios/`, Swift 6, iPadOS 17+, logique dans le package `PosKit`). Les DTO du web, de PosKit et de .NET sont synchronisés à la main, avec des fixtures de contrat côté Swift.

### III. Transactional Integrity and Offline-First Operations
Les écritures fiscales MUST être append-only: toute correction passe par une annulation (void) ou un avoir chaîné, jamais par une mise à jour. Le journal `TransactionJournalEntry` et l'endpoint `/api/sync/receipt` assurent la synchronisation idempotente (clé d'idempotence sur chaque mutation). Les identifiants sont des UUIDv7 générés localement. La persistance est SQLite (EF Core, `EnsureCreated()` + `ALTER TABLE` idempotents dans `Program.cs`, sans migrations EF); le fournisseur InMemory est réservé à l'environnement `Testing`. Les routes qui écrivent des reçus MUST exiger un appareil appairé (`X-Device-Token`); le serveur impose le `terminalId` issu de l'appareil. Le client iPad garde un ticket brouillon local et ne l'envoie qu'aux moments clés (envoi cuisine, paiement, remise, mise en attente, transfert).

*Rationale*: un réseau instable ne doit ni interrompre le service ni perdre de transaction.

### IV. Périphériques, découverte réseau & temps réel
L'impression MUST passer par `PrintDispatcher` (mise en file de `PrintJobs`, sans jamais faire échouer l'opération métier) et `PrintWorker` (rendu ESC/POS raster, envoi TCP 9100); la station est résolue article → produit → catégorie → `HOT_KITCHEN`. Le serveur s'annonce en Bonjour (`_restaurantpos._tcp`) via `NetworkDiscoveryBeaconService`. Le temps réel utilise SignalR: `/hubs/pos` (`PosHub`, authentifié) et `/hubs/kitchen` (`KitchenHub`); renommer un événement casse les deux clients. Aucune abstraction `IPrinterService`/`ICashDrawerService`/`IPaymentTerminalService` n'existe à ce jour; les terminaux de paiement et le tiroir-caisse ne sont pas intégrés.

### V. Test-First, Immutabilité & Traçabilité fiscale (NON-NEGOTIABLE)
Les calculs financiers (TVA, remises, partage de note) MUST utiliser le value object `Money` en centimes entiers, sans tolérance d'arrondi; `float`/`double` sont interdits. Les reçus fiscaux et les clôtures Z MUST être chaînés en SHA-256 (norme NF525) par `NF525FiscalAuditService`:

`SignatureHash = HEX(SHA256("{previousHash}|{terminalId}|{sequenceNumber}|{amountCents}|{timestampUtc:O}|{taxBreakdownJson}"))`

La chaîne commence à `GenesisHash` (`GENESIS_` + 64 zéros), est propre à chaque terminal et vérifiable par `ValidateAuditChainIntegrityAsync`. Modifier l'ordre des champs, le format ou la sérialisation de la ventilation TVA invalide les chaînes existantes: c'est INTERDIT sans migration fiscale validée. Un reçu ne peut être annulé que depuis l'appareil qui l'a émis, et plus après une clôture Z couvrant sa période. La clôture Z est refusée tant qu'il reste des commandes ouvertes. Le Journal des Événements Techniques (JET) est tenu dans `TransactionJournalEntry` (événements `EVENT_*`); il n'est pas encore chaîné cryptographiquement. Le TDD est obligatoire pour la logique domaine, complété par des tests d'intégration API (`WebApplicationFactory`), des tests Swift Testing/XCUITest côté iOS et des tests Playwright côté web.

## Topologies de déploiement & pile technique

- **Topologie supportée**: serveur local .NET 9 (mini-PC/Linux/Mac) avec base SQLite, relié en Wi-Fi à plusieurs iPad (app SwiftUI ou client web), écrans cuisine (KDS) et imprimantes ESC/POS réseau.
- **Authentification**: connexion opérateur par PIN → JWT (`Jwt:Secret` obligatoire hors Development), limitation de débit sur le PIN; rôles manager, serveur, cuisine, admin.
- **Objectifs de performance (SLO)**: retour tactile < 50 ms; latence API transactionnelle p95 < 200 ms; synchronisation temps réel multi-terminaux < 200 ms; envoi d'impression < 500 ms. Ces objectifs ne sont pas mesurés automatiquement à ce jour.

## Qualité logicielle & conformité fiscale

- **Analyse statique**: `Directory.Build.props` active `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild` et `AnalysisLevel=latest-recommended`; `.editorconfig` fait foi. Zéro avertissement toléré.
- **Observabilité**: journalisation ASP.NET Core standard. Pas de Polly, FluentValidation, Serilog/OpenTelemetry ni de points de santé `/healthz` à ce jour.
- **Conformité & archivage**: rapports X, clôtures Z (total perpétuel `PerpetualGrandTotalCents`) et export FEC (`FecExportService`) avec audit de la chaîne de hachage. Aucune attestation de conformité NF525 par un tiers n'est documentée.

## Governance

Cette Constitution est la référence architecturale et opérationnelle du projet. En cas de divergence avec le code, le code fait foi et la Constitution MUST être corrigée.

- **Amendements**: tout changement de principe, de pile technique ou de gouvernance requiert une proposition, une revue d'architecture et un consensus explicite.
- **Revues de code**: chaque PR MUST être évaluée au regard de ces principes (ergonomie tactile, frontières Clean Architecture, résilience offline, intégrité du hash fiscal).
- **Versionnage**: Semantic Versioning (MAJOR pour suppression/rupture de gouvernance ou d'architecture, MINOR pour nouveau principe, PATCH pour clarifications).

**Version**: 2.0.0 | **Ratified**: 2026-08-15 | **Last Amended**: 2026-10-01
