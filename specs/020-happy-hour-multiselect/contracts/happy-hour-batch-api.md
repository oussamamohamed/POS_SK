# API Contract: Happy Hour Batch Operations

## Endpoints

### 1. `POST /api/happy-hour/schedules/{scheduleId}/rules/batch`
Créer ou mettre à jour un lot de règles tarifaires pour un planning Happy Hour.

#### Request Headers
- `Content-Type: application/json`
- `Authorization: Bearer <token>` (Rôle `FloorManager` ou `Admin`)

#### Request Body
```json
{
  "targetType": 0,
  "targetIds": [
    "0191c49b-7324-7801-9a74-d4b8f05e3250",
    "0191c49b-7324-7801-9a74-d4b8f05e3251",
    "0191c49b-7324-7801-9a74-d4b8f05e3252"
  ],
  "pricingMode": 0,
  "fixedPrice": 5.00,
  "discountPercent": null
}
```
*Ou pour des familles (catégories) :*
```json
{
  "targetType": 1,
  "targetIds": [
    "CAT-DRINKS",
    "CAT-BAR"
  ],
  "pricingMode": 1,
  "fixedPrice": null,
  "discountPercent": 20.0
}
```

#### Responses
- **200 OK**
  ```json
  {
    "success": true,
    "appliedCount": 3,
    "scheduleId": "0191c49b-7324-7801-9a74-d4b8f05e0001",
    "message": "3 règles appliquées avec succès au planning."
  }
  ```
- **400 Bad Request**
  ```json
  {
    "message": "TargetIds ne peut pas être vide et le prix/remise doit être valide."
  }
  ```
- **404 Not Found**
  ```json
  {
    "message": "Planning Happy Hour introuvable."
  }
  ```

---

### 2. `DELETE /api/happy-hour/schedules/{scheduleId}/rules/batch`
Supprimer un ensemble de règles de prix d'un planning.

#### Request Headers
- `Content-Type: application/json`
- `Authorization: Bearer <token>`

#### Request Body
```json
{
  "ruleIds": [
    "0191c49b-7324-7801-9a74-d4b8f05e3299",
    "0191c49b-7324-7801-9a74-d4b8f05e3300"
  ]
}
```

#### Responses
- **200 OK**
  ```json
  {
    "success": true,
    "deletedCount": 2,
    "scheduleId": "0191c49b-7324-7801-9a74-d4b8f05e0001",
    "message": "2 règles supprimées avec succès."
  }
  ```
