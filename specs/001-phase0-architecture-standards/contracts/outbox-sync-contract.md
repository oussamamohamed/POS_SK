# Contract: Outbox Synchronization & Conflict Resolution Protocol

**Feature**: `001-phase0-architecture-standards`
**Protocol**: HTTP/2 REST + SignalR WebSocket Channel
**Base Path**: `/api/v1/sync`

## 1. Batch Push Sync Endpoint

### Request
`POST /api/v1/sync/batch`

**Headers**:
- `Content-Type: application/json`
- `X-Terminal-Id: POS01`
- `X-Terminal-Token: <bearer-or-hmac-token>`

**Payload**:
```json
{
  "terminalId": "POS01",
  "batchId": "0191438a-3f10-7000-8000-112233445566",
  "sentAtUtc": "2026-08-15T20:45:00.000Z",
  "messages": [
    {
      "messageId": "0191438a-3f10-7001-8000-aabbccddeeff",
      "idempotencyKey": "POS01-ORD-20260815-00124",
      "eventType": "OrderCreated",
      "occurredAtUtc": "2026-08-15T20:44:30.000Z",
      "payload": {
        "orderId": "0191438a-3f10-7002-8000-1234567890ab",
        "tableNumber": "T04",
        "operatorId": "0191438a-3f10-7003-8000-9876543210fe",
        "items": [
          {
            "itemId": "0191438a-3f10-7004-8000-abcdef123456",
            "productId": "0191438a-3f10-7005-8000-fedcba654321",
            "quantity": 2,
            "unitPriceCents": 1450,
            "taxRatePercent": 10.00,
            "modifiers": ["Cuisson: A point", "Sauce: Poivre"]
          }
        ]
      }
    }
  ]
}
```

### Response
`200 OK`
```json
{
  "batchId": "0191438a-3f10-7000-8000-112233445566",
  "processedCount": 1,
  "results": [
    {
      "messageId": "0191438a-3f10-7001-8000-aabbccddeeff",
      "idempotencyKey": "POS01-ORD-20260815-00124",
      "status": "Accepted",
      "hasConflict": false,
      "conflictDetails": null
    }
  ]
}
```

### Conflict Handling Response (Concurrent Modification on Same Table)
`200 OK` (with additive merge confirmation and alert trigger):
```json
{
  "batchId": "0191438a-3f10-7000-8000-112233445566",
  "processedCount": 1,
  "results": [
    {
      "messageId": "0191438a-3f10-7001-8000-aabbccddeeff",
      "idempotencyKey": "POS01-ORD-20260815-00124",
      "status": "MergedWithAlert",
      "hasConflict": true,
      "conflictDetails": {
        "tableNumber": "T04",
        "alertMessage": "Table T04 was concurrently updated offline by POS02. Items have been merged additively.",
        "mergedAtUtc": "2026-08-15T20:45:01.000Z"
      }
    }
  ]
}
```

---

## 2. SignalR TableHub Real-Time Synchronization Channel

**Hub URL**: `/hubs/tables`

### Client-to-Server Methods
- `JoinDiningRoom(string roomId)`
- `BroadcastTableState(TableStateUpdateDto update)`

### Server-to-Client Events
- `TableStateChanged(TableStateEventDto event)`: Real-time update of occupied/free/bill requested status.
- `ConcurrentModificationAlert(ConflictAlertDto alert)`: On-screen banner alert trigger for staff when concurrent offline edits are synchronized.
