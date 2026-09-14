#!/bin/bash
set -e

# ==============================================================================
# Script d'Exécution & Vérification Visuelle en Direct sur Émulateur iPad
# Déploie l'application, capture les écrans en direct et vérifie avec Apple Vision OCR
# ==============================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$ROOT_DIR"

SIM_DEVICE="iPad Pro 11-inch (M5)"
BUNDLE_ID="com.restaurantpos.client"
API_URL="http://127.0.0.1:5000"
SCREENSHOTS_DIR="$ROOT_DIR/tests/artifacts/simulator_screenshots"
mkdir -p "$SCREENSHOTS_DIR"

echo "================================================================================"
echo " 📱 RESTAURANT POS — TEST VISUEL ET FONCTIONNEL DIRECT SUR ÉMULATEUR IPAD      "
echo "================================================================================"

# 1. Simulateur iPad
echo "[1/5] Vérification de l'émulateur iPad..."
BOOTED_UDID=$(xcrun simctl list devices booted | grep -E -o "[0-9A-F]{8}-([0-9A-F]{4}-){3}[0-9A-F]{12}" | head -n 1 || true)
if [ -z "$BOOTED_UDID" ]; then
    echo "  Démarrage de l'émulateur $SIM_DEVICE..."
    xcrun simctl boot "$SIM_DEVICE" 2>/dev/null || true
    open -a Simulator
    sleep 3
    BOOTED_UDID=$(xcrun simctl list devices booted | grep -E -o "[0-9A-F]{8}-([0-9A-F]{4}-){3}[0-9A-F]{12}" | head -n 1)
fi
echo "  Émulateur iPad connecté (UDID: $BOOTED_UDID)"

# 2. Serveur API
echo "[2/5] Vérification de l'API Backend..."
if ! curl -s -f "$API_URL/api/health" > /dev/null 2>&1; then
    echo "  Démarrage de l'API en environnement Production..."
    pkill -f "RestaurantPos.Api" 2>/dev/null || true
    sleep 1
    ASPNETCORE_ENVIRONMENT=Production dotnet run --project src/RestaurantPos.Api/RestaurantPos.Api.csproj -c Release --no-build > /tmp/pos_api_prod.log 2>&1 &
    for i in {1..20}; do
        if curl -s -f "$API_URL/api/health" > /dev/null 2>&1; then
            break
        fi
        sleep 0.5
    done
fi
"$SCRIPT_DIR/seed_production_test_data.sh" > /dev/null 2>&1 || true
echo "  API Backend prête avec données de test insérées."

# 3. Compilation & Déploiement Client MAUI
echo "[3/5] Compilation et installation du client MAUI sur l'émulateur..."
APP_BUNDLE="src/RestaurantPos.Client.Maui/bin/Release/net9.0-ios/iossimulator-arm64/RestaurantPos.Client.Maui.app"
dotnet build src/RestaurantPos.Client.Maui/RestaurantPos.Client.Maui.csproj -c Release -p:BuildingForMaui=true -f net9.0-ios --nologo
xcrun simctl install booted "$APP_BUNDLE"

# 4. Nettoyage des signaux et anciens rapports
rm -f /tmp/pos_visual_*.ready /tmp/pos_simulator_test_report.json /tmp/pos_sim_stdout.log /tmp/pos_sim_stderr.log

# 5. Lancement de la vérification visuelle OCR et du test applicatif
echo "[4/5] Lancement de l'inspecteur visuel Apple Vision..."
swift "$SCRIPT_DIR/verify_simulator_visual.swift" "$SCREENSHOTS_DIR" &
VISUAL_PID=$!

echo "[5/5] Lancement de l'application sur l'émulateur iPad..."
sleep 0.5
xcrun simctl terminate booted "$BUNDLE_ID" 2>/dev/null || true
sleep 0.5
SIMCTL_CHILD_POS_AUTO_TEST=1 xcrun simctl launch \
    --stdout=/tmp/pos_sim_stdout.log \
    --stderr=/tmp/pos_sim_stderr.log \
    booted "$BUNDLE_ID" --auto-test > /dev/null 2>&1 &

# Attente de la fin de l'inspecteur visuel
wait $VISUAL_PID
RESULT=$?

echo ""
if [ $RESULT -eq 0 ]; then
    echo "================================================================================"
    echo " 🎉 SUCCÈS : L'ÉMULATEUR IPAD A AFFICHÉ ET VALIDÉ TOUS LES ÉCRANS DU TEST !"
    echo " Captures enregistrées dans : $SCREENSHOTS_DIR"
    echo "================================================================================"
    exit 0
else
    echo "================================================================================"
    echo " ❌ ERREUR : Des anomalies visuelles ont été détectées sur l'émulateur."
    echo "================================================================================"
    exit 1
fi
