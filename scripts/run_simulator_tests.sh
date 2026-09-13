#!/bin/bash
set -e

# ==============================================================================
# Script d'Automatisation des Tests End-to-End sur Simulateur iPad / iOS (.NET MAUI)
# Restaurant POS
# ==============================================================================

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$PROJECT_ROOT"

echo "========================================================"
echo " 🚀 RESTAURANT POS — TESTS AUTOMATISÉS SUR SIMULATEUR"
echo "========================================================"

# 1. Vérification du simulateur actif
DEVICE_UDID=$(xcrun simctl list devices booted | grep -E "\([A-F0-9-]{36}\)" | head -1 | grep -oE "[A-F0-9-]{36}")

if [ -z "$DEVICE_UDID" ]; then
    echo "⚠️ Aucun simulateur allumé détecté. Démarrage de iPad Pro 11-inch (M5)..."
    DEVICE_UDID=$(xcrun simctl list devices | grep -E "iPad Pro 11-inch" | head -1 | grep -oE "[A-F0-9-]{36}")
    if [ -z "$DEVICE_UDID" ]; then
        DEVICE_UDID=$(xcrun simctl list devices | grep -E "iPad" | head -1 | grep -oE "[A-F0-9-]{36}")
    fi
    xcrun simctl boot "$DEVICE_UDID"
    open -a Simulator
fi

echo "📱 Simulateur cible: $DEVICE_UDID"

# 2. Compilation de l'application MAUI iOS
echo "📦 Compilation de RestaurantPos.Client.Maui (net9.0-ios)..."
dotnet build src/RestaurantPos.Client.Maui/RestaurantPos.Client.Maui.csproj \
    -p:BuildingForMaui=true \
    -f net9.0-ios \
    -v:m

APP_BUNDLE="src/RestaurantPos.Client.Maui/bin/Debug/net9.0-ios/iossimulator-arm64/RestaurantPos.Client.Maui.app"

if [ ! -d "$APP_BUNDLE" ]; then
    echo "❌ Erreur : App bundle introuvable à $APP_BUNDLE"
    exit 1
fi

# 3. Déploiement sur le simulateur
echo "📲 Installation de l'application..."
xcrun simctl install "$DEVICE_UDID" "$APP_BUNDLE"

# 4. Nettoyage des anciens rapports
rm -f /tmp/pos_simulator_test_report.json
mkdir -p tests/artifacts/simulator_screenshots

# 5. Lancement avec drapeau de test automatique
echo "⚡ Lancement de la suite de tests automatiques..."
SIMCTL_CHILD_POS_AUTO_TEST=1 xcrun simctl launch \
    --terminate-running-process \
    --stdout=/tmp/pos_sim_stdout.log \
    --stderr=/tmp/pos_sim_stderr.log \
    "$DEVICE_UDID" com.restaurantpos.client --auto-test

echo "⏳ Exécution du scénario en cours sur le simulateur..."

# Surveillance et capture de captures d'écran aux étapes clés
for i in {1..40}; do
    sleep 1
    if [ -f /tmp/pos_simulator_test_report.json ]; then
        echo "✅ Rapport de test détecté après ${i}s !"
        break
    fi
    # Captures intermédiaires
    if [ "$i" -eq 4 ]; then
        xcrun simctl io "$DEVICE_UDID" screenshot tests/artifacts/simulator_screenshots/step2_floor.png 2>/dev/null || true
    elif [ "$i" -eq 7 ]; then
        xcrun simctl io "$DEVICE_UDID" screenshot tests/artifacts/simulator_screenshots/step3_cart.png 2>/dev/null || true
    elif [ "$i" -eq 12 ]; then
        xcrun simctl io "$DEVICE_UDID" screenshot tests/artifacts/simulator_screenshots/step5_kds.png 2>/dev/null || true
    elif [ "$i" -eq 17 ]; then
        xcrun simctl io "$DEVICE_UDID" screenshot tests/artifacts/simulator_screenshots/step7_checkout.png 2>/dev/null || true
    fi
done

# Capture finale
xcrun simctl io "$DEVICE_UDID" screenshot tests/artifacts/simulator_screenshots/step8_final.png 2>/dev/null || true

# 6. Analyse du résultat
if [ ! -f /tmp/pos_simulator_test_report.json ]; then
    echo "❌ TIMEOUT : Le rapport de test n'a pas été généré dans le délai imparti."
    if [ -f /tmp/pos_sim_stderr.log ]; then
        echo "--- STDERR ---"
        cat /tmp/pos_sim_stderr.log
    fi
    if [ -f /tmp/pos_sim_stdout.log ]; then
        echo "--- STDOUT ---"
        tail -n 30 /tmp/pos_sim_stdout.log
    fi
    exit 1
fi

echo ""
echo "========================================================"
echo " 📊 RÉSULTATS DES TESTS AUTOMATIQUES DU SIMULATEUR"
echo "========================================================"
cat /tmp/pos_simulator_test_report.json
echo ""

ALL_PASSED=$(grep -o '"AllPassed": true' /tmp/pos_simulator_test_report.json || true)

if [ -n "$ALL_PASSED" ]; then
    echo "🎉 SUCCÈS COMPLET : Tous les tests ont été validés sur le simulateur iOS !"
    exit 0
else
    echo "❌ ÉCHEC : Certains tests ont échoué."
    exit 1
fi
