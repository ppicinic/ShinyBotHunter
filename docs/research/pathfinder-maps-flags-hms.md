# Pathfinder spike: rebuilding the map data, flag-gated routes, and HM use (HGSS)

Status: spike done, 2026-11-03 (map data and flags, Rock Smash, Cut, Surf), extended the same day to Strength, Waterfall, Whirlpool and Rock Climb. Nothing in this doc is implemented in the bot; the design that implements it is the [pathfinder map data, flag routes and HM spec](../specs/pathfinder-maps-flags-hms.md) (plan phase 11).
Related: [pointer discovery](pointer-discovery.md) (the field script chain this reads), [headbutt](headbutt.md) (the same A-at-an-object script shape), [HGSS state reader spec](../specs/hgss-state-reader.md), the guide page [Wild encounters](../guide/04-hunts/01-wild-encounter.md)

## What was proved

| Question | Result |
|---|---|
| Can `--generatemapdatahgss` rebuild `Data/HGSS` from what is on the machine now? | Yes, from the `pokeheartgold` checkout alone (`C:\Code\pokeheartgold`): the land data NARC, the matrix files and the header table are all in it. `maps.bin` came back byte for byte equal to the committed file (with the old name matching), in 10 s. The graph step needed two fixes before it ran (below) and then rebuilt in 30 s. |
| Is the old build right? | Almost. Three overworld maps carry objects from the wrong event file because the old build matched event files by substring. The exact match corrects maps 9, 14 and 21. One map in the committed `maps.bin` (id 472) has no header and no node; the rebuild drops it. Two tile rules are wrong as well (section 1): every rock-climb wall is written as blocked, so the graph has no Rock Climb edge, and the brink above a waterfall is written as free land although the game does not let the player onto it. |
| Can routes depend on script flags? | The game's rule is one line: an object with a non-zero event flag is created only while that flag is clear. 820 such objects stand on walkable tiles across 209 bot maps, at most 18 distinct flags on one map. The flag array is at a fixed offset in the save data in RAM and matches PKHeX's reading of the save file bit for bit. |
| Can the bot use HMs with nothing but memory reads and macros? | All seven field moves, yes: a held A facing the obstacle starts the standard field script, the Yes/No menu and the text are the natives the Primo and headbutt work already gate on, and the game then removes the object (Rock Smash, Cut), mounts the player (Surf), lets the boulder be pushed (Strength) or carries the player across itself in a field task (Waterfall up, Rock Climb either way, Whirlpool through). One driver worked for all seven: Yes at `MENU_CHOICE`, A at a text that has sat for a second, done at field free. Each use is 7 to 12 s from the first press. Fly was not exercised. |
| Which Pokémon and badge the game wants | From the game's own scripts: a party member knowing the move (`GetPartySlotWithMove`, 6 means none) and one badge per move (`CheckBadge`): Cut needs Hive, Rock Smash Zephyr, Surf Fog, Strength Plain, Waterfall Rising, Whirlpool Glacier, Rock Climb Earth. The badge bits are two bytes in the save data. |

Every run was on a throwaway copy of the owner's HeartGold save, never saved in-game; the owner's `Pokemon HeartGold.SaveRAM` and the copy were compared with their `.bak` files afterwards and had not changed.

## 1. Rebuilding the map data from the decomp checkout

### The inputs, and where they are now

The committed map data was built from SDSME extracts on a `D:` drive that no longer exists, and from a header CSV produced by the `MapHeaderProcessor` that #62 deleted. All three inputs exist in the `pokeheartgold` checkout:

| `--generatemapdatahgss` expects | In the checkout | Notes |
|---|---|---|
| `<romDataDir>/a/0/6/map/NNNN`, one land-data file per matrix cell | `files/a/0/6/5`, a NARC with 676 members | Member index = the file id the matrix cells name. The spike's `layout.py` unpacked it (BTAF offsets into GMIF) and wrote the members as `0000` to `0675`. |
| `<romDataDir>/a/0/4/matrix/NNNN` | `files/fielddata/mapmatrix/map_matrix/map_matrix_NNNN_<name>.bin`, 288 files | Same bytes; the number in the name is the id. |
| `<headerCsv>`: one line per header, `<name> _ _ <matrixId>`, line index = header id | `src/data/map_headers.h`: 540 `[MAP_X] = { .matrixId = NARC_map_matrix_map_matrix_NNNN_..., .eventsBank = NARC_zone_event_NNN_NAME_bin, ... }` entries, ids from `include/constants/maps.h` | The spike wrote `headers.txt` from it with the **events bank name** (`153_T22PC0101`) as the name. |
| `<decompDir>/files/fielddata/eventdata/zone_event/*.json` and `include/constants/maps.h` | as before | 492 event files; `maps.h` still has the 540 `MAP_` defines on lines 4 to 543, which `GetWarpNames` reads by line number. |

### What the rebuild produced

`maps.bin`: 464 maps, 18,638,848 tiles. With the old-style names (`T22PC0101`, substring match) it is identical to the committed file except that the committed one has a 465th map, id 472, which no header maps to and no node uses: a leftover of an earlier run, harmless and dropped. With the exact events-bank names, three headers pick up their own event file instead of a neighbour's:

| Header | Old substring match | Exact file | Bot map changed | Tiles changed |
|---|---|---|---|---|
| 9 (`R01`, Route 1) | `005_D18R0101.json` | `006_R01.json` | 14 (Pallet Town zone) | 8 |
| 19 (`R11`, Route 11) | `004_D17R1101.json` | `016_R11.json` | 21 (Lavender / Route 11 zone) | 251 |
| 174 (`D47`) | `167_D47R0101.json` | `168_D47.json` | 9 (end-game zone) | 21 |

The changes are NPC blocks and walking ranges (0x80 and 0x11) and 177 grass tiles under a walking range. `boulders.json` is unchanged; `mapreverselookup.json` keeps every `BotMapId` and only renames `InternalName` to the events-bank name (which still ends in `PC0101` for a Pokémon Center).

`graph.bin`: 1,076 nodes (1,082 before) and 4,975 distinct edges (4,914 before). The differences are on the three corrected maps and the orphan: 5 nodes and 82 edges new, 6 nodes and 21 edges gone, among them two zero-cost self edges and a node on map 21 at (9, 9) that the corrected objects no longer isolate.

The capability masks on the committed graph's edges, for scale: 3,835 edges need nothing, 350 Cut, 593 Surf, 188 Surf and Whirlpool, 81 Cut and Surf, 18 Strength, the rest under ten each.

### Two things that stop the graph step today

- `GraphProcessor.CreateBaseGraph` calls `mapService.GetMapLookups(key)` and `GetMapLookups(w.header)` with bot map ids, but since #122 that overload takes a **game** header id unless `internalId: true` is passed. The first bot map id, 0, has no header and the run dies with `KeyNotFoundException: '0'`. The spike's scratch worktree added the `true` on both calls and nothing else.
- `SetupCommands` builds a `GraphService(HgssProfile.DataDirectory)` before the graph exists, so a clean output folder throws `FileNotFoundException: graph.bin`. The processor never reads the graph; the `PathfindingService` it needs only uses `ASearch`. The spike seeded the folder with the committed `graph.bin`.

Also: `HgssProfile.DataDirectory` is `.\data\HGSS` relative to the working directory, so the graph step reads whatever `maps.bin` is **there**, not the one the map step just wrote to `<outputDir>`. The spike ran the command from a folder whose `data\HGSS` held the fresh `maps.bin`.

Timings on this machine: move permissions, matrices and `maps.bin` 10 s; the graph (408 maps with nodes, longest map 9 s) 30 s.

### Two tile rules the processor gets wrong

Each move-permission cell is two bytes: a behaviour (the decomp's `TILE_BEHAVIOR_*`, read by `GetMetatileBehavior`) and a permission byte whose bit 7 is "blocked". `MapProcessor.GenerateMapFile` tests the permission byte's 0x80 before it tests the rock-climb behaviours, and the two never co-occur the other way round:

| Raw (behaviour / permission) | Where | What the game does | What `maps.bin` says |
|---|---|---|---|
| 0x4B / 0x80 and 0x4C / 0x80 (`TILE_BEHAVIOR_75`, `_76`: a climbable wall faced up-down, left-right) | 167 cells in 29 permission files, 20 bot maps (Mt. Mortar, Mt. Silver, Cherrygrove, Route 45, Kanto) | `MetatileBehavior_IsRockClimbInDirection` offers Rock Climb; the field task carries the player across the whole run of wall tiles | 0x80, blocked. The `0x4B`/`0x4C` branch that writes 0x08 is dead code, so the graph has no Rock Climb edge at all and `CapabilityType.RockClimb` has never mattered. |
| 0x08 / 0x0A (`TILE_BEHAVIOR_8`) | 12,805 cells in 103 files; in Tohjo Falls the whole row above the two waterfalls except the five cells atop each fall | the player cannot move onto it from the water (the brink of the cliff) | 0x00, free. The walker planned straight along that row and stalled on the first such cell. |
| 0x22 / 0x0A (`TILE_BEHAVIOR_34`) | the five cells atop each waterfall | the water the player lands on after Waterfall; surfable sideways | 0x00, free; passable in practice because the player is surfing |

Permission bytes other than 0x00 and 0x80 are common (0x04 and 0x06 on over 57,000 cells, 0x0A on 13,000); the processor reads only bit 7, and nothing in this spike contradicted that except the 0x08 / 0x0A pair. The fix the spec asks for is the smallest: test the rock-climb behaviours before the blocked bit, and write 0x08 / 0x0A as blocked.

The walker also stalled twice on things the committed map calls free that are flag-gated objects: an item ball on Route 42 at (472, 176) (`FLAG_` 1172) and a standing NPC on Route 27 at (735, 402). Both are what section 2 and FR-4 are for.

## 2. Objects that come and go with script flags

### The game's rule

`src/map_object.c:560` (`MapObject_CreateFromInitArgs`): for every object event of the map being loaded, the object is created unless its script id is set **and** its event flag is set. So an object with `eventFlag` 0 (`FLAG_NOTHING`) is always there, and any other flag hides the object exactly while the flag is set. There is no "shown while set" case. `FLAG_MAPTEMP_*` flags are the per-map ones the cut tree, the smashed rock and the pushed boulder set (`HidePerson` at the end of the script) and the game clears on the next map load.

The current `MapProcessor` applies a different rule: `FLAG_HIDE_*` and `FLAG_MAPTEMP_*` objects are left out of the map (as if always absent), every other object with a flag is a permanent wall. Both halves are wrong for routing: a hidden rival stands in a doorway until the flag is set, and a `FLAG_UNK_*` guard (205 of them) leaves when his flag is set.

### How many

Across the 492 event files: 2,667 objects; 1,559 `FLAG_NOTHING`, 737 `FLAG_HIDE_*`, 205 `FLAG_UNK_*`, 160 `FLAG_MAPTEMP_*`, 4 `FLAG_OPENED_*`, 2 `FLAG_AZALEA_*`. After the bot-map build, 820 flag-gated objects (634 HIDE, 184 UNK, 2 AZALEA; trees, rocks and boulders excluded) stand on a tile with at least two walkable neighbours, on 209 of the 464 bot maps. The most distinct flags on one map is 18 (bot maps 0, New Bark and Route 29 to 31, and 16, Cerulean); 23 maps have more than 6, 10 more than 10. So edges cannot be enumerated per flag combination the way capabilities are (2^18); they have to carry the flags a path needs.

### The flag array in RAM

`SaveVarsFlags` (`include/save_vars_flags.h`) is `u16 vars[0x170]` then `u8 flags[2912 / 8]`. The spike found it by dumping 16 KB from the save data pointer (`[[0x021D4158] + 0x0C]`) and searching for the 364-byte flag array PKHeX.Core reads from the same save file (`IEventFlagArray`): one match, at **save data + 0x10D4** (so the vars start at + 0xDF4). The live array had the same 73 non-zero bytes as the file's. Flag `n` is bit `n & 7` of byte `n >> 3`, as `Save_VarsFlags_CheckFlagInArray` reads it. Route 32's photographer Cameron (`FLAG_HIDE_CAMERON`, 0x27E) was in the live object table with his flag clear, as the rule says.

### A Continue restores the saved map's objects

The first HM attempt failed in a way worth keeping. PKHeX had moved the player to Route 32 (header 36) at (470, 439), next to the rock; the live map id read 36 and the flag was clear, but the rock was not there, and neither was any Route 32 object. The live object table (`FieldSystem + 0x3C`, the `MapObjectManager`; the player's `LocalMapObject` found by its `currentX` and `currentZ` at + 0x64 and + 0x6C) held Route 33's four NPCs and the follower, at Route 33 coordinates: the save's `SAVE_MAP_OBJECTS` block, restored by Continue, not the header's event file. Walking into the Route 32 Pokémon Center and out again loaded Route 32's objects (16 active, the rock and the tree among them, at exactly the coordinates `objects.json` gives). So a QA save whose player was moved by PKHeX must start inside a building and take one door, and the first map after a Continue may hold stale objects until the first warp.

## 3. HM use from memory gates

### What the game checks

`src/field_move.c` (the party-menu route) and the standard field scripts `scr_seq_0146.s` (the A-at-the-obstacle route, which is what the bot uses) check the same two things: `GetPartySlotWithMove` (6 = nobody) and `CheckBadge`:

| Move (id) | Script | Badge | Johto byte bit |
|---|---|---|---|
| Cut (15) | `std_field_cut` 10000 | Hive | 1 |
| Rock Smash (249) | `std_field_rock_smash` 10001 | Zephyr | 0 |
| Strength (70) | `std_field_strength` 10002 | Plain | 2 |
| Rock Climb (431) | `std_field_rock_climb` 10003 | Earth | Kanto byte bit 7 |
| Surf (57) | `std_field_surf` 10004 | Fog | 3 |
| Waterfall (127) | `std_field_waterfall` 10005 | Rising | 7 |
| Whirlpool (250) | `std_field_whirlpool` 10016 | Glacier | 6 |

`PlayerProfile` (`include/player_data.h`) sits at save data + 0x74: the trainer id at + 0x84 (already a pointer member), `johtoBadges` at **+ 0x8E** and `kantoBadges` at **+ 0x93**, bit `BADGE_x` for Johto 0 to 7 and `BADGE_x - 8` for Kanto. Read live: 0x0F after PKHeX set badges 0 to 3, and the cut script's `CheckBadge BADGE_HIVE` put 1 in `VAR_SPECIAL_RESULT`. (Surf's script does not check the badge; `FieldMove_CheckSurf` does for the menu route, and the A-at-water route is in code the decomp has not named yet; the bot should require the badge either way.)

### The press

A one-frame `T.A` facing the rock did nothing, twice. `H.A.4` (held four frames) started the script every time, four of four, as `HgssEasyChatInput` already presses. Everything after the first A is the script chain the Primo work gates on: `env.script`, the innermost context's native, `VAR_SPECIAL_*`.

### Rock Smash (Route 32, rock at (471, 439), player at (470, 439) facing Right)

| t (s) | State | Press |
|---|---|---|
| 0.0 | field free | `H.A.4` |
| 0.4 | script 10001, `PRINTING`; vars 8004 = 3 (Furret's slot), 800C = 1 (badge ok), 800D = 15 (the rock's object id) | |
| 1.3 | `TOUCHMENU_HIDE` | |
| 1.8 | `MENU_CHOICE` (Yes/No, cursor on Yes) | `H.A.4` |
| 2.1 | `TOUCHMENU_SHOW`, then `PRINTING` ("Furret used Rock Smash!") waiting at the arrow | `H.A.4` |
| +0.5 | native 0204378D (the animation), then context 1 only | |
| +2.7 | native 020408D9 | |
| +4.2 | field free; 8005 = 1 | |

The object table went from 16 to 15 active objects (the rock gone); `H.Right.16` then moved the player to (472, 439), through the rock's tile. No wild battle this time; Rock Smash can start one, and the walk later did twice in grass.

### Cut (Route 32, tree at (462, 320), player at (462, 321) facing Up)

Same shape: 10000 at 0.5 s with 800C = 1, 800D = 2; `MENU_CHOICE` at 2.2 s; after Yes `PRINTING` at 3.5 s with 8004 = 3, 800C = 3; after that A the animation natives 0204378D (4.0 s) and 020408D9 (6.5 s); field free at 7.6 s from the first A. The tree left the table (15 to 14) and `H.Up.16` put the player on (462, 319), through the tree's tile.

### Surf (Route 32, water at (462, 360), player at (462, 359) facing Down)

10004 at 0.5 s (no vars yet), `MENU_CHOICE` at 1.8 s, after Yes `PRINTING` at 3.2 s with 8004 = 3, 800C = 3. After that A: field task 021F2119 at 0.3 s, 02205DB5 at 2.5 s, the position became (462, 360) at 3.6 s and the field was free at 3.7 s. `M.Down.362` moved on the water; `M.Up.359` came back onto land and dismounted on its own. The player avatar (`FieldSystem + 0x40`) has `state` at **+ 0x18**: 2 while surfing (`PLAYER_STATE_SURFING`), 0 on land, read on both sides of the ride.

### The one driver (second run)

The second run replaced the per-move press list with one loop over the script state, and it drove all seven moves unchanged: after the first `H.A.4`, press `H.A.4` when the innermost native is `MENU_CHOICE` (Yes is the default), press `H.A.4` when the native is `PRINTING` or `WAIT_BUTTON` and the state has not changed for 1 s, stop when the field task is gone. The 1 s rule is what Strength needs: its first text ("It's a big boulder, but a Pokémon may be able to push it aside.") waits for a button before the Yes/No menu, where Rock Smash, Cut, Surf, Waterfall, Whirlpool and Rock Climb go to the menu on their own. After Yes, every move prints "X used Y!" with 8004 = the party slot and 800C = the same slot, and that text waits for A.

Facing: a one-frame `T.<dir>` and a two-frame `H.<dir>.2` do not turn the player; `H.<dir>.4` turns in place when the way is blocked and the facing byte (`FieldSystem + 0x1DC4`) confirms it. The walker arrives facing its last leg's direction, which is usually the obstacle's; the turn was needed once (Rock Climb, arriving from below).

### Surf, again (Route 42, Mt. Mortar B1F, Route 27)

Six more uses, 7.0 to 9.1 s from the first press to field free (the earlier 3.7 s was measured from the last A). Script 10004: `PRINTING` at 0.6 s, `MENU_CHOICE` at 1.9 to 2.2 s, after Yes `PRINTING` with 8004 = 3 at 3.1 to 3.7 s, after that A the field tasks 021F2119 and 02205DB5, the position on the water 0.2 s before the field is free, avatar state 2.

### Strength (Mt. Mortar B1F, map 252, boulder at (26, 38), player at (26, 39) facing Up)

The boulder is in the live object table as sprite 84 (`SPRITE_ROCK`) with script 10002 and flag 16 (`FLAG_MAPTEMP_010`) at (26, 38), exactly `boulders.json`'s entry for bot map 205.

| t (s) | State | Press |
|---|---|---|
| 0.0 | field free | `H.A.4` |
| 0.7 | script 10002, `PRINTING` ("It's a big boulder…"), 800C = 1; stays | `H.A.4` at 2.0 |
| 2.8 | `TOUCHMENU_HIDE`, then `MENU_CHOICE` at 3.2 | `H.A.4` at 3.6 |
| 4.4 | `TOUCHMENU_SHOW`, `PRINTING` ("Furret used Strength!") with 8004 = 3, 800C = 3 | `H.A.4` at 6.1 |
| 7.0 | native 0204378D (the animation) | |
| 8.7 | `PRINTING` ("…boulders can be moved"), then `WAIT_BUTTON` at 9.5 | `H.A.4` at 10.7 |
| 11.3 | field free | |

Then `H.Up.48`: field task 021F1ECD for 0.5 s, the player on (26, 38) at 0.9 s and the boulder's object at (26, 37). The push is a normal move leg that takes about twice as long as a step; the pathfinder's boulder model (walk into it, it moves one tile) holds. A wild battle started on the tile the player stepped onto; after it the boulder was still at (26, 37), so the activation and the push survive a battle (the `FLAG_MAPTEMP` is per map load).

### Waterfall (Mt. Mortar 1F entrance, map 119, fall at (38..51, 22), player surfing at (45, 23) facing Up; Tohjo Falls, map 126, at (27, 14))

Script 10005: `PRINTING` 0.6 s, `MENU_CHOICE` 1.9 s, after Yes `PRINTING` with 8004 = 4 (Pidgeot) at 3.7 s, after that A the field task 021F2945 carries the player up: (45, 22) at 8.9 s, (45, 21) at 10.0 s, field free at 10.4 s, still surfing, on the water above the fall. Both uses took 10.4 s. Going down needs no move: `H.Down.60` from above ran the same task 021F2945 and the player was back below the fall in 1.7 s. So the graph's Waterfall mask is for the upward edge only; the downward edge needs Surf.

### Rock Climb (Mt. Mortar 1F entrance, map 119, wall at (74..78, 40), player at (73, 40) facing Right, then back from (79, 40) facing Left)

Script 10003: `PRINTING` 0.6 s, a frame of context mode 1, `TOUCHMENU_HIDE`, `MENU_CHOICE` 2.1 s, after Yes `PRINTING` with 8004 = 4 at 3.7 s, after that A the field task 021F2629 (with 02205DB5 in the middle, the same pair as Surf's mount) carries the player across every wall tile: 9.1 s to field free on (79, 40), 8.5 s back to (73, 40). The wall is five tiles wide; the position passes through (74, 75, 77, 78) on the way, so a position gate should wait for the far side, not count tiles. Avatar state stays 0.

### Whirlpool (Route 27, map 31, whirlpool at (804..806, 409..411), player surfing at (805, 408) facing Down)

Script 10016: `PRINTING` 0.7 s, `MENU_CHOICE` 2.0 s, after Yes `PRINTING` with 8004 = 4 at 3.6 s, after that A the field task 021F2DD1 carries the player through the three whirlpool tiles, one per 1.1 s: (805, 412) and field free at 11.5 s, still surfing on the water beyond. The whirlpool is not cleared; the move crosses it. A later `H.Down` moved on to (805, 413) normally.

### Not exercised

Fly, which is a party-menu action and a route shortcut rather than an obstacle. Everything else in `CapabilityType` has now been driven from a script.

### Things met on the way

- **Doors and cave mouths are left by stepping off the warp tile in the exit direction.** The walker's arrival on the gatehouse tile (10, 7), the Mt. Mortar doorway (45, 50), the B1F stairs (32, 71) and the Tohjo east exit (45, 20) warped nothing; one held step out (`H.Down.24` and so on) did. Entering from outside, arriving on the door tile was enough. The navigator's warp handling must do the same (to confirm in P3).
- **A story cutscene on Route 42.** With the first four badges and the Burned Tower done, stepping on (434, 175) fires the Suicune and Eusine scene (a coordinate event, script 1, text "Oh, boy!"); eleven A presses through it, then the NPC at (435, 175) leaves. The `hgss.qa-hm-mortar` save does not have its flag set; QA either presses through it or the save gets the flag (which one is not known yet).
- **Tohjo Falls is crossed through its upper pool,** not along the top row: up the west fall, north into the pool (rows 3 to 10), east, south to the top of the east fall, down. The door at (27, 11) is the hidden room (bot map 236, one warp, a dead end), not a passage.
- **Wild battles on water.** Surfing in Mt. Mortar B1F and on Route 27 started seven wild battles in about 300 tiles; one Tentacool took several Run attempts. After a battle on water the player is still surfing (avatar state 2 was never reset by a battle in this run).
- **A malformed macro kills the emulator.** `M.Left.45.0x21da6f4` (a hex address where ApiHawk parses a decimal) raised `FormatException` in `CommandServer.ParseFrameCommands` on the emulator thread; BizHawk showed its exception dialog and exited on OK. The bot never sends that form; noted for the ApiHawk backlog, not for this spec.
- **PKHeX's `M`, `X`, `Y` are the header id and the global matrix coordinates**, the same numbers the bot reads at `X_ADDR` and `Y_ADDR`: `pos 31 755 396` put the player on Route 27 at (755, 396). An outdoor placement works when no object matters (the whirlpool); the stale objects are the saved map's, off-screen.

### Getting around during the spike

A small BFS over the regenerated map (free, grass, walking-trainer tiles, ledges one way) ran `M.<dir>.<dest>.<addr>` legs with a 2 s stall check. Two wild battles started in Route 32's grass on the way to the tree; `S.125.175.3` (the Run button's touch, the preprocessor's `S.Run.3`) at the battle menu left them. The second run's walker (`go2.py`) plans over water too and calls the Surf driver at each land-to-water step, so a walk that needs Surf is one call; it also takes a set of tiles to avoid, for the item ball and the NPC the map calls free. The bot's navigator already races its walks against battle detection; nothing new is needed there.

## 4. What the spec takes from this

- The rebuild runs from the decomp checkout: the command takes the checkout and an output folder, unpacks the NARC itself, parses `map_headers.h` and matches event files by their bank name; `GetMapLookups(…, true)`; no seed graph. The corrected three maps and the dropped orphan go in with the rebuild.
- Objects with a non-zero event flag become a flag-gated tile with its flag id in a side table; the in-map A* takes the set flags; graph edges carry the flags they need, found by an A* that crosses as few gated tiles as it can; Dijkstra reads the 364-byte array once per navigation at save data + 0x10D4.
- The capability comes from the party's moves (already parsed) and the two badge bytes at + 0x8E and + 0x93. Each HM use is one driver: face the obstacle (`H.<dir>.4` until the facing byte agrees), `H.A.4`, the script id expected for that move, `H.A.4` at `MENU_CHOICE`, `H.A.4` at a `PRINTING` or `WAIT_BUTTON` that has stood for a second, then field free; then the live position is re-read, because Waterfall, Rock Climb and Whirlpool move the player and Surf mounts it (avatar state 2). The timed `W.100;T.A;W.70` strings in `CreateMacro` go.
- Two tile rules in `MapProcessor`: rock-climb behaviours before the blocked bit, and 0x08 / 0x0A as blocked. Descending a waterfall needs no move.
- Strength's push is a move leg into the boulder; the boulder model stays. Leaving a map is a step off the warp tile in the exit direction.
- Three QA saves: `hgss.qa-hm` (Route 32 Pokémon Center: Rock Smash, Cut, Surf), `hgss.qa-hm-mortar` (the Route 42 gatehouse: Strength, Waterfall, Rock Climb in Mt. Mortar, with the Route 42 cutscene in the way) and `hgss.qa-hm-r27` (Route 27 by Tohjo Falls' east door: Whirlpool). A Continue does not load the header's objects.

## Files

Spike scripts and outputs, in the session scratchpad `C:\Users\philp\AppData\Local\Temp\claude\C--Code-ShinyBotHunter\53f46af4-c569-462c-bfe2-71138c33406e\scratchpad\pathfinder\`: `layout.py` (NARC unpack, matrix copy, `headers.txt`), `compare.py` (maps, graph and lookup diffs), `out0\`, `out1\`, `out1g\` (the old-name build, the exact-name build, and its graph), `wt\` (a detached worktree of `main` with the two-line `GraphProcessor` fix, used only to run the graph step), `sav4hm\` (PKHeX.Core console: `badges`, `teach`, `pos`, `flag`, `getflag`, `dumpflags`), `hm.py`, `objs.py`, `go.py`, `pfwalk.py` (state, the live object table, the BFS walker), `go2.py` (the walker over water, Surf at each shore), `hmact.py` (the one HM driver, `act(dir, script)`), `strength2.py`, `waterfall2.py`, `rockclimb.py`, `whirl5.py` (the four runs), `scan.py`, `scan2.py`, `plan.py` (tile, boulder, warp and reachability scans over `out1`), `saves\HG-HM.SaveRAM`, `HG-HM-Mortar.SaveRAM`, `HG-HM-R27e.SaveRAM` (the three QA saves; pristine copies in `../shinybothunterqasaves/hgss/qa-hm*.SaveRAM`, installed as `HG-HM.nds` and `HG-HM.SaveRAM`). The `.bak` BizHawk keeps next to a `.SaveRAM` is its own copy of the previous flush, not a pristine file; the pristine copies are the QA folder's.
