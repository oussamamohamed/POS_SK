# Data Model: UI Profile Simulation Tests

**Feature**: `015-ui-profile-simulation-tests`
**Phase**: 1 — Design

---

## Key Entities

All entities below are test-infrastructure types — no production code changes.

---

### SimulatedOperator

Represents a fixed test persona with a known identity, used to seed `FakeOperatorAuthenticationService`.

| Field | Type | Description |
|---|---|---|
| `Name` | `string` | Display name (e.g., `"Sophie Durand"`) |
| `Role` | `UserRole` | One of `Waiter`, `Cashier`, `KitchenStaff`, `FloorManager`, `Admin` |
| `KnownPin` | `string` | Plain-text PIN used in simulation input (e.g., `"1111"`) |
| `OperatorId` | `Guid` | Fixed `UuidV7` for deterministic assertions |
| `IsActive` | `bool` | Always `true` for simulation; tested as `false` in edge case |

**Static factory per role**:
```csharp
SimulatedOperator.Waiter()        // Sophie, PIN "1111"
SimulatedOperator.Cashier()       // Marc, PIN "2222"
SimulatedOperator.KitchenStaff()  // Pierre, PIN "3333"
SimulatedOperator.FloorManager()  // Isabelle, PIN "4444"
SimulatedOperator.Admin()         // Alexandre, PIN "5555"
```

---

### FakeOperatorAuthenticationService

Implements `IOperatorAuthenticationService`. Maps a pre-configured `SimulatedOperator` to the expected `OperatorAuthenticationResult`.

| Field | Type | Description |
|---|---|---|
| `_operator` | `SimulatedOperator` | The seeded operator |
| `_failedAttempts` | `int` | Tracks consecutive wrong PINs (lockout at 5) |

**State transitions**:
- Correct PIN → `IsSuccess=true`, `OperatorId`, `OperatorName`, `Role`
- Wrong PIN (< 5) → `IsSuccess=false`, `ErrorMessage="PIN invalide"`
- Wrong PIN (≥ 5) → `IsSuccess=false`, `ErrorMessage="Compte verrouillé"`
- `HashPin` and `GenerateSalt` return fixed stub strings (not used in simulation path)

---

### FakeStaffManagementService

Implements `IStaffManagementService`. In-memory `List<User>` as backing store.

| Operation | Behavior |
|---|---|
| `GetAllStaffAsync` | Returns all users in backing list |
| `GetStaffByIdAsync` | Returns matching user or `null` |
| `CreateStaffMemberAsync` | Creates `User` with hashed PIN stub, appends to list, returns it |
| `DeactivateStaffMemberAsync` | Sets `IsActive=false`, returns `true` |
| `UpdateStaffMemberAsync` | Updates matching user fields, returns it |
| `ResetStaffPinAsync` | Resets pin hash stub, returns `true` |

**Duplicate detection** (edge case): `CreateStaffMemberAsync` throws `InvalidOperationException` if a user with the same name already exists.

---

### FakeCheckoutPaymentService

Implements `ICheckoutPaymentService`.

| Operation | Behavior |
|---|---|
| `ProcessPaymentTendersAsync` | Always returns `CheckoutResult(IsSuccess=true, ...)` with a generated receipt number |
| `VoidReceiptAsync` | Returns success result |
| `CalculateEqualSplitPartitions` | Delegates to real algorithm: `long[] parts` where each part = `totalCents / n`, remainder added to last partition |

---

### FakeBackOfficeCatalogService

Implements `IBackOfficeCatalogService`. In-memory lists for categories and products.

| Operation | Behavior |
|---|---|
| `GetAllCategoriesAsync` | Returns seed categories |
| `CreateCategoryAsync` | Appends to list, returns new `Category` |
| `GetProductsByCategoryAsync` | Returns products filtered by `categoryId` |
| `CreateProductAsync` | Appends product to in-memory store, returns it |
| `ArchiveCategoryAsync` | Marks category inactive, returns `true` |
| `ArchiveProductAsync` | Marks product inactive, returns `true` |
| `UpdateCategoryAsync` / `UpdateProductAsync` | Updates matching record |

**Seed state**: 2 categories (`"Entrées"`, `"Plats"`), 2 products per category.

---

### FakePrinterConfigurationService

Implements `IPrinterConfigurationService`.

| Operation | Behavior |
|---|---|
| `GetAllPrintersAsync` | Returns empty list initially |
| `RegisterPrinterAsync` | Appends `PrinterConfiguration`, returns it |
| `SendTestPrintAsync` | Returns `TestPrintResult(Success=true, Message="OK", ResponseTime=50ms)` |
| `DeletePrinterAsync` / `UpdatePrinterAsync` | Standard in-memory mutations |

---

### FakePlatformEnvironmentService

Implements `IPlatformEnvironmentService`. No-op for all methods; haptic feedback calls are recorded for assertion.

| Field | Type | Description |
|---|---|---|
| `HapticCalls` | `List<HapticFeedbackType>` | Records all `TriggerHapticFeedback` calls |
| `GetCurrentDeviceProfile()` | Returns `DevicePlatformProfile.Desktop` stub |
| `GetSecureDatabasePath()` | Returns `"/tmp/test.db"` stub |
| `EnsureLocalNetworkPermissionsAsync()` | Returns `true` |
| `PreventScreenSleep()` | No-op |

**Note**: `FakePlatformEnvironmentService` replaces Moq for platform environment across all profile simulations to give a shared, readable assertion surface.

---

### FakeLocalJournalService

Implements `ILocalJournalService`. Required by `PosTerminalViewModel`.

| Operation | Behavior |
|---|---|
| All write operations | No-op / return success |

---

## State Transition Diagrams

### PIN Authentication Flow

```
EnterPin(correct)
  └─> IsAuthenticated=true
      CurrentOperatorRole=[seeded role]

EnterPin(wrong, attempt < 5)
  └─> IsAuthenticated=false
      ErrorMessage="PIN invalide"
      PinInput cleared

EnterPin(wrong, attempt = 5)
  └─> IsAuthenticated=false
      ErrorMessage="Compte verrouillé"
```

### KDS Ticket State Machine

```
[Pending] --BumpTicket--> [InPreparation] --BumpTicket--> [Ready] --BumpTicket--> [Served]
[InPreparation] --RecallTicket--> [Pending]
```

### Staff Creation Flow (Admin)

```
ShowCreateForm()
  └─> IsFormVisible=true

CreateStaffAsync(valid name, valid PIN)
  └─> StaffMembers += newUser
      IsFormVisible=false
      StatusMessage contains name

CreateStaffAsync(duplicate name)
  └─> ErrorMessage set
      StaffMembers unchanged
```
