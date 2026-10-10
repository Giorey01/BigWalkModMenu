<p align="center">
  <a href="https://store.steampowered.com/app/1478500/">
    <img src="https://shared.steamstatic.com/store_item_assets/steam/apps/1478500/logo.png" alt="Big Walk" width="420">
  </a>
</p>

<h1 align="center">Big Walk Mod Menu</h1>

<p align="center">
  One in-game menu for <b>Big Walk</b>, one tab per mod. Install only the tabs you want.
</p>

<p align="center">
  <a href="https://github.com/Giorey01/BigWalkModMenu/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/Giorey01/BigWalkModMenu?label=release"></a>
  <img alt="Game version" src="https://img.shields.io/badge/Big%20Walk-1.6.0-2ea44f">
  <img alt="BepInEx" src="https://img.shields.io/badge/BepInEx-6%20IL2CPP%20be.785-blue">
  <img alt="Platforms" src="https://img.shields.io/badge/Windows%20%7C%20Linux%20%7C%20Steam%20Deck-supported-lightgrey">
</p>

<p align="center">
  <a href="https://github.com/Giorey01/BigWalkModMenu/releases/latest"><b>Download</b></a> ·
  <a href="#install">Install</a> ·
  <a href="#the-tabs">Tabs</a> ·
  <a href="#build-from-source">Build</a> ·
  <a href="#fork-it-and-keep-going">Fork it</a>
</p>

---

Open the menu in game with **F8** or **Insert** and drag it by its title bar. Every tab is its own DLL: the
menu shows only the ones you installed, and a tab that is switched off leaves the game's own values alone.
Everything is client-side unless noted below.

## The tabs

| Tab | What it does |
|---|---|
| **General** | The base menu, always installed: window, key bindings, menu size, sharing your look with friends who run the mod. |
| **Faces** | Put a photo on a character's head (and torso), with placement controls. Photos come from a folder you choose. |
| **Solo** | Play alone with up to four characters: add, switch, remove, gather, follow. Host only. |
| **Character** | Hats, accessories, colours, proportions, light and trail. |
| **Camera** | Selfie, third person, free camera, screenshots up to 4x, hide the interface. |
| **Performance** | FPS counter, render scale 10–200%, FOV 45–150, antialiasing, shadows, LOD, per-system quality. |
| **World** | Fixed time of day (only you see it), sky, light and fog, colour filters. |
| **Reshade** | Bloom, lens dirt and flares, filmic tone curve, exposure, sharpening, ready-made looks. No ReShade needed. |
| **Voice** | Volume of each other player's voice, from mute to 1500%, remembered between sessions. |
| **Extras** | Music and sounds from your own files, emotes, speech bubbles. |
| **Replay** | Record a scene and play it back on the real characters; export frames for a video. |

## Install

1. Download `BigWalkModMenu-x.y.z.zip` from the [latest release](https://github.com/Giorey01/BigWalkModMenu/releases/latest) and extract all of it.
2. Close the game.
3. Run the installer:
   - **Windows** — double-click `install-windows.bat`.
   - **Linux / Steam Deck** — `bash install-linux.sh`, then once in Steam set the launch option
     `WINEDLLOVERRIDES="winhttp=n,b" %command%`.

The installer finds the game, installs BepInEx 6 (be.785) if it is missing, checking its SHA-256, and asks
which tabs you want. Run it again any time to update or add tabs; it never removes anything. The first start
after installing BepInEx takes a few minutes.

Installing by hand, updating from older versions, uninstalling and troubleshooting are covered in the
[package README](dist/pacchetto/README.md); what changed is in the [changelog](dist/pacchetto/CHANGELOG.md).

### Multiplayer

- Voice, Reshade, Performance, World, Camera and Replay only change what you see and hear.
- Seeing each other's faces and looks needs the mod on every player, host included, same version.
- Solo needs you to be the host.
- Please do not report bugs to the game's developers while you are using mods.

## Build from source

You need the .NET SDK (6 or newer) and the game with BepInEx installed and started once: the projects
reference the DLLs BepInEx generates in the game folder.

```bash
bash build.sh             # builds everything, fills dist/pacchetto, makes dist/*.zip
GAME_DIR="/path/to/Big Walk" bash build.sh
```

A single project: `dotnet build -c Release` in its folder. Adding `-p:Dev=1` to Core includes the in-game
test bridge (`Core/Dev.cs`, driven by `tools/dev.sh`); never ship that build.

## Repository layout

```
BigWalkModMenu/
  Core/                  the menu: window, Ui, settings, key bindings, networking, General tab
  Faces/ Solo/ Character/ Camera/ Performance/ World/ Reshade/ Voice/ Extras/ Replay/
                         one project per tab
  Directory.Build.props  version, shared settings, references to the game
  MODLOG.md              project journal (in Italian): what was found, tested and not tested
dist/pacchetto/          what ships: installers, README, changelog (DLLs are build output)
tools/dev.sh             client of the in-game test bridge
build.sh                 build + package
.claude/skills/          the agent skills this mod was built with
CLAUDE.md                project notes for an AI agent or a new contributor
```

No photos, no game files and no decompiled game code are in this repository.

## Fork it and keep going

This mod was written with [Claude Code](https://claude.com/claude-code) working with the author, and the
repository carries what an agent needs to pick it up where it stopped:

- [`CLAUDE.md`](CLAUDE.md) — how to build, package and test, and the rules the code follows.
- [`.claude/skills`](.claude/skills) — the modding skills that were used (from
  [universal-modder](https://github.com/rehan-remade/universal-modder), MIT).
- [`BigWalkModMenu/MODLOG.md`](BigWalkModMenu/MODLOG.md) — every fact learned about the game and the state of
  each feature, including what has not been tested yet.

Open the folder in Claude Code and ask for the change you want; the skills and notes load on their own.

## Credits

- [BepInEx](https://github.com/BepInEx/BepInEx), HarmonyX and Il2CppInterop load and patch the game.
- NVorbis, NLayer, StbImageSharp, StbImageWriteSharp: see [`THIRD-PARTY-NOTICES.txt`](dist/pacchetto/THIRD-PARTY-NOTICES.txt).
- Ideas and feature lists came from these mods, whose features were rebuilt inside the menu: **Big Voice** and
  **Big Visuals** by glarmer, **Big Solo Walk / Big Walk Solo Player**, and the **Cinematic Reshade** preset.

Big Walk and its logo belong to [House House](https://househou.se/). This is an unofficial fan project, not
affiliated with or endorsed by them; the logo above is loaded from the game's Steam page and is not part of
this repository.
