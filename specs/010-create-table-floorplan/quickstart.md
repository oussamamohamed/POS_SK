# Quickstart Validation: Création Directe de Table depuis la Vue Tables

**Feature**: `010-create-table-floorplan`  
**Date**: 2026-08-30

---

## 1. Prérequis

- Le serveur `RestaurantPos.Api` est démarré sur `http://localhost:5000`.
- La base de données SQLite est initialisée avec les tables par défaut (`T1` à `T8`).

---

## 2. Scénarios de Validation de Bout en Bout

### Scénario 1 : Création d'une table via l'API REST
```powershell
$tablePayload = @{
    tableNumber = "T10"
    capacity = 6
} | ConvertTo-Json

$created = Invoke-RestMethod -Uri "http://localhost:5000/api/tables" -Method Post -Body $tablePayload -ContentType "application/json"
Write-Host "Table créée: $($created.tableNumber) (Capacité: $($created.capacity), Statut: $($created.status))"
```

---

### Scénario 2 : Création d'une table sur l'interface Web
1. Ouvrir `http://localhost:5000` dans le navigateur.
2. Cliquer sur l'onglet **🗺️ Plan de Salle**.
3. Cliquer sur le bouton **➕ Nouvelle Table**.
4. Saisir le numéro `T12` et sélectionner le bouton rapide `[6p]`.
5. Cliquer sur **Créer la Table**.
6. **Vérification** :
   - La modale se ferme.
   - Un toast vert s'affiche : `"Table T12 (6 places) créée avec succès ! 🎉"`.
   - La carte `T12` apparaît immédiatement dans la grille avec le statut vert `"Libre"`.
7. Cliquer sur la carte `T12` $\to$ La commande de la table s'ouvre avec 6 couverts par défaut.
