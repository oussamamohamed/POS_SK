#!/usr/bin/env bash
# Installe le SDK Epson ePOS (iOS, framework dynamique) dans Packages/PosPrinters/Vendor/ (ignoré par git).
#   ./scripts/install_epson_sdk.sh <ePOS_SDK_iOS_vX.Y.Z.zip | dossier extrait>
# Ne copie que libepos2.xcframework (jamais libepos2-static ni libeposeasyselect). Idempotent.
# Variable : EPSON_VENDOR_DIR (défaut Packages/PosPrinters/Vendor), pour les tests.
set -euo pipefail

SRC="${1:?Usage : $0 <zip ou dossier du SDK Epson>}"
VENDOR="${EPSON_VENDOR_DIR:-$(cd "$(dirname "$0")/.." && pwd)/Packages/PosPrinters/Vendor}"
die() { echo "Erreur : $*" >&2; exit 1; }

WORK="$(mktemp -d)"; trap 'rm -rf "$WORK"' EXIT
if [ -f "$SRC" ]; then
  mkdir "$WORK/x"
  unzip -q "$SRC" -d "$WORK/x" || die "zip illisible : $SRC"
  ROOT="$WORK/x"
elif [ -d "$SRC" ]; then
  ROOT="$SRC"
else
  die "introuvable : $SRC"
fi

# Racine du SDK : le dossier qui contient libepos2.xcframework (lui-même ou un sous-dossier ePOS_SDK_iOS_v*).
XCF="$(find "$ROOT" -maxdepth 2 -type d -name libepos2.xcframework | head -n 1)"
[ -n "$XCF" ] || die "libepos2.xcframework introuvable dans $SRC"
SDK_ROOT="$(dirname "$XCF")"
VERSION="$(basename "$SDK_ROOT" | sed -n 's/.*_v\([0-9][0-9.]*\).*/\1/p')"
VERSION="${VERSION:-inconnue}"

SLICES=0
for slice in "$XCF"/*/; do
  [ -d "$slice/libepos2.framework" ] || continue
  SLICES=$((SLICES + 1))
  [ -f "$slice/libepos2.framework/Headers/ePOS2.h" ] || die "${slice}libepos2.framework/Headers/ePOS2.h manquant"
  [ -f "$slice/libepos2.framework/Modules/module.modulemap" ] || die "${slice}libepos2.framework/Modules/module.modulemap manquant"
done
[ "$SLICES" -gt 0 ] || die "aucune tranche libepos2.framework dans $XCF (SDK statique ?)"

mkdir -p "$VENDOR"
# Copie dans un nom temporaire puis échange : un échec de copie ne laisse pas Vendor/ sans SDK.
rm -rf "$VENDOR/libepos2.xcframework.tmp"
cp -R "$XCF" "$VENDOR/libepos2.xcframework.tmp"
rm -rf "$VENDOR/libepos2.xcframework"
mv "$VENDOR/libepos2.xcframework.tmp" "$VENDOR/libepos2.xcframework"
echo "SDK Epson ePOS $VERSION installé dans $VENDOR/libepos2.xcframework ($SLICES tranches)."
echo "Ne commitez jamais ce SDK (Vendor/ est dans .gitignore)."
# SwiftPM met en cache l'évaluation de Package.swift sans voir Vendor/ : la purge est laissée à l'utilisateur
# (elle vide les caches SwiftPM de tout le poste, donc ce script ne la lance jamais).
echo "Si SwiftPM ou Xcode ignore le SDK (« does not contain a binary artifact » ou SDK non vu), relancez le build avec une variable d'environnement neuve (ex. \`env POS_X=\$RANDOM xcodebuild …\`) : la clé du cache de manifestes change."
echo "Dernier recours, à lancer à la main : \`swift package purge-cache\` (vide les caches SwiftPM de tous vos projets)."
