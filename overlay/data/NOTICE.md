# Game data tables

The lookup tables in this folder are extracted from AION 2 game data (© NCSOFT) and were taken from
open-source meters licensed under GPL-3.0, which is why AionMeter as a whole is distributed under GPL-3.0
(see `../LICENSE`).

| File | Source |
|---|---|
| `skills/*.json`, `npcs/*.json`, `dungeons/*.json`, `dot_skill_ids.json`, `servers.json`, `skill_icons.json` | [taengu/A2Tools-DPS-Meter](https://github.com/taengu/A2Tools-DPS-Meter) `src/data`, commit `e32dc5e` (2026-10-05), GPL-3.0 |
| `healing_skill_ids.json` | [Kuroukihime/AIon2-Dps-Meter](https://github.com/Kuroukihime/AIon2-Dps-Meter) `GameData/Assets`, commit `2e8d67d` (2026-10-01), GPL-3.0 |
| `src/AionMeter.App/Assets/Classes/*.png` (class emblems; brawler downscaled to 128 px) | same repository, `AionDpsMeter.UI/Assets/Classes`, commit `2e8d67d`, GPL-3.0 |
| `boss_portraits.json` (NPC code → portrait file) | built from [MetaBot.GG's boss list](https://metabot.gg/en/aion-2/bosses) (2026-10-05): each dungeon boss's portrait file, matched to every NPC code with the same English name in `npcs/en.json`. The portraits themselves are not bundled: the app downloads them from MetaBot.GG at runtime (credit: [MetaBot.GG](https://metabot.gg)) |
| `map_calibration.json` (game position → world map units) | 1 HP: fitted on a player's recording of Kibelisk flights (2026-10-30) |
| `checklist.json` (daily and weekly tasks) | 1 HP: our own list, after the site's weekly entries and patch limits (`aion2-guide/data.js`) |
| `pet_souls.json` (pet → family, soul monster, territories) | built from [Aion2Data's pet map](https://aion2data.com/pets/map) (AION 2 Global 2026.10.21, read 2026-10-27), which allows reuse with a link back; soul monsters matched to NPC codes by English name in `npcs/en.json` (credit: [Aion2Data](https://aion2data.com)); the map icons are the game's own markers, downloaded from the game's CDN (`assets.playnccdn.com`) |
| `world_maps.json` (map size and areas; field bosses, dungeon entrances, rifts, Kibelisks, monoliths, gathering, pet-soul monsters, Hidden Cube spots) | built by `tools/world_maps.js` from [Aion2Data's interactive maps](https://aion2data.com/map) (AION 2 Global 2026.10.28, read 2026-10-30); only names and points. The map pictures are not bundled: the app downloads them from aion2data.com when a map is opened (credit: [Aion2Data](https://aion2data.com)) |

Formats AionMeter reads (every file is optional):

- `skills/<lang>.json` — `{ "16040000": "Skill name", … }`
- `npcs/<lang>.json` — `{ "2000002": { "name": "…", "isBoss": false, "isDummy": false }, … }`
- `dungeons/<lang>.json` — `{ "600021": { "name": "Fire Temple" }, … }` (map id → name)
- `servers.json` — `{ "servers": { "2305": { "en": "…", "ru": "…" } } }`
- `dot_skill_ids.json`, `healing_skill_ids.json` — arrays of skill codes
- `field_boss_maps.json` — `{ "maps": { "1110": { "block": 2400, "en": "Altgard", "ru": "Альтгард" } } }`: a map's field boss
  list (opcode 01 91) holds its bosses in NPC-code order, all from one block of a thousand codes. Established on the
  2026-10-06 EU capture against the in-game list (24 of 24 slots).
- `boss_portraits.json` — `{ "portraits": { "2300471": "2300471", "2300104": "f/mob_berc_01", … } }` (file name on
  metabot.gg/web/aion2/npcs/ without `.webp`; NPCs not listed are tried under their own code)
- `pet_souls.json` — `{ "pets": [ { "id": 1026, "pet": "Krall Warrior", "family": "Cogni", "monster": "Krall Test Subject",
  "areas": [["Verteron", 52]], "npcs": [2000123, …] }, … ] }` (areas: territory and number of spawn points)

Missing languages fall back to `en`.
