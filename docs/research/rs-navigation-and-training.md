# Research: map data, pathfinding and training in Ruby and Sapphire (1.2)

Date: 2026-11-15. Spike in two halves. The map half is offline, from a checkout of `pret/pokeruby` (commit `5784633`). The emulator half ran on a second EmuHawk from the QA install (`C:\BizHawk-2.11`, sound off) on its own ports (20500/20501) with its own ROM copy and save (`SA-Path`, Sapphire 1.2, the `qa-route101` save), so the QA emulator the orchestrator uses was not touched; the copy and its save were deleted afterwards. Spec: [rs-navigation-and-training](../specs/rs-navigation-and-training.md). Builds on [rs-wild-encounter](rs-wild-encounter.md).

Checked on 2026-11-16 against `main` with the wild-encounter feature merged (`d29053c`): the addresses and macros here agree with `RsPointerService` and `Macro/Configs/RS` as built. The field-side function addresses in this doc are for 1.2 only; the profile also serves 1.1, where they were not read (spec FR-12).

## What was proved

1. **Hoenn's maps can be generated from the decomp in the HGSS scheme.** 370 of 394 maps (the 24 not built are Secret Base interiors), 249,712 tiles, one kind byte per tile as HGSS has, plus one elevation byte per tile. Both planes for every map are 504 KB raw and **53 KB gzipped** in the `maps.bin` layout HGSS uses.
2. **The data matches the game.** A search over it produced a route and one `M` macro per straight run, and the emulator walked it at the first try: Route 101 grass to the Oldale Pokémon Center counter (5 segments, across a map connection and through a door), back out over the exit mat, and later all of Route 102 into the Petalburg Pokémon Center.
3. **Healing works by rule.** Talk to the nurse, A at each text wait, A at Yes, A at the last line: HP 5 to 25 and 15 to 25, about 10.6 s, the field free at the end.
4. **Script flags and variables are in the data and in memory, and they are right.**
   - A trainer's line of sight was in the map and the search reported crossing it; the trainer stopped the player on exactly that tile. After the battle the trainer's flag (`0x500 + trainer id`, here 1598) read set in RAM, the same tiles were free, and the route got cheaper (224 to 128).
   - A script trigger tile on the route (Oldale, (9, 19)) is gated on a variable; the variable did not match and nothing fired, as predicted.
5. **A wild battle in the middle of a walk is survivable**: the segment ends, the battle is fled by the wild-encounter rules, and a new route is planned from where the player stands. Four times in these walks.
6. **Training's screens are all mapped and driven from memory**, on the first Pokémon from Lv 7 to Lv 16: fighting with a move, a level-up box (five of them), a new move with a free slot, the "Delete a move?" prompt with the forget-a-move screen, and evolution with its own move prompt.
7. **A trainer battle runs on the same press rules** (15 presses to win one), though the bot will not fight them: the owner's rule is Waiting, a Discord ping, and Resume.
8. **The in-battle switch works** and is confirmed by the battler's party index.

## The HGSS format, as read

For reference, since the Hoenn data follows it. `Scripts/Maps/MapProcessor.cs` writes and `Pathfinding/MapService.cs` reads `maps.bin`: gzip around an int count, then per map an int id, int height, int width and one byte per tile, row by row. The byte is a terrain kind, not a weight: 0 free, 1 and 2 portals, 3 grass, 4 surf, 5/6/7 right/left/down ledge, 8 rock climb, 9 whirlpool, 0x0A/0x0B/0x0C bridge, 0x0D waterfall, 0x0E ice, 0x0F cut tree, 0x10 smash rock, 0x11 a tile inside a moving NPC's range, 0x80 blocked. A standing NPC's tile is written blocked; objects whose flag starts `FLAG_HIDE` or `FLAG_MAPTEMP` are skipped; boulders go to `boulders.json`. The weights are in `PathfindingService.GetNeighboringMapPoint`: free 1, grass 3, NPC range 10 (30 while surfing), first surf tile 10, cut 10, smash 30, a turn doubles the step, and a tile the bot got stuck on carries a learned penalty. `graph.bin` holds nodes (warps and Pokémon Centers) and edges with a capability mask and a cost; A* runs inside a map and Dijkstra across the graph. The flag-gated tiles of the [HGSS pathfinder spec](../specs/pathfinder-maps-flags-hms.md) (kind 0x12, `flagged.json`, edges with required flags) are specified and not built.

## Hoenn's source data

Everything needed is in the checkout, no ROM unpacking:

- `data/maps/map_groups.json`: the order that gives each map its id, group × 256 + number, the same two bytes the game keeps at `gSaveBlock1.location`.
- `data/maps/<Name>/map.json`: the layout, `connections` (direction, offset, neighbour), `object_events` (tile, elevation, movement type and range, trainer type and sight, script, flag), `warp_events` (tile, destination map and warp number), `coord_events` (tile, variable, value, script), `bg_events`.
- `data/layouts/layouts.json` and each layout's `map.bin`: one 16-bit cell per tile, metatile id in bits 0 to 9, collision in bits 10 and 11, elevation in bits 12 to 15.
- `data/tilesets/*/metatile_attributes.bin`: per metatile, the behaviour in the low byte (`include/constants/metatile_behaviors.h`): tall grass, water kinds, ledges (`MB_JUMP_*`), doors, arrow warps, ice, currents, and so on.
- `include/constants/flags.h`, `vars.h`, `opponents.h`: the numbers behind the names. `data/maps/<Name>/scripts.inc` links a trainer object's script to its `TRAINER_*` id.

## The generated data

`rsmaps.py` (prototype, Python) builds per map a kind plane and an elevation plane.

| Kind | Byte | From | Count |
|---|---|---|---|
| Free | 0x00 | no collision, plain behaviour | 59,013 |
| Portal | 0x01 | a warp event's tile | 1,108 |
| Grass | 0x03 | tall, long and ash grass, indoor encounter floor | 5,972 |
| Surf | 0x04 | every water behaviour, currents included | 55,450 |
| Ledges right, left, down | 0x05, 0x06, 0x07 | `MB_JUMP_EAST`, `_WEST`, `_SOUTH` | 132, 171, 647 |
| Waterfall | 0x0D | `MB_WATERFALL` | 196 |
| Ice and other sliding or special floor | 0x0E | ice, slides, cracked floor, rails, slopes | 946 |
| Cut tree, smash rock | 0x0F, 0x10 | objects by their graphic | 36, 40 |
| NPC range | 0x11 | the box a wandering or walking NPC moves in | 2,136 |
| Flagged NPC | 0x12 | an object with an event flag | 512 tiles, 545 objects, every flag resolved to its number |
| Trainer sight | 0x13 | the tiles in front of a trainer, as far as its sight, in each direction its movement type faces | 1,040 tiles; 901 of 1,045 entries resolved to a trainer flag |
| Script trigger | 0x14 | a `coord_event` | 192 tiles; 162 of 194 resolved to a variable id |
| Blocked | 0x80 | collision, standing NPCs without a flag | 122,121 |

Kinds 0x00 to 0x11 and 0x80 mean what they mean on HGSS; 0x12 is the HGSS spec's flag-gated NPC; 0x13 and 0x14 are new. Beside the planes, per map: the flagged objects with their flag ids, the sight tiles with their trainer flags, the triggers with variable id and value, the warps with destination and whether they are arrow warps, and the connections.

Three things differ from HGSS and shape the design:

- **Maps are separate, joined by connections.** HGSS stitches its outdoor world into a few big maps with one coordinate space. Hoenn's routes and towns each keep their own coordinates, and the game changes `gSaveBlock1.location` and `pos` when the player steps over the edge. So a connection is an edge between maps, as a warp is. Walking Up off Route 101 at y 0 lands on Oldale's bottom row at y 19.
- **Elevation.** A tile's elevation of 0 or 15 never blocks; otherwise the player's elevation must match (the game's `IsElevationMismatchAt`), and the player takes on the tile's elevation unless it is 15. That is how bridges and cave levels work, and it is why the second plane exists. The search state carries the elevation.
- **Two kinds of warp.** A door is entered by walking onto its tile. An arrow warp (`MB_*_ARROW_WARP`, the mat inside a building) fires when the player stands on it and presses its direction. The search needs both.

## HMs in the data and the search

The owner asked for every HM to be modelled now, with the reads that say what the party can use left for later. Checked offline with the prototype, each route with and without its capability:

| HM | In the data | Route without | Route with |
|---|---|---|---|
| Cut | 36 trees, by the object's graphic | Petalburg Woods south door to (22, 10): none | found, through the trees at (19, 10) and (19, 11) |
| Rock Smash | 40 rocks, by the object's graphic | Rusturf Tunnel (20, 4) to (28, 4): none | found, through the rock at (24, 4) |
| Surf | 55,450 water tiles, by behaviour | Route 103 west bank (10, 10) to east bank (50, 12): none | found, 46 steps |
| Waterfall | 196 tiles on five maps (Ever Grande, Routes 114 and 119, Meteor Falls, Victory Road) | not routed in the spike | |
| Strength | 60 boulders on nine maps (Fiery Path, Seafloor Cavern, Victory Road, Shoal Cave, a Trick House room) | listed; the push rule is HGSS's and was not simulated | |
| Dive | the surface and underwater maps are joined by `dive` and `emerge` connections in `map.json` (eight maps have one); 3,531 deep-water tiles | not routed in the spike. The owner's call: Dive is a warp between maps, so it is an edge in the cross-map search with a capability, not a tile kind | |

Water needed one rule the land does not: water tiles sit at elevation 1 beside land at 3, and the game lets a surfer on and off regardless, so the elevation check is skipped when either tile is water.

None was used on the emulator: the save has no HMs or badges.

## Search and macros

The prototype searches tile by tile across maps (Dijkstra over map, x, y, elevation), with HGSS's costs: free 1, grass 3, NPC range 10, a turn doubles; a live trainer sight tile 40, a live trigger 200, a door or arrow warp 20. Flags make a flagged NPC's tile free when the flag is set (the game hides the object) and a sight tile free when the trainer's flag is set; a trigger is live only when its variable holds its value. The routes here (20 to 110 steps over up to four maps) were found at once.

Segments: one `M` macro per straight run, `M.<dir>.<coordinate>.<address>:H.B.1`, with the addresses of the wild-encounter research (`0x02025732` for x, `0x02025734` for y) and B held. Observed on the emulator:

- A run can cross a connection or go through a door inside one macro, because the coordinate it waits for is the destination map's. `M.Up.17` from Route 101 row 0 ends on Oldale row 17; `M.Up.4` from outside the Pokémon Center door ends at the counter, held through the fade.
- An arrow warp is `M.Down.<y outside>`: the player presses Down on the mat and the coordinate is only reached after the warp.
- After each segment the bot waits for the macro to end, the field free, and the map and tile it expected. A segment that does not get there in 20 s is "stuck", and a battle bit during a segment ends it.

| Walk | Segments | Result |
|---|---|---|
| Route 101 (14, 4) to Oldale Pokémon Center (7, 4) | 5 | arrived; 33, 41, 26, 48, 181 frames |
| Back to Route 101 (14, 4) | 6, then 1 | a wild battle on the last run; fled; re-planned; arrived |
| Route 101 to Petalburg Pokémon Center | 15 | stopped on Route 102 (33, 16) by the trainer the route said it crossed |
| Route 102 (33, 16) to Petalburg Pokémon Center, the trainer beaten | 9, re-planned three times | arrived in 59 s through three wild battles, no trainer |

## Flags, variables and trainers in memory

| What | Address | Seen |
|---|---|---|
| Event flags | `gSaveBlock1 + 0x1220` = `0x02026954`, 0x120 bytes, bit `id & 7` of byte `id >> 3` | 171 set on the save; 172 after the trainer |
| Trainer flag | flag `0x500 + trainer id` | Calvin (318): 1598, clear before the battle, set after |
| Variables | `gSaveBlock1 + 0x1340` = `0x02026A74`, 16 bits each, index `id − 0x4000` | `VAR_OLDALE_STATE` did not hold the trigger's value |
| Battle type | `gBattleTypeFlags` `0x020239F8` | `0x000C` in the trainer battle (wild `0x0004` plus trainer `0x0008`) |

The one live sight tile the bot crossed could not be avoided: the trainer's three tiles span the only corridor. The owner's rules (2026-11-15): a trainer battle puts the bot into Waiting with a Discord ping, and Resume carries on once the operator has won; and lines of sight stay out of the map and the route altogether. The prototype's sight kind (0x13) and its cost are therefore not carried into the spec. A trainer matters to a route only as an NPC: one that walks a path, or a beaten one still standing on its tile.

## Healing

At (7, 4), facing the nurse at (7, 2) over the counter: A, then by rule until the field is free.

| Wait | Read | Press |
|---|---|---|
| A field message waits | the field message window's state, `0x0202E87C + 0x16`, is 8 (5 and 9 are the other waits) | A, confirmed by the state changing |
| The Yes/No menu | the field script's status byte `0x030005B0` is 1 (stopped) with the message window idle | A (Yes is under the cursor), confirmed by the status leaving 1 |
| "We hope to see you again!" | the script's native at `0x030005BC` is `WaitForAorBPress` | A, confirmed by the native changing or the lock clearing |

Six presses, 10.6 s. One A at the Yes/No was dropped and sent again: the status byte turns 1 a moment before the menu takes input. The state that takes the input is the menu's own task (`Task_HandleYesNoInput`); the spec gates on that.

**Field script functions differ by game**, unlike the battle ones: `WaitForAorBPress` is `0x080670E5` on Sapphire 1.2 and `0x080670E1` on Ruby 1.2 (and `0x080670C1` on Ruby 1.0). They sit above `0x0803FBDC`, where the ROMs diverge. Anything gated on a field-script or menu function needs one value per game.

## Training

`battle2.py`: at the action menu A on FIGHT (confirmed by the controller function becoming `HandleInputChooseMove`), A on the move, A at every battle text wait, and the rules below.

| Screen | Read | Press |
|---|---|---|
| Level-up box, first page (the gains) | `gBattleStruct->atk6C_statetracker`, `0x0201609C`, is 1 | A, confirmed by 2 |
| Level-up box, second page (the totals) | the same byte is 2 | A, confirmed by 3 |
| A new move with a free slot | nothing new: one more text wait | A |
| "Delete a move to make room for …?" in battle | `atk5A_StateTracker`, `0x0201601A`, is 1; the cursor is `gBattleCommunication[1]` (`0x02024D1F`): 0 Yes, 1 No | A for Yes (confirmed by the tracker leaving 1) or B for No |
| The forget-a-move screen | callback 2 is no longer the battle's (`0x0800F809`) with the tracker at 3; the cursor is `0x02018079` (0 to 3, 4 is the new move) | Down to the slot (confirmed by the cursor), A (confirmed by callback 2 being the battle's again) |
| "1, 2 and… Poof!" and the rest | text waits | A |
| Evolution | callback 2 is the evolution scene's and `Task_EvolutionScene` is in the task list; its `data[0]` is the scene's state | nothing while it animates (B would cancel it); A at its text waits |
| Evolution's move prompt | the task's `data[0]` is 21 and `data[8]` is 4; `data[9]` is 5 for "Delete a move?" and 10 for "Stop learning?" | B for No then A for Yes ran here; the Yes path opens the same forget-a-move screen |

What ran: Lv 7 to 8 and 8 to 9 (one box each); 9 to 10 learning Water Gun into the free fourth slot; 10 to 15 in one battle (five boxes), then Bide over Mud-Slap through the forget-a-move screen; 15 to 16, evolution to Marshtomp (three text presses), Mud Shot refused. Experience was set just under each level by rewriting the experience field in party RAM, which is how QA can reach these screens in one battle.

The battle-side trackers and cursors are in `gSharedMem` and the same in every ROM. `Task_EvolutionScene` (`0x0811242D` on Sapphire 1.2) and the evolution and summary callbacks are above the divergence and need a value per game.

## The switch

From the action menu: Down to POKéMON (cursor 2), A; the party screen opens (callback 2 `CB2_PartyMenuMain`). Down moves the party cursor, which is `data[0]` of the sprite named by the party task's `data[3] >> 8` (here `0x02020142`, sprite 4). A on the slot opens SHIFT / SUMMARY / CANCEL, A on SHIFT returns to the battle in under a second; 8.7 s later the action menu is back with `gBattlerPartyIndexes[0]` (`0x02024A6A`) reading 1 and the battler's species the Zigzagoon's. The submenu's readiness was not gated in the spike (a fixed wait); the party task's function changes when it opens.

## Not proved

- Any HM on the emulator, and the field script each one runs: the save has no HMs or badges. Waterfall, Strength and Dive routes even offline. Ledges, ice and the other special floors on the emulator. Currents are written as plain surf.
- The elevation rule on a real bridge or a multi-level cave.
- Long routes and the search's speed across the whole region; whether the bot wants HGSS's graph (warps and connections as nodes) or the tile-level search the prototype used.
- The 24 Secret Base layouts.
- 144 trainer sight entries and 32 triggers whose flag or variable the prototype did not resolve (scripts that reach the battle through a `call`, special variables).
- NPCs that walk a fixed path (only wander and walk ranges are marked), and objects a script moves.
- The lead fainting in training, running out of PP, the "No" answer to the in-battle move prompt, a Pokémon that evolves while it is not the lead, EXP for a switched-out lead.
- The Yes/No menu gated on its task, and the party submenu gated at all.
- Ruby. Everything here ran on Sapphire 1.2; the battle-side addresses are shared, the field-side functions are not.

## What this means for the bot

- The map generator belongs in `Scripts/Maps` beside the HGSS one, reading the pokeruby checkout, writing `Data/RS` in the same container. The search and macro code can be HGSS's with three additions: connections, the elevation plane, and the two warp kinds.
- Flags and scripts can be in from the first version: the data resolves them and one 288-byte and one 512-byte read per navigation give their state. This is the same design the HGSS flag spec describes, so it is worth building once for both.
- Training is the wild-encounter battle event grown by five rules, each a byte.
- The heal trip is the walk, the nurse rule, and the walk back; a battle on the way ends a segment and the route is planned again.
- A second address table dimension is needed: field-side function addresses per game (Ruby, Sapphire), where everything so far has been one table.

## Evidence

On the dev PC, in `ShinyBotHunter.Qa/dashboard/qa-runs/20261115-rs-nav-training-spike/` of the spike worktree (not committed): `survey.py` and `rsmaps.py` (the generator and search) with the generated `maps.bin`, `walk.py` (routes on the emulator), `heal.py`, `heal2.py`, `fieldtalk.py` (the nurse), `battle2.py` (fighting and the level-up, move and field rules), `setexp.py`, `evo2.py`, `catch1.py`, and `shots/` (the nurse's prompts, the level-up box, the move prompt, the forget-a-move screen, the evolution, the party screen). The pokeruby checkout used is at `D:\Code\_spike_pokeruby\pokeruby`.
