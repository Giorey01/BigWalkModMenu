# Changelog

## Unreleased
- **Character**: ten new hats: cowboy hat, chef hat, beanie, propeller cap, sombrero, fez, viking helmet, santa hat, traffic cone and mushroom. Friends on 1.5.0 or older see you without a hat when you wear one of these.
- **Character**: the **moustache** can be adjusted: size, width, height, in / out and how much its ends go down or curl up.
- **Character**: the **nose** can be moved: height and sideways around the head, and in / out.
- **World**: fix for the game stopping when a filter such as **VHS** is switched on. The TV scanlines no longer use a texture of the mod's own, a filter volume that got lost is removed instead of staying on, and every filter effect is tried on its own the first time: if the game stops right after one is switched on, the next start keeps that effect off and the World tab says which one it was (with a button to try it again).
- **Character**: new **Head → Shape** choice with a **D20 die**: a twenty-sided die with its numbers, in the head's colour. Friends with the mod see it; on versions up to 1.5.0 they see your normal head.
- **Extras**: speech bubble texts left in Italian by an older version (`Ciao!`, `Foto!`, ...) are translated to English on first start. Texts you wrote yourself are kept.

## 1.5.0
- **Faces** is now a tab like every other: `BigWalkModMenu/BigWalk.ModMenu.Faces.dll`, no separate `FaceMaskMod` folder.
- Photos, per-face settings and friends' faces move to `BigWalkModMenu/faces/` on first start; the old plugin file is removed.

## 1.4.0
- The whole interface, the installers and the documentation are in English.
- Data folders with Italian names are renamed to English ones on first use; their contents are kept.
- Installer scripts renamed: `install-*` / `uninstall-*`; options are `--yes`, `--dry-run`, `--all-mods`, `--mod`.

## 1.3.0
- New tab **Solo**: several characters for one player, rebuilt inside the menu (replaces the stand-alone Solo mod).
- **Faces**: the photo folder can be chosen from the menu and can be anywhere on disk.
- The menu window can be dragged by its title bar; its position is remembered.
- Installer: choose which mods to install; re-running it updates and adds, never removes.
- No personal photos are shipped.

## 1.2.0
- New tabs **Voice** (per-player voice volume) and **Reshade** (bloom, tone, sharpening).
- **Performance**: FOV, antialiasing mode, upscaling filter, shadow cascades/resolution, soft shadows, anisotropic filtering, wider render scale and LOD ranges.

## 1.1.0
- Sharing your look, face, emotes and speech bubbles with friends who run the mod.

## 1.0.0
- First version: General, Faces, Character, Camera, Performance, World, Extras, Replay.
