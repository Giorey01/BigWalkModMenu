#!/usr/bin/env bash
# Big Walk Mod Menu - uninstall on Linux / Steam Deck.
#
#   bash uninstall-linux.sh                 looks for Big Walk in the Steam libraries
#   bash uninstall-linux.sh "/path/to/Big Walk"
#   options:  --yes       answers yes (removes the mods, KEEPS your files)
#             --all       also removes your files: photos, faces, recordings, settings
#             --bepinex   also removes BepInEx (and so every other installed mod)
#             --dry-run   shows what it would do, without touching anything

set -u

GAME_NAME="Big Walk"
GAME_EXE="Big Walk.exe"
YES=0
DRY=0
ALL=0
LOADER=0
GAME=""

for arg in "$@"; do
  case "$arg" in
    --yes) YES=1 ;;
    --dry-run) DRY=1 ;;
    --all) ALL=1 ;;
    --bepinex) LOADER=1 ;;
    -h|--help) sed -n '2,10p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) GAME="$arg" ;;
  esac
done

say()  { printf '%s\n' "$*"; }
fail() { printf '\nERROR: %s\n' "$*" >&2; exit 1; }

ask() { # question, answer used with --yes (0 = yes, 1 = no)
  local answer
  if [ "$YES" = 1 ]; then return "$2"; fi
  printf '%s [y/N] ' "$1"
  read -r answer || return 1
  case "$answer" in y|Y|yes|YES|Yes) return 0 ;; *) return 1 ;; esac
}

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
  GAME="$(find_game)" || fail "Cannot find $GAME_NAME in the Steam libraries. Pass the game folder: bash uninstall-linux.sh \"/path/to/$GAME_NAME\""
fi
GAME="${GAME%/}"
# Nothing is deleted outside a folder that really holds the game's executable.
[ -f "$GAME/$GAME_EXE" ] || fail "There is no \"$GAME_EXE\" in \"$GAME\": that is not the game folder."
say "Game found: $GAME"

game_running() {
  local f first
  pgrep -x "$GAME_EXE" >/dev/null 2>&1 && return 0
  for f in /proc/[0-9]*/cmdline; do
    first="$(tr '\0' '\n' < "$f" 2>/dev/null | head -n 1)"
    case "$first" in *"$GAME_EXE") return 0 ;; esac
  done
  return 1
}
game_running && fail "Big Walk is running. Close it and run this script again."

PLUGINS="$GAME/BepInEx/plugins"
MENU="$PLUGINS/BigWalkModMenu"
FACES="$PLUGINS/FaceMaskMod"

remove() { # path: a file or folder inside the game folder
  case "$1" in "$GAME"/*) ;; *) fail "Refusing to delete outside the game folder: $1" ;; esac
  [ -e "$1" ] || return 0
  if [ "$DRY" = 1 ]; then say "[dry run] would delete: ${1#"$GAME"/}"; else rm -rf -- "$1"; fi
}

if [ ! -d "$MENU" ] && [ ! -d "$FACES" ]; then
  say "Big Walk Mod Menu does not appear to be installed."
else
  say ""
  say "Removing Big Walk Mod Menu and the faces mod."
  ask "Go ahead?" 0 || fail "Uninstall cancelled."

  if [ "$ALL" = 0 ] && [ "$YES" = 0 ]; then
    say ""
    say "Your files are: photos taken, recordings, music and sounds, profiles, the faces"
    say "(the photos in the faces folder) and every setting."
    ask "Delete those too?" 1 && ALL=1
  fi

  if [ "$ALL" = 1 ]; then
    remove "$MENU"
    remove "$FACES"
    say "Mods and personal files removed."
  else
    # Only the programs: BepInEx ignores everything else, which stays for a future reinstall.
    for f in "$MENU"/*.dll "$FACES"/*.dll; do
      [ -e "$f" ] && remove "$f"
    done
    say "Mods removed. Your files stay in:"
    [ -d "$MENU" ] && say "  $MENU"
    [ -d "$FACES" ] && say "  $FACES"
  fi
fi

# ---- BepInEx, only on request ----

if [ -d "$GAME/BepInEx" ]; then
  OTHERS=""
  if [ -d "$PLUGINS" ]; then
    OTHERS="$(find "$PLUGINS" -type f -name '*.dll' ! -path "$MENU/*" ! -path "$FACES/*" -printf '%P\n' 2>/dev/null | sort)"
  fi
  REMOVE_LOADER=0
  if [ "$LOADER" = 1 ]; then
    REMOVE_LOADER=1
  elif [ "$YES" = 0 ]; then
    say ""
    if [ -n "$OTHERS" ]; then
      say "BepInEx (the mod loader) stays installed. These other mods use it too:"
      printf '%s\n' "$OTHERS" | sed 's/^/  /'
      ask "Remove BepInEx too? ALL the mods listed will stop working." 1 && REMOVE_LOADER=1
    else
      ask "Remove BepInEx (the mod loader) too? The game goes back to a fresh install." 1 && REMOVE_LOADER=1
    fi
  fi

  if [ "$REMOVE_LOADER" = 1 ]; then
    if [ "$ALL" = 0 ] && { [ -d "$MENU" ] || [ -d "$FACES" ]; }; then
      # Personal files live inside BepInEx: they are moved to safety first.
      KEEP="$GAME/BigWalkModMenu-my-files"
      if [ "$DRY" = 1 ]; then
        say "[dry run] would move your files to: ${KEEP#"$GAME"/}"
      else
        mkdir -p "$KEEP" || fail "Could not create \"$KEEP\"."
        [ -d "$MENU" ] && { rm -rf -- "$KEEP/BigWalkModMenu"; mv -- "$MENU" "$KEEP/BigWalkModMenu" || fail "Could not move your files."; }
        [ -d "$FACES" ] && { rm -rf -- "$KEEP/FaceMaskMod"; mv -- "$FACES" "$KEEP/FaceMaskMod" || fail "Could not move your files."; }
        say "Your files were moved to: $KEEP"
      fi
    fi
    # Exactly the files BepInEx adds to the game folder.
    for item in BepInEx dotnet winhttp.dll doorstop_config.ini .doorstop_version changelog.txt; do
      remove "$GAME/$item"
    done
    say "BepInEx removed."
    say "You can also remove the WINEDLLOVERRIDES line from Big Walk's launch options in Steam."
  fi
fi

say ""
say "DONE."
