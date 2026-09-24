# Fake Service Contracts

**Feature**: `015-ui-profile-simulation-tests`

This document defines the behavioural contracts of each fake service (test double) used in the profile simulations. These contracts are stable across all simulation tests and must not be changed without updating the corresponding simulation test class.

---

## FakeOperatorAuthenticationService

**Implements**: `RestaurantPos.Application.Common.Interfaces.IOperatorAuthenticationService`
**Namespace**: `RestaurantPos.Client.Maui.ProfileSimulations.Fakes`

### Contract

```
Constructor: FakeOperatorAuthenticationService(SimulatedOperator op)

AuthenticatePinAsync(rawPin):
  IF rawPin == op.KnownPin AND _failedAttempts < 5
    RETURN OperatorAuthenticationResult(IsSuccess=true, op.OperatorId, op.Name, op.Role, null)
    RESET _failedAttempts = 0
  ELSE IF _failedAttempts >= 5
    RETURN OperatorAuthenticationResult(IsSuccess=false, null, null, null, "Compte verrouillé")
  ELSE
    _failedAttempts++
    RETURN OperatorAuthenticationResult(IsSuccess=false, null, null, null, "Code PIN invalide")

HashPin(rawPin, salt) : RETURN "FAKE_HASH"
GenerateSalt()        : RETURN "FAKE_SALT"
```

---

## FakeStaffManagementService

**Implements**: `RestaurantPos.Application.Common.Interfaces.IStaffManagementService`

### Contract

```
Constructor: FakeStaffManagementService(IEnumerable<User>? seed = null)
  _store = seed?.ToList() ?? []

GetAllStaffAsync(includeInactive):
  RETURN _store WHERE includeInactive=true → all
                      includeInactive=false → only IsActive=true

GetStaffByIdAsync(staffId):
  RETURN _store.FirstOrDefault(u => u.Id == staffId)

CreateStaffMemberAsync(name, role, pin):
  IF _store.Any(u => u.Name == name)
    THROW InvalidOperationException("Duplicate name")
  user = new User { Name=name, Role=role, PinHash="FAKE", PinSalt="FAKE" }
  _store.Add(user)
  RETURN user

DeactivateStaffMemberAsync(staffId):
  user = _store.First(u => u.Id == staffId)
  user.IsActive = false
  RETURN true

UpdateStaffMemberAsync / ResetStaffPinAsync: standard mutations
```

---

## FakeCheckoutPaymentService

**Implements**: `RestaurantPos.Application.Common.Interfaces.ICheckoutPaymentService`

### Contract

```
ProcessPaymentTendersAsync(orderId, terminalId, tenders):
  totalPaid = tenders.Sum(t => t.AmountInCents)
  RETURN CheckoutResult(
    IsSuccess=true,
    TotalPaidCents=totalPaid,
    ChangeGivenCents=0,
    RemainingBalanceCents=0,
    ReceiptNumber=$"{terminalId}-SIM-{orderId:N[..8]}",
    FiscalSignature="SIM-SIG"
  )

VoidReceiptAsync: RETURN CheckoutResult(IsSuccess=true, ...)

CalculateEqualSplitPartitions(totalCents, n):
  base = totalCents / n
  remainder = totalCents % n
  parts = [base] * n
  parts[n-1] += remainder
  RETURN parts
```

---

## FakeBackOfficeCatalogService

**Implements**: `RestaurantPos.Application.Common.Interfaces.IBackOfficeCatalogService`

### Contract (seed state)

```
Seeded categories: [
  Category { Id="CAT-001", Name="Entrées", IsActive=true },
  Category { Id="CAT-002", Name="Plats",  IsActive=true }
]

Seeded products: [
  Product { Id=Guid1, Name="Salade César", CategoryId="CAT-001", Price=9.50 },
  Product { Id=Guid2, Name="Soupe du Jour", CategoryId="CAT-001", Price=7.00 },
  Product { Id=Guid3, Name="Burger Rossini", CategoryId="CAT-002", Price=19.50 },
  Product { Id=Guid4, Name="Steak Frites", CategoryId="CAT-002", Price=22.00 }
]
```

All CRUD operations mutate the in-memory list and return the affected object.

---

## FakePrinterConfigurationService

**Implements**: `RestaurantPos.Application.Common.Interfaces.IPrinterConfigurationService`

### Contract

```
GetAllPrintersAsync(): RETURN _printers (initially empty)

RegisterPrinterAsync(request):
  printer = new PrinterConfiguration { Name=request.Name, IpAddress=request.IpAddress, ... }
  _printers.Add(printer)
  RETURN printer

SendTestPrintAsync(printerId):
  RETURN TestPrintResult(Success=true, Message="Test OK (Simulation)", ResponseTime=TimeSpan.FromMilliseconds(50))
```

---

## FakePlatformEnvironmentService

**Implements**: `RestaurantPos.Client.Maui.Contracts.IPlatformEnvironmentService`

### Contract

```
HapticCalls: List<HapticFeedbackType> = []

TriggerHapticFeedback(type): HapticCalls.Add(type)
GetCurrentDeviceProfile(): RETURN DevicePlatformProfile.Desktop
GetSecureDatabasePath(name): RETURN $"/tmp/{name}.db"
EnsureLocalNetworkPermissionsAsync(): RETURN Task.FromResult(true)
PreventScreenSleep(keepAwake): no-op
```

---

## FakeLocalJournalService

**Implements**: `RestaurantPos.Client.Maui.Contracts.ILocalJournalService`

### Contract

All operations are no-ops that return default/success values. No data is persisted.
