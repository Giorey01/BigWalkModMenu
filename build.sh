#!/usr/bin/env bash
# Builds every project and makes the release package.
#
#   bash build.sh              build, fill dist/pacchetto, make the zips in dist/
#   bash build.sh --no-zip     build and fill dist/pacchetto only
#
# A release is: dist/BigWalkModMenu-<version>.zip, dist/install.sh, dist/install.ps1 and dist/single/*.zip.
#
# Needs the .NET 6 SDK (or newer) and the game installed with BepInEx started at least once.
# Works on Linux and on Windows from Git Bash. The game is looked for in Steam's usual folder.
# Game somewhere else:  GAME_DIR="/path/to/Big Walk" bash build.sh
# dotnet not on PATH:   DOTNET=~/.dotnet/dotnet bash build.sh

set -eu

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC="$ROOT/BigWalkModMenu"
PKG="$ROOT/dist/pacchetto"
OUT="$PKG/BepInEx/plugins/BigWalkModMenu"
TABS="Faces Solo Character Camera Performance World Reshade Voice Extras Replay"
ZIP=1
[ "${1:-}" = "--no-zip" ] && ZIP=0

DOTNET="${DOTNET:-}"
if [ -z "$DOTNET" ]; then
  if command -v dotnet >/dev/null 2>&1; then DOTNET=dotnet
  elif [ -x "$HOME/.dotnet/dotnet" ]; then DOTNET="$HOME/.dotnet/dotnet"; export DOTNET_ROOT="$HOME/.dotnet"
  else echo "ERROR: dotnet not found. Install the .NET SDK or set DOTNET." >&2; exit 1
  fi
fi

if [ -z "${GAME_DIR:-}" ]; then
  GAME_DIR="$HOME/.local/share/Steam/steamapps/common/Big Walk"
  # Windows (Git Bash): Steam's usual place.
  WIN_GAME="${PROGRAMFILES:-C:/Program Files} (x86)/Steam/steamapps/common/Big Walk"
  [ -d "$GAME_DIR" ] || [ ! -d "$WIN_GAME" ] || GAME_DIR="$WIN_GAME"
fi
[ -f "$GAME_DIR/BepInEx/interop/Assembly-CSharp.dll" ] || {
  echo "ERROR: no BepInEx/interop/Assembly-CSharp.dll in \"$GAME_DIR\"." >&2
  echo "Install BepInEx, start the game once, or set GAME_DIR." >&2
  exit 1
}

VERSION="$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' "$SRC/Directory.Build.props")"
echo "Big Walk Mod Menu $VERSION"

# Never with -p:Dev=1: the test bridge must not ship.
for project in Core $TABS; do
  echo "  build $project"
  "$DOTNET" build "$SRC/$project" -c Release -nologo -v q -p:GameDir="$GAME_DIR" >/dev/null || {
    echo "ERROR: $project does not build. Run: $DOTNET build \"$SRC/$project\" -c Release" >&2; exit 1; }
done

rm -rf "$OUT"
mkdir -p "$OUT"
cp "$SRC/Core/bin/Release/BigWalk.ModMenu.dll" "$SRC/Core/bin/Release/StbImageSharp.dll" "$SRC/Core/bin/Release/StbImageWriteSharp.dll" "$OUT/"
cp "$SRC/Extras/bin/Release/NVorbis.dll" "$SRC/Extras/bin/Release/NLayer.dll" "$OUT/"
for tab in $TABS; do cp "$SRC/$tab/bin/Release/BigWalk.ModMenu.$tab.dll" "$OUT/"; done
echo "Package folder: $PKG"
[ "$ZIP" = 1 ] || exit 0

make_zip() { # zip file, folder whose contents go in it
  if command -v zip >/dev/null 2>&1; then (cd "$2" && zip -qr "$1" .)
  # "python3 -c" and not "command -v": on Windows python3 can be the Store placeholder, which is found but does not run.
  elif python3 -c "" >/dev/null 2>&1; then (cd "$2" && python3 -m zipfile -c "$1" ./*)
  elif command -v bsdtar >/dev/null 2>&1; then (cd "$2" && bsdtar -a -cf "$1" ./*)
  # Windows 10 and 11 come with bsdtar as tar.exe (the "tar" of Git Bash is GNU tar and cannot write zips).
  elif [ -x "${SYSTEMROOT:-}/System32/tar.exe" ]; then (cd "$2" && "$SYSTEMROOT/System32/tar.exe" -a -cf "$1" *)
  else echo "ERROR: zip, python3 or bsdtar is needed to make the zips." >&2; exit 1
  fi
}

NAME="BigWalkModMenu-$VERSION"
rm -f "$ROOT/dist/$NAME.zip"
make_zip "$ROOT/dist/$NAME.zip" "$PKG"
echo "  $NAME.zip"

# The one-line installers ("irm ... | iex", "bash <(curl ...)"): they point at this version's zip and
# carry its checksum, and go in the release next to it.
SUM="$(sha256sum "$ROOT/dist/$NAME.zip" | cut -d' ' -f1)"
for f in install.sh install.ps1; do
  sed -e "/^#/!s/@VERSION@/$VERSION/g" -e "/^#/!s/@SHA256@/$SUM/g" "$ROOT/dist/online/$f" > "$ROOT/dist/$f"
done
echo "  install.sh, install.ps1"

# One zip per mod, for mod sites that list them separately.
SINGLE="$ROOT/dist/single"
rm -rf "$SINGLE"
mkdir -p "$SINGLE"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

single() { # zip name, title, files...
  local zipname="$1" title="$2" dir
  shift 2
  dir="$TMP/$zipname"
  mkdir -p "$dir/BepInEx/plugins/BigWalkModMenu"
  for f in "$@"; do cp "$OUT/$f" "$dir/BepInEx/plugins/BigWalkModMenu/"; done
  cp "$PKG/THIRD-PARTY-NOTICES.txt" "$dir/"
  if [ "$title" = base ]; then
    cp "$PKG/README.md" "$PKG/CHANGELOG.md" "$dir/"
  else
    printf '%s\n\n%s\n%s\n' "Big Walk Mod Menu $VERSION - $title" \
      "Copy the BepInEx folder into the Big Walk game folder (merge the folders)." \
      "This file needs the base menu (\"$NAME-0-BaseMenu\") and BepInEx 6 IL2CPP be.785." > "$dir/README.txt"
  fi
  make_zip "$SINGLE/$zipname.zip" "$dir"
}

single "$NAME-0-BaseMenu" base BigWalk.ModMenu.dll StbImageSharp.dll StbImageWriteSharp.dll
for tab in $TABS; do
  if [ "$tab" = Extras ]; then single "$NAME-$tab" "$tab" "BigWalk.ModMenu.$tab.dll" NVorbis.dll NLayer.dll
  else single "$NAME-$tab" "$tab" "BigWalk.ModMenu.$tab.dll"
  fi
done
echo "  single/ ($(ls "$SINGLE" | wc -l) zips)"
