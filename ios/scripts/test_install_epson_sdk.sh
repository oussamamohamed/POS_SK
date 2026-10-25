#!/usr/bin/env bash
# Test de install_epson_sdk.sh sur un faux xcframework (même structure que le SDK 2.37.1). Aucun vrai SDK requis.
set -uo pipefail
cd "$(dirname "$0")"
SCRIPT="$PWD/install_epson_sdk.sh"
T="$(mktemp -d)"; trap 'rm -rf "$T"' EXIT
fail() { echo "ÉCHEC : $*"; exit 1; }

make_sdk() { # $1 = dossier racine du faux SDK ; $2 = "nomodulemap" pour en omettre un
  local root="$1/ePOS_SDK_iOS_v2.37.1"
  for slice in ios-arm64 ios-arm64_x86_64-simulator; do
    local fw="$root/libepos2.xcframework/$slice/libepos2.framework"
    mkdir -p "$fw/Headers" "$fw/Modules"
    echo "// ePOS2" > "$fw/Headers/ePOS2.h"
    echo "framework module libepos2 {}" > "$fw/Modules/module.modulemap"
  done
  mkdir -p "$root/libepos2-static.xcframework/ios-arm64" "$root/libeposeasyselect.xcframework"
  [ "${2:-}" = nomodulemap ] && rm "$1/ePOS_SDK_iOS_v2.37.1/libepos2.xcframework/ios-arm64/libepos2.framework/Modules/module.modulemap"
  return 0
}

# 1. dossier valide : installé, version affichée, voisins ignorés
make_sdk "$T/ok"
V="$T/vendor"
out="$(EPSON_VENDOR_DIR="$V" "$SCRIPT" "$T/ok/ePOS_SDK_iOS_v2.37.1" 2>&1)" || fail "dossier valide refusé : $out"
echo "$out" | grep -q "2.37.1" || fail "version non affichée"
echo "$out" | grep -q "swift package purge-cache" || fail "rappel de purge absent"
grep -v '^[[:space:]]*#' "$SCRIPT" | grep -v '^echo' | grep -q "purge-cache" && fail "le script lance une purge"
echo "$out" | grep -qi "commit" || fail "rappel de non-commit absent"
[ -f "$V/libepos2.xcframework/ios-arm64_x86_64-simulator/libepos2.framework/Modules/module.modulemap" ] || fail "modulemap non copié"
[ ! -e "$V/libepos2-static.xcframework" ] && [ ! -e "$V/libeposeasyselect.xcframework" ] || fail "voisin copié"

# 2. idempotent
EPSON_VENDOR_DIR="$V" "$SCRIPT" "$T/ok/ePOS_SDK_iOS_v2.37.1" >/dev/null 2>&1 || fail "relance refusée"
[ "$(find "$V/libepos2.xcframework" -name ePOS2.h | wc -l)" -eq 2 ] || fail "relance non idempotente"

# 3. zip valide (racine détectée dans un dossier temporaire neuf)
(cd "$T/ok" && zip -qr "$T/sdk.zip" ePOS_SDK_iOS_v2.37.1)
EPSON_VENDOR_DIR="$T/vendor-zip" "$SCRIPT" "$T/sdk.zip" >/dev/null 2>&1 || fail "zip valide refusé"
[ -f "$T/vendor-zip/libepos2.xcframework/ios-arm64/libepos2.framework/Headers/ePOS2.h" ] || fail "zip non installé"

# 4. zip sans libepos2.xcframework
mkdir -p "$T/empty/ePOS_SDK_iOS_v2.37.1/libepos2-static.xcframework"
(cd "$T/empty" && zip -qr "$T/empty.zip" ePOS_SDK_iOS_v2.37.1)
out="$(EPSON_VENDOR_DIR="$T/v2" "$SCRIPT" "$T/empty.zip" 2>&1)" && fail "zip sans libepos2 accepté"
echo "$out" | grep -q "libepos2.xcframework" || fail "message absent pour zip sans libepos2"

# 5. modulemap absent
make_sdk "$T/bad" nomodulemap
out="$(EPSON_VENDOR_DIR="$T/v3" "$SCRIPT" "$T/bad/ePOS_SDK_iOS_v2.37.1" 2>&1)" && fail "modulemap absent accepté"
echo "$out" | grep -q "module.modulemap" || fail "message absent pour modulemap"
[ ! -e "$T/v3/libepos2.xcframework" ] || fail "installation partielle laissée"

echo "OK : install_epson_sdk.sh"
