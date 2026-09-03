# Quickstart Validation Guide: Backend API Authentication & RBAC

**Feature**: Backend API Authentication & Multi-Role Authorization  
**Directory**: `specs/014-backend-api-auth`  
**Date**: 2026-09-03  

## Overview
This guide provides executable validation steps to verify that backend API authentication, RBAC authorization, and automated testing support operate correctly end-to-end.

---

## 1. Prerequisites
- The backend API is running locally:
  ```bash
  dotnet exec --roll-forward Major src/RestaurantPos.Api/bin/Debug/net9.0/RestaurantPos.Api.dll
  ```
  *(API listens on `http://localhost:5000`)*

---

## 2. Validation Scenarios

### Scenario 1: Operator PIN Authentication
1. Authenticate with the pre-seeded Manager PIN (`1234`):
   ```bash
   curl -i -X POST http://localhost:5000/api/auth/login \
     -H "Content-Type: application/json" \
     -d '{"pin":"1234"}'
   ```
2. **Expected Outcome**:
   - HTTP Status `200 OK`
   - Response contains `"success": true`, `"role": "FloorManager"`, and a signed `"token"`.

### Scenario 2: Unauthenticated Access Denied on Protected Operations
1. Attempt to open a dining table without an Authorization header:
   ```bash
   curl -i -X POST http://localhost:5000/api/tables/T1/open \
     -H "Content-Type: application/json" \
     -d '{"coversCount": 2}'
   ```
2. **Expected Outcome**:
   - HTTP Status `401 Unauthorized`
   - Header `WWW-Authenticate: Bearer` present.

### Scenario 3: Authenticated Table Operation
1. Extract the token from Scenario 1 and send with the request:
   ```bash
   TOKEN=$(curl -s -X POST http://localhost:5000/api/auth/login \
     -H "Content-Type: application/json" \
     -d '{"pin":"1234"}' | grep -o '"token":"[^"]*' | cut -d'"' -f4)

   curl -i -X POST http://localhost:5000/api/tables/T1/open \
     -H "Content-Type: application/json" \
     -H "Authorization: Bearer $TOKEN" \
     -d '{"coversCount": 2, "waiterName": "Alexandre"}'
   ```
2. **Expected Outcome**:
   - HTTP Status `200 OK`
   - Table `T1` status transitioned to occupied.

### Scenario 4: Role-Based Access Control (RBAC) Enforcement
1. Login as a Waiter (`PIN: 2468`):
   ```bash
   WAITER_TOKEN=$(curl -s -X POST http://localhost:5000/api/auth/login \
     -H "Content-Type: application/json" \
     -d '{"pin":"2468"}' | grep -o '"token":"[^"]*' | cut -d'"' -f4)
   ```
2. Attempt to trigger a receipt void (requires `FloorManager` or `Admin`):
   ```bash
   curl -i -X POST http://localhost:5000/api/checkout/void/00000000-0000-0000-0000-000000000000 \
     -H "Authorization: Bearer $WAITER_TOKEN"
   ```
3. **Expected Outcome**:
   - HTTP Status `403 Forbidden`

### Scenario 5: Automated Testing Non-Regression
1. Run all automated tests in the repository:
   ```bash
   dotnet test
   ```
2. **Expected Outcome**:
   - 100% of test suites succeed with 0 failures across Domain, Application, Client, Infrastructure, and API tests.
