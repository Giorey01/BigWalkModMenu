# Skills

These are the agent skills this mod was built with, so that a fork can carry on the same way. They are read
automatically by Claude Code (`.claude/skills`) and by agents that look in `.agents/skills` (a link to this folder).

They come from [universal-modder](https://github.com/rehan-remade/universal-modder) (commit `5506516`),
MIT licence: see `LICENSE-universal-modder.txt`. Update them from there.

The ones this project leans on:

| Skill | Used for |
|---|---|
| `mod-any-game` | the overall method: recon, route, first slice, verification in game |
| `game-recon` | engine, loader and anti-cheat check (Unity IL2CPP → BepInEx 6) |
| `reverse-engineering` | reading the game with Cpp2IL / the BepInEx interop DLLs |
| `game-automation` | launching the game windowed, screenshots, the in-game test bridge |
| `publish-mod` | pre-release checks, README, credits, packaging |

The others (`fal-assets`, `asset-pipeline`, `showcase-video`, `mashup-mods`, `game-research-websearch`,
`share-field-notes`) are the rest of the same set, kept so the set works as its authors intended.
