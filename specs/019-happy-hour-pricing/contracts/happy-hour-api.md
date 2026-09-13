# API Contracts: Happy Hour Pricing & Management

**Feature**: [`specs/019-happy-hour-pricing/spec.md`](file:///Users/oussama/Library/CloudStorage/OneDrive-Personnel/Documents/Visual%20Studio%202022/POS_SK_Antigavity/specs/019-happy-hour-pricing/spec.md)  
**Phase**: Phase 1 — Design & Contracts  
**Status**: Completed  

---

## 1. REST Endpoints Overview

All endpoints mapped under `/api/happy-hour` with tags `"Happy Hour Pricing"`.

| Method | Route | Auth | Description |
|---|---|---|---|
| `GET` | `/api/happy-hour/status` | Anonymous / Bearer | Check if Happy Hour is currently active on a terminal |
| `GET` | `/api/happy-hour/pricing-table` | Anonymous / Bearer | Returns the active lookup table of discounted prices |
| `POST` | `/api/happy-hour/override/activate` | Bearer (Supervisor PIN) | Manually activate/extend Happy Hour for N minutes |
| `POST` | `/api/happy-hour/override/stop` | Bearer (Supervisor PIN) | Manually cancel/stop active override |
| `GET` | `/api/happy-hour/schedules` | Bearer (Manager/Admin) | List all configured schedules and rules |
| `POST` | `/api/happy-hour/schedules` | Bearer (Admin) | Create or update a schedule with rules |
| `DELETE` | `/api/happy-hour/schedules/{id}` | Bearer (Admin) | Delete a schedule |

---

## 2. Request / Response Contracts

### 2.1 `GET /api/happy-hour/status`

**Query Parameters**:
- `terminalId`: string (optional, defaults to `"POS_MAIN"`)

**Response 200 OK**:
```json
{
  "isActive": true,
  "isOverride": false,
  "activeScheduleName": "Afterwork Standard",
  "activeScheduleId": "0191eb58-6c84-7a91-9e12-38d58c14a24f",
  "appliesToTakeaway": false,
  "currentWindow": {
    "startTime": "17:00:00",
    "endTime": "20:00:00",
    "remainingMinutes": 45
  },
  "overrideDetails": null
}
```

---

### 2.2 `GET /api/happy-hour/pricing-table`

**Response 200 OK**:
```json
{
  "isActive": true,
  "generatedAtUtc": "2026-09-13T18:15:00Z",
  "items": [
    {
      "productId": "0191eb44-1234-7a91-9e12-38d58c14a111",
      "productName": "Pinte Blonde Artisanale",
      "standardPrice": 7.50,
      "happyHourPrice": 5.00,
      "discountAmount": 2.50,
      "ruleType": "FixedPrice"
    },
    {
      "productId": "0191eb44-5678-7a91-9e12-38d58c14a222",
      "productName": "Cocktail Mojito",
      "standardPrice": 9.00,
      "happyHourPrice": 7.20,
      "discountAmount": 1.80,
      "ruleType": "CategoryDiscount"
    }
  ]
}
```

---

### 2.3 `POST /api/happy-hour/override/activate`

**Request Body**:
```json
{
  "terminalId": "POS_A",
  "supervisorPin": "9999",
  "durationMinutes": 60,
  "reason": "Prolongation diffusion match football"
}
```

**Response 200 OK**:
```json
{
  "success": true,
  "overrideSessionId": "0191eb58-8888-7a91-9e12-38d58c14b999",
  "authorizedBy": "Marc (Directeur)",
  "startsAtUtc": "2026-09-13T20:00:00Z",
  "expiresAtUtc": "2026-09-13T21:00:00Z",
  "message": "Happy Hour prolongé de 60 minutes avec succès."
}
```

**Response 403 Forbidden**:
```json
{
  "message": "Autorisation insuffisante : code PIN superviseur ou gérant requis."
}
```

---

### 2.4 `POST /api/happy-hour/override/stop`

**Request Body**:
```json
{
  "terminalId": "POS_A",
  "supervisorPin": "9999",
  "reason": "Arrêt anticipé"
}
```

**Response 200 OK**:
```json
{
  "success": true,
  "message": "Dérogation Happy Hour désactivée."
}
```

---

## 3. Real-Time SignalR Events (`PosHub`)

Broadcast via `IHubContext<PosHub>` to all clients:

### `HappyHourStatusChanged`

```json
{
  "isActive": true,
  "scheduleName": "Afterwork Standard",
  "isOverride": true,
  "expiresAtUtc": "2026-09-13T21:00:00Z",
  "remainingMinutes": 60
}
```
