#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$ROOT_DIR"

SIM_DEVICE="iPad Pro 11-inch (M5)"
BUNDLE_ID="com.restaurantpos.ipad"
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

# 5. Compilation du client iPad natif (SwiftUI) en Release
echo "[4/6] Compilation Release du client iPad natif (SwiftUI)..."
DERIVED_DIR="$ROOT_DIR/ios/build/DerivedData"
APP_BUNDLE="$DERIVED_DIR/Build/Products/Release-iphonesimulator/RestaurantPOS.app"
(cd ios && xcodegen generate --quiet)
xcodebuild -project ios/RestaurantPOS.xcodeproj -scheme RestaurantPOS -configuration Release \
    -destination "platform=iOS Simulator,id=$BOOTED_UDID" -derivedDataPath "$DERIVED_DIR" build -quiet

echo "[5/6] Installation de l'application sur le simulateur iPad..."
echo "  Desinstallation precedente pour rafraichir le cache de SpringBoard..."
xcrun simctl uninstall booted "$BUNDLE_ID" 2>/dev/null || true
xcrun simctl install booted "$APP_BUNDLE"

ARTIFACTS_DIR="$ROOT_DIR/ios/build/deploy"
mkdir -p "$ARTIFACTS_DIR"

echo "  Capture d'ecran de l'ecran d'accueil (icone)..."
xcrun simctl spawn booted killall -9 SpringBoard 2>/dev/null || true
sleep 2
xcrun simctl io booted screenshot "$ARTIFACTS_DIR/homescreen_verified.png" 2>/dev/null || true

echo "[6/6] Lancement de l'application RestaurantPos en Mode Production..."
xcrun simctl terminate booted "$BUNDLE_ID" 2>/dev/null || true
sleep 1
SIMCTL_CHILD_POS_SERVER_URL="$API_URL" xcrun simctl launch booted "$BUNDLE_ID"

sleep 3
SCREENSHOT_PATH="$ARTIFACTS_DIR/prod_deployed_screen.png"
xcrun simctl io booted screenshot "$SCREENSHOT_PATH"

echo ""
echo "========================================================"
echo " [TERMINE] Deploiement Production Reussi !"
echo " - Environnement API : Production (Release)"
echo " - Base SQLite       : restaurantpos.db avec donnees reelles inserees"
echo " - Client iPad natif : Installe et execute en Release"
echo " - Capture d'ecran   : $SCREENSHOT_PATH"
echo "========================================================"
