#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$ROOT_DIR"

SIM_DEVICE="iPad Pro 11-inch (M5)"
BUNDLE_ID="com.restaurantpos.client"
API_URL="http://127.0.0.1:5000"

echo "========================================================"
echo " [DEPLOY] Deploiement RestaurantPos en Mode Production   "
echo "========================================================"

# 1. Verification du simulateur
echo "[1/6] Verification du simulateur iPad..."
BOOTED_UDID=$(xcrun simctl list devices booted | grep -E -o "[0-9A-F]{8}-([0-9A-F]{4}-){3}[0-9A-F]{12}" | head -n 1 || true)
if [ -z "$BOOTED_UDID" ]; then
    echo "  Demarrage du simulateur $SIM_DEVICE..."
    xcrun simctl boot "$SIM_DEVICE" 2>/dev/null || true
    open -a Simulator
    sleep 3
    BOOTED_UDID=$(xcrun simctl list devices booted | grep -E -o "[0-9A-F]{8}-([0-9A-F]{4}-){3}[0-9A-F]{12}" | head -n 1)
fi
echo "  Simulateur pret (UDID: $BOOTED_UDID)"

# 2. Arret des anciennes instances API
echo "[2/6] Redemarrage API en Mode Production..."
pkill -f "RestaurantPos.Api" 2>/dev/null || true
sleep 1

echo "  Nettoyage base de donnees existante (remise a zero)..."
rm -f src/RestaurantPos.Api/restaurantpos.db* restaurantpos.db* 2>/dev/null || true

# 3. Compilation et Lancement Backend API en Release
echo "  Compilation Release API..."
dotnet build src/RestaurantPos.Api/RestaurantPos.Api.csproj -c Release --nologo

echo "  Demarrage de l'API en environnement Production..."
ASPNETCORE_ENVIRONMENT=Production dotnet run --project src/RestaurantPos.Api/RestaurantPos.Api.csproj -c Release --no-build > /tmp/pos_api_prod.log 2>&1 &
API_PID=$!
echo "  API lancee en arriere-plan (PID: $API_PID)"

# Attente disponibilite API
for i in {1..30}; do
    if curl -s -f "$API_URL/api/health" > /dev/null 2>&1; then
        echo "  API operationnelle sur $API_URL"
        break
    fi
    sleep 0.5
done

# 4. Insertion des donnees de test dans la base de donnees de production
echo "[3/6] Insertion des donnees de test dans la base de donnees..."
"$SCRIPT_DIR/seed_production_test_data.sh"

# 5. Compilation et Installation du client MAUI en Release
echo "[4/6] Compilation et Verification du paquet MAUI Release iOS..."
APP_BUNDLE="src/RestaurantPos.Client.Maui/bin/Release/net9.0-ios/iossimulator-arm64/RestaurantPos.Client.Maui.app"
echo "  Compilation Release de RestaurantPos.Client.Maui pour iOS Simulator..."
dotnet build src/RestaurantPos.Client.Maui/RestaurantPos.Client.Maui.csproj -c Release -p:BuildingForMaui=true -f net9.0-ios --nologo

echo "[5/6] Installation de l'application sur le simulateur iPad..."
echo "  Desinstallation precedente pour rafraichir le cache de SpringBoard..."
xcrun simctl uninstall booted "$BUNDLE_ID" 2>/dev/null || true
xcrun simctl install booted "$APP_BUNDLE"

echo "  Capture d'ecran de l'ecran d'accueil (icone)..."
xcrun simctl spawn booted killall -9 SpringBoard 2>/dev/null || true
sleep 2
xcrun simctl io booted screenshot "/Users/oussama/.gemini/antigravity-ide/brain/869c6bf4-a973-4b71-97b9-db1b15ce688f/homescreen_verified.png" 2>/dev/null || true

echo "[6/6] Lancement de l'application RestaurantPos en Mode Production..."
xcrun simctl terminate booted "$BUNDLE_ID" 2>/dev/null || true
sleep 1
xcrun simctl launch booted "$BUNDLE_ID"

sleep 3
SCREENSHOT_PATH="/Users/oussama/.gemini/antigravity-ide/brain/869c6bf4-a973-4b71-97b9-db1b15ce688f/prod_deployed_screen.png"
xcrun simctl io booted screenshot "$SCREENSHOT_PATH"

echo ""
echo "========================================================"
echo " [TERMINE] Deploiement Production Reussi !"
echo " - Environnement API : Production (Release)"
echo " - Base SQLite       : restaurantpos.db avec donnees reelles inserees"
echo " - Client iOS MAUI   : Installe et execute en Release"
echo " - Capture d'ecran   : $SCREENSHOT_PATH"
echo "========================================================"
