# Implementation Plan: Backend API Authentication & Multi-Role Authorization

**Branch**: `014-backend-api-auth` | **Date**: 2026-09-03 | **Spec**: [specs/014-backend-api-auth/spec.md](spec.md)

**Input**: Feature specification from `specs/014-backend-api-auth/spec.md`

## Summary

Deliver a robust, compliant API authentication and role-based authorization (RBAC) layer for the centralized ASP.NET Core backend. This includes fast JWT token issuance from operator PINs, strict endpoint authorization separating server actions from manager/admin operations, SignalR real-time hub connection security, non-blocking test authentication for `WebApplicationFactory` and CI/CD, and safe development-mode defaults.

## Technical Context

**Language/Version**: C# 13 / .NET 9.0 (executing with .NET 10 RollForward runtime)

**Primary Dependencies**:
- Web Framework: ASP.NET Core Minimal APIs / Endpoint Groups
- Authentication & JWT: `Microsoft.AspNetCore.Authentication.JwtBearer`, `System.IdentityModel.Tokens.Jwt`
- Real-Time: `Microsoft.AspNetCore.SignalR`
- Testing: `xUnit`, `FluentAssertions`, `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory`)
- Persistence: EF Core (`AppDbContext` with SQLite / PostgreSQL)

**Storage**:
- Users & Operator Credential Hashes: `Users` table in `AppDbContext`
- In-memory token validation with HMAC-SHA256 signature checking

**Testing**:
- `tests/RestaurantPos.Api.Tests` (Integration tests, WebApplicationFactory)
- `tests/RestaurantPos.Infrastructure.Tests` (Service unit tests)
- `tests/RestaurantPos.Domain.Tests` (Domain entity verification)

**Target Platform**: Linux / macOS / Windows hosting centralized ASP.NET Core backend serving iPad and desktop POS clients.

**Performance Goals**:
- Token validation latency: $< 1\text{ms}$ per request
- PIN verification & token generation: $< 50\text{ms}$
- Test suite execution: 100% test pass rate in $< 5\text{s}$

**Constraints**:
- Must not break or block automated tests (`WebApplicationFactory`)
- Zero compiler warnings (`TreatWarningsAsErrors=true`)
- Strict separation between public read queries and protected mutating operations

## Constitution Check

*GATE: Evaluated and Passed across all Core Principles.*

| Principle | Status | Compliance Verification |
| :--- | :--- | :--- |
| **I. Touch-First Ergonomics & Tactile Design** | **PASS** | Authentication utilizes fast numeric PINs without physical keyboard requirements; tactile feedback on lock/unlock. |
| **II. Clean Architecture & Centralized Multi-POS Backend** | **PASS** | Auth contracts in `Application`, crypto/token generator in `Infrastructure`, endpoints and policies in `Api`. |
| **III. Transactional Integrity and Offline-First Operations** | **PASS** | Offline terminals buffer transactions with local operator hashes; online API verifies centralized bearer tokens with UUIDv7 traceability. |
| **IV. Hardware Driver Abstraction & Real-Time Sync** | **PASS** | SignalR `/hubs/pos` secured via token query parameter during WebSocket negotiation. |
| **V. Test-First, Immutability & Fiscal Traceability** | **PASS** | Operator identity embedded into every signed NF525 fiscal receipt; automated testing strategy guarantees non-regression. |

## Project Structure

### Documentation (this feature)

```text
specs/014-backend-api-auth/
├── spec.md              # Feature specification
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Architecture research & testing isolation decisions
├── data-model.md        # Entities, claims schema, and DTO contracts
├── quickstart.md        # Runnable verification guide
├── contracts/           # API interface contracts
│   └── auth-api.yaml
└── checklists/
    └── requirements.md  # Specification quality review checklist
```

### Source Code (repository root)

```text
src/
├── RestaurantPos.Domain/
│   └── Entities/
│       └── User.cs                       # User entity with UserRole enum
├── RestaurantPos.Application/
│   ├── Common/Interfaces/
│   │   └── IOperatorAuthenticationService.cs # Auth contract
│   └── DTOs/
│       ├── PinLoginRequest.cs
│       └── AuthLoginResponse.cs
├── RestaurantPos.Infrastructure/
│   └── Security/
│       ├── JwtTokenGeneratorService.cs   # JWT token generation
│       └── OperatorAuthenticationService.cs # PIN validation against PBKDF2/SHA256
└── RestaurantPos.Api/
    ├── Endpoints/
    │   ├── AuthEndpoints.cs              # POST /api/auth/login, /api/auth/me
    │   ├── CatalogEndpoints.cs           # RBAC policies on catalog mutations
    │   ├── TableEndpoints.cs             # Table operations authorization
    │   ├── CheckoutEndpoints.cs          # Payment & Void authorization
    │   ├── FiscalEndpoints.cs            # Z-report Admin/Manager authorization
    │   ├── StaffEndpoints.cs             # Staff admin authorization
    │   └── KitchenEndpoints.cs           # KDS bump authorization
    ├── Hubs/
    │   └── PosHub.cs                     # SignalR real-time security
    └── Program.cs                        # Authentication & Authorization pipeline configuration

tests/
└── RestaurantPos.Api.Tests/
    ├── PosApiApplicationFactory.cs      # Test authentication handler configuration
    └── AuthEndpointsTests.cs            # E2E authentication & RBAC integration tests
```

---

## Execution Milestones

### Milestone 1: Authorization Policy Configuration & Endpoint Gating
- Configure explicit named authorization policies in `Program.cs`:
  - `RequireAuthenticatedOperator`
  - `RequireManagerOrAdmin`
  - `RequireKitchenOrAdmin`
- Apply policies across endpoint groups:
  - Public read endpoints keep `.AllowAnonymous()`
  - Mutating operations require valid operator claims
  - Sensitive operations (`void`, `daily-z`, `staff`) require `RequireManagerOrAdmin`

### Milestone 2: Testing Authentication Strategy (Non-Blocking)
- Implement `TestAuthHandler` in `RestaurantPos.Api.Tests` to synthesize test claims when `X-Test-Role` is passed or default test user is requested.
- Ensure integration tests in `RestaurantPos.Api.Tests` execute seamlessly with role assertions.
- Verify all existing test projects (`RestaurantPos.Domain.Tests`, `RestaurantPos.Client.Maui.Tests`, `RestaurantPos.Infrastructure.Tests`) pass with zero regressions.

### Milestone 3: Real-Time SignalR Security & Client Interceptor
- Ensure `JwtBearerEvents.OnMessageReceived` retrieves tokens from `access_token` query string for `/hubs/pos`.
- Verify browser web client and MAUI client automatically attach JWT bearer tokens on all mutating requests.
