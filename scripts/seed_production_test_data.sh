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
    echo "[1/2] Serveur API en ligne ($API_URL) -> Authentification Admin et appel endpoint de seed..."
    TOKEN=$(curl -s -X POST "$API_URL/api/auth/login" -H "Content-Type: application/json" -d '{"pin":"9999"}' | grep -o '"token":"[^"]*' | cut -d'"' -f4)
    if [ -n "$TOKEN" ]; then
        RESPONSE=$(curl -s -w '\n%{http_code}' -X POST "$API_URL/api/admin/seed-test-data" -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json")
        HTTP_CODE=${RESPONSE##*$'\n'}
        echo "Reponse API: ${RESPONSE%$'\n'*}"
        if [ "$HTTP_CODE" != "200" ]; then
            echo "ECHEC : seed-test-data a repondu HTTP $HTTP_CODE (route montee en Development/Testing uniquement)." >&2
            exit 1
        fi
    else
        echo "ECHEC : authentification Admin refusee. Le seed de test n'est utilisable qu'en Development : ne l'utilisez pas en production (il recreerait des comptes aux PIN connus)." >&2
        exit 1
    fi
else
    if [ "${POS_ALLOW_DEMO_SEED:-}" != "1" ]; then
        echo "ECHEC : serveur API non detecte sur $API_URL. Le seed CLI recree les PIN de demonstration (1234/2468/5678/9999) : jamais en production. Developpement uniquement : POS_ALLOW_DEMO_SEED=1." >&2
        exit 1
    fi
    echo "ATTENTION : seed CLI en Development, recree les PIN de demonstration sur la base locale par defaut." >&2
    ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/RestaurantPos.Api/RestaurantPos.Api.csproj -c Release --no-launch-profile -- --seed-prod-data
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
echo "    - Tables T1 a T8 configurees (Toutes Libres, 0 commande active, pret pour audit rapports)"
echo ""
echo "  Peripheriques & Hotellerie :"
echo "    - 3 Imprimantes reseau (Caisse 9100, Cuisine 9100, Bar 9100)"
echo "    - 3 Chambres PMS Hotel (101 Jean Dujardin, 204 Alexandre Dupont, 305 Sophie Marceau)"
echo "    - Happy Hour 'Afterwork' (17h-20h) avec tarifs reduits"
echo "========================================================"
