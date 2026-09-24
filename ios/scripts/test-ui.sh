#!/usr/bin/env bash
# Lance les tests UI (XCUITest) sur un simulateur iPad.
# Usage : ios/scripts/test-ui.sh ["Nom du simulateur"]   (macOS + Xcode 16 ou plus récent)
set -euo pipefail

cd "$(dirname "$0")/.."

if command -v xcodegen >/dev/null 2>&1; then
  xcodegen generate --quiet
fi

DEVICE_NAME="${1:-}"
if [[ -z "$DEVICE_NAME" ]]; then
  # Premier iPad disponible, en privilégiant un iPad Pro / Air récent.
  DEVICE_ID=$(xcrun simctl list devices available --json | python3 -c '
import json, sys
devices = [d for runtime, items in json.load(sys.stdin)["devices"].items() if "iOS" in runtime for d in items]
ipads = [d for d in devices if d["name"].startswith("iPad")]
ipads.sort(key=lambda d: (("Pro" not in d["name"]) and ("Air" not in d["name"]), d["name"]))
print(ipads[0]["udid"] if ipads else "")
')
else
  DEVICE_ID=$(xcrun simctl list devices available | grep -F "$DEVICE_NAME (" | head -1 | sed -E 's/.*\(([0-9A-F-]{36})\).*/\1/')
fi

if [[ -z "$DEVICE_ID" ]]; then
  echo "Aucun simulateur iPad disponible. Installez un runtime iOS dans Xcode > Settings > Platforms." >&2
  exit 1
fi

echo "Simulateur : $DEVICE_ID"
rm -rf build/UITests.xcresult
xcodebuild test \
  -project RestaurantPOS.xcodeproj \
  -scheme RestaurantPOS \
  -destination "id=$DEVICE_ID" \
  -resultBundlePath build/UITests.xcresult \
  CODE_SIGNING_ALLOWED=NO \
  | { command -v xcbeautify >/dev/null 2>&1 && xcbeautify || cat; }
