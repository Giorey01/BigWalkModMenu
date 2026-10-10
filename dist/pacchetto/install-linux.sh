#!/usr/bin/env bash
# Big Walk Mod Menu - install on Linux / Steam Deck (the game runs under Proton).
#
#   bash install-linux.sh                 looks for Big Walk in the Steam libraries
#   bash install-linux.sh "/path/to/Big Walk"
#   options:  --yes             answers yes to every question (and installs every mod)
#             --dry-run         shows what it would do, without touching anything
#             --all-mods        installs every mod without asking
#             --mod a,b,c       installs only these (names: faces solo character camera
#                               performance world reshade voice extras replay)
#
# Run it again any time: it updates what is installed and adds what you pick.

set -u

BEPINEX_URL="https://builds.bepinex.dev/projects/bepinex_be/785/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.785%2B6abdba4.zip"
BEPINEX_SHA256="2a7cbf74d26abe4765c3e662db1721b923bac39849ebfef2ca5dc7de7e2d9b7f"
GAME_NAME="Big Walk"
GAME_EXE="Big Walk.exe"

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
YES=0
DRY=0
ALL=0
WANTED=""
GAME=""

while [ $# -gt 0 ]; do
  case "$1" in
    --yes) YES=1 ;;
    --dry-run) DRY=1 ;;
    --all-mods) ALL=1 ;;
    --mod) shift; WANTED="${1:-}" ;;
    --mod=*) WANTED="${1#--mod=}" ;;
    -h|--help) sed -n '2,13p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) GAME="$1" ;;
  esac
  shift
done

say()  { printf '%s\n' "$*"; }
fail() { printf '\nERROR: %s\n' "$*" >&2; exit 1; }

ask() {
  [ "$YES" = 1 ] && return 0
  local answer
  printf '%s [y/N] ' "$1"
  read -r answer || return 1
  case "$answer" in y|Y|yes|YES|Yes) return 0 ;; *) return 1 ;; esac
}

[ -d "$HERE/BepInEx/plugins/BigWalkModMenu" ] || fail "The package's BepInEx folder is missing next to this script. Extract the whole zip and try again."

# ---- find the game ----

steam_roots() {
  printf '%s\n' \
    "$HOME/.local/share/Steam" \
    "$HOME/.steam/steam" \
    "$HOME/.steam/root" \
    "$HOME/.steam/debian-installation" \
    "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam" \
    "$HOME/.var/app/com.valvesoftware.Steam/data/Steam" \
    "$HOME/snap/steam/common/.local/share/Steam" \
    "$HOME/snap/steam/common/.steam/steam"
}

find_game() {
  local root lib vdf
  while IFS= read -r root; do
    [ -d "$root/steamapps" ] || continue
    if [ -f "$root/steamapps/common/$GAME_NAME/$GAME_EXE" ]; then
      printf '%s\n' "$root/steamapps/common/$GAME_NAME"; return 0
    fi
    # Other libraries (other drives) are listed here.
    vdf="$root/steamapps/libraryfolders.vdf"
    [ -f "$vdf" ] || continue
    while IFS= read -r lib; do
      if [ -f "$lib/steamapps/common/$GAME_NAME/$GAME_EXE" ]; then
        printf '%s\n' "$lib/steamapps/common/$GAME_NAME"; return 0
      fi
    done < <(sed -n 's/^[[:space:]]*"path"[[:space:]]*"\(.*\)"[[:space:]]*$/\1/p' "$vdf")
  done < <(steam_roots)
  return 1
}

if [ -z "$GAME" ]; then
  GAME="$(find_game)" || fail "Cannot find $GAME_NAME in the Steam libraries. Pass the game folder: bash install-linux.sh \"/path/to/$GAME_NAME\""
fi
GAME="${GAME%/}"
[ -f "$GAME/$GAME_EXE" ] || fail "There is no \"$GAME_EXE\" in \"$GAME\": that is not the game folder."
say "Game found: $GAME"

# The game is running if a process has the name of its executable, or its first
# argument ends with that name (how it shows up under Proton). The whole command line is not
# searched: it would contain the name for this very script too.
game_running() {
  local f first
  pgrep -x "$GAME_EXE" >/dev/null 2>&1 && return 0
  for f in /proc/[0-9]*/cmdline; do
    first="$(tr '\0' '\n' < "$f" 2>/dev/null | head -n 1)"
    case "$first" in *"$GAME_EXE") return 0 ;; esac
  done
  return 1
}

if game_running; then
  fail "Big Walk is running. Close it and run this script again."
fi

# ---- BepInEx (the mod loader) ----

extract_zip() { # zip, destination
  if command -v unzip >/dev/null 2>&1; then
    unzip -qo "$1" -d "$2"
  elif command -v python3 >/dev/null 2>&1; then
    python3 -c 'import sys, zipfile; zipfile.ZipFile(sys.argv[1]).extractall(sys.argv[2])' "$1" "$2"
  elif command -v bsdtar >/dev/null 2>&1; then
    bsdtar -xf "$1" -C "$2"
  else
    return 1
  fi
}

download() { # url, file
  if command -v curl >/dev/null 2>&1; then
    curl -fL --retry 2 -o "$2" "$1"
  elif command -v wget >/dev/null 2>&1; then
    wget -O "$2" "$1"
  else
    return 1
  fi
}

if [ -f "$GAME/BepInEx/core/BepInEx.Unity.IL2CPP.dll" ]; then
  say "BepInEx is already installed."
else
  say ""
  say "The game does not have BepInEx, the program that loads mods."
  say "Downloading it from the official site (about 35 MB):"
  say "  $BEPINEX_URL"
  if [ "$DRY" = 1 ]; then
    say "[dry run] would download BepInEx and extract it into: $GAME"
  else
    ask "Go ahead?" || fail "Without BepInEx the mods cannot start. Installation cancelled."
    command -v sha256sum >/dev/null 2>&1 || fail "The sha256sum command is missing: the download cannot be verified."
    TMP="$(mktemp -d)" || fail "Could not create a temporary folder."
    trap 'rm -rf "$TMP"' EXIT
    # A file already downloaded next to the script is fine too, if it is the right one.
    LOCAL="$(ls "$HERE"/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.785*.zip 2>/dev/null | head -n 1)"
    if [ -n "$LOCAL" ]; then
      cp "$LOCAL" "$TMP/bepinex.zip"
    else
      download "$BEPINEX_URL" "$TMP/bepinex.zip" || fail "Download failed (curl or wget and a network connection are needed)."
    fi
    SUM="$(sha256sum "$TMP/bepinex.zip" | cut -d' ' -f1)"
    [ "$SUM" = "$BEPINEX_SHA256" ] || fail "The downloaded file is not the expected one (different checksum). Not installing it."
    extract_zip "$TMP/bepinex.zip" "$GAME" || fail "Could not extract the zip (unzip, python3 or bsdtar is needed)."
    [ -f "$GAME/BepInEx/core/BepInEx.Unity.IL2CPP.dll" ] || fail "BepInEx does not appear to be installed after extraction."
    say "BepInEx installed."
  fi
fi

# ---- the mods ----
#
# Each mod is a tab of the menu and a file of its own. The base menu ("General") is always installed.
# id | name | description | files, space separated, relative to BepInEx/plugins

MODS="faces|Faces|a photo of a face on the characters' heads|BigWalkModMenu/BigWalk.ModMenu.Faces.dll
solo|Solo|up to four characters controlled by one player|BigWalkModMenu/BigWalk.ModMenu.Solo.dll
character|Character|hats, accessories, colours, proportions|BigWalkModMenu/BigWalk.ModMenu.Character.dll
camera|Camera|selfie, third person, free camera, photos|BigWalkModMenu/BigWalk.ModMenu.Camera.dll
performance|Performance|FPS counter and advanced graphics options|BigWalkModMenu/BigWalk.ModMenu.Performance.dll
world|World|time of day, sky, fog, colour filters|BigWalkModMenu/BigWalk.ModMenu.World.dll
reshade|Reshade|bloom, film tones, sharpening|BigWalkModMenu/BigWalk.ModMenu.Reshade.dll
voice|Voice|volume of each player's voice|BigWalkModMenu/BigWalk.ModMenu.Voice.dll
extras|Extras|music, sounds, emotes, speech bubbles|BigWalkModMenu/BigWalk.ModMenu.Extras.dll BigWalkModMenu/NVorbis.dll BigWalkModMenu/NLayer.dll
replay|Replay|record and replay scenes|BigWalkModMenu/BigWalk.ModMenu.Replay.dll"
CORE_FILES="BigWalkModMenu/BigWalk.ModMenu.dll BigWalkModMenu/StbImageSharp.dll BigWalkModMenu/StbImageWriteSharp.dll"

SRC="$HERE/BepInEx/plugins"
DST="$GAME/BepInEx/plugins"
# Up to 1.4.0 the Faces mod was a plugin of its own, in this folder.
OLD_FACES="$DST/FaceMaskMod"

mod_field() { printf '%s\n' "$MODS" | sed -n "${1}p" | cut -d'|' -f"$2"; }
MOD_COUNT="$(printf '%s\n' "$MODS" | wc -l)"

first_file() { printf '%s' "${1%% *}"; }
in_package() { [ -f "$SRC/$(first_file "$(mod_field "$1" 4)")" ]; }
installed() {
  [ -f "$DST/$(first_file "$(mod_field "$1" 4)")" ] && return 0
  [ "$(mod_field "$1" 1)" = faces ] && [ -f "$OLD_FACES/BigWalk.FaceMaskMod.dll" ]
}

# SELECTED: the numbers of the mods to install or update, space separated.
SELECTED=""
select_all() {
  SELECTED=""
  local n=1
  while [ "$n" -le "$MOD_COUNT" ]; do in_package "$n" && SELECTED="$SELECTED $n"; n=$((n + 1)); done
}

if [ -n "$WANTED" ]; then
  for name in $(printf '%s' "$WANTED" | tr ',' ' ' | tr '[:upper:]' '[:lower:]'); do
    n=1; found=0
    while [ "$n" -le "$MOD_COUNT" ]; do
      if [ "$(mod_field "$n" 1)" = "$name" ]; then
        in_package "$n" || fail "The mod \"$name\" is not in this package."
        SELECTED="$SELECTED $n"; found=1
      fi
      n=$((n + 1))
    done
    [ "$found" = 1 ] || fail "Unknown mod \"$name\". Valid names: $(printf '%s\n' "$MODS" | cut -d'|' -f1 | tr '\n' ' ')"
  done
elif [ "$ALL" = 1 ] || [ "$YES" = 1 ] || [ "$DRY" = 1 ]; then
  select_all
else
  say ""
  say "Which mods do you want? Each one is a tab of the menu. The base menu is always installed."
  say ""
  n=1
  while [ "$n" -le "$MOD_COUNT" ]; do
    if in_package "$n"; then
      state=""
      installed "$n" && state="  [installed]"
      printf '  %2d) %-12s %s%s\n' "$n" "$(mod_field "$n" 2)" "$(mod_field "$n" 3)" "$state"
    fi
    n=$((n + 1))
  done
  say ""
  say "  Enter      all of them"
  say "  numbers    only those, separated by spaces (example: 1 3 8)"
  say "  m          only the ones you are missing"
  say ""
  say "The ones already installed are updated anyway, and none is removed."
  printf 'Choice: '
  read -r choice || choice=""
  case "$choice" in
    "") select_all ;;
    m|M)
      n=1
      while [ "$n" -le "$MOD_COUNT" ]; do
        in_package "$n" && ! installed "$n" && SELECTED="$SELECTED $n"
        n=$((n + 1))
      done ;;
    *)
      for n in $(printf '%s' "$choice" | tr ',;' '  '); do
        case "$n" in ''|*[!0-9]*) fail "\"$n\" is not a number from the list." ;; esac
        { [ "$n" -ge 1 ] && [ "$n" -le "$MOD_COUNT" ] && in_package "$n"; } || fail "There is no number $n in the list."
        SELECTED="$SELECTED $n"
      done ;;
  esac
fi

# Mods already present are always updated: tabs of a different version do not work with the menu.
n=1
while [ "$n" -le "$MOD_COUNT" ]; do
  if in_package "$n" && installed "$n"; then
    case " $SELECTED " in *" $n "*) ;; *) SELECTED="$SELECTED $n" ;; esac
  fi
  n=$((n + 1))
done

copy_file() { # path relative to BepInEx/plugins
  [ -f "$SRC/$1" ] || fail "The package is missing the file $1. Extract the whole zip again."
  if [ "$DRY" = 1 ]; then say "[dry run] would copy: $1"; return 0; fi
  mkdir -p "$(dirname "$DST/$1")" || fail "Cannot write to \"$GAME\"."
  cp -f "$SRC/$1" "$DST/$1" || fail "Copy failed: $1"
}

say ""
for f in $CORE_FILES; do copy_file "$f"; done
say "Base menu: ok"
NAMES=""
n=1
while [ "$n" -le "$MOD_COUNT" ]; do
  case " $SELECTED " in
    *" $n "*)
      for f in $(mod_field "$n" 4); do copy_file "$f"; done
      NAMES="$NAMES $(mod_field "$n" 2)" ;;
  esac
  n=$((n + 1))
done
[ -n "$NAMES" ] && say "Mods installed or updated:$NAMES"
if [ "$DRY" = 0 ]; then
  [ -f "$DST/BigWalkModMenu/BigWalk.ModMenu.dll" ] || fail "The mod does not appear to have been copied."
  [ -f "$DST/BigWalkModMenu/BigWalk.ModMenu.Faces.dll" ] && mkdir -p "$DST/BigWalkModMenu/faces/photos" 2>/dev/null
fi

# The old Faces plugin would be loaded next to the new one. Its photos and settings stay where they
# are: the Faces tab moves them to BigWalkModMenu/faces the first time the game starts.
if [ -f "$DST/BigWalkModMenu/BigWalk.ModMenu.Faces.dll" ] || [ "$DRY" = 1 ]; then
  for f in "$OLD_FACES/BigWalk.FaceMaskMod.dll" "$OLD_FACES/StbImageSharp.dll"; do
    [ -f "$f" ] || continue
    if [ "$DRY" = 1 ]; then say "[dry run] would delete the old file: ${f#"$DST"/}"; else rm -f -- "$f" && say "Old Faces plugin file removed: ${f#"$DST"/}"; fi
  done
fi
MISSING=""
n=1
while [ "$n" -le "$MOD_COUNT" ]; do
  if in_package "$n" && ! installed "$n"; then
    case " $SELECTED " in *" $n "*) ;; *) MISSING="$MISSING $(mod_field "$n" 2)" ;; esac
  fi
  n=$((n + 1))
done
[ -n "$MISSING" ] && say "Not installed (run the script again when you want them):$MISSING"

if [ -f "$DST/BigWalkSoloMod/BigWalkSoloMod.dll" ] && [ -f "$DST/BigWalkModMenu/BigWalk.ModMenu.Solo.dll" ]; then
  say ""
  say "WARNING: the stand-alone \"Big Walk Solo Player\" mod is installed too. It does the same job as the"
  say "Solo tab: remove the folder BepInEx/plugins/BigWalkSoloMod, or the tab will stay off."
fi

say ""
say "DONE. Two more things:"
say ""
say "1. On Linux Steam has to load BepInEx. In Steam: right-click Big Walk >"
say "   Properties > General > Launch Options, and paste this line:"
say ""
say '      WINEDLLOVERRIDES="winhttp=n,b" %command%'
say ""
say "2. The first start after installing BepInEx takes a few minutes (it prepares the"
say "   game files) and looks frozen: let it finish."
say ""
say "In game the menu opens with F8 or Insert."
if [ -f "$DST/BigWalkModMenu/BigWalk.ModMenu.Faces.dll" ]; then
  say "Photos for the faces go in: $GAME/BepInEx/plugins/BigWalkModMenu/faces/photos"
  say "(or pick another folder from the Faces tab)."
fi
