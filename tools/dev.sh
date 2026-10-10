#!/usr/bin/env bash
# Client of the in-game test bridge (BigWalkModMenu/Core/Dev.cs, builds made with -p:Dev=1).
# Each argument is one command; the answers are printed when all of them have run.
#
#   bash tools/dev.sh "open faces" "wait 30" "shot faces_tab"
#
# The bridge only works while the folder BepInEx/plugins/BigWalkModMenu/dev exists in the game folder.
# Game somewhere else:  GAME_DIR="/path/to/Big Walk" bash tools/dev.sh ...

GAME_DIR="${GAME_DIR:-$HOME/.local/share/Steam/steamapps/common/Big Walk}"
P="$GAME_DIR/BepInEx/plugins/BigWalkModMenu/dev"
[ -d "$P" ] || { echo "ERROR: $P does not exist. Create it and start the game with a Dev build." >&2; exit 1; }
rm -f "$P/out.txt"
printf '%s\n' "$@" > "$P/in.tmp" && mv "$P/in.tmp" "$P/in.txt"
n=$#
for i in $(seq 1 160); do
  sleep 0.5
  if [ -f "$P/out.txt" ] && [ "$(grep -c '^> ' "$P/out.txt")" -ge "$n" ]; then break; fi
done
cat "$P/out.txt" 2>/dev/null
