# Contract: Operator PIN Authentication & Security Services

**Feature**: `003-phase1-core-pin-persistence`
**Domain**: Staff Authentication & Access Control

## 1. Authentication Service Interface

```csharp
namespace RestaurantPos.Application.Common.Interfaces;

public interface IOperatorAuthenticationService
{
    ValueTask<OperatorAuthenticationResult> AuthenticatePinAsync(
        string rawPin, 
        CancellationToken cancellationToken = default);

    string HashPin(string rawPin, string salt);
    
    string GenerateSalt();
}

public enum UserRole
{
    Waiter = 0,
    Cashier = 1,
    KitchenStaff = 2,
    FloorManager = 3,
    Admin = 4
}

public record OperatorAuthenticationResult(
    bool IsSuccess,
    Guid? OperatorId,
    string? OperatorName,
    UserRole? Role,
    string? ErrorMessage);
```

---

## 2. Hashing Algorithm Specification

$$\text{PinHash} = \text{Hex}(\text{SHA256}(\text{UTF8}(\text{Salt} + \text{RawPin})))$$

- `Salt`: 32 hex characters generated from 16 cryptographically secure random bytes (`RandomNumberGenerator.GetBytes(16)`).
- `RawPin`: 4 to 6 numeric characters (`0-9`).
