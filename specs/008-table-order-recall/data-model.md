# Data Model & State Transitions: Table Order Recall & Cart Hydration

**Feature**: `008-table-order-recall`  
**Date**: 2026-08-30  

---

## 1. Modèle Conceptuel & Relations

```mermaid
erDiagram
    DiningTable ||--o| Order : "has active order"
    Order ||--|{ OrderLine : "contains"
    OrderLine ||--o{ OrderLineModifier : "has customizations"
    Order ||--o| FiscalReceipt : "settled by"

    DiningTable {
        string TableNumber PK
        int Capacity
        TableStatus Status
        Guid ActiveOrderId FK
        string AssignedWaiterName
        int CoversCount
        DateTimeOffset OpenedAtUtc
    }

    Order {
        Guid Id PK
        string TableNumber
        Guid OperatorId
        string OperatorName
        OrderStatus Status
        DateTimeOffset CreatedAtUtc
        DateTimeOffset UpdatedAtUtc
    }

    OrderLine {
        Guid Id PK
        Guid OrderId FK
        Guid ProductId
        string ProductName
        int Quantity
        Money UnitPrice
        decimal TaxRatePercent
        string PreparationStationId
        bool IsDispatched
        DateTimeOffset AddedAtUtc
    }

    OrderLineModifier {
        Guid Id PK
        Guid OrderLineId FK
        string OptionName
        Money ExtraPrice
    }
```

---

## 2. Machine à États du Panier & Cycle de Vie d'une Table

```mermaid
stateDiagram-v2
    [*] --> Free : Table initialisée
    Free --> Occupied : Sélection table + saisie couverts (OpenTable)
    Occupied --> Occupied : Ajout d'articles (Lignes Pending)
    Occupied --> Occupied : Rappel de table (Order Recall / Hydration)
    Occupied --> InKitchen : Envoi Cuisine (Lignes -> IsDispatched = true)
    InKitchen --> Occupied : Ajout desserts / cafés (Nouvelles lignes Pending)
    InKitchen --> BillRequested : Demande d'addition / Split Note
    BillRequested --> Paid : Encaissement total (Checkout & Fiscal Receipt)
    Paid --> Free : Libération automatique de la table
```

---

## 3. DTOs d'Échange de Données

```csharp
public record ActiveTableOrderDto(
    Guid OrderId,
    string TableNumber,
    string? WaiterName,
    int CoversCount,
    DateTimeOffset OpenedAtUtc,
    IReadOnlyList<ActiveOrderLineDto> Lines,
    decimal TotalHtAmount,
    decimal TotalVatAmount,
    decimal TotalTtcAmount
);

public record ActiveOrderLineDto(
    Guid LineId,
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal TotalPrice,
    decimal TaxRatePercent,
    string? PreparationStationId,
    bool IsDispatched,
    IReadOnlyList<string> ModifiersSummary
);
```
