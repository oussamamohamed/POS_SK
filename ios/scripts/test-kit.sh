#!/usr/bin/env bash
# Tests unitaires, de contrat et d'intégration du package POSKit (macOS ou Linux).
# Avec un serveur RestaurantPos.Api démarré : POS_API_URL=http://127.0.0.1:5080 ios/scripts/test-kit.sh
set -euo pipefail
cd "$(dirname "$0")/../POSKit"
swift test "$@"
