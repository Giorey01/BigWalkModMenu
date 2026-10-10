#!/usr/bin/env bash
# Big Walk Mod Menu - installazione su Linux / Steam Deck (il gioco gira con Proton).
#
#   bash installa-linux.sh                 cerca Big Walk nelle librerie di Steam
#   bash installa-linux.sh "/percorso/Big Walk"
#   opzioni:  --si              risponde si' a tutte le domande (e installa tutte le mod)
#             --prova           mostra cosa farebbe, senza toccare niente
#             --tutte           installa tutte le mod senza chiedere
#             --mod a,b,c       installa solo queste (nomi: facce solo personaggio telecamera
#                               prestazioni mondo reshade voce extra replay)
#
# Si puo' rilanciare quando si vuole: aggiorna quello che c'e' gia' e aggiunge quello che scegli.

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
    --si) YES=1 ;;
    --prova) DRY=1 ;;
    --tutte) ALL=1 ;;
    --mod) shift; WANTED="${1:-}" ;;
    --mod=*) WANTED="${1#--mod=}" ;;
    -h|--help) sed -n '2,13p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) GAME="$1" ;;
  esac
  shift
done

say()  { printf '%s\n' "$*"; }
fail() { printf '\nERRORE: %s\n' "$*" >&2; exit 1; }

ask() {
  [ "$YES" = 1 ] && return 0
  local answer
  printf '%s [s/N] ' "$1"
  read -r answer || return 1
  case "$answer" in s|S|si|SI|Si|y|Y) return 0 ;; *) return 1 ;; esac
}

[ -d "$HERE/BepInEx/plugins/BigWalkModMenu" ] || fail "Accanto a questo script manca la cartella BepInEx del pacchetto. Estrai tutto lo zip e riprova."

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
    # Altre librerie (altri dischi) sono elencate qui.
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
  GAME="$(find_game)" || fail "Non trovo $GAME_NAME nelle librerie di Steam. Passa la cartella del gioco: bash installa-linux.sh \"/percorso/$GAME_NAME\""
fi
GAME="${GAME%/}"
[ -f "$GAME/$GAME_EXE" ] || fail "In \"$GAME\" non c'e' \"$GAME_EXE\": non e' la cartella del gioco."
say "Gioco trovato: $GAME"

# Il gioco e' aperto se esiste un processo che si chiama come il suo eseguibile, o il cui primo
# argomento finisce con quel nome (cosi' appare sotto Proton). Non si guarda l'intera riga di comando:
# conterrebbe il nome anche per questo stesso script.
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
  fail "Big Walk e' aperto. Chiudilo e rilancia questo script."
fi

# ---- BepInEx (il caricatore di mod) ----

extract_zip() { # zip, destinazione
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
  say "BepInEx e' gia' installato."
else
  say ""
  say "Nel gioco manca BepInEx, il programma che carica le mod."
  say "Lo scarico dal sito ufficiale (circa 35 MB):"
  say "  $BEPINEX_URL"
  if [ "$DRY" = 1 ]; then
    say "[prova] scaricherei BepInEx e lo estrarrei in: $GAME"
  else
    ask "Procedo?" || fail "Senza BepInEx le mod non partono. Installazione annullata."
    command -v sha256sum >/dev/null 2>&1 || fail "Manca il comando sha256sum: non posso verificare il file scaricato."
    TMP="$(mktemp -d)" || fail "Non riesco a creare una cartella temporanea."
    trap 'rm -rf "$TMP"' EXIT
    # Un file gia' scaricato accanto allo script va bene lo stesso, se e' quello giusto.
    LOCAL="$(ls "$HERE"/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.785*.zip 2>/dev/null | head -n 1)"
    if [ -n "$LOCAL" ]; then
      cp "$LOCAL" "$TMP/bepinex.zip"
    else
      download "$BEPINEX_URL" "$TMP/bepinex.zip" || fail "Download non riuscito (servono curl o wget e la rete)."
    fi
    SUM="$(sha256sum "$TMP/bepinex.zip" | cut -d' ' -f1)"
    [ "$SUM" = "$BEPINEX_SHA256" ] || fail "Il file scaricato non e' quello atteso (impronta diversa). Non lo installo."
    extract_zip "$TMP/bepinex.zip" "$GAME" || fail "Non riesco a estrarre lo zip (servono unzip, python3 o bsdtar)."
    [ -f "$GAME/BepInEx/core/BepInEx.Unity.IL2CPP.dll" ] || fail "BepInEx non risulta installato dopo l'estrazione."
    say "BepInEx installato."
  fi
fi

# ---- le mod ----
#
# Ogni mod e' una scheda del menu e un file a se'. Il menu base ("Generale") c'e' sempre.
# id | nome | descrizione | file, separati da spazi, relativi a BepInEx/plugins

MODS="facce|Facce|la foto di una faccia sulla testa dei personaggi|FaceMaskMod/BigWalk.FaceMaskMod.dll FaceMaskMod/StbImageSharp.dll
solo|Solo|fino a quattro personaggi comandati da un solo giocatore|BigWalkModMenu/BigWalk.ModMenu.Solo.dll
personaggio|Personaggio|cappelli, accessori, colori, proporzioni|BigWalkModMenu/BigWalk.ModMenu.Character.dll
telecamera|Telecamera|selfie, terza persona, camera libera, foto|BigWalkModMenu/BigWalk.ModMenu.Camera.dll
prestazioni|Prestazioni|contatore FPS e opzioni grafiche avanzate|BigWalkModMenu/BigWalk.ModMenu.Performance.dll
mondo|Mondo|ora del giorno, cielo, nebbia, filtri di colore|BigWalkModMenu/BigWalk.ModMenu.World.dll
reshade|Reshade|bagliore, toni da film, nitidezza|BigWalkModMenu/BigWalk.ModMenu.Reshade.dll
voce|Voce|volume della voce di ogni giocatore|BigWalkModMenu/BigWalk.ModMenu.Voice.dll
extra|Extra|musica, suoni, emote, fumetti|BigWalkModMenu/BigWalk.ModMenu.Extras.dll BigWalkModMenu/NVorbis.dll BigWalkModMenu/NLayer.dll
replay|Replay|registra e rivedi le scene|BigWalkModMenu/BigWalk.ModMenu.Replay.dll"
CORE_FILES="BigWalkModMenu/BigWalk.ModMenu.dll BigWalkModMenu/StbImageSharp.dll BigWalkModMenu/StbImageWriteSharp.dll"

SRC="$HERE/BepInEx/plugins"
DST="$GAME/BepInEx/plugins"

mod_field() { printf '%s\n' "$MODS" | sed -n "${1}p" | cut -d'|' -f"$2"; }
MOD_COUNT="$(printf '%s\n' "$MODS" | wc -l)"

first_file() { printf '%s' "${1%% *}"; }
in_package() { [ -f "$SRC/$(first_file "$(mod_field "$1" 4)")" ]; }
installed()  { [ -f "$DST/$(first_file "$(mod_field "$1" 4)")" ]; }

# SELECTED: i numeri delle mod da installare o aggiornare, separati da spazi.
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
        in_package "$n" || fail "La mod \"$name\" non e' in questo pacchetto."
        SELECTED="$SELECTED $n"; found=1
      fi
      n=$((n + 1))
    done
    [ "$found" = 1 ] || fail "Non conosco la mod \"$name\". Nomi validi: $(printf '%s\n' "$MODS" | cut -d'|' -f1 | tr '\n' ' ')"
  done
elif [ "$ALL" = 1 ] || [ "$YES" = 1 ] || [ "$DRY" = 1 ]; then
  select_all
else
  say ""
  say "Quali mod vuoi? Ognuna e' una scheda del menu. Il menu base c'e' sempre."
  say ""
  n=1
  while [ "$n" -le "$MOD_COUNT" ]; do
    if in_package "$n"; then
      state=""
      installed "$n" && state="  [gia' installata]"
      printf '  %2d) %-12s %s%s\n' "$n" "$(mod_field "$n" 2)" "$(mod_field "$n" 3)" "$state"
    fi
    n=$((n + 1))
  done
  say ""
  say "  Invio      tutte"
  say "  numeri     solo quelle, separate da spazi (esempio: 1 3 8)"
  say "  m          solo quelle che ti mancano"
  say ""
  say "Quelle gia' installate vengono comunque aggiornate, e nessuna viene tolta."
  printf 'Scelta: '
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
        case "$n" in ''|*[!0-9]*) fail "\"$n\" non e' un numero dell'elenco." ;; esac
        { [ "$n" -ge 1 ] && [ "$n" -le "$MOD_COUNT" ] && in_package "$n"; } || fail "Nell'elenco non c'e' il numero $n."
        SELECTED="$SELECTED $n"
      done ;;
  esac
fi

# Le mod gia' presenti si aggiornano sempre: schede di versioni diverse non vanno d'accordo col menu.
n=1
while [ "$n" -le "$MOD_COUNT" ]; do
  if in_package "$n" && installed "$n"; then
    case " $SELECTED " in *" $n "*) ;; *) SELECTED="$SELECTED $n" ;; esac
  fi
  n=$((n + 1))
done

copy_file() { # percorso relativo a BepInEx/plugins
  [ -f "$SRC/$1" ] || fail "Nel pacchetto manca il file $1. Estrai di nuovo tutto lo zip."
  if [ "$DRY" = 1 ]; then say "[prova] copierei: $1"; return 0; fi
  mkdir -p "$(dirname "$DST/$1")" || fail "Non posso scrivere in \"$GAME\"."
  cp -f "$SRC/$1" "$DST/$1" || fail "Copia non riuscita: $1"
}

say ""
for f in $CORE_FILES; do copy_file "$f"; done
say "Menu base: ok"
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
[ -n "$NAMES" ] && say "Mod installate o aggiornate:$NAMES"
if [ "$DRY" = 0 ]; then
  [ -f "$DST/BigWalkModMenu/BigWalk.ModMenu.dll" ] || fail "La mod non risulta copiata."
  [ -f "$DST/FaceMaskMod/BigWalk.FaceMaskMod.dll" ] && mkdir -p "$DST/FaceMaskMod/faces" 2>/dev/null
fi
MISSING=""
n=1
while [ "$n" -le "$MOD_COUNT" ]; do
  if in_package "$n" && ! installed "$n"; then
    case " $SELECTED " in *" $n "*) ;; *) MISSING="$MISSING $(mod_field "$n" 2)" ;; esac
  fi
  n=$((n + 1))
done
[ -n "$MISSING" ] && say "Non installate (rilancia lo script quando le vuoi):$MISSING"

if [ -f "$DST/BigWalkSoloMod/BigWalkSoloMod.dll" ] && [ -f "$DST/BigWalkModMenu/BigWalk.ModMenu.Solo.dll" ]; then
  say ""
  say "ATTENZIONE: hai anche la mod \"Big Walk Solo Player\" originale. Fa le stesse cose della"
  say "scheda Solo: togli la cartella BepInEx/plugins/BigWalkSoloMod, altrimenti la scheda resta ferma."
fi

say ""
say "FATTO. Ancora due cose:"
say ""
say "1. Su Linux Steam deve caricare BepInEx. In Steam: tasto destro su Big Walk >"
say "   Proprieta' > Generali > Opzioni di avvio, e incolla questa riga:"
say ""
say '      WINEDLLOVERRIDES="winhttp=n,b" %command%'
say ""
say "2. Il primo avvio dopo aver installato BepInEx dura qualche minuto (prepara i file"
say "   del gioco) e sembra bloccato: lascialo finire."
say ""
say "In gioco il menu si apre con F8 oppure Insert."
if [ -f "$DST/FaceMaskMod/BigWalk.FaceMaskMod.dll" ]; then
  say "Le foto per le facce vanno in: $GAME/BepInEx/plugins/FaceMaskMod/faces"
  say "(oppure scegli un'altra cartella dalla scheda Facce)."
fi
