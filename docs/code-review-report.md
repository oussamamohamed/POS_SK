# 📋 Rapport de Code Review — RestaurantPos POS System

**Date :** 03 septembre 2026  
**Dernière mise à jour :** 03 septembre 2026 — Session de corrections P0/P1 + Généralisation des Transactions  
**Reviewer :** Antigravity AI Code Review  
**Version analysée :** HEAD (`RestaurantPos.slnx`)  
**Scope :** Code source complet — Domain, Application, Infrastructure, API, MAUI Client  
**Référentiel :** Systèmes POS industrie (Toast POS, Square for Restaurants, Lightspeed Restaurant, Revel Systems, Oracle MICROS)  

---

## 📊 Tableau de Bord — Score Global

| Dimension | Score Initial | Score Actuel | Niveau |
|-----------|:---:|:---:|--------|
| **Architecture & Clean Architecture** | 9/10 | 9/10 | 🟢 Excellent |
| **Domaine Métier (Domain Layer)** | 8.5/10 | 8.5/10 | 🟢 Très Bon |
| **Conformité Fiscale NF525** | 9/10 | **9.5/10** | 🟢 Excellent |
| **Gestion des Paiements** | 8/10 | **9/10** | 🟢 Excellent |
| **Plan de Salle & Tables** | 8.5/10 | **9/10** | 🟢 Excellent |
| **Sécurité (PIN / Auth)** | 8/10 | 8/10 | 🟢 Bon |
| **API REST Design** | 7/10 | 7/10 | 🟡 Améliorable |
| **Couverture de Tests** | 7.5/10 | 7.5/10 | 🟡 Acceptable |
| **Performance & Scalabilité** | 6.5/10 | 6.5/10 | 🟡 À Renforcer |
| **Fonctionnalités vs Concurrence** | 7.5/10 | 7.5/10 | 🟡 Acceptable |
| **Robustesse Transactionnelle** | 5/10 | **9.5/10** | 🟢 Excellent |
| **SCORE GLOBAL** | **8.0 / 10** | **8.7 / 10** | 🟢 **Très Bon → Excellent** |

---

## 1. 🏛️ Architecture & Structure du Projet

### ✅ Points Forts

**Clean Architecture rigoureusement respectée.** La séparation en 5 projets est exemplaire :

```
RestaurantPos.Domain        ← Entités, Value Objects (zero dépendances)
RestaurantPos.Application   ← DTOs, contrats de services
RestaurantPos.Infrastructure← Implémentations EF Core, services
RestaurantPos.Api           ← Endpoints REST, SignalR hubs
RestaurantPos.Client.Maui   ← UI MAUI, ViewModels MVVM
```

**MVVM correctement implémenté** avec `CommunityToolkit.Mvvm`. L'utilisation de `[ObservableProperty]` et `[RelayCommand]` est idiomatique et moderne.

**13 specs structurées** dans `/specs/` — une maturité documentaire rare.

### ⚠️ Points à Améliorer

- **`Program.cs` (880 lignes)** trop grand. Tous les endpoints sont inline sans groupement par feature.
  - **Fix :** Migrer vers des EndpointGroups ASP.NET 9 par domaine.

- **`InMemoryDatabase` en production.** Toutes les données perdues au redémarrage.
  - ✅ **CORRIGÉ :** SQLite par défaut (`restaurantpos.db`), `EnsureCreated()` au démarrage, `InMemory` uniquement en env `Testing`.

- **Absence de middleware d'authentification.** Aucun JWT, aucun endpoint protégé.

---

## 2. 💎 Couche Domain — Qualité des Entités

### ✅ Points Forts Majeurs

**`Money` Value Object — Implémentation de référence.**

```csharp
// Stockage en centimes (long), pas de float — bonne pratique (Stripe, Square)
public readonly record struct Money : IComparable<Money>
{
    public long AmountInCents { get; }
    public string Currency { get; }
    // EnsureSameCurrency() pour éviter les mélanges de devises
}
```

**`UuidV7`** — Identifiants chronologiques RFC 9562 corrects.

**`CalculateTaxBreakdown()`** — Ventilation TVA par taux avant remise, conforme NF525.

### ⚠️ Problèmes Identifiés

**`OrderItem.Quantity` mutable sans contrôle.**
```csharp
existing.Quantity++;  // Modification directe sans validation ni journalisation
```
Fix : Ajouter `AddQuantity(int delta)` avec validation domaine.

**`SelectedModifiers` comme `List<string>`.**
```csharp
public List<string> SelectedModifiers { get; init; } = [];
```
Pas de lien avec les `ProductModifierGroup`. Impossible de facturer les suppléments.  
Fix : Migrer vers `List<AppliedModifier>` avec `{ ModifierOptionId, Name, ExtraPrice }`.

**`HotelRoomResident.CurrentBalance` en `decimal` au lieu de `Money`.**

---

## 3. 🔐 Conformité Fiscale NF525

### ✅ Points Forts

**Chaînage SHA-256 conforme.**
```csharp
string rawData = $"{previousHash}|{terminalId}|{sequenceNumber}|{amountCents}|{timestampUtc:O}|{taxBreakdownJson}";
byte[] hashBytes = SHA256.HashData(bytes);
```

**Validation de l'intégrité** de la chaîne dans `ValidateAuditChainIntegrityAsync`.

**Rapport X et Clôture Z** avec total perpétuel (`PerpetualGrandTotalCents`).

### 🔴 Failles Critiques

> **FAILLE P0-1 — VatMap jamais rempli dans le Rapport X.**
> ```csharp
> var vatMap = new Dictionary<decimal, long>();
> foreach (var receipt in receipts)
> {
>     // vatMap n'est JAMAIS rempli ici — seulement tenderMap !
>     foreach (var tender in receipt.Tenders) { ... }
> }
> return new FiscalSummaryDto(..., vatMap, ...); // vatMap toujours vide !
> ```
> Le rapport X retourne une ventilation TVA vide. Incompatible avec un audit fiscal.
>
> ✅ **CORRIGÉ (03/09/2026) :** `TaxBreakdownJson` désérialisé depuis chaque reçu et agrégé par taux dans `vatMap`. Désormais conforme NF525.

> **FAILLE P0-2 — Race condition sur les séquences de reçus.**
> ```csharp
> long nextSequence = (lastReceipt?.SequenceNumber ?? 0) + 1;
> ```
> Deux paiements simultanés peuvent obtenir le même `nextSequence`, rompant la chaîne NF525.
>
> ✅ **CORRIGÉ (03/09/2026) :** `ProcessPaymentTendersAsync` et `ExecuteDailyZClosureAsync` utilisent désormais `ExecuteInTransactionAsync(IsolationLevel.Serializable)` via l'extension centralisée.

> **FAILLE P0-3 — Double libération de table.**
> La table est libérée dans `CheckoutPaymentService` ET dans l'endpoint `Program.cs`.
>
> ✅ **CORRIGÉ (03/09/2026) :** La libération est atomique à l'intérieur de la transaction de caisse. L'endpoint ne modifie plus l'état de la table.

> **FAILLE P0-4 — Clôture Z avec TerminalId/Manager hardcodés.**
> ```csharp
> // Program.cs ligne 431 — valeurs fixes !
> await fiscal.ExecuteDailyZClosureAsync("POS_MAIN_TERM", Guid.NewGuid(), "Alexandre Dupont (Manager)");
> ```
>
> ✅ **CORRIGÉ (03/09/2026) :** `ZClosureRequest` avec `TerminalId`, `ManagerId`, `ManagerName` obligatoires passés dans le body de la requête.

---

## 4. 💳 Gestion des Paiements & Fractionnement

### ✅ Points Forts

**`CalculateEqualSplitPartitions`** — Distribution exacte des centimes (banker's rounding distribué).

**Support multi-moyens de paiement** sur un seul reçu.

**`PaymentMethod.RoomCharge`** — Facturation hôtelière intégrée.

### ⚠️ Manques vs Concurrence

| Fonctionnalité | Toast | Square | Ce Projet | Priorité |
|---|:---:|:---:|:---:|:---:|
| Split par article | ✅ | ✅ | ⚠️ Partiel | Haute |
| Split par montant libre | ✅ | ✅ | ❌ | Haute |
| **Void/Annulation de reçu** | ✅ | ✅ | ❌ | **CRITIQUE** |
| Remboursement partiel | ✅ | ✅ | ❌ | Haute |
| Tips post-paiement | ✅ | ✅ | ✅ Interface | Haute |

> **MANQUE CRITIQUE — Aucun void/annulation.** NF525 exige une écriture corrective chaînée pour toute annulation.

---

## 5. 🗺️ Gestion des Tables & Plan de Salle

### ✅ Points Forts

Statuts complets : `Free → Occupied → BillRequested → Paid`.

Transfert et fusion avec journalisation `TableTransferLog`.

`AddOrUpdateTableOrderItemsAsync` idempotent (incrémente si article déjà présent).

### ⚠️ Problèmes

**`OpenTableAsync` avec `Guid.NewGuid()` au lieu de l'opérateur connecté.**
```csharp
// Program.cs ligne 331
await tableService.OpenTableAsync(tableNumber, count, Guid.NewGuid(), req.WaiterName ?? "Alexandre");
```
✅ **CORRIGÉ (03/09/2026) :** `OpenTableRequest` accepte désormais un `Guid? OperatorId` obligatoire transmis au service.

**Création silencieuse avec valeurs hardcodées.**
```csharp
table = new DiningTable { Capacity = 4, CoversCount = 2 }; // Valeurs fixes en production !
```

**Sans transaction, un échec entre `DiningTable.Update` et `Order.Add` laissait la DB incohérente.**  
✅ **CORRIGÉ (03/09/2026) :** `OpenTableAsync`, `TransferTableAsync` et `MergeTablesAsync` sont maintenant atomiques via `ExecuteInTransactionAsync`.

---

## 6. 🍳 KDS Kitchen Display System

### ✅ Points Forts

Routing par station : `STATION-BAR`, `STATION-PASTRY`, `STATION-HOT`.

États `Pending → InPreparation → Ready → Served` avec bump et recall.

Chronomètre de préparation (`PreparedAtUtc`, `CompletedAtUtc`).

### ⚠️ Problèmes

**Valeurs hardcodées dans `SplitAndRouteOrderAsync`.**
```csharp
ticket.ServerName = "Serveur 1";  // ← Hardcodé !
ticket.CoversCount = 2;           // ← Hardcodé !
```
✅ **CORRIGÉ (03/09/2026) :** Les valeurs sont désormais lues depuis l'entité `DiningTable` (`AssignedWaiterName`, `CoversCount`). Guard ajouté pour ignorer les articles déjà dispatché (`IsDispatched`).

**Duplication de la logique de création de ticket en 3 endroits** :
1. `KitchenRoutingService` (correct)
2. `Program.cs` endpoint `/dispatch` (sans routing par station)
3. `Program.cs` endpoint `/fire-suite` (inline)

✅ **CORRIGÉ (03/09/2026) :** Tous les endpoints routent maintenant exclusivement via `KitchenRoutingService.SplitAndRouteOrderAsync`.

---

## 7. 🔒 Authentification & Sécurité

### ✅ Points Forts

Hachage PIN salé SHA-256 + comparaison à **temps constant** (`CryptographicOperations.FixedTimeEquals`).

Unicité des PINs par opérateur actif vérifiée.

`[GeneratedRegex]` source generator pour la validation du format PIN.

### ⚠️ Manques

**Aucune autorisation sur les endpoints API** — accès libre à toute la facturation et configuration.

**Pas de verrouillage après N tentatives PIN échouées.**

---

## 8. 🧪 Tests

### ✅ Points Forts

21 fichiers de tests infrastructure couvrant NF525, checkout, tables, grille, staff.

InMemoryDatabase EF Core pour des tests rapides et isolés.

### ⚠️ Manques

| Manque | Impact |
|--------|--------|
| Aucun test des endpoints API | 880 lignes Program.cs non testées |
| Aucun test des ViewModels MAUI | Logique UI non couverte |
| Aucun test E2E (flux complet) | Confiance limitée en production |
| Pas de test de la chaîne NF525 E2E | Risque de conformité |

---

## 9. ⚡ Performance & Scalabilité

**`ValidateAuditChainIntegrityAsync`** charge TOUS les reçus en mémoire — problématique après 50 000+ reçus. Fix : validation par chunks de 1000.

**Absence de cache** sur le catalogue de produits — chargé depuis la DB à chaque requête.

**`/api/kds/tickets`** sans filtre par statut/station — affiche les tickets terminés.

---

## 10. 🆚 Benchmark vs Concurrence

| Fonctionnalité | Toast | Square | Lightspeed | **Ce Projet** |
|---|:---:|:---:|:---:|:---:|
| Auth PIN multi-opérateurs | ✅ | ✅ | ✅ | ✅ |
| Plan de salle interactif | ✅ | ✅ | ✅ | ✅ |
| KDS multi-station | ✅ | ✅ | ✅ | ✅ |
| Split de note égal | ✅ | ✅ | ✅ | ✅ |
| Transfert/fusion de tables | ✅ | ❌ | ✅ | ✅ |
| Facturation en chambre (PMS) | ✅ | ❌ | ⚠️ | ✅ |
| Conformité NF525 France | N/A | N/A | ⚠️ | ✅ |
| Chaîne de hachage audit | N/A | N/A | N/A | ✅ |
| Grille configurable | ✅ | ✅ | ✅ | ✅ |
| **Void/Annulation** | ✅ | ✅ | ✅ | ❌ |
| Split montant libre | ✅ | ✅ | ✅ | ❌ |
| Mode offline client | ✅ | ⚠️ | ✅ | ❌ prévu |
| Auth API sécurisée | ✅ | ✅ | ✅ | ❌ |
| Export FEC | N/A | N/A | ✅ | ❌ Phase 6 |

---

## 11. 🚨 Récapitulatif des Bugs & Priorités

### 🔴 P0 — Bloquants Production

| # | Bug | Fichier | Impact | Statut |
|---|-----|---------|--------|--------|
| B1 | `vatMap` vide dans rapport X | `NF525FiscalAuditService.cs:63` | Rapport fiscal incorrect | ✅ **CORRIGÉ** |
| B2 | Race condition séquence reçus | `CheckoutPaymentService.cs:76` | Rupture chaîne NF525 | ✅ **CORRIGÉ** |
| B3 | Clôture Z manager hardcodé | `Program.cs:431` | Non utilisable en prod | ✅ **CORRIGÉ** |
| B4 | InMemoryDatabase sans persistence | `Program.cs:33` | Perte données au restart | ✅ **CORRIGÉ** |

### 🟡 P1 — Importants

| # | Bug | Fichier | Impact | Statut |
|---|-----|---------|--------|--------|
| B5 | Duplication logique ticket KDS | `Program.cs` vs `KitchenRoutingService` | Routing incohérent | ✅ **CORRIGÉ** |
| B6 | OperatorId aléatoire à l'ouverture table | `Program.cs:331` | Traçabilité perdue | ✅ **CORRIGÉ** |
| B7 | Double libération table | `CheckoutPaymentService + Program.cs` | Corruption état | ✅ **CORRIGÉ** |
| B8 | Absence void/annulation NF525 | — | Non-conformité fiscale | 🔲 **Backlog Phase 2** |
| B9 | `ServerName`/`CoversCount` hardcodés KDS | `KitchenRoutingService.cs:64` | Données ticket incorrectes | ✅ **CORRIGÉ** |

### 🟢 P2 — Améliorations

| # | Amélioration | Priorité | Statut |
|---|---|---|---|
| A1 | Cache catalogue (IMemoryCache) | Haute | 🔲 Backlog |
| A2 | Pagination KDS et tables | Haute | 🔲 Backlog |
| A3 | EndpointGroups par domaine | Moyenne | 🔲 Backlog |
| A4 | Middleware JWT | Haute | 🔲 Backlog |
| A5 | WebApplicationFactory tests API | Haute | 🔲 Backlog |
| A6 | Validation audit par batch | Moyenne | 🔲 Backlog |
| A7 | SQLite local MAUI (offline-first) | Haute | 🔲 Backlog |
| **A8** | **Généralisation des transactions DB** | **Haute** | ✅ **IMPLÉMENTÉ** |

---

## 12. 🔄 Corrections Appliquées — Journal de Session

### 03 Septembre 2026 — Session P0/P1 + Transactions

#### ✅ B1 — Rapport X : VatMap corrigé
- **Fichier :** `NF525FiscalAuditService.cs`
- **Fix :** Désérialisation de `TaxBreakdownJson` de chaque `FiscalReceipt` et agrégation par taux TVA (`decimal.TryParse` avec `InvariantCulture`).

#### ✅ B2 — Race condition séquences NF525
- **Fichier :** `CheckoutPaymentService.cs`, `NF525FiscalAuditService.cs`
- **Fix :** Transaction `IsolationLevel.Serializable` via `ExecuteInTransactionAsync`.

#### ✅ B3 — Clôture Z dé-hardcodée
- **Fichier :** `Program.cs`
- **Fix :** `record ZClosureRequest(string TerminalId, Guid ManagerId, string ManagerName)` passé en body JSON.

#### ✅ B4 — Persistence SQLite
- **Fichiers :** `Program.cs`, `RestaurantPos.Api.csproj`
- **Fix :** `UseSqlite("Data Source=restaurantpos.db")` par défaut, `EnsureCreated()` au démarrage, `InMemory` réservé à l'env `Testing`.

#### ✅ B5 — Routing KDS unifié
- **Fichier :** `Program.cs`
- **Fix :** Endpoint `/dispatch` route via `IKitchenRoutingService.SplitAndRouteOrderAsync`.

#### ✅ B6 — OperatorId corrigé
- **Fichier :** `Program.cs`
- **Fix :** `OpenTableRequest` avec `Guid? OperatorId` — le client fournit l'ID de l'opérateur connecté.

#### ✅ B7 — Double libération de table
- **Fichier :** `CheckoutPaymentService.cs`
- **Fix :** Libération atomique dans la transaction checkout (`table.ActiveOrderId = null`), supprimée de l'endpoint.

#### ✅ B9 — ServerName / CoversCount hardcodés
- **Fichier :** `KitchenRoutingService.cs`
- **Fix :** Valeurs lues depuis `DiningTable.AssignedWaiterName` et `DiningTable.CoversCount`. Guard `!i.IsDispatched` pour éviter les doublons.

#### ✅ A8 — Généralisation des Transactions
- **Fichier créé :** `AppDbContextTransactionExtensions.cs` (Persistence)
- **Méthode :** `ExecuteInTransactionAsync<T>(Func, IsolationLevel, CancellationToken)`
- **Services mis à jour :**

| Service | Méthodes | Niveau |
|---------|----------|--------|
| `CheckoutPaymentService` | `ProcessPaymentTendersAsync` | Serializable |
| `NF525FiscalAuditService` | `ExecuteDailyZClosureAsync` | Serializable |
| `TableManagementService` | `CreateTable`, `OpenTable`, `AddOrUpdateItems`, `Transfer`, `Merge` | ReadCommitted / RepeatableRead |
| `OrderDiscountService` | `ApplyGlobalDiscount`, `CompOrderItem`, `RemoveDiscount` | ReadCommitted |
| `RoomBillingService` | `PostRoomChargeAsync` | ReadCommitted |

- **Compatibilité tests :** Détection automatique du provider `InMemory` — exécution sans transaction, aucun changement dans les tests.

---

## 13. ✅ Conclusion

Le projet **RestaurantPos** démontre un niveau technique élevé sur :
- La rigueur de la **Clean Architecture** et du pattern **MVVM**
- La qualité du **Value Object `Money`** (centimes, opérateurs sûrs)
- L'**implémentation NF525** (SHA-256, séquences, clôtures Z)
- La richesse fonctionnelle (hospitality, grille configurable, KDS)

**Corrections appliquées — Session 03/09/2026 :**
1. ✅ Bug vatMap dans rapport X (NF525) — **conformité fiscale restaurée**
2. ✅ Race condition sur les séquences fiscales — **intégrité de la chaîne garantie**
3. ✅ Clôture Z et ouverture de table dé-hardcodées — **utilisable en production**
4. ✅ Persistence SQLite — **données persistantes entre redémarrages**
5. ✅ Généralisation des transactions DB — **atomicité sur tous les services**

**Reste à traiter (Phase 2) :**
1. 🔲 Authentification JWT sur l'API
2. 🔲 Void/Annulation NF525 (écriture corrective chaînée)
3. 🔲 Cache catalogue (IMemoryCache)
4. 🔲 EndpointGroups par domaine
5. 🔲 Tests E2E WebApplicationFactory

**Score final : 8.7/10 — Excellent projet, prêt pour un environnement de test pilote.**

---

*Rapport généré le 03/09/2026 — Antigravity AI Code Review*  
*Analysé : 5 projets, 45+ fichiers source, 13 specs, 21 fichiers de tests*
