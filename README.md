# Big Walk Mod Menu — source

One in-game menu for Big Walk (BepInEx 6, IL2CPP) with one DLL per tab.

- `BigWalkModMenu/Core` — the menu itself (`BigWalk.ModMenu.dll`): window, `Ui`, settings, key bindings, networking, the General tab.
- `BigWalkModMenu/<Tab>` — one project per tab: Solo, Character, Camera, Performance, World, Reshade, Voice, Extras, Replay.
- `BigWalkFaceMaskMod` — the Faces tab.
- `dist/pacchetto` — what ships: installers, README, changelog. The DLLs in it are build output and are not tracked.
- `BigWalkModMenu/MODLOG.md` — project journal (in Italian): what was found in the game, what was tested and what was not.

## Build

Needs the .NET 6 SDK and the game installed with BepInEx (references point at the game folder, see `GameDir` in
`BigWalkModMenu/Directory.Build.props`).

    dotnet build -c Release            # in each project folder
    dotnet build -c Release -p:Dev=1   # adds the in-game test bridge (Core/Dev.cs); never ship this build

## Rules for this repository

- No photos, no game files and no decompiled game code are committed (see `.gitignore`).
- Every change is a commit; releases are tagged `vX.Y.Z`.
