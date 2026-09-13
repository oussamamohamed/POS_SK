#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$ROOT_DIR"

API_URL="http://127.0.0.1:5000"

echo "========================================================"
echo " [SEED] Insertion des donnees de test en Production POS "
echo "========================================================"

# Verification de l'API
if curl -s -f "$API_URL/api/health" > /dev/null 2>&1; then
    echo "[1/2] Serveur API en ligne ($API_URL) -> Appel endpoint de seed..."
    RESPONSE=$(curl -s -X POST "$API_URL/api/admin/seed-test-data" -H "Content-Type: application/json")
    echo "Reponse API: $RESPONSE"
else
    echo "[1/2] Serveur API non detecte sur le port 5000 -> Execution directe CLI .NET..."
    dotnet run --project src/RestaurantPos.Api/RestaurantPos.Api.csproj -c Release --no-launch-profile -- --seed-prod-data
fi

echo ""
echo "========================================================"
echo " [SUCCES] Donnees de test de production inserees :"
echo " ------------------------------------------------------"
echo "  Operateurs & Codes PIN :"
echo "    - Alexandre Dupont (FloorManager) : PIN 1234"
echo "    - Sophie Martin (Serveuse)        : PIN 2468"
echo "    - Thomas Bernard (Chef Cuisine)   : PIN 5678"
echo "    - Administrateur Systeme (Admin)   : PIN 9999"
echo ""
echo "  Catalogue :"
echo "    - 5 Categories (Entrees, Plats, Pizzas, Desserts, Boissons)"
echo "    - 11 Produits avec TVA (10% et 20%) et postes de cuisine"
echo "    - Groupes modificateurs (Cuissons viandes, Supplements burgers & pizzas)"
echo ""
echo "  Plan de Table :"
echo "    - Tables T1 a T8 configurees"
echo "    - Table T2 : Occupee (Sophie Martin) avec commande active (Salade Cesar x2 + Burger)"
echo "    - Table T6 : Occupee (Sophie Martin) avec commande active (Pizza + Bieres IPA)"
echo "    - Table T3 : Note demandee (Alexandre Dupont)"
echo "    - Table T8 : Payee"
echo ""
echo "  Peripheriques & Hotellerie :"
echo "    - 3 Imprimantes reseau (Caisse 9100, Cuisine 9100, Bar 9100)"
echo "    - 3 Chambres PMS Hotel (101 Jean Dujardin, 204 Alexandre Dupont, 305 Sophie Marceau)"
echo "    - Happy Hour 'Afterwork' (17h-20h) avec tarifs reduits"
echo "========================================================"
