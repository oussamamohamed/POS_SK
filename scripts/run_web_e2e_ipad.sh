#!/bin/bash
set -e

# ==============================================================================
# Script d'Exécution des Tests E2E Web sur Profil iPad (Playwright WebKit / Safari)
# et Synchronisation avec le Simulateur iPad iOS
# Restaurant POS
# ==============================================================================

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$PROJECT_ROOT"

echo "========================================================"
echo " 🌐 RESTAURANT POS — TESTS E2E WEB (PROFIL IPAD PRO 11)"
echo "========================================================"

# 1. Vérification du serveur API Web (http://localhost:5000)
if ! curl -s --head http://127.0.0.1:5000 >/dev/null; then
    echo "⚠️ Serveur API non détecté. Démarrage de RestaurantPos.Api en arrière-plan..."
    dotnet run --project src/RestaurantPos.Api/RestaurantPos.Api.csproj > /tmp/api_server.log 2>&1 &
    API_PID=$!
    echo "Attente de démarrage du serveur API (PID: $API_PID)..."
    for i in {1..30}; do
        if curl -s --head http://127.0.0.1:5000 >/dev/null; then
            echo "✅ Serveur API démarré sur http://127.0.0.1:5000"
            break
        fi
        sleep 1
    done
else
    echo "✅ Serveur API actif sur http://127.0.0.1:5000"
fi

# 2. Synchronisation et affichage sur le simulateur iPad si disponible
DEVICE_UDID=$(xcrun simctl list devices booted | grep -E "\([A-F0-9-]{36}\)" | head -1 | grep -oE "[A-F0-9-]{36}" || true)

if [ -n "$DEVICE_UDID" ]; then
    echo "📱 Simulateur iPad actif ($DEVICE_UDID) : ouverture de l'application Web dans Mobile Safari..."
    xcrun simctl openurl "$DEVICE_UDID" "http://127.0.0.1:5000" || true
fi

# 3. Lancement des tests Playwright sous profil iPad Pro 11 (moteur WebKit / Apple Safari)
echo ""
echo "🚀 Exécution des tests Playwright E2E (iPad Pro 11 - WebKit)..."
cd tests/RestaurantPos.Web.E2ETests

npx playwright test --project="iPad Pro 11"

echo ""
echo "========================================================"
echo " 🎉 SUCCÈS : TOUS LES TESTS E2E WEB SONT VALIDÉS SUR IPAD !"
echo "========================================================"

if [ -n "$DEVICE_UDID" ]; then
    mkdir -p "$PROJECT_ROOT/tests/artifacts/simulator_screenshots"
    xcrun simctl io "$DEVICE_UDID" screenshot "$PROJECT_ROOT/tests/artifacts/simulator_screenshots/ipad_safari_e2e_done.png" 2>/dev/null || true
    echo "📸 Capture écran Mobile Safari sauvée dans tests/artifacts/simulator_screenshots/ipad_safari_e2e_done.png"
fi
