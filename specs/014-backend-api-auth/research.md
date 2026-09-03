# Phase 0: Technical Research & Architecture Decisions

**Feature**: Backend API Authentication & Multi-Role Authorization  
**Directory**: `specs/014-backend-api-auth`  
**Date**: 2026-09-03  

## 1. Authentication Scheme & Token Mechanics

### Decision
Use industry-standard **JWT Bearer Authentication** (`Microsoft.AspNetCore.Authentication.JwtBearer`) with symmetric HMAC-SHA256 signing (`SecurityAlgorithms.HmacSha256`), paired with a deterministic **Test Authentication Scheme** for automated testing and CI/CD.

### Rationale
- Fast local verification with zero database lookup per request (sub-1ms token validation latency, easily meeting the $< 50\text{ms}$ tactile budget).
- Compact payload containing key operator claims (`sub` = OperatorId, `name` = OperatorName, `role` = UserRole, `jti` = unique token ID).
- Native support across modern clients (.NET MAUI `HttpClient`, browser `fetch`, and SignalR WebSocket `access_token` query parameter).

### Alternatives Considered
- **Stateful Cookie Sessions**: Rejected because REST APIs and mobile tablet clients (iOS/Android) prefer stateless tokenized authorization headers, and cookies complicate cross-origin / mobile tablet calls.
- **OAuth2 / OIDC External Provider**: Rejected for the restaurant local-first topology; local POS servers must operate autonomously during broadband internet outages without external identity provider dependencies.

---

## 2. Testing & Development Environment Isolation (Non-Blocking Strategy)

### Decision
Implement a multi-tier authentication pipeline:
1. **Testing Environment (`ASPNETCORE_ENVIRONMENT=Testing`)**:
   - Register a lightweight `TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>` when running under automated integration tests (`WebApplicationFactory`).
   - If an `Authorization: Bearer <token>` header is present, validate it normally.
   - If a header `X-Test-Role: Admin` (or `FloorManager`, `Waiter`) is passed by integration test clients, synthesize a valid `ClaimsPrincipal` with the requested role.
   - If no credentials are provided on public endpoints, allow access. If no credentials on protected endpoints, return 401 as expected.
   - This ensures **100% of existing tests** (`RestaurantPos.Api.Tests`, `RestaurantPos.Infrastructure.Tests`, etc.) execute without regression and without requiring mock database login fixtures.
2. **Development Environment (`ASPNETCORE_ENVIRONMENT=Development`)**:
   - Seed standard test accounts (`1234` Manager, `2468` Waiter, `5678` Chef, `9999` Admin).
   - In Swagger / browser, generate ready-to-copy dev tokens or use the touch PIN login screen.
3. **Production Environment (`ASPNETCORE_ENVIRONMENT=Production`)**:
   - Test handlers and bypasses are strictly not registered (`#if DEBUG` or `!env.IsProduction()`). Any attempt to pass `X-Test-Role` is ignored.

### Rationale
Completely addresses the user requirement: *"il faut gerer aussi l'environement de dev pour ne pas bloquer les testes"*. Tests can test RBAC boundaries precisely by asserting 401/403 responses when no credentials or wrong roles are supplied, while happy-path integration tests remain concise and robust.

---

## 3. Authorization Policies & Role Hierarchy

### Decision
Define explicit ASP.NET Core Authorization Policies:
- **`RequireAuthenticatedOperator`**: Requires any valid authenticated session (`Waiter`, `FloorManager`, `KitchenStaff`, `Admin`).
- **`RequireManagerOrAdmin`**: Requires `role` in (`FloorManager`, `Admin`). Applied to receipt voids (`/api/checkout/void/*`), staff management (`/api/staff/*`), catalog mutations (`POST /api/catalog/*`), and daily fiscal closures (`/api/fiscal/closure/daily-z`).
- **`RequireKitchenOrAdmin`**: Requires `role` in (`KitchenStaff`, `FloorManager`, `Admin`). Applied to KDS bump ticket actions.
- **`AllowAnonymous`**: Applied to read-only passive queries (`GET /api/catalog/categories`, `GET /api/catalog/products`, `GET /api/tables`, `GET /api/grid-layouts/*`, `GET /api/health`).

---

## 4. SignalR Hub Security

### Decision
Configure SignalR authentication via standard JWT Bearer token passing:
- For WebSockets, browsers and MAUI clients pass the token in `context.Token = context.Request.Query["access_token"]` inside `JwtBearerEvents.OnMessageReceived`.
- In `Development` / `Testing`, if no token is provided, allow the client to connect with a guest identity so that table floor plan displays on passive screens continue to operate.
