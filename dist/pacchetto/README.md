# Big Walk Mod Menu 1.5.0

One in-game menu for Big Walk, with one tab per mod. Every tab is its own DLL: install only the
ones you want and the menu shows only those. Everything is client-side unless noted.

Open the menu with **F8** or **Insert**. Drag it by its title bar.

## The mods

| Mod (tab) | File | What it does |
|---|---|---|
| Base menu (General) | `BigWalk.ModMenu.dll` | Required. The window, key bindings, menu size, sharing your look with friends who run the mod. |
| Faces | `BigWalk.ModMenu.Faces.dll` | Put a photo on a character's head (and torso), with placement controls. Photos are read from a folder you choose. |
| Solo | `BigWalk.ModMenu.Solo.dll` | Play alone with up to four characters: add, switch, remove, gather, follow. Host only. |
| Character | `BigWalk.ModMenu.Character.dll` | Hats, accessories, a D20 die for a head, colours, proportions, light and trail. |
| Camera | `BigWalk.ModMenu.Camera.dll` | Selfie, third person, free camera, screenshots up to 4x, hide the interface. |
| Performance | `BigWalk.ModMenu.Performance.dll` | FPS counter; render scale 10-200% (supersampling), FOV 45-150, FXAA/SMAA/TAA/none, shadow cascades and resolution, soft shadows, upscaling filter, LOD, anisotropic filtering, per-system quality. |
| World | `BigWalk.ModMenu.World.dll` | Fixed time of day (only you see it), sky, light and fog, colour filters. |
| Reshade | `BigWalk.ModMenu.Reshade.dll` | Bloom, lens dirt, lens flares, filmic tone curve, exposure/contrast/saturation, sharpening, ready-made looks. No ReShade install needed. |
| Voice | `BigWalk.ModMenu.Voice.dll` | Volume of each other player's voice, from mute to 1500%, remembered between sessions; default volume for new players. |
| Extras | `BigWalk.ModMenu.Extras.dll` (+ `NVorbis.dll`, `NLayer.dll`) | Music and sounds from your files, emotes, speech bubbles. |
| Replay | `BigWalk.ModMenu.Replay.dll` | Record a scene and play it back on the real characters; export frames for a video. |

Each tab that changes something leaves the game's own value alone while it is switched off.

## Requirements

- Big Walk on Steam, tested on game version 1.6.0 (Unity 6000.3.17f1).
- BepInEx 6 for IL2CPP games, bleeding-edge build **be.785**, win-x64. The installer downloads it
  from builds.bepinex.dev and checks its SHA-256. BepInEx 5 does not work with this game.

## Install

Quickest: close the game and paste one line in a terminal. It downloads the latest release, checks it
and runs the installer below.

- **Windows** (PowerShell): `irm https://github.com/Giorey01/BigWalkModMenu/releases/latest/download/install.ps1 | iex`
- **Linux / Steam Deck**: `bash <(curl -fsSL https://github.com/Giorey01/BigWalkModMenu/releases/latest/download/install.sh)`

Or, with this zip: extract the whole of it anywhere, close the game, then:

- **Windows**: double-click `install-windows.bat`.
- **Linux / Steam Deck**: `bash install-linux.sh`, then once in Steam set the launch option
  `WINEDLLOVERRIDES="winhttp=n,b" %command%`.

The installer finds the game, installs BepInEx if it is missing, and asks which mods you want
(Enter = all). Run it again any time: it updates what is installed, adds what you pick and never
removes anything. `--mod voice,reshade` (Linux) or `-Mod voice,reshade` (Windows) skips the question.

**By hand**: copy `BepInEx/plugins/BigWalkModMenu/BigWalk.ModMenu.dll`, `StbImageSharp.dll` and
`StbImageWriteSharp.dll` into the same place in the game folder, plus the DLL of each tab you want
(see the table). Removing a tab is deleting its DLL.

The first start after installing BepInEx takes a few minutes.

Mod names for `--mod` / `-Mod`: `faces solo character camera performance world reshade voice extras replay`.

## Updating from 1.4.0 or earlier

Just install over it. Up to 1.4.0 the Faces mod lived in its own folder, `BepInEx/plugins/FaceMaskMod`.
It is now a tab like the others: the installer removes the old `BigWalk.FaceMaskMod.dll`, and the
first time the game starts your photos and per-face settings are moved to
`BepInEx/plugins/BigWalkModMenu/faces/` (`photos`, `settings`, `friends`). If you update by hand,
delete `BepInEx/plugins/FaceMaskMod/BigWalk.FaceMaskMod.dll` yourself.

## Updating from 1.3.0 or earlier

Just install over it. Settings are kept. Data folders that had Italian names (`foto`, `musica`,
`suoni`, `adesivi`, `registrazioni`, `profili`) are renamed to `photos`, `music`, `sounds`,
`stickers`, `recordings`, `profiles` the first time they are used, with their contents.

## Uninstall

`uninstall-windows.bat` / `bash uninstall-linux.sh`. It asks whether to keep your own files
(photos, recordings, settings) and whether to remove BepInEx too.

## Multiplayer and compatibility

- Voice, Reshade, Performance, World, Camera and Replay only change what you see and hear.
- Seeing each other's faces and looks needs the mod on every player including the host, same version.
- Solo needs you to be the host. Do not install it together with the stand-alone
  "Big Walk Solo Player" mod: the tab stays off while that one is loaded.
- Do not run it together with "Big Voice" or "Big Visuals": the Voice and Performance tabs change
  the same values.
- Please do not report bugs to the game's developers while using mods.

## Troubleshooting

The log is `BepInEx/LogOutput.log` in the game folder. A tab that keeps failing is paused and shown
in red; it can be restarted from the tab itself. Settings are in
`BepInEx/plugins/BigWalkModMenu/config/`.

## Credits

- BepInEx, HarmonyX and Il2CppInterop load and patch the game.
- NVorbis, NLayer, StbImageSharp, StbImageWriteSharp: see `THIRD-PARTY-NOTICES.txt`.
- Ideas and feature lists came from these mods, whose features were rebuilt inside the menu:
  **Big Voice** and **Big Visuals** by glarmer, **Big Solo Walk / Big Walk Solo Player**, and the
  **Cinematic Reshade** preset.
- Written with Claude Code (Anthropic's Claude) working with the author; tested in the game.
