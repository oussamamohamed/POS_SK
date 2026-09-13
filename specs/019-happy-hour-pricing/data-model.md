# Data Model: Happy Hour Pricing & Schedule Management

**Feature**: [`specs/019-happy-hour-pricing/spec.md`](file:///Users/oussama/Library/CloudStorage/OneDrive-Personnel/Documents/Visual%20Studio%202022/POS_SK_Antigavity/specs/019-happy-hour-pricing/spec.md)  
**Phase**: Phase 1 — Design & Contracts  
**Status**: Completed  

---

## 1. Domain Entities & Value Objects

### 1.1 `HappyHourSchedule` (Aggregate Root)

Represents a recurring or one-off Happy Hour time window.

```csharp
namespace RestaurantPos.Domain.Entities;

public class HappyHourSchedule
{
    public Guid Id { get; set; } = UuidV7.NewGuid();
    public required string Name { get; set; } // e.g. "Afterwork Standard", "Week-end Festif"
    
    // Days of week (Flag or collection)
    public List<DayOfWeek> DaysOfWeek { get; set; } = new();
    
    // Time windows (Local restaurant time)
    public TimeOnly StartTime { get; set; } // e.g. 17:00
    public TimeOnly EndTime { get; set; }   // e.g. 20:00
    
    public bool IsActive { get; set; } = true;
    public bool AppliesToTakeaway { get; set; } = false; // By default on-site only
    public int Priority { get; set; } = 1;               // Higher priority wins on overlap
    
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    
    // Associated pricing rules
    public List<HappyHourPriceRule> PriceRules { get; set; } = new();
}
```

### 1.2 `HappyHourPriceRule`

Defines how an eligible item or category is discounted during a schedule.

```csharp
namespace RestaurantPos.Domain.Entities;

public enum HappyHourTargetType
{
    Product = 0,
    Category = 1
}

public enum HappyHourPricingMode
{
    FixedPrice = 0,         // e.g. exactly 5.00 EUR
    PercentageDiscount = 1  // e.g. 20% off standard price
}

public class HappyHourPriceRule
{
    public Guid Id { get; set; } = UuidV7.NewGuid();
    public Guid ScheduleId { get; set; }
    
    public HappyHourTargetType TargetType { get; set; }
    public Guid TargetId { get; set; }          // ProductId or CategoryId
    public string TargetName { get; set; } = ""; // Human-readable cache
    
    public HappyHourPricingMode PricingMode { get; set; }
    
    // Value: Fixed Price in EUR (Money) OR Percentage (0-100)
    public Money? FixedPrice { get; set; }
    public decimal? DiscountPercent { get; set; }
    
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
```

### 1.3 `HappyHourOverrideSession`

Records manual manager interventions (force trigger, extend).

```csharp
namespace RestaurantPos.Domain.Entities;

public enum HappyHourOverrideType
{
    ForceStart = 0,
    Extend = 1,
    ForceStop = 2
}

public class HappyHourOverrideSession
{
    public Guid Id { get; set; } = UuidV7.NewGuid();
    public required string TerminalId { get; set; }
    public Guid OperatorId { get; set; }
    public required string OperatorName { get; set; }
    
    public HappyHourOverrideType OverrideType { get; set; }
    public DateTimeOffset StartsAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public required string Reason { get; set; }
    public bool IsActive { get; set; } = true;
    
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
```

### 1.4 `OrderItem` (Updated Properties)

Enrich existing `OrderItem` entity with audit fields:

```csharp
public class OrderItem
{
    // Existing fields...
    public Money UnitPrice { get; set; }           // Effective charged price (Money)
    
    // New fields for Happy Hour traceability (NF525 compliant)
    public bool IsHappyHourApplied { get; set; } = false;
    public Money? OriginalUnitPrice { get; set; }  // Standard menu price before Happy Hour
    public Guid? AppliedHappyHourScheduleId { get; set; }
    public DateTimeOffset OrderedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
```

---

## 2. Entity Relationships (ERD)

```text
┌─────────────────────────┐
│    HappyHourSchedule    │
├─────────────────────────┤
│ Id (PK, UUIDv7)         │
│ Name                    │
│ DaysOfWeek (JSON)       │
│ StartTime (TimeOnly)    │
│ EndTime (TimeOnly)      │
│ IsActive (bool)         │
│ AppliesToTakeaway (bool)│
└────────────┬────────────┘
             │ 1
             │
             │ N
┌────────────▼────────────┐
│   HappyHourPriceRule    │
├─────────────────────────┤
│ Id (PK, UUIDv7)         │
│ ScheduleId (FK)         │
│ TargetType (Product/Cat)│
│ TargetId (Guid)         │
│ PricingMode (Fixed/Pct) │
│ FixedPrice (Money)      │
│ DiscountPercent (dec)   │
└─────────────────────────┘

┌───────────────────────────────┐
│   HappyHourOverrideSession    │
├───────────────────────────────┤
│ Id (PK, UUIDv7)               │
│ TerminalId (string)           │
│ OperatorId (Guid)             │
│ StartsAtUtc (DateTimeOffset)  │
│ ExpiresAtUtc (DateTimeOffset) │
│ Reason (string)               │
│ IsActive (bool)               │
└───────────────────────────────┘
```

---

## 3. State & Lifecycle Transitions

### 3.1 Happy Hour State Machine

```text
       ┌──────────────┐
       │   INACTIVE   │ (Outside schedule & no override)
       └──────┬───────┘
              │
    ┌─────────┴────────────────────────┐
    │ (Time in schedule)               │ (Manager PIN override)
    ▼                                  ▼
┌──────────────┐             ┌─────────────────────┐
│    ACTIVE    │             │   ACTIVE OVERRIDE   │
│ (Scheduled)  │             │ (Manager Triggered) │
└──────┬───────┘             └─────────┬───────────┘
       │                               │
       │ (Time > EndTime)              │ (Now > ExpiresAtUtc OR Stop)
       └─────────┬─────────────────────┘
                 │
                 ▼
       ┌──────────────────┐
       │     EXPIRED      │ ──► Reverts to INACTIVE
       └──────────────────┘
```

### 3.2 Order Line Pricing Lifecycle

1. **Item Added**: Evaluated against active pricing table. `UnitPrice` set to Happy Hour rate; `OriginalUnitPrice` saved; `IsHappyHourApplied = true`.
2. **Order Dispatched / Saved**: Line is persisted in database with `UnitPrice` locked.
3. **Happy Hour Closes**: Subsequent additions receive standard price. Prior lines remain unchanged.
4. **Checkout & Payment**: Calculated from persisted line amounts. Fiscal receipt generates `[HH]` tag and mentions savings.
