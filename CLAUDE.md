# Big Walk Mod Menu — notes for an AI agent (or a new contributor)

A BepInEx 6 (IL2CPP) mod for the game Big Walk (Steam app 1478500, Unity 6000.3.17f1): one in-game menu,
one DLL per tab.

## Where things are

- `BigWalkModMenu/Core` — the menu itself (`BigWalk.ModMenu.dll`): window, `Ui`, `ModConfig`, key bindings,
  `Net` (messages between players who run the mod), `Profiles`, the General tab, the test bridge `Dev.cs`.
- `BigWalkModMenu/<Tab>` — one project per tab: Faces, Solo, Character, Camera, Performance, World, Reshade,
  Voice, Extras, Replay. Each has a `BasePlugin` that calls `ModMenuApi.Register(new XModule())`.
- `BigWalkModMenu/Directory.Build.props` — version, shared settings and the references to the game's DLLs.
- `dist/pacchetto` — what ships: installers, README, changelog. The DLLs there are build output, not tracked.
- `BigWalkModMenu/MODLOG.md` and `BigWalkModMenu/Faces/MODLOG.md` — the project journal, **in Italian**. It
  records what was found in the game, what was tested in game and what was not. Read the latest entry
  before changing anything, and add an entry for every change.
- `.claude/skills` — the modding skills this project was built with (see its README).

## Build, package, test

    bash build.sh            # builds all projects, fills dist/pacchetto, makes dist/*.zip and dist/single/
    bash build.sh --no-zip

`GAME_DIR` points at the game folder if it is not in `~/.local/share/Steam/steamapps/common/Big Walk`;
`DOTNET` at the dotnet binary if it is not on PATH. The game must have BepInEx installed and have been
started once: the references are the interop DLLs BepInEx generates in `BepInEx/interop`.

In-game test bridge: build Core with `-p:Dev=1`, create `BepInEx/plugins/BigWalkModMenu/dev` in the game
folder, then send commands with `bash tools/dev.sh "open faces" "shot name"`. Never ship a Dev build.

Launch windowed for screenshots: `steam -applaunch 1478500 -screen-fullscreen 0 -screen-width 1600 -screen-height 900`.
The installers have `--dry-run`; test them against a fake game folder (an empty `Big Walk.exe` and
`BepInEx/core/BepInEx.Unity.IL2CPP.dll` are enough) before touching a real install.

## Rules

- No photos, no game files and no decompiled game code in the repository (see `.gitignore`). Decompile the
  game outside this folder.
- Interface, installers and README are in English; the journal stays in Italian.
- IMGUI only with `GUI.*` and fixed rectangles, no `GUILayout`. Unity objects kept between frames go through
  `Game.Alive(obj)`.
- A tab must leave the game's own values alone while it is switched off.
- Data folders: `BepInEx/plugins/BigWalkModMenu/<tab id>/`, settings in `config/<tab id>.cfg`. When a folder is
  renamed or moved, move the user's files too (`ModMenuApi.Renamed`, `Faces/Plugin.cs: MoveLegacyData`).
- Every change is a commit; releases are tagged `vX.Y.Z` and the version lives in `Directory.Build.props`
  and `ModMenuApi.Version`. Say plainly in the journal what was not tested in game.
