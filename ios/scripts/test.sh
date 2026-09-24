#!/usr/bin/env bash
# Lance toute la chaîne de tests de l'app iPad native.
#
#   ./scripts/test.sh              # tests unitaires + tests UI (simulateur)
#   ./scripts/test.sh unit         # tests unitaires PosKit seulement (quelques secondes)
#   ./scripts/test.sh ui           # tests UI XCUITest seulement
#   POS_API_URL=http://localhost:5080 ./scripts/test.sh contract   # contrat contre la vraie API .NET
#
# Variables : SIMULATOR (défaut « iPad Pro 13-inch (M5) »), RESULTS_DIR (défaut build/test-results).
set -euo pipefail

cd "$(dirname "$0")/.."
MODE="${1:-all}"
SIMULATOR="${SIMULATOR:-iPad Pro 13-inch (M5)}"
RESULTS_DIR="${RESULTS_DIR:-build/test-results}"
DERIVED="build/DerivedData"

command -v xcodegen >/dev/null || { echo "xcodegen requis : brew install xcodegen"; exit 1; }

run_unit() {
  echo "▶︎ Tests unitaires PosKit (logique métier, contrats JSON, stores, HTTP, SignalR)"
  (cd Packages/PosKit && swift test)
}

run_contract() {
  : "${POS_API_URL:?Définissez POS_API_URL (ex. http://localhost:5080)}"
  echo "▶︎ Tests de contrat contre $POS_API_URL"
  (cd Packages/PosKit && POS_API_URL="$POS_API_URL" swift test --filter LiveAPI)
}

run_ui() {
  echo "▶︎ Tests UI XCUITest sur « $SIMULATOR »"
  xcodegen generate --quiet
  mkdir -p "$RESULTS_DIR"
  rm -rf "$RESULTS_DIR/ui.xcresult"
  xcodebuild test \
    -project RestaurantPOS.xcodeproj \
    -scheme RestaurantPOS \
    -destination "platform=iOS Simulator,name=$SIMULATOR" \
    -derivedDataPath "$DERIVED" \
    -resultBundlePath "$RESULTS_DIR/ui.xcresult" \
    -only-testing:RestaurantPOSUITests \
    | grep -E "Test Case .*(passed|failed)|error:|Executed|\*\* TEST" || true
  if xcrun xcresulttool get test-results summary --path "$RESULTS_DIR/ui.xcresult" 2>/dev/null | grep -q '"result" : "Failed"'; then
    echo "✘ Des tests UI ont échoué — captures d'écran dans $RESULTS_DIR/ui.xcresult"
    exit 1
  fi
  echo "✔ Rapport : open $RESULTS_DIR/ui.xcresult"
}

case "$MODE" in
  unit) run_unit ;;
  ui) run_ui ;;
  contract) run_contract ;;
  all) run_unit; run_ui ;;
  *) echo "Usage : $0 [all|unit|ui|contract]"; exit 2 ;;
esac
