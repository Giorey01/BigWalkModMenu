#!/usr/bin/env bash
# Big Walk Mod Menu - disinstallazione su Linux / Steam Deck.
#
#   bash disinstalla-linux.sh                 cerca Big Walk nelle librerie di Steam
#   bash disinstalla-linux.sh "/percorso/Big Walk"
#   opzioni:  --si        risponde si' alle domande (toglie le mod, TIENE i tuoi file)
#             --tutto     toglie anche i tuoi file: foto, facce, registrazioni, impostazioni
#             --bepinex   toglie anche BepInEx (e quindi ogni altra mod installata)
#             --prova     mostra cosa farebbe, senza toccare niente

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
    --si) YES=1 ;;
    --prova) DRY=1 ;;
    --tutto) ALL=1 ;;
    --bepinex) LOADER=1 ;;
    -h|--help) sed -n '2,10p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) GAME="$arg" ;;
  esac
done

say()  { printf '%s\n' "$*"; }
fail() { printf '\nERRORE: %s\n' "$*" >&2; exit 1; }

ask() { # domanda, risposta usata con --si (0 = si', 1 = no)
  local answer
  if [ "$YES" = 1 ]; then return "$2"; fi
  printf '%s [s/N] ' "$1"
  read -r answer || return 1
  case "$answer" in s|S|si|SI|Si|y|Y) return 0 ;; *) return 1 ;; esac
}

# ---- trova il gioco ----

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
  GAME="$(find_game)" || fail "Non trovo $GAME_NAME nelle librerie di Steam. Passa la cartella del gioco: bash disinstalla-linux.sh \"/percorso/$GAME_NAME\""
fi
GAME="${GAME%/}"
# Si cancella solo dentro una cartella che contiene davvero l'eseguibile del gioco.
[ -f "$GAME/$GAME_EXE" ] || fail "In \"$GAME\" non c'e' \"$GAME_EXE\": non e' la cartella del gioco."
say "Gioco trovato: $GAME"

game_running() {
  local f first
  pgrep -x "$GAME_EXE" >/dev/null 2>&1 && return 0
  for f in /proc/[0-9]*/cmdline; do
    first="$(tr '\0' '\n' < "$f" 2>/dev/null | head -n 1)"
    case "$first" in *"$GAME_EXE") return 0 ;; esac
  done
  return 1
}
game_running && fail "Big Walk e' aperto. Chiudilo e rilancia questo script."

PLUGINS="$GAME/BepInEx/plugins"
MENU="$PLUGINS/BigWalkModMenu"
FACES="$PLUGINS/FaceMaskMod"

remove() { # percorso: file o cartella dentro la cartella del gioco
  case "$1" in "$GAME"/*) ;; *) fail "Rifiuto di cancellare fuori dal gioco: $1" ;; esac
  [ -e "$1" ] || return 0
  if [ "$DRY" = 1 ]; then say "[prova] cancellerei: ${1#"$GAME"/}"; else rm -rf -- "$1"; fi
}

if [ ! -d "$MENU" ] && [ ! -d "$FACES" ]; then
  say "Big Walk Mod Menu non risulta installato."
else
  say ""
  say "Tolgo Big Walk Mod Menu e la mod delle facce."
  ask "Procedo?" 0 || fail "Disinstallazione annullata."

  if [ "$ALL" = 0 ] && [ "$YES" = 0 ]; then
    say ""
    say "I tuoi file sono: foto scattate, registrazioni, musica e suoni, profili, le facce"
    say "(le foto nella cartella faces) e tutte le regolazioni."
    ask "Cancello anche quelli?" 1 && ALL=1
  fi

  if [ "$ALL" = 1 ]; then
    remove "$MENU"
    remove "$FACES"
    say "Mod e file personali tolti."
  else
    # Solo i programmi: BepInEx ignora tutto il resto, che resta per una futura reinstallazione.
    for f in "$MENU"/*.dll "$FACES"/*.dll; do
      [ -e "$f" ] && remove "$f"
    done
    say "Mod tolte. I tuoi file restano in:"
    [ -d "$MENU" ] && say "  $MENU"
    [ -d "$FACES" ] && say "  $FACES"
  fi
fi

# ---- BepInEx, solo su richiesta ----

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
      say "BepInEx (il caricatore di mod) resta installato. Lo usano anche queste altre mod:"
      printf '%s\n' "$OTHERS" | sed 's/^/  /'
      ask "Tolgo anche BepInEx? Spariranno TUTTE le mod elencate." 1 && REMOVE_LOADER=1
    else
      ask "Tolgo anche BepInEx (il caricatore di mod)? Il gioco torna come appena installato." 1 && REMOVE_LOADER=1
    fi
  fi

  if [ "$REMOVE_LOADER" = 1 ]; then
    if [ "$ALL" = 0 ] && { [ -d "$MENU" ] || [ -d "$FACES" ]; }; then
      # I file personali stanno dentro BepInEx: vanno messi in salvo prima.
      KEEP="$GAME/BigWalkModMenu-i-miei-file"
      if [ "$DRY" = 1 ]; then
        say "[prova] sposterei i tuoi file in: ${KEEP#"$GAME"/}"
      else
        mkdir -p "$KEEP" || fail "Non riesco a creare \"$KEEP\"."
        [ -d "$MENU" ] && { rm -rf -- "$KEEP/BigWalkModMenu"; mv -- "$MENU" "$KEEP/BigWalkModMenu" || fail "Non riesco a spostare i tuoi file."; }
        [ -d "$FACES" ] && { rm -rf -- "$KEEP/FaceMaskMod"; mv -- "$FACES" "$KEEP/FaceMaskMod" || fail "Non riesco a spostare i tuoi file."; }
        say "I tuoi file sono stati spostati in: $KEEP"
      fi
    fi
    # Esattamente i file che BepInEx aggiunge alla cartella del gioco.
    for item in BepInEx dotnet winhttp.dll doorstop_config.ini .doorstop_version changelog.txt; do
      remove "$GAME/$item"
    done
    say "BepInEx tolto."
    say "Puoi anche togliere la riga WINEDLLOVERRIDES dalle opzioni di avvio di Big Walk in Steam."
  fi
fi

say ""
say "FATTO."
