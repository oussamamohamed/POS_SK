# Contract: Catalog & Staff Reference Data Sync API

**Feature**: `003-phase1-core-pin-persistence`
**Protocol**: HTTP/2 REST
**Base Path**: `/api/v1/catalog`

## 1. Catalog Sync Endpoint

### Request
`GET /api/v1/catalog/sync?since=2026-08-15T12:00:00.000Z`

**Headers**:
- `Accept: application/json`
- `X-Terminal-Id: POS01`

### Response
`200 OK`
```json
{
  "syncTimestampUtc": "2026-08-15T21:30:00.000Z",
  "categories": [
    {
      "id": "CAT-DRINKS",
      "name": "Boissons Fraîches",
      "colorHex": "#3B82F6",
      "displayOrder": 1,
      "updatedAtUtc": "2026-08-15T14:00:00.000Z"
    }
  ],
  "products": [
    {
      "id": "0191438a-3f10-7000-8000-112233445566",
      "name": "Café Espresso",
      "categoryId": "CAT-DRINKS",
      "priceCents": 250,
      "taxRatePercent": 10.00,
      "colorHex": "#3B82F6",
      "isAvailable": true,
      "updatedAtUtc": "2026-08-15T14:00:00.000Z"
    }
  ],
  "operators": [
    {
      "id": "0191438a-3f10-7001-8000-aabbccddeeff",
      "name": "Alexandre Dupont",
      "role": "Waiter",
      "pinHash": "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
      "pinSalt": "e3b0c44298fc1c149afbf4c8996fb924",
      "isActive": true,
      "updatedAtUtc": "2026-08-15T14:00:00.000Z"
    }
  ]
}
```
